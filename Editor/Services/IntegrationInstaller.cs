using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class IntegrationInstaller
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;
        private readonly ScriptingDefineService _scriptingDefineService;

        public IntegrationInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService,
            ScriptingDefineService scriptingDefineService)
        {
            _packageInstallService = packageInstallService ?? throw new ArgumentNullException(nameof(packageInstallService));
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _scriptingDefineService = scriptingDefineService ?? throw new ArgumentNullException(nameof(scriptingDefineService));
        }

        public void InstallPackage(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!packageDefinition.HasPackageReference)
            {
                Debug.LogWarning(LogPrefix + " " + packageDefinition.DisplayName + " is not a standalone installable package.");
                return;
            }

            if (_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                Debug.Log(LogPrefix + " " + packageDefinition.DisplayName + " is already installed.");
                return;
            }

            _packageInstallService.Install(packageDefinition);
        }

        public void InstallIntegration(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return;
            }

            IEnumerable<PackageDefinition> missingDependencies = PackageRegistry
                .GetInstallableDependencies(integrationDefinition)
                .Where(dependency => !_packageDetectionService.IsInstalled(dependency.PackageId));

            _packageInstallService.InstallMany(missingDependencies);
            _scriptingDefineService.AddSymbolsToSelectedBuildTargetGroup(integrationDefinition.ScriptingDefineSymbols);

            Debug.Log(LogPrefix + " Processed integration " + integrationDefinition.DisplayName + ".");
        }

        public void InstallAll()
        {
            IEnumerable<PackageDefinition> missingPackages = PackageRegistry.StandalonePackages
                .Where(package => !_packageDetectionService.IsInstalled(package.PackageId));

            _packageInstallService.InstallMany(missingPackages);

            foreach (PackageDefinition integration in PackageRegistry.Integrations)
            {
                _scriptingDefineService.AddSymbolsToSelectedBuildTargetGroup(integration.ScriptingDefineSymbols);
            }

            Debug.Log(LogPrefix + " Processed Install All.");
        }

        public bool ArePackageDependenciesInstalled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return integrationDefinition.Dependencies.All(_packageDetectionService.IsInstalled);
        }

        public bool AreIntegrationSymbolsEnabled(PackageDefinition integrationDefinition)
        {
            if (integrationDefinition == null)
            {
                return false;
            }

            return _scriptingDefineService.HasSymbols(
                _scriptingDefineService.SelectedBuildTargetGroup,
                integrationDefinition.ScriptingDefineSymbols);
        }

        public bool IsIntegrationComplete(PackageDefinition integrationDefinition)
        {
            return ArePackageDependenciesInstalled(integrationDefinition) && AreIntegrationSymbolsEnabled(integrationDefinition);
        }
    }
}
