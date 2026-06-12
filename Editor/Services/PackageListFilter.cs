using System;
using System.Collections.Generic;
using System.Linq;

namespace JorisHoef.PackageInstaller.Editor
{
    internal static class PackageListFilter
    {
        public static IReadOnlyList<PackageCategoryListView> CreateCategoryViews(
            IEnumerable<PackageDefinition> packages,
            IEnumerable<string> orderedCategories,
            PackageListFilterOptions options,
            Func<PackageDefinition, bool> isInstalled,
            Func<string, bool> isCategoryExpanded)
        {
            PackageDefinition[] packageArray = packages != null
                ? packages.Where(package => package != null).ToArray()
                : Array.Empty<PackageDefinition>();
            string[] categories = GetCategories(packageArray, orderedCategories);
            PackageListFilterOptions activeOptions = options ?? new PackageListFilterOptions(string.Empty, true, true);
            Func<PackageDefinition, bool> installedPredicate = isInstalled ?? (_ => false);
            Func<string, bool> expandedPredicate = isCategoryExpanded ?? (_ => true);
            List<PackageCategoryListView> views = new List<PackageCategoryListView>();

            foreach (string category in categories)
            {
                PackageDefinition[] categoryPackages = packageArray
                    .Where(package => string.Equals(package.Category, category, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (categoryPackages.Length == 0)
                {
                    continue;
                }

                int installedCount = categoryPackages.Count(installedPredicate);
                PackageDefinition[] filteredPackages = categoryPackages
                    .Where(package => IsVisibleByInstalledFilter(package, activeOptions, installedPredicate))
                    .Where(package => MatchesSearch(package, activeOptions.SearchText))
                    .ToArray();
                bool isExpanded = expandedPredicate(category);

                views.Add(new PackageCategoryListView(
                    category,
                    categoryPackages.Length,
                    installedCount,
                    filteredPackages.Length,
                    isExpanded,
                    filteredPackages,
                    isExpanded ? filteredPackages : Array.Empty<PackageDefinition>()));
            }

            return views;
        }

        private static string[] GetCategories(
            IReadOnlyList<PackageDefinition> packages,
            IEnumerable<string> orderedCategories)
        {
            string[] packageCategories = packages
                .Select(package => package.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] explicitCategories = orderedCategories != null
                ? orderedCategories
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Select(category => category.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            if (explicitCategories.Length > 0)
            {
                return explicitCategories
                    .Concat(packageCategories.Where(category =>
                        !explicitCategories.Contains(category, StringComparer.OrdinalIgnoreCase)))
                    .ToArray();
            }

            return packageCategories;
        }

        private static bool IsVisibleByInstalledFilter(
            PackageDefinition packageDefinition,
            PackageListFilterOptions options,
            Func<PackageDefinition, bool> isInstalled)
        {
            bool installed = isInstalled(packageDefinition);
            return installed ? options.ShowInstalled : options.ShowNotInstalled;
        }

        private static bool MatchesSearch(PackageDefinition packageDefinition, string searchText)
        {
            if (packageDefinition == null || string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            string[] tokens = searchText
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 0)
            {
                return true;
            }

            string searchableText = string.Join(
                "\n",
                new[]
                {
                    packageDefinition.DisplayName,
                    packageDefinition.PackageId,
                    packageDefinition.Category,
                    packageDefinition.Description,
                    packageDefinition.DisplayVersion,
                    packageDefinition.StableUrl,
                    packageDefinition.DevelopmentUrl,
                    string.Join(" ", packageDefinition.Dependencies.ToArray())
                });

            return tokens.All(token =>
                searchableText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
