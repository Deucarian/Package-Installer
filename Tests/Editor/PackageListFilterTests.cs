using System;
using System.Linq;
using NUnit.Framework;

namespace JorisHoef.PackageInstaller.Editor.Tests
{
    internal sealed class PackageListFilterTests
    {
        [Test]
        public void InstalledFilterShowsOnlyInstalledPackages()
        {
            PackageDefinition installedPackage = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition notInstalledPackage = CreatePackage("Generic UI", "com.example.generic-ui", "UI");

            PackageDefinition[] visiblePackages = GetVisiblePackages(
                new[] { installedPackage, notInstalledPackage },
                new PackageListFilterOptions(string.Empty, showInstalled: true, showNotInstalled: false),
                installedPackage.PackageId);

            CollectionAssert.AreEqual(new[] { installedPackage }, visiblePackages);
        }

        [Test]
        public void NotInstalledFilterShowsOnlyNotInstalledPackages()
        {
            PackageDefinition installedPackage = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition notInstalledPackage = CreatePackage("Generic UI", "com.example.generic-ui", "UI");

            PackageDefinition[] visiblePackages = GetVisiblePackages(
                new[] { installedPackage, notInstalledPackage },
                new PackageListFilterOptions(string.Empty, showInstalled: false, showNotInstalled: true),
                installedPackage.PackageId);

            CollectionAssert.AreEqual(new[] { notInstalledPackage }, visiblePackages);
        }

        [Test]
        public void CollapsedCategoryHidesChildrenButKeepsCounts()
        {
            PackageDefinition coreState = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition coreData = CreatePackage("Core Data", "com.example.core-data", "Core");

            PackageCategoryListView coreView = PackageListFilter
                .CreateCategoryViews(
                    new[] { coreState, coreData },
                    new[] { "Core" },
                    new PackageListFilterOptions(string.Empty, showInstalled: true, showNotInstalled: true),
                    package => package.PackageId == coreState.PackageId,
                    category => false)
                .Single();

            Assert.IsFalse(coreView.IsExpanded);
            Assert.AreEqual(2, coreView.PackageCount);
            Assert.AreEqual(1, coreView.InstalledCount);
            Assert.AreEqual(2, coreView.FilteredPackageCount);
            Assert.AreEqual(0, coreView.VisiblePackages.Count);
        }

        [Test]
        public void SearchAndInstalledFiltersCombine()
        {
            PackageDefinition installedState = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition notInstalledState = CreatePackage("State Tools", "com.example.state-tools", "Tools");
            PackageDefinition installedUi = CreatePackage("Generic UI", "com.example.generic-ui", "UI");

            PackageDefinition[] visiblePackages = GetVisiblePackages(
                new[] { installedState, notInstalledState, installedUi },
                new PackageListFilterOptions("state", showInstalled: true, showNotInstalled: false),
                installedState.PackageId,
                installedUi.PackageId);

            CollectionAssert.AreEqual(new[] { installedState }, visiblePackages);
        }

        private static PackageDefinition[] GetVisiblePackages(
            PackageDefinition[] packages,
            PackageListFilterOptions options,
            params string[] installedPackageIds)
        {
            return PackageListFilter
                .CreateCategoryViews(
                    packages,
                    packages.Select(package => package.Category).Distinct(StringComparer.OrdinalIgnoreCase),
                    options,
                    package => installedPackageIds.Contains(package.PackageId),
                    _ => true)
                .SelectMany(view => view.VisiblePackages)
                .ToArray();
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                displayName + " package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/" + packageId + ".git#develop",
                category: category);
        }
    }
}
