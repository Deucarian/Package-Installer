using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageDetectionService : IDisposable
    {
        private readonly Dictionary<string, PackageManagerPackageInfo> _installedPackages =
            new Dictionary<string, PackageManagerPackageInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageReferences =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageInstallSourceType> _installedPackageSourceTypes =
            new Dictionary<string, PackageInstallSourceType>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageVersions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;
        private readonly PackageInstallerStateRepository _stateRepository;
        private readonly IPackageListClient _packageListClient;

        private IPackageListRequest _listRequest;
        private bool _refreshRetryScheduled;
        private bool _manifestRefreshCheckScheduled;
        private string _lastManifestStateSignature;

        public bool IsRefreshing => _listRequest != null;

        public IReadOnlyCollection<string> InstalledPackageIds =>
            new List<string>(_installedPackages.Keys);
    }
}
