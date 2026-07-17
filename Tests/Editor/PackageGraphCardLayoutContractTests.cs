using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphCardLayoutContractTests
    {
        private const string GraphStylesheetPath =
            "Editor/UI/PackageInstaller/PackageInstallerGraph.uss";

        [Test]
        public void PresentationMetrics_ActionableCardsReserveSafeVerticalInsets()
        {
            PackageGraphNodeMetrics compact =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Compact);
            PackageGraphNodeMetrics full =
                PackageGraphPresentationPolicy.GetMetrics(PackageGraphNodePresentationLevel.Full);

            Assert.AreEqual(90f, compact.Height);
            Assert.AreEqual(144f, full.Height);
        }

        [Test]
        public void Stylesheet_ActionButtonsUseExplicitPaddingAndNonShrinkingHeights()
        {
            string stylesheet = ReadPackageFile(GraphStylesheetPath);

            AssertRuleValues(stylesheet, ".dpi-graph-node__action",
                "height", "23px",
                "min-height", "23px",
                "max-height", "23px",
                "padding-left", "8px",
                "padding-right", "8px",
                "padding-top", "0",
                "padding-bottom", "0",
                "flex-shrink", "0",
                "-unity-text-align", "middle-center");
            AssertRuleValues(
                stylesheet,
                ".dpi-graph-node--presentation-compact .dpi-graph-node__action",
                "height", "16px",
                "min-height", "16px",
                "max-height", "16px",
                "padding-left", "6px",
                "padding-right", "6px",
                "padding-top", "0",
                "padding-bottom", "0");
        }

        private static string ReadPackageFile(string relativePath)
        {
            string assetPath = "Packages/com.deucarian.package-installer/" +
                               relativePath.Replace('\\', '/');
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            Assert.NotNull(package, "Could not resolve package owning '" + assetPath + "'.");

            string fullPath = Path.Combine(
                package.resolvedPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(fullPath), "Expected package file at '" + fullPath + "'.");
            return File.ReadAllText(fullPath);
        }

        private static void AssertRuleValues(
            string stylesheet,
            string selector,
            params string[] nameValuePairs)
        {
            Assert.AreEqual(0, nameValuePairs.Length % 2, "USS assertions must be name/value pairs.");
            IReadOnlyDictionary<string, string> declarations = ParseRule(stylesheet, selector);

            for (int index = 0; index < nameValuePairs.Length; index += 2)
            {
                string property = nameValuePairs[index];
                string expected = nameValuePairs[index + 1];
                Assert.IsTrue(
                    declarations.TryGetValue(property, out string actual),
                    "Selector '" + selector + "' did not declare '" + property + "'.");
                Assert.AreEqual(expected, actual, selector + " -> " + property);
            }
        }

        private static IReadOnlyDictionary<string, string> ParseRule(
            string stylesheet,
            string selector)
        {
            string withoutComments = Regex.Replace(
                stylesheet ?? string.Empty,
                @"/\*.*?\*/",
                string.Empty,
                RegexOptions.Singleline);
            Match rule = Regex.Matches(
                    withoutComments,
                    @"(?s)(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}")
                .Cast<Match>()
                .FirstOrDefault(candidate => candidate.Groups["selectors"].Value
                    .Split(',')
                    .Select(value => value.Trim())
                    .Contains(selector));
            Assert.NotNull(rule, "Could not find USS selector '" + selector + "'.");

            Dictionary<string, string> declarations =
                new Dictionary<string, string>(StringComparer.Ordinal);
            MatchCollection properties = Regex.Matches(
                rule.Groups["body"].Value,
                @"(?m)^\s*(?<name>[-\w]+)\s*:\s*(?<value>[^;]+);\s*$");

            foreach (Match property in properties)
            {
                declarations.Add(
                    property.Groups["name"].Value,
                    property.Groups["value"].Value.Trim());
            }

            return declarations;
        }
    }
}
