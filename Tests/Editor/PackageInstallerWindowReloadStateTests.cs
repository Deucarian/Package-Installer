using System;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerWindowReloadStateTests
    {
        private const string IntegrationPackageId = "com.deucarian.session.api-integration";
        private const string IntegrationsGroupId = "integrations";

        [Test]
        public void WorkspaceReloadSnapshotRoundTripsWithCompleteUiState()
        {
            PackageInstallerWindowReloadSnapshot source = new PackageInstallerWindowReloadSnapshot
            {
                searchText = "logging diagnostics",
                workspaceSearchText = "notifications",
                workspaceCategory = 2,
                showInstalled = false,
                showNotInstalled = true,
                selectedPackageId = IntegrationPackageId,
                navigationTargetKind = (int)PackageGraphNavigationTargetKind.Package,
                focusedPackageId = IntegrationPackageId,
                focusedGroupId = IntegrationsGroupId,
                viewMode = 1,
                sidebarScrollX = 12f,
                sidebarScrollY = 34f,
                detailsScrollX = 56f,
                detailsScrollY = 78f,
                operationScrollX = 90f,
                operationScrollY = 123f,
                hasGraphCamera = true,
                graphPanX = -240f,
                graphPanY = 135f,
                graphZoom = 0.82f
            };

            string state = PackageInstallerWindowReloadState.Serialize(source);
            Assert.IsTrue(PackageInstallerWindowReloadState.TryDeserialize(state, out PackageInstallerWindowReloadSnapshot restored));
            Assert.AreEqual(source.searchText, restored.searchText);
            Assert.AreEqual(source.workspaceSearchText, restored.workspaceSearchText);
            Assert.AreEqual(source.workspaceCategory, restored.workspaceCategory);
            Assert.AreEqual(source.showInstalled, restored.showInstalled);
            Assert.AreEqual(source.showNotInstalled, restored.showNotInstalled);
            Assert.AreEqual(source.selectedPackageId, restored.selectedPackageId);
            Assert.AreEqual(source.navigationTargetKind, restored.navigationTargetKind);
            Assert.AreEqual(source.focusedPackageId, restored.focusedPackageId);
            Assert.AreEqual(source.focusedGroupId, restored.focusedGroupId);
            Assert.AreEqual(source.viewMode, restored.viewMode);
            Assert.AreEqual(source.sidebarScrollX, restored.sidebarScrollX);
            Assert.AreEqual(source.sidebarScrollY, restored.sidebarScrollY);
            Assert.AreEqual(source.detailsScrollX, restored.detailsScrollX);
            Assert.AreEqual(source.detailsScrollY, restored.detailsScrollY);
            Assert.AreEqual(source.operationScrollX, restored.operationScrollX);
            Assert.AreEqual(source.operationScrollY, restored.operationScrollY);
            Assert.AreEqual(source.graphPanX, restored.graphPanX);
            Assert.AreEqual(source.graphPanY, restored.graphPanY);
            Assert.AreEqual(source.graphZoom, restored.graphZoom);
        }

        [Test]
        public void IndependentWorkspaceSnapshotsKeepOwnTabSearchSelectionAndQueue()
        {
            string first = PackageInstallerWindowReloadState.Serialize(new PackageInstallerWindowReloadSnapshot
            {
                workspaceSearchText = "notifications", workspaceCategory = 1,
                selectedPackageId = "com.deucarian.notifications", sidebarScrollY = 145,
                queue = new PackageOperationQueueSnapshot
                {
                    operationName = "Install notifications",
                    items = new[] { new PackageOperationQueueItem
                    {
                        packageId = "com.deucarian.notifications", displayName = "Notifications",
                        state = PackageInstallProgressItemState.Completed
                    } }
                }
            });
            string second = PackageInstallerWindowReloadState.Serialize(new PackageInstallerWindowReloadSnapshot
            {
                workspaceSearchText = "themes", workspaceCategory = 2,
                selectedPackageId = "com.deucarian.theming", sidebarScrollY = 20
            });
            Assert.IsTrue(PackageInstallerWindowReloadState.TryDeserialize(first, out var firstState));
            Assert.IsTrue(PackageInstallerWindowReloadState.TryDeserialize(second, out var secondState));
            Assert.AreEqual(1, firstState.workspaceCategory);
            Assert.AreEqual("notifications", firstState.workspaceSearchText);
            Assert.AreEqual("com.deucarian.notifications", firstState.selectedPackageId);
            Assert.AreEqual(145, firstState.sidebarScrollY);
            Assert.AreEqual("1 completed · 0 current · 0 remaining", firstState.queue.Summary);
            Assert.AreEqual(2, secondState.workspaceCategory);
            Assert.AreEqual("themes", secondState.workspaceSearchText);
            Assert.AreEqual("com.deucarian.theming", secondState.selectedPackageId);
            Assert.AreEqual(20, secondState.sidebarScrollY);
        }

        [Test]
        public void InvalidTabAndScrollValuesNormalizeWithoutDiscardingSelection()
        {
            string state = PackageInstallerWindowReloadState.Serialize(new PackageInstallerWindowReloadSnapshot
            {
                workspaceCategory = -1, sidebarScrollY = float.NaN, detailsScrollY = -10,
                selectedPackageId = IntegrationPackageId
            });
            Assert.IsTrue(PackageInstallerWindowReloadState.TryDeserialize(state, out var restored));
            Assert.AreEqual(0, restored.workspaceCategory);
            Assert.AreEqual(0, restored.sidebarScrollY);
            Assert.AreEqual(0, restored.detailsScrollY);
            Assert.AreEqual(IntegrationPackageId, restored.selectedPackageId);
        }

        [Test]
        public void MalformedOrInvalidCameraStateIsRejectedWithoutRestoring()
        {
            Assert.IsFalse(PackageInstallerWindowReloadState.TryDeserialize("{malformed", out _));

            string state = PackageInstallerWindowReloadState.Serialize(
                new PackageInstallerWindowReloadSnapshot
                {
                    hasGraphCamera = true,
                    graphZoom = 0f
                });
            Assert.IsFalse(PackageInstallerWindowReloadState.TryDeserialize(state, out _));
        }

        [Test]
        public void ResolverRestoresPackageSelectionAndUsesCurrentGroup()
        {
            PackageGraphModel graph = CreateGraph();
            PackageInstallerWindowReloadSnapshot snapshot = new PackageInstallerWindowReloadSnapshot
            {
                selectedPackageId = IntegrationPackageId,
                navigationTargetKind = (int)PackageGraphNavigationTargetKind.Package,
                focusedPackageId = IntegrationPackageId,
                focusedGroupId = "stale-group"
            };

            PackageInstallerWindowReloadResolution result =
                PackageInstallerWindowReloadState.Resolve(snapshot, graph);

            Assert.AreEqual(IntegrationPackageId, result.SelectedPackageId);
            Assert.IsTrue(result.SelectedPackageIsIntegration);
            Assert.AreEqual(PackageGraphNavigationTargetKind.Package, result.Navigation.TargetKind);
            Assert.AreEqual(IntegrationPackageId, result.Navigation.FocusedPackageId);
            Assert.AreEqual(IntegrationsGroupId, result.Navigation.FocusedGroupId);
        }

        [Test]
        public void ResolverFallsBackToExistingGroupThenOverview()
        {
            PackageGraphModel graph = CreateGraph();
            PackageInstallerWindowReloadSnapshot groupFallback = new PackageInstallerWindowReloadSnapshot
            {
                selectedPackageId = "missing-package",
                navigationTargetKind = (int)PackageGraphNavigationTargetKind.Package,
                focusedPackageId = "missing-package",
                focusedGroupId = IntegrationsGroupId
            };

            PackageInstallerWindowReloadResolution groupResult =
                PackageInstallerWindowReloadState.Resolve(groupFallback, graph);

            Assert.IsEmpty(groupResult.SelectedPackageId);
            Assert.AreEqual(PackageGraphNavigationTargetKind.Group, groupResult.Navigation.TargetKind);
            Assert.AreEqual(IntegrationsGroupId, groupResult.Navigation.FocusedGroupId);

            groupFallback.focusedGroupId = "missing-group";
            PackageInstallerWindowReloadResolution overviewResult =
                PackageInstallerWindowReloadState.Resolve(groupFallback, graph);

            Assert.IsTrue(overviewResult.Navigation.IsOverview);
            Assert.IsEmpty(overviewResult.SelectedPackageId);
        }

        [Test]
        public void GraphViewAppliesReloadCameraAfterResponsiveLayout()
        {
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                () => { });
            PackageGraphCameraState expected = new PackageGraphCameraState(
                new Vector2(-180f, 92f),
                0.76f);

            view.PrepareCameraRestoreAfterReload();
            view.SetResponsiveMode(PackageInstallerResponsiveMode.Compact);
            view.RestoreCameraAfterReload(expected);

            PackageGraphCameraState actual = view.CameraStateForTests;
            Assert.AreEqual(expected.Pan.x, actual.Pan.x, 0.001f);
            Assert.AreEqual(expected.Pan.y, actual.Pan.y, 0.001f);
            Assert.AreEqual(expected.Zoom, actual.Zoom, 0.001f);
        }

        private static PackageGraphModel CreateGraph()
        {
            PackageDefinition integration = new PackageDefinition(
                "Session API Integration",
                IntegrationPackageId,
                "https://example.com/session-api.git#main",
                "Session integration.",
                packageKind: PackageKind.Integration,
                groupId: IntegrationsGroupId);
            PackageGraphGroup group = new PackageGraphGroup(
                IntegrationsGroupId,
                "Integrations",
                string.Empty,
                "Integration packages.",
                10);
            return new PackageGraphBuilder(_ => false).Build(
                new[] { integration },
                new[] { group });
        }
    }
}
