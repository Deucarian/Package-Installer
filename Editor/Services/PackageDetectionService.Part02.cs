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


        private void Update()
        {
            if (_listRequest == null || !_listRequest.IsCompleted)
            {
                return;
            }

            if (_listRequest.IsSuccess)
            {
                HasSuccessfulRefresh = true;
                _installedPackages.Clear();
                _installedPackageReferences.Clear();
                _installedPackageSourceTypes.Clear();
                _installedPackageVersions.Clear();
                _installedPackageHashes.Clear();

                foreach (PackageManagerPackageInfo packageInfo in
                         _listRequest.Packages ?? Array.Empty<PackageManagerPackageInfo>())
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

                        if (TryReadPackageLockField(
                                packageInfo.name,
                                "hash",
                                out string packageHash))
                        {
                            _installedPackageHashes[packageInfo.name] = packageHash;
                        }
                    }
                }

                _lastManifestStateSignature = _stateRepository.GetManifestStateSignature();
            }
            else
            {
                string errorMessage = string.IsNullOrWhiteSpace(_listRequest.ErrorMessage)
                    ? "Package Manager returned an unknown error."
                    : _listRequest.ErrorMessage;

                PackageInstallerLog.Registry.Error("Failed to refresh installed-package state: " + errorMessage);
                HasSuccessfulRefresh = false;
                PackageInstallerActivityService.Record(
                    "Installed Packages",
                    PackageInstallerActivitySeverity.Error,
                    "Failed to refresh installed-package state.",
                    errorMessage,
                    retryKind: PackageInstallerRetryKind.Refresh);
            }

            _listRequest = null;
            EditorApplication.update -= Update;
            NotifyStateChanged();
            RefreshCompleted?.Invoke();
        }

        internal void UpdateForTests()
        {
            Update();
        }

        private void NotifyStateChanged()
        {
            PackageInstallerEditorStatus.PublishInstalledPackages(
                _installedPackages.Count,
                HasSuccessfulRefresh,
                IsRefreshing);
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
            return TryReadPackageLockField(packageId, "version", out packageReference);
        }

        private bool TryReadPackageLockField(
            string packageId,
            string fieldName,
            out string value)
        {
            value = string.Empty;

            foreach (string packageLockPath in _packageLockPaths)
            {
                if (PackageLockJsonReader.TryReadPackageStringField(
                        packageLockPath,
                        packageId,
                        fieldName,
                        out value))
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
            return PackageGitReference.MatchesChannel(installedReference, channelUrl);
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
