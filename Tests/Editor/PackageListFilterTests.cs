using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
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
        public void DefaultVisibilityShowsInstalledAndNotInstalledPackages()
        {
            PackageDefinition installedPackage = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition notInstalledPackage = CreatePackage("Generic UI", "com.example.generic-ui", "UI");
            PackageVisibilityFilterState state = new PackageVisibilityFilterState();

            PackageDefinition[] visiblePackages = GetVisiblePackages(
                new[] { installedPackage, notInstalledPackage },
                state,
                installedPackage.PackageId);

            CollectionAssert.AreEqual(new[] { installedPackage, notInstalledPackage }, visiblePackages);
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
        public void DisabledVisibilityTogglesShowNoPackages()
        {
            PackageDefinition installedPackage = CreatePackage("Core State", "com.example.core-state", "Core");
            PackageDefinition notInstalledPackage = CreatePackage("Generic UI", "com.example.generic-ui", "UI");
            PackageVisibilityFilterState state = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: false,
                showNotInstalled: false);

            PackageDefinition[] visiblePackages = GetVisiblePackages(
                new[] { installedPackage, notInstalledPackage },
                state,
                installedPackage.PackageId);

            Assert.IsEmpty(visiblePackages);
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

        [Test]
        public void SearchMatchesPackageIdCategoryAndType()
        {
            PackageDefinition integration = CreatePackage(
                "Session API",
                "com.example.session-api-integration",
                "Integration",
                "Integration");
            PackageDefinition tools = CreatePackage("Diagnostics", "com.example.diagnostics", "Tools");

            Assert.IsTrue(PackageVisibilityFilter.MatchesSearch(integration, "session-api"));
            Assert.IsTrue(PackageVisibilityFilter.MatchesSearch(tools, "tools"));
            Assert.IsTrue(PackageVisibilityFilter.MatchesSearch(integration, "integration"));
        }

        [Test]
        public void ClearFiltersRestoresDefaultState()
        {
            PackageVisibilityFilterState state = new PackageVisibilityFilterState(
                "logging",
                showInstalled: false,
                showNotInstalled: true);

            Assert.IsTrue(state.Reset());

            Assert.IsTrue(state.ShowInstalled);
            Assert.IsTrue(state.ShowNotInstalled);
            Assert.IsEmpty(state.SearchText);
            Assert.IsTrue(state.IsDefault);
        }

        [Test]
        public void SharedStateFiltersListAndGraphConsistently()
        {
            PackageDefinition logging = CreatePackage("Logging", "com.example.logging", "Core");
            PackageDefinition diagnostics = CreatePackage("Diagnostics", "com.example.diagnostics", "Tools");
            PackageVisibilityFilterState state = new PackageVisibilityFilterState(
                "logging",
                showInstalled: true,
                showNotInstalled: false);
            PackageDefinition[] packages = { logging, diagnostics };
            PackageGraphModel graph = new PackageGraphBuilder(packageId => packageId == logging.PackageId)
                .Build(packages);

            PackageDefinition[] listPackages = GetVisiblePackages(
                packages,
                state,
                logging.PackageId);
            HashSet<string> graphPackageIds = PackageVisibilityFilter.CreateVisiblePackageIdSet(graph, state);

            CollectionAssert.AreEqual(new[] { logging }, listPackages);
            CollectionAssert.AreEquivalent(new[] { logging.PackageId }, graphPackageIds);
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

        private static PackageDefinition[] GetVisiblePackages(
            PackageDefinition[] packages,
            PackageVisibilityFilterState state,
            params string[] installedPackageIds)
        {
            return PackageListFilter
                .CreateCategoryViews(
                    packages,
                    packages.Select(package => package.Category).Distinct(StringComparer.OrdinalIgnoreCase),
                    state,
                    package => installedPackageIds.Contains(package.PackageId),
                    _ => true)
                .SelectMany(view => view.VisiblePackages)
                .ToArray();
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category,
            string metadataType = null)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://example.com/" + packageId + ".git#main",
                displayName + " package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://example.com/" + packageId + ".git#develop",
                category: category,
                metadataType: metadataType);
        }
    }
}
