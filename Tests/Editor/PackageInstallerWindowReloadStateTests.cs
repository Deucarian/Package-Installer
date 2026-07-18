using System;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerWindowReloadStateTests
    {
        private const string IntegrationPackageId = "com.deucarian.session.api-integration";
        private const string IntegrationsGroupId = "integrations";

        [SetUp]
        public void SetUp()
        {
            PackageInstallerWindowReloadState.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PackageInstallerWindowReloadState.ResetForTests();
        }

        [Test]
        public void AssemblyReloadSnapshotRoundTripsOnceWithCompleteUiState()
        {
            PackageInstallerWindowReloadSnapshot source = new PackageInstallerWindowReloadSnapshot
            {
                searchText = "logging diagnostics",
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

            PackageInstallerWindowReloadState.SaveForAssemblyReload(source);

            Assert.IsTrue(PackageInstallerWindowReloadState.IsAssemblyReloading);
            Assert.IsTrue(PackageInstallerWindowReloadState.HasSavedStateForTests);

            PackageInstallerWindowReloadState.SimulateNewDomainForTests();
            Assert.IsTrue(PackageInstallerWindowReloadState.TryConsume(out PackageInstallerWindowReloadSnapshot restored));
            Assert.AreEqual(source.searchText, restored.searchText);
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
            Assert.IsFalse(PackageInstallerWindowReloadState.HasSavedStateForTests);
            Assert.IsFalse(PackageInstallerWindowReloadState.TryConsume(out _));
        }

        [Test]
        public void AssemblyReloadDisableRetainsMarkerButNormalDisableClearsIt()
        {
            PackageInstallerWindowReloadState.SaveForAssemblyReload(
                new PackageInstallerWindowReloadSnapshot());

            PackageInstallerWindowReloadState.ClearForNormalDisable();

            Assert.IsTrue(PackageInstallerWindowReloadState.HasSavedStateForTests);

            PackageInstallerWindowReloadState.SimulateNewDomainForTests();
            PackageInstallerWindowReloadState.ClearForNormalDisable();

            Assert.IsFalse(PackageInstallerWindowReloadState.HasSavedStateForTests);
        }

        [Test]
        public void MalformedOrInvalidCameraStateIsConsumedWithoutRestoring()
        {
            PackageInstallerWindowReloadState.SetRawStateForTests("{malformed");

            Assert.IsFalse(PackageInstallerWindowReloadState.TryConsume(out _));
            Assert.IsFalse(PackageInstallerWindowReloadState.HasSavedStateForTests);

            PackageInstallerWindowReloadState.SaveForAssemblyReload(
                new PackageInstallerWindowReloadSnapshot
                {
                    hasGraphCamera = true,
                    graphZoom = 0f
                });
            PackageInstallerWindowReloadState.SimulateNewDomainForTests();

            Assert.IsFalse(PackageInstallerWindowReloadState.TryConsume(out _));
            Assert.IsFalse(PackageInstallerWindowReloadState.HasSavedStateForTests);
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
