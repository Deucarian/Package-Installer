using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageReverseDependencySource
    {
        UnityPackageManager,
        PackageLock,
        Registry
    }

    internal sealed class PackageReverseDependency
    {
        public PackageReverseDependency(
            string packageId,
            string displayName,
            PackageReverseDependencySource source)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? PackageId : displayName;
            Source = source;
        }

        public string PackageId { get; }
        public string DisplayName { get; }
        public PackageReverseDependencySource Source { get; }
    }

    internal sealed class PackageReverseDependencyPackageMetadata
    {
        public PackageReverseDependencyPackageMetadata(
            string packageId,
            string displayName,
            bool hasDependencyMetadata,
            IEnumerable<string> dependencyIds)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            HasDependencyMetadata = hasDependencyMetadata;
            DependencyIds = (dependencyIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public bool HasDependencyMetadata { get; }

        public IReadOnlyList<string> DependencyIds { get; }
    }

    internal sealed class PackageReverseDependencyResolver
    {
        internal static IReadOnlyList<string> ResolveRegistryIdsForTests(
            string packageId,
            IEnumerable<PackageDefinition> packages,
            IEnumerable<string> installedPackageIds = null)
        {
            HashSet<string> installed = installedPackageIds == null
                ? null
                : new HashSet<string>(installedPackageIds, StringComparer.OrdinalIgnoreCase);
            return (packages ?? Array.Empty<PackageDefinition>())
                .Where(package => package != null &&
                                  (installed == null || installed.Contains(package.PackageId)) &&
                                  package.Dependencies.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                .Select(package => package.PackageId)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static IReadOnlyList<string> ResolvePackageLockIdsForTests(
            string json,
            string packageId,
            IEnumerable<string> candidateIds)
        {
            return (candidateIds ?? Array.Empty<string>())
                .Where(candidateId =>
                    PackageLockJsonReader.TryReadPackageDependenciesFromJsonForTests(
                        json,
                        candidateId,
                        out IReadOnlyList<string> dependencies) &&
                    dependencies.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<PackageReverseDependency> Resolve(
            string packageId,
            IEnumerable<string> detectedInstalledPackageIds = null)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return Array.Empty<PackageReverseDependency>();
            }

            PackageManagerPackageInfo[] installedPackages =
                PackageManagerPackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageManagerPackageInfo>();
            HashSet<string> installedIds = new HashSet<string>(
                installedPackages
                    .Where(package => package != null && !string.IsNullOrWhiteSpace(package.name))
                    .Select(package => package.name)
                    .Concat(detectedInstalledPackageIds ?? Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase);
            PackageReverseDependencyPackageMetadata[] packageMetadata = installedPackages
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.name))
                .Select(package => new PackageReverseDependencyPackageMetadata(
                    package.name,
                    package.displayName,
                    package.dependencies != null,
                    package.dependencies == null
                        ? Array.Empty<string>()
                        : package.dependencies.Select(dependency => dependency.name)))
                .ToArray();

            string packageLockPath = Path.Combine(GetProjectRootPath(), "Packages", "packages-lock.json");
            PackageLockJsonReader.TryReadFileText(packageLockPath, out string packageLockJson);

            return ResolveFromSources(
                packageId,
                installedIds,
                packageMetadata,
                packageLockJson,
                PackageRegistryProvider.All);
        }

        internal static IReadOnlyList<PackageReverseDependency> ResolveFromSourcesForTests(
            string packageId,
            IEnumerable<string> installedPackageIds,
            IEnumerable<PackageReverseDependencyPackageMetadata> packageMetadata,
            string packageLockJson,
            IEnumerable<PackageDefinition> registryPackages)
        {
            return ResolveFromSources(
                packageId,
                installedPackageIds,
                packageMetadata,
                packageLockJson,
                registryPackages);
        }

        private static IReadOnlyList<PackageReverseDependency> ResolveFromSources(
            string packageId,
            IEnumerable<string> installedPackageIds,
            IEnumerable<PackageReverseDependencyPackageMetadata> packageMetadata,
            string packageLockJson,
            IEnumerable<PackageDefinition> registryPackages)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return Array.Empty<PackageReverseDependency>();
            }

            Dictionary<string, PackageReverseDependencyPackageMetadata> metadataByPackageId =
                (packageMetadata ?? Array.Empty<PackageReverseDependencyPackageMetadata>())
                .Where(metadata => metadata != null && !string.IsNullOrWhiteSpace(metadata.PackageId))
                .GroupBy(metadata => metadata.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageDefinition> registryByPackageId =
                (registryPackages ?? Array.Empty<PackageDefinition>())
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.PackageId))
                .GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            string[] installedIds = (installedPackageIds ?? Array.Empty<string>())
                .Concat(metadataByPackageId.Keys)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            List<PackageReverseDependency> dependents = new List<PackageReverseDependency>();

            foreach (string candidateId in installedIds)
            {
                metadataByPackageId.TryGetValue(
                    candidateId,
                    out PackageReverseDependencyPackageMetadata metadata);
                registryByPackageId.TryGetValue(candidateId, out PackageDefinition definition);
                string displayName = metadata != null && !string.IsNullOrWhiteSpace(metadata.DisplayName)
                    ? metadata.DisplayName
                    : definition != null
                        ? definition.DisplayName
                        : candidateId;

                if (metadata != null && metadata.HasDependencyMetadata)
                {
                    if (metadata.DependencyIds.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                    {
                        dependents.Add(new PackageReverseDependency(
                            candidateId,
                            displayName,
                            PackageReverseDependencySource.UnityPackageManager));
                    }

                    continue;
                }

                if (PackageLockJsonReader.TryReadPackageDependenciesFromJson(
                        packageLockJson,
                        candidateId,
                        out IReadOnlyList<string> lockDependencies))
                {
                    if (lockDependencies.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                    {
                        dependents.Add(new PackageReverseDependency(
                            candidateId,
                            displayName,
                            PackageReverseDependencySource.PackageLock));
                    }

                    continue;
                }

                if (definition != null &&
                    definition.Dependencies.Contains(packageId, StringComparer.OrdinalIgnoreCase))
                {
                    dependents.Add(new PackageReverseDependency(
                        candidateId,
                        displayName,
                        PackageReverseDependencySource.Registry));
                }
            }

            return dependents
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetProjectRootPath()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot != null ? projectRoot.FullName : Application.dataPath;
        }
    }
}
