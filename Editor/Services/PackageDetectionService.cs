using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageDetectionService : IDisposable
    {
        private readonly Dictionary<string, PackageManagerPackageInfo> _installedPackages =
            new Dictionary<string, PackageManagerPackageInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageReferences =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PackageInstallSourceType> _installedPackageSourceTypes =
            new Dictionary<string, PackageInstallSourceType>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _installedPackageVersions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<string> _packageLockPaths;
        private readonly PackageInstallerStateRepository _stateRepository;

        private ListRequest _listRequest;
        private bool _refreshRetryScheduled;
        private bool _manifestRefreshCheckScheduled;
        private string _lastManifestStateSignature;

        public PackageDetectionService()
        {
            _packageLockPaths = GetPackageLockPaths();
            _stateRepository = new PackageInstallerStateRepository();
            _lastManifestStateSignature = _stateRepository.GetManifestStateSignature();
            EditorApplication.projectChanged += HandleProjectChanged;
        }

        public event Action StateChanged;

        public event Action RefreshCompleted;

        public bool IsRefreshing => _listRequest != null && !_listRequest.IsCompleted;

        public void Refresh()
        {
            if (IsRefreshing)
            {
                return;
            }

            try
            {
                _listRequest = Client.List(true, true);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                NotifyStateChanged();
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Registry.Error("Failed to start installed-package refresh: " + exception.Message);
                _listRequest = null;
                ScheduleRefreshRetry();
                NotifyStateChanged();
            }
        }

        public bool RefreshIfManifestStateChanged()
        {
            // Unity package state is owned by the project manifest and package lock files.
            // Their signature is the cheap invalidation gate before we ask UPM for a fresh list.
            string currentSignature = _stateRepository.GetManifestStateSignature();

            if (!HasManifestStateChanged(_lastManifestStateSignature, currentSignature))
            {
                return false;
            }

            _lastManifestStateSignature = currentSignature;
            Refresh();
            return true;
        }

        public bool IsInstalled(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _installedPackages.ContainsKey(packageId);
        }

        internal void ReplaceInstalledPackageNamesForTests(IEnumerable<string> packageIds)
        {
            _installedPackages.Clear();
            _installedPackageReferences.Clear();
            _installedPackageSourceTypes.Clear();
            _installedPackageVersions.Clear();

            if (packageIds == null)
            {
                return;
            }

            foreach (string packageId in packageIds)
            {
                if (!string.IsNullOrWhiteSpace(packageId))
                {
                    _installedPackages[packageId.Trim()] = null;
                }
            }
        }

        internal void ReplaceInstalledPackageReferenceForTests(string packageId, string packageReference)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            _installedPackages[packageId.Trim()] = null;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                _installedPackageReferences.Remove(packageId.Trim());
                _installedPackageSourceTypes[packageId.Trim()] = PackageInstallSourceType.Unknown;
                return;
            }

            _installedPackageReferences[packageId.Trim()] = packageReference.Trim();
            _installedPackageSourceTypes[packageId.Trim()] = PackageInstallSourceUtility.Detect(
                string.Empty,
                string.Empty,
                packageReference,
                string.Empty);
        }

        internal void ReplaceInstalledPackageForTests(
            string packageId,
            string packageReference,
            PackageInstallSourceType sourceType,
            string version = "")
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            string normalizedPackageId = packageId.Trim();
            _installedPackages[normalizedPackageId] = null;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                _installedPackageReferences.Remove(normalizedPackageId);
            }
            else
            {
                _installedPackageReferences[normalizedPackageId] = packageReference.Trim();
            }

            _installedPackageSourceTypes[normalizedPackageId] = sourceType;

            if (string.IsNullOrWhiteSpace(version))
            {
                _installedPackageVersions.Remove(normalizedPackageId);
            }
            else
            {
                _installedPackageVersions[normalizedPackageId] = version.Trim();
            }
        }

        public bool TryGetInstalledPackage(string packageId, out PackageManagerPackageInfo packageInfo)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                packageInfo = null;
                return false;
            }

            return _installedPackages.TryGetValue(packageId, out packageInfo);
        }

        public bool TryGetInstalledPackageReference(string packageId, out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            return _installedPackageReferences.TryGetValue(packageId, out packageReference) &&
                   !string.IsNullOrWhiteSpace(packageReference);
        }

        public bool TryGetInstalledPackageSourceType(string packageId, out PackageInstallSourceType sourceType)
        {
            sourceType = PackageInstallSourceType.Unknown;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            return _installedPackageSourceTypes.TryGetValue(packageId, out sourceType);
        }

        public bool TryGetInstalledPackageVersion(string packageId, out string version)
        {
            version = string.Empty;

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            return _installedPackageVersions.TryGetValue(packageId, out version) &&
                   !string.IsNullOrWhiteSpace(version);
        }

        public bool TryGetInstalledPackageChannel(
            PackageDefinition packageDefinition,
            out PackageChannel channel,
            out string packageReference)
        {
            channel = PackageChannel.Stable;
            packageReference = string.Empty;

            if (packageDefinition == null)
            {
                return false;
            }

            bool hasInstalledPackageReference = TryGetInstalledPackageReference(
                packageDefinition.PackageId,
                out packageReference);

            if (TryGetInstalledPackageSourceType(
                    packageDefinition.PackageId,
                    out PackageInstallSourceType sourceType) &&
                sourceType == PackageInstallSourceType.Registry)
            {
                if (TryGetInstalledPackageVersion(
                        packageDefinition.PackageId,
                        out string installedVersion) &&
                    IsDevelopmentRegistryVersion(installedVersion))
                {
                    channel = PackageChannel.Development;
                }
                else
                {
                    channel = PackageChannel.Stable;
                }

                return true;
            }

            if (!hasInstalledPackageReference)
            {
                return false;
            }

            if (TryGetReferenceName(packageReference, out string installedReferenceName))
            {
                if (string.Equals(installedReferenceName, "develop", StringComparison.OrdinalIgnoreCase) &&
                    ReferenceMatchesChannel(packageReference, packageDefinition.DevelopmentUrl))
                {
                    channel = PackageChannel.Development;
                    return true;
                }

                if (string.Equals(installedReferenceName, "main", StringComparison.OrdinalIgnoreCase) &&
                    ReferenceMatchesChannel(packageReference, packageDefinition.StableUrl))
                {
                    channel = PackageChannel.Stable;
                    return true;
                }
            }

            if (ReferenceMatchesChannel(packageReference, packageDefinition.DevelopmentUrl))
            {
                channel = PackageChannel.Development;
                return true;
            }

            if (ReferenceMatchesChannel(packageReference, packageDefinition.StableUrl))
            {
                channel = PackageChannel.Stable;
                return true;
            }

            channel = PackageChannel.Custom;
            return true;
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
            EditorApplication.delayCall -= RetryRefresh;
            EditorApplication.delayCall -= CheckManifestRefresh;
            EditorApplication.projectChanged -= HandleProjectChanged;
        }

        private void Update()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            if (_listRequest.Status == StatusCode.Success)
            {
                _installedPackages.Clear();
                _installedPackageReferences.Clear();
                _installedPackageSourceTypes.Clear();
                _installedPackageVersions.Clear();

                foreach (PackageManagerPackageInfo packageInfo in _listRequest.Result)
                {
                    if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.name))
                    {
                        _installedPackages[packageInfo.name] = packageInfo;

                        if (TryReadPackageLockReference(packageInfo.name, out string packageReference) ||
                            TryExtractReferenceFromPackageManagerPackageId(packageInfo.packageId, packageInfo.name, out packageReference))
                        {
                            _installedPackageReferences[packageInfo.name] = packageReference;
                        }

                        PackageInstallSourceType sourceType = PackageInstallSourceUtility.Detect(
                            packageInfo.source.ToString(),
                            packageInfo.packageId,
                            packageReference,
                            packageInfo.resolvedPath);
                        _installedPackageSourceTypes[packageInfo.name] = sourceType;

                        if (!string.IsNullOrWhiteSpace(packageInfo.version))
                        {
                            _installedPackageVersions[packageInfo.name] = packageInfo.version.Trim();
                        }
                        else if (PackageInstallSourceUtility.TryExtractRegistryVersion(
                                     packageInfo.packageId,
                                     packageInfo.name,
                                     out string packageIdVersion))
                        {
                            _installedPackageVersions[packageInfo.name] = packageIdVersion;
                        }
                        else if (PackageInstallSourceUtility.TryExtractRegistryVersion(
                                     packageReference,
                                     packageInfo.name,
                                     out string packageReferenceVersion))
                        {
                            _installedPackageVersions[packageInfo.name] = packageReferenceVersion;
                        }
                    }
                }

                _lastManifestStateSignature = _stateRepository.GetManifestStateSignature();
            }
            else
            {
                string errorMessage = _listRequest.Error != null
                    ? _listRequest.Error.message
                    : "Package Manager returned an unknown error.";

                PackageInstallerLog.Registry.Error("Failed to refresh installed-package state: " + errorMessage);
            }

            _listRequest = null;
            EditorApplication.update -= Update;
            NotifyStateChanged();
            RefreshCompleted?.Invoke();
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private void ScheduleRefreshRetry()
        {
            if (_refreshRetryScheduled)
            {
                return;
            }

            _refreshRetryScheduled = true;
            EditorApplication.delayCall += RetryRefresh;
        }

        private void RetryRefresh()
        {
            EditorApplication.delayCall -= RetryRefresh;
            _refreshRetryScheduled = false;
            Refresh();
        }

        private void HandleProjectChanged()
        {
            if (_manifestRefreshCheckScheduled)
            {
                return;
            }

            _manifestRefreshCheckScheduled = true;
            EditorApplication.delayCall += CheckManifestRefresh;
        }

        private void CheckManifestRefresh()
        {
            EditorApplication.delayCall -= CheckManifestRefresh;
            _manifestRefreshCheckScheduled = false;
            RefreshIfManifestStateChanged();
        }

        internal static bool HasManifestStateChangedForTests(string previousSignature, string currentSignature)
        {
            return HasManifestStateChanged(previousSignature, currentSignature);
        }

        private static bool HasManifestStateChanged(string previousSignature, string currentSignature)
        {
            return !string.Equals(
                previousSignature ?? string.Empty,
                currentSignature ?? string.Empty,
                StringComparison.Ordinal);
        }

        private bool TryReadPackageLockReference(string packageId, out string packageReference)
        {
            packageReference = string.Empty;

            foreach (string packageLockPath in _packageLockPaths)
            {
                if (TryReadPackageLockReference(packageLockPath, packageId, out packageReference))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadPackageLockReference(
            string packageLockPath,
            string packageId,
            out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageLockPath) || !File.Exists(packageLockPath))
            {
                return false;
            }

            return PackageLockJsonReader.TryReadPackageStringField(
                packageLockPath,
                packageId,
                "version",
                out packageReference);
        }

        private static bool TryExtractReferenceFromPackageManagerPackageId(
            string packageManagerPackageId,
            string packageId,
            out string packageReference)
        {
            packageReference = string.Empty;

            if (string.IsNullOrWhiteSpace(packageManagerPackageId) ||
                string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            string prefix = packageId + "@";

            if (!packageManagerPackageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            packageReference = packageManagerPackageId.Substring(prefix.Length).Trim();
            return !string.IsNullOrWhiteSpace(packageReference);
        }

        private static bool ReferenceMatchesChannel(string installedReference, string channelUrl)
        {
            if (string.IsNullOrWhiteSpace(installedReference) || string.IsNullOrWhiteSpace(channelUrl))
            {
                return false;
            }

            if (string.Equals(
                    NormalizePackageReference(installedReference),
                    NormalizePackageReference(channelUrl),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryGetReferenceName(installedReference, out string installedReferenceName) &&
                   TryGetReferenceName(channelUrl, out string channelReferenceName) &&
                   string.Equals(
                       NormalizeReferenceName(installedReferenceName),
                       NormalizeReferenceName(channelReferenceName),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetReferenceName(string packageReference, out string referenceName)
        {
            referenceName = string.Empty;

            if (string.IsNullOrWhiteSpace(packageReference))
            {
                return false;
            }

            int hashIndex = packageReference.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == packageReference.Length - 1)
            {
                return false;
            }

            referenceName = packageReference.Substring(hashIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(referenceName);
        }

        private static string NormalizePackageReference(string packageReference)
        {
            return (packageReference ?? string.Empty).Trim();
        }

        private static string NormalizeReferenceName(string referenceName)
        {
            referenceName = (referenceName ?? string.Empty).Trim();

            const string refsHeadsPrefix = "refs/heads/";
            const string headsPrefix = "heads/";
            const string originPrefix = "origin/";

            if (referenceName.StartsWith(refsHeadsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return referenceName.Substring(refsHeadsPrefix.Length);
            }

            if (referenceName.StartsWith(headsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return referenceName.Substring(headsPrefix.Length);
            }

            if (referenceName.StartsWith(originPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return referenceName.Substring(originPrefix.Length);
            }

            return referenceName;
        }

        private static bool IsDevelopmentRegistryVersion(string version)
        {
            return !string.IsNullOrWhiteSpace(version) &&
                   version.IndexOf("-dev.", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IReadOnlyList<string> GetPackageLockPaths()
        {
            string projectRoot = Directory.GetParent(Application.dataPath) != null
                ? Directory.GetParent(Application.dataPath).FullName
                : Application.dataPath;

            string packagesDirectory = Path.Combine(projectRoot, "Packages");

            return new[]
            {
                Path.Combine(packagesDirectory, "packages-lock.json"),
                Path.Combine(packagesDirectory, "package-lock.json")
            };
        }
    }
}
