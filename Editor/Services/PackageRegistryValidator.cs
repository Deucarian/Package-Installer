using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryValidator
    {
        public const int LegacySchemaVersion = 1;
        public const int SupportedSchemaVersion = 2;

        public static bool Validate(PackageRegistry registry, out string message)
        {
            if (registry == null)
            {
                message = "Registry is empty.";
                return false;
            }

            if (registry.schemaVersion < LegacySchemaVersion ||
                registry.schemaVersion > SupportedSchemaVersion)
            {
                message = "Unsupported registry schemaVersion: " + registry.schemaVersion + ".";
                return false;
            }

            if (registry.packages == null)
            {
                message = "Registry packages cannot be null.";
                return false;
            }

            bool usesCanonicalSchema = registry.schemaVersion == SupportedSchemaVersion;

            if (usesCanonicalSchema && (registry.groups == null || registry.groups.Length == 0))
            {
                message = "Registry schemaVersion 2 requires groups.";
                return false;
            }

            if (!PackageGraphHierarchyBuilder.ValidateGroups(registry.groups, out message))
            {
                return false;
            }

            if (usesCanonicalSchema && !ValidateCanonicalGroupDepth(registry.groups, out message))
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

                if (!usesCanonicalSchema && string.IsNullOrWhiteSpace(package.category))
                {
                    message = "Package category cannot be empty for " + package.id + ".";
                    return false;
                }

                if (usesCanonicalSchema &&
                    !PackageKindParser.TryParseCanonical(package.kind, out PackageKind ignoredKind))
                {
                    message = "Package " + package.id + " has unknown kind " +
                              (package.kind ?? "(empty)") + ".";
                    return false;
                }

                if (usesCanonicalSchema && string.IsNullOrWhiteSpace(package.groupId))
                {
                    message = "Package groupId cannot be empty for " + package.id + ".";
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
                    !ValidateRelationshipIds(package, package.optionalIntegrations, "optionalIntegrations", packageIds, usesCanonicalSchema, out message) ||
                    !ValidateRelationshipIds(package, package.integrationTargets, "integrationTargets", packageIds, usesCanonicalSchema, out message) ||
                    !ValidateRelationshipIds(package, package.suiteMembers, "suiteMembers", packageIds, usesCanonicalSchema, out message) ||
                    !ValidateRelationshipIds(package, package.recommendedWith, "recommendedWith", packageIds, usesCanonicalSchema, out message))
                {
                    return false;
                }

                if (usesCanonicalSchema && !ValidateCanonicalKindContract(package, out message))
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

        private static bool ValidateRelationshipIds(
            PackageRegistryEntry package,
            IEnumerable<string> relationshipIds,
            string fieldName,
            ISet<string> knownPackageIds,
            bool requireKnownIds,
            out string message)
        {
            if (relationshipIds == null)
            {
                message = string.Empty;
                return true;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string relationshipId in relationshipIds)
            {
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    message = "Package " + package.id + " contains an empty " + fieldName + " id.";
                    return false;
                }

                string normalizedId = relationshipId.Trim();

                if (string.Equals(package.id.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    message = "Package " + package.id + " cannot reference itself in " + fieldName + ".";
                    return false;
                }

                if (!seen.Add(normalizedId))
                {
                    message = "Package " + package.id + " contains duplicate " + fieldName +
                              " id " + relationshipId + ".";
                    return false;
                }

                if (requireKnownIds && !knownPackageIds.Contains(normalizedId))
                {
                    message = "Package " + package.id + " references unknown " + fieldName +
                              " id " + relationshipId + ".";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool ValidateCanonicalKindContract(
            PackageRegistryEntry package,
            out string message)
        {
            PackageKindParser.TryParseCanonical(package.kind, out PackageKind kind);

            if (kind == PackageKind.Integration)
            {
                if (package.integrationTargets == null || package.integrationTargets.Length == 0)
                {
                    message = "Integration package " + package.id + " requires integrationTargets.";
                    return false;
                }

                if (!ContainsEvery(package.dependencies, package.integrationTargets))
                {
                    message = "Integration package " + package.id +
                              " must directly depend on every integration target.";
                    return false;
                }
            }
            else if (package.integrationTargets != null && package.integrationTargets.Length > 0)
            {
                message = "Package " + package.id +
                          " declares integrationTargets but its kind is not Integration.";
                return false;
            }

            if (kind == PackageKind.Suite)
            {
                if (package.suiteMembers == null || package.suiteMembers.Length == 0)
                {
                    message = "Suite package " + package.id + " requires suiteMembers.";
                    return false;
                }

                if (!SetsEqual(package.dependencies, package.suiteMembers))
                {
                    message = "Suite package " + package.id +
                              " dependencies must exactly match suiteMembers.";
                    return false;
                }
            }
            else if (package.suiteMembers != null && package.suiteMembers.Length > 0)
            {
                message = "Package " + package.id +
                          " declares suiteMembers but its kind is not Suite.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool ValidateCanonicalGroupDepth(
            IEnumerable<PackageRegistryGroupEntry> groups,
            out string message)
        {
            Dictionary<string, PackageRegistryGroupEntry> groupById =
                (groups ?? Array.Empty<PackageRegistryGroupEntry>())
                .Where(group => group != null && !string.IsNullOrWhiteSpace(group.id))
                .ToDictionary(
                    group => PackageGraphHierarchyBuilder.NormalizeGroupId(group.id),
                    StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, PackageRegistryGroupEntry> pair in groupById)
            {
                int depth = 1;
                string parentId = PackageGraphHierarchyBuilder.NormalizeGroupId(
                    pair.Value.parentGroupId);

                while (!string.IsNullOrWhiteSpace(parentId) &&
                       groupById.TryGetValue(parentId, out PackageRegistryGroupEntry parent))
                {
                    depth++;

                    if (depth > 2)
                    {
                        message = "Group " + pair.Value.id +
                                  " exceeds the maximum schemaVersion 2 depth of 2.";
                        return false;
                    }

                    parentId = PackageGraphHierarchyBuilder.NormalizeGroupId(
                        parent.parentGroupId);
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool ContainsEvery(
            IEnumerable<string> values,
            IEnumerable<string> requiredValues)
        {
            HashSet<string> valueSet = new HashSet<string>(
                values ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            return (requiredValues ?? Array.Empty<string>()).All(valueSet.Contains);
        }

        private static bool SetsEqual(
            IEnumerable<string> first,
            IEnumerable<string> second)
        {
            HashSet<string> firstSet = new HashSet<string>(
                first ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> secondSet = new HashSet<string>(
                second ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            return firstSet.SetEquals(secondSet);
        }
    }
}
