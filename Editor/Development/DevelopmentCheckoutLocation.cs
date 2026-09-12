using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal static class DevelopmentCheckoutLocation
    {
        internal static string DefaultPath(string project, string packageId)
        {
            DevelopmentGitPolicy.RequireAbsolutePath(project);
            if (!Regex.IsMatch(packageId ?? "", @"^com\.deucarian\.[a-z0-9][a-z0-9.-]*$"))
                throw new DevelopmentGitException("Select an installed Deucarian package.");
            return Path.Combine(Path.GetFullPath(project), ".deucarian", "checkouts", packageId);
        }

        internal static bool IsDefault(string project, string packageId, string path) =>
            DevelopmentGitPolicy.SamePath(DefaultPath(project, packageId), Path.GetFullPath(path));

        internal static void RequireAllowed(string path, string project, string packageId, bool metadata = false)
        {
            string checkout = DefaultPath(project, packageId);
            bool permittedLocal = DevelopmentGitPolicy.SamePath(path, metadata ? Path.Combine(checkout, ".git") : checkout);
            if (DevelopmentGitPolicy.ContainsPath(path, project) ||
                (DevelopmentGitPolicy.ContainsPath(project, path) && !permittedLocal) ||
                Array.Exists(path.Replace('\\', '/').Split('/'), part => part.Equals("PackageCache", StringComparison.OrdinalIgnoreCase)))
                throw new DevelopmentGitException("Use this package's default local checkout or a separate repository outside the project and PackageCache.");
        }
    }
}
