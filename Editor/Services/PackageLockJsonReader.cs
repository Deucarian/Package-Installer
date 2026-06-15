using System;
using System.IO;
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

            return TryReadPackageObjectBodyFromJson(
                File.ReadAllText(packageLockPath),
                packageId,
                out packageBody);
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
    }
}
