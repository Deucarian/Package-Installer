using System;
using System.IO;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Storage below a validated root must not follow a nested junction or symbolic link.
    // This also guards existing lock/temp files. Rechecked immediately before each commit.
    internal static class DevelopmentSourceStoragePaths
    {
        internal static void RequireRegular(string trustedRoot, string target)
        {
            string root = Path.GetFullPath(trustedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string path = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(root, path, comparison) && !path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("The development storage target escapes its validated root.");
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Development storage cannot follow a junction or symbolic link. Use ordinary project and repository metadata directories.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (string.Equals(path, root, comparison)) return;
                path = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("The development storage root is invalid.");
            }
        }
    }
}
