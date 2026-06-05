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
        public const string GenericUIItemsCoreStateBridgePackageId = "com.jorishoef.generic-ui-items.core-state-bridge";
        public const string SessionHelperAPIHelperBridgePackageId = "com.jorishoef.session-helper.api-helper-bridge";

        private static readonly PackageDefinition[] CorePackageDefinitions =
        {
            new PackageDefinition(
                "Core State",
                CoreStatePackageId,
                "https://github.com/JorisHoef/Core-State.git#main",
                "Small, standalone repository and selection services for Unity projects.",
                packageType: PackageType.Core,
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
                "API Helper",
                APIHelperPackageId,
                "https://github.com/JorisHoef/API-Helper.git#main",
                "Reusable API client package for JSON, text, bytes, textures, and endpoint workflows.",
                packageType: PackageType.Core,
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
                packageType: PackageType.Core,
                developmentUrl: "https://github.com/JorisHoef/Session-Helper.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic Session Usage",
                        "Minimal fake login, restore, refresh, logout, and store clearing scene using standalone SessionHelper APIs.",
                        samplePath: "Samples~/BasicUsage")
                })
        };

        private static readonly PackageDefinition[] UiPackageDefinitions =
        {
            new PackageDefinition(
                "Generic UI Items",
                GenericUIItemsPackageId,
                "https://github.com/JorisHoef/GenericUIItems.git#develop",
                "Lightweight UGUI collection-to-item presentation helpers.",
                packageType: PackageType.UI,
                developmentUrl: "https://github.com/JorisHoef/GenericUIItems.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic Usage",
                        "Example scene showing list, scroll view, add, update, remove, clear, and identity-based synchronization.",
                        samplePath: "Samples~/BasicUsage")
                })
        };

        private static readonly PackageDefinition[] BridgePackageDefinitions =
        {
            new PackageDefinition(
                "GenericUIItems CoreState Bridge",
                GenericUIItemsCoreStateBridgePackageId,
                "https://github.com/JorisHoef/GenericUIItems-CoreState-Bridge.git#main",
                "Bridge package that binds Core State repositories and selection services to Generic UI Items containers.",
                new[] { GenericUIItemsPackageId, CoreStatePackageId },
                PackageType.Bridge,
                developmentUrl: "https://github.com/JorisHoef/GenericUIItems-CoreState-Bridge.git#develop",
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic Usage",
                        "Example scene scripts showing a Core State repository and selection service bound to a Generic UI Items container.",
                        samplePath: "Samples~/BasicUsage")
                }),

            new PackageDefinition(
                "SessionHelper APIHelper Bridge",
                SessionHelperAPIHelperBridgePackageId,
                "https://github.com/JorisHoef/SessionHelper-APIHelper-Bridge.git#main",
                "Bridge package that exposes Session Helper tokens through APIHelper authentication.",
                new[] { SessionHelperPackageId, APIHelperPackageId },
                PackageType.Bridge,
                displayVersion: "1.0.0",
                extras: new[]
                {
                    new PackageExtraDefinition(
                        "Basic APIHelper Bridge Usage",
                        "Minimal sample showing Session Helper access tokens supplied to APIHelper through SessionAuthProvider.",
                        samplePath: "Samples~/BasicUsage")
                })
        };

        private static readonly PackageDefinition[] AllPackageDefinitions =
            CorePackageDefinitions
                .Concat(UiPackageDefinitions)
                .Concat(BridgePackageDefinitions)
                .ToArray();

        private static readonly PackageDefinition[] StandalonePackageDefinitions =
            CorePackageDefinitions.Concat(UiPackageDefinitions).ToArray();

        public static IReadOnlyList<PackageDefinition> StandalonePackages => StandalonePackageDefinitions;

        public static IReadOnlyList<PackageDefinition> CorePackages => CorePackageDefinitions;

        public static IReadOnlyList<PackageDefinition> UiPackages => UiPackageDefinitions;

        public static IReadOnlyList<PackageDefinition> BridgePackages => BridgePackageDefinitions;

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
