using System;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    // Presentation only: exact targets and recovery decisions stay in the operation service.
    [Serializable]
    internal sealed class PackageOperationQueueSnapshot
    {
        public string operationName;
        public bool waitingForRecovery;
        public PackageOperationQueueItem[] items = Array.Empty<PackageOperationQueueItem>();

        internal static PackageOperationQueueSnapshot Capture(PackageInstallService service)
        {
            if (service == null || service.ProgressItems.Count == 0) return null;
            return new PackageOperationQueueSnapshot
            {
                operationName = service.CurrentOperationName,
                items = service.ProgressItems.Select(item => new PackageOperationQueueItem
                {
                    packageId = item.PackageId, displayName = item.DisplayName, state = item.State
                }).ToArray()
            };
        }

        internal static PackageOperationQueueSnapshot Capture(PackageOperationRecoveryRecord recovery)
        {
            if (recovery == null) return null;
            return new PackageOperationQueueSnapshot
            {
                operationName = recovery.OperationName,
                waitingForRecovery = true,
                items = recovery.Steps.Select(step => new PackageOperationQueueItem
                {
                    packageId = step.PackageId, displayName = step.DisplayName, state = step.State
                }).ToArray()
            };
        }

        internal string Summary
        {
            get
            {
                var rows = items ?? Array.Empty<PackageOperationQueueItem>();
                int completed = rows.Count(item => item != null &&
                    (item.state == PackageInstallProgressItemState.Completed ||
                     item.state == PackageInstallProgressItemState.AlreadyCorrect));
                int current = rows.Count(item => item?.state == PackageInstallProgressItemState.Active);
                int remaining = rows.Count(item => item?.state == PackageInstallProgressItemState.Pending);
                int stopped = rows.Length - completed - current - remaining;
                return completed + " completed · " + current + " current · " + remaining + " remaining" +
                       (stopped > 0 ? " · " + stopped + " stopped or skipped" : string.Empty);
            }
        }

        internal float Progress => items == null || items.Length == 0 ? 0f :
            items.Count(item => item != null && item.state != PackageInstallProgressItemState.Pending &&
                item.state != PackageInstallProgressItemState.Active) / (float)items.Length;
    }

    [Serializable]
    internal sealed class PackageOperationQueueItem
    {
        public string packageId;
        public string displayName;
        public PackageInstallProgressItemState state;

        internal string Status
        {
            get
            {
                switch (state)
                {
                    case PackageInstallProgressItemState.Active: return "Current";
                    case PackageInstallProgressItemState.Completed: return "Completed";
                    case PackageInstallProgressItemState.AlreadyCorrect: return "Completed · already installed";
                    case PackageInstallProgressItemState.Failed: return "Failed";
                    case PackageInstallProgressItemState.Blocked: return "Blocked";
                    case PackageInstallProgressItemState.Canceled: return "Canceled";
                    case PackageInstallProgressItemState.Skipped: return "Skipped";
                    default: return "Remaining";
                }
            }
        }
    }
}
