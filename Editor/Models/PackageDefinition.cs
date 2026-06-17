using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageDefinition
    {
        public PackageDefinition(
            string displayName,
            string packageId,
            string stableUrl,
            string description,
            IEnumerable<string> dependencies = null,
            PackageType packageType = PackageType.Core,
            string developmentUrl = null,
            string displayVersion = null,
            IEnumerable<PackageExtraDefinition> extras = null,
            IEnumerable<string> optionalCompanions = null,
            string category = null,
            string metadataType = null,
            IEnumerable<string> optionalIntegrations = null,
            IEnumerable<string> bridgeTargets = null,
            IEnumerable<string> suiteMembers = null,
            IEnumerable<string> recommendedWith = null)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name cannot be empty.", nameof(displayName));
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("Package id cannot be empty.", nameof(packageId));
            }

            DisplayName = displayName;
            PackageId = packageId;
            StableUrl = stableUrl ?? string.Empty;
            DevelopmentUrl = developmentUrl ?? string.Empty;
            Description = description ?? string.Empty;
            Dependencies = ToReadOnlyList(dependencies);
            OptionalIntegrations = ToReadOnlyList(optionalIntegrations);
            BridgeTargets = ToReadOnlyList(bridgeTargets);
            SuiteMembers = ToReadOnlyList(suiteMembers);
            RecommendedWith = ToReadOnlyList(recommendedWith);
            PackageType = packageType;
            Category = string.IsNullOrWhiteSpace(category)
                ? GetDefaultCategory(packageType)
                : category.Trim();
            MetadataType = string.IsNullOrWhiteSpace(metadataType)
                ? string.Empty
                : metadataType.Trim();
            DisplayVersion = displayVersion ?? string.Empty;
            Extras = ToReadOnlyList(extras);
            OptionalCompanions = ToReadOnlyList(optionalCompanions);
        }

        public string DisplayName { get; }

        public string PackageId { get; }

        public string StableUrl { get; }

        public string DevelopmentUrl { get; }

        public string PackageReference => GetUrl(PackageChannel.Stable);

        public string Description { get; }

        public string DisplayVersion { get; }

        public IReadOnlyList<string> Dependencies { get; }

        public IReadOnlyList<string> OptionalIntegrations { get; }

        public IReadOnlyList<string> BridgeTargets { get; }

        public IReadOnlyList<string> SuiteMembers { get; }

        public IReadOnlyList<string> RecommendedWith { get; }

        public IReadOnlyList<PackageExtraDefinition> Extras { get; }

        public IReadOnlyList<string> OptionalCompanions { get; }

        public PackageType PackageType { get; }

        public string Category { get; }

        public string MetadataType { get; }

        public bool IsBridge => string.Equals(Category, "Bridge", StringComparison.OrdinalIgnoreCase);

        public bool IsSuite =>
            string.Equals(Category, "Suites", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Category, "Suite", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(MetadataType, "Suite", StringComparison.OrdinalIgnoreCase);

        public bool HasPackageReference => !string.IsNullOrWhiteSpace(GetUrl(PackageChannel.Stable));

        public bool HasDisplayVersion => !string.IsNullOrWhiteSpace(DisplayVersion);

        public bool HasDevelopmentUrl => !string.IsNullOrWhiteSpace(DevelopmentUrl);

        public string GetUrl(PackageChannel channel)
        {
            if (channel == PackageChannel.Stable)
            {
                return StableUrl;
            }

            if (channel == PackageChannel.Development && !string.IsNullOrWhiteSpace(DevelopmentUrl))
            {
                return DevelopmentUrl;
            }

            return channel == PackageChannel.Development ? StableUrl : string.Empty;
        }

        private static IReadOnlyList<string> ToReadOnlyList(IEnumerable<string> values)
        {
            if (values == null)
            {
                return Array.Empty<string>();
            }

            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<PackageExtraDefinition> ToReadOnlyList(IEnumerable<PackageExtraDefinition> values)
        {
            if (values == null)
            {
                return Array.Empty<PackageExtraDefinition>();
            }

            return values
                .Where(value => value != null)
                .ToArray();
        }

        private static string GetDefaultCategory(PackageType packageType)
        {
            switch (packageType)
            {
                case PackageType.UI:
                    return "UI";
                case PackageType.Bridge:
                    return "Bridge";
                default:
                    return "Core";
            }
        }
    }
}
