using System;
using System.Collections.Generic;
using Deucarian.Editor;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    [InitializeOnLoad]
    internal static class PackageInstallerControlCenterIntegration
    {
        private const string PackageId = "com.deucarian.package-installer";

        static PackageInstallerControlCenterIntegration()
        {
            DeucarianToolRegistry.Register(new DeucarianToolDescriptor(
                DeucarianToolIds.PackageInstaller,
                "Package Installer",
                "Browse, install, update, and compose Deucarian packages.",
                DeucarianControlCenterArea.BuildAndPackages,
                PackageInstallerWindow.Open,
                PackageId,
                "package-plus",
                new[] { "upm", "packages", "dependencies", "updates" },
                10));
            DeucarianControlCenterRegistry.RegisterCardProvider(
                new PackageInstallerCardProvider());
        }
    }

    internal sealed class PackageInstallerCardProvider :
        IDeucarianControlCenterCardProvider
    {
        public string Id => "com.deucarian.package-installer.status";

        public IEnumerable<DeucarianControlCenterCard> Capture(
            DeucarianControlCenterContext context)
        {
            yield return CreateCard(PackageInstallerEditorStatus.Capture());
        }

        internal static DeucarianControlCenterCard CreateCard(
            PackageInstallerEditorSnapshot snapshot)
        {
            DeucarianControlCenterStatus status;
            string statusText;
            if (!snapshot.CatalogIsValid)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = "Catalog needs attention";
            }
            else if (snapshot.FailedOperationSteps > 0)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = snapshot.FailedOperationSteps + " operation failure(s)";
            }
            else if (snapshot.UpdateCheckFailureCount > 0)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = snapshot.UpdateCheckFailureCount + " update check failure(s)";
            }
            else if (snapshot.OperationInProgress)
            {
                status = DeucarianControlCenterStatus.Info;
                statusText = "Package operation in progress";
            }
            else if (snapshot.AvailableUpdateCount > 0)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = snapshot.AvailableUpdateCount + " update(s) available";
            }
            else if (!snapshot.HasInstalledPackageSnapshot)
            {
                status = DeucarianControlCenterStatus.Info;
                statusText = "Installed status pending";
            }
            else
            {
                status = DeucarianControlCenterStatus.Success;
                statusText = "Packages ready";
            }

            var details = new List<string>
            {
                snapshot.CatalogPackageCount + " catalog package(s).",
                snapshot.HasInstalledPackageSnapshot
                    ? snapshot.InstalledPackageCount + " catalog package(s) installed."
                    : "Installed packages have not been refreshed yet.",
                snapshot.UpdateCheckInProgress
                    ? "Update check is in progress."
                    : snapshot.AvailableUpdateCount + " available update(s)."
            };
            if (snapshot.CatalogRefreshInProgress)
            {
                details.Add("Catalog refresh is in progress.");
            }

            if (snapshot.InstalledRefreshInProgress)
            {
                details.Add("Installed-package refresh is in progress.");
            }

            if (snapshot.OperationInProgress || snapshot.TotalOperationSteps > 0)
            {
                details.Add(
                    snapshot.CompletedOperationSteps + " of " +
                    snapshot.TotalOperationSteps + " operation step(s) completed.");
            }

            if (snapshot.LastUpdateCheckedUtc.HasValue)
            {
                details.Add(
                    "Updates last checked " +
                    snapshot.LastUpdateCheckedUtc.Value.ToUniversalTime().ToString("u") + ".");
            }

            return new DeucarianControlCenterCard(
                "package-installer.catalog",
                DeucarianControlCenterArea.BuildAndPackages,
                "Packages",
                "Review the package catalog and manage project package composition.",
                "com.deucarian.package-installer",
                status,
                statusText,
                10,
                details,
                new[]
                {
                    new DeucarianControlCenterAction(
                        "package-installer.open",
                        "Open Package Installer",
                        PackageInstallerWindow.Open,
                        "Open the full package workflow.")
                },
                new[] { "package manager", "install", "update", "catalog" });
        }
    }
}