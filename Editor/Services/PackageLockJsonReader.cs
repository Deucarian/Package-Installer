using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageLockJsonReader
    {
        public static bool TryReadPackageStringField(
            string packageLockPath,
            string packageId,
            string fieldName,
            out string value)
        {
            value = string.Empty;

            if (!TryReadPackageObjectBody(packageLockPath, packageId, out string packageBody))
            {
                return false;
            }

            Match match = Regex.Match(
                packageBody,
                "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"(?<value>[^\"]+)\"",
                RegexOptions.Singleline);

            if (!match.Success)
            {
                return false;
            }

            value = match.Groups["value"].Value.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        public static bool TryReadPackageObjectBody(
            string packageLockPath,
            string packageId,
            out string packageBody)
        {
            packageBody = string.Empty;

            if (string.IsNullOrWhiteSpace(packageLockPath) ||
                string.IsNullOrWhiteSpace(packageId) ||
                !File.Exists(packageLockPath))
            {
                return false;
            }

            try
            {
                return TryReadPackageObjectBodyFromJson(
                    File.ReadAllText(packageLockPath),
                    packageId,
                    out packageBody);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static bool TryReadPackageDependencies(
            string packageLockPath,
            string packageId,
            out IReadOnlyList<string> dependencies)
        {
            dependencies = Array.Empty<string>();

            if (!TryReadPackageObjectBody(packageLockPath, packageId, out string packageBody) ||
                !TryReadNamedObjectBody(packageBody, "dependencies", out string dependencyBody))
            {
                return false;
            }

            dependencies = Regex.Matches(
                    dependencyBody,
                    "\"(?<id>[^\"]+)\"\\s*:\\s*\"[^\"]*\"",
                    RegexOptions.Singleline)
                .Cast<Match>()
                .Select(match => match.Groups["id"].Value.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }

        internal static bool TryReadPackageDependenciesFromJson(
            string json,
            string packageId,
            out IReadOnlyList<string> dependencies)
        {
            dependencies = Array.Empty<string>();
            if (!TryReadPackageObjectBodyFromJson(json, packageId, out string packageBody) ||
                !TryReadNamedObjectBody(packageBody, "dependencies", out string dependencyBody))
            {
                return false;
            }

            dependencies = Regex.Matches(
                    dependencyBody,
                    "\"(?<id>[^\"]+)\"\\s*:\\s*\"[^\"]*\"",
                    RegexOptions.Singleline)
                .Cast<Match>()
                .Select(match => match.Groups["id"].Value.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return true;
        }

        internal static bool TryReadPackageDependenciesFromJsonForTests(
            string json,
            string packageId,
            out IReadOnlyList<string> dependencies)
        {
            return TryReadPackageDependenciesFromJson(json, packageId, out dependencies);
        }

        internal static bool TryReadFileText(string path, out string contents)
        {
            contents = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                contents = File.ReadAllText(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryReadPackageObjectBodyFromJson(
            string json,
            string packageId,
            out string packageBody)
        {
            packageBody = string.Empty;

            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            MatchCollection packageKeyMatches = Regex.Matches(
                json,
                "\"" + Regex.Escape(packageId) + "\"\\s*:",
                RegexOptions.Singleline);

            foreach (Match packageKeyMatch in packageKeyMatches)
            {
                int objectStart = packageKeyMatch.Index + packageKeyMatch.Length;

                while (objectStart < json.Length && char.IsWhiteSpace(json[objectStart]))
                {
                    objectStart++;
                }

                if (objectStart >= json.Length || json[objectStart] != '{')
                {
                    continue;
                }

                int depth = 0;
                bool inString = false;
                bool escaped = false;

                for (int index = objectStart; index < json.Length; index++)
                {
                    char character = json[index];

                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (character == '\\')
                        {
                            escaped = true;
                        }
                        else if (character == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (character == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (character == '{')
                    {
                        depth++;
                        continue;
                    }

                    if (character != '}')
                    {
                        continue;
                    }

                    depth--;

                    if (depth == 0)
                    {
                        packageBody = json.Substring(objectStart + 1, index - objectStart - 1);
                        return true;
                    }

                    if (depth < 0)
                    {
                        break;
                    }
                }
            }

            return false;
        }

        private static bool TryReadNamedObjectBody(
            string containingBody,
            string fieldName,
            out string objectBody)
        {
            objectBody = string.Empty;
            if (string.IsNullOrWhiteSpace(containingBody) || string.IsNullOrWhiteSpace(fieldName))
            {
                return false;
            }

            Match fieldMatch = Regex.Match(
                containingBody,
                "\"" + Regex.Escape(fieldName) + "\"\\s*:",
                RegexOptions.Singleline);
            if (!fieldMatch.Success)
            {
                return false;
            }

            int objectStart = fieldMatch.Index + fieldMatch.Length;
            while (objectStart < containingBody.Length && char.IsWhiteSpace(containingBody[objectStart]))
            {
                objectStart++;
            }

            if (objectStart >= containingBody.Length || containingBody[objectStart] != '{')
            {
                return false;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int index = objectStart; index < containingBody.Length; index++)
            {
                char character = containingBody[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                }
                else if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && --depth == 0)
                {
                    objectBody = containingBody.Substring(objectStart + 1, index - objectStart - 1);
                    return true;
                }
            }

            return false;
        }
    }
}
