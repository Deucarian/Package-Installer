using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageManifestGitIntegrationTests
    {
        [UnityTest]
        public IEnumerator CloudFallbackReadsTheExactCommitWithoutTouchingCheckoutOrRemote() => DevelopmentAsyncTest.Run(() => Exercise(false));

        [UnityTest]
        public IEnumerator GitHubFilteredFallbackRetrievesManifestWithoutFetchingOtherBlobs() => DevelopmentAsyncTest.Run(() => Exercise(true));

        private static async Task Exercise(bool filtered)
        {
            string root = PackageManifestProviderTests.CreateStorage();
            string source = Path.Combine(root, "source");
            string remote = Path.Combine(root, "fixture.git");
            string storage = Path.Combine(root, "scratch");
            Directory.CreateDirectory(source);
            var runner = new DevelopmentGitProcessRunner(15000, 300000);
            await Git(runner, source, "init", "--initial-branch=develop");
            await Git(runner, source, "config", "user.name", "Disposable Metadata Fixture");
            await Git(runner, source, "config", "user.email", "metadata-fixture@example.invalid");
            string manifest = "{\"name\":\"com.deucarian.fixture\",\"version\":\"1.0.0\"}";
            File.WriteAllText(Path.Combine(source, "package.json"), manifest);
            byte[] large = new byte[1024 * 1024];
            new Random(7).NextBytes(large);
            File.WriteAllBytes(Path.Combine(source, "Unrelated.bin"), large);
            await Git(runner, source, "add", ".");
            await Git(runner, source, "commit", "-m", "Seed synthetic package metadata");
            string head = (await Git(runner, source, "rev-parse", "HEAD")).Trim();
            string blob = (await Git(runner, source, "rev-parse", "HEAD:Unrelated.bin")).Trim();
            await Git(runner, root, "clone", "--bare", source, remote);
            // Only the synthetic local server is configured for filtered-fetch tests.
            await Git(runner, remote, "config", "uploadpack.allowFilter", "true");
            await Git(runner, remote, "config", "uploadpack.allowAnySHA1InWant", "true");
            string sourceIndex = await Git(runner, source, "ls-files", "--stage", "-z");
            string remoteBefore = await Git(runner, remote, "show-ref");
            File.WriteAllText(Path.Combine(source, "package.json"), "{\"name\":\"com.deucarian.fixture\",\"version\":\"9.0.0\"}");
            string advertised = filtered ? "git@github.com:deucarian/fixture.git" : "git@bitbucket.org:deucarian/fixture.git";
            var mapped = new FixtureRemoteRunner(runner, advertised, remote, blob);
            var reader = new PackageGitManifestReader(mapped, new PackageManifestScratchFactory(storage));
            var response = await reader.ReadAsync(advertised + "#develop", head, CancellationToken.None, TimeSpan.FromSeconds(10));
            Assert.AreEqual(manifest, response.Content);
            Assert.AreEqual(filtered, mapped.FilterRequested);
            if (filtered) Assert.IsTrue(mapped.UnrelatedBlobAbsentAfterRead, "The deferred metadata blob fetch must not retrieve unrelated package assets.");
            Assert.AreEqual(head, (await Git(runner, source, "rev-parse", "HEAD")).Trim());
            Assert.AreEqual(sourceIndex, await Git(runner, source, "ls-files", "--stage", "-z"));
            Assert.AreEqual(remoteBefore, await Git(runner, remote, "show-ref"));
            StringAssert.Contains("9.0.0", File.ReadAllText(Path.Combine(source, "package.json")));
            Assert.IsEmpty(Directory.GetDirectories(storage));
            int requests = mapped.Fetches;
            Assert.AreEqual(manifest, (await reader.ReadAsync(advertised + "#develop", head, CancellationToken.None, TimeSpan.FromSeconds(10))).Content);
            Assert.AreEqual(requests, mapped.Fetches, "A second immutable commit lookup uses the bounded instance cache.");
        }

        private static async Task<string> Git(IDevelopmentGitRunner runner, string directory, params string[] args) =>
            (await runner.RunAsync(directory, args, CancellationToken.None)).RequireSuccess();

        // This test-only adapter maps one allowlisted provider URL to one synthetic bare server.
        // Production never accepts local/file metadata remotes.
        private sealed class FixtureRemoteRunner : IDevelopmentGitRunner
        {
            private readonly IDevelopmentGitRunner runner;
            private readonly string advertised, remote, unrelatedBlob;
            internal bool FilterRequested, UnrelatedBlobAbsentAfterRead;
            internal int Fetches;
            internal FixtureRemoteRunner(IDevelopmentGitRunner runner, string advertised, string remote, string unrelatedBlob)
            { this.runner = runner; this.advertised = advertised; this.remote = remote; this.unrelatedBlob = unrelatedBlob; }

            public async Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments, CancellationToken token)
            {
                string localUrl = new Uri(remote + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
                string[] mapped = arguments.Select(arg => arg == advertised ? localUrl :
                    arg == "remote.manifest.url=" + advertised ? "remote.manifest.url=" + localUrl :
                    arg == "protocol.file.allow=never" ? "protocol.file.allow=always" : arg).ToArray();
                if (mapped.Contains("fetch")) Fetches++;
                FilterRequested |= mapped.Contains("--filter=blob:none");
                if (mapped.Contains("--filter=blob:none"))
                {
                    Assert.AreEqual("1", (await Git(runner, directory, "config", "--local", "--get", "core.repositoryformatversion")).Trim());
                    Assert.AreEqual("manifest", (await Git(runner, directory, "config", "--local", "--get", "extensions.partialClone")).Trim());
                    string config = File.ReadAllText(Path.Combine(directory, "config"));
                    StringAssert.DoesNotContain(advertised, config, "Provider URLs and authentication stay per-command.");
                    StringAssert.DoesNotContain(remote, config, "The fixture mapping also stays per-command.");
                }
                var result = await runner.RunAsync(directory, mapped, token);
                if (result.Success && mapped.Contains("cat-file") && mapped.Contains("blob") && FilterRequested)
                {
                    // Without the per-command promisor URL this cannot perform a lazy network fetch.
                    var absent = await runner.RunAsync(directory,
                        new[] { "-c", "remote.manifest.promisor=false", "cat-file", "-e", unrelatedBlob }, token);
                    UnrelatedBlobAbsentAfterRead = !absent.Success;
                }
                return result;
            }
        }
    }
}
