using System;
using System.Collections.Generic;
using System.Linq;

namespace JorisHoef.PackageInstaller.Editor
{
    internal static class PackageRegistry
    {
        public const string CoreStatePackageId = "com.jorishoef.core.state";
        public const string GenericUIItemsPackageId = "com.jorishoef.generic-ui-items";
        public const string APIHelperPackageId = "com.jorishoef.api-helper";
        public const string SessionHelperPackageId = "com.jorishoef.session-helper";

        public const string GenericUIItemsCoreStateSymbol = "GENERIC_UI_ITEMS_CORE_STATE";
        public const string SessionHelperAPIHelperSymbol = "SESSION_HELPER_APIHELPER";

        private static readonly PackageDefinition[] StandalonePackageDefinitions =
        {
            new PackageDefinition(
                "Core State",
                CoreStatePackageId,
                "https://github.com/JorisHoef/Core-State.git#main",
                "Small, standalone repository and selection services for Unity projects.",
                developmentUrl: "https://github.com/JorisHoef/Core-State.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Standalone Repository Selection",
                        "Simple IMGUI scene showing repository-backed key selection without any UI package dependency.",
                        samplePath: "Samples~/StandaloneRepositorySelection")
                }),

            new PackageDefinition(
                "Generic UI Items",
                GenericUIItemsPackageId,
                "https://github.com/JorisHoef/GenericUIItems.git#develop",
                "Lightweight UGUI collection-to-item presentation helpers.",
                developmentUrl: "https://github.com/JorisHoef/GenericUIItems.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic Usage",
                        "Example scene showing list, scroll view, add, update, remove, clear, and identity-based synchronization.",
                        samplePath: "Samples~/BasicUsage")
                }),

            new PackageDefinition(
                "API Helper",
                APIHelperPackageId,
                "https://github.com/JorisHoef/API-Helper.git#main",
                "Reusable API client package for JSON, text, bytes, textures, and endpoint workflows.",
                developmentUrl: "https://github.com/JorisHoef/API-Helper.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "API Helper Example Scene",
                        "Example scene showing config, authentication, GET, POST, and ApiResult handling.",
                        samplePath: "Samples~/ExampleScene")
                }),

            new PackageDefinition(
                "Session Helper",
                SessionHelperPackageId,
                "https://github.com/JorisHoef/Session-Helper.git#main",
                "Standalone authenticated-session lifecycle helpers with storage, restore, refresh, and change notifications.",
                developmentUrl: "https://github.com/JorisHoef/Session-Helper.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic Session Usage",
                        "Minimal fake login, restore, refresh, logout, and store clearing scene using standalone SessionHelper APIs.",
                        samplePath: "Samples~/BasicUsage"),
                    new PackageExtraDefinition(
                        "APIHelper Integration",
                        "Minimal code sample showing how to pass SessionHelper tokens to APIHelper through IApiAuthProvider.",
                        samplePath: "Samples~/APIHelperIntegration")
                })
        };

        private static readonly PackageDefinition[] IntegrationPackageDefinitions =
        {
            new PackageDefinition(
                "Generic UI Items + Core State integration",
                "com.jorishoef.integration.generic-ui-items-core-state",
                string.Empty,
                "Installs Generic UI Items and Core State, then enables their optional integration define symbol.",
                new[] { GenericUIItemsPackageId, CoreStatePackageId },
                new[] { GenericUIItemsCoreStateSymbol },
                true),

            new PackageDefinition(
                "Session Helper + API Helper integration",
                "com.jorishoef.integration.session-helper-api-helper",
                string.Empty,
                "Installs Session Helper and API Helper, then enables the Session Helper API Helper adapter symbol.",
                new[] { SessionHelperPackageId, APIHelperPackageId },
                new[] { SessionHelperAPIHelperSymbol },
                true)
        };

        private static readonly PackageDefinition[] AllPackageDefinitions =
            StandalonePackageDefinitions.Concat(IntegrationPackageDefinitions).ToArray();

        public static IReadOnlyList<PackageDefinition> StandalonePackages => StandalonePackageDefinitions;

        public static IReadOnlyList<PackageDefinition> Integrations => IntegrationPackageDefinitions;

        public static IReadOnlyList<PackageDefinition> All => AllPackageDefinitions;

        public static bool TryGetPackage(string packageId, out PackageDefinition packageDefinition)
        {
            packageDefinition = AllPackageDefinitions.FirstOrDefault(definition =>
                string.Equals(definition.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

            return packageDefinition != null;
        }

        public static IEnumerable<PackageDefinition> GetInstallableDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                yield break;
            }

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (TryGetPackage(dependencyId, out PackageDefinition dependency) && dependency.HasPackageReference)
                {
                    yield return dependency;
                }
            }
        }
    }
}
