using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Deucarian.PackageInstaller.Editor
{
    [Flags]
    internal enum PackageOperationRisk
    {
        None = 0,
        MultiStep = 1 << 0,
        SourceOrChannelMigration = 1 << 1,
        Downgrade = 1 << 2,
        ChannelFallback = 1 << 3,
        Conflict = 1 << 4,
        Destructive = 1 << 5
    }

    internal sealed class PackageDependencyInstallStep
    {
        public PackageDependencyInstallStep(
            PackageDefinition packageDefinition,
            PackageChannel channel,
            bool isDependency,
            string targetUrl = null,
            IEnumerable<string> prerequisitePackageIds = null,
            IEnumerable<string> rootPackageIds = null,
            IEnumerable<string> rootPaths = null,
            string dependencyReason = null,
            string detectedCurrentSource = null,
            string detectedCurrentVersion = null,
            string detectedCurrentIdentity = null)
        {
            PackageDefinition = packageDefinition ?? throw new ArgumentNullException(nameof(packageDefinition));
            Channel = channel;
            IsDependency = isDependency;
            TargetUrl = string.IsNullOrWhiteSpace(targetUrl)
                ? packageDefinition.GetUrl(channel)
                : targetUrl.Trim();
            PrerequisitePackageIds = ToReadOnlyList(prerequisitePackageIds);
            RootPackageIds = ToReadOnlyList(rootPackageIds);
            RootPaths = ToReadOnlyList(rootPaths);
            DependencyReason = dependencyReason ?? string.Empty;
            DetectedCurrentSource = detectedCurrentSource ?? string.Empty;
            DetectedCurrentVersion = detectedCurrentVersion ?? string.Empty;
            DetectedCurrentIdentity = detectedCurrentIdentity ?? string.Empty;
        }

        public PackageDefinition PackageDefinition { get; }

        public PackageChannel Channel { get; }

        public bool IsDependency { get; }

        public string TargetUrl { get; }

        public IReadOnlyList<string> PrerequisitePackageIds { get; }

        public IReadOnlyList<string> RootPackageIds { get; }

        public IReadOnlyList<string> RootPaths { get; }

        public string DependencyReason { get; }

        public string DetectedCurrentSource { get; }

        public string DetectedCurrentVersion { get; }

        public string DetectedCurrentIdentity { get; }

        private static IReadOnlyList<string> ToReadOnlyList(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    internal sealed class PackageDependencyInstallPlan
    {
        private readonly Dictionary<string, PackageChannel> _channelsByPackageId;

        private PackageDependencyInstallPlan(
            bool isValid,
            IEnumerable<PackageDependencyInstallStep> steps,
            IEnumerable<string> messages,
            string errorMessage,
            string operationId,
            string registryFingerprint,
            long createdAtUtcTicks,
            PackageOperationRisk risks,
            IEnumerable<PackageOperationRootRequest> rootRequests)
        {
            IsValid = isValid;
            Steps = (steps ?? Array.Empty<PackageDependencyInstallStep>()).ToArray();
            Messages = (messages ?? Array.Empty<string>()).ToArray();
            ErrorMessage = errorMessage ?? string.Empty;
            OperationId = string.IsNullOrWhiteSpace(operationId)
                ? Guid.NewGuid().ToString("N")
                : operationId;
            RegistryFingerprint = registryFingerprint ?? string.Empty;
            CreatedAtUtcTicks = createdAtUtcTicks > 0 ? createdAtUtcTicks : DateTime.UtcNow.Ticks;
            Risks = Steps.Count > 1
                ? risks | PackageOperationRisk.MultiStep
                : risks;
            RootRequests = NormalizeRootRequests(rootRequests, Steps);

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

        public string OperationId { get; }

        public string RegistryFingerprint { get; }

        public long CreatedAtUtcTicks { get; }

        public PackageOperationRisk Risks { get; }

        public IReadOnlyList<PackageOperationRootRequest> RootRequests { get; }

        public bool IsMultiStep => (Risks & PackageOperationRisk.MultiStep) != 0;

        public bool HasMigrationRisk =>
            (Risks & PackageOperationRisk.SourceOrChannelMigration) != 0;

        public bool HasDowngradeRisk => (Risks & PackageOperationRisk.Downgrade) != 0;

        public bool HasChannelFallback => (Risks & PackageOperationRisk.ChannelFallback) != 0;

        public bool HasConflict => (Risks & PackageOperationRisk.Conflict) != 0;

        public bool HasDestructiveRisk => (Risks & PackageOperationRisk.Destructive) != 0;

        public bool RequiresPreflight => Risks != PackageOperationRisk.None;

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

        internal PackageDependencyInstallPlan WithAdditionalRisks(
            PackageOperationRisk additionalRisks)
        {
            if (additionalRisks == PackageOperationRisk.None ||
                (Risks & additionalRisks) == additionalRisks)
            {
                return this;
            }

            return new PackageDependencyInstallPlan(
                IsValid,
                Steps,
                Messages,
                ErrorMessage,
                OperationId,
                RegistryFingerprint,
                CreatedAtUtcTicks,
                Risks | additionalRisks,
                RootRequests);
        }

        public static PackageDependencyInstallPlan Success(
            IEnumerable<PackageDependencyInstallStep> steps,
            IEnumerable<string> messages,
            string registryFingerprint = "",
            PackageOperationRisk risks = PackageOperationRisk.None,
            IEnumerable<PackageOperationRootRequest> rootRequests = null)
        {
            return new PackageDependencyInstallPlan(
                true,
                steps,
                messages,
                string.Empty,
                string.Empty,
                registryFingerprint,
                DateTime.UtcNow.Ticks,
                risks,
                rootRequests);
        }

        public static PackageDependencyInstallPlan Failure(
            string errorMessage,
            IEnumerable<string> messages,
            PackageOperationRisk risks = PackageOperationRisk.None,
            IEnumerable<PackageOperationRootRequest> rootRequests = null)
        {
            return new PackageDependencyInstallPlan(
                false,
                Array.Empty<PackageDependencyInstallStep>(),
                messages,
                errorMessage,
                string.Empty,
                string.Empty,
                DateTime.UtcNow.Ticks,
                risks,
                rootRequests);
        }

        internal static PackageDependencyInstallPlan Restore(
            string operationId,
            string registryFingerprint,
            long createdAtUtcTicks,
            IEnumerable<PackageDependencyInstallStep> steps,
            IEnumerable<string> messages,
            IEnumerable<PackageOperationRootRequest> rootRequests = null)
        {
            return new PackageDependencyInstallPlan(
                true,
                steps,
                messages,
                string.Empty,
                operationId,
                registryFingerprint,
                createdAtUtcTicks,
                PackageOperationRisk.None,
                rootRequests);
        }

        private static IReadOnlyList<PackageOperationRootRequest> NormalizeRootRequests(
            IEnumerable<PackageOperationRootRequest> rootRequests,
            IEnumerable<PackageDependencyInstallStep> steps)
        {
            List<PackageOperationRootRequest> normalized = new List<PackageOperationRootRequest>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageOperationRootRequest root in rootRequests ??
                         Array.Empty<PackageOperationRootRequest>())
            {
                if (root != null && !string.IsNullOrWhiteSpace(root.PackageId) &&
                    seen.Add(root.PackageId.Trim()))
                {
                    normalized.Add(new PackageOperationRootRequest(root.PackageId.Trim(), root.Channel));
                }
            }

            if (normalized.Count > 0)
            {
                return Array.AsReadOnly(normalized.ToArray());
            }

            PackageDependencyInstallStep[] installSteps =
                (steps ?? Array.Empty<PackageDependencyInstallStep>())
                .Where(step => step != null)
                .ToArray();
            foreach (PackageDependencyInstallStep step in installSteps.Where(step => !step.IsDependency))
            {
                if (seen.Add(step.PackageDefinition.PackageId))
                {
                    normalized.Add(new PackageOperationRootRequest(
                        step.PackageDefinition.PackageId,
                        step.Channel));
                }
            }

            foreach (PackageDependencyInstallStep step in installSteps)
            {
                foreach (string rootPackageId in step.RootPackageIds)
                {
                    if (seen.Add(rootPackageId))
                    {
                        normalized.Add(new PackageOperationRootRequest(rootPackageId, step.Channel));
                    }
                }
            }

            return Array.AsReadOnly(normalized.ToArray());
        }
    }

    internal sealed class PackageDependencyInstaller
    {
        private readonly PackageInstallService _packageInstallService;
        private readonly PackageDetectionService _packageDetectionService;
        private readonly Func<IEnumerable<PackageDefinition>> _registeredPackagesProvider;
        private PackagePlannerRetryRequest _lastPlannerRetryRequest;

        internal Func<PackageDependencyInstallPlan, string, bool> PreflightConfirmation { get; set; }

        internal bool CanRetryLastPlannerFailure =>
            _lastPlannerRetryRequest != null && _lastPlannerRetryRequest.RootRequests.Count > 0;

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
                    : "No packages to reinstall.",
                additionalRisks: PackageOperationRisk.Destructive);
        }

        public void InstallAll(Func<PackageDefinition, PackageChannel> channelSelector)
        {
            InstallManyWithDependencies(
                GetInstallAllRootPackages(),
                channelSelector,
                "Install All Packages",
                "All registered packages are already installed.");
        }

        public void InstallManyWithDependencies(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            string alreadyInstalledMessage = "All selected packages are already installed.")
        {
            StartDependencyAwareOperation(
                packageDefinitions,
                channelSelector,
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName,
                includeInstalledRequestedPackages: false,
                alreadyInstalledMessage: alreadyInstalledMessage);
        }

        internal PackageDefinition[] GetInstallAllRootPackagesForTests()
        {
            return GetInstallAllRootPackages().ToArray();
        }

        public void UpdateAll(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector)
        {
            UpdateManyWithDependencies(
                packageDefinitions,
                channelSelector,
                "Update All Installed Packages",
                "No installed packages need updates.");
        }

        public void UpdateManyWithDependencies(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            string alreadyInstalledMessage = "No selected packages need updates.")
        {
            StartDependencyAwareOperation(
                packageDefinitions,
                channelSelector,
                string.IsNullOrWhiteSpace(operationName) ? "Update Packages" : operationName,
                includeInstalledRequestedPackages: true,
                alreadyInstalledMessage: alreadyInstalledMessage);
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
            List<string> messages = new List<string>();
            Dictionary<string, PackageDefinition> registeredPackages = GetRegisteredPackages()
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.PackageId))
                .GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackageDependencyInstallStepBuilder> stepBuilders =
                new Dictionary<string, PackageDependencyInstallStepBuilder>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, PackagePlanningTarget> planningTargets =
                new Dictionary<string, PackagePlanningTarget>(StringComparer.OrdinalIgnoreCase);
            List<PackageDependencyInstallStepBuilder> orderedBuilders =
                new List<PackageDependencyInstallStepBuilder>();
            PackageOperationRisk risks = PackageOperationRisk.None;

            PackageDefinition[] requestedPackages = (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .GroupBy(packageDefinition => packageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            Dictionary<string, PackageChannel> requestedChannelsByPackageId = requestedPackages
                .ToDictionary(
                    packageDefinition => packageDefinition.PackageId,
                    packageDefinition => SelectRequestedChannel(packageDefinition, channelSelector),
                    StringComparer.OrdinalIgnoreCase);
            PackageOperationRootRequest[] rootRequests = requestedPackages
                .Select(packageDefinition => new PackageOperationRootRequest(
                    packageDefinition.PackageId,
                    requestedChannelsByPackageId[packageDefinition.PackageId]))
                .ToArray();

            foreach (PackageDefinition packageDefinition in requestedPackages)
            {
                PackageChannel requestedChannel =
                    requestedChannelsByPackageId[packageDefinition.PackageId];
                List<PackageDefinition> path = new List<PackageDefinition> { packageDefinition };
                HashSet<string> visitingPackageIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!AddPackageToPlan(
                        packageDefinition,
                        packageDefinition,
                        requestedChannel,
                        requestedChannel,
                        isDependency: false,
                        includeInstalledRequestedPackages: includeInstalledRequestedPackages,
                        registeredPackages: registeredPackages,
                        stepBuilders: stepBuilders,
                        orderedBuilders: orderedBuilders,
                        planningTargets: planningTargets,
                        visitingPackageIds: visitingPackageIds,
                        visitStack: path,
                        messages: messages,
                        risks: ref risks,
                        out _,
                        out string errorMessage))
                {
                    PackageOperationRisk failureRisks = errorMessage.StartsWith(
                        "Conflicting package targets",
                        StringComparison.Ordinal)
                        ? risks | PackageOperationRisk.Conflict
                        : risks;
                    return PackageDependencyInstallPlan.Failure(
                        errorMessage,
                        messages,
                        failureRisks,
                        rootRequests);
                }
            }

            foreach (PackageDependencyInstallStepBuilder builder in orderedBuilders)
            {
                risks |= GetInstalledTargetRisks(builder.PackageDefinition, builder.Channel);

                foreach (string dependencyId in builder.PackageDefinition.Dependencies)
                {
                    if (stepBuilders.ContainsKey(dependencyId))
                    {
                        builder.AddPrerequisite(dependencyId);
                    }
                }
            }

            return PackageDependencyInstallPlan.Success(
                orderedBuilders.Select(builder => builder.Build()).ToArray(),
                messages,
                ComputeRegistryFingerprint(registeredPackages.Values),
                risks,
                rootRequests);
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

        internal bool RetryLastPlannerFailure()
        {
            PackagePlannerRetryRequest retryRequest = _lastPlannerRetryRequest;
            if (retryRequest == null || retryRequest.RootRequests.Count == 0)
            {
                return false;
            }

            Dictionary<string, PackageDefinition> registeredPackages = GetRegisteredPackages()
                .Where(package => package != null && !string.IsNullOrWhiteSpace(package.PackageId))
                .GroupBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<PackageDefinition> requestedPackages = new List<PackageDefinition>();

            foreach (PackageOperationRootRequest rootRequest in retryRequest.RootRequests)
            {
                if (!registeredPackages.TryGetValue(
                        rootRequest.PackageId,
                        out PackageDefinition packageDefinition))
                {
                    string errorMessage = "Cannot retry " + retryRequest.OperationName +
                                          " because root package " + rootRequest.PackageId +
                                          " is no longer registered.";
                    PackageInstallerLog.Install.Error(errorMessage);
                    PackageInstallerActivityService.Record(
                        "Planner",
                        PackageInstallerActivitySeverity.Error,
                        errorMessage,
                        retryKind: PackageInstallerRetryKind.ReplanOperation);
                    return false;
                }

                requestedPackages.Add(packageDefinition);
            }

            StartDependencyAwareOperation(
                requestedPackages,
                packageDefinition => retryRequest.GetChannel(packageDefinition.PackageId),
                retryRequest.OperationName,
                retryRequest.IncludeInstalledRequestedPackages,
                retryRequest.AlreadyInstalledMessage,
                retryRequest.AdditionalRisks);
            return true;
        }

        private void StartDependencyAwareOperation(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            bool includeInstalledRequestedPackages,
            string alreadyInstalledMessage,
            PackageOperationRisk additionalRisks = PackageOperationRisk.None)
        {
            PackageDependencyInstallPlan installPlan = CreateInstallPlan(
                packageDefinitions,
                channelSelector,
                includeInstalledRequestedPackages)
                .WithAdditionalRisks(additionalRisks);

            LogMessages(installPlan.Messages);

            if (!installPlan.IsValid)
            {
                _lastPlannerRetryRequest = installPlan.RootRequests.Count > 0
                    ? new PackagePlannerRetryRequest(
                        installPlan.RootRequests,
                        operationName,
                        includeInstalledRequestedPackages,
                        alreadyInstalledMessage,
                        additionalRisks)
                    : null;
                PackageInstallerLog.Install.Error(installPlan.ErrorMessage);
                PackageInstallerActivityService.Record(
                    "Planner",
                    PackageInstallerActivitySeverity.Error,
                    installPlan.ErrorMessage,
                    installPlan.Messages.Count > 0
                        ? string.Join("\n", installPlan.Messages.ToArray())
                        : string.Empty,
                    retryKind: CanRetryLastPlannerFailure
                        ? PackageInstallerRetryKind.ReplanOperation
                        : PackageInstallerRetryKind.None);
                return;
            }

            _lastPlannerRetryRequest = null;

            if (installPlan.Steps.Count == 0)
            {
                _packageInstallService.RecordCompletedOperation(
                    operationName,
                    alreadyInstalledMessage,
                    installPlan.Messages);
                PackageInstallerLog.Install.Info(alreadyInstalledMessage);
                return;
            }

            if (installPlan.RequiresPreflight &&
                PreflightConfirmation != null &&
                !PreflightConfirmation(installPlan, operationName))
            {
                PackageInstallerLog.Install.Info(operationName + " canceled during preflight.");
                PackageInstallerActivityService.Record(
                    "Planner",
                    PackageInstallerActivitySeverity.Warning,
                    operationName + " canceled during preflight.");
                return;
            }

            _packageInstallService.InstallPlan(installPlan, operationName);
        }

        private sealed class PackagePlannerRetryRequest
        {
            private readonly Dictionary<string, PackageChannel> _channelsByPackageId;

            public PackagePlannerRetryRequest(
                IEnumerable<PackageOperationRootRequest> rootRequests,
                string operationName,
                bool includeInstalledRequestedPackages,
                string alreadyInstalledMessage,
                PackageOperationRisk additionalRisks)
            {
                RootRequests = Array.AsReadOnly((rootRequests ??
                        Array.Empty<PackageOperationRootRequest>())
                    .Where(root => root != null && !string.IsNullOrWhiteSpace(root.PackageId))
                    .Select(root => new PackageOperationRootRequest(root.PackageId, root.Channel))
                    .ToArray());
                OperationName = string.IsNullOrWhiteSpace(operationName)
                    ? "Package Operation"
                    : operationName;
                IncludeInstalledRequestedPackages = includeInstalledRequestedPackages;
                AlreadyInstalledMessage = alreadyInstalledMessage ?? string.Empty;
                AdditionalRisks = additionalRisks;
                _channelsByPackageId = RootRequests
                    .GroupBy(root => root.PackageId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().Channel,
                        StringComparer.OrdinalIgnoreCase);
            }

            public IReadOnlyList<PackageOperationRootRequest> RootRequests { get; }

            public string OperationName { get; }

            public bool IncludeInstalledRequestedPackages { get; }

            public string AlreadyInstalledMessage { get; }

            public PackageOperationRisk AdditionalRisks { get; }

            public PackageChannel GetChannel(string packageId)
            {
                return !string.IsNullOrWhiteSpace(packageId) &&
                       _channelsByPackageId.TryGetValue(packageId, out PackageChannel channel)
                    ? channel
                    : PackageChannel.Stable;
            }
        }

        private bool AddPackageToPlan(
            PackageDefinition packageDefinition,
            PackageDefinition requestedPackage,
            PackageChannel requestedChannel,
            PackageChannel packageChannel,
            bool isDependency,
            bool includeInstalledRequestedPackages,
            IReadOnlyDictionary<string, PackageDefinition> registeredPackages,
            IDictionary<string, PackageDependencyInstallStepBuilder> stepBuilders,
            ICollection<PackageDependencyInstallStepBuilder> orderedBuilders,
            IDictionary<string, PackagePlanningTarget> planningTargets,
            ISet<string> visitingPackageIds,
            IList<PackageDefinition> visitStack,
            ICollection<string> messages,
            ref PackageOperationRisk risks,
            out bool isPlanned,
            out string errorMessage)
        {
            isPlanned = false;
            errorMessage = string.Empty;

            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return true;
            }

            if (visitingPackageIds.Contains(packageDefinition.PackageId))
            {
                errorMessage = "Circular dependency detected: " + FormatCycle(visitStack, packageDefinition) + ".";
                return false;
            }

            string packageUrl = packageDefinition.GetUrl(packageChannel);

            if (string.IsNullOrWhiteSpace(packageUrl))
            {
                errorMessage = "Cannot install " + requestedPackage.DisplayName +
                               " because dependency " + packageDefinition.DisplayName +
                               " has no " + packageChannel + " package URL.";
                return false;
            }

            string rootPath = FormatPath(visitStack);

            if (planningTargets.TryGetValue(
                    packageDefinition.PackageId,
                    out PackagePlanningTarget existingTarget) &&
                !string.Equals(existingTarget.TargetUrl, packageUrl, StringComparison.Ordinal))
            {
                errorMessage = "Conflicting package targets for " + packageDefinition.DisplayName +
                               ": " + existingTarget.RootPath + " requests " +
                               existingTarget.Channel + " (" + existingTarget.TargetUrl + "), while " +
                               rootPath + " requests " + packageChannel + " (" + packageUrl + ").";
                return false;
            }

            if (existingTarget == null)
            {
                planningTargets[packageDefinition.PackageId] =
                    new PackagePlanningTarget(packageChannel, packageUrl, rootPath);
            }

            visitingPackageIds.Add(packageDefinition.PackageId);

            foreach (string dependencyId in packageDefinition.Dependencies)
            {
                if (!registeredPackages.TryGetValue(dependencyId, out PackageDefinition dependency) ||
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
                    messages,
                    out bool usedChannelFallback);

                if (usedChannelFallback)
                {
                    risks |= PackageOperationRisk.ChannelFallback;
                }

                visitStack.Add(dependency);

                if (!AddPackageToPlan(
                        dependency,
                        requestedPackage,
                        requestedChannel,
                        dependencyChannel,
                        isDependency: true,
                        includeInstalledRequestedPackages: includeInstalledRequestedPackages,
                        registeredPackages: registeredPackages,
                        stepBuilders: stepBuilders,
                        orderedBuilders: orderedBuilders,
                        planningTargets: planningTargets,
                        visitingPackageIds: visitingPackageIds,
                        visitStack: visitStack,
                        messages: messages,
                        risks: ref risks,
                        out _,
                        out errorMessage))
                {
                    return false;
                }

                visitStack.RemoveAt(visitStack.Count - 1);
            }

            visitingPackageIds.Remove(packageDefinition.PackageId);

            if (!ShouldInstallPackage(
                    packageDefinition,
                    packageChannel,
                    isDependency,
                    includeInstalledRequestedPackages))
            {
                if (isDependency)
                {
                    AddUniqueMessage(
                        messages,
                        "Skipped dependency " + packageDefinition.DisplayName + "; already installed.");
                }

                return true;
            }

            string rootPackageId = requestedPackage != null ? requestedPackage.PackageId : packageDefinition.PackageId;
            string dependencyReason = isDependency && requestedPackage != null
                ? "Required by " + requestedPackage.DisplayName + "."
                : string.Empty;

            if (stepBuilders.TryGetValue(
                    packageDefinition.PackageId,
                    out PackageDependencyInstallStepBuilder existingBuilder))
            {
                existingBuilder.MergeContext(
                    isDependency,
                    rootPackageId,
                    rootPath,
                    dependencyReason);
                isPlanned = true;

                if (isDependency)
                {
                    AddUniqueMessage(
                        messages,
                        "Skipped dependency " + packageDefinition.DisplayName + "; already queued.");
                }

                return true;
            }

            if (isDependency)
            {
                AddUniqueMessage(
                    messages,
                    "Installing dependency " + packageDefinition.DisplayName +
                    " before " + requestedPackage.DisplayName + ".");
            }

            PackageDependencyInstallStepBuilder builder = new PackageDependencyInstallStepBuilder(
                packageDefinition,
                packageChannel,
                packageUrl,
                isDependency,
                rootPackageId,
                rootPath,
                dependencyReason,
                GetDetectedCurrentSource(packageDefinition.PackageId),
                GetDetectedCurrentVersion(packageDefinition.PackageId),
                _packageDetectionService.GetInstalledIdentity(packageDefinition.PackageId));
            stepBuilders.Add(packageDefinition.PackageId, builder);
            orderedBuilders.Add(builder);
            isPlanned = true;
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
            ICollection<string> messages,
            out bool usedFallback)
        {
            usedFallback = false;

            if (requestedChannel == PackageChannel.Development)
            {
                if (dependency.HasDevelopmentUrl)
                {
                    return PackageChannel.Development;
                }

                AddUniqueMessage(
                    messages,
                    "Dependency " + dependency.DisplayName +
                    " has no Development channel; falling back to Stable before installing " +
                    requestedPackage.DisplayName + ".");
                usedFallback = true;
                return PackageChannel.Stable;
            }

            if (requestedChannel == PackageChannel.Custom)
            {
                AddUniqueMessage(
                    messages,
                    "Dependency " + dependency.DisplayName +
                    " has no Custom channel; falling back to Stable before installing " +
                    requestedPackage.DisplayName + ".");
                usedFallback = true;
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

        private IEnumerable<PackageDefinition> GetInstallAllRootPackages()
        {
            return GetRegisteredPackages().Where(package => package == null || !package.IsTemplate);
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

            IEnumerable<PackageDefinition> cycleWithEnd = cycle;
            PackageDefinition lastPackage = cycle.LastOrDefault();

            if (lastPackage == null || repeatedPackage == null ||
                !string.Equals(
                    lastPackage.PackageId,
                    repeatedPackage.PackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                cycleWithEnd = cycle.Concat(new[] { repeatedPackage });
            }

            return string.Join(
                " -> ",
                cycleWithEnd
                    .Where(packageDefinition => packageDefinition != null)
                    .Select(packageDefinition => packageDefinition.DisplayName)
                    .ToArray());
        }

        private static string FormatPath(IEnumerable<PackageDefinition> path)
        {
            return string.Join(
                " -> ",
                (path ?? Array.Empty<PackageDefinition>())
                    .Where(package => package != null)
                    .Select(package => package.DisplayName)
                    .ToArray());
        }

        private static void AddUniqueMessage(ICollection<string> messages, string message)
        {
            if (messages == null || string.IsNullOrWhiteSpace(message) || messages.Contains(message))
            {
                return;
            }

            messages.Add(message);
        }

        private PackageOperationRisk GetInstalledTargetRisks(
            PackageDefinition packageDefinition,
            PackageChannel targetChannel)
        {
            if (packageDefinition == null ||
                !_packageDetectionService.IsInstalled(packageDefinition.PackageId))
            {
                return PackageOperationRisk.None;
            }

            PackageOperationRisk risks = PackageOperationRisk.None;

            if (_packageDetectionService.TryGetInstalledPackageSourceType(
                    packageDefinition.PackageId,
                    out PackageInstallSourceType sourceType) &&
                (sourceType == PackageInstallSourceType.Registry ||
                 sourceType == PackageInstallSourceType.Local ||
                 sourceType == PackageInstallSourceType.Embedded))
            {
                risks |= PackageOperationRisk.SourceOrChannelMigration;
            }

            if (_packageDetectionService.TryGetInstalledPackageChannel(
                    packageDefinition,
                    out PackageChannel installedChannel,
                    out _) &&
                installedChannel != targetChannel)
            {
                risks |= installedChannel == PackageChannel.Development &&
                         targetChannel == PackageChannel.Stable
                    ? PackageOperationRisk.Downgrade
                    : PackageOperationRisk.SourceOrChannelMigration;
            }

            return risks;
        }

        private string GetDetectedCurrentSource(string packageId)
        {
            return _packageDetectionService.TryGetInstalledPackageSourceType(
                packageId,
                out PackageInstallSourceType sourceType)
                ? sourceType.ToString()
                : string.Empty;
        }

        private string GetDetectedCurrentVersion(string packageId)
        {
            return _packageDetectionService.TryGetInstalledPackageVersion(
                packageId,
                out string version)
                ? version
                : string.Empty;
        }

        private static string ComputeRegistryFingerprint(IEnumerable<PackageDefinition> packages)
        {
            string payload = string.Join(
                "\n",
                (packages ?? Array.Empty<PackageDefinition>())
                    .Where(package => package != null)
                    .OrderBy(package => package.PackageId, StringComparer.OrdinalIgnoreCase)
                    .Select(package =>
                        package.PackageId.Trim() + "|" +
                        (package.StableUrl ?? string.Empty).Trim() + "|" +
                        (package.DevelopmentUrl ?? string.Empty).Trim() + "|" +
                        string.Join(",", package.Dependencies
                            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                            .Select(id => id.Trim())
                            .ToArray()))
                    .ToArray());

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        internal static string ComputeRegistryFingerprintForTests(
            IEnumerable<PackageDefinition> packages)
        {
            return ComputeRegistryFingerprint(packages);
        }

        private sealed class PackagePlanningTarget
        {
            public PackagePlanningTarget(PackageChannel channel, string targetUrl, string rootPath)
            {
                Channel = channel;
                TargetUrl = targetUrl ?? string.Empty;
                RootPath = rootPath ?? string.Empty;
            }

            public PackageChannel Channel { get; }

            public string TargetUrl { get; }

            public string RootPath { get; }
        }

        private sealed class PackageDependencyInstallStepBuilder
        {
            private readonly List<string> _prerequisitePackageIds = new List<string>();
            private readonly List<string> _rootPackageIds = new List<string>();
            private readonly List<string> _rootPaths = new List<string>();
            private readonly List<string> _dependencyReasons = new List<string>();

            public PackageDependencyInstallStepBuilder(
                PackageDefinition packageDefinition,
                PackageChannel channel,
                string targetUrl,
                bool isDependency,
                string rootPackageId,
                string rootPath,
                string dependencyReason,
                string detectedCurrentSource,
                string detectedCurrentVersion,
                string detectedCurrentIdentity)
            {
                PackageDefinition = packageDefinition;
                Channel = channel;
                TargetUrl = targetUrl ?? string.Empty;
                IsDependency = isDependency;
                DetectedCurrentSource = detectedCurrentSource ?? string.Empty;
                DetectedCurrentVersion = detectedCurrentVersion ?? string.Empty;
                DetectedCurrentIdentity = detectedCurrentIdentity ?? string.Empty;
                MergeContext(isDependency, rootPackageId, rootPath, dependencyReason);
            }

            public PackageDefinition PackageDefinition { get; }

            public PackageChannel Channel { get; }

            public string TargetUrl { get; }

            public bool IsDependency { get; private set; }

            public string DetectedCurrentSource { get; }

            public string DetectedCurrentVersion { get; }

            public string DetectedCurrentIdentity { get; }

            public void AddPrerequisite(string packageId)
            {
                AddUnique(_prerequisitePackageIds, packageId);
            }

            public void MergeContext(
                bool isDependency,
                string rootPackageId,
                string rootPath,
                string dependencyReason)
            {
                IsDependency = IsDependency && isDependency;
                AddUnique(_rootPackageIds, rootPackageId);
                AddUnique(_rootPaths, rootPath);
                AddUnique(_dependencyReasons, dependencyReason);
            }

            public PackageDependencyInstallStep Build()
            {
                return new PackageDependencyInstallStep(
                    PackageDefinition,
                    Channel,
                    IsDependency,
                    TargetUrl,
                    _prerequisitePackageIds,
                    _rootPackageIds,
                    _rootPaths,
                    string.Join(" ", _dependencyReasons.ToArray()),
                    DetectedCurrentSource,
                    DetectedCurrentVersion,
                    DetectedCurrentIdentity);
            }

            private static void AddUnique(ICollection<string> values, string value)
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    values.Any(existing => string.Equals(
                        existing,
                        value,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                values.Add(value.Trim());
            }
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
