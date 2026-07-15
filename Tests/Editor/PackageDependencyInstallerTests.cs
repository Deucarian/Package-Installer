using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageDependencyInstallerTests
    {
        [Test]
        public void DependencyInstallOrderingInstallsEditorBeforeLogging()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                CollectionAssert.AreEqual(
                    new[] { fixture.Editor.PackageId, fixture.Logging.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
                Assert.AreEqual(PackageChannel.Stable, plan.GetChannel(fixture.Editor));
                PackageDependencyInstallStep editorStep = plan.Steps.Single(step =>
                    step.PackageDefinition.PackageId == fixture.Editor.PackageId);
                PackageDependencyInstallStep loggingStep = plan.Steps.Single(step =>
                    step.PackageDefinition.PackageId == fixture.Logging.PackageId);
                Assert.AreEqual(fixture.Editor.StableUrl, editorStep.TargetUrl);
                CollectionAssert.AreEqual(
                    new[] { fixture.Editor.PackageId },
                    loggingStep.PrerequisitePackageIds);
                CollectionAssert.AreEqual(
                    new[] { fixture.Logging.PackageId },
                    editorStep.RootPackageIds);
                CollectionAssert.AreEqual(
                    new[] { "Deucarian Logging -> Deucarian Editor" },
                    editorStep.RootPaths);
                StringAssert.Contains(
                    "Installing dependency Deucarian Editor before Deucarian Logging.",
                    JoinMessages(plan));
            }
        }

        [Test]
        public void MissingDependencyFailsClearly()
        {
            PackageDefinition logging = CreatePackage(
                "Deucarian Logging",
                "com.deucarian.logging",
                "Logging package.",
                new[] { "com.deucarian.editor" });

            using (PlanFixture fixture = new PlanFixture(logging))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { logging },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsFalse(plan.IsValid);
                StringAssert.Contains(
                    "Cannot install Deucarian Logging because dependency com.deucarian.editor is unavailable.",
                    plan.ErrorMessage);
            }
        }

        [Test]
        public void PlannerFailureIsSurfacedInChronologicalActivity()
        {
            PackageInstallerActivityService.ClearForTests();
            PackageDefinition package = CreatePackage(
                "Missing Dependency Root",
                "com.deucarian.missing-root",
                "Missing dependency test.",
                new[] { "com.deucarian.not-registered" });

            try
            {
                using (PlanFixture fixture = new PlanFixture(package))
                {
                    LogAssert.Expect(
                        LogType.Error,
                        "[PackageInstaller.Install] Cannot install Missing Dependency Root because dependency com.deucarian.not-registered is unavailable.");
                    fixture.Installer.InstallWithDependencies(
                        package,
                        _ => PackageChannel.Stable);

                    Assert.IsNotNull(PackageInstallerActivityService.Latest);
                    Assert.AreEqual("Planner", PackageInstallerActivityService.Latest.Source);
                    Assert.AreEqual(
                        PackageInstallerActivitySeverity.Error,
                        PackageInstallerActivityService.Latest.Severity);
                    StringAssert.Contains(
                        "com.deucarian.not-registered",
                        PackageInstallerActivityService.Latest.Summary);
                }
            }
            finally
            {
                PackageInstallerActivityService.ClearForTests();
            }
        }

        [Test]
        public void RegistryFingerprintChangesWhenDependencyEdgesChange()
        {
            PackageDefinition withoutDependency = CreatePackage(
                "Root",
                "com.deucarian.root",
                "Root package.");
            PackageDefinition withDependency = CreatePackage(
                "Root",
                "com.deucarian.root",
                "Root package.",
                new[] { "com.deucarian.dependency" },
                stableUrl: withoutDependency.StableUrl);

            Assert.AreNotEqual(
                PackageDependencyInstaller.ComputeRegistryFingerprintForTests(
                    new[] { withoutDependency }),
                PackageDependencyInstaller.ComputeRegistryFingerprintForTests(
                    new[] { withDependency }));
        }

        [Test]
        public void OperationPlanAndStepCollectionsAreImmutableSnapshots()
        {
            PackageDefinition package = CreatePackage(
                "Immutable Root",
                "com.deucarian.immutable-root",
                "Immutable operation model test.");
            string[] prerequisiteIds = { "com.deucarian.prerequisite" };
            string[] rootIds = { package.PackageId };
            string[] rootPaths = { package.DisplayName };
            PackageDependencyInstallStep step = new PackageDependencyInstallStep(
                package,
                PackageChannel.Stable,
                isDependency: false,
                prerequisitePackageIds: prerequisiteIds,
                rootPackageIds: rootIds,
                rootPaths: rootPaths);
            PackageDependencyInstallStep[] steps = { step };
            string[] messages = { "Original message" };
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                steps,
                messages);

            prerequisiteIds[0] = "com.deucarian.changed";
            rootIds[0] = "com.deucarian.changed-root";
            rootPaths[0] = "Changed Root";
            steps[0] = null;
            messages[0] = "Changed message";

            CollectionAssert.AreEqual(
                new[] { "com.deucarian.prerequisite" },
                step.PrerequisitePackageIds);
            CollectionAssert.AreEqual(new[] { package.PackageId }, step.RootPackageIds);
            CollectionAssert.AreEqual(new[] { package.DisplayName }, step.RootPaths);
            Assert.AreSame(step, plan.Steps.Single());
            CollectionAssert.AreEqual(new[] { "Original message" }, plan.Messages);
            Assert.IsFalse(step.PrerequisitePackageIds is string[]);
            Assert.IsFalse(plan.Steps is PackageDependencyInstallStep[]);
            Assert.IsFalse(plan.Messages is string[]);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)step.PrerequisitePackageIds)[0] = "mutated");
            Assert.Throws<NotSupportedException>(() =>
                ((IList<PackageDependencyInstallStep>)plan.Steps)[0] = null);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)plan.Messages)[0] = "mutated");
        }

        [Test]
        public void AlreadyInstalledDependencyIsSkipped()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                fixture.DetectionService.ReplaceInstalledPackageNamesForTests(
                    new[] { fixture.Editor.PackageId });

                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                CollectionAssert.AreEqual(
                    new[] { fixture.Logging.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
                StringAssert.Contains(
                    "Skipped dependency Deucarian Editor; already installed.",
                    JoinMessages(plan));
            }
        }

        [Test]
        public void DuplicateDependencyIsQueuedOnce()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging, fixture.Theming },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.AreEqual(
                    1,
                    plan.Packages.Count(package => package.PackageId == fixture.Editor.PackageId));
                CollectionAssert.AreEqual(
                    new[] { fixture.Editor.PackageId, fixture.Logging.PackageId, fixture.Theming.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
            }
        }

        [Test]
        public void SharedDependencyRetainsEveryRootPath()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging, fixture.Theming },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                PackageDependencyInstallStep editorStep = plan.Steps.Single(step =>
                    step.PackageDefinition.PackageId == fixture.Editor.PackageId);
                CollectionAssert.AreEquivalent(
                    new[] { fixture.Logging.PackageId, fixture.Theming.PackageId },
                    editorStep.RootPackageIds);
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "Deucarian Logging -> Deucarian Editor",
                        "Deucarian Theming -> Deucarian Editor"
                    },
                    editorStep.RootPaths);
                Assert.IsFalse(string.IsNullOrWhiteSpace(plan.RegistryFingerprint));
            }
        }

        [Test]
        public void SharedDependencyWithDifferentExactTargetsFailsWithBothRootPaths()
        {
            PackageDefinition shared = CreatePackage(
                "Shared",
                "com.deucarian.shared",
                "Shared dependency.",
                developmentUrl: "https://github.com/Deucarian/Shared.git#develop");
            PackageDefinition stableRoot = CreatePackage(
                "Stable Root",
                "com.deucarian.stable-root",
                "Stable root.",
                new[] { shared.PackageId });
            PackageDefinition developmentRoot = CreatePackage(
                "Development Root",
                "com.deucarian.development-root",
                "Development root.",
                new[] { shared.PackageId },
                developmentUrl: "https://github.com/Deucarian/Development-Root.git#develop");

            using (PlanFixture fixture = new PlanFixture(shared, stableRoot, developmentRoot))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { stableRoot, developmentRoot },
                    package => package.PackageId == developmentRoot.PackageId
                        ? PackageChannel.Development
                        : PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsFalse(plan.IsValid);
                Assert.IsTrue(plan.HasConflict);
                Assert.IsTrue(plan.RequiresPreflight);
                StringAssert.Contains("Conflicting package targets for Shared", plan.ErrorMessage);
                StringAssert.Contains("Stable Root -> Shared", plan.ErrorMessage);
                StringAssert.Contains("Development Root -> Shared", plan.ErrorMessage);
                StringAssert.Contains("#main", plan.ErrorMessage);
                StringAssert.Contains("#develop", plan.ErrorMessage);
            }
        }

        [Test]
        public void SharedDependencyConflictIncludesEveryPriorRootPath()
        {
            PackageDefinition shared = CreatePackage(
                "Shared",
                "com.deucarian.shared",
                "Shared dependency.",
                developmentUrl: "https://github.com/Deucarian/Shared.git#develop");
            PackageDefinition firstStableRoot = CreatePackage(
                "First Stable Root",
                "com.deucarian.first-stable-root",
                "First stable root.",
                new[] { shared.PackageId });
            PackageDefinition secondStableRoot = CreatePackage(
                "Second Stable Root",
                "com.deucarian.second-stable-root",
                "Second stable root.",
                new[] { shared.PackageId });
            PackageDefinition developmentRoot = CreatePackage(
                "Development Root",
                "com.deucarian.development-root",
                "Development root.",
                new[] { shared.PackageId },
                developmentUrl: "https://github.com/Deucarian/Development-Root.git#develop");

            using (PlanFixture fixture = new PlanFixture(
                       shared,
                       firstStableRoot,
                       secondStableRoot,
                       developmentRoot))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { firstStableRoot, secondStableRoot, developmentRoot },
                    package => package.PackageId == developmentRoot.PackageId
                        ? PackageChannel.Development
                        : PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsFalse(plan.IsValid);
                Assert.IsTrue(plan.HasConflict);
                StringAssert.Contains("First Stable Root -> Shared", plan.ErrorMessage);
                StringAssert.Contains("Second Stable Root -> Shared", plan.ErrorMessage);
                StringAssert.Contains("Development Root -> Shared", plan.ErrorMessage);
            }
        }

        [Test]
        public void AlreadyCorrectSharedDependencyDoesNotCreateFalseTargetConflict()
        {
            PackageDefinition shared = CreatePackage(
                "Shared",
                "com.deucarian.shared",
                "Shared dependency.",
                developmentUrl: "https://github.com/Deucarian/Shared.git#develop");
            PackageDefinition stableRoot = CreatePackage(
                "Stable Root",
                "com.deucarian.stable-root",
                "Stable root.",
                new[] { shared.PackageId });
            PackageDefinition developmentRoot = CreatePackage(
                "Development Root",
                "com.deucarian.development-root",
                "Development root.",
                new[] { shared.PackageId },
                developmentUrl: "https://github.com/Deucarian/Development-Root.git#develop");

            using (PlanFixture fixture = new PlanFixture(shared, stableRoot, developmentRoot))
            {
                fixture.DetectionService.ReplaceInstalledPackageForTests(
                    shared.PackageId,
                    shared.DevelopmentUrl,
                    PackageInstallSourceType.Git);

                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { stableRoot, developmentRoot },
                    package => package.PackageId == developmentRoot.PackageId
                        ? PackageChannel.Development
                        : PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.IsFalse(plan.HasConflict);
                Assert.IsFalse(plan.Steps.Any(step =>
                    step.PackageDefinition.PackageId == shared.PackageId));
            }
        }

        [Test]
        public void InvalidConflictUsesContextualReviewBeforePlannerFailure()
        {
            PackageDefinition shared = CreatePackage(
                "Shared",
                "com.deucarian.shared",
                "Shared dependency.",
                developmentUrl: "https://github.com/Deucarian/Shared.git#develop");
            PackageDefinition stableRoot = CreatePackage(
                "Stable Root",
                "com.deucarian.stable-root",
                "Stable root.",
                new[] { shared.PackageId });
            PackageDefinition developmentRoot = CreatePackage(
                "Development Root",
                "com.deucarian.development-root",
                "Development root.",
                new[] { shared.PackageId },
                developmentUrl: "https://github.com/Deucarian/Development-Root.git#develop");

            PackageInstallerActivityService.ClearForTests();
            try
            {
                using (PlanFixture fixture = new PlanFixture(shared, stableRoot, developmentRoot))
                {
                    Func<PackageDefinition, PackageChannel> channelSelector = package =>
                        package.PackageId == developmentRoot.PackageId
                            ? PackageChannel.Development
                            : PackageChannel.Stable;
                    PackageDependencyInstallPlan preview = fixture.Installer.CreateInstallPlan(
                        new[] { stableRoot, developmentRoot },
                        channelSelector,
                        includeInstalledRequestedPackages: false);
                    PackageDependencyInstallPlan reviewedPlan = null;
                    fixture.Installer.PreflightConfirmation = (plan, operationName) =>
                    {
                        reviewedPlan = plan;
                        Assert.AreEqual("Install conflicting roots", operationName);
                        return false;
                    };

                    LogAssert.Expect(
                        LogType.Error,
                        "[PackageInstaller.Install] " + preview.ErrorMessage);
                    fixture.Installer.InstallManyWithDependencies(
                        new[] { stableRoot, developmentRoot },
                        channelSelector,
                        "Install conflicting roots");

                    Assert.AreEqual(preview.ErrorMessage, reviewedPlan?.ErrorMessage);
                    Assert.IsNotNull(reviewedPlan);
                    Assert.IsFalse(reviewedPlan.IsValid);
                    Assert.IsTrue(reviewedPlan.HasConflict);
                }
            }
            finally
            {
                PackageInstallerActivityService.ClearForTests();
            }
        }

        [Test]
        public void CircularDependencyFailsClearly()
        {
            PackageDefinition alpha = CreatePackage(
                "Alpha",
                "com.deucarian.alpha",
                "Alpha package.",
                new[] { "com.deucarian.beta" });
            PackageDefinition beta = CreatePackage(
                "Beta",
                "com.deucarian.beta",
                "Beta package.",
                new[] { "com.deucarian.alpha" });

            using (PlanFixture fixture = new PlanFixture(alpha, beta))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { alpha },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsFalse(plan.IsValid);
                StringAssert.Contains("Circular dependency detected: Alpha -> Beta -> Alpha.", plan.ErrorMessage);
            }
        }

        [Test]
        public void UpdateAllKeepsDependencyFirstOrderingForInstalledRoots()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                fixture.DetectionService.ReplaceInstalledPackageNamesForTests(
                    new[] { fixture.Logging.PackageId, fixture.Theming.PackageId });

                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging, fixture.Theming },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: true);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                CollectionAssert.AreEqual(
                    new[] { fixture.Editor.PackageId, fixture.Logging.PackageId, fixture.Theming.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
            }
        }

        [Test]
        public void DependencyFallsBackToStableWhenRequestedDevelopmentChannelIsUnavailable()
        {
            using (PlanFixture fixture = PlanFixture.CreateDefault())
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { fixture.Logging },
                    _ => PackageChannel.Development,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.AreEqual(PackageChannel.Stable, plan.GetChannel(fixture.Editor));
                Assert.AreEqual(PackageChannel.Development, plan.GetChannel(fixture.Logging));
                PackageDependencyInstallStep editorStep = plan.Steps.Single(step =>
                    step.PackageDefinition.PackageId == fixture.Editor.PackageId);
                Assert.AreEqual(PackageChannel.Development, editorStep.RequestedChannel);
                Assert.AreEqual(PackageChannel.Stable, editorStep.Channel);
                Assert.IsTrue(plan.HasChannelFallback);
                Assert.IsTrue(plan.RequiresPreflight);
                StringAssert.Contains(
                    "Dependency Deucarian Editor has no Development channel; falling back to Stable before installing Deucarian Logging.",
                    JoinMessages(plan));
            }
        }

        [Test]
        public void SingleSafeInstallDoesNotRequirePreflight()
        {
            PackageDefinition standalone = CreatePackage(
                "Standalone",
                "com.deucarian.standalone",
                "Standalone package.");

            using (PlanFixture fixture = new PlanFixture(standalone))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { standalone },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.IsFalse(plan.RequiresPreflight);
                Assert.IsFalse(plan.IsMultiStep);
            }
        }

        [Test]
        public void MultipleRootsRequirePreflightEvenWhenOnlyOneSharedDependencyIsPending()
        {
            PackageDefinition shared = CreatePackage(
                "Shared Dependency",
                "com.deucarian.shared-dependency",
                "Shared dependency.");
            PackageDefinition firstRoot = CreatePackage(
                "First Root",
                "com.deucarian.first-root",
                "First root.",
                new[] { shared.PackageId });
            PackageDefinition secondRoot = CreatePackage(
                "Second Root",
                "com.deucarian.second-root",
                "Second root.",
                new[] { shared.PackageId });

            using (PlanFixture fixture = new PlanFixture(shared, firstRoot, secondRoot))
            {
                fixture.DetectionService.ReplaceInstalledPackageNamesForTests(
                    new[] { firstRoot.PackageId, secondRoot.PackageId });

                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { firstRoot, secondRoot },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.AreEqual(1, plan.Steps.Count);
                Assert.AreEqual(2, plan.RootRequests.Count);
                Assert.IsFalse(plan.IsMultiStep);
                Assert.IsTrue(plan.IsBulk);
                Assert.IsTrue(plan.RequiresPreflight);
                CollectionAssert.AreEquivalent(
                    new[] { firstRoot.PackageId, secondRoot.PackageId },
                    plan.Steps.Single().RootPackageIds);
            }
        }

        [Test]
        public void DevelopmentToStableTargetIsMarkedAsDowngradeRisk()
        {
            PackageDefinition package = CreatePackage(
                "Channel Package",
                "com.deucarian.channel-package",
                "Channel package.",
                developmentUrl: "https://github.com/Deucarian/Channel-Package.git#develop");

            using (PlanFixture fixture = new PlanFixture(package))
            {
                fixture.DetectionService.ReplaceInstalledPackageForTests(
                    package.PackageId,
                    package.DevelopmentUrl,
                    PackageInstallSourceType.Git);
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { package },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: true);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                Assert.IsTrue(plan.HasDowngradeRisk);
                Assert.IsTrue(plan.RequiresPreflight);
            }
        }

        [Test]
        public void OptionalCompanionsAreNotInstalledAsDependencies()
        {
            PackageDefinition diagnostics = CreatePackage(
                "Deucarian Diagnostics",
                "com.deucarian.diagnostics",
                "Diagnostics package.");
            PackageDefinition objectLoading = CreatePackage(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                "Object loading package.",
                optionalCompanions: new[] { diagnostics.PackageId });

            using (PlanFixture fixture = new PlanFixture(objectLoading, diagnostics))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { objectLoading },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                CollectionAssert.AreEqual(
                    new[] { objectLoading.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
            }
        }

        [Test]
        public void CommonIsInstalledBeforeRuntimeConsumers()
        {
            PackageDefinition common = CreatePackage(
                "Deucarian Common",
                "com.deucarian.common",
                "Common package.");
            PackageDefinition uiBinding = CreatePackage(
                "Deucarian UI Binding",
                "com.deucarian.ui-binding",
                "UI Binding package.",
                new[] { common.PackageId });

            using (PlanFixture fixture = new PlanFixture(common, uiBinding))
            {
                PackageDependencyInstallPlan plan = fixture.Installer.CreateInstallPlan(
                    new[] { uiBinding },
                    _ => PackageChannel.Stable,
                    includeInstalledRequestedPackages: false);

                Assert.IsTrue(plan.IsValid, plan.ErrorMessage);
                CollectionAssert.AreEqual(
                    new[] { common.PackageId, uiBinding.PackageId },
                    plan.Packages.Select(package => package.PackageId).ToArray());
            }
        }

        [Test]
        public void InstallServiceCancelPendingOperationSkipsQueuedPackages()
        {
            PackageDefinition editor = CreatePackage(
                "Deucarian Editor",
                "com.deucarian.editor",
                "Shared editor tooling.");
            PackageDefinition logging = CreatePackage(
                "Deucarian Logging",
                "com.deucarian.logging",
                "Logging package.");

            using (PackageInstallService installService = new PackageInstallService())
            {
                bool queueCompleted = false;
                installService.QueueCompleted += () => queueCompleted = true;
                installService.QueuePendingOperationForTests(
                    "Install All Packages",
                    new[] { editor, logging });

                Assert.IsTrue(installService.IsBusy);
                Assert.IsTrue(installService.CancelCurrentOperation());

                Assert.IsFalse(installService.IsBusy);
                Assert.IsFalse(installService.IsCancelRequested);
                Assert.IsTrue(queueCompleted);
                Assert.AreEqual(2, installService.CompletedSteps);
                Assert.AreEqual(0, installService.SkippedSteps);
                Assert.AreEqual(2, installService.CanceledSteps);
                Assert.AreEqual("Install All Packages canceled with 2 canceled.", installService.LastStatusMessage);
                Assert.IsTrue(installService.ProgressItems.All(
                    item => item.State == PackageInstallProgressItemState.Canceled));
            }
        }

        private static string JoinMessages(PackageDependencyInstallPlan plan)
        {
            return string.Join("\n", plan.Messages.ToArray());
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string description,
            string[] dependencies = null,
            string[] optionalCompanions = null,
            string stableUrl = null,
            string developmentUrl = null)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                stableUrl ?? "https://github.com/Deucarian/" + displayName.Replace("Deucarian ", string.Empty).Replace(" ", "-") + ".git#main",
                description,
                dependencies ?? Array.Empty<string>(),
                PackageType.Core,
                developmentUrl,
                optionalCompanions: optionalCompanions,
                category: "Core");
        }

        private sealed class PlanFixture : IDisposable
        {
            private readonly PackageInstallService _installService;

            public PlanFixture(params PackageDefinition[] packages)
            {
                Packages = packages;
                _installService = new PackageInstallService();
                DetectionService = new PackageDetectionService();
                Installer = new PackageDependencyInstaller(
                    _installService,
                    DetectionService,
                    () => Packages);
            }

            public PackageDefinition[] Packages { get; }

            public PackageDefinition Editor { get; private set; }

            public PackageDefinition Logging { get; private set; }

            public PackageDefinition Theming { get; private set; }

            public PackageDetectionService DetectionService { get; }

            public PackageDependencyInstaller Installer { get; }

            public static PlanFixture CreateDefault()
            {
                PackageDefinition editor = CreatePackage(
                    "Deucarian Editor",
                    "com.deucarian.editor",
                    "Shared editor tooling.",
                    developmentUrl: string.Empty);
                PackageDefinition logging = CreatePackage(
                    "Deucarian Logging",
                    "com.deucarian.logging",
                    "Logging package.",
                    new[] { editor.PackageId },
                    developmentUrl: "https://github.com/Deucarian/Logging.git#develop");
                PackageDefinition theming = CreatePackage(
                    "Deucarian Theming",
                    "com.deucarian.theming",
                    "Theming package.",
                    new[] { editor.PackageId },
                    developmentUrl: "https://github.com/Deucarian/Theming.git#develop");
                PlanFixture fixture = new PlanFixture(editor, logging, theming)
                {
                    Editor = editor,
                    Logging = logging,
                    Theming = theming
                };

                return fixture;
            }

            public void Dispose()
            {
                DetectionService.Dispose();
                _installService.Dispose();
            }
        }
    }
}
