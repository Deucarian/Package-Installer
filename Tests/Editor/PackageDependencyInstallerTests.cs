using System;
using System.Linq;
using NUnit.Framework;

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
                StringAssert.Contains(
                    "Dependency Deucarian Editor has no Development channel; falling back to Stable before installing Deucarian Logging.",
                    JoinMessages(plan));
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
