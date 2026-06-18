using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphHierarchyBuilder
    {
        public const string FoundationGroupId = "foundation";
        public const string RuntimeWorldGroupId = "runtime-world";
        public const string UiExperienceGroupId = "ui-experience";
        public const string ToolsQualityGroupId = "tools-quality";
        public const string IntegrationsGroupId = "integrations";
        public const string SuitesGroupId = "suites";

        private static readonly PackageGraphGroup[] DefaultGroups =
        {
            new PackageGraphGroup(
                FoundationGroupId,
                "Foundation",
                string.Empty,
                "Base editor, logging, API, and shared state packages.",
                10,
                "editor",
                "foundation"),
            new PackageGraphGroup(
                RuntimeWorldGroupId,
                "Runtime / World",
                string.Empty,
                "Runtime services and world-facing package capabilities.",
                20,
                "object-loading",
                "runtime"),
            new PackageGraphGroup(
                UiExperienceGroupId,
                "UI / Experience",
                string.Empty,
                "UI binding, theming, and user-facing experience packages.",
                30,
                "generic-ui-items",
                "experience"),
            new PackageGraphGroup(
                ToolsQualityGroupId,
                "Tools / Quality",
                string.Empty,
                "Installer, diagnostics, and development quality tooling.",
                40,
                "package-installer",
                "tools"),
            new PackageGraphGroup(
                IntegrationsGroupId,
                "Integrations",
                string.Empty,
                "Installable packages that connect two or more systems.",
                50,
                "api-helper",
                "integration"),
            new PackageGraphGroup(
                SuitesGroupId,
                "Suites",
                string.Empty,
                "Curated installable bundle packages.",
                60,
                "selection",
                "suite")
        };

        public static IReadOnlyList<PackageGraphGroup> CreateGroups(IEnumerable<PackageGraphGroup> groups)
        {
            PackageGraphGroup[] provided = groups == null
                ? Array.Empty<PackageGraphGroup>()
                : groups
                    .Where(group => group != null && !string.IsNullOrWhiteSpace(group.Id))
                    .ToArray();

            return provided.Length == 0
                ? DefaultGroups
                : provided
                    .OrderBy(group => group.SortOrder)
                    .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public static IReadOnlyList<PackageGraphGroup> CreateGroups(PackageRegistryGroupEntry[] entries)
        {
            if (entries == null || entries.Length == 0)
            {
                return DefaultGroups;
            }

            return CreateGroups(entries.Select(entry => entry == null
                ? null
                : new PackageGraphGroup(
                    NormalizeGroupId(entry.id),
                    entry.displayName,
                    NormalizeGroupId(entry.parentGroupId),
                    entry.description,
                    entry.sortOrder,
                    entry.iconKey,
                    entry.styleKey)));
        }

        public static string ResolvePackageGroupId(
            PackageDefinition package,
            IEnumerable<PackageGraphGroup> groups)
        {
            HashSet<string> knownGroupIds = new HashSet<string>(
                CreateGroups(groups).Select(group => group.Id),
                StringComparer.OrdinalIgnoreCase);

            string explicitGroupId = NormalizeGroupId(package != null ? package.GroupId : string.Empty);

            if (!string.IsNullOrWhiteSpace(explicitGroupId) && knownGroupIds.Contains(explicitGroupId))
            {
                return explicitGroupId;
            }

            string legacyGroupId = NormalizeLegacyGroupId(package != null ? package.EcosystemGroup : string.Empty);

            if (!string.IsNullOrWhiteSpace(legacyGroupId) && knownGroupIds.Contains(legacyGroupId))
            {
                return legacyGroupId;
            }

            string inferredGroupId = InferPackageGroupId(package);

            return knownGroupIds.Contains(inferredGroupId)
                ? inferredGroupId
                : FoundationGroupId;
        }

        public static string NormalizeGroupId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            string legacy = NormalizeLegacyGroupId(trimmed);
            return string.IsNullOrWhiteSpace(legacy) ? trimmed : legacy;
        }

        public static bool ValidateGroups(
            PackageRegistryGroupEntry[] groups,
            out string message)
        {
            message = string.Empty;

            if (groups == null || groups.Length == 0)
            {
                return true;
            }

            Dictionary<string, PackageRegistryGroupEntry> byId =
                new Dictionary<string, PackageRegistryGroupEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageRegistryGroupEntry group in groups)
            {
                if (group == null)
                {
                    message = "Registry contains a null group entry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(group.id))
                {
                    message = "Group id cannot be empty.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(group.displayName))
                {
                    message = "Group displayName cannot be empty for " + group.id + ".";
                    return false;
                }

                string groupId = NormalizeGroupId(group.id);

                if (byId.ContainsKey(groupId))
                {
                    message = "Duplicate group id in registry: " + group.id + ".";
                    return false;
                }

                byId[groupId] = group;
            }

            foreach (PackageRegistryGroupEntry group in groups)
            {
                string parentGroupId = NormalizeGroupId(group.parentGroupId);

                if (!string.IsNullOrWhiteSpace(parentGroupId) && !byId.ContainsKey(parentGroupId))
                {
                    message = "Group " + group.id + " references unknown parentGroupId " + group.parentGroupId + ".";
                    return false;
                }
            }

            foreach (PackageRegistryGroupEntry group in groups)
            {
                if (HasCycle(NormalizeGroupId(group.id), byId))
                {
                    message = "Group parent hierarchy contains a cycle at " + group.id + ".";
                    return false;
                }
            }

            return true;
        }

        private static bool HasCycle(
            string startGroupId,
            IReadOnlyDictionary<string, PackageRegistryGroupEntry> groupsById)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = startGroupId;

            while (!string.IsNullOrWhiteSpace(currentGroupId))
            {
                if (!visited.Add(currentGroupId))
                {
                    return true;
                }

                if (!groupsById.TryGetValue(currentGroupId, out PackageRegistryGroupEntry group))
                {
                    return false;
                }

                currentGroupId = NormalizeGroupId(group.parentGroupId);
            }

            return false;
        }

        private static string InferPackageGroupId(PackageDefinition package)
        {
            if (package == null)
            {
                return FoundationGroupId;
            }

            if (package.IsIntegration)
            {
                return IntegrationsGroupId;
            }

            if (package.IsSuite)
            {
                return SuitesGroupId;
            }

            switch ((package.PackageId ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "com.deucarian.editor":
                case "com.deucarian.logging":
                case "com.deucarian.api":
                case "com.deucarian.core-state":
                    return FoundationGroupId;
                case "com.deucarian.session":
                case "com.deucarian.object-loading":
                case "com.deucarian.object-selection":
                    return RuntimeWorldGroupId;
                case "com.deucarian.ui-binding":
                case "com.deucarian.theming":
                    return UiExperienceGroupId;
                case "com.deucarian.package-installer":
                case "com.deucarian.diagnostics":
                    return ToolsQualityGroupId;
            }

            if (string.Equals(package.Category, "Integration", StringComparison.OrdinalIgnoreCase))
            {
                return IntegrationsGroupId;
            }

            if (string.Equals(package.Category, "Suites", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Suite", StringComparison.OrdinalIgnoreCase))
            {
                return SuitesGroupId;
            }

            if (string.Equals(package.Category, "Tools", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Editor", StringComparison.OrdinalIgnoreCase))
            {
                return ToolsQualityGroupId;
            }

            if (string.Equals(package.Category, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return UiExperienceGroupId;
            }

            if (string.Equals(package.Category, "World", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Runtime", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Services", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeWorldGroupId;
            }

            return FoundationGroupId;
        }

        private static string NormalizeLegacyGroupId(string value)
        {
            string token = NormalizeToken(value);

            switch (token)
            {
                case "foundation":
                case "core":
                    return FoundationGroupId;
                case "servicesruntime":
                case "services":
                case "runtime":
                case "runtimeworld":
                case "world":
                    return RuntimeWorldGroupId;
                case "experienceuiworld":
                case "uiexperience":
                case "experience":
                case "ui":
                    return UiExperienceGroupId;
                case "toolsquality":
                case "tools":
                case "quality":
                    return ToolsQualityGroupId;
                case "integrations":
                case "integration":
                    return IntegrationsGroupId;
                case "suites":
                case "suite":
                    return SuitesGroupId;
                default:
                    return string.Empty;
            }
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray();
            return new string(chars);
        }
    }
}
