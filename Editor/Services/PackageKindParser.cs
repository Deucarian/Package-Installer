using System;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageKindParser
    {
        public static PackageKind Parse(
            string canonicalKind,
            string legacyType,
            string legacyCategory)
        {
            if (TryParseCanonical(canonicalKind, out PackageKind kind))
            {
                return kind;
            }

            string type = Normalize(legacyType);

            switch (type)
            {
                case "tool":
                    return PackageKind.Tool;
                case "integration":
                    return PackageKind.Integration;
                case "suite":
                    return PackageKind.Suite;
                case "template":
                    return PackageKind.Template;
            }

            string category = Normalize(legacyCategory);

            switch (category)
            {
                case "tools":
                case "tool":
                case "editor":
                    return PackageKind.Tool;
                case "integration":
                case "integrations":
                    return PackageKind.Integration;
                case "suite":
                case "suites":
                    return PackageKind.Suite;
                case "template":
                case "templates":
                    return PackageKind.Template;
                default:
                    return PackageKind.Library;
            }
        }

        public static bool TryParseCanonical(string value, out PackageKind kind)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();

            foreach (PackageKind candidate in Enum.GetValues(typeof(PackageKind)))
            {
                if (string.Equals(
                        normalized,
                        candidate.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = PackageKind.Library;
            return false;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }
}
