using System;
using System.IO;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal static class DevelopmentSourceMatchPolicy
    {
        internal static bool Matches(string packageId, string source, string resolvedPath, string version,
            DevelopmentSourceExpectation expectation)
        {
            if (packageId == null) return !expectation.Connecting && !expectation.WasDirectDependency;
            bool local = PathEquals(resolvedPath, expectation.LocalPath);
            if (expectation.Connecting) return source == "Local" && local;
            string reference = expectation.OriginalReference;
            if (string.IsNullOrEmpty(reference)) return !local;
            if (reference.StartsWith(expectation.PackageId + "@", StringComparison.Ordinal))
                reference = reference.Substring(expectation.PackageId.Length + 1);
            if (reference.StartsWith("file:", StringComparison.Ordinal))
                return source == "Local" && PathEquals(resolvedPath, expectation.OriginalResolvedPath);
            if (local) return false;
            if (reference.IndexOf("://", StringComparison.Ordinal) >= 0 || reference.StartsWith("git@", StringComparison.Ordinal))
                return source == "Git" && string.Equals(packageId, expectation.PackageId + "@" + reference, StringComparison.Ordinal);
            return source == "Registry" && version == reference;
        }

        private static bool PathEquals(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            return string.Equals(Path.GetFullPath(left).TrimEnd('/', '\\'), Path.GetFullPath(right).TrimEnd('/', '\\'),
                Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
    }
}
