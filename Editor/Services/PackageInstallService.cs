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
            string message,
            PackageChannel? requestedChannel = null)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Channel = channel;
            RequestedChannel = requestedChannel ?? channel;
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
        public PackageChannel RequestedChannel { get; }
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

    internal sealed partial class PackageInstallService : IDisposable
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
    }
}
