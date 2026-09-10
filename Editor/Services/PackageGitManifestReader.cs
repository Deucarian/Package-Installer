using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGitManifestReader : IPackageManifestReader
    {
        private readonly IDevelopmentGitRunner runner;
        private readonly IPackageManifestScratchFactory scratch;
        private readonly Dictionary<string, PackageRegistryRemoteFetchResponse> cache = new Dictionary<string, PackageRegistryRemoteFetchResponse>(StringComparer.Ordinal);
        private readonly Queue<string> cacheOrder = new Queue<string>();
        internal PackageGitManifestReader(IDevelopmentGitRunner runner, IPackageManifestScratchFactory scratch)
        { this.runner = runner ?? throw new ArgumentNullException(nameof(runner)); this.scratch = scratch ?? throw new ArgumentNullException(nameof(scratch)); }

        public async Task<PackageRegistryRemoteFetchResponse> ReadAsync(string packageReference, string revisionOverride,
            CancellationToken cancellationToken, TimeSpan timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PackageGitReference.TryParse(packageReference, out PackageGitReference parsed) ||
                !parsed.TryGetMetadataSource(revisionOverride, out string remote, out string reference, out string manifestPath))
                throw new InvalidOperationException("Use a credential-free GitHub or Bitbucket Cloud package reference.");
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                budget.CancelAfter(timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(15));
                try
                {
                    using (IPackageManifestScratch owned = scratch.Create())
                    {
                        string repository = Path.Combine(owned.Root, "repository.git");
                        bool filtered = parsed.RepositoryIdentity.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase);
                        string fetchRef = reference;
                        string cacheRevision = reference;
                        if (!IsRevision(reference))
                        {
                            string listing = await Run(owned, owned.Root, remote, filtered, budget.Token, "ls-remote", "--exit-code", "--refs", "--", remote,
                                "refs/heads/" + reference, "refs/tags/" + reference).ConfigureAwait(false);
                            string[] records = listing.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            string[] record = records.Length == 1 ? records[0].Split('\t') : Array.Empty<string>();
                            if (record.Length != 2 || !IsRevision(record[0]) ||
                                (record[1] != "refs/heads/" + reference && record[1] != "refs/tags/" + reference))
                                throw new PackageManifestReadException("The package metadata reference is missing or ambiguous.");
                            fetchRef = record[1];
                            cacheRevision = record[0];
                        }
                        string key = remote + "\n" + cacheRevision.ToLowerInvariant() + "\n" + manifestPath;
                        lock (cache) if (cache.TryGetValue(key, out var cached)) return cached;
                        await Run(owned, owned.Root, remote, filtered, budget.Token, "init", "--bare", "--template=", repository).ConfigureAwait(false);
                        if (filtered)
                        {
                            // Git 2.23 reads this extension from the repository file, ignoring -c for format detection.
                            // Only this disposable bare repository receives settings; no URL or credential is stored.
                            await Run(owned, repository, remote, filtered, budget.Token,
                                "config", "--local", "core.repositoryformatversion", "1").ConfigureAwait(false);
                            await Run(owned, repository, remote, filtered, budget.Token,
                                "config", "--local", "extensions.partialClone", "manifest").ConfigureAwait(false);
                        }
                        // Fetch only one selected ref into this request's bare repository. No checkout, tags or submodules.
                        // Cloud may transfer all tip blobs; the scratch cancellation guard bounds that fallback.
                        var fetch = new List<string> { "fetch", "--depth=1", "--no-tags", "--no-recurse-submodules", "--no-auto-gc" };
                        if (filtered) fetch.Add("--filter=blob:none");
                        fetch.AddRange(new[] { "--", filtered ? "manifest" : remote, fetchRef + ":refs/package-manifest/candidate" });
                        await Run(owned, repository, remote, filtered, budget.Token, fetch.ToArray()).ConfigureAwait(false);
                        string fetchedObject = (await Run(owned, repository, remote, filtered, budget.Token,
                            "rev-parse", "--verify", "refs/package-manifest/candidate").ConfigureAwait(false)).Trim();
                        if (!string.Equals(fetchedObject, cacheRevision, StringComparison.OrdinalIgnoreCase))
                            throw new PackageManifestReadException("The package metadata reference changed during fetch. Retry the check.");
                        string revision = (await Run(owned, repository, remote, filtered, budget.Token,
                            "rev-parse", "--verify", "refs/package-manifest/candidate^{commit}").ConfigureAwait(false)).Trim();
                        if (!IsRevision(revision) || (IsRevision(reference) && !revision.StartsWith(reference, StringComparison.OrdinalIgnoreCase)))
                            throw new PackageManifestReadException("The package metadata revision differs from the selected commit.");
                        string objectName = revision + ":" + manifestPath;
                        string size = await Run(owned, repository, remote, filtered, budget.Token, "cat-file", "-s", objectName).ConfigureAwait(false);
                        if (!long.TryParse(size.Trim(), out long bytes) || bytes < 1 || bytes > PackageManifestReader.MaximumManifestLength)
                            throw new PackageManifestReadException("Package metadata exceeds the 256 KiB size limit.");
                        string content = await Run(owned, repository, remote, filtered, budget.Token, "cat-file", "blob", objectName).ConfigureAwait(false);
                        if (content.Length > PackageManifestReader.MaximumManifestLength ||
                            !PackageRegistryPackageNameValidator.TryReadPackageName(content, out _))
                            throw new PackageManifestReadException("The selected package manifest is invalid.");
                        var response = new PackageRegistryRemoteFetchResponse(content);
                        lock (cache)
                        {
                            if (!cache.ContainsKey(key))
                            {
                                while (cache.Count >= 32) cache.Remove(cacheOrder.Dequeue());
                                cacheOrder.Enqueue(key);
                            }
                            cache[key] = response;
                        }
                        return response;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { throw new PackageManifestReadException("Package metadata Git read timed out. Retry after checking Git access."); }
                catch (PackageManifestScratchLimitException) { throw new PackageManifestReadException("Package metadata Git read exceeded the 32 MiB scratch cancellation budget. Use your existing checkout for this package."); }
                catch (PackageManifestReadException) { throw; }
                catch (Exception) { throw new InvalidOperationException("Package metadata could not be read with existing Git authentication. Check repository access and the selected reference in your Git client."); }
            }
        }

        private static bool IsRevision(string value) => Regex.IsMatch(value ?? "", @"^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$");

        private async Task<string> Run(IPackageManifestScratch owned, string directory, string remote, bool filtered, CancellationToken token, params string[] command)
        {
            owned.AssertOwned();
            var args = new List<string> { "-c", "core.hooksPath=" + Path.Combine(owned.Root, "disabled-hooks"),
                "-c", "gc.auto=0", "-c", "maintenance.auto=false", "-c", "protocol.file.allow=never", "-c", "protocol.ext.allow=never" };
            if (filtered) args.AddRange(new[] { "-c", "remote.manifest.url=" + remote,
                "-c", "remote.manifest.promisor=true", "-c", "remote.manifest.partialclonefilter=blob:none" });
            args.AddRange(command);
            using (var processCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var process = runner.RunAsync(directory, args, processCancellation.Token);
                try
                {
                    while (!process.IsCompleted)
                    {
                        await Task.WhenAny(process, Task.Delay(50, processCancellation.Token)).ConfigureAwait(false);
                        owned.AssertOwned();
                        token.ThrowIfCancellationRequested();
                    }
                    var result = await process.ConfigureAwait(false);
                    owned.AssertOwned();
                    return result.RequireSuccess();
                }
                catch
                {
                    processCancellation.Cancel();
                    try { await process.ConfigureAwait(false); } catch (Exception) { }
                    throw;
                }
            }
        }
    }

    internal sealed class PackageManifestReadException : InvalidOperationException
    {
        internal PackageManifestReadException(string message) : base(message) { }
    }
}
