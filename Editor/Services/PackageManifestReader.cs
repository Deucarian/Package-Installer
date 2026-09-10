using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;

namespace Deucarian.PackageInstaller.Editor
{
    internal interface IPackageManifestReader
    {
        Task<PackageRegistryRemoteFetchResponse> ReadAsync(string packageReference, string revisionOverride,
            CancellationToken cancellationToken, TimeSpan timeout);
    }

    internal sealed class PackageManifestReader : IPackageManifestReader
    {
        internal const int MaximumManifestLength = 262144;
        private readonly PackageRegistryRemoteFetchDelegate http;
        private readonly IPackageManifestReader git;

        internal PackageManifestReader(PackageRegistryRemoteFetchDelegate http, IPackageManifestReader git = null)
        { this.http = http ?? throw new ArgumentNullException(nameof(http)); this.git = git; }

        // Called by loader/update composition on Unity's main thread; worker reads use captured paths only.
        internal static IPackageManifestReader CreateDefault(PackageRegistryRemoteFetchDelegate http)
        {
            var actual = http ?? PackageRegistryRemoteFetch.FetchAsync;
            if (actual != (PackageRegistryRemoteFetchDelegate)PackageRegistryRemoteFetch.FetchAsync)
                return new PackageManifestReader(actual); // Injected HTTP tests never start real Git implicitly.
            string storage = Path.Combine(Path.GetDirectoryName(PackageRegistryCache.GetDefaultCachePath()), "ManifestReads");
            return new PackageManifestReader(PackageRegistryRemoteFetch.FetchManifestAsync, new PackageGitManifestReader(
                new DevelopmentGitProcessRunner(15000, MaximumManifestLength + 4096),
                new PackageManifestScratchFactory(storage)));
        }

        public async Task<PackageRegistryRemoteFetchResponse> ReadAsync(string packageReference, string revisionOverride,
            CancellationToken cancellationToken, TimeSpan timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PackageRegistryPackageNameValidator.TryCreatePackageJsonUrl(packageReference, revisionOverride, out string url))
                throw new InvalidOperationException("Use a credential-free GitHub or Bitbucket Cloud package reference.");
            bool httpTimedOut = false;
            try
            {
                var response = await PackageRegistryRemoteFetch.ExecuteAsync(http, url, cancellationToken, timeout).ConfigureAwait(false);
                if (response != null && response.Content.Length <= MaximumManifestLength &&
                    PackageRegistryPackageNameValidator.TryReadPackageName(response.Content, out _)) return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (TimeoutException) { httpTimedOut = true; }
            catch (Exception) { /* Public metadata may be unavailable for a private repository. */ }

            cancellationToken.ThrowIfCancellationRequested();
            if (git != null)
                return await git.ReadAsync(packageReference, revisionOverride, cancellationToken, timeout).ConfigureAwait(false);
            if (httpTimedOut) throw new PackageManifestReadException("Package metadata HTTP read timed out. Retry the check.");
            throw new InvalidOperationException("Package metadata is unavailable through the configured HTTP reader.");
        }
    }
}
