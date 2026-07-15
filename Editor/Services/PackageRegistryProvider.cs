using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryProvider
    {
        private static PackageRegistryLoader _loader = new PackageRegistryLoader();
        private static readonly IReadOnlyList<PackageDefinition> EmptyPackages =
            Array.Empty<PackageDefinition>();

        private static PackageRegistryLoadResult _currentLoadResult;
        private static IReadOnlyList<PackageDefinition> _allPackages = EmptyPackages;
        private static IReadOnlyDictionary<string, PackageDefinition> _packageById =
            new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<PackageGraphGroup> _ecosystemGroups =
            PackageGraphHierarchyBuilder.CreateGroups((IEnumerable<PackageGraphGroup>)null);
        private static RemoteRefreshOperation _remoteRefreshOperation;
        private static int _remoteRefreshGeneration;
        private static bool _bundledLoaded;
        private static bool _remoteRefreshStarted;

        static PackageRegistryProvider()
        {
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        public static event Action RegistryChanged;

        public static IReadOnlyList<PackageDefinition> All
        {
            get
            {
                EnsureLoaded();
                return _allPackages;
            }
        }

        public static IReadOnlyList<PackageDefinition> StandalonePackages =>
            All.Where(package => !package.IsIntegration).ToArray();

        public static IReadOnlyList<PackageDefinition> IntegrationPackages =>
            GetPackagesByCategory("Integration");

        public static IReadOnlyList<PackageGraphGroup> EcosystemGroups
        {
            get
            {
                EnsureLoaded();
                return _ecosystemGroups;
            }
        }

        public static IReadOnlyList<string> Categories =>
            All.Select(package => package.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetCategorySortIndex)
                .ThenBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static PackageRegistryLoadResult CurrentLoadResult
        {
            get
            {
                EnsureLoaded();
                return _currentLoadResult;
            }
        }

        public static bool IsRemoteRefreshing => _remoteRefreshOperation != null;

        public static string StatusMessage
        {
            get
            {
                PackageRegistryLoadResult result = CurrentLoadResult;
                return result != null ? result.StatusMessage : "Using bundled registry";
            }
        }

        public static void EnsureLoaded()
        {
            EnsureBundledLoaded();

            if (!_remoteRefreshStarted)
            {
                StartRemoteRefresh();
            }
        }

        public static void RefreshRemote()
        {
            EnsureBundledLoaded();
            StartRemoteRefresh(replaceExisting: true);
        }

        public static bool CancelRemoteRefresh()
        {
            RemoteRefreshOperation operation = _remoteRefreshOperation;
            if (operation == null)
            {
                return false;
            }

            CancelAndObserve(operation);
            _remoteRefreshOperation = null;
            _remoteRefreshGeneration++;
            EditorApplication.update -= UpdateRemoteRefresh;
            return true;
        }

        internal static void NotifyEditorQuittingForTests()
        {
            OnEditorQuitting();
        }

        public static IReadOnlyList<PackageDefinition> GetPackagesByCategory(string category)
        {
            EnsureLoaded();

            if (string.IsNullOrWhiteSpace(category))
            {
                return EmptyPackages;
            }

            return _allPackages
                .Where(package => string.Equals(
                    package.Category,
                    category,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public static bool TryGetPackage(string packageId, out PackageDefinition packageDefinition)
        {
            EnsureLoaded();

            packageDefinition = null;

            return !string.IsNullOrWhiteSpace(packageId) &&
                   _packageById.TryGetValue(packageId.Trim(), out packageDefinition);
        }

        public static IEnumerable<PackageDefinition> GetInstallableDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                yield break;
            }

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (TryGetPackage(dependencyId, out PackageDefinition dependency) && dependency.HasPackageReference)
                {
                    yield return dependency;
                }
            }
        }

        internal static IReadOnlyList<PackageDefinition> CreatePackageDefinitions(PackageRegistry registry)
        {
            if (registry == null || registry.packages == null)
            {
                return new[] { CreateInstallerPackageDefinition() };
            }

            PackageDefinition[] packageDefinitions = registry.packages
                .Where(entry => entry != null)
                .Select(CreatePackageDefinition)
                .ToArray();

            return EnsureInstallerPackageDefinition(packageDefinitions);
        }

        private static PackageDefinition CreatePackageDefinition(PackageRegistryEntry entry)
        {
            string category = entry.category != null ? entry.category.Trim() : string.Empty;

            return new PackageDefinition(
                entry.displayName,
                entry.id,
                entry.stableUrl,
                entry.description,
                entry.dependencies,
                ParsePackageType(category),
                entry.developmentUrl,
                optionalCompanions: entry.optionalCompanions,
                category: category,
                metadataType: entry.type,
                optionalIntegrations: entry.optionalIntegrations,
                integrationTargets: entry.integrationTargets,
                suiteMembers: entry.suiteMembers,
                recommendedWith: entry.recommendedWith,
                ecosystemGroup: entry.ecosystemGroup,
                groupId: entry.groupId,
                overviewOrder: entry.overviewOrder,
                searchAliases: entry.searchAliases,
                searchTags: entry.searchTags);
        }

        private static IReadOnlyList<PackageDefinition> EnsureInstallerPackageDefinition(
            IReadOnlyList<PackageDefinition> packageDefinitions)
        {
            if (packageDefinitions.Any(package => string.Equals(
                    package.PackageId,
                    "com.deucarian.package-installer",
                    StringComparison.OrdinalIgnoreCase)))
            {
                return packageDefinitions;
            }

            return packageDefinitions
                .Concat(new[] { CreateInstallerPackageDefinition() })
                .ToArray();
        }

        private static PackageDefinition CreateInstallerPackageDefinition()
        {
            return new PackageDefinition(
                "Deucarian Package Installer",
                "com.deucarian.package-installer",
                "https://github.com/Deucarian/Package-Installer.git#main",
                "Editor installer window for installing and composing Deucarian Unity UPM packages.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Package-Installer.git#develop",
                category: "Tools",
                metadataType: "Tool",
                ecosystemGroup: "Tools & Quality",
                groupId: PackageGraphHierarchyBuilder.ToolsQualityGroupId,
                overviewOrder: 20,
                searchAliases: new[] { "installer" },
                searchTags: new[] { "package-management", "upm" });
        }

        private static PackageType ParsePackageType(string category)
        {
            if (string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return PackageType.UI;
            }

            if (string.Equals(category, "Integration", StringComparison.OrdinalIgnoreCase))
            {
                return PackageType.Integration;
            }

            return PackageType.Core;
        }

        private static void StartRemoteRefresh(bool replaceExisting = false)
        {
            if (_remoteRefreshOperation != null)
            {
                if (!replaceExisting)
                {
                    return;
                }

                CancelAndObserve(_remoteRefreshOperation);
            }

            _remoteRefreshStarted = true;
            int generation = ++_remoteRefreshGeneration;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            PackageRegistryCacheCommitGuard cacheCommitGuard =
                new PackageRegistryCacheCommitGuard();
            PackageRegistryLoadResult fallback = _currentLoadResult ??
                PackageRegistryLoadResult.Failure(
                    PackageRegistrySource.Bundled,
                    "Bundled registry is unavailable.");
            Task<PackageRegistryLoadResult> task = _loader.LoadRemoteAsync(
                fallback,
                cancellation.Token,
                cacheCommitGuard);
            _remoteRefreshOperation = new RemoteRefreshOperation(
                generation,
                fallback,
                cancellation,
                cacheCommitGuard,
                task);

            EditorApplication.update -= UpdateRemoteRefresh;
            EditorApplication.update += UpdateRemoteRefresh;
        }

        private static void OnEditorQuitting()
        {
            CancelRemoteRefresh();
        }

        private static void EnsureBundledLoaded()
        {
            if (_bundledLoaded)
            {
                return;
            }

            _bundledLoaded = true;
            _remoteRefreshStarted = true;
            ApplyLoadResult(_loader.LoadBundled(), logFailures: true);

            if (_loader.TryLoadCached(
                    out PackageRegistryLoadResult cachedResult,
                    out string cacheErrorMessage))
            {
                ApplyLoadResult(cachedResult, logFailures: true);
            }
            else if (!string.IsNullOrWhiteSpace(cacheErrorMessage))
            {
                PackageInstallerLog.Registry.Warning(
                    "Cached registry was ignored: " + cacheErrorMessage);
            }

            _remoteRefreshStarted = false;
        }

        private static void UpdateRemoteRefresh()
        {
            RemoteRefreshOperation operation = _remoteRefreshOperation;

            if (operation == null || !operation.Task.IsCompleted)
            {
                return;
            }

            if (!ReferenceEquals(operation, _remoteRefreshOperation))
            {
                return;
            }

            _remoteRefreshOperation = null;
            EditorApplication.update -= UpdateRemoteRefresh;
            PackageRegistryLoadResult result;

            try
            {
                result = operation.Task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                result = PackageRegistryLoadResult.RemoteFailureUsingFallback(
                    operation.Fallback,
                    exception.GetBaseException().Message);
            }
            finally
            {
                operation.Cancellation.Dispose();
            }

            if (ShouldApplyRemoteRefresh(operation.Generation, _remoteRefreshGeneration))
            {
                ApplyLoadResult(result, logFailures: true);
            }
        }

        private static void ApplyLoadResult(PackageRegistryLoadResult result, bool logFailures)
        {
            if (result == null)
            {
                return;
            }

            _currentLoadResult = result;

            if (result.IsValid && result.Registry != null)
            {
                _allPackages = CreatePackageDefinitions(result.Registry);
                // Registry reloads are the invalidation point for package ID lookup and graph structure caches.
                _packageById = CreatePackageById(_allPackages);
                _ecosystemGroups = PackageGraphHierarchyBuilder.CreateGroups(result.Registry.groups);
            }
            else if (_allPackages == null)
            {
                _allPackages = EmptyPackages;
                _packageById = CreatePackageById(_allPackages);
                _ecosystemGroups = PackageGraphHierarchyBuilder.CreateGroups((IEnumerable<PackageGraphGroup>)null);
            }

            if (!result.IsValid && logFailures)
            {
                PackageInstallerLog.Registry.Warning("Registry load failed: " + result.ErrorMessage);
            }
            else if (result.Source == PackageRegistrySource.RemoteFailedUsingBundled && logFailures)
            {
                PackageInstallerLog.Registry.Warning("Remote registry failed, using bundled registry: " + result.ErrorMessage);
            }
            else if (result.Source == PackageRegistrySource.RemoteFailedUsingCache && logFailures)
            {
                PackageInstallerLog.Registry.Warning("Remote registry failed, using cached registry: " + result.ErrorMessage);
            }

            RegistryChanged?.Invoke();
        }

        internal static bool ShouldApplyRemoteRefreshForTests(
            int completedGeneration,
            int activeGeneration)
        {
            return ShouldApplyRemoteRefresh(completedGeneration, activeGeneration);
        }

        internal static void SetLoaderForTests(PackageRegistryLoader loader)
        {
            ResetState(loader ?? new PackageRegistryLoader());
        }

        internal static void PollRemoteRefreshForTests()
        {
            UpdateRemoteRefresh();
        }

        internal static void ResetForTests()
        {
            ResetState(new PackageRegistryLoader());
        }

        private static bool ShouldApplyRemoteRefresh(
            int completedGeneration,
            int activeGeneration)
        {
            return completedGeneration == activeGeneration;
        }

        private static void ResetState(PackageRegistryLoader loader)
        {
            EditorApplication.update -= UpdateRemoteRefresh;

            if (_remoteRefreshOperation != null)
            {
                CancelAndObserve(_remoteRefreshOperation);
                _remoteRefreshOperation = null;
            }

            _loader = loader;
            _currentLoadResult = null;
            _allPackages = EmptyPackages;
            _packageById = new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
            _ecosystemGroups = PackageGraphHierarchyBuilder.CreateGroups(
                (IEnumerable<PackageGraphGroup>)null);
            _remoteRefreshGeneration = 0;
            _bundledLoaded = false;
            _remoteRefreshStarted = false;
        }

        private static void CancelAndObserve(RemoteRefreshOperation operation)
        {
            operation.Cancellation.Cancel();
            operation.CacheCommitGuard.Revoke();
            operation.Task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        Exception ignored = completed.Exception;
                    }

                    operation.Cancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private sealed class RemoteRefreshOperation
        {
            public RemoteRefreshOperation(
                int generation,
                PackageRegistryLoadResult fallback,
                CancellationTokenSource cancellation,
                PackageRegistryCacheCommitGuard cacheCommitGuard,
                Task<PackageRegistryLoadResult> task)
            {
                Generation = generation;
                Fallback = fallback;
                Cancellation = cancellation;
                CacheCommitGuard = cacheCommitGuard;
                Task = task;
            }

            public int Generation { get; }

            public PackageRegistryLoadResult Fallback { get; }

            public CancellationTokenSource Cancellation { get; }

            public PackageRegistryCacheCommitGuard CacheCommitGuard { get; }

            public Task<PackageRegistryLoadResult> Task { get; }
        }

        private static IReadOnlyDictionary<string, PackageDefinition> CreatePackageById(
            IEnumerable<PackageDefinition> packages)
        {
            Dictionary<string, PackageDefinition> packageById =
                new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageDefinition packageDefinition in packages ?? EmptyPackages)
            {
                if (packageDefinition != null && !string.IsNullOrWhiteSpace(packageDefinition.PackageId))
                {
                    packageById[packageDefinition.PackageId.Trim()] = packageDefinition;
                }
            }

            return packageById;
        }

        private static int GetCategorySortIndex(string category)
        {
            if (string.Equals(category, "Core", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(category, "Tools", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(category, "Integration", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            if (string.Equals(category, "Suites", StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            if (string.Equals(category, "Templates", StringComparison.OrdinalIgnoreCase))
            {
                return 6;
            }

            return 7;
        }
    }
}
