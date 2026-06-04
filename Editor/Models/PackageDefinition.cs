using System;
using System.Collections.Generic;
using System.Linq;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageDefinition
    {
        public PackageDefinition(
            string displayName,
            string packageId,
            string packageReference,
            string description,
            IEnumerable<string> dependencies = null,
            IEnumerable<string> scriptingDefineSymbols = null,
            bool isIntegration = false,
            string displayVersion = null)
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
            PackageReference = packageReference ?? string.Empty;
            Description = description ?? string.Empty;
            Dependencies = ToReadOnlyList(dependencies);
            ScriptingDefineSymbols = ToReadOnlyList(scriptingDefineSymbols);
            IsIntegration = isIntegration;
            DisplayVersion = displayVersion ?? string.Empty;
        }

        public string DisplayName { get; }

        public string PackageId { get; }

        public string PackageReference { get; }

        public string Description { get; }

        public string DisplayVersion { get; }

        public IReadOnlyList<string> Dependencies { get; }

        public IReadOnlyList<string> ScriptingDefineSymbols { get; }

        public bool IsIntegration { get; }

        public bool HasPackageReference => !string.IsNullOrWhiteSpace(PackageReference);

        public bool HasDisplayVersion => !string.IsNullOrWhiteSpace(DisplayVersion);

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
    }
}
