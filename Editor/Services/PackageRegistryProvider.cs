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

        public static IReadOnlyList<PackageDefinition> StandalonePackages =>
            All.Where(package => !package.IsIntegration).ToArray();

        public static IReadOnlyList<PackageDefinition> IntegrationPackages =>
            All.Where(package => package.Kind == PackageKind.Integration).ToArray();

        public static bool IsRemoteRefreshing => _remoteRefreshOperation != null;

        public static string StatusMessage
        {
            get
            {
                PackageRegistryLoadResult result = CurrentLoadResult;
                return result != null ? result.StatusMessage : "Using bundled registry";
            }
        }
    }
}
