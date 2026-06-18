using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageRegistryTests
    {
        private const string ValidRegistryJson =
            "{ \"schemaVersion\": 1, \"updatedAt\": \"2026-06-05\", \"packages\": [" +
            "{ \"id\": \"com.deucarian.core-state\", \"displayName\": \"Deucarian Core State\", \"category\": \"Core\", \"description\": \"Core package.\", \"stableUrl\": \"https://github.com/Deucarian/Core-State.git#main\", \"developmentUrl\": \"https://github.com/Deucarian/Core-State.git#develop\", \"dependencies\": [] }," +
            "{ \"id\": \"com.deucarian.core-state.integration\", \"displayName\": \"Core Integration\", \"category\": \"Integration\", \"description\": \"Integration package.\", \"stableUrl\": \"https://github.com/Deucarian/Core-State-Integration.git#main\", \"developmentUrl\": \"https://github.com/Deucarian/Core-State-Integration.git#develop\", \"dependencies\": [\"com.deucarian.core-state\"] }" +
            "] }";

        [Test]
        public void ValidRegistryParses()
        {
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(ValidRegistryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.Bundled, result.Source);
            Assert.AreEqual(2, result.Registry.packages.Length);
        }

        [Test]
        public void UnsupportedSchemaVersionIsRejected()
        {
            string json = ValidRegistryJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2");

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("Unsupported registry schemaVersion", result.ErrorMessage);
        }

        [Test]
        public void DuplicateIdsAreRejected()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.duplicate\", \"displayName\": \"One\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/one.git#main\", \"dependencies\": [] }," +
                "{ \"id\": \"com.deucarian.duplicate\", \"displayName\": \"Two\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/two.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("Duplicate package id", result.ErrorMessage);
        }

        [Test]
        public void MissingDependencyIdIsRejected()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.integration\", \"displayName\": \"Integration\", \"category\": \"Integration\", \"stableUrl\": \"https://example.com/integration.git#main\", \"dependencies\": [\"com.deucarian.missing\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("depends on unknown package id", result.ErrorMessage);
        }

        [Test]
        public void UnknownOptionalGraphRelationshipDoesNotRejectRegistry()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.core\", \"displayName\": \"Core\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/core.git#main\", \"dependencies\": [], \"optionalIntegrations\": [\"com.deucarian.optional-missing\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
        }

        [Test]
        public void MissingDevelopmentUrlDisablesDevelopmentChannel()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.stable-only\", \"displayName\": \"Stable Only\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/stable.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);
            PackageDefinition packageDefinition = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single(package => package.PackageId == "com.deucarian.stable-only");

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.IsFalse(packageDefinition.HasDevelopmentUrl);
            Assert.AreEqual("https://example.com/stable.git#main", packageDefinition.GetUrl(PackageChannel.Stable));
        }

        [Test]
        public void SemanticOverviewMetadataIsOptionalAndMapsToPackageDefinitions()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.session\", \"displayName\": \"Session\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/session.git#main\", \"ecosystemGroup\": \"ServicesRuntime\", \"overviewOrder\": 20, \"dependencies\": [] }," +
                "{ \"id\": \"com.deucarian.legacy\", \"displayName\": \"Legacy\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/legacy.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);
            PackageDefinition[] packages = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .ToArray();
            PackageDefinition session = packages
                .Single(package => package.PackageId == "com.deucarian.session");
            PackageDefinition legacy = packages
                .Single(package => package.PackageId == "com.deucarian.legacy");

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual("ServicesRuntime", session.EcosystemGroup);
            Assert.AreEqual(20, session.OverviewOrder);
            Assert.IsTrue(session.HasOverviewOrder);
            Assert.IsEmpty(legacy.EcosystemGroup);
            Assert.AreEqual(0, legacy.OverviewOrder);
            Assert.IsFalse(legacy.HasOverviewOrder);
        }

        [Test]
        public void RemoteFailureKeepsBundledRegistry()
        {
            RunAsync(async () =>
            {
            PackageRegistry bundledRegistry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://example.com/core.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            PackageRegistryLoader loader = new PackageRegistryLoader(
                _ => Task.FromException<string>(new InvalidOperationException("offline")));

            PackageRegistryLoadResult result = await loader.LoadRemoteAsync(bundledRegistry);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingBundled, result.Source);
            Assert.AreSame(bundledRegistry, result.Registry);
            });
        }

        [Test]
        public void PackageNameValidationAcceptsMatchingPackageJsonNames()
        {
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(ValidRegistryJson, PackageRegistrySource.Bundled);

            bool isValid = PackageRegistryPackageNameValidator.ValidatePackageNames(
                result.Registry,
                package => "{ \"name\": \"" + package.id + "\" }",
                out string message);

            Assert.IsTrue(isValid, message);
        }

        [Test]
        public void PackageNameValidationRejectsPackageIdMismatch()
        {
            PackageRegistry registry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state-invalid",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            bool isValid = PackageRegistryPackageNameValidator.ValidatePackageNames(
                registry,
                _ => "{ \"name\": \"com.deucarian.core-state\" }",
                out string message);

            Assert.IsFalse(isValid);
            StringAssert.Contains("does not match target package.json name", message);
        }

        [Test]
        public void RemoteRegistryPackageNameMismatchKeepsBundledRegistry()
        {
            RunAsync(async () =>
            {
            PackageRegistry bundledRegistry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.core-state",
                        displayName = "Deucarian Core State",
                        category = "Core",
                        stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            string remoteJson =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.deucarian.core-state-invalid\", \"displayName\": \"Deucarian Core State\", \"category\": \"Core\", \"stableUrl\": \"https://github.com/Deucarian/Core-State.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoader loader = new PackageRegistryLoader(
                _ => Task.FromResult(remoteJson),
                packageManifestFetcher: _ => Task.FromResult("{ \"name\": \"com.deucarian.core-state\" }"));

            PackageRegistryLoadResult result = await loader.LoadRemoteAsync(bundledRegistry);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(PackageRegistrySource.RemoteFailedUsingBundled, result.Source);
            Assert.AreSame(bundledRegistry, result.Registry);
            StringAssert.Contains("does not match target package.json name", result.ErrorMessage);
            });
        }

        [Test]
        public void GitHubPackageJsonUrlUsesBranchAndPackagePath()
        {
            bool resolved = PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                "https://github.com/Deucarian/Example.git?path=/Packages/Example#develop",
                out string packageJsonUrl);

            Assert.IsTrue(resolved);
            Assert.AreEqual(
                "https://raw.githubusercontent.com/Deucarian/Example/develop/Packages/Example/package.json",
                packageJsonUrl);
        }

        [Test]
        public void GitHubPackageJsonUrlCanOverrideReferenceWithResolvedRevision()
        {
            const string revision = "0123456789abcdef0123456789abcdef01234567";

            bool resolved = PackageRegistryPackageNameValidator.TryCreateGitHubPackageJsonUrl(
                "https://github.com/Deucarian/Example.git?path=/Packages/Example#develop",
                revision,
                out string packageJsonUrl);

            Assert.IsTrue(resolved);
            Assert.AreEqual(
                "https://raw.githubusercontent.com/Deucarian/Example/" + revision + "/Packages/Example/package.json",
                packageJsonUrl);
        }

        [Test]
        public void PackageJsonVersionCanBeRead()
        {
            bool found = PackageRegistryPackageNameValidator.TryReadPackageVersion(
                "{ \"name\": \"com.deucarian.example\", \"version\": \"1.2.3\" }",
                out string version);

            Assert.IsTrue(found);
            Assert.AreEqual("1.2.3", version);
        }

        [Test]
        public void RemoteRegistryPackageFetchFailureReportsPackageJsonUrl()
        {
            RunAsync(async () =>
            {
            PackageRegistry registry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.deucarian.editor",
                        displayName = "Deucarian Editor",
                        category = "Editor",
                        stableUrl = "https://github.com/Deucarian/Editor.git#main",
                        dependencies = Array.Empty<string>()
                    }
                }
            };

            string message = await PackageRegistryPackageNameValidator.ValidateRemotePackageNamesAsync(
                registry,
                _ => Task.FromException<string>(new InvalidOperationException("404 Not Found")));

            StringAssert.Contains(
                "https://raw.githubusercontent.com/Deucarian/Editor/main/package.json",
                message);
            StringAssert.Contains("404 Not Found", message);
            });
        }

        [Test]
        public void BundledRegistryUsesRealCoreStatePackageId()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            PackageDefinition coreState = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single(package => package.DisplayName == "Deucarian Core State");

            string[] dependencyIds = result.Registry.packages
                .Where(package => package.dependencies != null)
                .SelectMany(package => package.dependencies)
                .ToArray();

            Assert.AreEqual("com.deucarian.core-state", coreState.PackageId);
            Assert.IsFalse(dependencyIds.Contains("com.deucarian.core-state-legacy"));
        }

        [Test]
        public void BundledRegistryIncludesObjectLoadingAndApiIntegration()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);

            PackageRegistryEntry objectLoading = result.Registry.packages
                .Single(package => package.id == "com.deucarian.object-loading");
            Assert.AreEqual("Deucarian Object Loading", objectLoading.displayName);
            Assert.AreEqual("Core", objectLoading.category);
            CollectionAssert.AreEqual(
                new[]
                {
                    "com.deucarian.logging"
                },
                objectLoading.dependencies);
            CollectionAssert.AreEqual(
                new[]
                {
                    "com.deucarian.diagnostics"
                },
                objectLoading.optionalCompanions);

            PackageRegistryEntry integration = result.Registry.packages
                .Single(package => package.id == "com.deucarian.object-loading.api-integration");
            Assert.AreEqual("Deucarian Object Loading API Integration", integration.displayName);
            Assert.AreEqual("Integration", integration.category);
            CollectionAssert.AreEqual(
                new[]
                {
                    "com.deucarian.object-loading",
                    "com.deucarian.api"
                },
                integration.dependencies);
        }

        [Test]
        public void BundledRegistryIncludesDiagnosticsPackage()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);

            PackageRegistryEntry diagnostics = result.Registry.packages
                .Single(package => package.id == "com.deucarian.diagnostics");
            Assert.AreEqual("Deucarian Diagnostics", diagnostics.displayName);
            Assert.AreEqual("Tools", diagnostics.category);
            CollectionAssert.AreEqual(
                new[]
                {
                    "com.deucarian.editor",
                    "com.deucarian.logging"
                },
                diagnostics.dependencies);

            Assert.IsFalse(result.Registry.packages.Any(
                package => package.id == "com.deucarian.diagnostics-suite"));
        }

        [Test]
        public void BundledRegistryIncludesPackageInstaller()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            PackageDefinition packageInstaller = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single(package => package.PackageId == "com.deucarian.package-installer");

            Assert.AreEqual("Tools", packageInstaller.Category);
            StringAssert.Contains("Package-Installer.git#main", packageInstaller.StableUrl);
            Assert.AreEqual("ToolsQuality", packageInstaller.EcosystemGroup);
            Assert.AreEqual(20, packageInstaller.OverviewOrder);
        }

        [Test]
        public void BundledRegistryIncludesEditorBackedTools()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);

            PackageRegistryEntry editor = result.Registry.packages
                .Single(package => package.id == "com.deucarian.editor");
            PackageRegistryEntry logging = result.Registry.packages
                .Single(package => package.id == "com.deucarian.logging");
            PackageRegistryEntry theming = result.Registry.packages
                .Single(package => package.id == "com.deucarian.theming");
            PackageRegistryEntry packageInstaller = result.Registry.packages
                .Single(package => package.id == "com.deucarian.package-installer");

            Assert.AreEqual("Editor", editor.category);
            StringAssert.Contains("Editor.git#main", editor.stableUrl);
            StringAssert.Contains("Editor.git#develop", editor.developmentUrl);
            CollectionAssert.AreEqual(new[] { "com.deucarian.editor" }, logging.dependencies);
            CollectionAssert.AreEqual(new[] { "com.deucarian.editor", "com.deucarian.logging" }, theming.dependencies);
            CollectionAssert.AreEqual(new[] { "com.deucarian.editor", "com.deucarian.logging" }, packageInstaller.dependencies);
        }

        [Test]
        public void BundledRegistryProvidesStableAndDevelopmentChannels()
        {
            string registryJson = File.ReadAllText(GetBundledRegistryPath());
            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(registryJson, PackageRegistrySource.Bundled);

            Assert.IsTrue(result.IsValid, result.ErrorMessage);

            foreach (PackageRegistryEntry package in result.Registry.packages)
            {
                StringAssert.StartsWith("https://github.com/Deucarian/", package.stableUrl, package.id);
                StringAssert.EndsWith(".git#main", package.stableUrl, package.id);
                StringAssert.StartsWith("https://github.com/Deucarian/", package.developmentUrl, package.id);
                StringAssert.EndsWith(".git#develop", package.developmentUrl, package.id);
            }
        }

        [Test]
        public void CoreStateInstalledDetectionUsesRealPackageId()
        {
            PackageDefinition coreState = PackageRegistryProvider
                .CreatePackageDefinitions(new PackageRegistry
                {
                    schemaVersion = 1,
                    packages = new[]
                    {
                        new PackageRegistryEntry
                        {
                            id = "com.deucarian.core-state",
                            displayName = "Deucarian Core State",
                            category = "Core",
                            stableUrl = "https://github.com/Deucarian/Core-State.git#main",
                            dependencies = Array.Empty<string>()
                        }
                    }
                })
                .Single(package => package.PackageId == "com.deucarian.core-state");

            using (PackageDetectionService detectionService = new PackageDetectionService())
            {
                detectionService.ReplaceInstalledPackageNamesForTests(new[] { "com.deucarian.core-state" });

                Assert.IsTrue(detectionService.IsInstalled(coreState.PackageId));
                Assert.IsFalse(detectionService.IsInstalled("com.deucarian.core-state-legacy"));
            }
        }

        [Test]
        public void PackageJsonSamplesParse()
        {
            string packageJson =
                "{ \"name\": \"com.example.samples\", \"version\": \"1.2.3\", \"samples\": [" +
                "{ \"displayName\": \"Scene Setup\", \"description\": \"A ready-to-open setup scene.\", \"path\": \"Samples~/Scene Setup\" }," +
                "{ \"displayName\": \"Runtime Demo\", \"description\": \"Runtime sample content.\", \"path\": \"Samples~/Runtime Demo\" }" +
                "] }";

            PackageExtraDefinition[] samples = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .ToArray();

            Assert.AreEqual(2, samples.Length);
            Assert.AreEqual("Scene Setup", samples[0].DisplayName);
            Assert.AreEqual("A ready-to-open setup scene.", samples[0].Description);
            Assert.AreEqual("Samples~/Scene Setup", samples[0].SamplePath);
            Assert.AreEqual("Runtime Demo", samples[1].DisplayName);
        }

        [Test]
        public void PackageJsonSampleWithoutDisplayNameUsesPathFolder()
        {
            string packageJson =
                "{ \"samples\": [" +
                "{ \"description\": \"Missing a displayName.\", \"path\": \"Samples~/Path Named Sample\" }" +
                "] }";

            PackageExtraDefinition sample = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .Single();

            Assert.AreEqual("Path Named Sample", sample.DisplayName);
            Assert.AreEqual("Missing a displayName.", sample.Description);
            Assert.AreEqual("Samples~/Path Named Sample", sample.SamplePath);
        }

        [Test]
        public void PackageJsonSamplesSkipDuplicates()
        {
            string packageJson =
                "{ \"samples\": [" +
                "{ \"displayName\": \"Duplicate\", \"path\": \"Samples~/Duplicate\" }," +
                "{ \"displayName\": \"Duplicate Alias\", \"path\": \"Samples~/Duplicate\" }" +
                "] }";

            PackageExtraDefinition[] samples = PackageSampleManifestParser
                .ParseSamples(packageJson)
                .ToArray();

            Assert.AreEqual(1, samples.Length);
        }

        private static string GetBundledRegistryPath()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(PackageRegistryLoader).Assembly);
            Assert.IsNotNull(packageInfo);
            Assert.IsFalse(string.IsNullOrWhiteSpace(packageInfo.resolvedPath));
            return Path.Combine(packageInfo.resolvedPath, PackageRegistryLoader.BundledRegistryFileName);
        }

        private static void RunAsync(Func<Task> asyncTest)
        {
            asyncTest().GetAwaiter().GetResult();
        }
    }
}
