using System;
using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryValidator
    {
        public const int SupportedSchemaVersion = 1;

        public static bool Validate(PackageRegistry registry, out string message)
        {
            if (registry == null)
            {
                message = "Registry is empty.";
                return false;
            }

            if (registry.schemaVersion != SupportedSchemaVersion)
            {
                message = "Unsupported registry schemaVersion: " + registry.schemaVersion + ".";
                return false;
            }

            if (registry.packages == null)
            {
                message = "Registry packages cannot be null.";
                return false;
            }

            if (!PackageGraphHierarchyBuilder.ValidateGroups(registry.groups, out message))
            {
                return false;
            }

            HashSet<string> groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (registry.groups != null)
            {
                foreach (PackageRegistryGroupEntry group in registry.groups)
                {
                    if (group != null && !string.IsNullOrWhiteSpace(group.id))
                    {
                        groupIds.Add(PackageGraphHierarchyBuilder.NormalizeGroupId(group.id));
                    }
                }
            }

            HashSet<string> packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageRegistryEntry package in registry.packages)
            {
                if (package == null)
                {
                    message = "Registry contains a null package entry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.id))
                {
                    message = "Package id cannot be empty.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.displayName))
                {
                    message = "Package displayName cannot be empty for " + package.id + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.category))
                {
                    message = "Package category cannot be empty for " + package.id + ".";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(package.stableUrl))
                {
                    message = "Package stableUrl cannot be empty for " + package.id + ".";
                    return false;
                }

                if (!packageIds.Add(package.id.Trim()))
                {
                    message = "Duplicate package id in registry: " + package.id + ".";
                    return false;
                }

                string packageGroupId = PackageGraphHierarchyBuilder.NormalizeGroupId(package.groupId);

                if (!string.IsNullOrWhiteSpace(packageGroupId) &&
                    groupIds.Count > 0 &&
                    !groupIds.Contains(packageGroupId))
                {
                    message = "Package " + package.id + " references unknown groupId " + package.groupId + ".";
                    return false;
                }
            }

            foreach (PackageRegistryEntry package in registry.packages)
            {
                if (package.dependencies == null)
                {
                    package.dependencies = Array.Empty<string>();
                }

                foreach (string dependencyId in package.dependencies)
                {
                    if (string.IsNullOrWhiteSpace(dependencyId))
                    {
                        message = "Package " + package.id + " contains an empty dependency id.";
                        return false;
                    }

                    if (!packageIds.Contains(dependencyId.Trim()))
                    {
                        message = "Package " + package.id + " depends on unknown package id " + dependencyId + ".";
                        return false;
                    }
                }

                if (!ValidateKnownRelationshipIds(package, package.optionalCompanions, "optional companion", packageIds, out message) ||
                    !ValidateOptionalRelationshipIds(package, package.optionalIntegrations, "optionalIntegrations", out message) ||
                    !ValidateOptionalRelationshipIds(package, package.integrationTargets, "integrationTargets", out message) ||
                    !ValidateOptionalRelationshipIds(package, package.suiteMembers, "suiteMembers", out message) ||
                    !ValidateOptionalRelationshipIds(package, package.recommendedWith, "recommendedWith", out message))
                {
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool ValidateKnownRelationshipIds(
            PackageRegistryEntry package,
            IEnumerable<string> relationshipIds,
            string relationshipName,
            ISet<string> knownPackageIds,
            out string message)
        {
            if (relationshipIds == null)
            {
                message = string.Empty;
                return true;
            }

            foreach (string relationshipId in relationshipIds)
            {
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    message = "Package " + package.id + " contains an empty " + relationshipName + " id.";
                    return false;
                }

                if (!knownPackageIds.Contains(relationshipId.Trim()))
                {
                    message = "Package " + package.id + " references unknown " + relationshipName + " id " + relationshipId + ".";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool ValidateOptionalRelationshipIds(
            PackageRegistryEntry package,
            IEnumerable<string> relationshipIds,
            string fieldName,
            out string message)
        {
            if (relationshipIds == null)
            {
                message = string.Empty;
                return true;
            }

            foreach (string relationshipId in relationshipIds)
            {
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    message = "Package " + package.id + " contains an empty " + fieldName + " id.";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }
    }
}
