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
    internal sealed class PackageManifestProviderTests
    {
        private const string Remote = "git@bitbucket.org:deucarian/fixture.git";
        private const string Revision = "0123456789abcdef0123456789abcdef01234567";
        private const string Manifest = "{\"name\":\"com.deucarian.fixture\",\"version\":\"1.2.3\"}";

        [TestCase("https://bitbucket.org/deucarian/fixture.git#develop")]
        [TestCase("git+https://bitbucket.org/deucarian/fixture.git#develop")]
        [TestCase("git@bitbucket.org:deucarian/fixture.git#develop")]
        [TestCase("ssh://git@bitbucket.org/deucarian/fixture.git#develop")]
        public void CloudHttpsAndSshResolveTheSameMetadata(string source)
        {
            Assert.IsTrue(PackageRegistryPackageNameValidator.TryCreatePackageJsonUrl(source, "", out string url));
            Assert.AreEqual("https://api.bitbucket.org/2.0/repositories/deucarian/fixture/src/develop/package.json", url);
        }

        [Test]
        public void RevisionOverrideAndNestedPackagePathAreEncodedWithoutLosingSshAuth()
        {
            string source = Remote + "?path=Packages%2FFixture%20Package#feature/Work";
            Assert.IsTrue(PackageGitReference.TryParse(source, out var reference));
            Assert.IsTrue(reference.TryCreatePackageJsonUrl("", out string url));
            StringAssert.Contains("/src/feature%2FWork/Packages/Fixture%20Package/package.json", url);
            Assert.IsTrue(reference.TryCreatePackageJsonUrl(Revision, out url));
            StringAssert.Contains("/src/" + Revision + "/", url);
            Assert.IsTrue(reference.TryGetMetadataSource(Revision, out string remote, out string revision, out string path));
            Assert.AreEqual(Remote, remote);
            Assert.AreEqual(Revision, revision);
            Assert.AreEqual("Packages/Fixture Package/package.json", path);
        }

        [Test]
        public void GitHubCompatibilityMethodKeepsItsProviderBoundary()
        {
            Assert.IsTrue(PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                "https://github.com/Deucarian/Example.git?path=/Packages/Example#develop", out string url));
            Assert.AreEqual("https://raw.githubusercontent.com/Deucarian/Example/develop/Packages/Example/package.json", url);
            Assert.IsFalse(PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(Remote + "#develop", out _));
        }

        [Test]
        public void PrivateVersionLookupUsesOriginalSshReferenceAndResolvedCommit()
        {
            var fallback = new CapturingReader();
            PackageRegistryRemoteFetchDelegate inaccessible = (_, __, ___) => Task.FromException<PackageRegistryRemoteFetchResponse>(new InvalidOperationException("Synthetic inaccessible HTTP response"));
            var reader = new PackageManifestReader(inaccessible, fallback);
            var result = PackageManifestVersionReader.Read(Remote + "#develop", Revision, CancellationToken.None, reader, inaccessible, TimeSpan.FromSeconds(2));
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("1.2.3", result.Version);
            Assert.AreEqual(Remote + "#develop", fallback.Source);
            Assert.AreEqual(Revision, fallback.Revision);
        }

        [Test]
        public void MetadataVersionDiagnosticsPreserveTimeoutWithoutRawTransportDetails()
        {
            PackageRegistryRemoteFetchDelegate timeout = (_, __, ___) => Task.FromException<PackageRegistryRemoteFetchResponse>(
                new TimeoutException("RAW PRIVATE DETAIL"));
            foreach (IPackageManifestReader reader in new IPackageManifestReader[] { null, new PackageManifestReader(timeout) })
            {
                var result = PackageManifestVersionReader.Read(Remote + "#develop", Revision, CancellationToken.None, reader, timeout, TimeSpan.FromSeconds(2));
                Assert.IsFalse(result.Success);
                StringAssert.Contains("timed out", result.Message);
                StringAssert.DoesNotContain("RAW PRIVATE DETAIL", result.Message);
            }
        }

        [TestCase("https://user:placeholder@bitbucket.org/team/repo.git#develop")]
        [TestCase("https://user@bitbucket.org/team/repo.git#develop")]
        [TestCase("http://bitbucket.org/team/repo.git#develop")]
        [TestCase("https://bitbucket.org.invalid/team/repo.git#develop")]
        [TestCase("https://bitbucket.org:8443/team/repo.git#develop")]
        [TestCase("https://private-host.invalid/team/repo.git#develop")]
        [TestCase("https://bitbucket.org/team/repo.git?value=placeholder#develop")]
        [TestCase("https://bitbucket.org/team/repo.git?path=Packages&value=placeholder#develop")]
        [TestCase("https://bitbucket.org/team/repo.git?path=../Outside#develop")]
        [TestCase("https://bitbucket.org/team/repo.git#main:refs/heads/other")]
        [TestCase("https://bitbucket.org/team/repo.git#--upload-pack=other")]
        [TestCase("https://bitbucket.org/team/repo.git#feature/*")]
        [TestCase("https://bitbucket.org/team/repo.git#main%0Aother")]
        [TestCase("https://bitbucket.org/team/repo.git#../main")]
        [TestCase("https://bitbucket.org/team/repo.git#main?value=other")]
        [TestCase("https://bitbucket.org/team/.git#main")]
        public void UnsupportedMetadataReferencesAreRejectedBeforeTransport(string source)
        {
            Assert.IsFalse(PackageRegistryPackageNameValidator.TryCreatePackageJsonUrl(source, "", out string url));
            Assert.IsEmpty(url);
        }

        [UnityTest]
        public IEnumerator PublicMetadataUsesHttpAndPrivateFallbackPreservesOriginalReference() => DevelopmentAsyncTest.Run(async () =>
        {
            var fallback = new CapturingReader();
            var reader = new PackageManifestReader((_, __, ___) => Task.FromResult(new PackageRegistryRemoteFetchResponse(Manifest)), fallback);
            Assert.AreEqual(Manifest, (await reader.ReadAsync(Remote + "#develop", Revision, CancellationToken.None, TimeSpan.FromSeconds(2))).Content);
            Assert.AreEqual(0, fallback.Calls);
            reader = new PackageManifestReader((_, __, ___) => Task.FromException<PackageRegistryRemoteFetchResponse>(
                new InvalidOperationException("Synthetic inaccessible metadata")), fallback);
            await reader.ReadAsync(Remote + "#develop", Revision, CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, fallback.Calls);
            Assert.AreEqual(Remote + "#develop", fallback.Source);
            Assert.AreEqual(Revision, fallback.Revision);
        });

        [UnityTest]
        public IEnumerator CancellationAndInvalidUrlsDoNotStartFallback() => DevelopmentAsyncTest.Run(async () =>
        {
            var fallback = new CapturingReader();
            int http = 0;
            var reader = new PackageManifestReader((_, __, ___) => { http++; return Task.FromResult(new PackageRegistryRemoteFetchResponse(Manifest)); }, fallback);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await DevelopmentAsyncTest.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(Remote + "#develop", "", cancellation.Token, TimeSpan.FromSeconds(2)));
            }
            await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync("https://user:placeholder@bitbucket.org/team/repo.git#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2)));
            Assert.AreEqual(0, http);
            Assert.AreEqual(0, fallback.Calls);
        });

        [UnityTest]
        public IEnumerator GitMetadataIsScopedBoundedAndReadsOnlyOneBlob() => DevelopmentAsyncTest.Run(async () =>
        {
            string storage = CreateStorage();
            var runner = new RecordingRunner();
            var reader = new PackageGitManifestReader(runner, new PackageManifestScratchFactory(storage));
            var response = await reader.ReadAsync(Remote + "?path=Packages/Fixture#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.AreEqual(Manifest, response.Content);
            Assert.IsTrue(runner.Calls.Any(call => call.Contains(Remote) && call.Contains("ls-remote")));
            Assert.IsTrue(runner.Calls.Any(call => call.Contains("refs/heads/develop:refs/package-manifest/candidate") && call.Contains("--depth=1")));
            Assert.IsTrue(runner.Calls.Any(call => call.Contains("cat-file") && call.Contains("blob") && call.Contains(Revision + ":Packages/Fixture/package.json")));
            Assert.IsTrue(runner.Calls.All(call => call.Contains("protocol.file.allow=never") && call.Contains("protocol.ext.allow=never")));
            Assert.IsFalse(runner.Calls.Any(call => call.Contains("checkout") || call.Contains("push") || call.Contains("config")));
            Assert.IsEmpty(Directory.GetDirectories(storage));
        });

        [UnityTest]
        public IEnumerator AmbiguousRefsOversizedMetadataAndErrorsAreSanitizedAndCleaned() => DevelopmentAsyncTest.Run(async () =>
        {
            foreach (string mode in new[] { "ambiguous", "oversize", "failure" })
            {
                string storage = CreateStorage();
                var runner = new RecordingRunner { Mode = mode };
                var reader = new PackageGitManifestReader(runner, new PackageManifestScratchFactory(storage));
                var error = await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(Remote + "#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2)));
                StringAssert.DoesNotContain("RAW PRIVATE DETAIL", error.Message);
                Assert.IsFalse(runner.Calls.Any(call => call.Contains("blob")));
                Assert.IsEmpty(Directory.GetDirectories(storage));
            }
        });

        [UnityTest]
        public IEnumerator ConcurrentReadsGetDifferentScratchRepositories() => DevelopmentAsyncTest.Run(async () =>
        {
            string storage = CreateStorage();
            var runner = new RecordingRunner { Mode = "concurrent" };
            var reader = new PackageGitManifestReader(runner, new PackageManifestScratchFactory(storage));
            await Task.WhenAll(reader.ReadAsync(Remote + "#develop", Revision, CancellationToken.None, TimeSpan.FromSeconds(2)),
                reader.ReadAsync(Remote + "#develop", Revision, CancellationToken.None, TimeSpan.FromSeconds(2)));
            Assert.AreEqual(2, runner.Roots.Distinct().Count());
            Assert.IsEmpty(Directory.GetDirectories(storage));
        });

        [UnityTest]
        public IEnumerator FloatingRefsInvalidateTheBoundedCommitCache() => DevelopmentAsyncTest.Run(async () =>
        {
            string storage = CreateStorage();
            var runner = new RecordingRunner();
            var reader = new PackageGitManifestReader(runner, new PackageManifestScratchFactory(storage));
            await reader.ReadAsync(Remote + "#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2));
            await reader.ReadAsync(Remote + "#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.AreEqual(1, runner.Calls.Count(call => call.Contains("fetch")));
            Assert.AreEqual(2, runner.Calls.Count(call => call.Contains("ls-remote")), "Floating refs are checked before a cache hit.");
            runner.CurrentRevision = new string('b', 40);
            await reader.ReadAsync(Remote + "#develop", "", CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.AreEqual(2, runner.Calls.Count(call => call.Contains("fetch")));
            for (int i = 1; i <= 32; i++)
            {
                runner.CurrentRevision = i.ToString("x40");
                await reader.ReadAsync(Remote + "#develop", runner.CurrentRevision, CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            runner.CurrentRevision = Revision;
            int before = runner.Calls.Count(call => call.Contains("fetch"));
            await reader.ReadAsync(Remote + "#develop", Revision, CancellationToken.None, TimeSpan.FromSeconds(2));
            Assert.AreEqual(before + 1, runner.Calls.Count(call => call.Contains("fetch")), "The oldest entry was evicted at 32 successful reads.");
        });

        [UnityTest]
        public IEnumerator ScratchBudgetAndTimeoutCancelTheOwnedProcessAndCleanScratch() => DevelopmentAsyncTest.Run(async () =>
        {
            foreach (string mode in new[] { "scratch-limit", "timeout" })
            {
                string storage = CreateStorage();
                var runner = new RecordingRunner { Mode = mode };
                var reader = new PackageGitManifestReader(runner, new PackageManifestScratchFactory(storage));
                var error = await DevelopmentAsyncTest.ThrowsAsync<PackageManifestReadException>(() => reader.ReadAsync(Remote + "#develop", Revision,
                    CancellationToken.None, mode == "timeout" ? TimeSpan.FromMilliseconds(80) : TimeSpan.FromSeconds(2)));
                StringAssert.Contains(mode == "timeout" ? "timed out" : "32 MiB", error.Message);
                Assert.IsTrue(runner.Canceled);
                Assert.IsEmpty(Directory.GetDirectories(storage));
            }
        });

        [UnityTest]
        public IEnumerator HttpManifestStreamStopsAtLimitPlusOneByte() => DevelopmentAsyncTest.Run(async () =>
        {
            using (var stream = new MemoryStream(new byte[100000]))
            {
                await DevelopmentAsyncTest.ThrowsAsync<PackageManifestReadException>(() =>
                    PackageRegistryRemoteFetch.ReadBoundedManifestAsync(stream, 8192, CancellationToken.None));
                Assert.AreEqual(8193, stream.Position);
            }
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Manifest)))
                Assert.AreEqual(Manifest, await PackageRegistryRemoteFetch.ReadBoundedManifestAsync(stream, 8192, CancellationToken.None));
        });

        [UnityTest]
        public IEnumerator MixedProviderPrivateCatalogValidatesBothOriginalChannelsBeforeCaching() => DevelopmentAsyncTest.Run(async () =>
        {
            string storage = CreateStorage();
            const string stable = "git@github.com:deucarian/fixture.git#main";
            string development = Remote + "#develop";
            string json = "{\"schemaVersion\":1,\"packages\":[{\"id\":\"com.deucarian.fixture\",\"displayName\":\"Fixture\",\"category\":\"Core\",\"stableUrl\":\"" + stable +
                "\",\"developmentUrl\":\"" + development + "\",\"dependencies\":[]}]}";
            var fallback = new CapturingReader();
            PackageRegistryRemoteFetchDelegate inaccessible = (_, __, ___) => Task.FromException<PackageRegistryRemoteFetchResponse>(new InvalidOperationException("Synthetic private HTTP response"));
            var reader = new PackageManifestReader(inaccessible, fallback);
            var loader = new PackageRegistryLoader((_, __, ___) => Task.FromResult(new PackageRegistryRemoteFetchResponse(json)),
                PackageRegistryLoader.RemoteRegistryUrl, inaccessible, Path.Combine(storage, "catalog-cache.json"), TimeSpan.FromSeconds(2), reader);
            var result = await loader.LoadRemoteAsync((PackageRegistry)null);
            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.Remote, result.Source);
            Assert.AreEqual(2, fallback.Calls);
            Assert.AreEqual(development, fallback.Source);
            Assert.IsTrue(loader.TryLoadCached(out var cached, out string error), error);
            Assert.AreEqual(development, cached.Registry.packages[0].developmentUrl);
        });

        [Test]
        public void ScratchCleanupRetainsChangedOwnershipAndNeverTouchesSiblingData()
        {
            string storage = CreateStorage();
            string sibling = Path.Combine(storage, "user-data");
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "keep.txt"), "retained");
            var factory = new PackageManifestScratchFactory(storage);
            using (var owned = factory.Create())
            {
                File.WriteAllText(Path.Combine(owned.Root, ".deucarian-manifest-owner"), "changed");
                Assert.Throws<IOException>(owned.AssertOwned);
            }
            Assert.AreEqual("retained", File.ReadAllText(Path.Combine(sibling, "keep.txt")));
            Assert.AreEqual(2, Directory.GetDirectories(storage).Length);
        }

        internal static string CreateStorage()
        {
            string root = Environment.GetEnvironmentVariable("DEUCARIAN_TEST_ARTIFACT_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = Path.DirectorySeparatorChar == '\\'
                ? "D:/Codex-storage/validation/package-development-20260910/manifest-tests"
                : Path.Combine(Path.GetTempPath(), "deucarian-package-manifest-tests");
            string storage = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(storage);
            return storage; // Retained synthetic test roots support review; production request directories are retired.
        }

        private sealed class CapturingReader : IPackageManifestReader
        {
            internal int Calls;
            internal string Source, Revision;
            public Task<PackageRegistryRemoteFetchResponse> ReadAsync(string source, string revision, CancellationToken token, TimeSpan timeout)
            { Calls++; Source = source; Revision = revision; return Task.FromResult(new PackageRegistryRemoteFetchResponse(Manifest)); }
        }

        private sealed class RecordingRunner : IDevelopmentGitRunner
        {
            internal readonly List<string[]> Calls = new List<string[]>();
            internal readonly List<string> Roots = new List<string>();
            internal string Mode;
            internal string CurrentRevision = Revision;
            internal bool Canceled;
            private readonly TaskCompletionSource<bool> bothInitializations = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> args, CancellationToken token)
            {
                string[] call = args.ToArray();
                lock (Calls)
                {
                    Calls.Add(call);
                    if (call.Contains("init"))
                    {
                        Roots.Add(directory);
                        if (Roots.Count == 2) bothInitializations.TrySetResult(true);
                    }
                }
                if (Mode == "concurrent" && call.Contains("init")) await bothInitializations.Task;
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                if (call.Contains("fetch") && (Mode == "scratch-limit" || Mode == "timeout"))
                {
                    if (Mode == "scratch-limit")
                    {
                        Directory.CreateDirectory(directory);
                        using (var file = File.Create(Path.Combine(directory, "synthetic-pack")))
                            file.SetLength(PackageManifestScratchFactory.MaximumScratchBytes + 1);
                    }
                    try { await Task.Delay(Timeout.Infinite, token); }
                    catch (OperationCanceledException) { Canceled = true; throw; }
                }
                if (Mode == "failure") return new DevelopmentGitResult(1, "RAW PRIVATE DETAIL", "RAW PRIVATE DETAIL");
                string output = "";
                if (call.Contains("ls-remote")) output = CurrentRevision + "\trefs/heads/develop\n" +
                    (Mode == "ambiguous" ? CurrentRevision + "\trefs/tags/develop\n" : "");
                if (call.Contains("rev-parse")) output = CurrentRevision;
                if (call.Contains("-s")) output = Mode == "oversize" ? "999999" : Manifest.Length.ToString();
                if (call.Contains("blob")) output = Manifest;
                return new DevelopmentGitResult(0, output);
            }
        }
    }
}
