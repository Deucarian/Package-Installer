using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageReverseDependencyResolverTests
    {
        [Test]
        public void PackageLockDependenciesAreReadFromNestedObjectOnly()
        {
            const string json = @"{
  ""dependencies"": {
    ""com.deucarian.consumer"": {
      ""version"": ""file:../Consumer"",
      ""dependencies"": {
        ""com.deucarian.target"": ""1.0.0"",
        ""com.deucarian.other"": ""2.0.0""
      }
    },
    ""com.deucarian.unrelated"": {
      ""dependencies"": {
        ""com.deucarian.other"": ""2.0.0""
      }
    }
  }
}";

            string[] dependents = PackageReverseDependencyResolver.ResolvePackageLockIdsForTests(
                    json,
                    "com.deucarian.target",
                    new[] { "com.deucarian.consumer", "com.deucarian.unrelated" })
                .ToArray();

            CollectionAssert.AreEqual(new[] { "com.deucarian.consumer" }, dependents);
        }

        [Test]
        public void RegistryFallbackReturnsOnlyDeclaredReverseDependencies()
        {
            PackageDefinition target = CreatePackage("com.deucarian.target");
            PackageDefinition direct = CreatePackage(
                "com.deucarian.direct",
                "com.deucarian.target");
            PackageDefinition unrelated = CreatePackage(
                "com.deucarian.unrelated",
                "com.deucarian.other");

            string[] dependents = PackageReverseDependencyResolver.ResolveRegistryIdsForTests(
                    target.PackageId,
                    new[] { target, direct, unrelated })
                .ToArray();

            CollectionAssert.AreEqual(new[] { direct.PackageId }, dependents);
        }

        [Test]
        public void RegistryFallbackExcludesPackagesThatAreNotInstalled()
        {
            PackageDefinition target = CreatePackage("com.deucarian.target");
            PackageDefinition installed = CreatePackage(
                "com.deucarian.installed",
                target.PackageId);
            PackageDefinition catalogOnly = CreatePackage(
                "com.deucarian.catalog-only",
                target.PackageId);

            string[] dependents = PackageReverseDependencyResolver.ResolveRegistryIdsForTests(
                    target.PackageId,
                    new[] { target, installed, catalogOnly },
                    new[] { target.PackageId, installed.PackageId })
                .ToArray();

            CollectionAssert.AreEqual(new[] { installed.PackageId }, dependents);
        }

        [Test]
        public void ResolutionUsesBestAvailableSourceForEachInstalledPackage()
        {
            const string targetId = "com.deucarian.target";
            string[] installedIds =
            {
                "com.deucarian.upm-positive",
                "com.deucarian.upm-negative",
                "com.deucarian.lock-positive",
                "com.deucarian.lock-negative",
                "com.deucarian.registry-positive"
            };
            PackageReverseDependencyPackageMetadata[] metadata =
            {
                new PackageReverseDependencyPackageMetadata(
                    "com.deucarian.upm-positive",
                    "UPM Positive",
                    hasDependencyMetadata: true,
                    new[] { targetId }),
                new PackageReverseDependencyPackageMetadata(
                    "com.deucarian.upm-negative",
                    "UPM Negative",
                    hasDependencyMetadata: true,
                    Array.Empty<string>()),
                new PackageReverseDependencyPackageMetadata(
                    "com.deucarian.lock-positive",
                    "Lock Positive",
                    hasDependencyMetadata: false,
                    Array.Empty<string>()),
                new PackageReverseDependencyPackageMetadata(
                    "com.deucarian.registry-positive",
                    "Registry Positive",
                    hasDependencyMetadata: false,
                    Array.Empty<string>())
            };
            const string packageLockJson = @"{
  ""dependencies"": {
    ""com.deucarian.upm-negative"": {
      ""dependencies"": { ""com.deucarian.target"": ""1.0.0"" }
    },
    ""com.deucarian.lock-positive"": {
      ""dependencies"": { ""com.deucarian.target"": ""1.0.0"" }
    },
    ""com.deucarian.lock-negative"": {
      ""dependencies"": {}
    }
  }
}";
            PackageDefinition[] registry = installedIds
                .Concat(new[] { "com.deucarian.catalog-only" })
                .Select(id => CreatePackage(id, targetId))
                .ToArray();

            IReadOnlyList<PackageReverseDependency> results =
                PackageReverseDependencyResolver.ResolveFromSourcesForTests(
                    targetId,
                    installedIds,
                    metadata,
                    packageLockJson,
                    registry);
            Dictionary<string, PackageReverseDependency> byId = results.ToDictionary(
                result => result.PackageId,
                StringComparer.OrdinalIgnoreCase);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "com.deucarian.upm-positive",
                    "com.deucarian.lock-positive",
                    "com.deucarian.registry-positive"
                },
                byId.Keys);
            Assert.AreEqual(
                PackageReverseDependencySource.UnityPackageManager,
                byId["com.deucarian.upm-positive"].Source);
            Assert.AreEqual(
                PackageReverseDependencySource.PackageLock,
                byId["com.deucarian.lock-positive"].Source);
            Assert.AreEqual(
                PackageReverseDependencySource.Registry,
                byId["com.deucarian.registry-positive"].Source);
            Assert.IsFalse(byId.ContainsKey("com.deucarian.upm-negative"));
            Assert.IsFalse(byId.ContainsKey("com.deucarian.lock-negative"));
            Assert.IsFalse(byId.ContainsKey("com.deucarian.catalog-only"));
        }

        [Test]
        public void InstalledDependentWarnsWithoutBlockingRemovalConfirmation()
        {
            IReadOnlyList<PackageReverseDependency> dependents = new[]
            {
                new PackageReverseDependency(
                    "com.deucarian.actual-consumer",
                    "Actual Consumer",
                    PackageReverseDependencySource.PackageLock)
            };

            Assert.IsTrue(PackageInstallerWindow.CanRemovePackageForTests(
                dependents,
                queuedOrInstalling: false,
                actionsBusy: false));
            string warning = PackageInstallerWindow.BuildRemoveDependentWarningForTests(dependents);
            StringAssert.Contains("Actual Consumer", warning);
            StringAssert.Contains("com.deucarian.actual-consumer", warning);
            StringAssert.Contains("may break", warning);
        }

        private static PackageDefinition CreatePackage(string id, params string[] dependencies)
        {
            return new PackageDefinition(
                id,
                id,
                "https://github.com/Deucarian/Test.git#main",
                "Test package.",
                dependencies);
        }
    }
}
