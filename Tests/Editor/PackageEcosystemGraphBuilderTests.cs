using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Deucarian.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphBuilderTests
    {
        [Test]
        public void Window_ExposesOnlyEcosystemGraphAndCoercesListRequests()
        {
            Assert.IsTrue(PackageInstallerWindow.DefaultsToEcosystemGraphForTests);
            CollectionAssert.AreEqual(
                new[] { "Ecosystem Graph" },
                PackageInstallerWindow.ViewToggleOrderForTests.ToArray());
            Assert.IsTrue(PackageInstallerWindow.ListViewRequestResolvesToEcosystemGraphForTests);
        }

        [Test]
        public void Window_RegistersProductionPackageInstallerMenuPath()
        {
            Assert.AreEqual("Tools/Deucarian/Package Installer", PackageInstallerWindow.MenuPathForTests);
            CollectionAssert.AreEqual(
                new[] { "Tools/Deucarian/Package Installer" },
                PackageInstallerWindow.UserFacingMenuPathsForTests.ToArray());
            Assert.IsFalse(PackageInstallerWindow.UserFacingMenuPathsForTests.Any(
                path => path.StartsWith("Deucarian/", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(PackageInstallerWindow.UserFacingMenuPathsForTests.Any(
                path => path.IndexOf("/Diagnostics", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(PackageInstallerWindow.UserFacingMenuPathsForTests.Any(
                path => path.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsFalse(PackageInstallerWindow.UserFacingMenuPathsForTests.Any(
                path => path.IndexOf("Development", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void Window_ActionButtonStateTurnsOwnerIntoCancelAndDisablesOtherActions()
        {
            PackageInstallerWindow.PackageInstallerActionButtonState owner =
                PackageInstallerWindow.GetActionButtonStateForTests(
                    PackageInstallerWindow.PackageInstallerActionKind.CheckUpdates,
                    PackageInstallerWindow.PackageInstallerActionKind.CheckUpdates,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    anyOperationBusy: true,
                    hasPackagesWithUpdates: true);
            PackageInstallerWindow.PackageInstallerActionButtonState unrelated =
                PackageInstallerWindow.GetActionButtonStateForTests(
                    PackageInstallerWindow.PackageInstallerActionKind.UpdateAll,
                    PackageInstallerWindow.PackageInstallerActionKind.CheckUpdates,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    anyOperationBusy: true,
                    hasPackagesWithUpdates: true);

            Assert.AreEqual("Cancel Check", owner.Label);
            Assert.IsTrue(owner.Enabled);
            Assert.AreEqual("Update All", unrelated.Label);
            Assert.IsFalse(unrelated.Enabled);
        }

        [Test]
        public void Window_ActionButtonStateShowsCancelingAsDisabledOwner()
        {
            PackageInstallerWindow.PackageInstallerActionButtonState state =
                PackageInstallerWindow.GetActionButtonStateForTests(
                    PackageInstallerWindow.PackageInstallerActionKind.InstallAll,
                    PackageInstallerWindow.PackageInstallerActionKind.InstallAll,
                    PackageInstallerWindow.PackageInstallerActionKind.InstallAll,
                    anyOperationBusy: true,
                    hasPackagesWithUpdates: false);

            Assert.AreEqual("Canceling...", state.Label);
            Assert.IsFalse(state.Enabled);
        }

        [Test]
        public void Window_UpdateAllActionButtonRequiresUpdatesWhenIdle()
        {
            PackageInstallerWindow.PackageInstallerActionButtonState withoutUpdates =
                PackageInstallerWindow.GetActionButtonStateForTests(
                    PackageInstallerWindow.PackageInstallerActionKind.UpdateAll,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    anyOperationBusy: false,
                    hasPackagesWithUpdates: false);
            PackageInstallerWindow.PackageInstallerActionButtonState withUpdates =
                PackageInstallerWindow.GetActionButtonStateForTests(
                    PackageInstallerWindow.PackageInstallerActionKind.UpdateAll,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    PackageInstallerWindow.PackageInstallerActionKind.None,
                    anyOperationBusy: false,
                    hasPackagesWithUpdates: true);

            Assert.AreEqual("Update All", withoutUpdates.Label);
            Assert.IsFalse(withoutUpdates.Enabled);
            Assert.IsTrue(withUpdates.Enabled);
        }

        [Test]
        public void Window_EcosystemUpdateAllIsVisibleOnlyWhenUpdatesExist()
        {
            Assert.IsEmpty(PackageInstallerWindow.CreateEcosystemOverviewActionsForTests(0));

            PackageInstallerWindow.EcosystemOverviewAction action =
                PackageInstallerWindow.CreateEcosystemOverviewActionsForTests(3).Single();

            Assert.AreEqual(PackageInstallerWindow.PackageInstallerActionKind.UpdateAll, action.Kind);
            Assert.AreEqual("Update all (3)", action.Label);
        }

        [Test]
        public void Window_CompletedUpdateConsumesOnlyFinishedPackageAttention()
        {
            PackageDefinition updated = CreatePackage("Updated", "com.example.updated", "Core");
            PackageDefinition stillUpdating = CreatePackage("Still Updating", "com.example.still-updating", "Core");
            HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                updated.PackageId,
                stillUpdating.PackageId
            };

            Assert.IsTrue(PackageInstallerWindow.TryConsumePendingUpdateStatusInvalidationForTests(
                pending,
                updated,
                success: true));

            Assert.IsFalse(pending.Contains(updated.PackageId));
            Assert.IsTrue(pending.Contains(stillUpdating.PackageId));
        }

        [Test]
        public void Window_FailedUpdateConsumesPendingWorkButKeepsAttentionVisible()
        {
            PackageDefinition failed = CreatePackage("Failed", "com.example.failed", "Core");
            HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                failed.PackageId
            };

            Assert.IsFalse(PackageInstallerWindow.TryConsumePendingUpdateStatusInvalidationForTests(
                pending,
                failed,
                success: false));

            Assert.IsFalse(pending.Contains(failed.PackageId));
        }

        [Test]
        public void Window_UntrackedCompletedPackageDoesNotClearUpdateAttention()
        {
            PackageDefinition tracked = CreatePackage("Tracked", "com.example.tracked", "Core");
            PackageDefinition unrelated = CreatePackage("Unrelated", "com.example.unrelated", "Core");
            HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                tracked.PackageId
            };

            Assert.IsFalse(PackageInstallerWindow.TryConsumePendingUpdateStatusInvalidationForTests(
                pending,
                unrelated,
                success: true));

            Assert.IsTrue(pending.Contains(tracked.PackageId));
        }

        [Test]
        public void StartupUpdateCheckIsTheOnlyRegisteredEditorStartupHook()
        {
            System.Reflection.Assembly packageInstallerAssembly = typeof(PackageInstallerWindow).Assembly;
            Type[] startupHookTypes = packageInstallerAssembly
                .GetTypes()
                .Where(type => type.GetCustomAttributes(typeof(InitializeOnLoadAttribute), inherit: false).Length > 0)
                .ToArray();
            MethodInfo[] startupHookMethods = packageInstallerAssembly
                .GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(method => method.GetCustomAttributes(typeof(InitializeOnLoadMethodAttribute), inherit: false).Length > 0)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { typeof(PackageInstallerStartupUpdateCheck) },
                startupHookTypes);
            Assert.IsEmpty(startupHookMethods);
        }

        [Test]
        public void StartupUpdateCheckSchedulesOncePerEnabledSession()
        {
            Assert.IsTrue(PackageInstallerStartupUpdateCheck.ShouldScheduleForTests(
                enabled: true,
                alreadyAttempted: false,
                isBatchMode: false));
            Assert.IsFalse(PackageInstallerStartupUpdateCheck.ShouldScheduleForTests(
                enabled: true,
                alreadyAttempted: true,
                isBatchMode: false));
            Assert.IsFalse(PackageInstallerStartupUpdateCheck.ShouldScheduleForTests(
                enabled: false,
                alreadyAttempted: false,
                isBatchMode: false));
            Assert.IsFalse(PackageInstallerStartupUpdateCheck.ShouldScheduleForTests(
                enabled: true,
                alreadyAttempted: false,
                isBatchMode: true));
            Assert.AreEqual(
                PackageInstallerStartupCheckDecision.WaitForEditor,
                PackageInstallerStartupUpdateCheck.GetDecisionForTests(
                    enabled: true,
                    alreadyAttempted: false,
                    isBatchMode: false,
                    isCompiling: true,
                    isUpdating: false));
            Assert.AreEqual(
                PackageInstallerStartupCheckDecision.WaitForEditor,
                PackageInstallerStartupUpdateCheck.GetDecisionForTests(
                    enabled: true,
                    alreadyAttempted: false,
                    isBatchMode: false,
                    isCompiling: false,
                    isUpdating: true));
            Assert.AreEqual(
                PackageInstallerStartupCheckDecision.Start,
                PackageInstallerStartupUpdateCheck.GetDecisionForTests(
                    enabled: true,
                    alreadyAttempted: false,
                    isBatchMode: false,
                    isCompiling: false,
                    isUpdating: false));
        }

        [Test]
        public void Window_FormatsEcosystemOverviewGroupRowsWithGraphStatusSummary()
        {
            Assert.AreEqual(
                "! 1 attention   \u2713 2 installed   \u25CB 3 not installed",
                PackageInstallerWindow.FormatEcosystemOverviewGroupStatusSummaryForTests(2, 3, 1, 0));
            Assert.AreEqual(
                "\u2713 1 installed",
                PackageInstallerWindow.FormatEcosystemOverviewGroupStatusSummaryForTests(1, 0, 0, 0));
            Assert.AreEqual(
                "\u25CB 2 not installed   ? 1 unknown",
                PackageInstallerWindow.FormatEcosystemOverviewGroupStatusSummaryForTests(0, 2, 0, 1));
        }

        [Test]
        public void Window_AttentionIsContextualAndHiddenAtZero()
        {
            Assert.IsFalse(PackageInstallerWindow.ShouldShowEcosystemAttentionForTests(0));
            Assert.IsTrue(PackageInstallerWindow.ShouldShowEcosystemAttentionForTests(1));
        }

        [Test]
        public void StateRepository_UsesSharedProjectChannelPreference()
        {
            string projectRoot = CreateTempProjectRoot();

            try
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                string key = PackageInstallerStateRepository.GetProjectChannelPreferenceKeyForTests(projectRoot);

                StringAssert.StartsWith(PackageInstallerStateRepository.ProjectChannelPreferencePrefix, key);
                Assert.AreEqual(PackageChannel.Stable, PackageInstallerStateRepository.GetProjectChannelForTests(projectRoot));

                PackageInstallerStateRepository.SetProjectChannelForTests(projectRoot, PackageChannel.Development);

                Assert.AreEqual(PackageChannel.Development, PackageInstallerStateRepository.GetProjectChannelForTests(projectRoot));
                Assert.AreEqual((int)PackageChannel.Development, EditorPrefs.GetInt(key, -1));

                PackageInstallerStateRepository.ClearProjectChannelForTests(projectRoot);

                PackageChannelSelection cleared =
                    PackageInstallerStateRepository.GetProjectChannelSelectionForTests(projectRoot);
                Assert.IsFalse(cleared.HasValue);
                Assert.AreEqual(PackageChannel.Stable, cleared.Channel);
                Assert.IsFalse(EditorPrefs.HasKey(key));
                Assert.IsFalse(EditorPrefs.HasKey(
                    PackageInstallerStateRepository.GetProjectChannelChangedAtPreferenceKeyForTests(projectRoot)));
            }
            finally
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                DeleteTempProjectRoot(projectRoot);
            }
        }

        [Test]
        public void StateRepository_ClearProjectChannelAlsoRemovesLegacyOverride()
        {
            string projectRoot = CreateTempProjectRoot();
            string legacyKey =
                PackageInstallerStateRepository.GetLegacyBootstrapChannelPreferenceKeyForTests(projectRoot);

            try
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                EditorPrefs.SetInt(legacyKey, (int)PackageChannel.Development);
                Assert.IsTrue(
                    PackageInstallerStateRepository.GetProjectChannelSelectionForTests(projectRoot).HasValue);

                PackageInstallerStateRepository.ClearProjectChannelForTests(projectRoot);

                Assert.IsFalse(EditorPrefs.HasKey(legacyKey));
                Assert.IsFalse(
                    PackageInstallerStateRepository.GetProjectChannelSelectionForTests(projectRoot).HasValue);
            }
            finally
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                DeleteTempProjectRoot(projectRoot);
            }
        }

        [Test]
        public void StateRepository_ReadsLegacyBootstrapChannelUntilSharedKeyExists()
        {
            string projectRoot = CreateTempProjectRoot();

            try
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                string legacyKey = PackageInstallerStateRepository.GetLegacyBootstrapChannelPreferenceKeyForTests(projectRoot);
                EditorPrefs.SetInt(legacyKey, (int)PackageChannel.Development);

                Assert.AreEqual(PackageChannel.Development, PackageInstallerStateRepository.GetProjectChannelForTests(projectRoot));

                PackageInstallerStateRepository.SetProjectChannelForTests(projectRoot, PackageChannel.Stable);

                Assert.AreEqual(PackageChannel.Stable, PackageInstallerStateRepository.GetProjectChannelForTests(projectRoot));
            }
            finally
            {
                PackageInstallerStateRepository.DeleteProjectChannelForTests(projectRoot);
                DeleteTempProjectRoot(projectRoot);
            }
        }

        [Test]
        public void StateRepository_StoresPackageChannelSelectionWithTimestamp()
        {
            string projectRoot = CreateTempProjectRoot();
            string packageId = "com.example.package";

            try
            {
                PackageInstallerStateRepository.DeletePackageChannelForTests(projectRoot, packageId);
                string key = PackageInstallerStateRepository.GetPackageChannelPreferenceKeyForTests(projectRoot, packageId);
                string changedAtKey =
                    PackageInstallerStateRepository.GetPackageChannelChangedAtPreferenceKeyForTests(projectRoot, packageId);

                StringAssert.StartsWith(PackageInstallerStateRepository.PackageChannelPreferencePrefix, key);
                StringAssert.StartsWith(
                    PackageInstallerStateRepository.PackageChannelChangedAtPreferencePrefix,
                    changedAtKey);
                Assert.IsFalse(
                    PackageInstallerStateRepository.GetPackageChannelSelectionForTests(projectRoot, packageId).HasValue);

                PackageInstallerStateRepository.SetPackageChannelForTests(
                    projectRoot,
                    packageId,
                    PackageChannel.Development,
                    1234L);
                PackageChannelSelection selection =
                    PackageInstallerStateRepository.GetPackageChannelSelectionForTests(projectRoot, packageId);

                Assert.IsTrue(selection.HasValue);
                Assert.AreEqual(PackageChannel.Development, selection.Channel);
                Assert.AreEqual(1234L, selection.ChangedAtUtcTicks);
                Assert.AreEqual((int)PackageChannel.Development, EditorPrefs.GetInt(key, -1));
                Assert.AreEqual("1234", EditorPrefs.GetString(changedAtKey, string.Empty));
            }
            finally
            {
                PackageInstallerStateRepository.DeletePackageChannelForTests(projectRoot, packageId);
                DeleteTempProjectRoot(projectRoot);
            }
        }

        [Test]
        public void Window_ResolvesLatestGlobalOrPackageChannelSelection()
        {
            PackageDefinition package = CreatePackage("Package", "com.example.package", "Core");
            PackageChannelSelection olderPackageSelection =
                PackageChannelSelection.Create(PackageChannel.Development, 10L);
            PackageChannelSelection newerGlobalSelection =
                PackageChannelSelection.Create(PackageChannel.Stable, 20L);
            PackageChannelSelection newerPackageSelection =
                PackageChannelSelection.Create(PackageChannel.Development, 30L);

            Assert.AreEqual(
                PackageChannel.Stable,
                PackageInstallerWindow.ResolveSelectedChannelForTests(
                    package,
                    newerGlobalSelection,
                    olderPackageSelection,
                    hasInstalledChannel: false,
                    installedChannel: PackageChannel.Stable));
            Assert.AreEqual(
                PackageChannel.Development,
                PackageInstallerWindow.ResolveSelectedChannelForTests(
                    package,
                    newerGlobalSelection,
                    newerPackageSelection,
                    hasInstalledChannel: false,
                    installedChannel: PackageChannel.Stable));

            PackageDefinition stableOnlyPackage = new PackageDefinition(
                "Stable Only",
                "com.example.stable-only",
                "https://example.com/stable-only.git#main",
                "Stable only package.");

            Assert.AreEqual(
                PackageChannel.Stable,
                PackageInstallerWindow.ResolveSelectedChannelForTests(
                    stableOnlyPackage,
                    PackageChannelSelection.None,
                    newerPackageSelection,
                    hasInstalledChannel: false,
                    installedChannel: PackageChannel.Stable));
            Assert.AreEqual(
                PackageChannel.Custom,
                PackageInstallerWindow.ResolveSelectedChannelForTests(
                    package,
                    PackageChannelSelection.None,
                    PackageChannelSelection.None,
                    hasInstalledChannel: true,
                    installedChannel: PackageChannel.Custom));
            Assert.AreEqual(
                PackageChannel.Stable,
                PackageInstallerWindow.ResolveSelectedChannelForTests(
                    package,
                    newerGlobalSelection,
                    PackageChannelSelection.None,
                    hasInstalledChannel: true,
                    installedChannel: PackageChannel.Custom));
        }

        [Test]
        public void Window_ChannelProvenanceAppearsOnlyForOverrideFallbackOrCustomSource()
        {
            PackageDefinition package = CreatePackage("Package", "com.example.package", "Core");
            PackageDefinition stableOnly = new PackageDefinition(
                "Stable Only",
                "com.example.stable-only",
                "https://example.com/stable-only.git#main",
                "Stable only package.");

            Assert.IsEmpty(PackageInstallerWindow.GetContextualChannelProvenance(
                package,
                PackageChannelSelection.None,
                PackageChannelSelection.None,
                hasInstalledChannel: false,
                installedChannel: PackageChannel.Stable,
                installedSourceReason: string.Empty));
            StringAssert.StartsWith("Package override", PackageInstallerWindow.GetContextualChannelProvenance(
                package,
                PackageChannelSelection.None,
                PackageChannelSelection.Create(PackageChannel.Development, 10L),
                hasInstalledChannel: false,
                installedChannel: PackageChannel.Stable,
                installedSourceReason: string.Empty));
            StringAssert.Contains("Stable fallback", PackageInstallerWindow.GetContextualChannelProvenance(
                stableOnly,
                PackageChannelSelection.Create(PackageChannel.Development, 10L),
                PackageChannelSelection.None,
                hasInstalledChannel: false,
                installedChannel: PackageChannel.Stable,
                installedSourceReason: string.Empty));
            StringAssert.StartsWith("Custom installed source", PackageInstallerWindow.GetContextualChannelProvenance(
                package,
                PackageChannelSelection.None,
                PackageChannelSelection.None,
                hasInstalledChannel: true,
                installedChannel: PackageChannel.Custom,
                installedSourceReason: "Local package path"));
        }

        [Test]
        public void StateRepository_ManifestSignatureChangesForPackageManifestState()
        {
            string projectRoot = CreateTempProjectRoot();
            string packagesDirectory = Path.Combine(projectRoot, "Packages");
            Directory.CreateDirectory(packagesDirectory);
            string manifestPath = Path.Combine(packagesDirectory, "manifest.json");
            string packageLockPath = Path.Combine(packagesDirectory, "packages-lock.json");

            try
            {
                File.WriteAllText(manifestPath, "{\"dependencies\":{}}");
                string firstSignature = PackageInstallerStateRepository.GetManifestStateSignatureForTests(projectRoot);

                File.WriteAllText(packageLockPath, "{\"dependencies\":{\"com.deucarian.logging\":{\"version\":\"1.0.1\"}}}");
                string secondSignature = PackageInstallerStateRepository.GetManifestStateSignatureForTests(projectRoot);

                Assert.IsTrue(PackageDetectionService.HasManifestStateChangedForTests(firstSignature, secondSignature));
                Assert.IsFalse(PackageDetectionService.HasManifestStateChangedForTests(secondSignature, secondSignature));
            }
            finally
            {
                DeleteTempProjectRoot(projectRoot);
            }
        }

        [Test]
        public void Window_ResponsiveBreakpointsPreserveUsableMinimumWidth()
        {
            Assert.AreEqual(PackageInstallerResponsiveMode.Wide, PackageInstallerWindow.ResolveResponsiveModeForTests(1280f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Compact, PackageInstallerWindow.ResolveResponsiveModeForTests(1040f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Narrow, PackageInstallerWindow.ResolveResponsiveModeForTests(860f));
            Assert.AreEqual(PackageInstallerResponsiveMode.Narrow, PackageInstallerWindow.ResolveResponsiveModeForTests(820f));
            Assert.AreEqual(820f, PackageInstallerWindow.MinWindowSizeForTests.x);
            Assert.AreEqual(650f, PackageInstallerWindow.MinWindowSizeForTests.y);
        }

        [TestCase(1280f, 410f, 410f, true)]
        [TestCase(1040f, 350f, 350f, true)]
        [TestCase(820f, 800f, 800f, false)]
        public void Window_GraphDetailsWidthUsesMeasuredPaneForActionLayout(
            float windowWidth,
            float measuredContentWidth,
            float expectedContentWidth,
            bool expectedStackedActions)
        {
            float detailsContentWidth = PackageInstallerWindow.ResolveDetailsContentWidthForTests(
                windowWidth,
                true,
                measuredContentWidth);

            Assert.AreEqual(expectedContentWidth, detailsContentWidth);
            Assert.AreEqual(
                expectedStackedActions,
                PackageInstallerWindow.ShouldStackDetailsActionsForTests(detailsContentWidth));
        }

        [Test]
        public void Window_DetailsWidthFallsBackForListAndUnresolvedGraphLayout()
        {
            const float windowWidth = 1280f;
            const float expectedFallbackWidth = 884f;

            Assert.AreEqual(
                expectedFallbackWidth,
                PackageInstallerWindow.ResolveDetailsContentWidthForTests(
                    windowWidth,
                    false,
                    410f));
            Assert.AreEqual(
                expectedFallbackWidth,
                PackageInstallerWindow.ResolveDetailsContentWidthForTests(
                    windowWidth,
                    true,
                    0f));
            Assert.AreEqual(
                expectedFallbackWidth,
                PackageInstallerWindow.ResolveDetailsContentWidthForTests(
                    windowWidth,
                    true,
                    float.NaN));
            Assert.AreEqual(
                expectedFallbackWidth,
                PackageInstallerWindow.ResolveDetailsContentWidthForTests(
                    windowWidth,
                    true,
                    float.PositiveInfinity));
            Assert.IsFalse(
                PackageInstallerWindow.ShouldStackDetailsActionsForTests(expectedFallbackWidth));
        }

        [Test]
        public void Window_DetailsGroupRowsActivateOnlyFromFocusedKeyboardControls()
        {
            Assert.IsTrue(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                true,
                EventType.KeyDown,
                KeyCode.Return));
            Assert.IsTrue(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                true,
                EventType.KeyDown,
                KeyCode.KeypadEnter));
            Assert.IsTrue(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                true,
                EventType.KeyDown,
                KeyCode.Space));
            Assert.IsFalse(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                false,
                EventType.KeyDown,
                KeyCode.Return));
            Assert.IsFalse(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                true,
                EventType.KeyUp,
                KeyCode.Return));
            Assert.IsFalse(PackageInstallerWindow.IsGraphNavigationRowKeyboardActivationForTests(
                true,
                EventType.KeyDown,
                KeyCode.Escape));
        }

        [Test]
        public void Window_GlobalChannelResetAppearsOnlyForExplicitOverrides()
        {
            PackageChannelSelection inherited = PackageChannelSelection.None;
            PackageChannelSelection explicitOverride = PackageChannelSelection.Create(
                PackageChannel.Development,
                123L);

            Assert.AreEqual(
                "Channel: Stable",
                PackageInstallerWindow.FormatGlobalChannelButtonLabelForTests(inherited));
            Assert.AreEqual(
                "Override: Development",
                PackageInstallerWindow.FormatGlobalChannelButtonLabelForTests(explicitOverride));
            Assert.IsFalse(PackageInstallerWindow.ShouldShowGlobalChannelResetForTests(inherited));
            Assert.IsTrue(PackageInstallerWindow.ShouldShowGlobalChannelResetForTests(explicitOverride));
        }

        [Test]
        public void Window_NarrowDetailsPutContextBeforeGraphNavigation()
        {
            Assert.IsTrue(PackageInstallerWindow.ShouldDrawGraphNavigationBeforeContextForTests(
                PackageInstallerResponsiveMode.Wide));
            Assert.IsTrue(PackageInstallerWindow.ShouldDrawGraphNavigationBeforeContextForTests(
                PackageInstallerResponsiveMode.Compact));
            Assert.IsFalse(PackageInstallerWindow.ShouldDrawGraphNavigationBeforeContextForTests(
                PackageInstallerResponsiveMode.Narrow));
        }

        [Test]
        public void Window_FixedWallpaperLayerIsAbsoluteAndNonInteractive()
        {
            VisualElement root = new VisualElement();
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            root.Add(background);
            root.Add(overlay);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root);

            Assert.IsTrue(background.ClassListContains(DeucarianEditorWindowChrome.BackgroundLayerClass));
            Assert.IsTrue(overlay.ClassListContains(DeucarianEditorWindowChrome.OverlayLayerClass));
            Assert.AreEqual(PickingMode.Ignore, background.pickingMode);
            Assert.AreEqual(PickingMode.Ignore, overlay.pickingMode);
            Assert.AreEqual(Position.Absolute, background.style.position.value);
            Assert.AreEqual(Position.Absolute, overlay.style.position.value);
        }

        [Test]
        public void Window_FixedWallpaperUsesApplicationShellHostAndTopSafeFade()
        {
            VisualElement root = new VisualElement();
            VisualElement shell = new VisualElement { name = "deucarian-application-shell" };
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            root.Add(background);
            root.Add(overlay);
            root.Add(shell);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root, shell);

            Assert.AreSame(shell, background.parent);
            Assert.AreSame(shell, overlay.parent);
            Assert.IsTrue(shell.ClassListContains(DeucarianEditorWindowChrome.SafeShellClass));
            Assert.AreEqual(Overflow.Hidden, shell.style.overflow.value);

            VisualElement fade = shell.Q<VisualElement>(PackageInstallerWindow.WallpaperTopSafeFadeName);
            Assert.NotNull(fade);
            Assert.IsTrue(fade.ClassListContains(DeucarianEditorWindowChrome.TopSafeFadeClass));
            Assert.AreEqual(PickingMode.Ignore, fade.pickingMode);
            Assert.AreEqual(Position.Absolute, fade.style.position.value);
            Assert.AreEqual(86f, fade.style.height.value.value);
        }

        [Test]
        public void Window_AmbientGlassLayersAreFixedDecorativeAndOutsideGraphTransform()
        {
            VisualElement root = new VisualElement();
            VisualElement shell = new VisualElement { name = "deucarian-application-shell" };
            VisualElement background = new VisualElement { name = "deucarian-window-background" };
            VisualElement overlay = new VisualElement { name = "deucarian-window-overlay" };
            root.Add(background);
            root.Add(overlay);
            root.Add(shell);

            PackageInstallerWindow.ConfigureFixedWallpaperForTests(root, shell);

            VisualElement ambient = shell.Q<VisualElement>(DeucarianEditorAmbientGlass.AmbientLayerName);
            VisualElement grain = shell.Q<VisualElement>(DeucarianEditorAmbientGlass.GrainLayerName);
            VisualElement vignette = shell.Q<VisualElement>(DeucarianEditorAmbientGlass.VignetteLayerName);

            AssertFixedDecorativeLayer(ambient, "deucarian-ambient-lighting-layer");
            AssertFixedDecorativeLayer(grain, "deucarian-grain-layer");
            AssertFixedDecorativeLayer(vignette, "deucarian-vignette-layer");
            Assert.IsTrue(overlay.ClassListContains(DeucarianEditorWindowChrome.ReadabilityOverlayClass));

            VisualElement[] children = shell.Children().ToArray();
            Assert.Less(Array.IndexOf(children, background), Array.IndexOf(children, ambient));
            Assert.Less(Array.IndexOf(children, vignette), Array.IndexOf(children, overlay));
            Assert.IsFalse(ambient.ClassListContains("dpi-ecosystem-graph__content"));
        }

        [Test]
        public void Window_AmbientMotionModesExposeStableMotionScale()
        {
            try
            {
                DeucarianEditorAmbientMotionSettings.SetModeForTests(DeucarianEditorAmbientMotionMode.On);
                Assert.AreEqual(1f, DeucarianEditorAmbientMotionSettings.MotionScale);

                DeucarianEditorAmbientMotionSettings.SetModeForTests(DeucarianEditorAmbientMotionMode.Reduced);
                Assert.That(DeucarianEditorAmbientMotionSettings.MotionScale, Is.GreaterThan(0f).And.LessThan(1f));

                DeucarianEditorAmbientMotionSettings.SetModeForTests(DeucarianEditorAmbientMotionMode.Off);
                Assert.AreEqual(0f, DeucarianEditorAmbientMotionSettings.MotionScale);
            }
            finally
            {
                DeucarianEditorAmbientMotionSettings.SetModeForTests(null);
            }
        }

        [Test]
        public void Window_OperationFooterBuildsStableVisibleHierarchy()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();

            Assert.AreEqual(PackageInstallerWindow.OperationFooterRowName, footer.name);
            Assert.IsTrue(footer.ClassListContains("dpi-operation-surface"));
            Assert.IsTrue(footer.ClassListContains("dpi-operation-footer"));
            Assert.AreEqual(FlexDirection.Row, footer.style.flexDirection.value);
            Assert.AreEqual(Align.Center, footer.style.alignItems.value);
            Assert.AreEqual(0f, footer.style.flexShrink.value);
            Assert.AreEqual(34f, footer.style.height.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationInlinePaddingForTests, footer.style.paddingLeft.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationInlinePaddingForTests, footer.style.paddingRight.value.value);

            VisualElement statusGroup = footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName);
            Label statusIcon = footer.Q<Label>(PackageInstallerWindow.OperationFooterStatusIconName);
            Label statusLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterStatusLabelName);
            Label summaryLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName);

            AssertFooterElementVisible(statusGroup);
            AssertFooterElementVisible(statusIcon);
            AssertFooterElementVisible(statusLabel);
            AssertFooterElementVisible(summaryLabel);
            AssertFooterElementVisible(detailsButton);
            AssertFooterElementVisible(versionLabel);

            Assert.IsFalse(string.IsNullOrWhiteSpace(statusIcon.text));
            Assert.IsFalse(string.IsNullOrWhiteSpace(statusLabel.text));
            Assert.IsFalse(string.IsNullOrWhiteSpace(summaryLabel.text));
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, statusGroup.style.marginRight.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, summaryLabel.style.marginRight.value.value);
            Assert.AreEqual(PackageInstallerWindow.OperationControlGapForTests, detailsButton.style.marginRight.value.value);
            Assert.IsTrue(detailsButton.text == "Show Details" || detailsButton.text == "Hide Details");
            Assert.IsFalse(string.IsNullOrWhiteSpace(versionLabel.text));
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);
            StringAssert.Contains(PackageInstallerWindow.PackageVersionForTests, versionLabel.text);
        }

        [Test]
        public void Window_OperationFooterDrawerToggleKeepsVersionVisible()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);
            Label versionLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName);

            Assert.AreEqual("Show Details", detailsButton.text);
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);

            PackageInstallerWindow.SetOperationFooterExpandedForTests(footer, true);

            Assert.AreEqual("Hide Details", detailsButton.text);
            AssertFooterElementVisible(versionLabel);
            StringAssert.Contains(PackageInstallerWindow.PackageIdForTests, versionLabel.text);
            StringAssert.Contains(PackageInstallerWindow.PackageVersionForTests, versionLabel.text);
        }

        [Test]
        public void Window_OperationFooterResponsiveClassesDoNotHideContent()
        {
            VisualElement root = new VisualElement();
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            root.Add(footer);

            foreach (string responsiveClass in new[]
                     {
                         "dpi-responsive--wide",
                         "dpi-responsive--compact",
                         "dpi-responsive--narrow"
                     })
            {
                root.RemoveFromClassList("dpi-responsive--wide");
                root.RemoveFromClassList("dpi-responsive--compact");
                root.RemoveFromClassList("dpi-responsive--narrow");
                root.AddToClassList(responsiveClass);

                AssertFooterElementVisible(footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName));
                AssertFooterElementVisible(footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName));
                AssertFooterElementVisible(footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName));
                AssertFooterElementVisible(footer.Q<Label>(PackageInstallerWindow.OperationFooterVersionName));
            }
        }

        [Test]
        public void Window_OperationDrawerHeightShrinksToContentAndCapsLargeSummaries()
        {
            float collapsedHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: false,
                contentLineCount: 20);
            float smallHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: true,
                contentLineCount: 1);
            float largeHeight = PackageInstallerWindow.CalculateOperationDrawerContainerHeightForTests(
                expanded: true,
                contentLineCount: 40);

            Assert.AreEqual(0f, collapsedHeight);
            Assert.That(smallHeight, Is.GreaterThan(PackageInstallerWindow.OperationFooterHeightForTests));
            Assert.That(smallHeight, Is.LessThan(110f));
            Assert.That(largeHeight, Is.GreaterThan(smallHeight));
            Assert.That(largeHeight, Is.LessThanOrEqualTo(PackageInstallerWindow.OperationDrawerExpandedMaxHeightForTests));
            Assert.That(largeHeight, Is.GreaterThan(180f));
        }

        [Test]
        public void Window_OperationDrawerBuildsPersistentVisibleContent()
        {
            VisualElement drawer = PackageInstallerWindow.CreateOperationDrawerForTests(
                expanded: true,
                report: "Package operation completed.\nInstalled com.example.core.");
            ScrollView scrollView = drawer.Q<ScrollView>(PackageInstallerWindow.OperationDrawerScrollViewName);
            VisualElement content = drawer.Q<VisualElement>(PackageInstallerWindow.OperationDrawerContentName);
            Label title = drawer.Q<Label>(PackageInstallerWindow.OperationDrawerTitleName);
            Toggle toggle = drawer.Q<Toggle>(PackageInstallerWindow.OperationDrawerVerboseToggleName);
            Label verboseLabel = drawer.Q<Label>(PackageInstallerWindow.OperationDrawerVerboseLabelName);
            Label message = drawer.Q<Label>(PackageInstallerWindow.OperationDrawerMessageName);
            Button retry = drawer.Q<Button>(PackageInstallerWindow.OperationDrawerRetryButtonName);

            Assert.AreEqual(PackageInstallerWindow.OperationDrawerName, drawer.name);
            Assert.IsTrue(drawer.ClassListContains("dpi-operation-surface"));
            Assert.IsTrue(drawer.ClassListContains("dpi-operation-drawer"));
            Assert.IsTrue(drawer.ClassListContains("dpi-operation-drawer--expanded"));
            AssertFooterElementVisible(drawer);
            AssertFooterElementVisible(scrollView);
            AssertFooterElementVisible(content);
            AssertFooterElementVisible(title);
            AssertFooterElementVisible(toggle);
            AssertFooterElementVisible(verboseLabel);
            AssertFooterElementVisible(message);
            Assert.IsNotNull(retry);
            Assert.AreEqual(DisplayStyle.None, retry.style.display.value);
            Assert.AreEqual("Activity", title.text);
            Assert.AreEqual("Verbose Console Logging", verboseLabel.text);
            StringAssert.Contains("Package operation completed.", message.text);
            StringAssert.Contains("Installed com.example.core.", message.text);
            Assert.That(drawer.style.height.value.value, Is.GreaterThan(PackageInstallerWindow.OperationFooterHeightForTests));
        }

        [Test]
        public void Window_OperationSpacingTokensKeepFooterPixelValuesStable()
        {
            VisualElement footer = PackageInstallerWindow.CreateOperationFooterForTests();
            VisualElement statusGroup = footer.Q<VisualElement>(PackageInstallerWindow.OperationFooterStatusGroupName);
            Label summaryLabel = footer.Q<Label>(PackageInstallerWindow.OperationFooterSummaryName);
            Button detailsButton = footer.Q<Button>(PackageInstallerWindow.OperationFooterDetailsButtonName);

            Assert.AreEqual(12, PackageInstallerWindow.OperationInlinePaddingForTests);
            Assert.AreEqual(8, PackageInstallerWindow.OperationControlGapForTests);
            Assert.AreEqual(34f, PackageInstallerWindow.OperationFooterHeightForTests);
            Assert.AreEqual(12f, footer.style.paddingLeft.value.value);
            Assert.AreEqual(12f, footer.style.paddingRight.value.value);
            Assert.AreEqual(8f, statusGroup.style.marginRight.value.value);
            Assert.AreEqual(8f, summaryLabel.style.marginRight.value.value);
            Assert.AreEqual(8f, detailsButton.style.marginRight.value.value);
        }

        [Test]
        public void Build_MapsRegistryMetadataToNodeTypesAndRelationships()
        {
            PackageDefinition core = CreatePackage("Core", "com.example.core", "Core");
            PackageDefinition tool = CreatePackage("Tool", "com.example.tool", "Tools", "Tool");
            PackageDefinition optional = CreatePackage("Optional", "com.example.optional", "UI", "OptionalIntegration");
            PackageDefinition integration = CreatePackage(
                "Integration",
                "com.example.integration",
                "Integration",
                "Integration",
                dependencies: new[] { core.PackageId, optional.PackageId },
                integrationTargets: new[] { core.PackageId, optional.PackageId });
            PackageDefinition suite = CreatePackage(
                "Suite",
                "com.example.suite",
                "Suites",
                "Suite",
                dependencies: new[] { core.PackageId, optional.PackageId, integration.PackageId },
                suiteMembers: new[] { core.PackageId, optional.PackageId, integration.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == core.PackageId || packageId == integration.PackageId)
                .Build(new[] { core, tool, optional, integration, suite });

            Assert.AreEqual(PackageGraphNodeType.Core, graph.Nodes.Single(node => node.PackageId == core.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Tool, graph.Nodes.Single(node => node.PackageId == tool.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Companion, graph.Nodes.Single(node => node.PackageId == optional.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Integration, graph.Nodes.Single(node => node.PackageId == integration.PackageId).NodeType);
            Assert.AreEqual(PackageGraphNodeType.Suite, graph.Nodes.Single(node => node.PackageId == suite.PackageId).NodeType);

            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == core.PackageId &&
                edge.ToPackageId == integration.PackageId));
            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.IntegrationConnection &&
                edge.FromPackageId == integration.PackageId &&
                edge.ToPackageId == core.PackageId));
            Assert.IsFalse(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.OptionalCompanion &&
                edge.ToPackageId == integration.PackageId));
            Assert.IsTrue(graph.Edges.Any(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == suite.PackageId &&
                edge.ToPackageId == optional.PackageId));
            Assert.AreEqual(1, graph.SuiteRegions.Count);
            CollectionAssert.Contains(graph.SuiteRegions[0].MemberPackageIds.ToArray(), integration.PackageId);
        }

        [Test]
        public void Build_CreatesStructuralGroupsAndKeepsSuiteAndIntegrationAsPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "infrastructure",
                    "state-data",
                    "runtime-services",
                    "experience-interaction",
                    "ui-presentation",
                    "world-interaction",
                    "tools-quality",
                    "integrations",
                    "suites"
                },
                graph.Groups.Select(group => group.Id).ToArray());
            Assert.AreEqual(
                "experience-interaction",
                graph.Groups.Single(group => group.Id == "ui-presentation").ParentGroupId);
            Assert.AreEqual(
                "experience-interaction",
                graph.Groups.Single(group => group.Id == "world-interaction").ParentGroupId);
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.editor").GroupId);
            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == "com.deucarian.logging").GroupId);
            Assert.AreEqual("state-data", graph.Nodes.Single(node => node.PackageId == "com.deucarian.core-state").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.api").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.session").GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-loading").GroupId);
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == "com.deucarian.ui-binding").GroupId);
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == "com.deucarian.theming").GroupId);
            Assert.AreEqual("world-interaction", graph.Nodes.Single(node => node.PackageId == "com.deucarian.object-selection").GroupId);
            Assert.AreEqual("tools-quality", graph.Nodes.Single(node => node.PackageId == "com.deucarian.package-installer").GroupId);
            Assert.AreEqual("tools-quality", graph.Nodes.Single(node => node.PackageId == "com.deucarian.diagnostics").GroupId);
            Assert.AreEqual("integrations", graph.Nodes.Single(node => node.PackageId == "com.deucarian.session.api-integration").GroupId);
            Assert.AreEqual("suites", graph.Nodes.Single(node => node.PackageId == "com.deucarian.selection-suite").GroupId);
            Assert.AreEqual(PackageGraphNodeType.Integration, graph.Nodes.Single(node => node.PackageId == "com.deucarian.session.api-integration").NodeType);
            Assert.AreEqual(PackageGraphNodeType.Suite, graph.Nodes.Single(node => node.PackageId == "com.deucarian.selection-suite").NodeType);
        }

        [Test]
        public void Build_MigratesLegacyGroupAliasesToCurrentTaxonomy()
        {
            PackageGraphGroup[] groups =
            {
                new PackageGraphGroup("infrastructure", "Infrastructure", string.Empty, string.Empty, 10, string.Empty, string.Empty),
                new PackageGraphGroup("state-data", "State & Data", string.Empty, string.Empty, 20, string.Empty, string.Empty),
                new PackageGraphGroup("runtime-services", "Runtime Services", string.Empty, string.Empty, 30, string.Empty, string.Empty),
                new PackageGraphGroup("experience-interaction", "Experience & Interaction", string.Empty, string.Empty, 40, string.Empty, string.Empty),
                new PackageGraphGroup("ui-presentation", "UI & Presentation", "experience-interaction", string.Empty, 41, string.Empty, string.Empty),
                new PackageGraphGroup("world-interaction", "World Interaction", "experience-interaction", string.Empty, 42, string.Empty, string.Empty),
                new PackageGraphGroup("tools-quality", "Tools & Quality", string.Empty, string.Empty, 50, string.Empty, string.Empty),
                new PackageGraphGroup("integrations", "Integrations", string.Empty, string.Empty, 60, string.Empty, string.Empty),
                new PackageGraphGroup("suites", "Suites", string.Empty, string.Empty, 70, string.Empty, string.Empty)
            };
            PackageDefinition logging = CreatePackage(
                "Logging",
                "com.deucarian.logging",
                "Core",
                groupId: "Foundation");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.deucarian.session",
                "Core",
                ecosystemGroup: "ServicesRuntime");
            PackageDefinition ui = CreatePackage(
                "UI Binding",
                "com.deucarian.ui-binding",
                "UI",
                ecosystemGroup: "ExperienceUiWorld");

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { logging, session, ui }, groups);

            Assert.AreEqual("infrastructure", graph.Nodes.Single(node => node.PackageId == logging.PackageId).GroupId);
            Assert.AreEqual("runtime-services", graph.Nodes.Single(node => node.PackageId == session.PackageId).GroupId);
            Assert.AreEqual("ui-presentation", graph.Nodes.Single(node => node.PackageId == ui.PackageId).GroupId);
            Assert.IsFalse(graph.Groups.Any(group => string.Equals(group.DisplayName, "Foundation", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void Build_CreatesNestedExperienceInteractionCategories()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphGroup experience = graph.Groups.Single(group => group.Id == "experience-interaction");
            PackageGraphGroup uiPresentation = graph.Groups.Single(group => group.Id == "ui-presentation");
            PackageGraphGroup worldInteraction = graph.Groups.Single(group => group.Id == "world-interaction");

            Assert.IsTrue(string.IsNullOrWhiteSpace(experience.ParentGroupId));
            Assert.AreEqual(experience.Id, uiPresentation.ParentGroupId);
            Assert.AreEqual(experience.Id, worldInteraction.ParentGroupId);
            Assert.IsFalse(graph.Nodes.Any(node => node.GroupId == experience.Id));
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.ui-binding", "com.deucarian.theming" },
                graph.Nodes
                    .Where(node => node.GroupId == uiPresentation.Id)
                    .Select(node => node.PackageId)
                    .ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.object-selection" },
                graph.Nodes
                    .Where(node => node.GroupId == worldInteraction.Id)
                    .Select(node => node.PackageId)
                    .ToArray());
        }

        [Test]
        public void Build_CreatesWarningNodeForMissingOptionalRelationship()
        {
            PackageDefinition package = CreatePackage(
                "Package",
                "com.example.package",
                "Core",
                "Core",
                optionalIntegrations: new[] { "com.example.missing" });

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { package });

            PackageGraphNode missingNode = graph.Nodes.Single(node => node.PackageId == "com.example.missing");
            PackageGraphEdge warningEdge = graph.Edges.Single(edge => edge.ToPackageId == missingNode.PackageId);

            Assert.IsFalse(missingNode.IsRegistered);
            Assert.AreEqual(PackageGraphNodeStatus.Missing, missingNode.Status);
            Assert.AreEqual(PackageGraphEdgeState.Warning, warningEdge.State);

            IReadOnlyList<PackageGraphGroupNavigationRow> rows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Overview());
            PackageGraphGroupNavigationRow infrastructureRow =
                rows.Single(row => row.Id == "infrastructure");
            Assert.IsTrue(infrastructureRow.HasAttention);
            Assert.AreEqual(1, infrastructureRow.StatusSummary.AttentionCount);
            StringAssert.Contains("! 1 attention", infrastructureRow.Summary);
        }

        [Test]
        public void Build_CreatesNodeActionsFromInstallAndUpdateState()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition missing = CreatePackage("Missing", "com.example.not-installed", "Core");

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId || packageId == update.PackageId,
                    _ => PackageChannel.Development,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Development,
                            package.GetUrl(PackageChannel.Development),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Development,
                            package.GetUrl(PackageChannel.Development),
                            "1111111",
                            "1111111"))
                .Build(new[] { installed, update, missing });

            Assert.AreEqual(
                PackageGraphNodeAction.Reinstall,
                graph.Nodes.Single(node => node.PackageId == installed.PackageId).PrimaryAction);
            Assert.AreEqual(
                PackageGraphNodeAction.Update,
                graph.Nodes.Single(node => node.PackageId == update.PackageId).PrimaryAction);
            Assert.AreEqual(
                PackageGraphNodeStatus.UpdateAvailable,
                graph.Nodes.Single(node => node.PackageId == update.PackageId).Status);
            Assert.AreEqual(
                PackageGraphNodeAction.Install,
                graph.Nodes.Single(node => node.PackageId == missing.PackageId).PrimaryAction);
        }

        [Test]
        public void Build_LabelsDependencyEdgesAsDependentUsesRequiredPackage()
        {
            PackageDefinition logging = CreatePackage("Logging", "com.example.logging", "Core");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.example.session",
                "Core",
                dependencies: new[] { logging.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { logging, session });
            PackageGraphEdge dependencyEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == logging.PackageId &&
                edge.ToPackageId == session.PackageId);

            Assert.AreEqual("Session uses Logging", dependencyEdge.Label);
        }

        [Test]
        public void GraphView_UsesAttentionStylingForUpdateAvailableNodes()
        {
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == update.PackageId,
                    _ => PackageChannel.Stable,
                    package => PackageUpdateStatus.UpdateAvailable(
                        package,
                        PackageChannel.Stable,
                        package.GetUrl(PackageChannel.Stable),
                        "1111111",
                        "2222222"))
                .Build(new[] { update });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsFalse(HasGraphNode(view, update.PackageId));
            Assert.IsTrue(FindGraphGroup(view, "infrastructure").ClassListContains("dpi-graph-group--attention"));

            PackageGraphView selectedView = new PackageGraphView(_ => { }, (_, __) => { });

            selectedView.SetGraph(graph, update.PackageId, actionsEnabled: true);

            Assert.AreEqual(1, FindByClass(selectedView, "dpi-graph-node--status-update").Count);
            Assert.AreEqual(
                "!",
                FindByClass(selectedView, "dpi-graph-node__status-icon--update")
                    .OfType<Label>()
                    .Single()
                    .text);
            Label updateBadge = FindByClass(selectedView, "dpi-graph-node__badge--update")
                .OfType<Label>()
                .Single();
            Assert.AreEqual("Update available", updateBadge.text);
            Button selectedAction = FindByClass(selectedView, "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
            Assert.AreEqual("Update", selectedAction.text);
            Assert.IsTrue(selectedAction.enabledSelf);
        }

        [Test]
        public void GraphView_UsesExistingAttentionStateForGroupCards()
        {
            PackageDefinition update = CreatePackage(
                "Infrastructure Update",
                "com.example.infrastructure-update",
                "Core",
                groupId: "infrastructure");
            PackageDefinition normal = CreatePackage(
                "Runtime Service",
                "com.example.runtime-service",
                "Core",
                groupId: "runtime-services");
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == update.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "2222222")
                        : null)
                .Build(new[] { update, normal });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsTrue(FindGraphGroup(view, "infrastructure").ClassListContains("dpi-graph-group--attention"));
            Assert.IsFalse(FindGraphGroup(view, "runtime-services").ClassListContains("dpi-graph-group--attention"));
            Assert.IsNotEmpty(FindByClass(
                FindGraphGroup(view, "infrastructure"),
                "dpi-graph-group__stat--attention"));
            Assert.IsEmpty(FindByClass(
                FindGraphGroup(view, "runtime-services"),
                "dpi-graph-group__stat--attention"));

            IReadOnlyList<PackageGraphGroupNavigationRow> rows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Overview());
            Assert.IsTrue(rows.Single(row => row.Id == "infrastructure").HasAttention);
            Assert.IsFalse(rows.Single(row => row.Id == "runtime-services").HasAttention);
            StringAssert.Contains("! 1 attention", rows.Single(row => row.Id == "infrastructure").Summary);
        }

        [Test]
        public void GraphView_ExposesMajorStatusVisualClassesAndLabels()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition consumer = CreatePackage(
                "Consumer",
                "com.example.consumer",
                "Core",
                dependencies: new[] { dependency.PackageId },
                optionalIntegrations: new[] { "com.example.missing" });

            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId ||
                                 packageId == update.PackageId ||
                                 packageId == consumer.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "1111111"))
                .Build(new[] { installed, notInstalled, update, dependency, consumer });
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });
            string statusGroupId = graph.Nodes
                .Single(node => string.Equals(
                    node.PackageId,
                    installed.PackageId,
                    StringComparison.OrdinalIgnoreCase))
                .GroupId;

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                statusGroupId,
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            GetCanvas(view).SetViewportZoom(0.5f);

            PackageGraphView missingView = new PackageGraphView(_ => { }, (_, __) => { });
            missingView.SetGraph(
                graph,
                consumer.PackageId,
                consumer.PackageId,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.IsTrue(FindGraphNode(view, installed.PackageId).ClassListContains("dpi-graph-node--status-installed"));
            Assert.IsTrue(FindGraphNode(view, notInstalled.PackageId).ClassListContains("dpi-graph-node--status-available"));
            Assert.IsTrue(FindGraphNode(view, update.PackageId).ClassListContains("dpi-graph-node--status-update"));
            Assert.IsTrue(FindGraphNode(view, dependency.PackageId).ClassListContains("dpi-graph-node--status-warning"));
            Assert.IsTrue(FindGraphNode(missingView, "com.example.missing").ClassListContains("dpi-graph-node--status-missing"));
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, installed.PackageId), "dpi-graph-node__status-rail--installed").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, notInstalled.PackageId), "dpi-graph-node__status-icon--available").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, update.PackageId), "dpi-graph-node__status-icon--update").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(view, dependency.PackageId), "dpi-graph-node__status-icon--warning").Count);
            Assert.AreEqual(1, FindByClass(FindGraphNode(missingView, "com.example.missing"), "dpi-graph-node__status-icon--missing").Count);
            Assert.AreEqual(
                "\u2713",
                FindByClass(FindGraphNode(view, installed.PackageId), "dpi-graph-node__status-icon--installed")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.AreEqual(
                "\u25CB",
                FindByClass(FindGraphNode(view, notInstalled.PackageId), "dpi-graph-node__status-icon--available")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.AreEqual(
                "!",
                FindByClass(FindGraphNode(view, update.PackageId), "dpi-graph-node__status-icon--update")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.IsEmpty(FindByClass(view, "dpi-graph-node__badge--warning"));
            Assert.IsEmpty(FindByClass(view, "dpi-graph-node__badge--missing"));
            Assert.IsEmpty(FindByClass(view, "deucarian-badge"));
        }

        [Test]
        public void CategoryStatusSummary_ClassifiesPackagesIntoMutuallyExclusiveBuckets()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition consumer = CreatePackage(
                "Consumer",
                "com.example.consumer",
                "Core",
                dependencies: new[] { dependency.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId ||
                                 packageId == update.PackageId ||
                                 packageId == consumer.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "1111111"))
                .Build(new[] { installed, update, notInstalled, dependency, consumer });

            PackageGraphCategoryStatusSummary summary =
                PackageGraphCategoryStatusSummary.Create(graph.Nodes);

            Assert.AreEqual(2, summary.InstalledCount);
            Assert.AreEqual(1, summary.NotInstalledCount);
            Assert.AreEqual(2, summary.AttentionCount);
            Assert.AreEqual(0, summary.UnknownCount);
            Assert.AreEqual(graph.Nodes.Count, summary.TotalCount);
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                PackageGraphCategoryStatusClassifier.Classify(
                    graph.Nodes.Single(node => node.PackageId == update.PackageId)));
            Assert.AreEqual(
                PackageGraphCategoryStatusKey.Attention,
                PackageGraphCategoryStatusClassifier.Classify(
                    graph.Nodes.Single(node => node.PackageId == dependency.PackageId)));
        }

        [Test]
        public void CategoryStatusSlices_AreOrderedAndProportionalToCounts()
        {
            IReadOnlyList<CategoryStatusSlice> mixedSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(2, 1, 1, 0));
            IReadOnlyList<CategoryStatusSlice> singleSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(0, 4, 0, 0));
            IReadOnlyList<CategoryStatusSlice> futureSlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(1, 1, 1, 1));
            IReadOnlyList<CategoryStatusSlice> emptySlices =
                PackageGraphCategoryStatusVisuals.CreateSlices(
                    new PackageGraphCategoryStatusSummary(0, 0, 0, 0));

            CollectionAssert.AreEqual(
                new[]
                {
                    PackageGraphCategoryStatusKey.Installed,
                    PackageGraphCategoryStatusKey.NotInstalled,
                    PackageGraphCategoryStatusKey.Attention
                },
                mixedSlices.Select(slice => slice.StatusKey).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 1, 1 }, mixedSlices.Select(slice => slice.Count).ToArray());
            Assert.AreEqual(4, mixedSlices.Sum(slice => slice.Count));
            Assert.AreEqual(0.50f, mixedSlices[0].Count / 4f, 0.001f);
            Assert.AreEqual(0.25f, mixedSlices[1].Count / 4f, 0.001f);
            Assert.AreEqual(0.25f, mixedSlices[2].Count / 4f, 0.001f);
            Assert.AreEqual(1, singleSlices.Count);
            Assert.AreEqual(PackageGraphCategoryStatusKey.NotInstalled, singleSlices[0].StatusKey);
            Assert.AreEqual(4, futureSlices.Count);
            Assert.IsTrue(futureSlices.Any(slice => slice.StatusKey == PackageGraphCategoryStatusKey.Unknown));
            Assert.IsEmpty(emptySlices);
        }

        [Test]
        public void CategoryStatusRingSegments_EmptyCategoryRendersCompleteNeutralRing()
        {
            IReadOnlyList<CategoryStatusRingSegment> segments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(0, 0, 0, 0));

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(PackageGraphCategoryStatusKey.Unknown, segments[0].StatusKey);
            Assert.IsTrue(segments[0].FullRing);
            Assert.AreEqual(360f, segments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(0f, segments[0].SeparatorAfterDegrees, 0.001f);
        }

        [TestCase(1, 0, 0, PackageGraphCategoryStatusKey.Installed)]
        [TestCase(2, 0, 0, PackageGraphCategoryStatusKey.Installed)]
        [TestCase(0, 1, 0, PackageGraphCategoryStatusKey.NotInstalled)]
        [TestCase(0, 0, 1, PackageGraphCategoryStatusKey.Attention)]
        public void CategoryStatusRingSegments_SingleNonZeroStatusRendersOneCompleteRing(
            int installed,
            int notInstalled,
            int attention,
            PackageGraphCategoryStatusKey expectedStatus)
        {
            IReadOnlyList<CategoryStatusRingSegment> segments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(installed, notInstalled, attention, 0));

            Assert.AreEqual(1, segments.Count);
            Assert.AreEqual(expectedStatus, segments[0].StatusKey);
            Assert.IsTrue(segments[0].FullRing);
            Assert.AreEqual(360f, segments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(0f, segments[0].SeparatorAfterDegrees, 0.001f);
        }

        [Test]
        public void CategoryStatusRingSegments_MultipleStatusesUseOnlyConfiguredSeparators()
        {
            IReadOnlyList<CategoryStatusRingSegment> equalSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(1, 1, 0, 0));
            IReadOnlyList<CategoryStatusRingSegment> mixedSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    new PackageGraphCategoryStatusSummary(2, 1, 1, 0));

            Assert.AreEqual(2, equalSegments.Count);
            Assert.IsTrue(equalSegments.All(segment => !segment.FullRing));
            Assert.AreEqual(
                180f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees,
                equalSegments[0].SweepDegrees,
                0.001f);
            Assert.AreEqual(
                180f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees,
                equalSegments[1].SweepDegrees,
                0.001f);
            Assert.AreEqual(
                360f,
                equalSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);

            Assert.AreEqual(3, mixedSegments.Count);
            float usableAngle = 360f - PackageGraphCategoryStatusVisuals.StatusRingSeparatorDegrees * 3f;
            Assert.AreEqual(usableAngle * 0.50f, mixedSegments[0].SweepDegrees, 0.001f);
            Assert.AreEqual(usableAngle * 0.25f, mixedSegments[1].SweepDegrees, 0.001f);
            Assert.AreEqual(usableAngle * 0.25f, mixedSegments[2].SweepDegrees, 0.001f);
            Assert.AreEqual(
                360f,
                mixedSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);
        }

        [Test]
        public void CategoryStatusRingSegments_BackHintAndHoverDoNotChangeCoverage()
        {
            PackageGraphCategoryStatusSummary summary = new PackageGraphCategoryStatusSummary(2, 1, 1, 0);
            IReadOnlyList<CategoryStatusRingSegment> normalSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(summary);
            CategoryStatusRingVisualState hoveredRing = new CategoryStatusRingVisualState(
                "group:test:status",
                new Vector2(10f, 12f),
                44f,
                5f,
                PackageGraphCategoryStatusVisuals.CreateSlices(summary),
                true);
            IReadOnlyList<CategoryStatusRingSegment> hoveredSegments =
                PackageGraphCategoryStatusVisuals.CreateRingSegments(
                    hoveredRing.Slices,
                    hoveredRing.TotalCount);

            Assert.AreEqual(
                normalSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                hoveredSegments.Sum(segment => segment.SweepDegrees + segment.SeparatorAfterDegrees),
                0.001f);
            Assert.AreEqual(10f, hoveredRing.Center.x, 0.001f);
            Assert.AreEqual(12f, hoveredRing.Center.y, 0.001f);
            Assert.AreEqual(88f, hoveredRing.Radius * 2f, 0.001f);
            Assert.That(hoveredRing.Radius, Is.GreaterThan(hoveredRing.Thickness));
        }

        [Test]
        public void CategoryStatusRings_AggregateRootAndNestedCategoryPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, string.Empty, string.Empty, true);

            IReadOnlyList<CategoryStatusRingVisualState> rings = canvas.StatusRingVisualStatesForTests;
            CategoryStatusRingVisualState rootRing =
                rings.Single(ring => ring.RingId == "root:status");
            CategoryStatusRingVisualState experienceRing =
                rings.Single(ring => ring.RingId == "group:experience-interaction:status");

            Assert.AreEqual(graph.Nodes.Count, rootRing.TotalCount);
            Assert.AreEqual(3, experienceRing.TotalCount);
            Assert.AreEqual(
                3,
                experienceRing.Slices.Single(slice => slice.StatusKey == PackageGraphCategoryStatusKey.NotInstalled).Count);
            Assert.That(experienceRing.Radius, Is.GreaterThan(experienceRing.Thickness));
            Assert.That(
                Vector2.Distance(
                    canvas.AnimatedGroupCentersForTests["experience-interaction"],
                    experienceRing.Center),
                Is.LessThan(0.1f));
        }

        [Test]
        public void Layout_PlacesTopLevelGroupsOnOneGlobalOrbit()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);
            PackageGraphGroupLayoutNode[] topGroups = layout.GroupNodes
                .Where(groupNode => !groupNode.Collapsed)
                .OrderBy(groupNode => groupNode.Group.SortOrder)
                .ToArray();
            float globalRadius = Vector2.Distance(topGroups[0].HubCenter, PackageGraphLayout.GraphCenter);

            Assert.AreEqual(7, topGroups.Length);
            Assert.That(globalRadius, Is.InRange(320f, 380f));
            Assert.IsEmpty(layout.NodeRects);
            Assert.IsEmpty(layout.NodeRings);
            Assert.IsEmpty(layout.NodePresentationLevels);
            foreach (PackageGraphGroupLayoutNode groupNode in topGroups)
            {
                Assert.That(
                    Vector2.Distance(groupNode.HubCenter, PackageGraphLayout.GraphCenter),
                    Is.EqualTo(globalRadius).Within(0.1f),
                    groupNode.GroupId);
                Assert.AreEqual(0f, groupNode.OrbitRadius);
            }

            PackageGraphGroupLayoutNode infrastructure = topGroups.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode experience = topGroups.Single(groupNode => groupNode.GroupId == "experience-interaction");

            Assert.AreEqual(2, infrastructure.PackageCount);
            Assert.AreEqual(3, experience.PackageCount);
            AssertNoOverlaps(layout.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { layout.HubRect }).ToArray());
        }

        [Test]
        public void GraphCanvas_RootOverviewFitBoundsUseSummaryGroupsAndHub()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            Vector2 compactViewport = new Vector2(900f, 620f);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                compactViewport,
                PackageGraphNodePresentationLevel.Micro);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportSize(compactViewport);
            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true);

            Assert.IsEmpty(canvas.NodeRectsForTests);
            AssertRectsEqual(CreateExpectedFitBounds(layout), canvas.GetContentBounds(), 0.1f);
        }

        [Test]
        public void VisibilityFilter_UpdateAvailablePackagesCountAsInstalled()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition update = CreatePackage("Update", "com.example.update", "Core");
            PackageDefinition absent = CreatePackage("Absent", "com.example.absent", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(
                    packageId => packageId == installed.PackageId || packageId == update.PackageId,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == update.PackageId
                        ? PackageUpdateStatus.UpdateAvailable(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "2222222")
                        : PackageUpdateStatus.UpToDate(
                            package,
                            PackageChannel.Stable,
                            package.GetUrl(PackageChannel.Stable),
                            "1111111",
                            "1111111"))
                .Build(new[] { installed, update, absent });
            PackageVisibilityFilterState installedOnly = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);

            HashSet<string> visibleIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, installedOnly);
            PackageVisibilityFilterCounts counts = PackageVisibilityFilter.CalculateCounts(graph, installedOnly);

            Assert.AreEqual(2, counts.InstalledCount);
            Assert.AreEqual(1, counts.NotInstalledCount);
            Assert.AreEqual(2, counts.VisibleCount);
            CollectionAssert.Contains(visibleIds, update.PackageId);
            CollectionAssert.DoesNotContain(visibleIds, absent.PackageId);
        }

        [Test]
        public void VisibilityFilter_CreateVisibleGraphRemovesHiddenNodesAndEdges()
        {
            PackageDefinition logging = CreatePackage("Logging", "com.example.logging", "Core");
            PackageDefinition session = CreatePackage(
                "Session",
                "com.example.session",
                "Core",
                dependencies: new[] { logging.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == logging.PackageId)
                .Build(new[] { logging, session });
            PackageVisibilityFilterState installedOnly = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);

            HashSet<string> visibleIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, installedOnly);
            PackageGraphModel visibleGraph = PackageVisibilityFilter.CreateVisibleGraph(graph, visibleIds);

            Assert.AreEqual(1, visibleGraph.Nodes.Count);
            Assert.AreEqual(logging.PackageId, visibleGraph.Nodes[0].PackageId);
            Assert.IsEmpty(visibleGraph.Edges);
        }

        [Test]
        public void GraphCanvas_StatusFilteredRootOverviewKeepsBaselineGeometryAndSummarizesShownPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            Vector2 compactViewport = new Vector2(900f, 620f);
            HashSet<string> visibleIds = new HashSet<string>(
                new[]
                {
                    "com.deucarian.logging",
                    "com.deucarian.session"
                },
                StringComparer.OrdinalIgnoreCase);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportSize(compactViewport);
            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true);
            Rect baselineBounds = canvas.GetContentBounds();
            Dictionary<string, Rect> baselineGroupRects = GetCanvasGroupRects(canvas);

            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true, visibleIds);

            Assert.IsEmpty(canvas.NodeRectsForTests);
            PackageGraphGroupLayoutNode infrastructure =
                canvas.GroupLayoutNodesForTests.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode runtime =
                canvas.GroupLayoutNodesForTests.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode state =
                canvas.GroupLayoutNodesForTests.Single(groupNode => groupNode.GroupId == "state-data");
            Assert.AreEqual(1, infrastructure.PackageCount);
            Assert.AreEqual(1, runtime.PackageCount);
            Assert.AreEqual(0, state.PackageCount);
            AssertGroupRectsEqual(baselineGroupRects, GetCanvasGroupRects(canvas), 0.001f);
            AssertRectsEqual(baselineBounds, canvas.GetContentBounds(), 0.001f);
            Assert.IsFalse(canvas.LayoutTransitionActiveForTests);
        }

        [Test]
        public void VisibilityFilter_HidingFocusedPackageKeepsPackageEgoCanvas()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            HashSet<string> visibleIds = new HashSet<string>(
                graph.Nodes
                    .Select(node => node.PackageId)
                    .Where(packageId => !string.Equals(
                        packageId,
                        "com.deucarian.session",
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                actionsEnabled: true,
                visibleIds);

            Assert.IsTrue(PackageInstallerWindow.ShouldClearGraphSelectionForFilters(
                "com.deucarian.session",
                string.Empty,
                visibleIds));
            Assert.IsFalse(PackageInstallerWindow.ShouldClearGraphSelectionForFilters(
                "com.deucarian.session",
                "com.deucarian.session",
                visibleIds));
            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            Assert.AreEqual("com.deucarian.session", canvas.LayoutFocusPackageId);
            Assert.IsTrue(canvas.NodeRectsForTests.ContainsKey("com.deucarian.session"));
        }

        [Test]
        public void VisibilityFilter_HidingSelectedPackageStillClearsStructuralSelection()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            HashSet<string> visibleIds = new HashSet<string>(
                graph.Nodes
                    .Select(node => node.PackageId)
                    .Where(packageId => !string.Equals(
                        packageId,
                        "com.deucarian.session",
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                "com.deucarian.session",
                string.Empty,
                actionsEnabled: true,
                visibleIds);

            Assert.IsTrue(PackageInstallerWindow.ShouldClearGraphSelectionForFilters(
                "com.deucarian.session",
                string.Empty,
                visibleIds));
            Assert.AreEqual(PackageGraphLayoutMode.Overview, canvas.LayoutMode);
            Assert.IsFalse(canvas.NodeRectsForTests.ContainsKey("com.deucarian.session"));
        }

        [Test]
        public void VisibilityFilter_CountsHiddenRelatedPackages()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            HashSet<string> visibleIds = new HashSet<string>(
                graph.Nodes
                    .Select(node => node.PackageId)
                    .Where(packageId => !string.Equals(
                        packageId,
                        "com.deucarian.logging",
                        StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

            int hiddenRelatedCount = PackageVisibilityFilter.CountHiddenRelatedPackages(
                graph,
                "com.deucarian.session",
                visibleIds);

            Assert.AreEqual(1, hiddenRelatedCount);
        }

        [Test]
        public void GraphView_FilterEmptyStateDistinguishesDisabledTogglesFromNoMatches()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState disabledState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: false,
                showNotInstalled: false);
            PackageGraphView disabledView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                disabledState,
                null);
            HashSet<string> disabledIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, disabledState);

            disabledView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                disabledIds,
                PackageVisibilityFilter.CalculateCounts(graph, disabledState),
                hiddenRelatedCount: 0);

            Assert.IsEmpty(FindByClass(disabledView, "dpi-graph-node"));
            Assert.AreEqual(
                "No package visibility filters selected.",
                FindByClass(disabledView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);

            PackageVisibilityFilterState searchState = new PackageVisibilityFilterState(
                "no-such-package",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                searchState,
                null);
            HashSet<string> searchIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, searchState);
            PackageGraphSearchState graphSearchState = PackageGraphSearchIndex.Create(graph, searchState);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                searchIds,
                graphSearchState,
                PackageVisibilityFilter.CalculateCounts(graph, searchState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(
                "No categories or packages match the current search.",
                FindByClass(searchView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);
        }

        [Test]
        public void GraphSearch_PackageMatchIncludesOnlyItsFixedCategoryPath()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Theming",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);

            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.theming"));
            Assert.IsTrue(searchState.IsCategoryContext("experience-interaction"));
            Assert.IsTrue(searchState.IsCategoryContext("ui-presentation"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.theming"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.editor"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.logging"));
            CollectionAssert.AreEquivalent(
                new[] { "experience-interaction", "ui-presentation" },
                searchState.ContextCategoryIds.ToArray());
        }

        [Test]
        public void GraphSearch_CategoryMatchHighlightsCategoryAndAncestorsWithoutExpandingDescendants()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Experience Interaction",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);

            Assert.IsTrue(searchState.IsDirectCategoryMatch("experience-interaction"));
            Assert.IsTrue(searchState.IsCategoryContext("experience-interaction"));
            Assert.IsFalse(searchState.IsCategoryContext("ui-presentation"));
            Assert.IsFalse(searchState.IsCategoryContext("world-interaction"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.ui-binding"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.theming"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.object-selection"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.session.api-integration"));
            CollectionAssert.AreEquivalent(
                new[] { "experience-interaction" },
                searchState.ContextCategoryIds.ToArray());
            Assert.IsEmpty(searchState.ContextPackageIds);
        }

        [Test]
        public void GraphSearch_MultipleResultsRemainDirectMatchesWithoutStructuralExpansion()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Integration",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);

            Assert.IsTrue(searchState.IsDirectCategoryMatch("integrations"));
            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.session.api-integration"));
            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.object-loading.api-integration"));
            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.ui-binding.core-state-integration"));
            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.object-selection.core-state-integration"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(searchState.IsPackageContext("com.deucarian.api"));
            Assert.AreEqual(1, searchState.DirectCategoryMatchCount);
            Assert.AreEqual(4, searchState.DirectPackageMatchCount);
            Assert.AreEqual(PackageGraphSearchResultType.Category, searchState.BestResult.Type);
            Assert.AreEqual("integrations", searchState.BestResult.Id);
        }

        [Test]
        public void GraphSearch_PackageMatchDoesNotExpandRelationships()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState loggingFilterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState loggingSearch = PackageGraphSearchIndex.Create(
                graph,
                loggingFilterState);

            Assert.IsTrue(loggingSearch.IsDirectPackageMatch("com.deucarian.logging"));
            Assert.IsTrue(loggingSearch.IsPackageContext("com.deucarian.logging"));
            Assert.IsFalse(loggingSearch.IsDirectPackageMatch("com.deucarian.session"));
            Assert.IsFalse(loggingSearch.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(loggingSearch.IsPackageContext("com.deucarian.api"));
            PackageVisibilityFilterState integrationFilterState = new PackageVisibilityFilterState(
                "integration",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphSearchState integrationSearch = PackageGraphSearchIndex.Create(
                graph,
                integrationFilterState);

            Assert.IsTrue(integrationSearch.IsDirectCategoryMatch("integrations"));
            Assert.IsTrue(integrationSearch.IsDirectPackageMatch("com.deucarian.session.api-integration"));
            Assert.IsTrue(integrationSearch.IsDirectPackageMatch("com.deucarian.object-loading.api-integration"));
            Assert.IsFalse(integrationSearch.IsDirectPackageMatch("com.deucarian.session"));
            Assert.IsFalse(integrationSearch.IsPackageContext("com.deucarian.session"));
            Assert.IsFalse(integrationSearch.IsPackageContext("com.deucarian.api"));
        }

        [Test]
        public void GraphSearch_StatusFiltersRetainLexicalMatchesOutsideTheRenderProjection()
        {
            PackageGraphModel graph = new PackageGraphBuilder(packageId =>
                    string.Equals(packageId, "com.deucarian.logging", StringComparison.OrdinalIgnoreCase))
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: false,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);

            Assert.IsTrue(searchState.IsDirectPackageMatch("com.deucarian.logging"));
            Assert.IsTrue(searchState.IsPackageContext("com.deucarian.logging"));
            Assert.IsTrue(searchState.IsCategoryContext("infrastructure"));
            CollectionAssert.DoesNotContain(visiblePackageIds, "com.deucarian.logging");
            Assert.AreEqual("com.deucarian.logging", searchState.BestResult.Id);
        }

        [TestCase(KeyCode.Return, "Logging", "com.deucarian.logging", "")]
        [TestCase(KeyCode.KeypadEnter, "Integration", "", "integrations")]
        public void GraphSearch_SearchFieldCommitsRankedBestResultOnlyOnEnter(
            KeyCode keyCode,
            string query,
            string expectedPackageId,
            string expectedGroupId)
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            PackageDefinition selectedPackage = null;
            PackageGraphGroup focusedGroup = null;
            int filterChangedCount = 0;
            PackageGraphView view = new PackageGraphView(
                package => selectedPackage = package,
                (_, __) => { },
                selectionCleared: null,
                rootFocused: null,
                groupFocused: group => focusedGroup = group,
                filterState: filterState,
                filterChanged: () => filterChangedCount++);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            TextField searchField = FindByClass(view, "dpi-ecosystem-graph__search")
                .OfType<TextField>()
                .Single();

            view.ApplySearchTextForTests(query);

            Assert.AreEqual(query, filterState.SearchText);
            Assert.AreEqual(query, searchField.value);
            Assert.AreEqual(1, filterChangedCount);
            Assert.IsNull(selectedPackage, "Typing must not select a package.");
            Assert.IsNull(focusedGroup, "Typing must not focus a category.");

            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.IsNull(selectedPackage, "Rendering search highlights must not select a package.");
            Assert.IsNull(focusedGroup, "Rendering search highlights must not focus a category.");
            Assert.AreEqual(
                string.IsNullOrWhiteSpace(expectedGroupId)
                    ? PackageGraphSearchResultType.Package
                    : PackageGraphSearchResultType.Category,
                searchState.BestResult.Type);
            Assert.AreEqual(
                string.IsNullOrWhiteSpace(expectedGroupId) ? expectedPackageId : expectedGroupId,
                searchState.BestResult.Id);

            Assert.IsTrue(view.ActivateBestSearchResultForTests(keyCode));

            Assert.AreEqual(
                expectedPackageId,
                selectedPackage != null ? selectedPackage.PackageId : string.Empty);
            Assert.AreEqual(
                expectedGroupId,
                focusedGroup != null ? focusedGroup.Id : string.Empty);
        }

        [Test]
        public void GraphSearch_RootQueriesKeepHubGroupsBoundsCameraAndLayoutStable()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            PackageGraphCanvas canvas = GetCanvas(searchView);
            Dictionary<string, Rect> baselineGroupRects = GetCanvasGroupRects(canvas);
            Rect baselineHubRect = GetInlineRect(FindByClass(searchView, "dpi-graph-hub").Single());
            Rect baselineBounds = canvas.GetContentBounds();
            Vector2 baselineCenter = canvas.GetActiveCenter();
            PackageGraphCameraState baselineCamera = new PackageGraphCameraState(new Vector2(83f, -41f), 0.91f);
            searchView.ApplyCameraForTests(baselineCamera);

            string[] queries =
            {
                "Logging",
                "Infrastructure",
                "Integration",
                "no-such-package",
                "L",
                "Lo",
                "Log",
                "Logging",
                string.Empty
            };

            foreach (string query in queries)
            {
                filterState.SetSearchText(query);
                PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
                searchView.SetGraph(
                    graph,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    actionsEnabled: true,
                    visiblePackageIds,
                    searchState,
                    PackageVisibilityFilter.CalculateCounts(graph, filterState),
                    hiddenRelatedCount: 0);

                Assert.AreEqual(PackageGraphLayoutMode.Overview, canvas.LayoutMode, query);
                Assert.IsEmpty(canvas.NodeRectsForTests, query + " added package satellites at root.");
                AssertRectsEqual(
                    baselineHubRect,
                    GetInlineRect(FindByClass(searchView, "dpi-graph-hub").Single()),
                    0.001f);
                AssertGroupRectsEqual(baselineGroupRects, GetCanvasGroupRects(canvas), 0.001f);
                AssertRectsEqual(baselineBounds, canvas.GetContentBounds(), 0.001f);
                Assert.That(Vector2.Distance(baselineCenter, canvas.GetActiveCenter()), Is.LessThan(0.001f));
                AssertCameraClose(baselineCamera, searchView.CameraStateForTests);
                Assert.IsFalse(searchView.CameraTransitionActiveForTests, query);
                Assert.IsFalse(searchView.LayoutTransitionActiveForTests, query);

                if (string.Equals(query, "Logging", StringComparison.Ordinal))
                {
                    VisualElement infrastructure = FindGraphGroup(searchView, "infrastructure");
                    Assert.IsTrue(infrastructure.ClassListContains("dpi-graph-search--context"));
                    Assert.AreEqual(
                        "1 match",
                        FindByClass(infrastructure, "dpi-graph-group__subtitle").OfType<Label>().Single().text);
                    Assert.IsTrue(
                        FindGraphGroup(searchView, "tools-quality")
                            .ClassListContains("dpi-graph-search--dimmed"));
                    Assert.AreEqual(
                        "1 matching",
                        FindByClass(searchView, "dpi-ecosystem-graph__visible-count")
                            .OfType<Label>()
                            .Single()
                            .text);
                }
                else if (string.Equals(query, "Infrastructure", StringComparison.Ordinal))
                {
                    VisualElement infrastructure = FindGraphGroup(searchView, "infrastructure");
                    Assert.IsTrue(infrastructure.ClassListContains("dpi-graph-search--match"));
                    Assert.AreEqual(
                        "1 match",
                        FindByClass(infrastructure, "dpi-graph-group__subtitle").OfType<Label>().Single().text);
                }
                else if (string.Equals(query, "Integration", StringComparison.Ordinal))
                {
                    VisualElement integrations = FindGraphGroup(searchView, "integrations");
                    Assert.IsTrue(integrations.ClassListContains("dpi-graph-search--match"));
                    Assert.AreEqual(
                        "5 matches",
                        FindByClass(integrations, "dpi-graph-group__subtitle").OfType<Label>().Single().text);
                }
                else if (string.Equals(query, "no-such-package", StringComparison.Ordinal))
                {
                    Assert.IsTrue(
                        FindByClass(searchView, "dpi-graph-group")
                            .All(group => group.ClassListContains("dpi-graph-search--dimmed")));
                    Assert.AreEqual(
                        "No categories or packages match the current search.",
                        FindByClass(searchView, "dpi-ecosystem-graph__empty-title")
                            .OfType<Label>()
                            .Single()
                            .text);
                }
            }
        }

        [Test]
        public void GraphSearch_GroupFocusDimsWithoutReflowAndStatusHidesWithoutMovingSurvivors()
        {
            PackageGraphModel graph = new PackageGraphBuilder(packageId =>
                    string.Equals(packageId, "com.deucarian.logging", StringComparison.OrdinalIgnoreCase))
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { }, null, filterState, null);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            PackageGraphCanvas canvas = GetCanvas(view);
            Rect loggingRect = canvas.NodeRectsForTests["com.deucarian.logging"];
            Rect editorRect = canvas.NodeRectsForTests["com.deucarian.editor"];
            Rect baselineBounds = canvas.GetContentBounds();
            Dictionary<string, Rect> baselineGroupRects = GetCanvasGroupRects(canvas);
            PackageGraphCameraState baselineCamera = new PackageGraphCameraState(new Vector2(-57f, 29f), 0.84f);
            view.ApplyCameraForTests(baselineCamera);

            filterState.SetSearchText("Logging");
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            AssertRectsEqual(loggingRect, canvas.NodeRectsForTests["com.deucarian.logging"], 0.001f);
            AssertRectsEqual(editorRect, canvas.NodeRectsForTests["com.deucarian.editor"], 0.001f);
            Assert.IsTrue(FindGraphNode(view, "com.deucarian.logging").ClassListContains("dpi-graph-search--match"));
            Assert.IsTrue(FindGraphNode(view, "com.deucarian.editor").ClassListContains("dpi-graph-search--dimmed"));
            AssertGroupRectsEqual(baselineGroupRects, GetCanvasGroupRects(canvas), 0.001f);
            AssertRectsEqual(baselineBounds, canvas.GetContentBounds(), 0.001f);
            AssertCameraClose(baselineCamera, view.CameraStateForTests);
            Assert.IsFalse(view.CameraTransitionActiveForTests);
            Assert.IsFalse(view.LayoutTransitionActiveForTests);

            filterState.Set("Logging", showInstalled: true, showNotInstalled: false);
            HashSet<string> installedIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                installedIds,
                PackageGraphSearchIndex.Create(graph, filterState),
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.IsTrue(HasGraphNode(view, "com.deucarian.logging"));
            Assert.IsFalse(HasGraphNode(view, "com.deucarian.editor"));
            AssertRectsEqual(loggingRect, canvas.NodeRectsForTests["com.deucarian.logging"], 0.001f);
            Assert.IsFalse(canvas.NodeRectsForTests.ContainsKey("com.deucarian.editor"));
            AssertGroupRectsEqual(baselineGroupRects, GetCanvasGroupRects(canvas), 0.001f);
            AssertRectsEqual(baselineBounds, canvas.GetContentBounds(), 0.001f);
            AssertCameraClose(baselineCamera, view.CameraStateForTests);
            Assert.IsFalse(view.CameraTransitionActiveForTests);
            Assert.IsFalse(view.LayoutTransitionActiveForTests);
        }

        [Test]
        public void GraphSearch_HoveringDirectMatchDoesNotRevealRelationshipContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.IsFalse(HasGraphNode(searchView, "com.deucarian.session"));

            searchView.PreviewPackageHoverForTests("com.deucarian.logging");

            Assert.IsFalse(HasGraphNode(searchView, "com.deucarian.session"));
            Assert.AreEqual(
                "1 matching",
                FindByClass(searchView, "dpi-ecosystem-graph__visible-count")
                    .OfType<Label>()
                    .Single()
                    .text);

            searchView.ClearPackageHoverForTests("com.deucarian.logging");

            Assert.IsFalse(HasGraphNode(searchView, "com.deucarian.session"));
        }

        [Test]
        public void GraphSearch_CategoryFocusPreservesGeometryAndReportsMatchesOutsideTheGroup()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphView searchView = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "experience-interaction",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            PackageGraphCanvas canvas = GetCanvas(searchView);
            Dictionary<string, Rect> baselineGroupRects = GetCanvasGroupRects(canvas);
            Rect baselineBounds = canvas.GetContentBounds();

            filterState.SetSearchText("Logging");
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
            searchView.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "experience-interaction",
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(PackageGraphLayoutMode.GroupFocus, canvas.LayoutMode);
            Assert.AreEqual("experience-interaction", canvas.LayoutFocusGroupId);
            Assert.IsNotNull(FindGraphGroup(searchView, "experience-interaction"));
            Assert.IsFalse(HasGraphNode(searchView, "com.deucarian.logging"));
            Assert.IsTrue(
                FindGraphGroup(searchView, "experience-interaction")
                    .ClassListContains("dpi-graph-search--dimmed"));
            AssertGroupRectsEqual(baselineGroupRects, GetCanvasGroupRects(canvas), 0.001f);
            AssertRectsEqual(baselineBounds, canvas.GetContentBounds(), 0.001f);
            Assert.IsFalse(searchView.LayoutTransitionActiveForTests);
            Assert.AreEqual(
                "No matches in this group.",
                FindByClass(searchView, "dpi-ecosystem-graph__empty-title")
                    .OfType<Label>()
                    .Single()
                    .text);
        }

        [Test]
        public void GraphSearch_PackageEgoSuspendsSearchProjection()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);

            view.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                "runtime-services",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            PackageGraphCanvas canvas = GetCanvas(view);
            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            PackageGraphCameraState stableEgoCamera = new PackageGraphCameraState(new Vector2(128f, -64f), 0.86f);
            view.ApplyCameraForTests(stableEgoCamera);
            string[] unfilteredNodeIds = canvas.NodeRectsForTests.Keys
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, Rect> unfilteredRects = canvas.NodeRectsForTests
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            string[] unfilteredRouteIds = canvas.EdgeRoutesForTests
                .Select(CreateRouteId)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PackageVisibilityFilterState activeSearch = new PackageVisibilityFilterState(
                "Theming",
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> searchVisibleIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, activeSearch);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, activeSearch);

            view.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                "runtime-services",
                actionsEnabled: true,
                searchVisibleIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, activeSearch),
                hiddenRelatedCount: 0);

            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            Assert.AreEqual("com.deucarian.session", canvas.LayoutFocusPackageId);
            AssertCameraClose(stableEgoCamera, view.CameraStateForTests);
            CollectionAssert.AreEqual(
                unfilteredNodeIds,
                canvas.NodeRectsForTests.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());

            foreach (string packageId in unfilteredNodeIds)
            {
                AssertRectsEqual(unfilteredRects[packageId], canvas.NodeRectsForTests[packageId], 0.001f);
            }

            CollectionAssert.AreEqual(
                unfilteredRouteIds,
                canvas.EdgeRoutesForTests
                    .Select(CreateRouteId)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

            PackageVisibilityFilterState hiddenStatusFilter = new PackageVisibilityFilterState(
                "Theming",
                showInstalled: false,
                showNotInstalled: false);
            HashSet<string> hiddenVisibleIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, hiddenStatusFilter);
            PackageGraphSearchState hiddenSearchState = PackageGraphSearchIndex.Create(graph, hiddenStatusFilter);

            view.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                "runtime-services",
                actionsEnabled: true,
                hiddenVisibleIds,
                hiddenSearchState,
                PackageVisibilityFilter.CalculateCounts(graph, hiddenStatusFilter),
                hiddenRelatedCount: 0);

            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            Assert.AreEqual("com.deucarian.session", canvas.LayoutFocusPackageId);
            AssertCameraClose(stableEgoCamera, view.CameraStateForTests);
            CollectionAssert.AreEqual(
                unfilteredNodeIds,
                canvas.NodeRectsForTests.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());

            foreach (string packageId in unfilteredNodeIds)
            {
                AssertRectsEqual(unfilteredRects[packageId], canvas.NodeRectsForTests[packageId], 0.001f);
            }

            CollectionAssert.AreEqual(
                unfilteredRouteIds,
                canvas.EdgeRoutesForTests
                    .Select(CreateRouteId)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

            PackageVisibilityFilterState clearedSearchHiddenStatusFilter = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: false,
                showNotInstalled: false);
            HashSet<string> clearedHiddenVisibleIds =
                PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, clearedSearchHiddenStatusFilter);

            view.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                "runtime-services",
                actionsEnabled: true,
                clearedHiddenVisibleIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, clearedSearchHiddenStatusFilter),
                hiddenRelatedCount: 0);

            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            Assert.AreEqual("com.deucarian.session", canvas.LayoutFocusPackageId);
            AssertCameraClose(stableEgoCamera, view.CameraStateForTests);
            CollectionAssert.AreEqual(
                unfilteredNodeIds,
                canvas.NodeRectsForTests.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());

            view.SetGraph(
                graph,
                "com.deucarian.session",
                "com.deucarian.session",
                "runtime-services",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Assert.AreEqual(PackageGraphLayoutMode.Focus, canvas.LayoutMode);
            Assert.AreEqual("com.deucarian.session", canvas.LayoutFocusPackageId);
            AssertCameraClose(stableEgoCamera, view.CameraStateForTests);
            CollectionAssert.AreEqual(
                unfilteredNodeIds,
                canvas.NodeRectsForTests.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        [Test]
        public void GraphView_FilterChipsUseStatusIconsInsteadOfBracketMarkers()
        {
            PackageDefinition installed = CreatePackage("Installed", "com.example.installed", "Core");
            PackageDefinition notInstalled = CreatePackage("Not Installed", "com.example.not-installed", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == installed.PackageId)
                .Build(new[] { installed, notInstalled });
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                null,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            Button[] filterButtons = FindByClass(view, "dpi-ecosystem-graph__filter-toggle")
                .OfType<Button>()
                .ToArray();
            Label installedIcon = FindByClass(view, "dpi-ecosystem-graph__filter-icon--installed")
                .OfType<Label>()
                .Single();
            Label notInstalledIcon = FindByClass(view, "dpi-ecosystem-graph__filter-icon--not-installed")
                .OfType<Label>()
                .Single();

            Assert.AreEqual(2, filterButtons.Length);
            Assert.IsFalse(filterButtons.Any(button => (button.text ?? string.Empty).Contains("[")));
            Assert.AreEqual("\u2713", installedIcon.text);
            Assert.AreEqual("\u25CB", notInstalledIcon.text);
            CollectionAssert.AreEquivalent(
                new[] { "Installed", "Not installed" },
                FindByClass(view, "dpi-ecosystem-graph__filter-label")
                    .OfType<Label>()
                    .Select(label => label.text)
                    .ToArray());
        }

        [Test]
        public void GraphView_OverviewLegendShowsOnlyStructuralItems()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Logging",
                showInstalled: true,
                showNotInstalled: true);
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState = PackageGraphSearchIndex.Create(graph, filterState);
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);

            string[] labels = FindByClass(view, "dpi-graph-legend__label")
                .OfType<Label>()
                .Select(label => label.text)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "Deucarian root", "Group", "Package", "Installed", "Not installed" },
                labels);
            CollectionAssert.DoesNotContain(labels, "Dependency flow");
            CollectionAssert.DoesNotContain(labels, "Integration connection");
        }

        [Test]
        public void GraphView_TransientLegendItemsAppearOnlyWhenTheirStatusExists()
        {
            PackageDefinition package = CreatePackage("Checking", "com.example.checking", "Core");
            PackageGraphModel checkingGraph = new PackageGraphBuilder(
                    _ => true,
                    _ => PackageChannel.Stable,
                    definition => PackageUpdateStatus.Checking(
                        definition,
                        PackageChannel.Stable,
                        definition.StableUrl))
                .Build(new[] { package });
            PackageGraphModel idleGraph = new PackageGraphBuilder(_ => false)
                .Build(new[] { package });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(checkingGraph, string.Empty, actionsEnabled: true);

            CollectionAssert.Contains(
                FindByClass(view, "dpi-graph-legend__label").OfType<Label>().Select(label => label.text).ToArray(),
                "Checking");

            view.SetGraph(
                checkingGraph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds: Array.Empty<string>(),
                searchState: PackageGraphSearchState.Empty,
                filterCounts: null,
                hiddenRelatedCount: 0);

            CollectionAssert.DoesNotContain(
                FindByClass(view, "dpi-graph-legend__label").OfType<Label>().Select(label => label.text).ToArray(),
                "Checking");

            view.SetGraph(idleGraph, string.Empty, actionsEnabled: true);

            string[] idleLabels = FindByClass(view, "dpi-graph-legend__label")
                .OfType<Label>()
                .Select(label => label.text)
                .ToArray();
            CollectionAssert.DoesNotContain(idleLabels, "Checking");
            CollectionAssert.DoesNotContain(idleLabels, "Attention");
        }

        [Test]
        public void GraphView_AttentionLegendRequiresARenderedAttentionNode()
        {
            PackageDefinition update = CreatePackage(
                "Update",
                "com.example.update",
                "Core");
            PackageGraphModel graph = new PackageGraphBuilder(
                    _ => true,
                    _ => PackageChannel.Stable,
                    package => PackageUpdateStatus.UpdateAvailable(
                        package,
                        PackageChannel.Stable,
                        package.StableUrl,
                        "1111111",
                        "2222222"))
                .Build(new[] { update });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            CollectionAssert.DoesNotContain(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Select(label => label.text)
                    .ToArray(),
                "Attention");

            view.SetGraph(graph, update.PackageId, actionsEnabled: true);

            CollectionAssert.Contains(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Select(label => label.text)
                    .ToArray(),
                "Attention");
        }

        [Test]
        public void GraphView_PackageFocusExplainsThatDirectRelationsIgnoreVisibilityFilters()
        {
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition root = CreatePackage(
                "Root",
                "com.example.root",
                "Core",
                dependencies: new[] { dependency.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(new[] { dependency, root });
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                null,
                filterState,
                null);

            view.SetGraph(
                graph,
                root.PackageId,
                root.PackageId,
                actionsEnabled: true,
                Array.Empty<string>(),
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 1);

            Label context = FindByClass(view, "dpi-ecosystem-graph__hidden-related")
                .OfType<Label>()
                .Single();
            Assert.AreEqual("Focus includes direct relations", context.text);
            StringAssert.Contains("dense extras may be summarized behind a +N overflow summary", context.tooltip);
            StringAssert.DoesNotContain("every direct relationship visible", context.tooltip);
        }

        [Test]
        public void GraphView_EscapeClearsSearchBeforeBackingOut()
        {
            int backCount = 0;
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                () => backCount++,
                filterState,
                null);

            view.HandleEscapeForTests();

            Assert.IsFalse(filterState.HasSearch);
            Assert.AreEqual(0, backCount);

            view.HandleEscapeForTests();

            Assert.AreEqual(1, backCount);
        }

        [Test]
        public void Window_RootEscapeClearsGraphSearchBeforeBackNavigation()
        {
            int backCount = 0;
            int fallbackCount = 0;
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: true,
                showNotInstalled: true);
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                () => backCount++,
                filterState,
                null);

            PackageInstallerWindow.HandleGraphEscapeForTests(view, () => fallbackCount++);

            Assert.IsFalse(filterState.HasSearch);
            Assert.AreEqual(0, backCount);
            Assert.AreEqual(0, fallbackCount);

            PackageInstallerWindow.HandleGraphEscapeForTests(view, () => fallbackCount++);

            Assert.AreEqual(1, backCount);
            Assert.AreEqual(0, fallbackCount);

            PackageInstallerWindow.HandleGraphEscapeForTests(null, () => fallbackCount++);

            Assert.AreEqual(1, fallbackCount);
        }

        [Test]
        public void PresentationPolicy_UsesHysteresisForSemanticZoomLevels()
        {
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.IconOnly,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    0.30f,
                    PackageGraphNodePresentationLevel.Micro));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.Micro,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    1.00f,
                    PackageGraphNodePresentationLevel.Micro));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.Micro,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    1.00f,
                    PackageGraphNodePresentationLevel.Full));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.Compact,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Overview,
                    1.22f,
                    PackageGraphNodePresentationLevel.Micro));
            Assert.AreEqual(
                PackageGraphNodePresentationLevel.Full,
                PackageGraphPresentationPolicy.ResolveForZoom(
                    PackageGraphLayoutMode.Focus,
                    1.00f,
                    PackageGraphNodePresentationLevel.Micro));
        }

        [Test]
        public void PresentationMetrics_UseMateriallyDifferentFootprints()
        {
            PackageGraphNodeMetrics iconOnly =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.IconOnly);
            PackageGraphNodeMetrics micro =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Micro);
            PackageGraphNodeMetrics compact =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Compact);
            PackageGraphNodeMetrics full =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);

            Assert.Less(iconOnly.Width, micro.Width);
            Assert.Less(micro.Width, compact.Width);
            Assert.Less(compact.Width, full.Width);
            Assert.Less(iconOnly.Height, micro.Height);
            Assert.Less(micro.Height, compact.Height);
            Assert.Less(compact.Height, full.Height);
        }

        [Test]
        public void PresentationMetrics_StayWithinSemanticSizeBands()
        {
            PackageGraphNodeMetrics iconOnly =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.IconOnly);
            PackageGraphNodeMetrics micro =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Micro);
            PackageGraphNodeMetrics compact =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Compact);
            PackageGraphNodeMetrics full =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);

            Assert.That(iconOnly.Width, Is.InRange(30f, 42f));
            Assert.That(iconOnly.Height, Is.InRange(30f, 42f));
            Assert.That(micro.Width, Is.InRange(92f, 124f));
            Assert.That(micro.Height, Is.InRange(34f, 48f));
            Assert.That(compact.Width, Is.InRange(145f, 170f));
            Assert.That(compact.Height, Is.InRange(64f, 82f));
            Assert.That(full.Width, Is.InRange(190f, 220f));
            Assert.That(full.Height, Is.InRange(96f, 136f));
        }

        [Test]
        public void GraphView_NodePresentationProfilesUseDedicatedRows()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });

            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            VisualElement selected = FindGraphNode(focused, "com.deucarian.session");
            VisualElement related = FindGraphNode(focused, "com.deucarian.logging");

            Assert.IsTrue(selected.ClassListContains("dpi-graph-node--presentation-full"));
            Assert.IsTrue(related.ClassListContains("dpi-graph-node--presentation-compact"));
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__header").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__package-id").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__category-path").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__badges").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__footer").Count);
            Assert.AreEqual(1, FindByClass(selected, "dpi-graph-node__action").Count);

            Assert.IsEmpty(FindByClass(related, "dpi-graph-node__package-id"));
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__category-path").Count);
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__badges").Count);
            Assert.AreEqual(1, FindByClass(related, "dpi-graph-node__footer").Count);
            Assert.IsEmpty(FindByClass(related, "dpi-graph-node__channel"));
        }

        [Test]
        public void GraphView_MicroPresentationUsesShortReadableTitleWithoutMetadataRows()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportZoom(0.5f);
            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty);

            VisualElement logging = FindGraphNode(canvas, "com.deucarian.logging");

            Assert.IsTrue(logging.ClassListContains("dpi-graph-node--presentation-micro"));
            Assert.AreEqual(
                "Logging",
                FindByClass(logging, "dpi-graph-node__title")
                    .OfType<Label>()
                    .Single()
                    .text);
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__package-id"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__category-path"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__badges"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__footer"));
        }

        [Test]
        public void GraphView_IconOnlyPresentationOmitsTitleAndMetadataRows()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            HashSet<string> visiblePackageIds = PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetViewportZoom(0.25f);
            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds,
                PackageGraphSearchState.Empty);

            VisualElement logging = FindGraphNode(canvas, "com.deucarian.logging");

            Assert.IsTrue(logging.ClassListContains("dpi-graph-node--presentation-icon-only"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__title-block"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__title"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__package-id"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__category-path"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__badges"));
            Assert.IsEmpty(FindByClass(logging, "dpi-graph-node__footer"));
            StringAssert.Contains("Deucarian Logging", logging.tooltip);
        }

        [Test]
        public void Layout_FocusSelectedPackageNeverFallsBelowCompact()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session",
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.IconOnly);
            PackageGraphNodeMetrics selectedMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Compact);
            PackageGraphNodeMetrics relatedMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.IconOnly);

            Assert.AreEqual(PackageGraphNodePresentationLevel.Compact, layout.NodePresentationLevels["com.deucarian.session"]);
            Assert.AreEqual(PackageGraphNodePresentationLevel.IconOnly, layout.NodePresentationLevels["com.deucarian.logging"]);
            Assert.That(layout.NodeRects["com.deucarian.session"].width, Is.EqualTo(selectedMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.session"].height, Is.EqualTo(selectedMetrics.Height).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].width, Is.EqualTo(relatedMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].height, Is.EqualTo(relatedMetrics.Height).Within(0.1f));
        }

        [Test]
        public void PresentationPolicy_ShortGraphTitleOnlyRemovesRedundantDeucarianPrefix()
        {
            Assert.AreEqual(
                "Logging",
                PackageGraphPresentationPolicy.GetGraphTitle(
                    "Deucarian Logging",
                    PackageGraphNodePresentationLevel.Micro));
            Assert.AreEqual(
                "Deucarian Logging",
                PackageGraphPresentationPolicy.GetGraphTitle(
                    "Deucarian Logging",
                    PackageGraphNodePresentationLevel.Full));
            Assert.AreEqual(
                "Other Logging",
                PackageGraphPresentationPolicy.GetGraphTitle(
                    "Other Logging",
                    PackageGraphNodePresentationLevel.Micro));
        }

        [Test]
        public void GraphView_GlassDecorationsDoNotChangeGraphGeometry()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            PackageGraphCanvas canvas = GetCanvas(view);
            VisualElement session = FindGraphNode(view, "com.deucarian.session");
            VisualElement runtimeGroup = FindGraphGroup(view, "runtime-services");

            Assert.AreEqual(1, FindByClass(session, "dpi-graph-node__glass-highlight").Count);
            Assert.AreEqual(1, FindByClass(session, "deucarian-glass-sheen").Count);
            Assert.AreEqual(1, FindByClass(runtimeGroup, "dpi-graph-group__glass-highlight").Count);
            Assert.AreEqual(1, FindByClass(runtimeGroup, "deucarian-glass-sheen").Count);
            AssertRectsEqual(
                canvas.NodeRectsForTests["com.deucarian.session"],
                canvas.NodeVisualStatesForTests["com.deucarian.session"].Rect,
                0.001f);
        }

        [Test]
        public void GraphViewport_SemanticZoomClassesTrackZoomThresholds()
        {
            PackageGraphViewport viewport = new PackageGraphViewport(null);
            VisualElement contentRoot = viewport.Q<VisualElement>("ecosystem-graph-content");
            System.Reflection.FieldInfo zoomField = typeof(PackageGraphViewport).GetField(
                "_zoom",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo applyTransform = typeof(PackageGraphViewport).GetMethod(
                "ApplyTransform",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            zoomField.SetValue(viewport, 0.50f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--low-zoom"));

            zoomField.SetValue(viewport, 0.80f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--medium-zoom"));

            zoomField.SetValue(viewport, 1.20f);
            applyTransform.Invoke(viewport, null);

            Assert.IsTrue(contentRoot.ClassListContains("dpi-ecosystem-graph__content--high-zoom"));
        }

        [Test]
        public void GraphViewport_SpotlightLayerStaysBehindPanZoomContent()
        {
            PackageGraphViewport viewport = new PackageGraphViewport(null);
            PackageGraphSpotlightLayer spotlight = viewport.SpotlightLayerForTests;
            VisualElement contentRoot = viewport.ContentRootForTests;

            Assert.NotNull(spotlight);
            Assert.NotNull(contentRoot);
            Assert.AreSame(viewport, spotlight.parent);
            Assert.AreSame(viewport, contentRoot.parent);
            Assert.Less(
                viewport.Children().ToList().IndexOf(spotlight),
                viewport.Children().ToList().IndexOf(contentRoot));
            Assert.AreEqual(PickingMode.Ignore, spotlight.pickingMode);
            Assert.AreEqual(Position.Absolute, spotlight.style.position.value);

            viewport.SetSpotlightWorldCenter(new Vector2(42f, 80f), PackageGraphSpotlightKind.Category);

            Assert.AreSame(spotlight, viewport.SpotlightLayerForTests);
            Assert.AreSame(contentRoot, viewport.ContentRootForTests);
        }

        [Test]
        public void GraphViewport_LeftPanPolicyAllowsBackgroundAndRootOnly()
        {
            VisualElement packageNode = new VisualElement();
            packageNode.AddToClassList("dpi-graph-node");
            VisualElement groupNode = new VisualElement();
            groupNode.AddToClassList("dpi-graph-group");
            VisualElement hub = new VisualElement();
            hub.AddToClassList("dpi-graph-hub");
            VisualElement hubChild = new VisualElement();
            hub.Add(hubChild);
            VisualElement canvas = new VisualElement();
            canvas.AddToClassList("dpi-ecosystem-graph__canvas");

            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(packageNode));
            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(groupNode));
            Assert.IsTrue(PackageGraphViewport.IsLeftPanTargetForTests(hubChild));
            Assert.IsTrue(PackageGraphViewport.IsLeftPanTargetForTests(canvas));
        }

        [Test]
        public void GraphViewport_EffectiveMinimumZoomAllowsFitBelowNormalClamp()
        {
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.18f),
                Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.04f),
                Is.EqualTo(0.10f).Within(0.001f));
            Assert.That(
                PackageGraphViewport.CalculateEffectiveMinZoomForTests(0.35f, 0.90f),
                Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void GraphTransition_AnchoredCameraUsesExactEndpointsAndContinuousPath()
        {
            PackageGraphCameraState source = new PackageGraphCameraState(new Vector2(40f, 24f), 0.72f);
            PackageGraphCameraState target = new PackageGraphCameraState(new Vector2(-180f, 90f), 1.18f);
            Vector2 sourceAnchorWorld = new Vector2(120f, 80f);
            Vector2 targetAnchorWorld = new Vector2(360f, 220f);
            Vector2 sourceAnchorScreen = source.WorldToViewport(sourceAnchorWorld);
            Vector2 targetAnchorScreen = target.WorldToViewport(targetAnchorWorld);

            PackageGraphCameraState frame0 = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0f);
            PackageGraphCameraState middle = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0.5f);
            PackageGraphCameraState frame1 = PackageGraphTransition.EvaluateAnchoredCamera(
                source,
                target,
                sourceAnchorWorld,
                targetAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                1f);

            AssertCameraClose(source, frame0);
            AssertCameraClose(target, frame1);
            Assert.That(middle.Zoom, Is.InRange(source.Zoom, target.Zoom));

            Vector2 expectedMiddleAnchorScreen = Vector2.Lerp(
                sourceAnchorScreen,
                targetAnchorScreen,
                PackageGraphTransition.SmoothStep(0.5f));
            Vector2 middleAnchorWorld = Vector2.Lerp(
                sourceAnchorWorld,
                targetAnchorWorld,
                PackageGraphTransition.SmoothStep(0.5f));
            AssertVectorClose(expectedMiddleAnchorScreen, middle.WorldToViewport(middleAnchorWorld), 0.001f);
        }

        [Test]
        public void GraphTransition_AnimatedAnchorCameraUsesRenderedAnchorWorld()
        {
            PackageGraphCameraState source = new PackageGraphCameraState(new Vector2(40f, 24f), 0.72f);
            PackageGraphCameraState target = new PackageGraphCameraState(new Vector2(-180f, 90f), 1.18f);
            Vector2 sourceAnchorScreen = new Vector2(126.4f, 81.6f);
            Vector2 targetAnchorScreen = new Vector2(244.8f, 349.6f);
            Vector2 animatedAnchorWorld = new Vector2(264f, 148f);
            float eased = PackageGraphTransition.SmoothStep(0.5f);

            PackageGraphCameraState middle = PackageGraphTransition.EvaluateAnchoredCameraFromAnimatedAnchor(
                source,
                target,
                animatedAnchorWorld,
                sourceAnchorScreen,
                targetAnchorScreen,
                0.5f);

            Vector2 expectedAnchorScreen = Vector2.Lerp(sourceAnchorScreen, targetAnchorScreen, eased);
            Assert.That(middle.Zoom, Is.EqualTo(Mathf.Lerp(source.Zoom, target.Zoom, eased)).Within(0.001f));
            AssertVectorClose(expectedAnchorScreen, middle.WorldToViewport(animatedAnchorWorld), 0.001f);
        }

        [Test]
        public void GraphProjection_KeepsPreviewChildrenOnAnimatedGroupOrbit()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();
            PackageGraphLayoutResult source = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.Compact);
            PackageGraphLayoutResult target = layout.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "integrations",
                Vector2.zero,
                PackageGraphNodePresentationLevel.Compact);
            float frameProgress = 0.5f;
            Dictionary<string, Rect> nodeRects = CreateInterpolatedNodeRects(source, target, frameProgress);
            Dictionary<string, Rect> groupRects = CreateInterpolatedGroupRects(source, target, frameProgress);
            Dictionary<string, Vector2> groupCenters = CreateInterpolatedGroupCenters(source, target, frameProgress);
            Dictionary<string, float> groupRadii = CreateInterpolatedGroupRadii(source, target, frameProgress);

            PackageGraphCanvas.ProjectOrbitalChildrenForTests(
                graph,
                target,
                nodeRects,
                groupRects,
                groupCenters,
                groupRadii);

            Vector2 integrationCenter = groupCenters["integrations"];
            float integrationRadius = groupRadii["integrations"];
            Assert.That(integrationRadius, Is.GreaterThan(0f));

            foreach (PackageGraphNode node in graph.Nodes.Where(node =>
                         node != null &&
                         string.Equals(node.GroupId, "integrations", StringComparison.OrdinalIgnoreCase) &&
                         target.NodeRects.ContainsKey(node.PackageId)))
            {
                Assert.That(
                    Vector2.Distance(nodeRects[node.PackageId].center, integrationCenter),
                    Is.EqualTo(integrationRadius).Within(0.5f),
                    node.DisplayName);
            }
        }

        [Test]
        public void GraphCanvas_UsesPainterOrbitStatesWithoutCssRingGuideElements()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, string.Empty, string.Empty, actionsEnabled: true);

            Assert.IsEmpty(FindByClass(canvas, "dpi-graph-ring-guide"));
            IReadOnlyList<PackageGraphOrbitVisualState> orbitStates = canvas.OrbitVisualStatesForTests;
            Assert.That(orbitStates.Count, Is.GreaterThan(0));
            Assert.AreEqual(
                orbitStates.Count,
                orbitStates.Select(orbit => orbit.OrbitId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.AreEqual(1, orbitStates.Count(orbit => orbit.OrbitId.StartsWith("root:", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(orbitStates.Any(orbit => orbit.OrbitId.StartsWith("group:", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void GraphTransition_TargetOnlyCategoryChildrenStartSmallTransparentAndSeparated()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                string.Empty,
                actionsEnabled: true,
                visiblePackageIds: null);
            canvas.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "ui-presentation",
                actionsEnabled: true,
                visiblePackageIds: null);

            PackageGraphNodeVisualState uiBinding = canvas.NodeVisualStatesForTests["com.deucarian.ui-binding"];
            PackageGraphNodeVisualState theming = canvas.NodeVisualStatesForTests["com.deucarian.theming"];

            Assert.AreEqual(0f, uiBinding.Opacity);
            Assert.AreEqual(0f, theming.Opacity);
            Assert.That(uiBinding.Scale, Is.LessThan(0.30f));
            Assert.That(theming.Scale, Is.LessThan(0.30f));
            Assert.That(Vector2.Distance(uiBinding.Rect.center, theming.Rect.center), Is.GreaterThan(1f));
            Assert.AreEqual(1, canvas.CountNodeElementsForTests("com.deucarian.ui-binding"));
            Assert.AreEqual(1, canvas.CountNodeElementsForTests("com.deucarian.theming"));
        }

        [Test]
        public void GraphViewport_RemovesWheelDrivenHierarchyNavigationArchitecture()
        {
            System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static;
            System.Reflection.Assembly graphAssembly = typeof(PackageGraphViewport).Assembly;

            Assert.IsNull(typeof(PackageGraphViewport).GetEvent("HierarchyExitWheel", flags));
            Assert.IsNull(typeof(PackageGraphViewport).GetEvent("HierarchyEnterWheel", flags));
            Assert.IsNull(typeof(PackageGraphViewport).GetMethod("GetHierarchyExitIntentZoomForTests", flags));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyExitController"));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyExitWheelEvent"));
            Assert.IsNull(graphAssembly.GetType("Deucarian.PackageInstaller.Editor.PackageGraphHierarchyEnterWheelEvent"));
        }

        [Test]
        public void GraphCategoryHover_UsesDirectGroupHoverNotPackageParent()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, null);

            canvas.SetGraph(graph, string.Empty, string.Empty, true);
            canvas.SetPreviewPackageForTests("com.deucarian.logging");

            Assert.AreEqual("infrastructure", canvas.ActiveHoverGroupId);
            Assert.AreEqual(string.Empty, canvas.DirectHoverGroupId);

            canvas.SetExternalHoverGroup("infrastructure", respectInteractionLock: false);

            Assert.AreEqual("infrastructure", canvas.ActiveHoverGroupId);
            Assert.AreEqual("infrastructure", canvas.DirectHoverGroupId);
        }

        [Test]
        public void GraphView_DisablesSelectedNodeActionDuringLayoutTransition()
        {
            PackageDefinition package = CreatePackage("Installable", "com.example.installable", "Core");
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(new[] { package });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);
            view.SetGraph(graph, package.PackageId, actionsEnabled: true);

            Button selectedAction = FindByClass(view, "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
            Assert.AreEqual("Install", selectedAction.text);
            Assert.IsFalse(selectedAction.enabledSelf);
        }

        [Test]
        public void GraphView_ShowsActionsOnlyForFocusedRelationshipContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });

            overview.SetGraph(graph, string.Empty, actionsEnabled: true);
            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.IsEmpty(FindByClass(overview, "dpi-graph-node__action"));
            Assert.AreEqual(
                "Install",
                FindGraphNodeAction(focused, "com.deucarian.session").text);
            Assert.AreEqual(
                "Install",
                FindGraphNodeAction(focused, "com.deucarian.logging").text);
            Assert.AreEqual(
                "Install Integration",
                FindGraphNodeAction(focused, "com.deucarian.session.api-integration").text);
            Assert.IsEmpty(FindByClass(overview, "dpi-graph-node--overview"));
            Assert.AreEqual(7, FindByClass(overview, "dpi-graph-group--overview").Count);
            Assert.Less(FindByClass(focused, "dpi-graph-node").Count, graph.Nodes.Count);
            Assert.IsEmpty(FindByClass(overview, "dpi-graph-node__package-id"));
            Assert.IsEmpty(FindByClass(overview, "dpi-graph-unrelated-summary"));
            Assert.IsFalse(HasGraphNode(focused, "com.deucarian.theming"));
            Assert.IsTrue(FindByClass(focused, "dpi-graph-group--collapsed").Count > 0);
            Assert.IsTrue(
                FindByClass(focused, "dpi-graph-group__subtitle")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("related package")));
            Assert.IsTrue(FindGraphNode(focused, "com.deucarian.logging").ClassListContains("dpi-graph-node--focus"));
        }

        [Test]
        public void GraphView_GroupFocusShowsGroupCardsWithoutPackageActions()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });

            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);

            Assert.AreEqual(1, FindByClass(groupFocused, "dpi-graph-group--focused").Count);
            Assert.IsEmpty(FindByClass(groupFocused, "dpi-graph-node__action"));
            Assert.IsTrue(FindGraphNode(groupFocused, "com.deucarian.logging").ClassListContains("dpi-graph-node--overview"));
            Assert.IsTrue(
                FindByClass(groupFocused, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Structural membership"));
            Assert.IsFalse(
                FindByClass(groupFocused, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Dependency flow"));
        }

        [Test]
        public void GraphView_GroupHubsUseCircularSymbolsWithExternalLabels()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            VisualElement runtimeGroup = FindByClass(view, "dpi-graph-group")
                .Single(element => element.name == "group-runtime-services");
            VisualElement symbol = FindByClass(runtimeGroup, "dpi-graph-group__symbol").Single();

            Assert.That(
                symbol.style.width.value.value,
                Is.EqualTo(symbol.style.height.value.value).Within(0.1f));
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__title"));
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__count"));
            Assert.IsEmpty(FindByClass(symbol, "dpi-graph-group__attention"));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__title")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__subtitle")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("package")));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__stat--installed")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("installed")));
            Assert.IsTrue(
                FindByClass(runtimeGroup, "dpi-graph-group__stat--available")
                    .OfType<Label>()
                    .Any(label => label.text.Contains("not installed")));
            Assert.IsEmpty(FindByClass(runtimeGroup, "dpi-graph-group__stat--attention"));
            Assert.IsEmpty(FindByClass(runtimeGroup, "dpi-graph-node__action"));
        }

        [Test]
        public void GraphView_ActiveCategoryAndPackageExposeBackAffordance()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView nestedGroupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView packageFocused = new PackageGraphView(_ => { }, (_, __) => { });

            overview.SetGraph(graph, string.Empty, actionsEnabled: true);
            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "infrastructure",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            nestedGroupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "ui-presentation",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            packageFocused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            VisualElement inactiveOverviewGroup = FindGraphGroup(overview, "infrastructure");
            VisualElement activeTopLevelGroup = FindGraphGroup(groupFocused, "infrastructure");
            VisualElement activeNestedGroup = FindGraphGroup(nestedGroupFocused, "ui-presentation");
            VisualElement selectedPackage = FindGraphNode(packageFocused, "com.deucarian.session");
            VisualElement relatedPackage = FindGraphNode(packageFocused, "com.deucarian.logging");
            Label packageBack = FindByClass(selectedPackage, "dpi-graph-node__back-hint")
                .OfType<Label>()
                .Single();

            Assert.IsFalse(inactiveOverviewGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.IsEmpty(FindByClass(inactiveOverviewGroup, "dpi-graph-group__back-hint"));
            Assert.IsTrue(activeTopLevelGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-group__back-hint").Count);
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-category-caption-row").Count);
            Assert.AreEqual(1, FindByClass(activeTopLevelGroup, "dpi-graph-back-hint--category").Count);
            Assert.AreEqual("Back to Ecosystem Overview", activeTopLevelGroup.tooltip);
            Assert.IsTrue(activeNestedGroup.ClassListContains("dpi-graph-group--has-back"));
            Assert.AreEqual("Back to Experience & Interaction", activeNestedGroup.tooltip);
            Assert.IsTrue(selectedPackage.ClassListContains("dpi-graph-node--has-back"));
            Assert.AreEqual(PickingMode.Ignore, packageBack.pickingMode);
            Assert.AreEqual("Back to Runtime Services", packageBack.tooltip);
            Assert.AreEqual(1, FindByClass(selectedPackage, "dpi-graph-back-hint--package").Count);
            Assert.IsFalse((selectedPackage.tooltip ?? string.Empty).Contains("package."));
            Assert.AreEqual("Infrastructure", inactiveOverviewGroup.tooltip);
            Assert.IsEmpty(FindByClass(relatedPackage, "dpi-graph-node__back-hint"));
            Assert.IsEmpty(FindByClass(selectedPackage, "dpi-graph-node__back"));
            Assert.IsEmpty(FindByClass(activeTopLevelGroup, "dpi-graph-group__back"));
        }

        [Test]
        public void Window_GroupNavigationRowsIncludeOverviewAndUseSharedSelectionState()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            IReadOnlyList<PackageGraphGroupNavigationRow> overviewRows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Overview());

            Assert.AreEqual("overview", overviewRows[0].Id);
            Assert.AreEqual("Deucarian Overview", overviewRows[0].DisplayName);
            Assert.AreEqual("package-installer", overviewRows[0].IconKey);
            Assert.AreEqual(
                "\u25CB " + graph.Nodes.Count + " not installed",
                overviewRows[0].Summary);
            Assert.AreEqual(graph.Nodes.Count, overviewRows[0].StatusSummary.NotInstalledCount);
            Assert.IsTrue(overviewRows[0].IsOverview);
            Assert.IsTrue(overviewRows[0].IsSelected);
            Assert.IsFalse(overviewRows[0].Summary.Contains("packages"));

            IReadOnlyList<PackageGraphGroupNavigationRow> groupRows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Group("infrastructure"));
            Assert.IsFalse(groupRows.Single(row => row.Id == "overview").IsSelected);
            Assert.IsTrue(groupRows.Single(row => row.Id == "infrastructure").IsSelected);

            IReadOnlyList<PackageGraphGroupNavigationRow> nestedRows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Group("ui-presentation"));
            Assert.IsTrue(nestedRows.Single(row => row.Id == "experience-interaction").IsSelected);

            IReadOnlyList<PackageGraphGroupNavigationRow> packageRows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageInstallerWindow.CreatePackageNavigationStateForTests(graph, "com.deucarian.session"));
            Assert.IsTrue(packageRows.Single(row => row.Id == "runtime-services").IsSelected);
        }

        [Test]
        public void GraphView_DoesNotRenderDuplicateCategoryRailNavigation()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsEmpty(FindByClass(view, "dpi-category-rail"));
            Assert.IsEmpty(FindByClass(view, "dpi-category-rail__item"));
        }

        [Test]
        public void GraphView_GroupHoverPreviewHighlightsGraphStructuralContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);
            view.PreviewCategoryHoverForTests("infrastructure");

            Assert.AreEqual("infrastructure", view.ActiveHoverGroupId);
            Assert.AreEqual("infrastructure", view.ActiveTopLevelHoverGroupId);
            Assert.IsTrue(FindGraphGroup(view, "infrastructure").ClassListContains("dpi-graph-group--hover-context"));
            Assert.IsTrue(FindGraphGroup(view, "runtime-services").ClassListContains("dpi-graph-group--hover-dimmed"));
            Assert.IsEmpty(FindByClass(view, "dpi-graph-node--hover-context"));
        }

        [Test]
        public void GraphView_GraphPackageHoverHighlightsFocusedGraphNode()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView nestedPackageFocused = new PackageGraphView(_ => { }, (_, __) => { });

            nestedPackageFocused.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);
            nestedPackageFocused.PreviewPackageHoverForTests("com.deucarian.ui-binding");

            Assert.AreEqual("ui-presentation", nestedPackageFocused.ActiveHoverGroupId);
            Assert.AreEqual("experience-interaction", nestedPackageFocused.ActiveTopLevelHoverGroupId);
            Assert.IsTrue(
                FindGraphNode(nestedPackageFocused, "com.deucarian.ui-binding")
                    .ClassListContains("dpi-graph-node--hover-context"));
        }

        [Test]
        public void GraphView_PreviewUpdatesInPlaceWithoutReplacingKeyboardTargets()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView focused = new PackageGraphView(_ => { }, (_, __) => { });
            focused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);
            VisualElement nodeBeforePreview = FindGraphNode(focused, "com.deucarian.logging");

            focused.PreviewPackageHoverForTests("com.deucarian.logging");

            VisualElement nodeAfterPreview = FindGraphNode(focused, "com.deucarian.logging");
            Assert.AreSame(nodeBeforePreview, nodeAfterPreview);
            Assert.IsTrue(nodeAfterPreview.ClassListContains("dpi-graph-node--previewed"));

            focused.ClearPackageHoverForTests("com.deucarian.logging");

            Assert.AreSame(nodeBeforePreview, FindGraphNode(focused, "com.deucarian.logging"));
            Assert.IsFalse(nodeBeforePreview.ClassListContains("dpi-graph-node--previewed"));

            PackageGraphView overview = new PackageGraphView(_ => { }, (_, __) => { });
            overview.SetGraph(graph, string.Empty, actionsEnabled: true);
            VisualElement groupBeforePreview = FindGraphGroup(overview, "infrastructure");

            overview.PreviewCategoryHoverForTests("infrastructure");

            Assert.AreSame(groupBeforePreview, FindGraphGroup(overview, "infrastructure"));
            Assert.IsTrue(groupBeforePreview.ClassListContains("dpi-graph-group--hover-context"));
        }

        [Test]
        public void GraphView_ClearHoverStateClearsSharedPackageAndCategoryHover()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);
            view.PreviewPackageHoverForTests("com.deucarian.logging");
            view.ClearHoverState();

            Assert.IsTrue(string.IsNullOrWhiteSpace(view.ActiveHoverGroupId));
            Assert.IsTrue(string.IsNullOrWhiteSpace(view.ActiveTopLevelHoverGroupId));
            Assert.IsEmpty(FindByClass(view, "dpi-graph-node--hover-context"));

            view.PreviewCategoryHoverForTests("infrastructure");
            view.ClearHoverState();

            Assert.IsTrue(string.IsNullOrWhiteSpace(view.ActiveHoverGroupId));
            Assert.IsFalse(FindGraphGroup(view, "infrastructure").ClassListContains("dpi-graph-group--hover-context"));
        }

        [Test]
        public void GraphView_ResponsiveModeClassesAreStable()
        {
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });
            VisualElement filterRow = FindByClass(view, "dpi-ecosystem-graph__filter-row").Single();
            VisualElement toolbar = FindByClass(view, "dpi-ecosystem-graph__toolbar").Single();

            Assert.AreEqual(Wrap.Wrap, filterRow.style.flexWrap.value);
            Assert.AreEqual(Align.FlexStart, filterRow.style.alignContent.value);
            Assert.AreEqual(1f, toolbar.style.flexGrow.value);

            view.SetResponsiveMode(PackageInstallerResponsiveMode.Compact);

            Assert.IsTrue(view.ClassListContains("dpi-ecosystem-graph--compact"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--wide"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--narrow"));

            view.SetResponsiveMode(PackageInstallerResponsiveMode.Narrow);

            Assert.IsTrue(view.ClassListContains("dpi-ecosystem-graph--narrow"));
            Assert.IsFalse(view.ClassListContains("dpi-ecosystem-graph--compact"));
        }

        [Test]
        public void GraphView_ExistingGraphTargetsAreKeyboardFocusable()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView focusedView = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsTrue(FindByClass(view, "dpi-graph-hub").Single().focusable);
            Assert.IsTrue(FindByClass(view, "dpi-graph-group").All(group => group.focusable));

            focusedView.SetGraph(graph, "com.deucarian.logging", actionsEnabled: true);

            Assert.IsTrue(FindByClass(focusedView, "dpi-graph-node").All(node => node.focusable));
            Assert.IsTrue(PackageGraphKeyboard.IsActivationKey(KeyCode.Return));
            Assert.IsTrue(PackageGraphKeyboard.IsActivationKey(KeyCode.KeypadEnter));
            Assert.IsTrue(PackageGraphKeyboard.IsActivationKey(KeyCode.Space));
            Assert.IsFalse(PackageGraphKeyboard.IsActivationKey(KeyCode.Escape));
        }

        [Test]
        public void GraphView_BreadcrumbShowsCurrentSegmentWithoutClickablePill()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView groupFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView packageFocused = new PackageGraphView(_ => { }, (_, __) => { });
            PackageGraphView nestedPackageFocused = new PackageGraphView(_ => { }, (_, __) => { });

            groupFocused.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "runtime-services",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            packageFocused.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);
            nestedPackageFocused.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(groupFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));
            Assert.IsFalse(
                FindByClass(groupFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Deucarian Session"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Runtime Services"));
            Assert.IsTrue(
                FindByClass(packageFocused, "dpi-ecosystem-graph__breadcrumb-separator")
                    .OfType<Label>()
                    .All(label => label.text == ">"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "Experience & Interaction"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb")
                    .OfType<Button>()
                    .Any(button => button.text == "UI & Presentation"));
            Assert.IsTrue(
                FindByClass(nestedPackageFocused, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Any(label => label.text == "Deucarian UI Binding"));
        }

        [Test]
        public void GraphView_ReturningToRootClearsStaleCategoryState()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                "experience-interaction",
                actionsEnabled: true,
                visiblePackageIds: null,
                filterCounts: null,
                hiddenRelatedCount: 0);
            view.SetGraph(graph, string.Empty, actionsEnabled: true);

            Assert.IsEmpty(FindByClass(view, "dpi-category-rail__item"));
            Assert.AreEqual(
                "Deucarian",
                FindByClass(view, "dpi-ecosystem-graph__breadcrumb-current")
                    .OfType<Label>()
                    .Single()
                    .text);
        }

        [Test]
        public void GraphView_PackageFocusShowsPlainCategoryPath()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, "com.deucarian.session", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(FindGraphNode(view, "com.deucarian.session"), "dpi-graph-node__category-path")
                    .OfType<Label>()
                    .Any(label => label.text == "Runtime Services"));

            PackageGraphView nestedView = new PackageGraphView(_ => { }, (_, __) => { });
            nestedView.SetGraph(graph, "com.deucarian.ui-binding", actionsEnabled: true);

            Assert.IsTrue(
                FindByClass(FindGraphNode(nestedView, "com.deucarian.ui-binding"), "dpi-graph-node__category-path")
                    .OfType<Label>()
                    .Any(label => label.text == "Experience & Interaction / UI & Presentation"));
        }

        [Test]
        public void HierarchyDisplay_SeparatesStructuralCategoryFromPackageKind()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            AssertCategoryAndKind(graph, "com.deucarian.api", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.session", "Runtime Services", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.core-state", "State & Data", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.ui-binding", "Experience & Interaction / UI & Presentation", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.object-selection", "Experience & Interaction / World Interaction", "Library");
            AssertCategoryAndKind(graph, "com.deucarian.package-installer", "Tools & Quality", "Tool");
            AssertCategoryAndKind(graph, "com.deucarian.session.api-integration", "Integrations", "Integration");
            AssertCategoryAndKind(graph, "com.deucarian.selection-suite", "Suites", "Suite");
        }

        [Test]
        public void Build_MarksMissingInstalledDependencyRequirementAsWarningNode()
        {
            PackageDefinition dependency = CreatePackage("Dependency", "com.example.dependency", "Core");
            PackageDefinition installed = CreatePackage(
                "Installed",
                "com.example.installed",
                "Core",
                dependencies: new[] { dependency.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == installed.PackageId)
                .Build(new[] { dependency, installed });
            PackageGraphNode dependencyNode = graph.Nodes.Single(node => node.PackageId == dependency.PackageId);

            Assert.AreEqual(PackageGraphNodeStatus.Warning, dependencyNode.Status);
            Assert.AreEqual("Required by installed package", dependencyNode.UpdateStatusLabel);
            Assert.AreEqual(PackageGraphNodeAction.Install, dependencyNode.PrimaryAction);

            IReadOnlyList<PackageGraphGroupNavigationRow> rows =
                PackageInstallerWindow.CreateEcosystemOverviewGroupNavigationRowsForTests(
                    graph,
                    PackageGraphNavigationState.Overview());
            PackageGraphGroupNavigationRow infrastructureRow =
                rows.Single(row => row.Id == "infrastructure");
            Assert.IsTrue(infrastructureRow.HasAttention);
            Assert.AreEqual(1, infrastructureRow.StatusSummary.AttentionCount);
            StringAssert.Contains("! 1 attention", infrastructureRow.Summary);
        }

        [Test]
        public void MissingRelationshipTarget_ProvidesCopyableDiagnosticWithoutInstallAction()
        {
            PackageDefinition root = CreatePackage(
                "Root",
                "com.example.root",
                "Core",
                dependencies: new[] { "com.example.unregistered" });
            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(new[] { root });
            PackageGraphNode missing = graph.Nodes.Single(node => node.PackageId == "com.example.unregistered");

            string diagnostic = PackageGraphView.GetMissingPackageDiagnostic(missing);

            Assert.IsFalse(missing.IsRegistered);
            Assert.AreEqual(PackageGraphNodeAction.None, missing.PrimaryAction);
            StringAssert.Contains("Package ID: com.example.unregistered", diagnostic);
            StringAssert.Contains("Registry relationship target is not registered", diagnostic);

            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });
            view.SetGraph(graph, root.PackageId, actionsEnabled: true);
            PackageGraphNodeElement missingElement =
                (PackageGraphNodeElement)FindGraphNode(view, missing.PackageId);
            string previousClipboard = EditorGUIUtility.systemCopyBuffer;

            try
            {
                EditorGUIUtility.systemCopyBuffer = string.Empty;
                Assert.IsTrue(missingElement.focusable);
                Assert.IsTrue(missingElement.HasKeyboardActivationForTests);

                missingElement.ActivateForTests();

                Assert.AreEqual(diagnostic, EditorGUIUtility.systemCopyBuffer);
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = previousClipboard;
            }
        }

        [Test]
        public void GraphView_CreatesNodeElementsAndPainterEdgeLayer()
        {
            PackageDefinition core = CreatePackage("Core", "com.example.core", "Core");
            PackageDefinition companion = CreatePackage("Companion", "com.example.companion", "UI", "OptionalIntegration");
            PackageDefinition integration = CreatePackage(
                "Integration",
                "com.example.integration",
                "Integration",
                "Integration",
                integrationTargets: new[] { core.PackageId, companion.PackageId });

            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == core.PackageId)
                .Build(new[] { core, companion, integration });
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, core.PackageId, actionsEnabled: true);

            Assert.IsNull(view.Q<ScrollView>());
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-viewport"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-content"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-membership-layer"));
            Assert.IsNotNull(view.Q<VisualElement>("ecosystem-graph-edge-layer"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Dependency flow"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__item")
                    .Any(item => item.tooltip.Contains("Animated flow markers")));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Integration connection"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Optional companion"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__item")
                    .Any(item => item.tooltip == "Recommended alongside, not required"));
            Assert.IsTrue(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Any(label => label.text == "Suite membership"));
            Assert.AreEqual(2, FindByClass(view, "dpi-graph-node").Count);
            Assert.AreEqual(2, FindByClass(view, "dpi-graph-group--collapsed").Count);
            Assert.AreEqual(1, FindByClass(view, "dpi-graph-node--integration").Count);
            Assert.IsTrue(FindByClass(view, "dpi-graph-node__action").Count >= 1);
        }

        [Test]
        public void GraphEdges_TreatOptionalCompanionsAsNonDirectional()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphEdge optionalEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.OptionalCompanion &&
                edge.FromPackageId == "com.deucarian.object-loading" &&
                edge.ToPackageId == "com.deucarian.diagnostics");

            Assert.AreEqual("Optional companion", optionalEdge.Label);
            Assert.IsFalse(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.OptionalCompanion));
            Assert.IsFalse(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.OptionalCompanion));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesTwoPassStrokeForTests(PackageGraphEdgeKind.OptionalCompanion));
            Assert.IsTrue(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesTwoPassStrokeForTests(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.IntegrationConnection));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesDirectionalFlowMarkersForTests(PackageGraphEdgeKind.IntegrationConnection));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesTwoPassStrokeForTests(PackageGraphEdgeKind.IntegrationConnection));
            Assert.IsTrue(PackageGraphEdgeLayer.UsesTwoPassStrokeForTests(PackageGraphEdgeKind.SuiteMembership));
        }

        [Test]
        public void GraphEdgeRoutes_FanOutMultipleIntegrationTargetsThroughSharedTrunk()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.api");

            PackageGraphEdgeRoute[] integrationRoutes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Where(route => route.HasKind(PackageGraphEdgeKind.IntegrationConnection) &&
                                    route.Bundle.ConnectsPackage("com.deucarian.api"))
                    .OrderBy(route => route.BranchIndex)
                    .ToArray();

            Assert.AreEqual(2, integrationRoutes.Length);
            Assert.IsTrue(integrationRoutes.All(route => route.UsesSharedTrunk));
            Assert.AreEqual(1, integrationRoutes.Select(route => route.SharedTrunkId).Distinct().Count());
            Assert.IsTrue(integrationRoutes.All(route => route.Zone == PackageGraphEdgeRouteZone.Integrations));
            Assert.IsTrue(integrationRoutes.All(route => route.BranchCount == 2));
            Assert.IsTrue(integrationRoutes.All(route => route.Points.Count == 4));
            Assert.IsTrue(integrationRoutes.All(route => route.Bundle.IsCompositeDependencyIntegration));
            Assert.IsTrue(integrationRoutes.All(route => route.HasKind(PackageGraphEdgeKind.HardDependency)));
            Assert.That(
                Vector2.Distance(
                    integrationRoutes[0].Points[1],
                    integrationRoutes[1].Points[1]),
                Is.LessThan(0.1f));
            Assert.IsTrue(integrationRoutes.All(route =>
                !PackageGraphEdgeLayer.RouteCrossesNodeInteriorForTests(route, layout.NodeRects)));
            Assert.IsTrue(integrationRoutes.All(route =>
                PackageGraphEdgeLayer.RouteLengthForTests(route) <=
                PackageGraphEdgeLayer.DirectRouteDistanceForTests(route) * 1.8f));
        }

        [Test]
        public void GraphEdgeRoutes_BundleDependencyAndIntegrationByEndpointPair()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.api");

            PackageGraphEdgeRoute route =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Single(candidate =>
                        candidate.Bundle.SourcePackageId == "com.deucarian.api" &&
                        candidate.Bundle.TargetPackageId == "com.deucarian.session.api-integration");

            Assert.IsTrue(route.Bundle.IsCompositeDependencyIntegration);
            Assert.AreEqual(2, route.Bundle.Edges.Count);
            Assert.IsTrue(route.HasKind(PackageGraphEdgeKind.HardDependency));
            Assert.IsTrue(route.HasKind(PackageGraphEdgeKind.IntegrationConnection));
            Assert.AreEqual(PackageGraphEdgeKind.HardDependency, route.Edge.Kind);
            Assert.AreEqual("com.deucarian.api", route.Edge.FromPackageId);
            Assert.AreEqual("com.deucarian.session.api-integration", route.Edge.ToPackageId);
        }

        [Test]
        public void GraphEdgeRoutes_UseDirectBorderRouteForSingleTarget()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.session");

            PackageGraphEdgeRoute dependencyRoute =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .Single(route => route.Edge.Kind == PackageGraphEdgeKind.HardDependency &&
                                     route.Edge.FromPackageId == "com.deucarian.logging" &&
                                     route.Edge.ToPackageId == "com.deucarian.session");

            Assert.IsFalse(dependencyRoute.UsesSharedTrunk);
            Assert.AreEqual(2, dependencyRoute.Points.Count);
            Assert.AreEqual(PackageGraphEdgeRouteZone.Providers, dependencyRoute.Zone);
            Assert.AreEqual(PackageGraphEdgeRoutePort.Right, dependencyRoute.SourcePort);
            Assert.AreEqual(PackageGraphEdgeRoutePort.Left, dependencyRoute.TargetPort);
            Assert.IsFalse(PackageGraphEdgeLayer.RouteCrossesNodeInteriorForTests(dependencyRoute, layout.NodeRects));
        }

        [Test]
        public void GraphEdgeRoutes_DoNotBundleStructuralMembershipWithRelationshipRoutes()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.core-state");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.core-state");

            PackageGraphEdgeRoute[] routes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, focus)
                    .ToArray();

            Assert.IsTrue(routes.All(route => route.Edge != null));
            Assert.IsFalse(routes.Any(route => route.SharedTrunkId.IndexOf("membership", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsTrue(routes.Any(route => route.HasKind(PackageGraphEdgeKind.IntegrationConnection)));
            Assert.IsFalse(routes.Any(route => route.Zone == PackageGraphEdgeRouteZone.Direct &&
                                               route.HasKind(PackageGraphEdgeKind.IntegrationConnection) &&
                                               route.Bundle.ConnectsPackage("com.deucarian.core-state")));
        }

        [Test]
        public void GraphEdgeRoutes_AvoidPackageAndCategoryObstaclesForApiFocus()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.api");
            IReadOnlyDictionary<string, Rect> groupRects = GetGroupRects(layout);

            PackageGraphEdgeRoute[] routes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, groupRects, focus)
                    .ToArray();

            Assert.IsTrue(routes.Any(route => route.RouteKind == PackageGraphRouteKind.CompositeDependencyIntegration));
            Assert.IsTrue(routes.All(route =>
                !PackageGraphEdgeLayer.RouteCrossesGraphObstacleForTests(route, layout.NodeRects, groupRects)));
        }

        [Test]
        public void GraphEdgeRoutes_SuiteMembershipTargetsPackageCardsAndAvoidsCategories()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.selection-suite");
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, "com.deucarian.selection-suite");
            IReadOnlyDictionary<string, Rect> groupRects = GetGroupRects(layout);

            PackageGraphEdgeRoute[] suiteRoutes =
                PackageGraphEdgeLayer.BuildRoutesForTests(graph, layout.NodeRects, groupRects, focus)
                    .Where(route => route.RouteKind == PackageGraphRouteKind.SuiteMembership)
                    .ToArray();

            Assert.IsNotEmpty(suiteRoutes);
            Assert.IsTrue(suiteRoutes.All(route => route.Edge.FromPackageId == "com.deucarian.selection-suite"));
            Assert.IsTrue(suiteRoutes.All(route => layout.NodeRects.ContainsKey(route.Edge.ToPackageId)));
            Assert.IsTrue(suiteRoutes.All(route =>
                !PackageGraphEdgeLayer.RouteCrossesGraphObstacleForTests(route, layout.NodeRects, groupRects)));
            Assert.IsFalse(PackageGraphEdgeLayer.AnimatesEdgeForTests(PackageGraphEdgeKind.SuiteMembership));
        }

        [Test]
        public void GraphEdgeRoutes_CacheReusesLargeCentralFocusGeometry()
        {
            const int providerCount = 16;
            const int dependentCount = 32;
            const int integrationCount = 12;
            const int optionalCompanionCount = 12;

            List<PackageDefinition> packages = new List<PackageDefinition>();
            string centralPackageId = "com.example.logging";
            string[] providerIds = Enumerable.Range(0, providerCount)
                .Select(index => "com.example.provider-" + index)
                .ToArray();
            string[] optionalCompanionIds = Enumerable.Range(0, optionalCompanionCount)
                .Select(index => "com.example.optional-" + index)
                .ToArray();

            packages.Add(CreatePackage(
                "Logging",
                centralPackageId,
                "Core",
                dependencies: providerIds,
                optionalCompanions: optionalCompanionIds));

            foreach (string providerId in providerIds)
            {
                packages.Add(CreatePackage("Provider " + providerId, providerId, "Core"));
            }

            foreach (int index in Enumerable.Range(0, dependentCount))
            {
                packages.Add(CreatePackage(
                    "Dependent " + index,
                    "com.example.dependent-" + index,
                    "Core",
                    dependencies: new[] { centralPackageId }));
            }

            foreach (int index in Enumerable.Range(0, integrationCount))
            {
                packages.Add(CreatePackage(
                    "Integration " + index,
                    "com.example.integration-" + index,
                    "Integration",
                    "Integration",
                    integrationTargets: new[] { centralPackageId }));
            }

            foreach (string optionalCompanionId in optionalCompanionIds)
            {
                packages.Add(CreatePackage("Optional " + optionalCompanionId, optionalCompanionId, "UI"));
            }

            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(packages);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                centralPackageId);
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, centralPackageId);
            PackageGraphEdgeRouteCache routeCache = new PackageGraphEdgeRouteCache();
            IReadOnlyDictionary<string, Rect> groupRects = GetGroupRects(layout);

            PackageGraphEdgeRoute[] warmRoutes = PackageGraphEdgeLayer.BuildRoutesWithCacheForTests(
                    graph,
                    layout.NodeRects,
                    groupRects,
                    focus,
                    routeCache,
                    out PackageGraphEdgeRouteBuildDiagnostics warmDiagnostics)
                .ToArray();
            PackageGraphLayoutResult awayLayout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                providerIds[0]);
            PackageGraphFocus awayFocus = PackageGraphFocus.Create(graph, providerIds[0]);
            PackageGraphEdgeRoute[] awayRoutes = PackageGraphEdgeLayer.BuildRoutesWithCacheForTests(
                    graph,
                    awayLayout.NodeRects,
                    GetGroupRects(awayLayout),
                    awayFocus,
                    routeCache,
                    out _)
                .ToArray();
            PackageGraphEdgeRoute[] cachedRoutes = PackageGraphEdgeLayer.BuildRoutesWithCacheForTests(
                    graph,
                    layout.NodeRects,
                    groupRects,
                    focus,
                    routeCache,
                    out PackageGraphEdgeRouteBuildDiagnostics cachedDiagnostics)
                .ToArray();

            Assert.IsNotEmpty(warmRoutes);
            Assert.IsNotEmpty(awayRoutes);
            Assert.AreEqual(warmRoutes.Length, cachedRoutes.Length);
            Assert.AreEqual(warmRoutes.Length, warmDiagnostics.RouteCacheMisses);
            Assert.AreEqual(warmRoutes.Length, warmDiagnostics.RouteCacheNoEntryMisses);
            Assert.AreEqual(0, warmDiagnostics.RouteCacheHits);
            Assert.AreEqual(0, warmDiagnostics.RouteCacheLayoutMisses);
            Assert.AreEqual(0, warmDiagnostics.RouteCacheEndpointMisses);
            Assert.AreEqual(0, warmDiagnostics.RouteCacheFocusGraphMisses);
            Assert.AreEqual(0, warmDiagnostics.RouteCacheStyleMisses);
            Assert.AreEqual(cachedRoutes.Length, cachedDiagnostics.RouteCacheHits);
            Assert.AreEqual(0, cachedDiagnostics.RouteCacheMisses);
            Assert.AreEqual(cachedRoutes.Length, cachedDiagnostics.RouteCount);
            Assert.LessOrEqual(
                cachedDiagnostics.RouteCalculationTicks,
                Math.Max(1L, warmDiagnostics.RouteCalculationTicks / 2L));
        }

        [Test]
        public void Layout_CalculatesHierarchicalOverviewWithoutNodeOverlap()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);

            Assert.AreEqual(PackageGraphLayoutMode.Overview, layout.Mode);
            Assert.IsEmpty(layout.NodeRects);
            Assert.IsEmpty(layout.NodeRings);
            Assert.IsEmpty(layout.NodePresentationLevels);
            Assert.AreEqual(1, layout.RingGuides.Count);
            Assert.IsEmpty(layout.SectorLabels);
            Assert.AreEqual(7, layout.GroupNodes.Count);
            Assert.IsTrue(layout.GroupNodes.All(groupNode => !groupNode.Collapsed));
            Assert.IsFalse(layout.GroupNodes.Any(groupNode => groupNode.GroupId == "ui-presentation"));
            Assert.IsFalse(layout.GroupNodes.Any(groupNode => groupNode.GroupId == "world-interaction"));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "infrastructure",
                    "state-data",
                    "runtime-services",
                    "experience-interaction",
                    "tools-quality",
                    "integrations",
                    "suites"
                },
                layout.GroupNodes
                    .Select(groupNode => groupNode.GroupId)
                    .ToArray());
            PackageGraphGroupLayoutNode infrastructure = layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode experience = layout.GroupNodes.Single(groupNode => groupNode.GroupId == "experience-interaction");
            PackageGraphGroupLayoutNode integrations = layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");
            float globalRadius = Vector2.Distance(infrastructure.HubCenter, PackageGraphLayout.GraphCenter);
            Assert.That(globalRadius, Is.InRange(320f, 380f));
            Assert.That(Vector2.Distance(integrations.HubCenter, PackageGraphLayout.GraphCenter), Is.EqualTo(globalRadius).Within(0.1f));
            Assert.AreEqual(2, infrastructure.PackageCount);
            Assert.AreEqual(3, experience.PackageCount);
            Assert.AreEqual(4, integrations.PackageCount);
            Assert.AreEqual(0f, layout.GroupNodes.Max(groupNode => groupNode.OrbitRadius));
            AssertNoOverlaps(layout.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { layout.HubRect }).ToArray());
        }

        [Test]
        public void Layout_NestedCategoryFocusShowsImmediateChildrenOnly()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layoutCalculator = new PackageGraphLayout();

            PackageGraphLayoutResult experienceFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "experience-interaction",
                Vector2.zero);
            PackageGraphLayoutResult uiFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "ui-presentation",
                Vector2.zero);
            PackageGraphLayoutResult worldFocus = layoutCalculator.Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "world-interaction",
                Vector2.zero);

            Assert.AreEqual("experience-interaction", experienceFocus.FocusGroupId);
            Assert.IsTrue(experienceFocus.GroupNodes.Any(groupNode => groupNode.GroupId == "ui-presentation"));
            Assert.IsTrue(experienceFocus.GroupNodes.Any(groupNode => groupNode.GroupId == "world-interaction"));
            Assert.IsFalse(experienceFocus.NodeRects.ContainsKey("com.deucarian.ui-binding"));
            Assert.IsFalse(experienceFocus.NodeRects.ContainsKey("com.deucarian.object-selection"));

            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.ui-binding", "com.deucarian.theming" },
                uiFocus.NodeRects.Keys.ToArray());
            CollectionAssert.AreEquivalent(
                new[] { "com.deucarian.object-selection" },
                worldFocus.NodeRects.Keys.ToArray());
        }

        [Test]
        public void Layout_RootSummaryOrbitIsStableAcrossPresentationLevels()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();

            PackageGraphLayoutResult micro = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.Micro);
            PackageGraphLayoutResult standard = layout.Calculate(
                graph,
                PackageGraphLayoutMode.Overview,
                string.Empty,
                string.Empty,
                Vector2.zero,
                PackageGraphNodePresentationLevel.Compact);
            PackageGraphGroupLayoutNode microInfrastructure =
                micro.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode standardInfrastructure =
                standard.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");

            Assert.IsEmpty(micro.NodeRects);
            Assert.IsEmpty(standard.NodeRects);
            Assert.That(
                Vector2.Distance(microInfrastructure.HubCenter, PackageGraphLayout.GraphCenter),
                Is.EqualTo(Vector2.Distance(standardInfrastructure.HubCenter, PackageGraphLayout.GraphCenter)).Within(0.1f));
            Assert.AreEqual(0f, microInfrastructure.OrbitRadius);
            Assert.AreEqual(0f, standardInfrastructure.OrbitRadius);
            AssertNoOverlaps(micro.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { micro.HubRect }).ToArray());
            AssertNoOverlaps(standard.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { standard.HubRect }).ToArray());
        }

        [Test]
        public void Layout_FocusUsesFullSelectedCardAndCompactRelatedCards()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");
            PackageGraphNodeMetrics fullMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);
            PackageGraphNodeMetrics standardMetrics =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Compact);

            Assert.AreEqual(PackageGraphNodePresentationLevel.Full, layout.NodePresentationLevels["com.deucarian.session"]);
            Assert.AreEqual(PackageGraphNodePresentationLevel.Compact, layout.NodePresentationLevels["com.deucarian.logging"]);
            Assert.That(layout.NodeRects["com.deucarian.session"].width, Is.EqualTo(fullMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.session"].height, Is.EqualTo(fullMetrics.Height).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].width, Is.EqualTo(standardMetrics.Width).Within(0.1f));
            Assert.That(layout.NodeRects["com.deucarian.logging"].height, Is.EqualTo(standardMetrics.Height).Within(0.1f));
        }

        [Test]
        public void Layout_StoresPerfectVisibleOrbitRadiusSeparateFromRootGuide()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(graph);

            Assert.AreEqual(1, layout.RingGuides.Count);
            PackageGraphRingGuide rootGuide = layout.RingGuides.Single();
            Assert.That(rootGuide.Radius, Is.GreaterThan(0f));
            Assert.That(rootGuide.CircleRect.width, Is.EqualTo(rootGuide.CircleRect.height).Within(0.01f));
            Assert.That(rootGuide.CircleRect.width, Is.EqualTo(rootGuide.Radius * 2f).Within(0.01f));
            AssertVectorClose(rootGuide.Center, rootGuide.CircleRect.center, 0.01f);

            foreach (PackageGraphGroupLayoutNode groupNode in layout.GroupNodes.Where(groupNode => !groupNode.Collapsed))
            {
                Assert.That(groupNode.HubRect.width, Is.EqualTo(groupNode.HubRect.height).Within(0.1f));
                Assert.AreEqual(0f, groupNode.OrbitRadius);
                Assert.That(
                    Vector2.Distance(groupNode.HubCenter, PackageGraphLayout.GraphCenter),
                    Is.EqualTo(rootGuide.Radius).Within(0.1f));
            }
        }

        [Test]
        public void Layout_RootOverviewSemanticLevelsRecalculateStableNonOverlappingOrbits()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphLayout layout = new PackageGraphLayout();
            PackageGraphNodePresentationLevel[] levels =
            {
                PackageGraphNodePresentationLevel.IconOnly,
                PackageGraphNodePresentationLevel.Micro,
                PackageGraphNodePresentationLevel.Compact
            };
            List<float> rootRadii = new List<float>();

            foreach (PackageGraphNodePresentationLevel level in levels)
            {
                PackageGraphLayoutResult result = layout.Calculate(
                    graph,
                    PackageGraphLayoutMode.Overview,
                    string.Empty,
                    string.Empty,
                    new Vector2(900f, 620f),
                    level);
                PackageGraphGroupLayoutNode[] topGroups = result.GroupNodes
                    .Where(groupNode => !groupNode.Collapsed)
                    .OrderBy(groupNode => groupNode.Group.SortOrder)
                    .ToArray();
                float rootRadius = Vector2.Distance(topGroups[0].HubCenter, PackageGraphLayout.GraphCenter);

                rootRadii.Add(rootRadius);
                Assert.IsEmpty(result.NodeRects);
                Assert.IsEmpty(result.NodeRings);
                Assert.IsEmpty(result.NodePresentationLevels);
                AssertNoOverlaps(result.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { result.HubRect }).ToArray());

                foreach (PackageGraphGroupLayoutNode groupNode in topGroups)
                {
                    Assert.That(Vector2.Distance(groupNode.HubRect.center, groupNode.HubCenter), Is.LessThan(0.01f));
                    Assert.That(groupNode.HubRect.width, Is.EqualTo(groupNode.HubRect.height).Within(0.1f));
                    Assert.AreEqual(0f, groupNode.OrbitRadius);
                }

                Rect activeBounds = PackageGraphActiveLayoutBounds.Calculate(result);
                foreach (Rect rect in result.GroupNodes.Select(groupNode => groupNode.Rect).Concat(new[] { result.HubRect }))
                {
                    Assert.IsTrue(activeBounds.Contains(rect.min), rect + " min outside active bounds");
                    Assert.IsTrue(activeBounds.Contains(rect.max), rect + " max outside active bounds");
                }
            }

            Assert.That(rootRadii[0], Is.EqualTo(rootRadii[1]).Within(0.1f));
            Assert.That(rootRadii[1], Is.EqualTo(rootRadii[2]).Within(0.1f));
            Assert.That(rootRadii[1], Is.LessThan(380f));
        }

        [Test]
        public void Layout_GroupFocusCentersGroupAndExpandsOnlyDirectChildren()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.GroupFocus,
                string.Empty,
                "infrastructure",
                Vector2.zero);
            PackageGraphGroupLayoutNode focusedGroup = layout.GroupNodes.Single(groupNode =>
                groupNode.GroupId == "infrastructure" && groupNode.Focused);

            Assert.AreEqual(PackageGraphLayoutMode.GroupFocus, layout.Mode);
            Assert.AreEqual("infrastructure", layout.FocusGroupId);
            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, focusedGroup.HubCenter), Is.LessThan(0.1f));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "com.deucarian.editor",
                    "com.deucarian.logging"
                },
                layout.NodeRects.Keys.ToArray());
            Assert.IsFalse(layout.GroupNodes.Any(groupNode => groupNode.Collapsed && groupNode.GroupId == "runtime-services"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.session"));
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_CalculatesEgoFocusPositionsAroundSelectedPackage()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.session");

            Rect session = layout.NodeRects["com.deucarian.session"];
            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect sessionIntegration = layout.NodeRects["com.deucarian.session.api-integration"];
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode providerContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode integrationContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");

            Assert.AreEqual(PackageGraphLayoutMode.Focus, layout.Mode);
            Assert.AreEqual("com.deucarian.session", layout.FocusPackageId);
            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, session.center), Is.LessThan(0.1f));
            Assert.Less(logging.center.x, session.center.x);
            Assert.That(logging.center.y, Is.EqualTo(PackageGraphLayout.GraphCenter.y).Within(0.1f));
            Assert.That(sessionIntegration.center.x, Is.EqualTo(PackageGraphLayout.GraphCenter.x).Within(0.1f));
            Assert.Greater(sessionIntegration.center.y, session.center.y);
            Assert.Less(owningContext.HubCenter.y, session.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(session.center.x).Within(0.1f));
            Assert.Less(providerContext.HubCenter.x, logging.xMin);
            Assert.That(providerContext.HubCenter.y, Is.EqualTo(logging.center.y).Within(0.1f));
            Assert.Greater(integrationContext.HubCenter.y, sessionIntegration.yMax);
            Assert.That(integrationContext.HubCenter.x, Is.EqualTo(sessionIntegration.center.x).Within(0.1f));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.api"));
            Assert.IsFalse(layout.NodeRects.ContainsKey("com.deucarian.theming"));
            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed && groupNode.SummaryLabel.Contains("related package")));
            CollectionAssert.Contains(
                integrationContext.RepresentedPackageIds.ToArray(),
                "com.deucarian.session.api-integration");
            CollectionAssert.Contains(
                owningContext.RepresentedPackageIds.ToArray(),
                "com.deucarian.session");
            Assert.AreEqual(layout.ActiveCenter, session.center);
            Assert.IsEmpty(layout.RingGuides);
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_EgoFocusUsesFixedCategoryRailsForApi()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.api");

            Rect api = layout.NodeRects["com.deucarian.api"];
            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect sessionIntegration = layout.NodeRects["com.deucarian.session.api-integration"];
            Rect objectLoadingIntegration = layout.NodeRects["com.deucarian.object-loading.api-integration"];
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode providerContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            PackageGraphGroupLayoutNode integrationContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "integrations");

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, api.center), Is.LessThan(0.1f));
            Assert.Less(logging.center.x, api.center.x);
            Assert.Less(providerContext.HubCenter.x, logging.xMin);
            Assert.That(providerContext.HubCenter.y, Is.EqualTo(logging.center.y).Within(0.1f));
            Assert.Less(owningContext.HubCenter.y, api.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(api.center.x).Within(0.1f));
            Assert.Greater(sessionIntegration.center.y, api.center.y);
            Assert.That(sessionIntegration.center.y, Is.EqualTo(objectLoadingIntegration.center.y).Within(0.1f));
            Assert.Greater(integrationContext.HubCenter.y, sessionIntegration.yMax);
            Assert.That(
                integrationContext.HubCenter.x,
                Is.EqualTo((sessionIntegration.center.x + objectLoadingIntegration.center.x) * 0.5f).Within(0.1f));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "com.deucarian.session.api-integration",
                    "com.deucarian.object-loading.api-integration"
                },
                integrationContext.RepresentedPackageIds.ToArray());
        }

        [Test]
        public void GraphMembershipRoutes_UseBusForMultiPackageContextCategory()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphCanvas canvas = new PackageGraphCanvas(_ => { }, (_, __) => { }, () => { });

            canvas.SetGraph(graph, "com.deucarian.api", "com.deucarian.api", actionsEnabled: true);

            PackageGraphStructuralMembershipRoute integrationRoute =
                canvas.StructuralMembershipRoutesForTests.Single(route => route.GroupId == "integrations");
            PackageGraphStructuralMembershipRoute owningRoute =
                canvas.StructuralMembershipRoutesForTests.Single(route => route.GroupId == "runtime-services");

            Assert.IsTrue(integrationRoute.UsesBus);
            Assert.AreEqual(2, integrationRoute.PackageIds.Count);
            Assert.GreaterOrEqual(integrationRoute.Segments.Count, 4);
            Assert.IsTrue(integrationRoute.Segments.All(segment => segment.Length > 0.01f));
            Assert.IsFalse(owningRoute.UsesBus);
            Assert.AreEqual(1, owningRoute.PackageIds.Count);
            Assert.AreEqual("com.deucarian.api", owningRoute.PackageIds.Single());
        }

        [Test]
        public void Layout_CentersLoggingAndStacksDependentsInRightColumn()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.logging");

            Rect logging = layout.NodeRects["com.deucarian.logging"];
            Rect editor = layout.NodeRects["com.deucarian.editor"];
            Rect api = layout.NodeRects["com.deucarian.api"];

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, logging.center), Is.LessThan(0.1f));
            Assert.Less(editor.center.x, logging.center.x);
            foreach (string packageId in new[]
                     {
                         "com.deucarian.api",
                         "com.deucarian.object-loading",
                         "com.deucarian.session",
                         "com.deucarian.object-selection",
                         "com.deucarian.theming",
                         "com.deucarian.diagnostics",
                         "com.deucarian.package-installer"
                     })
            {
                Rect dependent = layout.NodeRects[packageId];
                Assert.Greater(dependent.center.x, logging.center.x);
                Assert.That(dependent.center.x, Is.EqualTo(api.center.x).Within(0.1f));
            }

            Assert.IsFalse(layout.HasUnrelatedSummary);
            Assert.IsTrue(layout.GroupNodes.Any(groupNode => groupNode.Collapsed));
            PackageGraphGroupLayoutNode runtimeContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "runtime-services");
            PackageGraphGroupLayoutNode owningContext =
                layout.GroupNodes.Single(groupNode => groupNode.GroupId == "infrastructure");
            Assert.Less(owningContext.HubCenter.y, logging.yMin);
            Assert.That(owningContext.HubCenter.x, Is.EqualTo(logging.center.x).Within(0.1f));
            Assert.Greater(runtimeContext.HubCenter.x, api.xMax);
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "com.deucarian.api",
                    "com.deucarian.object-loading",
                    "com.deucarian.session"
                },
                runtimeContext.RepresentedPackageIds.ToArray());
            AssertNoOverlaps(layout.NodeRects.Values.Concat(layout.GroupNodes.Select(groupNode => groupNode.Rect)).ToArray());
        }

        [Test]
        public void Layout_InvalidFocusFallsBackToOverview()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                "com.deucarian.missing");

            Assert.AreEqual(PackageGraphLayoutMode.Overview, layout.Mode);
            Assert.IsEmpty(layout.FocusPackageId);
        }

        [Test]
        public void Focus_SelectingObjectLoadingShowsDependencyIntegrationAndCompanionContext()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphFocus focus = PackageGraphFocus.Create(
                graph,
                "com.deucarian.object-loading");

            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.logging"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.object-loading.api-integration"));
            Assert.IsFalse(focus.IsPackageRelated("com.deucarian.api"));
            Assert.IsTrue(focus.IsPackageRelated("com.deucarian.diagnostics"));
            Assert.IsFalse(focus.IsPackageRelated("com.deucarian.theming"));

            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.logging",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.diagnostics",
                PackageGraphEdgeKind.OptionalCompanion);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.OptionalCompanion);

            PackageGraphEdge apiRequirementEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.api" &&
                edge.ToPackageId == "com.deucarian.object-loading.api-integration" &&
                edge.Kind == PackageGraphEdgeKind.HardDependency);
            Assert.IsFalse(focus.IsEdgeVisible(apiRequirementEdge));

            PackageGraphEdge apiIntegrationEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.object-loading.api-integration" &&
                edge.ToPackageId == "com.deucarian.api" &&
                edge.Kind == PackageGraphEdgeKind.IntegrationConnection);
            Assert.IsFalse(focus.IsEdgeVisible(apiIntegrationEdge));

            PackageGraphEdge unrelatedThemingEdge = graph.Edges.Single(edge =>
                edge.FromPackageId == "com.deucarian.editor" &&
                edge.ToPackageId == "com.deucarian.theming");
            Assert.IsFalse(focus.IsEdgeVisible(unrelatedThemingEdge));
        }

        [Test]
        public void Focus_SelectingIntegrationShowsHardRequirementsAndIntegrationConnectionsSeparately()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());

            PackageGraphFocus focus = PackageGraphFocus.Create(
                graph,
                "com.deucarian.object-loading.api-integration");

            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.api",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.HardDependency);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.object-loading",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading.api-integration",
                "com.deucarian.api",
                PackageGraphEdgeKind.IntegrationConnection);
            AssertEdgeVisible(
                graph,
                focus,
                "com.deucarian.object-loading",
                "com.deucarian.object-loading.api-integration",
                PackageGraphEdgeKind.OptionalCompanion);
        }

        [Test]
        public void Focus_ShowsSuiteMembershipEdgesWithoutSuiteRegions()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphSuiteRegion selectionSuite = graph.SuiteRegions.Single(region =>
                region.SuitePackageId == "com.deucarian.selection-suite");
            PackageGraphEdge suiteMembershipEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.SuiteMembership &&
                edge.FromPackageId == selectionSuite.SuitePackageId &&
                edge.ToPackageId == "com.deucarian.ui-binding");

            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty);

            Assert.IsFalse(overview.IsSuiteRegionVisible(selectionSuite));
            Assert.IsFalse(overview.IsEdgeVisible(suiteMembershipEdge));

            PackageGraphFocus focusedSuite = PackageGraphFocus.Create(
                graph,
                selectionSuite.SuitePackageId);

            Assert.IsFalse(focusedSuite.IsSuiteRegionVisible(selectionSuite));
            Assert.IsTrue(focusedSuite.IsEdgeVisible(suiteMembershipEdge));
            Assert.IsTrue(focusedSuite.IsPackageRelated("com.deucarian.object-selection.core-state-integration"));

            PackageGraphLayoutResult suiteLayout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                selectionSuite.SuitePackageId);
            Rect suiteRect = suiteLayout.NodeRects[selectionSuite.SuitePackageId];

            Assert.That(Vector2.Distance(PackageGraphLayout.GraphCenter, suiteRect.center), Is.LessThan(0.1f));
            Assert.IsEmpty(suiteLayout.RingGuides);
            Assert.IsEmpty(suiteLayout.SectorLabels);
            Assert.IsFalse(suiteLayout.HasUnrelatedSummary);
            Assert.IsTrue(suiteLayout.GroupNodes.Any(groupNode => groupNode.Collapsed));
        }

        [Test]
        public void Focus_OverviewHidesNormalDependencyEdges()
        {
            PackageDefinition editor = CreatePackage("Editor", "com.example.editor", "Editor");
            PackageDefinition logging = CreatePackage(
                "Logging",
                "com.example.logging",
                "Core",
                dependencies: new[] { editor.PackageId });
            PackageGraphModel graph = new PackageGraphBuilder(_ => true)
                .Build(new[] { editor, logging });
            PackageGraphFocus overview = PackageGraphFocus.Create(graph, string.Empty);
            PackageGraphEdge dependencyEdge = graph.Edges.Single(edge =>
                edge.Kind == PackageGraphEdgeKind.HardDependency &&
                edge.FromPackageId == editor.PackageId &&
                edge.ToPackageId == logging.PackageId);

            Assert.IsFalse(overview.IsEdgeVisible(dependencyEdge));

            PackageGraphFocus focused = PackageGraphFocus.Create(graph, logging.PackageId);

            Assert.IsTrue(focused.IsEdgeVisible(dependencyEdge));
            Assert.IsTrue(focused.IsEdgeEmphasized(dependencyEdge));
        }

        [Test]
        public void Focus_PreviewEmphasizesOnlyTheRelatedPackageRoute()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            const string FocusPackageId = "com.deucarian.api";
            const string PreviewPackageId = "com.deucarian.logging";
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, FocusPackageId);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                FocusPackageId);
            PackageGraphEdgeRoute[] routes = PackageGraphEdgeLayer.BuildRoutesForTests(
                    graph,
                    layout.NodeRects,
                    focus)
                .Where(route => route.Bundle.ConnectsPackage(FocusPackageId))
                .ToArray();
            PackageGraphEdgeRoute previewRoute = routes.Single(route =>
                route.Bundle.ConnectsPackage(PreviewPackageId));
            PackageGraphEdgeRoute unrelatedRoute = routes.First(route =>
                !route.Bundle.ConnectsPackage(PreviewPackageId));

            Assert.IsTrue(PackageGraphEdgeLayer.IsRouteEmphasized(previewRoute, focus, PreviewPackageId));
            Assert.IsFalse(PackageGraphEdgeLayer.IsRouteEmphasized(unrelatedRoute, focus, PreviewPackageId));
            Assert.IsTrue(PackageGraphEdgeLayer.IsRouteEmphasized(unrelatedRoute, focus, string.Empty));
        }

        [Test]
        public void GraphView_FocusedRelationshipTooltipNamesRelationshipType()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(CreateDefaultGraphPackages());
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            view.SetGraph(graph, "com.deucarian.api", actionsEnabled: true);

            VisualElement related = FindGraphNode(view, "com.deucarian.logging");
            StringAssert.Contains("Relationship:", related.tooltip);
            StringAssert.Contains("Required dependency", related.tooltip);
            StringAssert.Contains("Deucarian API uses Deucarian Logging", related.tooltip);
        }

        [Test]
        public void GraphView_DenseOverflowSummaryIsKeyboardActionableAndCopiesHiddenIds()
        {
            const int providerCount = 49;
            string centralPackageId = "com.example.dense-root";
            string[] providerIds = Enumerable.Range(0, providerCount)
                .Select(index => "com.example.dense-provider-" + index)
                .ToArray();
            List<PackageDefinition> packages = new List<PackageDefinition>
            {
                CreatePackage(
                    "Dense Root",
                    centralPackageId,
                    "Core",
                    dependencies: providerIds)
            };
            packages.AddRange(providerIds.Select(providerId =>
                CreatePackage("Provider " + providerId, providerId, "Core")));
            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(packages);
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });
            view.SetGraph(graph, centralPackageId, actionsEnabled: true);
            PackageGraphOverflowSummaryElement summary = FindByClass(view, "dpi-graph-overflow-summary")
                .OfType<PackageGraphOverflowSummaryElement>()
                .Single();
            Label focusContext = FindByClass(view, "dpi-ecosystem-graph__hidden-related")
                .OfType<Label>()
                .Single();
            string previousClipboard = EditorGUIUtility.systemCopyBuffer;

            try
            {
                EditorGUIUtility.systemCopyBuffer = string.Empty;
                Assert.AreEqual("Focus includes direct relations (1 summarized)", focusContext.text);
                StringAssert.Contains(
                    "1 dense direct relationship is summarized behind the +N overflow summary",
                    focusContext.tooltip);
                StringAssert.DoesNotContain("every direct relationship visible", focusContext.tooltip);
                Assert.AreEqual(DisplayStyle.Flex, focusContext.style.display.value);
                Assert.IsTrue(summary.focusable);
                Assert.AreEqual(0, summary.tabIndex);
                Assert.AreEqual(PickingMode.Position, summary.pickingMode);
                Assert.IsTrue(summary.HasKeyboardActivationForTests);

                summary.ActivateForTests();

                StringAssert.Contains("Additional prerequisites: 1", EditorGUIUtility.systemCopyBuffer);
                StringAssert.Contains("com.example.dense-provider-", EditorGUIUtility.systemCopyBuffer);
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = previousClipboard;
            }
        }

        [Test]
        public void GraphView_DenseOverflowDoesNotAdvertiseHiddenCheckingNode()
        {
            const int ProviderCount = 49;
            const string RootPackageId = "com.example.dense-root";
            const string CheckingPackageId = "com.example.dense-provider-checking";
            string[] providerIds = Enumerable.Range(0, ProviderCount - 1)
                .Select(index => "com.example.dense-provider-" + index.ToString("D2"))
                .Concat(new[] { CheckingPackageId })
                .ToArray();
            List<PackageDefinition> packages = new List<PackageDefinition>
            {
                CreatePackage(
                    "Dense Root",
                    RootPackageId,
                    "Core",
                    dependencies: providerIds)
            };
            packages.AddRange(providerIds.Select((providerId, index) =>
                CreatePackage(
                    providerId == CheckingPackageId ? "ZZZ Checking" : "Provider " + index.ToString("D2"),
                    providerId,
                    "Core")));
            PackageGraphModel graph = new PackageGraphBuilder(
                    _ => true,
                    _ => PackageChannel.Stable,
                    package => package.PackageId == CheckingPackageId
                        ? PackageUpdateStatus.Checking(
                            package,
                            PackageChannel.Stable,
                            package.StableUrl)
                        : null)
                .Build(packages);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                RootPackageId);
            PackageGraphView view = new PackageGraphView(_ => { }, (_, __) => { });

            Assert.AreEqual(1, layout.OverflowSummaries.Count);
            Assert.IsFalse(layout.NodeRects.ContainsKey(CheckingPackageId));

            view.SetGraph(graph, RootPackageId, actionsEnabled: true);

            CollectionAssert.DoesNotContain(
                FindByClass(view, "dpi-graph-legend__label")
                    .OfType<Label>()
                    .Select(label => label.text)
                    .ToArray(),
                "Checking");
        }

        [Test]
        public void Layout_DenseContextGroupCardsDoNotOverlap()
        {
            const int CompanionCount = 49;
            const string RootPackageId = "com.example.dense-root";
            string[] companionGroupIds =
            {
                "companion-a",
                "companion-b",
                "companion-c",
                "companion-d"
            };
            PackageGraphGroup[] groups =
            {
                new PackageGraphGroup("root", "Root", string.Empty, string.Empty, 0, string.Empty, string.Empty),
                new PackageGraphGroup("companion-a", "Companion A", string.Empty, string.Empty, 10, string.Empty, string.Empty),
                new PackageGraphGroup("companion-b", "Companion B", string.Empty, string.Empty, 20, string.Empty, string.Empty),
                new PackageGraphGroup("companion-c", "Companion C", string.Empty, string.Empty, 30, string.Empty, string.Empty),
                new PackageGraphGroup("companion-d", "Companion D", string.Empty, string.Empty, 40, string.Empty, string.Empty)
            };
            string[] companionIds = Enumerable.Range(0, CompanionCount)
                .Select(index => "com.example.companion-" + index.ToString("D2"))
                .ToArray();
            List<PackageDefinition> packages = new List<PackageDefinition>
            {
                CreatePackage(
                    "Dense Root",
                    RootPackageId,
                    "Core",
                    optionalCompanions: companionIds,
                    groupId: "root")
            };
            packages.AddRange(companionIds.Select((packageId, index) =>
                CreatePackage(
                    "Companion " + index.ToString("D2"),
                    packageId,
                    "Core",
                    groupId: companionGroupIds[index % companionGroupIds.Length])));
            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(packages, groups);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                RootPackageId);
            PackageGraphGroupLayoutNode[] contextGroups = layout.GroupNodes
                .Where(group => companionGroupIds.Contains(
                    group.GroupId,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual(1, layout.OverflowSummaries.Count);
            Assert.AreEqual(companionGroupIds.Length, contextGroups.Length);
            AssertNoOverlaps(contextGroups.Select(group => group.Rect).ToArray());
            AssertNoOverlaps(layout.GroupNodes.Select(group => group.Rect).ToArray());
        }

        [Test]
        public void LargeGraph_CentralPackageFocusUsesPrecomputedDirectRelationships()
        {
            const int providerCount = 120;
            const int dependentCount = 240;
            const int integrationCount = 80;
            const int optionalCompanionCount = 80;

            List<PackageDefinition> packages = new List<PackageDefinition>();
            string centralPackageId = "com.example.logging";
            string[] providerIds = Enumerable.Range(0, providerCount)
                .Select(index => "com.example.provider-" + index)
                .ToArray();
            string[] optionalCompanionIds = Enumerable.Range(0, optionalCompanionCount)
                .Select(index => "com.example.optional-" + index)
                .ToArray();

            packages.Add(CreatePackage(
                "Logging",
                centralPackageId,
                "Core",
                dependencies: providerIds,
                optionalCompanions: optionalCompanionIds));

            foreach (string providerId in providerIds)
            {
                packages.Add(CreatePackage("Provider " + providerId, providerId, "Core"));
            }

            foreach (int index in Enumerable.Range(0, dependentCount))
            {
                packages.Add(CreatePackage(
                    "Dependent " + index,
                    "com.example.dependent-" + index,
                    "Core",
                    dependencies: new[] { centralPackageId }));
            }

            foreach (int index in Enumerable.Range(0, integrationCount))
            {
                packages.Add(CreatePackage(
                    "Integration " + index,
                    "com.example.integration-" + index,
                    "Integration",
                    "Integration",
                    integrationTargets: new[] { centralPackageId }));
            }

            foreach (string optionalCompanionId in optionalCompanionIds)
            {
                packages.Add(CreatePackage("Optional " + optionalCompanionId, optionalCompanionId, "UI"));
            }

            PackageGraphModel graph = new PackageGraphBuilder(_ => false).Build(packages);
            PackageGraphFocus focus = PackageGraphFocus.Create(graph, centralPackageId);
            PackageGraphLayoutResult layout = new PackageGraphLayout().Calculate(
                graph,
                PackageGraphLayoutMode.Focus,
                centralPackageId);

            Assert.AreEqual(providerCount, graph.GetHardDependencyProviderEdges(centralPackageId).Count);
            Assert.AreEqual(dependentCount, graph.GetHardDependencyDependentEdges(centralPackageId).Count);
            Assert.AreEqual(integrationCount, graph.GetIntegrationEdges(centralPackageId).Count);
            Assert.AreEqual(optionalCompanionCount, graph.GetOptionalCompanionEdges(centralPackageId).Count);
            Assert.AreEqual(
                1 + providerCount + dependentCount + integrationCount + optionalCompanionCount,
                focus.RelatedPackageIds.Count);
            Assert.AreEqual(4, layout.OverflowSummaries.Count);
            Assert.AreEqual(
                focus.RelatedPackageIds.Count - layout.NodeRects.Count,
                layout.OverflowSummaries.Sum(summary => summary.HiddenCount));
            Assert.AreEqual(1 + 4 * 48, layout.NodeRects.Count);
            AssertNoOverlaps(layout.NodeRects.Values.ToArray());
            AssertNoOverlaps(
                layout.NodeRects.Values
                    .Concat(layout.OverflowSummaries.Select(summary => summary.Rect))
                    .ToArray());
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category,
            string metadataType = null,
            string[] dependencies = null,
            string[] optionalIntegrations = null,
            string[] integrationTargets = null,
            string[] suiteMembers = null,
            string[] optionalCompanions = null,
            string[] recommendedWith = null,
            string ecosystemGroup = null,
            string groupId = null)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                displayName + " package.",
                dependencies ?? Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/" + packageId + ".git#develop",
                category: category,
                metadataType: metadataType,
                optionalIntegrations: optionalIntegrations,
                integrationTargets: integrationTargets,
                suiteMembers: suiteMembers,
                optionalCompanions: optionalCompanions,
                recommendedWith: recommendedWith,
                ecosystemGroup: ecosystemGroup,
                groupId: groupId);
        }

        private static PackageDefinition[] CreateDefaultGraphPackages()
        {
            return new[]
            {
                CreatePackage("Deucarian Editor", "com.deucarian.editor", "Editor"),
                CreatePackage(
                    "Deucarian Logging",
                    "com.deucarian.logging",
                    "Core",
                    dependencies: new[] { "com.deucarian.editor" }),
                CreatePackage(
                    "Deucarian API",
                    "com.deucarian.api",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[]
                    {
                        "com.deucarian.session.api-integration",
                        "com.deucarian.object-loading.api-integration"
                    }),
                CreatePackage(
                    "Deucarian Core State",
                    "com.deucarian.core-state",
                    "Core",
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Object Loading",
                    "com.deucarian.object-loading",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.object-loading.api-integration" },
                    optionalCompanions: new[]
                    {
                        "com.deucarian.diagnostics",
                        "com.deucarian.object-loading.api-integration"
                    }),
                CreatePackage(
                    "Deucarian Session",
                    "com.deucarian.session",
                    "Core",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.session.api-integration" }),
                CreatePackage(
                    "Deucarian UI Binding",
                    "com.deucarian.ui-binding",
                    "UI",
                    "OptionalIntegration",
                    optionalIntegrations: new[] { "com.deucarian.ui-binding.core-state-integration" },
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Object Selection",
                    "com.deucarian.object-selection",
                    "World",
                    "OptionalIntegration",
                    dependencies: new[] { "com.deucarian.logging" },
                    optionalIntegrations: new[] { "com.deucarian.object-selection.core-state-integration" },
                    recommendedWith: new[] { "com.deucarian.selection-suite" }),
                CreatePackage(
                    "Deucarian Theming",
                    "com.deucarian.theming",
                    "UI",
                    "OptionalIntegration",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Diagnostics",
                    "com.deucarian.diagnostics",
                    "Tools",
                    "OptionalIntegration",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Package Installer",
                    "com.deucarian.package-installer",
                    "Tools",
                    "Tool",
                    dependencies: new[]
                    {
                        "com.deucarian.editor",
                        "com.deucarian.logging"
                    }),
                CreatePackage(
                    "Deucarian Session API Integration",
                    "com.deucarian.session.api-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.session",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian Object Loading API Integration",
                    "com.deucarian.object-loading.api-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.api",
                        "com.deucarian.object-loading"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.object-loading",
                        "com.deucarian.api"
                    }),
                CreatePackage(
                    "Deucarian UI Binding Core State Integration",
                    "com.deucarian.ui-binding.core-state-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.ui-binding",
                        "com.deucarian.core-state"
                    }),
                CreatePackage(
                    "Deucarian Object Selection Core State Integration",
                    "com.deucarian.object-selection.core-state-integration",
                    "Integration",
                    "Integration",
                    dependencies: new[]
                    {
                        "com.deucarian.object-selection",
                        "com.deucarian.core-state"
                    },
                    integrationTargets: new[]
                    {
                        "com.deucarian.object-selection",
                        "com.deucarian.core-state"
                    }),
                CreatePackage(
                    "Deucarian Selection Suite",
                    "com.deucarian.selection-suite",
                    "Suites",
                    "Suite",
                    dependencies: new[]
                    {
                        "com.deucarian.core-state",
                        "com.deucarian.ui-binding",
                        "com.deucarian.object-selection",
                        "com.deucarian.ui-binding.core-state-integration",
                        "com.deucarian.object-selection.core-state-integration"
                    },
                    suiteMembers: new[]
                    {
                        "com.deucarian.core-state",
                        "com.deucarian.ui-binding",
                        "com.deucarian.object-selection",
                        "com.deucarian.ui-binding.core-state-integration",
                        "com.deucarian.object-selection.core-state-integration"
                    })
            };
        }

        private static Dictionary<string, Rect> CreateInterpolatedNodeRects(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Rect> result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Rect> targetRect in target.NodeRects)
            {
                Rect start = source.NodeRects.TryGetValue(targetRect.Key, out Rect sourceRect)
                    ? sourceRect
                    : CenterRectOnForTests(targetRect.Value, source.ActiveCenter);
                result[targetRect.Key] = LerpRectForTests(start, targetRect.Value, progress);
            }

            return result;
        }

        private static Dictionary<string, Rect> CreateInterpolatedGroupRects(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Rect> result = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                Rect start = sourceGroup != null
                    ? sourceGroup.Rect
                    : CenterRectOnForTests(targetGroup.Rect, source.ActiveCenter);
                result[targetGroup.GroupId] = LerpRectForTests(start, targetGroup.Rect, progress);
            }

            return result;
        }

        private static Dictionary<string, Vector2> CreateInterpolatedGroupCenters(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, Vector2> result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                Vector2 start = sourceGroup != null ? sourceGroup.HubCenter : source.ActiveCenter;
                result[targetGroup.GroupId] = Vector2.Lerp(start, targetGroup.HubCenter, progress);
            }

            return result;
        }

        private static Dictionary<string, float> CreateInterpolatedGroupRadii(
            PackageGraphLayoutResult source,
            PackageGraphLayoutResult target,
            float progress)
        {
            Dictionary<string, float> result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageGraphGroupLayoutNode targetGroup in target.GroupNodes)
            {
                PackageGraphGroupLayoutNode sourceGroup = FindGroupForTests(source, targetGroup.GroupId);
                float start = sourceGroup != null ? sourceGroup.OrbitRadius : 0f;
                result[targetGroup.GroupId] = Mathf.Lerp(start, targetGroup.OrbitRadius, progress);
            }

            return result;
        }

        private static PackageGraphGroupLayoutNode FindGroupForTests(
            PackageGraphLayoutResult layout,
            string groupId)
        {
            return layout.GroupNodes.FirstOrDefault(groupNode =>
                groupNode != null &&
                string.Equals(groupNode.GroupId, groupId, StringComparison.OrdinalIgnoreCase));
        }

        private static Rect LerpRectForTests(Rect start, Rect end, float progress)
        {
            return new Rect(
                Mathf.Lerp(start.x, end.x, progress),
                Mathf.Lerp(start.y, end.y, progress),
                Mathf.Lerp(start.width, end.width, progress),
                Mathf.Lerp(start.height, end.height, progress));
        }

        private static Rect CenterRectOnForTests(Rect rect, Vector2 center)
        {
            return new Rect(
                center.x - rect.width * 0.5f,
                center.y - rect.height * 0.5f,
                rect.width,
                rect.height);
        }

        private static void AssertEdgeVisible(
            PackageGraphModel graph,
            PackageGraphFocus focus,
            string fromPackageId,
            string toPackageId,
            PackageGraphEdgeKind kind)
        {
            PackageGraphEdge edge = graph.Edges.Single(candidate =>
                candidate.Kind == kind &&
                candidate.FromPackageId == fromPackageId &&
                candidate.ToPackageId == toPackageId);

            Assert.IsTrue(focus.IsEdgeVisible(edge), edge.Key);
            Assert.IsTrue(focus.IsEdgeEmphasized(edge), edge.Key);
        }

        private static void AssertCategoryAndKind(
            PackageGraphModel graph,
            string packageId,
            string expectedCategory,
            string expectedKind)
        {
            Assert.IsTrue(graph.TryGetNode(packageId, out PackageGraphNode node), packageId);
            Assert.AreEqual(
                expectedCategory,
                PackageGraphHierarchyDisplay.GetPackageHierarchyPath(graph, node.PackageDefinition));
            Assert.AreEqual(expectedKind, PackageGraphHierarchyDisplay.GetPackageKind(node.PackageDefinition));
        }

        private static void AssertCameraClose(
            PackageGraphCameraState expected,
            PackageGraphCameraState actual)
        {
            AssertVectorClose(expected.Pan, actual.Pan, 0.001f);
            Assert.That(actual.Zoom, Is.EqualTo(expected.Zoom).Within(0.001f));
        }

        private static void AssertVectorClose(
            Vector2 expected,
            Vector2 actual,
            float tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
        }

        private static float GetAngle(Rect rect)
        {
            Vector2 direction = rect.center - PackageGraphLayout.GraphCenter;
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        private static float CircularMean(params float[] angles)
        {
            float x = 0f;
            float y = 0f;

            foreach (float angle in angles)
            {
                float radians = angle * Mathf.Deg2Rad;
                x += Mathf.Cos(radians);
                y += Mathf.Sin(radians);
            }

            return Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }

        private static float DeltaAngle(float first, float second)
        {
            return Mathf.Abs(Mathf.DeltaAngle(first, second));
        }

        private static void AssertNoOverlaps(IReadOnlyList<UnityEngine.Rect> rects)
        {
            for (int firstIndex = 0; firstIndex < rects.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < rects.Count; secondIndex++)
                {
                    Assert.IsFalse(
                        rects[firstIndex].Overlaps(rects[secondIndex]),
                        rects[firstIndex] + " overlaps " + rects[secondIndex]);
                }
            }
        }

        private static void AssertGroupClustersSeparated(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            float minimumGap)
        {
            Dictionary<string, PackageGraphGroupLayoutNode> groupNodeById = layout.GroupNodes
                .Where(groupNode => groupNode != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            PackageGraphGroupLayoutNode[] topGroups = layout.GroupNodes
                .Where(groupNode => groupNode != null && !groupNode.Collapsed)
                .OrderBy(groupNode => groupNode.Group.SortOrder)
                .ToArray();

            for (int firstIndex = 0; firstIndex < topGroups.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < topGroups.Length; secondIndex++)
                {
                    PackageGraphGroupLayoutNode first = topGroups[firstIndex];
                    PackageGraphGroupLayoutNode second = topGroups[secondIndex];
                    Rect[] firstRects = GetTopLevelClusterRects(graph, layout, first, groupNodeById);
                    Rect[] secondRects = GetTopLevelClusterRects(graph, layout, second, groupNodeById);

                    foreach (Rect firstRect in firstRects)
                    {
                        foreach (Rect secondRect in secondRects)
                        {
                            Assert.IsFalse(
                                Expand(firstRect, minimumGap * 0.5f).Overlaps(Expand(secondRect, minimumGap * 0.5f)),
                                first.GroupId + " cluster element is too close to " + second.GroupId);
                        }
                    }
                }
            }
        }

        private static Rect[] GetTopLevelClusterRects(
            PackageGraphModel graph,
            PackageGraphLayoutResult layout,
            PackageGraphGroupLayoutNode groupNode,
            IReadOnlyDictionary<string, PackageGraphGroupLayoutNode> groupNodeById)
        {
            List<Rect> rects = new List<Rect> { groupNode.Rect };

            foreach (PackageGraphNode node in graph.Nodes)
            {
                if (node == null ||
                    !string.Equals(node.GroupId, groupNode.GroupId, StringComparison.OrdinalIgnoreCase) ||
                    !layout.NodeRects.TryGetValue(node.PackageId, out Rect nodeRect))
                {
                    continue;
                }

                rects.Add(nodeRect);
            }

            foreach (PackageGraphGroup childGroup in graph.GetChildGroups(groupNode.GroupId))
            {
                if (childGroup != null &&
                    groupNodeById.TryGetValue(childGroup.Id, out PackageGraphGroupLayoutNode childGroupNode))
                {
                    rects.Add(childGroupNode.Rect);
                }
            }

            return rects.ToArray();
        }

        private static Rect CreateExpectedFitBounds(PackageGraphLayoutResult layout)
        {
            return PackageGraphActiveLayoutBounds.Calculate(layout);
        }

        private static IReadOnlyDictionary<string, Rect> GetGroupRects(PackageGraphLayoutResult layout)
        {
            return layout.GroupNodes
                .Where(groupNode => groupNode != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Rect, StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, Rect> GetCanvasGroupRects(PackageGraphCanvas canvas)
        {
            return canvas.GroupLayoutNodesForTests
                .Where(groupNode => groupNode != null)
                .GroupBy(groupNode => groupNode.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Rect, StringComparer.OrdinalIgnoreCase);
        }

        private static void AssertGroupRectsEqual(
            IReadOnlyDictionary<string, Rect> expected,
            IReadOnlyDictionary<string, Rect> actual,
            float tolerance)
        {
            CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray());

            foreach (KeyValuePair<string, Rect> pair in expected)
            {
                AssertRectsEqual(pair.Value, actual[pair.Key], tolerance);
            }
        }

        private static Rect GetInlineRect(VisualElement element)
        {
            return new Rect(
                element.style.left.value.value,
                element.style.top.value.value,
                element.style.width.value.value,
                element.style.height.value.value);
        }

        private static string CreateRouteId(PackageGraphEdgeRoute route)
        {
            return route.Bundle.Key + ":" + route.RouteKind;
        }

        private static void AssertFooterElementVisible(VisualElement element)
        {
            Assert.NotNull(element);
            Assert.AreNotEqual(DisplayStyle.None, element.style.display.value);
            Assert.That(element.style.opacity.value, Is.GreaterThan(0.01f));
        }

        private static void AssertFixedDecorativeLayer(VisualElement element, string className)
        {
            Assert.NotNull(element);
            Assert.IsTrue(element.ClassListContains(className));
            Assert.AreEqual(PickingMode.Ignore, element.pickingMode);
            Assert.AreEqual(Position.Absolute, element.style.position.value);
            Assert.AreEqual(0f, element.style.left.value.value);
            Assert.AreEqual(0f, element.style.right.value.value);
            Assert.AreEqual(0f, element.style.top.value.value);
            Assert.AreEqual(0f, element.style.bottom.value.value);
        }

        private static void AssertRectsEqual(Rect expected, Rect actual, float tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.width, Is.EqualTo(expected.width).Within(tolerance));
            Assert.That(actual.height, Is.EqualTo(expected.height).Within(tolerance));
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(
                rect.x - amount,
                rect.y - amount,
                rect.width + amount * 2f,
                rect.height + amount * 2f);
        }

        private static List<VisualElement> FindByClass(VisualElement root, string className)
        {
            List<VisualElement> matches = new List<VisualElement>();
            CollectByClass(root, className, matches);
            return matches;
        }

        private static VisualElement FindGraphNode(VisualElement root, string packageId)
        {
            return FindByClass(root, "dpi-graph-node")
                .Single(node => string.Equals(node.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static VisualElement FindGraphGroup(VisualElement root, string groupId)
        {
            return FindByClass(root, "dpi-graph-group")
                .Single(group => string.Equals(group.name, "group-" + groupId, StringComparison.OrdinalIgnoreCase));
        }

        private static PackageGraphCanvas GetCanvas(VisualElement root)
        {
            if (root is PackageGraphCanvas canvas)
            {
                return canvas;
            }

            foreach (VisualElement child in root.Children())
            {
                PackageGraphCanvas childCanvas = GetCanvas(child);

                if (childCanvas != null)
                {
                    return childCanvas;
                }
            }

            return null;
        }

        private static bool HasGraphNode(VisualElement root, string packageId)
        {
            return FindByClass(root, "dpi-graph-node")
                .Any(node => string.Equals(node.name, packageId, StringComparison.OrdinalIgnoreCase));
        }

        private static Button FindGraphNodeAction(VisualElement root, string packageId)
        {
            return FindByClass(FindGraphNode(root, packageId), "dpi-graph-node__action")
                .OfType<Button>()
                .Single();
        }

        private static void CollectByClass(VisualElement element, string className, ICollection<VisualElement> matches)
        {
            if (element == null)
            {
                return;
            }

            if (element.ClassListContains(className))
            {
                matches.Add(element);
            }

            foreach (VisualElement child in element.Children())
            {
                CollectByClass(child, className, matches);
            }
        }

        private static string CreateTempProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "dpi-state-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        private static void DeleteTempProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return;
            }

            Directory.Delete(projectRoot, true);
        }

    }
}
