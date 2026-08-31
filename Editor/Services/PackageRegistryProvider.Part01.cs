using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal static partial class PackageRegistryProvider
    {


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

        public static IReadOnlyList<PackageGraphGroup> EcosystemGroups
        {
            get
            {
                EnsureLoaded();
                return _ecosystemGroups;
            }
        }

        public static IReadOnlyList<string> Categories
        {
            get
            {
                EnsureLoaded();
                return _orderedNavigationGroups;
            }
        }

        public static PackageRegistryLoadResult CurrentLoadResult
        {
            get
            {
                EnsureLoaded();
                return _currentLoadResult;
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
                    package.NavigationGroup,
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

            IReadOnlyList<PackageGraphGroup> groups =
                PackageGraphHierarchyBuilder.CreateGroups(registry.groups);
            PackageDefinition[] packageDefinitions = registry.packages
                .Where(entry => entry != null)
                .Select(entry => CreatePackageDefinition(
                    entry,
                    groups,
                    ResolveOptionalCompanionIds(entry, registry.packages)))
                .ToArray();

            return EnsureInstallerPackageDefinition(packageDefinitions);
        }

        private static PackageDefinition CreatePackageDefinition(
            PackageRegistryEntry entry,
            IReadOnlyList<PackageGraphGroup> groups,
            IEnumerable<string> optionalCompanionIds)
        {
            string category = entry.category != null ? entry.category.Trim() : string.Empty;
            PackageKind kind = PackageKindParser.Parse(entry.kind, entry.type, category);
            string navigationGroup = PackageGraphHierarchyBuilder.GetGroupPath(
                groups,
                entry.groupId);

            return new PackageDefinition(
                entry.displayName,
                entry.id,
                entry.stableUrl,
                entry.description,
                entry.dependencies,
                kind,
                entry.developmentUrl,
                optionalCompanions: optionalCompanionIds,
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
                searchTags: entry.searchTags,
                navigationGroup: navigationGroup,
                iconKey: entry.iconKey,
                compositionPresets: CreateCompositionPresets(entry.compositionPresets));
        }

        private static IEnumerable<string> ResolveOptionalCompanionIds(
            PackageRegistryEntry target,
            IEnumerable<PackageRegistryEntry> entries)
        {
            IEnumerable<string> declared = target.optionalCompanions ?? Array.Empty<string>();
            IEnumerable<string> derived = (entries ?? Array.Empty<PackageRegistryEntry>())
                .Where(candidate => candidate != null &&
                                    (candidate.recommendedWith ?? Array.Empty<string>())
                                    .Any(targetId => string.Equals(
                                        targetId?.Trim(),
                                        target.id?.Trim(),
                                        StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => candidate.id);

            return declared
                .Concat(derived)
                .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                .Select(packageId => packageId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<PackageCompositionPresetDefinition> CreateCompositionPresets(
            IEnumerable<PackageCompositionPresetEntry> entries)
        {
            return (entries ?? Array.Empty<PackageCompositionPresetEntry>())
                .Where(entry => entry != null)
                .Select(entry => new PackageCompositionPresetDefinition(
                    entry.id,
                    entry.displayName,
                    entry.description,
                    entry.packageIds,
                    entry.recommended));
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
                PackageKind.Tool,
                "https://github.com/Deucarian/Package-Installer.git#develop",
                category: "Tools",
                metadataType: "Tool",
                ecosystemGroup: "Tools & Quality",
                groupId: PackageGraphHierarchyBuilder.ToolsQualityGroupId,
                overviewOrder: 20,
                searchAliases: new[] { "installer" },
                searchTags: new[] { "package-management", "upm" },
                navigationGroup: "Tools & Quality",
                iconKey: "package-plus");
        }

        internal static PackageRegistryCachedStatus CaptureCachedStatus()
        {
            // This path deliberately loads only bundled/cached local metadata.
            // Remote refresh remains an explicit Package Installer operation.
            EnsureBundledLoaded();
            return new PackageRegistryCachedStatus(
                _currentLoadResult != null && _currentLoadResult.IsValid,
                _allPackages.Count,
                _remoteRefreshOperation != null);
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
    }
}
