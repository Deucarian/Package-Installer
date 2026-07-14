using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageRegistryReliabilityTests
    {
        private readonly List<string> _temporaryDirectories = new List<string>();

        [TearDown]
        public void TearDown()
        {
            PackageRegistryProvider.ResetForTests();

            foreach (string directory in _temporaryDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }

            _temporaryDirectories.Clear();
        }

        [Test]
        public void OfficialChannelRequiresRepositoryPathAndReferenceMatch()
        {
            PackageDefinition package = CreatePackage(
                "https://github.com/Deucarian/Monorepo.git?path=/Packages/Object-Loading#main",
                "https://github.com/Deucarian/Monorepo.git?path=/Packages/Object-Loading#develop");

            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageReferenceForTests(
                    package.PackageId,
                    "git+https://github.com/deucarian/Monorepo.git?path=Packages%2FObject-Loading#refs/heads/main");

                Assert.IsTrue(detection.TryGetInstalledPackageChannel(package, out PackageChannel channel, out _));
                Assert.AreEqual(PackageChannel.Stable, channel);

                detection.ReplaceInstalledPackageReferenceForTests(
                    package.PackageId,
                    "https://github.com/Deucarian/Monorepo.git?path=/Packages/Other#main");

                Assert.IsTrue(detection.TryGetInstalledPackageChannel(package, out channel, out _));
                Assert.AreEqual(PackageChannel.Custom, channel);
            }
        }

        [Test]
        public void ForkWithOfficialBranchNameRemainsCustom()
        {
            PackageDefinition package = CreatePackage();

            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageReferenceForTests(
                    package.PackageId,
                    "https://github.com/example-fork/Object-Loading.git#main");

                Assert.IsTrue(detection.TryGetInstalledPackageChannel(package, out PackageChannel channel, out _));
                Assert.AreEqual(PackageChannel.Custom, channel);
            }
        }

        [Test]
        public void ScpAndHttpsReferencesNormalizeToSameOfficialTarget()
        {
            Assert.IsTrue(PackageGitReference.MatchesChannel(
                "git@github.com:Deucarian/Object-Loading.git#origin/develop",
                "https://github.com/deucarian/Object-Loading#develop"));
        }

        [Test]
        public void LocalOrEmbeddedInstalledPackageWithoutReferenceIsCustom()
        {
            PackageDefinition package = CreatePackage();

            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageForTests(
                    package.PackageId,
                    string.Empty,
                    PackageInstallSourceType.Local);

                Assert.IsTrue(detection.TryGetInstalledPackageChannel(
                    package,
                    out PackageChannel channel,
                    out string packageReference));
                Assert.AreEqual(PackageChannel.Custom, channel);
                Assert.IsEmpty(packageReference);
            }
        }

        [Test]
        public void GitReferenceNamesRemainCaseSensitive()
        {
            Assert.IsFalse(PackageGitReference.MatchesChannel(
                "https://github.com/Deucarian/Object-Loading.git#Feature/X",
                "https://github.com/deucarian/Object-Loading#feature/x"));
        }

        [Test]
        public void RecoveryExactTargetRequiresInstalledIdentityToChange()
        {
            PackageDefinition package = CreatePackage();
            using (PackageDetectionService detection = new PackageDetectionService())
            {
                detection.ReplaceInstalledPackageForTests(
                    package.PackageId,
                    package.StableUrl,
                    PackageInstallSourceType.Git,
                    "1.0.0",
                    "old-hash");
                string beforeUpdate = detection.GetInstalledIdentity(package.PackageId);

                Assert.IsFalse(detection.IsInstalledAtExactTargetAfterChange(
                    package.PackageId,
                    package.StableUrl,
                    beforeUpdate));

                detection.ReplaceInstalledPackageForTests(
                    package.PackageId,
                    package.StableUrl,
                    PackageInstallSourceType.Git,
                    "1.0.1",
                    "new-hash");
                Assert.IsTrue(detection.IsInstalledAtExactTargetAfterChange(
                    package.PackageId,
                    package.StableUrl,
                    beforeUpdate));
            }
        }

        [Test]
        public void RegistryRejectsSelfDependency()
        {
            const string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.self\", \"displayName\": \"Self\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/self.git#main\", \"dependencies\": [\"com.deucarian.self\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("cannot depend on itself", result.ErrorMessage);
        }

        [Test]
        public void RemoteValidationChecksDevelopmentManifestAsWellAsStable()
        {
            RunAsync(async () =>
            {
                PackageRegistry registry = new PackageRegistry
                {
                    schemaVersion = 1,
                    packages = new[]
                    {
                        new PackageRegistryEntry
                        {
                            id = "com.deucarian.channels",
                            displayName = "Channels",
                            category = "Core",
                            stableUrl = "https://github.com/Deucarian/Channels.git#main",
                            developmentUrl = "https://github.com/Deucarian/Channels.git#develop",
                            dependencies = Array.Empty<string>()
                        }
                    }
                };
                List<string> requestedUrls = new List<string>();
                PackageRegistryRemoteFetchDelegate fetcher = (url, token, timeout) =>
                {
                    requestedUrls.Add(url);
                    string name = url.EndsWith("/develop/package.json", StringComparison.Ordinal)
                        ? "com.deucarian.wrong-development-name"
                        : "com.deucarian.channels";
                    return Task.FromResult(new PackageRegistryRemoteFetchResponse(
                        "{ \"name\": \"" + name + "\" }"));
                };

                string message = await PackageRegistryPackageNameValidator
                    .ValidateRemotePackageNamesAsync(
                        registry,
                        fetcher,
                        CancellationToken.None,
                        TimeSpan.FromSeconds(2),
                        4)
                    .ConfigureAwait(false);

                Assert.AreEqual(2, requestedUrls.Count);
                StringAssert.Contains("wrong-development-name", message);
                StringAssert.Contains("/develop/package.json", message);
            });
        }

        [Test]
        public void RegistryRejectsDependencyCycleWithPath()
        {
            const string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.a\", \"displayName\": \"A\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/a.git#main\", \"dependencies\": [\"com.deucarian.b\"] }," +
                "{ \"id\": \"com.deucarian.b\", \"displayName\": \"B\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/b.git#main\", \"dependencies\": [\"com.deucarian.c\"] }," +
                "{ \"id\": \"com.deucarian.c\", \"displayName\": \"C\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/c.git#main\", \"dependencies\": [\"com.deucarian.a\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains(
                "com.deucarian.a -> com.deucarian.b -> com.deucarian.c -> com.deucarian.a",
                result.ErrorMessage);
        }

        [Test]
        public void RemoteFetchTimeoutKeepsFallbackAndCancelsOwnedRequest()
        {
            RunAsync(async () =>
            {
                CancellationToken ownedRequestToken = CancellationToken.None;
                TaskCompletionSource<PackageRegistryRemoteFetchResponse> pending =
                    new TaskCompletionSource<PackageRegistryRemoteFetchResponse>();
                PackageRegistryRemoteFetchDelegate remoteFetcher = (url, token, timeout) =>
                {
                    ownedRequestToken = token;
                    return pending.Task;
                };
                PackageRegistryLoader loader = CreateLoader(
                    remoteFetcher,
                    CreateManifestFetcher(),
                    CreateCachePath(),
                    TimeSpan.FromMilliseconds(50));
                PackageRegistryLoadResult fallback = loader.LoadFromJson(
                    CreateRegistryJson("com.deucarian.fallback", "Fallback"),
                    PackageRegistrySource.Bundled);

                PackageRegistryLoadResult result = await loader.LoadRemoteAsync(
                    fallback,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.IsTrue(result.IsValid, result.ErrorMessage);
                Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingBundled, result.Source);
                StringAssert.Contains("timed out", result.ErrorMessage);
                Assert.IsTrue(ownedRequestToken.CanBeCanceled);
                Assert.IsTrue(ownedRequestToken.IsCancellationRequested);
            });
        }

        [Test]
        public void RemoteFetchHonorsCallerCancellation()
        {
            RunAsync(async () =>
            {
                TaskCompletionSource<PackageRegistryRemoteFetchResponse> pending =
                    new TaskCompletionSource<PackageRegistryRemoteFetchResponse>();
                PackageRegistryRemoteFetchDelegate remoteFetcher = (url, token, timeout) => pending.Task;
                PackageRegistryLoader loader = CreateLoader(
                    remoteFetcher,
                    CreateManifestFetcher(),
                    CreateCachePath(),
                    TimeSpan.FromSeconds(5));
                PackageRegistryLoadResult fallback = loader.LoadFromJson(
                    CreateRegistryJson("com.deucarian.fallback", "Fallback"),
                    PackageRegistrySource.Bundled);

                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    Task<PackageRegistryLoadResult> loadTask = loader.LoadRemoteAsync(
                        fallback,
                        cancellation.Token);
                    cancellation.Cancel();

                    try
                    {
                        await loadTask.ConfigureAwait(false);
                        Assert.Fail("Expected the remote load to be canceled.");
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            });
        }

        [Test]
        public void ProviderCancellationStopsOwnedRemoteRefresh()
        {
            RunAsync(async () =>
            {
                CancellationToken observedToken = CancellationToken.None;
                PackageRegistryRemoteFetchDelegate pendingFetcher = (url, token, timeout) =>
                {
                    observedToken = token;
                    return new TaskCompletionSource<PackageRegistryRemoteFetchResponse>()
                        .Task;
                };
                PackageRegistryLoader loader = new PackageRegistryLoader(
                    pendingFetcher,
                    PackageRegistryLoader.RemoteRegistryUrl,
                    CreateManifestFetcher(),
                    CreateCachePath(),
                    TimeSpan.FromSeconds(10));
                PackageRegistryProvider.SetLoaderForTests(loader);

                PackageRegistryProvider.EnsureLoaded();
                Assert.IsTrue(PackageRegistryProvider.IsRemoteRefreshing);
                Assert.IsTrue(PackageRegistryProvider.CancelRemoteRefresh());
                await WaitUntilAsync(() => observedToken.IsCancellationRequested)
                    .ConfigureAwait(false);

                Assert.IsFalse(PackageRegistryProvider.IsRemoteRefreshing);
                Assert.IsFalse(PackageRegistryProvider.CancelRemoteRefresh());
            });
        }

        [Test]
        public void EditorQuitCancelsOwnedRemoteRefresh()
        {
            RunAsync(async () =>
            {
                CancellationToken observedToken = CancellationToken.None;
                TaskCompletionSource<bool> cancellationObserved = new TaskCompletionSource<bool>();
                PackageRegistryRemoteFetchDelegate pendingFetcher = async (url, token, timeout) =>
                {
                    observedToken = token;
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.TrySetResult(true);
                        throw;
                    }

                    return new PackageRegistryRemoteFetchResponse(string.Empty);
                };
                PackageRegistryLoader loader = new PackageRegistryLoader(
                    pendingFetcher,
                    PackageRegistryLoader.RemoteRegistryUrl,
                    CreateManifestFetcher(),
                    CreateCachePath(),
                    TimeSpan.FromSeconds(10));
                PackageRegistryProvider.SetLoaderForTests(loader);

                PackageRegistryProvider.EnsureLoaded();
                Assert.IsTrue(PackageRegistryProvider.IsRemoteRefreshing);
                Assert.IsTrue(observedToken.CanBeCanceled);
                PackageRegistryProvider.NotifyEditorQuittingForTests();
                await WaitUntilAsync(() => cancellationObserved.Task.IsCompleted)
                    .ConfigureAwait(false);

                Assert.IsTrue(observedToken.IsCancellationRequested);
                Assert.IsFalse(PackageRegistryProvider.IsRemoteRefreshing);
                Assert.IsFalse(PackageRegistryProvider.CancelRemoteRefresh());
            });
        }

        [Test]
        public void ManifestValidationNeverExceedsFourConcurrentFetches()
        {
            RunAsync(async () =>
            {
                PackageRegistry registry = CreateRegistry(12);
                Dictionary<string, string> packageIdByUrl = registry.packages.ToDictionary(
                    package => GetPackageJsonUrl(package),
                    package => package.id,
                    StringComparer.Ordinal);
                TaskCompletionSource<bool> releaseFetches = new TaskCompletionSource<bool>();
                int activeFetches = 0;
                int peakFetches = 0;
                PackageRegistryRemoteFetchDelegate fetcher = async (url, token, timeout) =>
                {
                    int active = Interlocked.Increment(ref activeFetches);
                    UpdateMaximum(ref peakFetches, active);

                    try
                    {
                        await releaseFetches.Task.ConfigureAwait(false);
                        return new PackageRegistryRemoteFetchResponse(
                            "{ \"name\": \"" + packageIdByUrl[url] + "\" }");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeFetches);
                    }
                };

                Task<string> validation =
                    PackageRegistryPackageNameValidator.ValidateRemotePackageNamesAsync(
                        registry,
                        fetcher,
                        CancellationToken.None,
                        TimeSpan.FromSeconds(5),
                        20);
                await WaitUntilAsync(() => Volatile.Read(ref activeFetches) == 4)
                    .ConfigureAwait(false);

                Assert.AreEqual(4, Volatile.Read(ref peakFetches));
                releaseFetches.SetResult(true);
                Assert.IsEmpty(await validation.ConfigureAwait(false));
                Assert.AreEqual(4, peakFetches);
            });
        }

        [Test]
        public void ValidRemoteIsCachedAndInvalidRefreshCannotReplaceIt()
        {
            RunAsync(async () =>
            {
                string cachePath = CreateCachePath();
                const string cachedPackageId = "com.deucarian.cached";
                string validRemoteJson = CreateRegistryJson(cachedPackageId, "Cached");
                PackageRegistryLoader seedLoader = CreateLoader(
                    CreateContentFetcher(validRemoteJson, "\"etag-one\""),
                    CreateManifestFetcher(cachedPackageId),
                    cachePath,
                    TimeSpan.FromSeconds(2));
                PackageRegistryLoadResult fallback = seedLoader.LoadFromJson(
                    CreateRegistryJson("com.deucarian.fallback", "Fallback"),
                    PackageRegistrySource.Bundled);

                PackageRegistryLoadResult fresh = await seedLoader.LoadRemoteAsync(
                    fallback,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(PackageRegistrySource.Remote, fresh.Source);
                Assert.IsTrue(File.Exists(cachePath));
                string cacheBeforeFailure = File.ReadAllText(cachePath);

                PackageRegistryLoader invalidLoader = CreateLoader(
                    CreateContentFetcher("{ \"schemaVersion\": 999, \"packages\": [] }"),
                    CreateManifestFetcher(),
                    cachePath,
                    TimeSpan.FromSeconds(2));
                Assert.IsTrue(invalidLoader.TryLoadCached(
                    out PackageRegistryLoadResult cached,
                    out string cacheError), cacheError);
                Assert.AreEqual(PackageRegistrySource.Cached, cached.Source);
                Assert.AreEqual("\"etag-one\"", cached.EntityTag);
                Assert.AreEqual(cachedPackageId, cached.Registry.packages.Single().id);

                PackageRegistryLoadResult failedRefresh = await invalidLoader.LoadRemoteAsync(
                    cached,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingCache, failedRefresh.Source);
                Assert.AreEqual(cachedPackageId, failedRefresh.Registry.packages.Single().id);
                Assert.AreEqual(cacheBeforeFailure, File.ReadAllText(cachePath));
            });
        }

        [Test]
        public void RevokedGenerationCannotReplaceLastKnownGoodCache()
        {
            string cachePath = CreateCachePath();
            PackageRegistryCache cache = new PackageRegistryCache(cachePath);
            string firstRegistry = CreateRegistryJson("com.deucarian.first", "First");
            string staleRegistry = CreateRegistryJson("com.deucarian.stale", "Stale");

            Assert.IsTrue(cache.TryWrite(
                firstRegistry,
                PackageRegistryLoader.RemoteRegistryUrl,
                "etag-first",
                DateTimeOffset.UtcNow,
                "2026-07-13T00:00:00Z",
                out string seedError), seedError);
            string originalCache = File.ReadAllText(cachePath);
            PackageRegistryCacheCommitGuard staleGeneration =
                new PackageRegistryCacheCommitGuard();
            staleGeneration.Revoke();

            Assert.IsFalse(cache.TryWrite(
                staleRegistry,
                PackageRegistryLoader.RemoteRegistryUrl,
                "etag-stale",
                DateTimeOffset.UtcNow,
                "2026-07-13T00:01:00Z",
                out string staleError,
                CancellationToken.None,
                staleGeneration));
            StringAssert.Contains("superseded", staleError);
            Assert.AreEqual(originalCache, File.ReadAllText(cachePath));
        }

        [Test]
        public void TamperedCachedRegistryIsRejected()
        {
            RunAsync(async () =>
            {
                string cachePath = CreateCachePath();
                const string packageId = "com.deucarian.cached";
                PackageRegistryLoader loader = CreateLoader(
                    CreateContentFetcher(CreateRegistryJson(packageId, "Cached")),
                    CreateManifestFetcher(packageId),
                    cachePath,
                    TimeSpan.FromSeconds(2));
                PackageRegistryLoadResult fallback = loader.LoadFromJson(
                    CreateRegistryJson("com.deucarian.fallback", "Fallback"),
                    PackageRegistrySource.Bundled);
                await loader.LoadRemoteAsync(fallback, CancellationToken.None).ConfigureAwait(false);

                string cacheJson = File.ReadAllText(cachePath)
                    .Replace(packageId, "com.deucarian.tampered");
                File.WriteAllText(cachePath, cacheJson);

                Assert.IsFalse(loader.TryLoadCached(out _, out string errorMessage));
                StringAssert.Contains("content hash", errorMessage);
            });
        }

        [Test]
        public void ProviderUsesCachedRegistryWhileFreshRefreshIsPending()
        {
            RunAsync(async () =>
            {
                string cachePath = CreateCachePath();
                const string cachedPackageId = "com.deucarian.cached";
                PackageRegistryLoader seedLoader = CreateLoader(
                    CreateContentFetcher(CreateRegistryJson(cachedPackageId, "Cached")),
                    CreateManifestFetcher(cachedPackageId),
                    cachePath,
                    TimeSpan.FromSeconds(2));
                PackageRegistryLoadResult fallback = seedLoader.LoadFromJson(
                    CreateRegistryJson("com.deucarian.fallback", "Fallback"),
                    PackageRegistrySource.Bundled);
                await seedLoader.LoadRemoteAsync(fallback, CancellationToken.None).ConfigureAwait(false);

                TaskCompletionSource<PackageRegistryRemoteFetchResponse> pending =
                    new TaskCompletionSource<PackageRegistryRemoteFetchResponse>();
                PackageRegistryLoader providerLoader = CreateLoader(
                    (url, token, timeout) => pending.Task,
                    CreateManifestFetcher(),
                    cachePath,
                    TimeSpan.FromSeconds(5));
                PackageRegistryProvider.SetLoaderForTests(providerLoader);

                PackageRegistryProvider.EnsureLoaded();

                Assert.AreEqual(
                    PackageRegistrySource.Cached,
                    PackageRegistryProvider.CurrentLoadResult.Source);
                Assert.AreEqual(
                    cachedPackageId,
                    PackageRegistryProvider.CurrentLoadResult.Registry.packages.Single().id);
                Assert.IsTrue(PackageRegistryProvider.IsRemoteRefreshing);
                pending.TrySetCanceled();
            });
        }

        [Test]
        public void ProviderGenerationGateRejectsStaleCompletion()
        {
            Assert.IsFalse(PackageRegistryProvider.ShouldApplyRemoteRefreshForTests(4, 5));
            Assert.IsTrue(PackageRegistryProvider.ShouldApplyRemoteRefreshForTests(5, 5));
        }

        private string CreateCachePath()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "Deucarian-PackageInstaller-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _temporaryDirectories.Add(directory);
            return Path.Combine(directory, PackageRegistryCache.CacheFileName);
        }

        private static PackageRegistryLoader CreateLoader(
            PackageRegistryRemoteFetchDelegate remoteFetcher,
            PackageRegistryRemoteFetchDelegate manifestFetcher,
            string cachePath,
            TimeSpan timeout)
        {
            return new PackageRegistryLoader(
                remoteFetcher,
                PackageRegistryLoader.RemoteRegistryUrl,
                manifestFetcher,
                cachePath,
                timeout);
        }

        private static PackageRegistryRemoteFetchDelegate CreateContentFetcher(
            string content,
            string entityTag = "")
        {
            return (url, token, timeout) => Task.FromResult(
                new PackageRegistryRemoteFetchResponse(content, entityTag));
        }

        private static PackageRegistryRemoteFetchDelegate CreateManifestFetcher(
            string packageId = "com.deucarian.example")
        {
            return (url, token, timeout) => Task.FromResult(
                new PackageRegistryRemoteFetchResponse(
                    "{ \"name\": \"" + packageId + "\" }"));
        }

        private static PackageDefinition CreatePackage(
            string stableUrl = "https://github.com/Deucarian/Object-Loading.git#main",
            string developmentUrl = "https://github.com/Deucarian/Object-Loading.git#develop")
        {
            return new PackageDefinition(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                stableUrl,
                "Reusable runtime loading pipeline.",
                Array.Empty<string>(),
                PackageType.Core,
                developmentUrl,
                category: "Core");
        }

        private static PackageRegistry CreateRegistry(int packageCount)
        {
            PackageRegistryEntry[] packages = new PackageRegistryEntry[packageCount];

            for (int index = 0; index < packageCount; index++)
            {
                packages[index] = new PackageRegistryEntry
                {
                    id = "com.deucarian.concurrent-" + index,
                    displayName = "Concurrent " + index,
                    category = "Core",
                    stableUrl = "https://github.com/Deucarian/Concurrent-" + index + ".git#main",
                    dependencies = Array.Empty<string>()
                };
            }

            return new PackageRegistry
            {
                schemaVersion = 1,
                updatedAt = "2026-07-13",
                packages = packages
            };
        }

        private static string CreateRegistryJson(string packageId, string repositoryName)
        {
            return "{ \"schemaVersion\": 1, \"updatedAt\": \"2026-07-13\", \"packages\": [" +
                   "{ \"id\": \"" + packageId + "\", \"displayName\": \"" + repositoryName +
                   "\", \"category\": \"Core\", \"stableUrl\": \"https://github.com/Deucarian/" +
                   repositoryName + ".git#main\", \"dependencies\": [] }" +
                   "] }";
        }

        private static string GetPackageJsonUrl(PackageRegistryEntry package)
        {
            Assert.IsTrue(PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                package.stableUrl,
                out string url));
            return url;
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            int observed;

            do
            {
                observed = Volatile.Read(ref maximum);

                if (candidate <= observed)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(3);

            while (!condition())
            {
                if (DateTime.UtcNow >= timeout)
                {
                    Assert.Fail("Timed out waiting for asynchronous test condition.");
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static void RunAsync(Func<Task> asyncTest)
        {
            asyncTest().GetAwaiter().GetResult();
        }
    }
}
