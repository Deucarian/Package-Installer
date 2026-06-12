using System;
using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageCategoryListView
    {
        public PackageCategoryListView(
            string category,
            int packageCount,
            int installedCount,
            int filteredPackageCount,
            bool isExpanded,
            IReadOnlyList<PackageDefinition> filteredPackages,
            IReadOnlyList<PackageDefinition> visiblePackages)
        {
            Category = string.IsNullOrWhiteSpace(category) ? "Uncategorized" : category.Trim();
            PackageCount = Math.Max(0, packageCount);
            InstalledCount = Math.Max(0, installedCount);
            FilteredPackageCount = Math.Max(0, filteredPackageCount);
            IsExpanded = isExpanded;
            FilteredPackages = filteredPackages ?? Array.Empty<PackageDefinition>();
            VisiblePackages = visiblePackages ?? Array.Empty<PackageDefinition>();
        }

        public string Category { get; }

        public int PackageCount { get; }

        public int InstalledCount { get; }

        public int FilteredPackageCount { get; }

        public bool IsExpanded { get; }

        public IReadOnlyList<PackageDefinition> FilteredPackages { get; }

        public IReadOnlyList<PackageDefinition> VisiblePackages { get; }

        public bool HasFilteredPackages => FilteredPackageCount > 0;
    }
}
