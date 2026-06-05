using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace JorisHoef.PackageInstaller.Editor.Tests
{
    internal sealed class PackageRegistryTests
    {
        private const string ValidRegistryJson =
            "{ \"schemaVersion\": 1, \"updatedAt\": \"2026-06-05\", \"packages\": [" +
            "{ \"id\": \"com.jorishoef.core-state\", \"displayName\": \"JorisHoef Core State\", \"category\": \"Core\", \"description\": \"Core package.\", \"stableUrl\": \"https://example.com/core.git#main\", \"developmentUrl\": \"https://example.com/core.git#develop\", \"dependencies\": [] }," +
            "{ \"id\": \"com.jorishoef.core-state.bridge\", \"displayName\": \"Core Bridge\", \"category\": \"Bridge\", \"description\": \"Bridge package.\", \"stableUrl\": \"https://example.com/bridge.git#main\", \"developmentUrl\": \"https://example.com/bridge.git#develop\", \"dependencies\": [\"com.jorishoef.core-state\"] }" +
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
                "{ \"id\": \"com.jorishoef.duplicate\", \"displayName\": \"One\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/one.git#main\", \"dependencies\": [] }," +
                "{ \"id\": \"com.jorishoef.duplicate\", \"displayName\": \"Two\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/two.git#main\", \"dependencies\": [] }" +
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
                "{ \"id\": \"com.jorishoef.bridge\", \"displayName\": \"Bridge\", \"category\": \"Bridge\", \"stableUrl\": \"https://example.com/bridge.git#main\", \"dependencies\": [\"com.jorishoef.missing\"] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("depends on unknown package id", result.ErrorMessage);
        }

        [Test]
        public void MissingDevelopmentUrlDisablesDevelopmentChannel()
        {
            string json =
                "{ \"schemaVersion\": 1, \"packages\": [" +
                "{ \"id\": \"com.jorishoef.stable-only\", \"displayName\": \"Stable Only\", \"category\": \"Core\", \"stableUrl\": \"https://example.com/stable.git#main\", \"dependencies\": [] }" +
                "] }";

            PackageRegistryLoadResult result = new PackageRegistryLoader()
                .LoadFromJson(json, PackageRegistrySource.Bundled);
            PackageDefinition packageDefinition = PackageRegistryProvider
                .CreatePackageDefinitions(result.Registry)
                .Single();

            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.IsFalse(packageDefinition.HasDevelopmentUrl);
            Assert.AreEqual("https://example.com/stable.git#main", packageDefinition.GetUrl(PackageChannel.Stable));
        }

        [Test]
        public async Task RemoteFailureKeepsBundledRegistry()
        {
            PackageRegistry bundledRegistry = new PackageRegistry
            {
                schemaVersion = 1,
                packages = new[]
                {
                    new PackageRegistryEntry
                    {
                        id = "com.jorishoef.core-state",
                        displayName = "JorisHoef Core State",
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
        }
    }
}
