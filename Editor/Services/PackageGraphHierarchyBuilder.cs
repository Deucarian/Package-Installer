using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageGraphHierarchyBuilder
    {
        public const string InfrastructureGroupId = "infrastructure";
        public const string StateDataGroupId = "state-data";
        public const string RuntimeServicesGroupId = "runtime-services";
        public const string ExperienceInteractionGroupId = "experience-interaction";
        public const string UiPresentationGroupId = "ui-presentation";
        public const string WorldInteractionGroupId = "world-interaction";
        public const string ToolsQualityGroupId = "tools-quality";
        public const string IntegrationsGroupId = "integrations";
        public const string SuitesGroupId = "suites";

        private static readonly PackageGraphGroup[] DefaultGroups =
        {
            new PackageGraphGroup(
                InfrastructureGroupId,
                "Infrastructure",
                string.Empty,
                "Cross-cutting shared infrastructure used by multiple Deucarian packages.",
                10,
                "editor",
                "infrastructure"),
            new PackageGraphGroup(
                StateDataGroupId,
                "State & Data",
                string.Empty,
                "Generic state, data, repository, and selection primitives.",
                20,
                "core-state",
                "state-data"),
            new PackageGraphGroup(
                RuntimeServicesGroupId,
                "Runtime Services",
                string.Empty,
                "Application-facing runtime API, session, and loading services.",
                30,
                "object-loading",
                "runtime-services"),
            new PackageGraphGroup(
                ExperienceInteractionGroupId,
                "Experience & Interaction",
                string.Empty,
                "UI, presentation, interaction, selection, and user/world experience systems.",
                40,
                "generic-ui-items",
                "experience-interaction"),
            new PackageGraphGroup(
                UiPresentationGroupId,
                "UI & Presentation",
                ExperienceInteractionGroupId,
                "User interface binding, flow, and presentation systems.",
                41,
                "generic-ui-items",
                "ui-presentation"),
            new PackageGraphGroup(
                WorldInteractionGroupId,
                "World Interaction",
                ExperienceInteractionGroupId,
                "World-object interaction, selection, and input-facing systems.",
                42,
                "selection",
                "world-interaction"),
            new PackageGraphGroup(
                ToolsQualityGroupId,
                "Tools & Quality",
                string.Empty,
                "Installer, diagnostics, and development quality tooling.",
                50,
                "package-installer",
                "tools"),
            new PackageGraphGroup(
                IntegrationsGroupId,
                "Integrations",
                string.Empty,
                "Installable packages that connect two or more systems.",
                60,
                "api-helper",
                "integration"),
            new PackageGraphGroup(
                SuitesGroupId,
                "Suites",
                string.Empty,
                "Curated installable bundle packages.",
                70,
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

            string rawGroupId = package != null ? package.GroupId : string.Empty;
            string explicitGroupId = NormalizeGroupId(rawGroupId);

            if (!IsLegacyGroupAlias(rawGroupId) &&
                !string.IsNullOrWhiteSpace(explicitGroupId) &&
                knownGroupIds.Contains(explicitGroupId))
            {
                return explicitGroupId;
            }

            string inferredGroupId = InferPackageGroupId(package);

            if (knownGroupIds.Contains(inferredGroupId))
            {
                return inferredGroupId;
            }

            string legacyGroupId = NormalizeLegacyGroupId(package != null ? package.EcosystemGroup : string.Empty);

            if (!string.IsNullOrWhiteSpace(legacyGroupId) && knownGroupIds.Contains(legacyGroupId))
            {
                return legacyGroupId;
            }

            return !string.IsNullOrWhiteSpace(explicitGroupId) && knownGroupIds.Contains(explicitGroupId)
                ? explicitGroupId
                : InfrastructureGroupId;
        }

        public static string GetGroupPath(
            IEnumerable<PackageGraphGroup> groups,
            string groupId)
        {
            Dictionary<string, PackageGraphGroup> groupById = CreateGroups(groups)
                .ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);
            List<string> path = new List<string>();
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentGroupId = NormalizeGroupId(groupId);

            while (!string.IsNullOrWhiteSpace(currentGroupId) &&
                   visited.Add(currentGroupId) &&
                   groupById.TryGetValue(currentGroupId, out PackageGraphGroup group))
            {
                path.Add(group.DisplayName);
                currentGroupId = group.ParentGroupId;
            }

            path.Reverse();
            return path.Count == 0 ? string.Empty : string.Join(" / ", path.ToArray());
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
                return InfrastructureGroupId;
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
                    return InfrastructureGroupId;
                case "com.deucarian.core-state":
                    return StateDataGroupId;
                case "com.deucarian.api":
                case "com.deucarian.session":
                case "com.deucarian.object-loading":
                    return RuntimeServicesGroupId;
                case "com.deucarian.theming":
                case "com.deucarian.ui-binding":
                case "com.deucarian.ui-flow":
                    return UiPresentationGroupId;
                case "com.deucarian.object-selection":
                    return WorldInteractionGroupId;
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
                return UiPresentationGroupId;
            }

            if (string.Equals(package.Category, "World", StringComparison.OrdinalIgnoreCase))
            {
                return WorldInteractionGroupId;
            }

            if (string.Equals(package.Category, "Runtime", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(package.Category, "Services", StringComparison.OrdinalIgnoreCase))
            {
                return RuntimeServicesGroupId;
            }

            return InfrastructureGroupId;
        }

        private static string NormalizeLegacyGroupId(string value)
        {
            string token = NormalizeToken(value);

            switch (token)
            {
                case "foundation":
                case "core":
                    return InfrastructureGroupId;
                case "infrastructure":
                    return InfrastructureGroupId;
                case "statedata":
                case "state":
                case "data":
                case "corestate":
                    return StateDataGroupId;
                case "servicesruntime":
                case "services":
                case "runtime":
                case "runtimeworld":
                case "runtimeservices":
                    return RuntimeServicesGroupId;
                case "experienceuiworld":
                case "uiexperience":
                case "experienceinteraction":
                case "interaction":
                case "experience":
                    return ExperienceInteractionGroupId;
                case "uipresentation":
                case "presentation":
                case "ui":
                    return UiPresentationGroupId;
                case "worldinteraction":
                case "world":
                case "selection":
                    return WorldInteractionGroupId;
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

        private static bool IsLegacyGroupAlias(string value)
        {
            string token = NormalizeToken(value);

            switch (token)
            {
                case "foundation":
                case "core":
                case "servicesruntime":
                case "runtimeworld":
                case "experienceuiworld":
                case "uiexperience":
                case "toolsquality":
                    return true;
                default:
                    return false;
            }
        }
    }
}
