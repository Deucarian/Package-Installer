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
        private static PackageRegistryLoader _loader = new PackageRegistryLoader();
        private static readonly IReadOnlyList<PackageDefinition> EmptyPackages =
            Array.Empty<PackageDefinition>();

        private static PackageRegistryLoadResult _currentLoadResult;
        private static IReadOnlyList<PackageDefinition> _allPackages = EmptyPackages;
        private static IReadOnlyList<string> _orderedNavigationGroups = Array.Empty<string>();
        private static IReadOnlyDictionary<string, PackageDefinition> _packageById =
            new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<PackageGraphGroup> _ecosystemGroups =
            PackageGraphHierarchyBuilder.CreateGroups((IEnumerable<PackageGraphGroup>)null);
        private static RemoteRefreshOperation _remoteRefreshOperation;
        private static int _remoteRefreshGeneration;
        private static bool _bundledLoaded;
        private static bool _remoteRefreshStarted;
        internal static bool IsLocalLoading => _loader.LocalLoad != null;

        internal static void LoadInBackground()
        {
            if (_bundledLoaded) { EnsureLoaded(); return; }
            if (IsLocalLoading) return;
            _loader.LocalLoad = new PackageRegistryLocalLoad(_loader, PackageRegistryLoader.ResolveBundledRegistryPath());
            EditorApplication.update += UpdateLocalLoad;
        }

        private static void UpdateLocalLoad()
        {
            if (!IsLocalLoading || !_loader.LocalLoad.TryComplete(out var snapshot)) return;
            _loader.LocalLoad = null;
            EditorApplication.update -= UpdateLocalLoad;
            ApplyLocalSnapshot(snapshot);
            StartRemoteRefresh();
        }

        private static void ApplyLocalSnapshot(PackageRegistryLocalLoad.Snapshot snapshot)
        {
            _bundledLoaded = true;
            _remoteRefreshStarted = true;
            if (!string.IsNullOrWhiteSpace(snapshot.Warning))
                PackageInstallerLog.Registry.Warning("Cached registry was ignored: " + snapshot.Warning);
            ApplyLoadResult(snapshot.Result, logFailures: true);
            _remoteRefreshStarted = false;
        }

        private static void AbandonLocalLoad()
        {
            EditorApplication.update -= UpdateLocalLoad;
            _loader.LocalLoad?.Abandon();
            _loader.LocalLoad = null;
        }

        public static IReadOnlyList<PackageDefinition> StandalonePackages =>
            All.Where(package => !package.IsIntegration).ToArray();

        public static IReadOnlyList<PackageDefinition> IntegrationPackages =>
            All.Where(package => package.Kind == PackageKind.Integration).ToArray();

        public static bool IsRemoteRefreshing => _remoteRefreshOperation != null;

        public static string StatusMessage
        {
            get
            {
                if (IsLocalLoading) return "Loading package catalog…";
                PackageRegistryLoadResult result = CurrentLoadResult;
                return result != null ? result.StatusMessage : "Using bundled registry";
            }
        }
    }
}
