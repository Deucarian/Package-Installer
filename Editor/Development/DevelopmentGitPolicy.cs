using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal static class DevelopmentGitPolicy
    {
        public static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        public static bool SamePath(string left, string right) => string.Equals(left.TrimEnd('/', '\\'),
            right.TrimEnd('/', '\\'), PathComparison);
        public static bool ContainsPath(string parent, string path) => SamePath(parent, path) ||
            path.StartsWith(parent.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, PathComparison);
        public static void RequireAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || !Path.IsPathRooted(path) ||
                (Path.DirectorySeparatorChar == '\\' && !Regex.IsMatch(path, @"^(?:[A-Za-z]:[\\/]|\\\\[^?\.])")))
                throw new DevelopmentGitException("Choose an absolute repository or project path.");
        }
        public static void RequireFeatureBranch(string branch)
        {
            if (string.IsNullOrEmpty(branch) || branch.Length > 160 ||
                !Regex.IsMatch(branch, @"^(feature|codex|fix|bugfix)/[A-Za-z0-9][A-Za-z0-9._/-]*$") ||
                branch.Contains("..") || branch.Contains("//") || branch.EndsWith("/", StringComparison.Ordinal) ||
                branch.EndsWith(".", StringComparison.Ordinal) || branch.Split('/').Any(p => p.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
                throw new DevelopmentGitException("Choose a feature/, codex/, fix/, or bugfix/ branch. Shared and release branches are protected.");
        }
        public static string RequireRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 4096 || Path.IsPathRooted(path) || path.Contains("\\") ||
                path.Any(char.IsControl) || path.Contains(":")) throw new DevelopmentGitException("Unsupported or unsafe file path.");
            string[] parts = path.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".." ||
                p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p.Equals("PackageCache", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".", StringComparison.Ordinal) || p.EndsWith(" ", StringComparison.Ordinal)))
                throw new DevelopmentGitException("Unsupported or unsafe file path.");
            return path;
        }
        public static string RemoteIdentity(string remote, bool allowFixtureRemote = false)
        {
            if (string.IsNullOrWhiteSpace(remote) || remote.Any(char.IsControl))
                throw new DevelopmentGitException("The package repository remote is invalid.");
            if (allowFixtureRemote && Path.IsPathRooted(remote)) return "fixture:" + Path.GetFullPath(remote).Replace('\\', '/');
            string normalized = remote.Trim();
            if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
                normalized = "https://github.com/" + normalized.Substring(15);
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri uri) ||
                !(uri.Scheme == "https" || uri.Scheme == "ssh") || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !(uri.UserInfo.Length == 0 || (uri.Scheme == "ssh" && uri.UserInfo == "git")) ||
                !uri.IsDefaultPort || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new DevelopmentGitException("Use the package's credential-free canonical GitHub HTTPS or SSH repository URL.");
            string repository = uri.AbsolutePath.Trim('/');
            if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repository = repository.Substring(0, repository.Length - 4);
            if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
                throw new DevelopmentGitException("The package repository remote is invalid.");
            return "github.com/" + repository.ToLowerInvariant();
        }
        public static string DisplayRemote(string identity) => identity.StartsWith("fixture:", StringComparison.Ordinal)
            ? "Disposable local fixture remote" : "https://" + identity + ".git";
        public static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""))).Replace("-", "").ToLowerInvariant();
        }
        public static string UnselectedIndex(string records, IEnumerable<string> selected)
        {
            HashSet<string> paths = new HashSet<string>(selected, StringComparer.Ordinal);
            return string.Join("\0", records.Split('\0').Where(record =>
            {
                int separator = record.IndexOf('\t');
                return separator >= 0 && !paths.Contains(record.Substring(separator + 1));
            }));
        }
        public static IReadOnlyList<string> IncludeMeta(IEnumerable<string> selected, IEnumerable<DevelopmentChangedFile> changes)
        {
            Dictionary<string, DevelopmentChangedFile> files = changes.ToDictionary(file => file.Path, StringComparer.Ordinal);
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in selected)
            {
                RequireRelativePath(path);
                if (!files.TryGetValue(path, out DevelopmentChangedFile file))
                    throw new DevelopmentGitException("The selected file changed. Refresh and select it again.");
                paths.Add(path);
                if (!string.IsNullOrEmpty(file.OriginalPath)) paths.Add(RequireRelativePath(file.OriginalPath));
                string partner = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path + ".meta";
                if (files.TryGetValue(partner, out DevelopmentChangedFile related))
                {
                    paths.Add(partner);
                    if (!string.IsNullOrEmpty(related.OriginalPath)) paths.Add(RequireRelativePath(related.OriginalPath));
                }
            }
            if (paths.Count == 0 || paths.Count > 256) throw new DevelopmentGitException("Select between 1 and 256 files including related .meta files.");
            return paths.ToArray();
        }
    }
}
