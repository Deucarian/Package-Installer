using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageCompositionPresetDefinition
    {
        public PackageCompositionPresetDefinition(
            string id,
            string displayName,
            string description,
            IEnumerable<string> packageIds,
            bool recommended = false)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Composition preset id cannot be empty.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Composition preset display name cannot be empty.", nameof(displayName));
            }

            Id = id.Trim();
            DisplayName = displayName.Trim();
            Description = description == null ? string.Empty : description.Trim();
            PackageIds = (packageIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Recommended = recommended;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public IReadOnlyList<string> PackageIds { get; }

        public bool Recommended { get; }
    }
}
