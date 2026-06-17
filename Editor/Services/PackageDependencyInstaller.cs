using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageDependencyInstallStep
    {
        public PackageDependencyInstallStep(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            bool isDependency)
        {
            PackageDefinition = packageDefinition ?? throw new ArgumentNullException(nameof(packageDefinition));
            Channel = channel;
            IsDependency = isDependency;
        }

        public PackageDefinition PackageDefinition { get; }

        public PackageChannel Channel { get; }

        public bool IsDependency { get; }
    }

    internal sealed class PackageDependencyInstallPlan
    {
        private readonly Dictionary<string, PackageChannel> _channelsByPackageId;

        private PackageDependencyInstallPlan(
            bool isValid,
            IEnumerable<PackageDependencyInstallStep> steps,
            IEnumerable<string> messages,
            string errorMessage)
        {
            IsValid = isValid;
            Steps = (steps ?? Array.Empty<PackageDependencyInstallStep>()).ToArray();
            Messages = (messages ?? Array.Empty<string>()).ToArray();
            ErrorMessage = errorMessage ?? string.Empty;

            _channelsByPackageId = Steps
                .GroupBy(step => step.PackageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Channel,
                    StringComparer.OrdinalIgnoreCase);
        }

        public bool IsValid { get; }

        public IReadOnlyList<PackageDependencyInstallStep> Steps { get; }

        public IReadOnlyList<string> Messages { get; }

        public string ErrorMessage { get; }

        public PackageDefinition[] Packages => Steps.Select(step => step.PackageDefinition).ToArray();

        public PackageChannel GetChannel(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            return _channelsByPackageId.TryGetValue(packageDefinition.PackageId, out PackageChannel channel)
                ? channel
                : PackageChannel.Stable;
        }

        public static PackageDependencyInstallPlan Success(
            IEnumerable<PackageDependencyInstallStep> steps,
            IEnumerable<string> messages)
        {
            return new PackageDependencyInstallPlan(true, steps, messages, string.Empty);
        }

        public static PackageDependencyInstallPlan Failure(
            string errorMessage,
            IEnumerable<string> messages)
        {
            return new PackageDependencyInstallPlan(
                false,
                Array.Empty<PackageDependencyInstallStep>(),
                messages,
                errorMessage);
        }
    }

    internal sealed class PackageDependencyInstaller
    {
        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;
        private readonly Func<IEnumerable<PackageDefinition>> _registeredPackagesProvider;

        public PackageDependencyInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService)
            : this(
                packageInstallService,
                packageDetectionService,
                () => PackageRegistryProvider.All)
        {
        }

        internal PackageDependencyInstaller(
            PackageInstallService packageInstallService,
            PackageDetectionService packageDetectionService,
            Func<IEnumerable<PackageDefinition>> registeredPackagesProvider)
        {
            _packageInstallService = packageInstallService ?? throw new ArgumentNullException(nameof(packageInstallService));
            _packageDetectionService = packageDetectionService ?? throw new ArgumentNullException(nameof(packageDetectionService));
            _registeredPackagesProvider = registeredPackagesProvider ?? (() => PackageRegistryProvider.All);
        }

        public void InstallWithDependencies(
            PackageDefinition packageDefinition,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            StartDependencyAwareOperation(
                new[] { packageDefinition },
                channelSelector,
                "Install " + (packageDefinition != null ? packageDefinition.DisplayName : "Package"),
                includeInstalledRequestedPackages: false,
                alreadyInstalledMessage: packageDefinition != null
                    ? packageDefinition.DisplayName + " and its dependencies are already installed."
                    : "Package and its dependencies are already installed.");
        }

        public void UpdateWithDependencies(
            PackageDefinition packageDefinition,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            StartDependencyAwareOperation(
                new[] { packageDefinition },
                channelSelector,
                "Update " + (packageDefinition != null ? packageDefinition.DisplayName : "Package"),
                includeInstalledRequestedPackages: true,
                alreadyInstalledMessage: packageDefinition != null
                    ? packageDefinition.DisplayName + " has no packages to update."
                    : "No packages to update.");
        }

        public void ReinstallWithDependencies(
            PackageDefinition packageDefinition,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            StartDependencyAwareOperation(
                new[] { packageDefinition },
                channelSelector,
                "Reinstall " + (packageDefinition != null ? packageDefinition.DisplayName : "Package"),
                includeInstalledRequestedPackages: true,
                alreadyInstalledMessage: packageDefinition != null
                    ? packageDefinition.DisplayName + " has no packages to reinstall."
                    : "No packages to reinstall.");
        }

        public void InstallAll(Func<PackageDefinition, PackageChannel> channelSelector)
        {
            StartDependencyAwareOperation(
                GetRegisteredPackages(),
                channelSelector,
                "Install All Packages",
                includeInstalledRequestedPackages: false,
                alreadyInstalledMessage: "All registered packages are already installed.");
        }

        public void UpdateAll(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            StartDependencyAwareOperation(
                packageDefinitions,
                channelSelector,
                "Update All Installed Packages",
                includeInstalledRequestedPackages: true,
                alreadyInstalledMessage: "No installed packages need updates.");
        }

        public PackageDefinition[] CreateInstallPlan(PackageDefinition packageDefinition)
        {
            return CreateInstallPlan(new[] { packageDefinition });
        }

        public PackageDefinition[] CreateInstallPlan(IEnumerable<PackageDefinition> packageDefinitions)
        {
            PackageDependencyInstallPlan plan = CreateInstallPlan(
                packageDefinitions,
                _ => PackageChannel.Stable,
                includeInstalledRequestedPackages: false);

            return plan.IsValid ? plan.Packages : Array.Empty<PackageDefinition>();
        }

        internal PackageDependencyInstallPlan CreateInstallPlan(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            bool includeInstalledRequestedPackages)
        {
            List<PackageDependencyInstallStep> installPlan = new List<PackageDependencyInstallStep>();
            List<string> messages = new List<string>();
            HashSet<string> plannedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visitedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visitingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<PackageDefinition> visitStack = new List<PackageDefinition>();

            PackageDefinition[] requestedPackages = (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .GroupBy(packageDefinition => packageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            foreach (PackageDefinition packageDefinition in requestedPackages)
            {
                PackageChannel requestedChannel = SelectRequestedChannel(packageDefinition, channelSelector);

                if (!AddPackageToPlan(
                        packageDefinition,
                        packageDefinition,
                        requestedChannel,
                        requestedChannel,
                        isDependency: false,
                        includeInstalledRequestedPackages: includeInstalledRequestedPackages,
                        installPlan: installPlan,
                        plannedPackageIds: plannedPackageIds,
                        visitedPackageIds: visitedPackageIds,
                        visitingPackageIds: visitingPackageIds,
                        visitStack: visitStack,
                        messages: messages,
                        out string errorMessage))
                {
                    return PackageDependencyInstallPlan.Failure(errorMessage, messages);
                }
            }

            return PackageDependencyInstallPlan.Success(installPlan, messages);
        }

        public bool AreDependenciesInstalled(PackageDefinition packageDefinition)
        {
            return packageDefinition == null ||
                   packageDefinition.Dependencies.All(_packageDetectionService.IsInstalled);
        }

        public PackageDefinition[] GetMissingDependencies(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return Array.Empty<PackageDefinition>();
            }

            return packageDefinition.Dependencies
                .Where(packageId => !_packageDetectionService.IsInstalled(packageId))
                .Select(packageId =>
                    TryGetRegisteredPackage(packageId, out PackageDefinition dependency)
                        ? dependency
                        : null)
                .Where(dependency => dependency != null)
                .ToArray();
        }

        public PackageDefinition[] GetInstalledDependents(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return Array.Empty<PackageDefinition>();
            }

            return GetRegisteredPackages()
                .Where(candidate => candidate != null &&
                                    !string.Equals(
                                        candidate.PackageId,
                                        packageDefinition.PackageId,
                                        StringComparison.OrdinalIgnoreCase) &&
                                    candidate.Dependencies.Any(dependencyId =>
                                        string.Equals(
                                            dependencyId,
                                            packageDefinition.PackageId,
                                            StringComparison.OrdinalIgnoreCase)) &&
                                    _packageDetectionService.IsInstalled(candidate.PackageId))
                .ToArray();
        }

        private void StartDependencyAwareOperation(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            bool includeInstalledRequestedPackages,
            string alreadyInstalledMessage)
        {
            PackageDependencyInstallPlan installPlan = CreateInstallPlan(
                packageDefinitions,
                channelSelector,
                includeInstalledRequestedPackages);

            LogMessages(installPlan.Messages);

            if (!installPlan.IsValid)
            {
                PackageInstallerLog.Install.Error(installPlan.ErrorMessage);
                return;
            }

            if (installPlan.Steps.Count == 0)
            {
                _packageInstallService.RecordCompletedOperation(
                    operationName,
                    alreadyInstalledMessage,
                    installPlan.Messages);
                PackageInstallerLog.Install.Info(alreadyInstalledMessage);
                return;
            }

            _packageInstallService.InstallMany(
                installPlan.Packages,
                installPlan.GetChannel,
                operationName,
                installPlan.Messages);
        }

        private bool AddPackageToPlan(
            PackageDefinition packageDefinition,
            PackageDefinition requestedPackage,
            PackageChannel requestedChannel,
            PackageChannel packageChannel,
            bool isDependency,
            bool includeInstalledRequestedPackages,
            ICollection<PackageDependencyInstallStep> installPlan,
            ISet<string> plannedPackageIds,
            ISet<string> visitedPackageIds,
            ISet<string> visitingPackageIds,
            IList<PackageDefinition> visitStack,
            ICollection<string> messages,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return true;
            }

            if (plannedPackageIds.Contains(packageDefinition.PackageId))
            {
                if (isDependency)
                {
                    messages.Add("Skipped dependency " + packageDefinition.DisplayName + "; already queued.");
                }

                return true;
            }

            if (visitingPackageIds.Contains(packageDefinition.PackageId))
            {
                errorMessage = "Circular dependency detected: " + FormatCycle(visitStack, packageDefinition) + ".";
                return false;
            }

            if (visitedPackageIds.Contains(packageDefinition.PackageId))
            {
                return true;
            }

            visitingPackageIds.Add(packageDefinition.PackageId);
            visitStack.Add(packageDefinition);

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (!TryGetRegisteredPackage(dependencyId, out PackageDefinition dependency) ||
                    !dependency.HasPackageReference)
                {
                    errorMessage = "Cannot install " + requestedPackage.DisplayName +
                                   " because dependency " + GetDependencyName(dependency, dependencyId) +
                                   " is unavailable.";
                    return false;
                }

                PackageChannel dependencyChannel = ResolveDependencyChannel(
                    dependency,
                    requestedChannel,
                    requestedPackage,
                    messages);

                if (!AddPackageToPlan(
                        dependency,
                        requestedPackage,
                        requestedChannel,
                        dependencyChannel,
                        isDependency: true,
                        includeInstalledRequestedPackages: includeInstalledRequestedPackages,
                        installPlan: installPlan,
                        plannedPackageIds: plannedPackageIds,
                        visitedPackageIds: visitedPackageIds,
                        visitingPackageIds: visitingPackageIds,
                        visitStack: visitStack,
                        messages: messages,
                        out errorMessage))
                {
                    return false;
                }
            }

            visitingPackageIds.Remove(packageDefinition.PackageId);
            visitStack.RemoveAt(visitStack.Count - 1);
            visitedPackageIds.Add(packageDefinition.PackageId);

            if (!ShouldInstallPackage(
                    packageDefinition,
                    packageChannel,
                    isDependency,
                    includeInstalledRequestedPackages))
            {
                if (isDependency)
                {
                    messages.Add("Skipped dependency " + packageDefinition.DisplayName + "; already installed.");
                }

                return true;
            }

            string packageUrl = packageDefinition.GetUrl(packageChannel);

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                errorMessage = "Cannot install " + requestedPackage.DisplayName +
                               " because dependency " + packageDefinition.DisplayName +
                               " has no " + packageChannel + " package URL.";
                return false;
            }

            if (isDependency)
            {
                messages.Add("Installing dependency " + packageDefinition.DisplayName +
                             " before " + requestedPackage.DisplayName + ".");
            }

            installPlan.Add(new PackageDependencyInstallStep(packageDefinition, packageChannel, isDependency));
            plannedPackageIds.Add(packageDefinition.PackageId);
            return true;
        }

        private bool ShouldInstallPackage(
            PackageDefinition packageDefinition,
            PackageChannel targetChannel,
            bool isDependency,
            bool includeInstalledRequestedPackages)
        {
            if (packageDefinition == null)
            {
                return false;
            }

            bool isInstalledAtTargetOrNewer = IsInstalledAtTargetOrNewer(packageDefinition, targetChannel);

            if (isDependency)
            {
                return !isInstalledAtTargetOrNewer;
            }

            return includeInstalledRequestedPackages || !isInstalledAtTargetOrNewer;
        }

        private bool IsInstalledAtTargetOrNewer(PackageDefinition packageDefinition, PackageChannel targetChannel)
        {
            if (!_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                return false;
            }

            if (!_packageDetectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel installedChannel,
                    out _))
            {
                return true;
            }

            if (installedChannel == targetChannel)
            {
                return true;
            }

            return installedChannel == PackageChannel.Development &&
                   targetChannel == PackageChannel.Stable;
        }

        private PackageChannel ResolveDependencyChannel(
            PackageDefinition dependency,
            PackageChannel requestedChannel,
            PackageDefinition requestedPackage,
            ICollection<string> messages)
        {
            if (requestedChannel == PackageChannel.Development)
            {
                if (dependency.HasDevelopmentUrl)
                {
                    return PackageChannel.Development;
                }

                messages.Add("Dependency " + dependency.DisplayName +
                             " has no Development channel; falling back to Stable before installing " +
                             requestedPackage.DisplayName + ".");
                return PackageChannel.Stable;
            }

            if (requestedChannel == PackageChannel.Custom)
            {
                messages.Add("Dependency " + dependency.DisplayName +
                             " has no Custom channel; falling back to Stable before installing " +
                             requestedPackage.DisplayName + ".");
            }

            return PackageChannel.Stable;
        }

        private PackageChannel SelectRequestedChannel(
            PackageDefinition packageDefinition,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            if (packageDefinition == null || channelSelector == null)
            {
                return PackageChannel.Stable;
            }

            return channelSelector(packageDefinition);
        }

        private IEnumerable<PackageDefinition> GetRegisteredPackages()
        {
            return _registeredPackagesProvider() ?? Array.Empty<PackageDefinition>();
        }

        private bool TryGetRegisteredPackage(string packageId, out PackageDefinition packageDefinition)
        {
            packageDefinition = GetRegisteredPackages().FirstOrDefault(definition =>
                definition != null &&
                string.Equals(definition.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

            return packageDefinition != null;
        }

        private static string GetDependencyName(PackageDefinition dependency, string dependencyId)
        {
            if (dependency != null && !string.IsNullOrWhiteSpace(dependency.DisplayName))
            {
                return dependency.DisplayName;
            }

            return string.IsNullOrWhiteSpace(dependencyId) ? "unknown" : dependencyId;
        }

        private static string FormatCycle(
            IEnumerable<PackageDefinition> visitStack,
            PackageDefinition repeatedPackage)
        {
            List<PackageDefinition> stack = (visitStack ?? Array.Empty<PackageDefinition>()).ToList();
            int cycleStart = stack.FindIndex(packageDefinition =>
                packageDefinition != null &&
                repeatedPackage != null &&
                string.Equals(
                    packageDefinition.PackageId,
                    repeatedPackage.PackageId,
                    StringComparison.OrdinalIgnoreCase));

            IEnumerable<PackageDefinition> cycle = cycleStart >= 0
                ? stack.Skip(cycleStart)
                : stack;

            return string.Join(
                " -> ",
                cycle
                    .Concat(new[] { repeatedPackage })
                    .Where(packageDefinition => packageDefinition != null)
                    .Select(packageDefinition => packageDefinition.DisplayName)
                    .ToArray());
        }

        private static void LogMessages(IEnumerable<string> messages)
        {
            foreach (string message in messages ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    PackageInstallerLog.Install.Info(message);
                }
            }
        }
    }
}
