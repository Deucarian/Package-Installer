using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallRequestState
    {
        Idle,
        Installing,
        Removing
    }

    internal enum PackageInstallProgressItemState
    {
        Pending,
        Active,
        Completed,
        Failed,
        Skipped,
        Blocked,
        Canceled,
        AlreadyCorrect
    }

    internal sealed class PackageInstallProgressItem
    {
        public PackageInstallProgressItem(string packageId, string displayName)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            State = PackageInstallProgressItemState.Pending;
            Message = string.Empty;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageInstallProgressItemState State { get; internal set; }

        public string Message { get; internal set; }
    }

    internal enum PackageOperationTerminalOutcome
    {
        Succeeded,
        Failed,
        Canceled
    }

    internal sealed class PackageOperationRootRequest
    {
        public PackageOperationRootRequest(string packageId, PackageChannel channel)
        {
            PackageId = packageId ?? string.Empty;
            Channel = channel;
        }

        public string PackageId { get; }
        public PackageChannel Channel { get; }
    }

    internal sealed class PackageOperationStepSnapshot
    {
        public PackageOperationStepSnapshot(
            string packageId,
            string displayName,
            PackageChannel channel,
            string targetUrl,
            bool isDependency,
            IEnumerable<string> rootPackageIds,
            PackageInstallProgressItemState state,
            string message)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Channel = channel;
            TargetUrl = targetUrl ?? string.Empty;
            IsDependency = isDependency;
            RootPackageIds = Array.AsReadOnly((rootPackageIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
            State = state;
            Message = message ?? string.Empty;
        }

        public string PackageId { get; }
        public string DisplayName { get; }
        public PackageChannel Channel { get; }
        public string TargetUrl { get; }
        public bool IsDependency { get; }
        public IReadOnlyList<string> RootPackageIds { get; }
        public PackageInstallProgressItemState State { get; }
        public string Message { get; }
    }

    internal sealed class PackageOperationTerminalSnapshot
    {
        public PackageOperationTerminalSnapshot(
            string operationId,
            string operationName,
            PackageOperationTerminalOutcome outcome,
            string summary,
            string errorMessage,
            IEnumerable<PackageOperationRootRequest> restartRoots,
            IEnumerable<PackageOperationStepSnapshot> steps,
            IEnumerable<string> messages,
            DateTime completedAtUtc)
        {
            OperationId = operationId ?? string.Empty;
            OperationName = operationName ?? string.Empty;
            Outcome = outcome;
            Summary = summary ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
            RestartRoots = Array.AsReadOnly((restartRoots ?? Array.Empty<PackageOperationRootRequest>())
                .Where(root => root != null && !string.IsNullOrWhiteSpace(root.PackageId))
                .ToArray());
            Steps = Array.AsReadOnly((steps ?? Array.Empty<PackageOperationStepSnapshot>())
                .Where(step => step != null)
                .ToArray());
            Messages = Array.AsReadOnly((messages ?? Array.Empty<string>())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Select(message => message.Trim())
                .ToArray());
            CompletedAtUtc = completedAtUtc;
        }

        public string OperationId { get; }
        public string OperationName { get; }
        public PackageOperationTerminalOutcome Outcome { get; }
        public string Summary { get; }
        public string ErrorMessage { get; }
        public IReadOnlyList<PackageOperationRootRequest> RestartRoots { get; }
        public IReadOnlyList<PackageOperationStepSnapshot> Steps { get; }
        public IReadOnlyList<string> Messages { get; }
        public DateTime CompletedAtUtc { get; }

        public bool CanRestart =>
            (Outcome == PackageOperationTerminalOutcome.Failed ||
             Outcome == PackageOperationTerminalOutcome.Canceled) &&
            RestartRoots.Count > 0;
    }

    internal sealed class PackageInstallService : IDisposable
    {
        private const string PendingOperationNameKey = "Deucarian.PackageInstaller.PendingOperationName";
        private const string PendingQueueKey = "Deucarian.PackageInstaller.PendingQueue";

        private readonly List<QueuedPackageInstall> _installQueue = new List<QueuedPackageInstall>();
        private readonly HashSet<string> _queuedOrInstallingPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageInstallProgressItem> _progressItems = new List<PackageInstallProgressItem>();
        private readonly List<string> _operationMessages = new List<string>();
        private readonly Dictionary<string, PackageInstallProgressItem> _progressItemsByPackageId =
            new Dictionary<string, PackageInstallProgressItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, QueuedPackageInstall> _operationInstallsByPackageId =
            new Dictionary<string, QueuedPackageInstall>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PackageOperationRootRequest> _currentRootRequests =
            new List<PackageOperationRootRequest>();
        private readonly IPackageInstallClient _packageClient;
        private readonly PackageOperationStateRepository _operationStateRepository;
        private IPackageInstallRequest _currentRequest;
        private IPackageInstallRequest _currentRemoveRequest;
        private QueuedPackageInstall _currentInstall;
        private PackageDefinition _currentRemovePackage;
        private string _currentOperationId = string.Empty;
        private string _currentRegistryFingerprint = string.Empty;
        private long _currentOperationCreatedAtUtcTicks;
        private string _currentOperationName = string.Empty;
        private string _lastStatusMessage = string.Empty;
        private string _lastErrorMessage = string.Empty;
        private int _completedSteps;
        private int _successfulSteps;
        private int _failedSteps;
        private int _skippedSteps;
        private int _blockedSteps;
        private int _canceledSteps;
        private int _totalSteps;
        private bool _cancelRequested;
        private bool _operationCanceled;
        private bool _completionActivityRecorded;
        private PackageOperationTerminalSnapshot _terminalOperationSnapshot;

        public event Action StateChanged;

        public event Action<PackageDefinition, bool, string> InstallCompleted;

        public event Action QueueCompleted;

        public PackageInstallRequestState State { get; private set; } = PackageInstallRequestState.Idle;

        public PackageDefinition CurrentPackage => _currentInstall != null ? _currentInstall.PackageDefinition : _currentRemovePackage;

        public PackageChannel CurrentChannel => _currentInstall != null ? _currentInstall.Channel : PackageChannel.Stable;

        public string CurrentUrl => _currentInstall != null ? _currentInstall.Url : string.Empty;

        public bool IsBusy =>
            State == PackageInstallRequestState.Installing ||
            State == PackageInstallRequestState.Removing ||
            _installQueue.Count > 0;

        public bool IsCancelRequested => _cancelRequested;

        public bool HasProgress => _totalSteps > 0 || !string.IsNullOrWhiteSpace(_currentOperationName);

        public string CurrentOperationName => _currentOperationName;

        public string CurrentPackageName => CurrentPackage != null ? CurrentPackage.DisplayName : string.Empty;

        public int CompletedSteps => _completedSteps;

        public int TotalSteps => _totalSteps;

        public int SuccessfulSteps => _successfulSteps;

        public int FailedSteps => _failedSteps;

        public int SkippedSteps => _skippedSteps;

        public int BlockedSteps => _blockedSteps;

        public int CanceledSteps => _canceledSteps;

        public string LastStatusMessage => _lastStatusMessage;

        public string LastErrorMessage => _lastErrorMessage;

        public IReadOnlyList<PackageInstallProgressItem> ProgressItems => _progressItems;

        public IReadOnlyList<string> OperationMessages => _operationMessages;

        public PackageOperationTerminalSnapshot TerminalOperationSnapshot =>
            _terminalOperationSnapshot;

        internal Func<string, string, string, bool> ExactTargetAlreadyInstalled { get; set; }

        public PackageInstallService()
            : this(new UnityPackageInstallClient(), new PackageOperationStateRepository())
        {
        }

        internal PackageInstallService(
            IPackageInstallClient packageClient,
            PackageOperationStateRepository operationStateRepository)
        {
            _packageClient = packageClient ?? throw new ArgumentNullException(nameof(packageClient));
            _operationStateRepository = operationStateRepository ??
                                        throw new ArgumentNullException(nameof(operationStateRepository));
            PackageInstallerSelfUpdateState.ReconcileCurrentRuntime();
        }

        public bool HasSavedOperation
        {
            get
            {
                return TryGetSavedOperation(out _, out _);
            }
        }

        public bool TryGetSavedOperation(
            out PackageOperationRecoveryRecord record,
            out string errorMessage)
        {
            if (_operationStateRepository.TryLoad(out record, out errorMessage))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                return false;
            }

            bool loadedLegacy = TryLoadSavedOperation(_operationStateRepository, out record);
            errorMessage = loadedLegacy
                ? string.Empty
                : "No saved package operation is available.";
            return loadedLegacy;
        }

        public bool ResumeSavedOperation()
        {
            if (IsBusy)
            {
                return false;
            }

            bool selfUpdateAppliedOnReload =
                PackageInstallerSelfUpdateState.ReconcileCurrentRuntime() ==
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;

            if (!TryPrepareSavedOperationForResume(
                    _operationStateRepository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord recoveryRecord))
            {
                return false;
            }

            if (recoveryRecord == null || recoveryRecord.Steps.Count == 0)
            {
                ClearSavedOperationState(_operationStateRepository);
                return false;
            }

            RestoreOperation(recoveryRecord);

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            bool completedDuringReconciliation = CompleteRecoveredOperationWithoutRequestIfNeeded();
            NotifyStateChanged();

            return IsBusy || completedDuringReconciliation;
        }

        public bool RestartSavedOperation()
        {
            if (IsBusy)
            {
                return false;
            }

            bool selfUpdateAppliedOnReload =
                PackageInstallerSelfUpdateState.ReconcileCurrentRuntime() ==
                PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;

            if (!TryPrepareSavedOperationForResume(
                    _operationStateRepository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord recoveryRecord))
            {
                return false;
            }

            PackageOperationRecoveryStep[] restartedSteps = recoveryRecord.Steps
                .Select(step => new PackageOperationRecoveryStep(
                    step.PackageId,
                    step.DisplayName,
                    step.Channel,
                    step.TargetUrl,
                    step.IsDependency,
                    step.PrerequisitePackageIds,
                    step.RootPackageIds,
                    step.RootPaths,
                    step.DependencyReason,
                    PackageInstallProgressItemState.Pending,
                    string.Empty,
                    step.DetectedCurrentSource,
                    step.DetectedCurrentVersion,
                    step.DetectedCurrentIdentity))
                .ToArray();
            PackageOperationRecoveryRecord restartedRecord = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                recoveryRecord.OperationName,
                recoveryRecord.RegistryFingerprint,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                restartedSteps,
                recoveryRecord.Messages,
                recoveryRecord.RootRequests);

            if (!_operationStateRepository.Save(restartedRecord, out string saveError))
            {
                _lastErrorMessage = saveError;
                PackageInstallerLog.Install.Warning(saveError);
                NotifyStateChanged();
                return false;
            }

            RestoreOperation(restartedRecord);
            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            bool completedDuringReconciliation = CompleteRecoveredOperationWithoutRequestIfNeeded();
            if (!completedDuringReconciliation)
            {
                SavePendingOperationState();
            }
            NotifyStateChanged();
            return IsBusy || completedDuringReconciliation;
        }

        private bool CompleteRecoveredOperationWithoutRequestIfNeeded()
        {
            if (IsBusy || !HasProgress)
            {
                return false;
            }

            ClearSavedOperationState(_operationStateRepository);
            QueueCompleted?.Invoke();
            return true;
        }

        public bool DiscardSavedOperation()
        {
            if (IsBusy)
            {
                return false;
            }

            bool hadSavedOperation = TryLoadSavedOperation(
                _operationStateRepository,
                out _);
            ClearSavedOperationState(_operationStateRepository);
            PackageInstallerSelfUpdateState.AcknowledgeApplied();
            NotifyStateChanged();
            return hadSavedOperation;
        }

        public bool CancelCurrentOperation()
        {
            if (!IsBusy)
            {
                return false;
            }

            if (_cancelRequested)
            {
                return true;
            }

            _cancelRequested = true;
            _operationCanceled = true;
            CancelQueuedInstalls("Canceled before starting.");
            ClearSavedOperationState(_operationStateRepository);

            if (_currentRequest == null && _currentRemoveRequest == null)
            {
                EditorApplication.update -= Update;
                State = PackageInstallRequestState.Idle;
                SetOperationCompleteSummary();
                _cancelRequested = false;
                QueueCompleted?.Invoke();
                NotifyStateChanged();
                return true;
            }

            _lastStatusMessage = State == PackageInstallRequestState.Removing
                ? "Cancel requested. Waiting for current remove operation to finish..."
                : "Cancel requested. Waiting for current package operation to finish...";
            NotifyStateChanged();
            return true;
        }

        public bool Install(PackageDefinition packageDefinition)
        {
            return Install(packageDefinition, PackageChannel.Stable);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel)
        {
            string operationName = packageDefinition != null
                ? "Install " + packageDefinition.DisplayName
                : "Install Package";

            return Install(packageDefinition, channel, operationName);
        }

        public bool Install(PackageDefinition packageDefinition, PackageChannel channel, string operationName)
        {
            if (packageDefinition == null)
            {
                PackageInstallerLog.Install.Error("Cannot install a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            string targetUrl = packageDefinition.GetUrl(channel);
            PackageDependencyInstallStep step = new PackageDependencyInstallStep(
                packageDefinition,
                channel,
                isDependency: false,
                targetUrl: targetUrl,
                rootPackageIds: new[] { packageDefinition.PackageId },
                rootPaths: new[] { packageDefinition.DisplayName });
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                new[] { step },
                Array.Empty<string>());

            return InstallPlan(
                plan,
                string.IsNullOrWhiteSpace(operationName)
                    ? "Install " + packageDefinition.DisplayName
                    : operationName);
        }

        private bool QueueInstall(PackageDefinition packageDefinition, PackageChannel channel)
        {
            return QueueInstall(new PackageDependencyInstallStep(
                packageDefinition,
                channel,
                isDependency: false,
                targetUrl: packageDefinition.GetUrl(channel),
                rootPackageIds: new[] { packageDefinition.PackageId },
                rootPaths: new[] { packageDefinition.DisplayName }));
        }

        private bool QueueInstall(PackageDependencyInstallStep step)
        {
            PackageDefinition packageDefinition = step != null ? step.PackageDefinition : null;
            string packageUrl = step != null ? step.TargetUrl : string.Empty;

            if (packageDefinition == null || string.IsNullOrWhiteSpace(packageUrl))
            {
                string displayName = packageDefinition != null ? packageDefinition.DisplayName : "Package";
                string message = displayName + " has no package URL to install.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Failed, message);
                PackageInstallerLog.Install.Warning(message);
                return false;
            }

            if (_queuedOrInstallingPackageIds.Contains(packageDefinition.PackageId))
            {
                string message = packageDefinition.DisplayName + " is already queued or installing.";
                MarkProgressItem(packageDefinition, PackageInstallProgressItemState.Skipped, message);
                PackageInstallerLog.Install.Info(message);
                return false;
            }

            QueuedPackageInstall install = new QueuedPackageInstall(step);
            _installQueue.Add(install);
            _operationInstallsByPackageId[packageDefinition.PackageId] = install;
            _queuedOrInstallingPackageIds.Add(packageDefinition.PackageId);
            _lastStatusMessage = "Queued " + packageDefinition.DisplayName + ".";
            PackageInstallerLog.Install.Info(
                "Queued " + packageDefinition.DisplayName + " from " + packageUrl + " (" + step.Channel + ").");

            return true;
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions)
        {
            InstallMany(packageDefinitions, PackageChannel.Stable);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, PackageChannel channel)
        {
            InstallMany(packageDefinitions, _ => channel);
        }

        public void InstallMany(IEnumerable<PackageDefinition> packageDefinitions, Func<PackageDefinition, PackageChannel> channelSelector)
        {
            InstallMany(packageDefinitions, channelSelector, "Install Packages");
        }

        public void InstallMany(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName)
        {
            InstallMany(packageDefinitions, channelSelector, operationName, Array.Empty<string>());
        }

        public void InstallMany(
            IEnumerable<PackageDefinition> packageDefinitions,
            Func<PackageDefinition, PackageChannel> channelSelector,
            string operationName,
            IEnumerable<string> operationMessages)
        {
            if (packageDefinitions == null)
            {
                return;
            }

            PackageDefinition[] packages = packageDefinitions
                .Where(packageDefinition => packageDefinition != null)
                .GroupBy(packageDefinition => packageDefinition.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(packageDefinition =>
                    PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId) ? 1 : 0)
                .ToArray();

            if (packages.Length == 0)
            {
                return;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName + " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return;
            }

            PackageDependencyInstallStep[] steps = packages
                .Select(packageDefinition =>
                {
                    PackageChannel channel = channelSelector != null
                        ? channelSelector(packageDefinition)
                        : PackageChannel.Stable;
                    return new PackageDependencyInstallStep(
                        packageDefinition,
                        channel,
                        isDependency: false,
                        targetUrl: packageDefinition.GetUrl(channel),
                        rootPackageIds: new[] { packageDefinition.PackageId },
                        rootPaths: new[] { packageDefinition.DisplayName });
                })
                .ToArray();

            InstallPlan(
                PackageDependencyInstallPlan.Success(steps, operationMessages),
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName);
        }

        internal bool InstallPlan(PackageDependencyInstallPlan plan, string operationName)
        {
            if (plan == null || !plan.IsValid || plan.Steps.Count == 0)
            {
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + operationName +
                                    " because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Install Packages" : operationName,
                plan);

            bool queuedAny = false;
            foreach (PackageDependencyInstallStep step in plan.Steps)
            {
                queuedAny |= QueueInstall(step);
            }

            StartNextRequestIfNeeded();
            CompleteOperationIfIdle();
            SavePendingOperationState();
            NotifyStateChanged();
            return queuedAny;
        }

        internal void RecordCompletedOperation(
            string operationName,
            string summaryMessage,
            IEnumerable<string> operationMessages)
        {
            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Package Operation" : operationName,
                Array.Empty<PackageDefinition>(),
                operationMessages);

            _lastStatusMessage = summaryMessage ?? string.Empty;
            _lastErrorMessage = string.Empty;
            RecordCompletionActivity(PackageInstallerActivitySeverity.Success);
            NotifyStateChanged();
        }

        internal void QueuePendingOperationForTests(
            string operationName,
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            PackageDefinition[] packages = (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .ToArray();

            PackageDependencyInstallStep[] steps = packages.Select(packageDefinition =>
                new PackageDependencyInstallStep(
                    packageDefinition,
                    PackageChannel.Stable,
                    isDependency: false,
                    targetUrl: packageDefinition.GetUrl(PackageChannel.Stable),
                    rootPackageIds: new[] { packageDefinition.PackageId },
                    rootPaths: new[] { packageDefinition.DisplayName })).ToArray();
            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Success(
                steps,
                Array.Empty<string>());

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Package Operation" : operationName,
                plan);

            foreach (PackageDependencyInstallStep step in steps)
            {
                QueueInstall(step);
            }

            NotifyStateChanged();
        }

        internal static PackageDefinition[] OrderSelfUpdateLastForTests(
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            return (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition => packageDefinition != null)
                .OrderBy(packageDefinition =>
                    PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId) ? 1 : 0)
                .ToArray();
        }

        internal static PackageDefinition[] FilterAppliedSelfUpdateForTests(
            IEnumerable<PackageDefinition> packageDefinitions)
        {
            return (packageDefinitions ?? Array.Empty<PackageDefinition>())
                .Where(packageDefinition =>
                    packageDefinition != null &&
                    !PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId))
                .ToArray();
        }

        internal void SavePendingOperationForTests()
        {
            SavePendingOperationState();
        }

        internal static string[] RestorePendingPackageIdsForTests(
            bool selfUpdateAppliedOnReload,
            out string operationName)
        {
            PackageOperationStateRepository repository = new PackageOperationStateRepository();
            if (!TryLoadSavedOperation(repository, out PackageOperationRecoveryRecord record))
            {
                operationName = string.Empty;
                return Array.Empty<string>();
            }

            operationName = record.OperationName;
            IEnumerable<PackageOperationRecoveryStep> steps = record.Steps;
            if (selfUpdateAppliedOnReload)
            {
                steps = FilterAppliedSelfUpdate(steps);
            }

            return steps
                .Where(step => step != null && IsResumableState(step.State))
                .Select(step => step.PackageId)
                .ToArray();
        }

        internal static string[] PreparePendingPackageIdsForResumeForTests(
            bool selfUpdateAppliedOnReload,
            out string operationName)
        {
            PackageOperationStateRepository repository = new PackageOperationStateRepository();
            if (!TryPrepareSavedOperationForResume(
                    repository,
                    selfUpdateAppliedOnReload,
                    out PackageOperationRecoveryRecord record))
            {
                operationName = string.Empty;
                return Array.Empty<string>();
            }

            operationName = record.OperationName;
            return record.Steps
                .Where(step => step != null && IsResumableState(step.State))
                .Select(step => step.PackageId)
                .ToArray();
        }

        internal static void ClearPendingOperationForTests()
        {
            ClearSavedOperationState(new PackageOperationStateRepository());
        }

        internal static void ReconcileSelfUpdateAfterInstallForTests(
            PackageDefinition completedPackage,
            bool success)
        {
            if (!success &&
                completedPackage != null &&
                PackageInstallerRuntimeIdentity.IsSelf(completedPackage.PackageId))
            {
                PackageInstallerSelfUpdateState.MarkInstallFailed();
            }
        }

        public bool Remove(PackageDefinition packageDefinition)
        {
            string operationName = packageDefinition != null
                ? "Remove " + packageDefinition.DisplayName
                : "Remove Package";

            return Remove(packageDefinition, operationName);
        }

        public bool Remove(PackageDefinition packageDefinition, string operationName)
        {
            if (packageDefinition == null)
            {
                PackageInstallerLog.Install.Error("Cannot remove a null package definition.");
                return false;
            }

            if (IsBusy)
            {
                _lastErrorMessage = "Cannot start " + packageDefinition.DisplayName + " removal because another package operation is already running.";
                PackageInstallerLog.Install.Warning(_lastErrorMessage);
                NotifyStateChanged();
                return false;
            }

            BeginOperation(
                string.IsNullOrWhiteSpace(operationName) ? "Remove " + packageDefinition.DisplayName : operationName,
                new[] { packageDefinition });

            _currentRemovePackage = packageDefinition;
            State = PackageInstallRequestState.Removing;
            MarkProgressItem(
                packageDefinition,
                PackageInstallProgressItemState.Active,
                "Removing " + packageDefinition.DisplayName + "...");
            _lastStatusMessage = "Removing " + packageDefinition.DisplayName + "...";
            ClearSavedOperationState(_operationStateRepository);

            try
            {
                _currentRemoveRequest = _packageClient.Remove(packageDefinition.PackageId);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                PackageInstallerLog.Install.Info("Removing " + packageDefinition.DisplayName + " (" + packageDefinition.PackageId + ").");
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Install.Error("Failed to start remove for " + packageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRemoveRequest(false, exception.Message);
            }

            NotifyStateChanged();
            return _currentRemoveRequest != null;
        }

        public bool IsQueuedOrInstalling(string packageId)
        {
            return !string.IsNullOrWhiteSpace(packageId) && _queuedOrInstallingPackageIds.Contains(packageId);
        }

        public void Dispose()
        {
            EditorApplication.update -= Update;
        }

        internal void UpdateForTests()
        {
            Update();
        }

        private void StartNextRequestIfNeeded()
        {
            if (_currentRequest != null || _currentRemoveRequest != null || _cancelRequested)
            {
                return;
            }

            BlockInstallsWithFailedPrerequisites();

            if (_installQueue.Count == 0)
            {
                return;
            }

            _currentInstall = _installQueue
                .Where(CanStartInstall)
                .OrderBy(install => PackageInstallerRuntimeIdentity.IsSelf(
                    install.PackageDefinition.PackageId) ? 1 : 0)
                .ThenBy(install => _installQueue.IndexOf(install))
                .FirstOrDefault();

            if (_currentInstall == null)
            {
                BlockUnresolvableInstalls();
                return;
            }

            _installQueue.Remove(_currentInstall);
            State = PackageInstallRequestState.Installing;
            MarkProgressItem(
                _currentInstall.PackageDefinition,
                PackageInstallProgressItemState.Active,
                "Installing " + _currentInstall.PackageDefinition.DisplayName + "...");
            _lastStatusMessage = "Installing " + _currentInstall.PackageDefinition.DisplayName + "...";

            try
            {
                if (PackageInstallerRuntimeIdentity.IsSelf(_currentInstall.PackageDefinition.PackageId))
                {
                    PackageInstallerSelfUpdateState.Begin(_currentInstall.Url);
                }

                _currentRequest = _packageClient.Add(_currentInstall.Url);
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                SavePendingOperationState();

                PackageInstallerLog.Install.Info("Installing " + _currentInstall.PackageDefinition.DisplayName + " using " + _currentInstall.Url + " (" + _currentInstall.Channel + ").");
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Install.Error("Failed to start install for " + _currentInstall.PackageDefinition.DisplayName + ": " + exception.Message);
                CompleteCurrentRequest(false, exception.Message);
            }
        }

        private void Update()
        {
            if (_currentRemoveRequest != null)
            {
                UpdateRemoveRequest();
                return;
            }

            if (_currentRequest == null || !_currentRequest.IsCompleted)
            {
                return;
            }

            if (_currentRequest.IsSuccess)
            {
                PackageDefinition packageDefinition = _currentInstall.PackageDefinition;
                string packageName = !string.IsNullOrWhiteSpace(_currentRequest.PackageName)
                    ? _currentRequest.PackageName
                    : packageDefinition.PackageId;
                string version = !string.IsNullOrWhiteSpace(_currentRequest.PackageVersion)
                    ? _currentRequest.PackageVersion
                    : "unknown";
                string message = "Installed " + packageDefinition.DisplayName + " (" + packageName + "@" + version + ") from " + _currentInstall.Channel + ".";

                if (PackageInstallerRuntimeIdentity.IsSelf(packageDefinition.PackageId))
                {
                    PackageInstallerSelfUpdateState.MarkResolved(version);
                    message += " Waiting for Unity to load the updated installer assembly.";
                }

                CompleteCurrentRequest(true, message);
                PackageInstallerLog.Install.Info(message);
                return;
            }

            string errorMessage = string.IsNullOrWhiteSpace(_currentRequest.ErrorMessage)
                ? "Package Manager returned an unknown error."
                : _currentRequest.ErrorMessage;
            string failedPackageName = _currentInstall != null && _currentInstall.PackageDefinition != null
                ? _currentInstall.PackageDefinition.DisplayName
                : "package";

            CompleteCurrentRequest(false, errorMessage);
            PackageInstallerLog.Install.Error("Failed to install " + failedPackageName + ": " + errorMessage);
        }

        private void UpdateRemoveRequest()
        {
            if (_currentRemoveRequest == null || !_currentRemoveRequest.IsCompleted)
            {
                return;
            }

            PackageDefinition packageDefinition = _currentRemovePackage;
            string packageName = packageDefinition != null ? packageDefinition.DisplayName : "package";

            if (_currentRemoveRequest.IsSuccess)
            {
                string message = "Removed " + packageName + ".";
                CompleteCurrentRemoveRequest(true, message);
                PackageInstallerLog.Install.Info(message);
                return;
            }

            string errorMessage = string.IsNullOrWhiteSpace(_currentRemoveRequest.ErrorMessage)
                ? "Package Manager returned an unknown error."
                : _currentRemoveRequest.ErrorMessage;

            CompleteCurrentRemoveRequest(false, errorMessage);
            PackageInstallerLog.Install.Error("Failed to remove " + packageName + ": " + errorMessage);
        }

        private void CompleteCurrentRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentInstall != null ? _currentInstall.PackageDefinition : null;

            ReconcileSelfUpdateAfterInstallForTests(completedPackage, success);

            if (completedPackage != null)
            {
                _queuedOrInstallingPackageIds.Remove(completedPackage.PackageId);
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRequest = null;
            _currentInstall = null;
            State = PackageInstallRequestState.Idle;

            InstallCompleted?.Invoke(completedPackage, success, message);
            if (!success)
            {
                BlockInstallsWithFailedPrerequisites();
            }

            if (!_cancelRequested)
            {
                StartNextRequestIfNeeded();
            }

            if (_currentRequest == null && _installQueue.Count == 0)
            {
                EditorApplication.update -= Update;
                SetOperationCompleteSummary();
                _cancelRequested = false;
                ClearSavedOperationState(_operationStateRepository);
                QueueCompleted?.Invoke();
            }
            else
            {
                SavePendingOperationState();
            }

            NotifyStateChanged();
        }

        private void CompleteCurrentRemoveRequest(bool success, string message)
        {
            PackageDefinition completedPackage = _currentRemovePackage;

            if (completedPackage != null)
            {
                MarkProgressItem(
                    completedPackage,
                    success ? PackageInstallProgressItemState.Completed : PackageInstallProgressItemState.Failed,
                    message);
            }

            _currentRemoveRequest = null;
            _currentRemovePackage = null;
            State = PackageInstallRequestState.Idle;
            EditorApplication.update -= Update;
            SetOperationCompleteSummary();
            _cancelRequested = false;
            ClearSavedOperationState(_operationStateRepository);
            QueueCompleted?.Invoke();
            NotifyStateChanged();
        }

        private void BeginOperation(
            string operationName,
            IEnumerable<PackageDefinition> packages,
            IEnumerable<string> operationMessages = null)
        {
            PackageDependencyInstallStep[] steps = (packages ?? Array.Empty<PackageDefinition>())
                .Where(package => package != null)
                .Select(package => new PackageDependencyInstallStep(
                    package,
                    PackageChannel.Stable,
                    isDependency: false,
                    targetUrl: package.GetUrl(PackageChannel.Stable),
                    rootPackageIds: new[] { package.PackageId },
                    rootPaths: new[] { package.DisplayName }))
                .ToArray();
            BeginOperation(
                operationName,
                PackageDependencyInstallPlan.Success(steps, operationMessages));
        }

        private void BeginOperation(
            string operationName,
            PackageDependencyInstallPlan plan)
        {
            _currentOperationName = operationName ?? string.Empty;
            _currentOperationId = plan != null ? plan.OperationId : Guid.NewGuid().ToString("N");
            _currentRegistryFingerprint = plan != null ? plan.RegistryFingerprint : string.Empty;
            _currentOperationCreatedAtUtcTicks = plan != null
                ? plan.CreatedAtUtcTicks
                : DateTime.UtcNow.Ticks;
            _lastStatusMessage = "Queued " + _currentOperationName + ".";
            _lastErrorMessage = string.Empty;
            _cancelRequested = false;
            _operationCanceled = false;
            _completionActivityRecorded = false;
            _completedSteps = 0;
            _successfulSteps = 0;
            _failedSteps = 0;
            _skippedSteps = 0;
            _blockedSteps = 0;
            _canceledSteps = 0;
            _totalSteps = 0;
            _installQueue.Clear();
            _queuedOrInstallingPackageIds.Clear();
            _progressItems.Clear();
            _operationMessages.Clear();
            _progressItemsByPackageId.Clear();
            _operationInstallsByPackageId.Clear();
            _currentRootRequests.Clear();

            foreach (PackageOperationRootRequest rootRequest in plan != null
                         ? plan.RootRequests
                         : Array.Empty<PackageOperationRootRequest>())
            {
                if (rootRequest != null && !string.IsNullOrWhiteSpace(rootRequest.PackageId))
                {
                    _currentRootRequests.Add(new PackageOperationRootRequest(
                        rootRequest.PackageId,
                        rootRequest.Channel));
                }
            }

            foreach (string message in plan != null
                         ? plan.Messages
                         : Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _operationMessages.Add(message.Trim());
                }
            }

            foreach (PackageDependencyInstallStep step in plan != null
                         ? plan.Steps
                         : Array.Empty<PackageDependencyInstallStep>())
            {
                PackageDefinition packageDefinition = step != null ? step.PackageDefinition : null;
                if (packageDefinition == null)
                {
                    continue;
                }

                PackageInstallProgressItem item = new PackageInstallProgressItem(
                    packageDefinition.PackageId,
                    packageDefinition.DisplayName);

                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
            }

            _totalSteps = _progressItems.Count;
        }

        private void MarkProgressItem(
            PackageDefinition packageDefinition,
            PackageInstallProgressItemState state,
            string message)
        {
            if (packageDefinition == null)
            {
                return;
            }

            if (!_progressItemsByPackageId.TryGetValue(packageDefinition.PackageId, out PackageInstallProgressItem item))
            {
                item = new PackageInstallProgressItem(packageDefinition.PackageId, packageDefinition.DisplayName);
                _progressItems.Add(item);
                _progressItemsByPackageId[packageDefinition.PackageId] = item;
                _totalSteps = _progressItems.Count;
            }

            PackageInstallProgressItemState previousState = item.State;
            item.State = state;
            item.Message = message ?? string.Empty;

            if (IsTerminalState(state) && !IsTerminalState(previousState))
            {
                _completedSteps++;

                if (state == PackageInstallProgressItemState.Completed)
                {
                    _successfulSteps++;
                }
                else if (state == PackageInstallProgressItemState.Failed)
                {
                    _failedSteps++;
                    _lastErrorMessage = message ?? string.Empty;
                }
                else if (state == PackageInstallProgressItemState.Blocked)
                {
                    _blockedSteps++;
                }
                else if (state == PackageInstallProgressItemState.Canceled)
                {
                    _canceledSteps++;
                }
                else
                {
                    _skippedSteps++;
                }
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                _lastStatusMessage = message;
            }
        }

        private void CompleteOperationIfIdle()
        {
            if (_currentRequest != null || _currentRemoveRequest != null || _installQueue.Count > 0)
            {
                return;
            }

            SetOperationCompleteSummary();
        }

        private void SetOperationCompleteSummary()
        {
            if (!HasProgress)
            {
                return;
            }

            if (_operationCanceled)
            {
                _lastStatusMessage = _currentOperationName + " canceled" + FormatOperationOutcomeSuffix() + ".";
                RecordCompletionActivity(PackageInstallerActivitySeverity.Warning);
                return;
            }

            if (_failedSteps > 0 || _blockedSteps > 0)
            {
                _lastStatusMessage = _currentOperationName + " finished" +
                                     FormatOperationOutcomeSuffix() + ".";
                RecordCompletionActivity(PackageInstallerActivitySeverity.Error);
                return;
            }

            _lastStatusMessage = _currentOperationName + " completed successfully" +
                                 FormatSkippedSummarySuffix() + ".";
            RecordCompletionActivity(PackageInstallerActivitySeverity.Success);
        }

        private void RecordCompletionActivity(PackageInstallerActivitySeverity severity)
        {
            if (_completionActivityRecorded || string.IsNullOrWhiteSpace(_lastStatusMessage))
            {
                return;
            }

            _completionActivityRecorded = true;
            _terminalOperationSnapshot = CreateTerminalOperationSnapshot(severity);
            List<string> details = new List<string>(_operationMessages);
            details.AddRange(_progressItems
                .Where(item => item != null && IsTerminalState(item.State))
                .Select(item =>
                    item.State + ": " +
                    (string.IsNullOrWhiteSpace(item.Message)
                        ? item.DisplayName
                        : item.Message)));
            PackageInstallerActivityService.Record(
                "Packages",
                severity,
                _lastStatusMessage,
                details.Count > 0 ? string.Join("\n", details.ToArray()) : string.Empty,
                packageId: _terminalOperationSnapshot.RestartRoots.Count == 1
                    ? _terminalOperationSnapshot.RestartRoots[0].PackageId
                    : string.Empty,
                retryKind: _terminalOperationSnapshot.CanRestart
                    ? PackageInstallerRetryKind.RestartOperation
                    : PackageInstallerRetryKind.None);
        }

        private PackageOperationTerminalSnapshot CreateTerminalOperationSnapshot(
            PackageInstallerActivitySeverity severity)
        {
            PackageOperationTerminalOutcome outcome = _operationCanceled
                ? PackageOperationTerminalOutcome.Canceled
                : severity == PackageInstallerActivitySeverity.Error
                    ? PackageOperationTerminalOutcome.Failed
                    : PackageOperationTerminalOutcome.Succeeded;
            List<PackageOperationStepSnapshot> stepSnapshots = new List<PackageOperationStepSnapshot>();

            foreach (PackageInstallProgressItem progress in _progressItems.Where(item => item != null))
            {
                _operationInstallsByPackageId.TryGetValue(
                    progress.PackageId,
                    out QueuedPackageInstall install);
                stepSnapshots.Add(new PackageOperationStepSnapshot(
                    progress.PackageId,
                    progress.DisplayName,
                    install != null ? install.Channel : PackageChannel.Stable,
                    install != null ? install.Url : string.Empty,
                    install != null && install.IsDependency,
                    install != null ? install.RootPackageIds : Array.Empty<string>(),
                    progress.State,
                    progress.Message));
            }

            List<string> affectedRootIds = new List<string>();
            HashSet<string> seenRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageOperationStepSnapshot step in stepSnapshots.Where(IsRetryableTerminalStep))
            {
                foreach (string rootId in step.RootPackageIds)
                {
                    if (seenRootIds.Add(rootId))
                    {
                        affectedRootIds.Add(rootId);
                    }
                }
            }

            List<PackageOperationRootRequest> restartRoots = new List<PackageOperationRootRequest>();
            foreach (string rootId in affectedRootIds)
            {
                PackageOperationRootRequest requestedRoot = _currentRootRequests.FirstOrDefault(
                    root => string.Equals(
                        root.PackageId,
                        rootId,
                        StringComparison.OrdinalIgnoreCase));
                PackageChannel channel = requestedRoot != null
                    ? requestedRoot.Channel
                    : _operationInstallsByPackageId.TryGetValue(
                        rootId,
                        out QueuedPackageInstall rootInstall)
                        ? rootInstall.Channel
                        : _operationInstallsByPackageId.Values
                            .Where(install => install != null &&
                                              install.RootPackageIds.Contains(
                                                  rootId,
                                                  StringComparer.OrdinalIgnoreCase))
                            .Select(install => install.Channel)
                            .DefaultIfEmpty(PackageChannel.Stable)
                            .First();
                restartRoots.Add(new PackageOperationRootRequest(rootId, channel));
            }

            return new PackageOperationTerminalSnapshot(
                _currentOperationId,
                _currentOperationName,
                outcome,
                _lastStatusMessage,
                _lastErrorMessage,
                restartRoots,
                stepSnapshots,
                _operationMessages,
                DateTime.UtcNow);
        }

        private static bool IsRetryableTerminalStep(PackageOperationStepSnapshot step)
        {
            return step != null &&
                   (step.State == PackageInstallProgressItemState.Failed ||
                    step.State == PackageInstallProgressItemState.Blocked ||
                    step.State == PackageInstallProgressItemState.Canceled);
        }

        private string FormatSkippedSummarySuffix()
        {
            return _skippedSteps > 0 ? " and " + _skippedSteps + " skipped" : string.Empty;
        }

        private string FormatOperationOutcomeSuffix()
        {
            List<string> parts = new List<string>();

            if (_successfulSteps > 0)
            {
                parts.Add(_successfulSteps + " succeeded");
            }

            if (_failedSteps > 0)
            {
                parts.Add(_failedSteps + " failed");
            }

            if (_skippedSteps > 0)
            {
                parts.Add(_skippedSteps + " skipped");
            }

            if (_blockedSteps > 0)
            {
                parts.Add(_blockedSteps + " blocked");
            }

            if (_canceledSteps > 0)
            {
                parts.Add(_canceledSteps + " canceled");
            }

            return parts.Count > 0 ? " with " + string.Join(", ", parts.ToArray()) : string.Empty;
        }

        private void CancelQueuedInstalls(string message)
        {
            while (_installQueue.Count > 0)
            {
                QueuedPackageInstall install = _installQueue[0];
                _installQueue.RemoveAt(0);

                if (install == null || install.PackageDefinition == null)
                {
                    continue;
                }

                _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                MarkProgressItem(
                    install.PackageDefinition,
                    PackageInstallProgressItemState.Canceled,
                    message);
            }
        }

        private static bool IsTerminalState(PackageInstallProgressItemState state)
        {
            return state == PackageInstallProgressItemState.Completed ||
                   state == PackageInstallProgressItemState.Failed ||
                   state == PackageInstallProgressItemState.Skipped ||
                   state == PackageInstallProgressItemState.Blocked ||
                   state == PackageInstallProgressItemState.Canceled ||
                   state == PackageInstallProgressItemState.AlreadyCorrect;
        }

        private bool CanStartInstall(QueuedPackageInstall install)
        {
            if (install == null)
            {
                return false;
            }

            foreach (string prerequisitePackageId in install.PrerequisitePackageIds)
            {
                if (!_progressItemsByPackageId.TryGetValue(
                        prerequisitePackageId,
                        out PackageInstallProgressItem prerequisite))
                {
                    return false;
                }

                if (prerequisite.State != PackageInstallProgressItemState.Completed &&
                    prerequisite.State != PackageInstallProgressItemState.AlreadyCorrect)
                {
                    return false;
                }
            }

            return true;
        }

        private void BlockInstallsWithFailedPrerequisites()
        {
            bool changed;

            do
            {
                changed = false;

                foreach (QueuedPackageInstall install in _installQueue.ToArray())
                {
                    string failedPrerequisiteId = install.PrerequisitePackageIds.FirstOrDefault(
                        prerequisiteId =>
                            _progressItemsByPackageId.TryGetValue(
                                prerequisiteId,
                                out PackageInstallProgressItem prerequisite) &&
                            (prerequisite.State == PackageInstallProgressItemState.Failed ||
                             prerequisite.State == PackageInstallProgressItemState.Blocked ||
                             prerequisite.State == PackageInstallProgressItemState.Canceled));

                    if (string.IsNullOrWhiteSpace(failedPrerequisiteId))
                    {
                        continue;
                    }

                    _installQueue.Remove(install);
                    _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                    MarkProgressItem(
                        install.PackageDefinition,
                        PackageInstallProgressItemState.Blocked,
                        "Blocked because prerequisite " + failedPrerequisiteId + " did not complete.");
                    changed = true;
                }
            }
            while (changed);
        }

        private void BlockUnresolvableInstalls()
        {
            foreach (QueuedPackageInstall install in _installQueue.ToArray())
            {
                _installQueue.Remove(install);
                _queuedOrInstallingPackageIds.Remove(install.PackageDefinition.PackageId);
                MarkProgressItem(
                    install.PackageDefinition,
                    PackageInstallProgressItemState.Blocked,
                    "Blocked because the prerequisite graph could not be satisfied.");
            }
        }

        private void SavePendingOperationState()
        {
            if (!IsBusy || State == PackageInstallRequestState.Removing)
            {
                ClearSavedOperationState(_operationStateRepository);
                return;
            }

            PackageOperationRecoveryStep[] steps = _progressItems
                .Where(item => item != null &&
                               _operationInstallsByPackageId.ContainsKey(item.PackageId))
                .Select(item =>
                {
                    QueuedPackageInstall install = _operationInstallsByPackageId[item.PackageId];
                    return new PackageOperationRecoveryStep(
                        item.PackageId,
                        item.DisplayName,
                        install.Channel,
                        install.Url,
                        install.IsDependency,
                        install.PrerequisitePackageIds,
                        install.RootPackageIds,
                        install.RootPaths,
                        install.DependencyReason,
                        item.State,
                        item.Message,
                        install.Step.DetectedCurrentSource,
                        install.Step.DetectedCurrentVersion,
                        install.Step.DetectedCurrentIdentity);
                })
                .ToArray();

            if (steps.Length == 0)
            {
                ClearSavedOperationState(_operationStateRepository);
                return;
            }

            PackageOperationRecoveryRecord record = new PackageOperationRecoveryRecord(
                _currentOperationId,
                _currentOperationName,
                _currentRegistryFingerprint,
                _currentOperationCreatedAtUtcTicks,
                DateTime.UtcNow.Ticks,
                steps,
                _operationMessages,
                _currentRootRequests);

            if (!_operationStateRepository.Save(record, out string errorMessage))
            {
                PackageInstallerLog.Install.Warning(errorMessage);
            }

            ClearLegacySavedOperationState();
        }

        private static void ClearSavedOperationState(PackageOperationStateRepository repository)
        {
            repository?.Clear();
            ClearLegacySavedOperationState();
        }

        private static void ClearLegacySavedOperationState()
        {
            SessionState.SetString(PendingOperationNameKey, string.Empty);
            SessionState.SetString(PendingQueueKey, string.Empty);
        }

        private static bool TryLoadSavedOperation(
            PackageOperationStateRepository repository,
            out PackageOperationRecoveryRecord record)
        {
            record = null;

            if (repository.TryLoad(out record, out string repositoryError))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(repositoryError))
            {
                PackageInstallerLog.Install.Warning(repositoryError);
                return false;
            }

            string operationName = SessionState.GetString(PendingOperationNameKey, string.Empty);
            string queue = SessionState.GetString(PendingQueueKey, string.Empty);

            if (string.IsNullOrWhiteSpace(queue))
            {
                return false;
            }

            List<PackageOperationRecoveryStep> legacySteps = new List<PackageOperationRecoveryStep>();

            foreach (string line in queue.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|');

                if (parts.Length < 2 ||
                    !PackageRegistryProvider.TryGetPackage(parts[0], out PackageDefinition packageDefinition) ||
                    !int.TryParse(parts[1], out int channelValue))
                {
                    continue;
                }

                PackageChannel channel = Enum.IsDefined(typeof(PackageChannel), channelValue)
                    ? (PackageChannel)channelValue
                    : PackageChannel.Stable;
                string url = packageDefinition.GetUrl(channel);

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                legacySteps.Add(new PackageOperationRecoveryStep(
                    packageDefinition.PackageId,
                    packageDefinition.DisplayName,
                    channel,
                    url,
                    isDependency: false,
                    prerequisitePackageIds: Array.Empty<string>(),
                    rootPackageIds: new[] { packageDefinition.PackageId },
                    rootPaths: new[] { packageDefinition.DisplayName },
                    dependencyReason: string.Empty,
                    state: PackageInstallProgressItemState.Pending,
                    message: string.Empty));
            }

            if (legacySteps.Count == 0)
            {
                return false;
            }

            record = new PackageOperationRecoveryRecord(
                Guid.NewGuid().ToString("N"),
                operationName,
                string.Empty,
                DateTime.UtcNow.Ticks,
                DateTime.UtcNow.Ticks,
                legacySteps,
                Array.Empty<string>());

            if (!repository.Save(record, out string saveError))
            {
                PackageInstallerLog.Install.Warning(saveError);
            }
            else
            {
                ClearLegacySavedOperationState();
            }

            return true;
        }

        private static bool TryPrepareSavedOperationForResume(
            PackageOperationStateRepository repository,
            bool selfUpdateAppliedOnReload,
            out PackageOperationRecoveryRecord record)
        {
            if (!TryLoadSavedOperation(repository, out record))
            {
                ClearSavedOperationState(repository);

                if (selfUpdateAppliedOnReload)
                {
                    PackageInstallerSelfUpdateState.AcknowledgeApplied();
                }

                return false;
            }

            IEnumerable<PackageOperationRecoveryStep> preparedSteps = record.Steps;

            if (selfUpdateAppliedOnReload)
            {
                preparedSteps = FilterAppliedSelfUpdate(preparedSteps);
            }

            PackageOperationRecoveryStep[] normalizedSteps = preparedSteps
                .Where(step => step != null)
                .Select(step => NormalizeStepForResume(step, selfUpdateAppliedOnReload))
                .ToArray();
            PackageOperationRootRequest[] normalizedRootRequests = record.RootRequests
                .Where(root => normalizedSteps.Any(step => step.RootPackageIds.Contains(
                    root.PackageId,
                    StringComparer.OrdinalIgnoreCase)))
                .ToArray();

            if (!normalizedSteps.Any(step => IsResumableState(step.State)))
            {
                ClearSavedOperationState(repository);

                if (selfUpdateAppliedOnReload)
                {
                    PackageInstallerSelfUpdateState.AcknowledgeApplied();
                }

                record = null;
                return false;
            }

            record = new PackageOperationRecoveryRecord(
                record.OperationId,
                record.OperationName,
                record.RegistryFingerprint,
                record.CreatedAtUtcTicks,
                DateTime.UtcNow.Ticks,
                normalizedSteps,
                record.Messages,
                normalizedRootRequests);

            if (!repository.Save(record, out string saveError))
            {
                PackageInstallerLog.Install.Warning(saveError);
                return false;
            }

            if (selfUpdateAppliedOnReload)
            {
                PackageInstallerSelfUpdateState.AcknowledgeApplied();
            }

            return true;
        }

        private static IEnumerable<PackageOperationRecoveryStep> FilterAppliedSelfUpdate(
            IEnumerable<PackageOperationRecoveryStep> steps)
        {
            return (steps ?? Array.Empty<PackageOperationRecoveryStep>())
                .Where(step =>
                    step != null &&
                    !PackageInstallerRuntimeIdentity.IsSelf(step.PackageId))
                .ToArray();
        }

        private static PackageOperationRecoveryStep NormalizeStepForResume(
            PackageOperationRecoveryStep step,
            bool selfUpdateAppliedOnReload)
        {
            PackageInstallProgressItemState state = step.State == PackageInstallProgressItemState.Active
                ? PackageInstallProgressItemState.Pending
                : step.State;
            string[] prerequisites = selfUpdateAppliedOnReload
                ? step.PrerequisitePackageIds
                    .Where(id => !PackageInstallerRuntimeIdentity.IsSelf(id))
                    .ToArray()
                : step.PrerequisitePackageIds.ToArray();

            return new PackageOperationRecoveryStep(
                step.PackageId,
                step.DisplayName,
                step.Channel,
                step.TargetUrl,
                step.IsDependency,
                prerequisites,
                step.RootPackageIds,
                step.RootPaths,
                step.DependencyReason,
                state,
                step.Message,
                step.DetectedCurrentSource,
                step.DetectedCurrentVersion,
                step.DetectedCurrentIdentity);
        }

        private static bool IsResumableState(PackageInstallProgressItemState state)
        {
            return state == PackageInstallProgressItemState.Pending ||
                   state == PackageInstallProgressItemState.Active;
        }

        private void RestoreOperation(PackageOperationRecoveryRecord record)
        {
            List<PackageDependencyInstallStep> planSteps = new List<PackageDependencyInstallStep>();
            Dictionary<string, PackageDefinition> definitions =
                new Dictionary<string, PackageDefinition>(StringComparer.OrdinalIgnoreCase);

            foreach (PackageOperationRecoveryStep recoveryStep in record.Steps)
            {
                PackageDefinition packageDefinition = CreateRecoveredPackageDefinition(recoveryStep);
                definitions[recoveryStep.PackageId] = packageDefinition;
                planSteps.Add(new PackageDependencyInstallStep(
                    packageDefinition,
                    recoveryStep.Channel,
                    recoveryStep.IsDependency,
                    recoveryStep.TargetUrl,
                    recoveryStep.PrerequisitePackageIds,
                    recoveryStep.RootPackageIds,
                    recoveryStep.RootPaths,
                    recoveryStep.DependencyReason,
                    recoveryStep.DetectedCurrentSource,
                    recoveryStep.DetectedCurrentVersion,
                    recoveryStep.DetectedCurrentIdentity));
            }

            PackageDependencyInstallPlan plan = PackageDependencyInstallPlan.Restore(
                record.OperationId,
                record.RegistryFingerprint,
                record.CreatedAtUtcTicks,
                planSteps,
                record.Messages,
                record.RootRequests);
            BeginOperation(
                string.IsNullOrWhiteSpace(record.OperationName)
                    ? "Resume Package Operation"
                    : record.OperationName,
                plan);

            foreach (PackageOperationRecoveryStep recoveryStep in record.Steps)
            {
                PackageDependencyInstallStep step = plan.Steps.First(planStep =>
                    string.Equals(
                        planStep.PackageDefinition.PackageId,
                        recoveryStep.PackageId,
                        StringComparison.OrdinalIgnoreCase));
                QueuedPackageInstall install = new QueuedPackageInstall(step);
                _operationInstallsByPackageId[recoveryStep.PackageId] = install;

                if (IsResumableState(recoveryStep.State))
                {
                    if (ExactTargetAlreadyInstalled != null &&
                        ExactTargetAlreadyInstalled(
                            recoveryStep.PackageId,
                            recoveryStep.TargetUrl,
                            recoveryStep.DetectedCurrentIdentity))
                    {
                        MarkProgressItem(
                            definitions[recoveryStep.PackageId],
                            PackageInstallProgressItemState.AlreadyCorrect,
                            "Already at the saved exact target after refresh.");
                        continue;
                    }

                    _installQueue.Add(install);
                    _queuedOrInstallingPackageIds.Add(recoveryStep.PackageId);
                    continue;
                }

                MarkProgressItem(
                    definitions[recoveryStep.PackageId],
                    recoveryStep.State,
                    recoveryStep.Message);
            }
        }

        private static PackageDefinition CreateRecoveredPackageDefinition(
            PackageOperationRecoveryStep step)
        {
            string displayName = string.IsNullOrWhiteSpace(step.DisplayName)
                ? step.PackageId
                : step.DisplayName;
            string developmentUrl = step.Channel == PackageChannel.Development
                ? step.TargetUrl
                : string.Empty;

            return new PackageDefinition(
                displayName,
                step.PackageId,
                step.TargetUrl,
                string.Empty,
                Array.Empty<string>(),
                PackageType.Core,
                developmentUrl,
                category: "Tools");
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }

        private sealed class QueuedPackageInstall
        {
            public QueuedPackageInstall(PackageDependencyInstallStep step)
            {
                Step = step ?? throw new ArgumentNullException(nameof(step));
            }

            public PackageDependencyInstallStep Step { get; }

            public PackageDefinition PackageDefinition => Step.PackageDefinition;

            public PackageChannel Channel => Step.Channel;

            public string Url => Step.TargetUrl;

            public bool IsDependency => Step.IsDependency;

            public IReadOnlyList<string> PrerequisitePackageIds => Step.PrerequisitePackageIds;

            public IReadOnlyList<string> RootPackageIds => Step.RootPackageIds;

            public IReadOnlyList<string> RootPaths => Step.RootPaths;

            public string DependencyReason => Step.DependencyReason;
        }
    }
}
