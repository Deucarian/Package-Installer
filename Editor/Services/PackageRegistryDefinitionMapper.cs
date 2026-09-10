using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryDefinitionMapper
    {
        internal static IReadOnlyList<PackageDefinition> CreatePackageDefinitions(PackageRegistry registry)
        {
            if (registry == null || registry.packages == null)
            {
                return new[] { CreateInstallerPackageDefinition() };
            }

            IReadOnlyList<PackageGraphGroup> groups =
                PackageGraphHierarchyBuilder.CreateGroups(registry.groups);
            PackageDefinition[] packageDefinitions = registry.packages
                .Where(entry => entry != null)
                .Select(entry => CreatePackageDefinition(
                    entry,
                    groups,
                    ResolveOptionalCompanionIds(entry, registry.packages)))
                .ToArray();

            return EnsureInstallerPackageDefinition(packageDefinitions);
        }

        private static PackageDefinition CreatePackageDefinition(
            PackageRegistryEntry entry,
            IReadOnlyList<PackageGraphGroup> groups,
            IEnumerable<string> optionalCompanionIds)
        {
            string category = entry.category != null ? entry.category.Trim() : string.Empty;
            PackageKind kind = PackageKindParser.Parse(entry.kind, entry.type, category);
            string navigationGroup = PackageGraphHierarchyBuilder.GetGroupPath(
                groups,
                entry.groupId);

            return new PackageDefinition(
                entry.displayName,
                entry.id,
                entry.stableUrl,
                entry.description,
                entry.dependencies,
                kind,
                entry.developmentUrl,
                optionalCompanions: optionalCompanionIds,
                category: category,
                metadataType: entry.type,
                optionalIntegrations: entry.optionalIntegrations,
                integrationTargets: entry.integrationTargets,
                suiteMembers: entry.suiteMembers,
                recommendedWith: entry.recommendedWith,
                ecosystemGroup: entry.ecosystemGroup,
                groupId: entry.groupId,
                overviewOrder: entry.overviewOrder,
                searchAliases: entry.searchAliases,
                searchTags: entry.searchTags,
                navigationGroup: navigationGroup,
                iconKey: entry.iconKey,
                compositionPresets: CreateCompositionPresets(entry.compositionPresets));
        }

        private static IEnumerable<string> ResolveOptionalCompanionIds(
            PackageRegistryEntry target,
            IEnumerable<PackageRegistryEntry> entries)
        {
            IEnumerable<string> declared = target.optionalCompanions ?? Array.Empty<string>();
            IEnumerable<string> derived = (entries ?? Array.Empty<PackageRegistryEntry>())
                .Where(candidate => candidate != null &&
                                    (candidate.recommendedWith ?? Array.Empty<string>())
                                    .Any(targetId => string.Equals(
                                        targetId?.Trim(),
                                        target.id?.Trim(),
                                        StringComparison.OrdinalIgnoreCase)))
                .Select(candidate => candidate.id);

            return declared
                .Concat(derived)
                .Where(packageId => !string.IsNullOrWhiteSpace(packageId))
                .Select(packageId => packageId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<PackageCompositionPresetDefinition> CreateCompositionPresets(
            IEnumerable<PackageCompositionPresetEntry> entries)
        {
            return (entries ?? Array.Empty<PackageCompositionPresetEntry>())
                .Where(entry => entry != null)
                .Select(entry => new PackageCompositionPresetDefinition(
                    entry.id,
                    entry.displayName,
                    entry.description,
                    entry.packageIds,
                    entry.recommended));
        }

        private static IReadOnlyList<PackageDefinition> EnsureInstallerPackageDefinition(
            IReadOnlyList<PackageDefinition> packageDefinitions)
        {
            if (packageDefinitions.Any(package => string.Equals(
                    package.PackageId,
                    "com.deucarian.package-installer",
                    StringComparison.OrdinalIgnoreCase)))
            {
                return packageDefinitions;
            }

            return packageDefinitions
                .Concat(new[] { CreateInstallerPackageDefinition() })
                .ToArray();
        }

        private static PackageDefinition CreateInstallerPackageDefinition()
        {
            return new PackageDefinition(
                "Deucarian Package Installer",
                "com.deucarian.package-installer",
                "https://github.com/Deucarian/Package-Installer.git#main",
                "Editor installer window for installing and composing Deucarian Unity UPM packages.",
                Array.Empty<string>(),
                PackageKind.Tool,
                "https://github.com/Deucarian/Package-Installer.git#develop",
                category: "Tools",
                metadataType: "Tool",
                ecosystemGroup: "Tools & Quality",
                groupId: PackageGraphHierarchyBuilder.ToolsQualityGroupId,
                overviewOrder: 20,
                searchAliases: new[] { "installer" },
                searchTags: new[] { "package-management", "upm" },
                navigationGroup: "Tools & Quality",
                iconKey: "package-plus");
        }
    }
}
