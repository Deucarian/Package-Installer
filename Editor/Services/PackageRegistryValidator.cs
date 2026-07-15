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

                    string normalizedDependencyId = dependencyId.Trim();

                    if (string.Equals(
                            package.id.Trim(),
                            normalizedDependencyId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        message = "Package " + package.id + " cannot depend on itself.";
                        return false;
                    }

                    if (!packageIds.Contains(normalizedDependencyId))
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

            if (!ValidateDependencyGraph(registry.packages, out message))
            {
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool ValidateDependencyGraph(
            IEnumerable<PackageRegistryEntry> packages,
            out string message)
        {
            Dictionary<string, PackageRegistryEntry> packageById =
                new Dictionary<string, PackageRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> visitState =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<string> path = new List<string>();

            foreach (PackageRegistryEntry package in packages)
            {
                packageById[package.id.Trim()] = package;
            }

            foreach (PackageRegistryEntry package in packages)
            {
                if (!VisitPackage(package, packageById, visitState, path, out message))
                {
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool VisitPackage(
            PackageRegistryEntry package,
            IReadOnlyDictionary<string, PackageRegistryEntry> packageById,
            IDictionary<string, int> visitState,
            IList<string> path,
            out string message)
        {
            string packageId = package.id.Trim();

            if (visitState.TryGetValue(packageId, out int state))
            {
                if (state == 2)
                {
                    message = string.Empty;
                    return true;
                }

                if (state == 1)
                {
                    int cycleStart = IndexOfPackage(path, packageId);
                    List<string> cycle = new List<string>();

                    for (int index = Math.Max(0, cycleStart); index < path.Count; index++)
                    {
                        cycle.Add(path[index]);
                    }

                    cycle.Add(packageId);
                    message = "Dependency cycle detected: " + string.Join(" -> ", cycle) + ".";
                    return false;
                }
            }

            visitState[packageId] = 1;
            path.Add(packageId);

            foreach (string dependencyIdValue in package.dependencies ?? Array.Empty<string>())
            {
                string dependencyId = dependencyIdValue.Trim();

                if (packageById.TryGetValue(dependencyId, out PackageRegistryEntry dependency) &&
                    !VisitPackage(dependency, packageById, visitState, path, out message))
                {
                    return false;
                }
            }

            path.RemoveAt(path.Count - 1);
            visitState[packageId] = 2;
            message = string.Empty;
            return true;
        }

        private static int IndexOfPackage(IList<string> path, string packageId)
        {
            for (int index = 0; index < path.Count; index++)
            {
                if (string.Equals(path[index], packageId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
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
