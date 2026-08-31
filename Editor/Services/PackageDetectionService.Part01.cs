using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageDetectionService
    {


        public PackageDetectionService()
            : this(new UnityPackageListClient())
        {
        }

        internal PackageDetectionService(IPackageListClient packageListClient)
        {
            _packageListClient = packageListClient ??
                                 throw new ArgumentNullException(nameof(packageListClient));
            _packageLockPaths = GetPackageLockPaths();
            _stateRepository = new PackageInstallerStateRepository();
            _lastManifestStateSignature = _stateRepository.GetManifestStateSignature();
            EditorApplication.projectChanged += HandleProjectChanged;
        }

        public event Action StateChanged;

        public event Action RefreshCompleted;

        public bool HasSuccessfulRefresh { get; private set; }

        public void Refresh()
        {
            if (IsRefreshing)
            {
                return;
            }

            try
            {
                HasSuccessfulRefresh = false;
                _listRequest = _packageListClient.List(
                    offlineMode: true,
                    includeIndirectDependencies: true);
                if (_listRequest == null)
                {
                    throw new InvalidOperationException("Package Manager returned no list request.");
                }

                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                NotifyStateChanged();
            }
            catch (Exception exception)
            {
                string message = "Failed to start installed-package refresh: " + exception.Message;
                PackageInstallerLog.Registry.Error(message);
                PackageInstallerActivityService.Record(
                    "Installed Packages",
                    PackageInstallerActivitySeverity.Error,
                    message,
                    retryKind: PackageInstallerRetryKind.Refresh);
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
            _installedPackageHashes.Clear();

            if (packageIds == null)
            {
                return;
            }

            foreach (string packageId in packageIds)
            {
                if (!string.IsNullOrWhiteSpace(packageId))
                {
                    string normalizedPackageId = packageId.Trim();
                    _installedPackages[normalizedPackageId] = null;
                    _installedPackageSourceTypes[normalizedPackageId] =
                        PackageInstallSourceType.Registry;
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
            string version = "",
            string packageHash = "")
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

            if (string.IsNullOrWhiteSpace(packageHash))
            {
                _installedPackageHashes.Remove(normalizedPackageId);
            }
            else
            {
                _installedPackageHashes[normalizedPackageId] = packageHash.Trim();
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

        public bool IsInstalledAtExactTarget(string packageId, string targetReference)
        {
            if (!IsInstalled(packageId) || string.IsNullOrWhiteSpace(targetReference) ||
                !TryGetInstalledPackageReference(packageId, out string installedReference))
            {
                return false;
            }

            if (PackageGitReference.TryParse(targetReference, out _))
            {
                return PackageGitReference.MatchesChannel(installedReference, targetReference);
            }

            return string.Equals(
                installedReference.Trim(),
                targetReference.Trim(),
                StringComparison.Ordinal);
        }

        public string GetInstalledIdentity(string packageId)
        {
            if (!IsInstalled(packageId))
            {
                return string.Empty;
            }

            _installedPackageReferences.TryGetValue(packageId, out string packageReference);
            _installedPackageVersions.TryGetValue(packageId, out string version);
            _installedPackageHashes.TryGetValue(packageId, out string packageHash);
            _installedPackageSourceTypes.TryGetValue(packageId, out PackageInstallSourceType sourceType);
            return sourceType + "|" +
                   (packageReference ?? string.Empty).Trim() + "|" +
                   (version ?? string.Empty).Trim() + "|" +
                   (packageHash ?? string.Empty).Trim();
        }

        public bool IsInstalledAtExactTargetAfterChange(
            string packageId,
            string targetReference,
            string previousInstalledIdentity)
        {
            if (!IsInstalledAtExactTarget(packageId, targetReference))
            {
                return false;
            }

            string currentIdentity = GetInstalledIdentity(packageId);
            return !string.IsNullOrWhiteSpace(currentIdentity) &&
                   !string.Equals(
                       currentIdentity,
                       previousInstalledIdentity ?? string.Empty,
                       StringComparison.Ordinal);
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

            bool hasInstalledSourceType = TryGetInstalledPackageSourceType(
                packageDefinition.PackageId,
                out PackageInstallSourceType sourceType);

            if (hasInstalledSourceType &&
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

            if (hasInstalledSourceType &&
                (sourceType == PackageInstallSourceType.Local ||
                 sourceType == PackageInstallSourceType.Embedded))
            {
                packageReference = packageReference ?? string.Empty;
                channel = PackageChannel.Custom;
                return true;
            }

            if (!hasInstalledPackageReference)
            {
                packageReference = string.Empty;
                if (IsInstalled(packageDefinition.PackageId))
                {
                    channel = PackageChannel.Custom;
                    return true;
                }

                return false;
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
    }
}
