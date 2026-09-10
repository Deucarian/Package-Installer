using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // The journal is authoritative even when its page is closed or Unity has reloaded.
    internal static class DevelopmentPackageProtection
    {
        internal const string Message = "Local development: restore the original source in Package development before updating or removing this package.";

        internal static bool IsProtected(string packageId, string projectRoot = null)
        {
            if (string.IsNullOrEmpty(packageId)) return false;
            try
            {
                var store = new PackageDevelopmentSessionStore(projectRoot ?? Path.GetDirectoryName(Application.dataPath),
                    PackageInstallerAtomicFileCommitter.Shared);
                return store.LoadAll().Any(session => session.PackageId == packageId && session.IsManaged);
            }
            catch (Exception)
            {
                // An unreadable journal cannot authorize changing package sources.
                return true;
            }
        }

        internal static PackageUpdateStatus Status(PackageDefinition package, PackageChannel channel)
            => PackageUpdateStatus.CannotDetermine(package, channel, package.GetUrl(channel), "", Message);

        internal static PackageUpdateStatus ResolveStatus(PackageDefinition package, PackageChannel channel,
            IReadOnlyDictionary<string, PackageUpdateStatus> statuses, Func<string, bool> installed, string projectRoot = null)
        {
            if (package == null) return PackageUpdateStatus.Unknown(null, channel);
            if (IsProtected(package.PackageId, projectRoot)) return Status(package, channel);
            string url = package.GetUrl(channel);
            if (statuses.TryGetValue(package.PackageId, out var status) && status.Channel == channel &&
                string.Equals(status.SelectedUrl, url, StringComparison.Ordinal)) return status;
            return installed(package.PackageId) ? PackageUpdateStatus.Unknown(package, channel)
                : PackageUpdateStatus.NotInstalled(package, channel, url);
        }

        internal static IPackageInstallRequest Add(IPackageInstallClient client, string packageId, string reference,
            string projectRoot = null)
        {
            if (IsProtected(packageId, projectRoot)) throw new InvalidOperationException(Message);
            return client.Add(reference);
        }

        internal static IPackageInstallRequest Remove(IPackageInstallClient client, string packageId, string projectRoot = null)
        {
            if (IsProtected(packageId, projectRoot)) throw new InvalidOperationException(Message);
            return client.Remove(packageId);
        }
    }
}
