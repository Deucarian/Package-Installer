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


        private static void ApplyLoadResult(PackageRegistryLoadResult result, bool logFailures)
        {
            if (result == null)
            {
                return;
            }

            _currentLoadResult = result;

            if (result.IsValid && result.Registry != null)
            {
                _ecosystemGroups = PackageGraphHierarchyBuilder.CreateGroups(result.Registry.groups);
                _allPackages = CreatePackageDefinitions(result.Registry);
                _orderedNavigationGroups = CreateOrderedNavigationGroups(
                    _allPackages,
                    _ecosystemGroups);
                // Registry reloads are the invalidation point for package ID lookup and graph structure caches.
                _packageById = CreatePackageById(_allPackages);
            }
            else if (_allPackages == null)
            {
                _allPackages = EmptyPackages;
                _orderedNavigationGroups = Array.Empty<string>();
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
            _orderedNavigationGroups = Array.Empty<string>();
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

        internal static IReadOnlyList<string> CreateOrderedNavigationGroups(
            IEnumerable<PackageDefinition> packages,
            IEnumerable<PackageGraphGroup> groups)
        {
            PackageDefinition[] definitions = (packages ?? EmptyPackages)
                .Where(package => package != null)
                .ToArray();
            IReadOnlyList<PackageGraphGroup> orderedGroups =
                PackageGraphHierarchyBuilder.CreateGroups(groups);
            string[] orderedPaths = orderedGroups
                .Select(group => PackageGraphHierarchyBuilder.GetGroupPath(orderedGroups, group.Id))
                .Where(path => definitions.Any(package => string.Equals(
                    package.NavigationGroup,
                    path,
                    StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return orderedPaths
                .Concat(definitions
                    .Select(package => package.NavigationGroup)
                    .Where(group => !string.IsNullOrWhiteSpace(group) &&
                                    !orderedPaths.Contains(group, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
