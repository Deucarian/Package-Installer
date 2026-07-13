using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageOperationRecoveryStep
    {
        public PackageOperationRecoveryStep(
            string packageId,
            string displayName,
            PackageChannel channel,
            string targetUrl,
            bool isDependency,
            IEnumerable<string> prerequisitePackageIds,
            IEnumerable<string> rootPackageIds,
            IEnumerable<string> rootPaths,
            string dependencyReason,
            PackageInstallProgressItemState state,
            string message,
            string detectedCurrentSource = null,
            string detectedCurrentVersion = null,
            string detectedCurrentIdentity = null)
        {
            PackageId = packageId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Channel = channel;
            TargetUrl = targetUrl ?? string.Empty;
            IsDependency = isDependency;
            PrerequisitePackageIds = ToArray(prerequisitePackageIds);
            RootPackageIds = ToArray(rootPackageIds);
            RootPaths = ToArray(rootPaths);
            DependencyReason = dependencyReason ?? string.Empty;
            State = state;
            Message = message ?? string.Empty;
            DetectedCurrentSource = detectedCurrentSource ?? string.Empty;
            DetectedCurrentVersion = detectedCurrentVersion ?? string.Empty;
            DetectedCurrentIdentity = detectedCurrentIdentity ?? string.Empty;
        }

        public string PackageId { get; }

        public string DisplayName { get; }

        public PackageChannel Channel { get; }

        public string TargetUrl { get; }

        public bool IsDependency { get; }

        public IReadOnlyList<string> PrerequisitePackageIds { get; }

        public IReadOnlyList<string> RootPackageIds { get; }

        public IReadOnlyList<string> RootPaths { get; }

        public string DependencyReason { get; }

        public PackageInstallProgressItemState State { get; }

        public string Message { get; }

        public string DetectedCurrentSource { get; }

        public string DetectedCurrentVersion { get; }

        public string DetectedCurrentIdentity { get; }

        private static string[] ToArray(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    internal sealed class PackageOperationRecoveryRecord
    {
        public PackageOperationRecoveryRecord(
            string operationId,
            string operationName,
            string registryFingerprint,
            long createdAtUtcTicks,
            long updatedAtUtcTicks,
            IEnumerable<PackageOperationRecoveryStep> steps,
            IEnumerable<string> messages,
            IEnumerable<PackageOperationRootRequest> rootRequests = null)
        {
            OperationId = operationId ?? string.Empty;
            OperationName = operationName ?? string.Empty;
            RegistryFingerprint = registryFingerprint ?? string.Empty;
            CreatedAtUtcTicks = createdAtUtcTicks;
            UpdatedAtUtcTicks = updatedAtUtcTicks;
            Steps = (steps ?? Array.Empty<PackageOperationRecoveryStep>())
                .Where(step => step != null)
                .ToArray();
            RootRequests = NormalizeRootRequests(rootRequests, Steps);
            Messages = (messages ?? Array.Empty<string>())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Select(message => message.Trim())
                .ToArray();
        }

        public string OperationId { get; }

        public string OperationName { get; }

        public string RegistryFingerprint { get; }

        public long CreatedAtUtcTicks { get; }

        public long UpdatedAtUtcTicks { get; }

        public IReadOnlyList<PackageOperationRecoveryStep> Steps { get; }

        public IReadOnlyList<PackageOperationRootRequest> RootRequests { get; }

        public IReadOnlyList<string> Messages { get; }

        public bool CanResume => Steps.Any(step =>
            step.State == PackageInstallProgressItemState.Pending ||
            step.State == PackageInstallProgressItemState.Active);

        public bool CanRestart => Steps.Count > 0;

        public bool HasFailures => Steps.Any(step =>
            step.State == PackageInstallProgressItemState.Failed ||
            step.State == PackageInstallProgressItemState.Blocked);

        public bool HasCompletedSteps => Steps.Any(step =>
            step.State == PackageInstallProgressItemState.Completed ||
            step.State == PackageInstallProgressItemState.AlreadyCorrect);

        private static IReadOnlyList<PackageOperationRootRequest> NormalizeRootRequests(
            IEnumerable<PackageOperationRootRequest> rootRequests,
            IEnumerable<PackageOperationRecoveryStep> steps)
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

            PackageOperationRecoveryStep[] recoverySteps =
                (steps ?? Array.Empty<PackageOperationRecoveryStep>())
                .Where(step => step != null)
                .ToArray();
            foreach (PackageOperationRecoveryStep step in recoverySteps.Where(step => !step.IsDependency))
            {
                if (seen.Add(step.PackageId))
                {
                    normalized.Add(new PackageOperationRootRequest(step.PackageId, step.Channel));
                }
            }

            foreach (PackageOperationRecoveryStep step in recoverySteps)
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

    internal sealed class PackageOperationStateRepository
    {
        internal const int CurrentSchemaVersion = 2;
        private const string RelativeStatePath = "Library/Deucarian/PackageInstaller/pending-operation.json";

        private readonly string _statePath;

        public PackageOperationStateRepository()
            : this(GetProjectRoot())
        {
        }

        internal PackageOperationStateRepository(string projectRoot)
        {
            string root = string.IsNullOrWhiteSpace(projectRoot) ? GetProjectRoot() : projectRoot;
            _statePath = Path.GetFullPath(Path.Combine(
                root,
                RelativeStatePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        internal string StatePathForTests => _statePath;

        public bool TryLoad(out PackageOperationRecoveryRecord record, out string errorMessage)
        {
            record = null;
            errorMessage = string.Empty;

            if (!File.Exists(_statePath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(_statePath);
                StorageRecord storage = JsonUtility.FromJson<StorageRecord>(json);

                if (storage == null)
                {
                    errorMessage = "Pending package operation state is empty.";
                    return false;
                }

                if (storage.schemaVersion != CurrentSchemaVersion)
                {
                    errorMessage = "Unsupported pending package operation schemaVersion: " +
                                   storage.schemaVersion + ".";
                    return false;
                }

                StorageStep[] storageSteps = storage.steps ?? Array.Empty<StorageStep>();
                List<PackageOperationRecoveryStep> steps = new List<PackageOperationRecoveryStep>();
                StorageRootRequest[] storageRootRequests =
                    storage.rootRequests ?? Array.Empty<StorageRootRequest>();
                List<PackageOperationRootRequest> rootRequests =
                    new List<PackageOperationRootRequest>();
                HashSet<string> rootRequestIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (storageRootRequests.Length == 0)
                {
                    errorMessage = "Pending package operation contains no root requests.";
                    return false;
                }

                foreach (StorageRootRequest rootRequest in storageRootRequests)
                {
                    if (rootRequest == null || string.IsNullOrWhiteSpace(rootRequest.packageId))
                    {
                        errorMessage = "Pending package operation contains an invalid root request.";
                        return false;
                    }

                    if (!Enum.IsDefined(typeof(PackageChannel), rootRequest.channel))
                    {
                        errorMessage = "Pending package operation root request has an invalid channel.";
                        return false;
                    }

                    string rootPackageId = rootRequest.packageId.Trim();
                    if (!rootRequestIds.Add(rootPackageId))
                    {
                        errorMessage = "Pending package operation contains duplicate root requests for " +
                                       rootPackageId + ".";
                        return false;
                    }

                    rootRequests.Add(new PackageOperationRootRequest(
                        rootPackageId,
                        (PackageChannel)rootRequest.channel));
                }

                HashSet<string> stepPackageIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (StorageStep step in storageSteps)
                {
                    if (step == null || string.IsNullOrWhiteSpace(step.packageId) ||
                        string.IsNullOrWhiteSpace(step.targetUrl))
                    {
                        errorMessage = "Pending package operation contains an invalid step.";
                        return false;
                    }

                    string packageId = step.packageId.Trim();
                    if (!stepPackageIds.Add(packageId))
                    {
                        errorMessage = "Pending package operation contains duplicate steps for " +
                                       packageId + ".";
                        return false;
                    }

                    if (!Enum.IsDefined(typeof(PackageChannel), step.channel))
                    {
                        errorMessage = "Pending package operation step " + packageId +
                                       " has an invalid channel.";
                        return false;
                    }

                    if (!Enum.IsDefined(typeof(PackageInstallProgressItemState), step.state))
                    {
                        errorMessage = "Pending package operation step " + packageId +
                                       " has an invalid state.";
                        return false;
                    }

                    if (!TryReadDistinctPackageIds(
                            step.prerequisitePackageIds,
                            allowEmpty: true,
                            "prerequisite",
                            packageId,
                            out string[] prerequisitePackageIds,
                            out errorMessage) ||
                        !TryReadDistinctPackageIds(
                            step.rootPackageIds,
                            allowEmpty: false,
                            "root",
                            packageId,
                            out string[] rootPackageIds,
                            out errorMessage))
                    {
                        return false;
                    }

                    steps.Add(new PackageOperationRecoveryStep(
                        packageId,
                        step.displayName,
                        (PackageChannel)step.channel,
                        step.targetUrl.Trim(),
                        step.isDependency,
                        prerequisitePackageIds,
                        rootPackageIds,
                        step.rootPaths,
                        step.dependencyReason,
                        (PackageInstallProgressItemState)step.state,
                        step.message,
                        step.detectedCurrentSource,
                        step.detectedCurrentVersion,
                        step.detectedCurrentIdentity));
                }

                if (steps.Count == 0)
                {
                    errorMessage = "Pending package operation contains no steps.";
                    return false;
                }

                foreach (PackageOperationRecoveryStep step in steps)
                {
                    foreach (string prerequisitePackageId in step.PrerequisitePackageIds)
                    {
                        if (string.Equals(
                                prerequisitePackageId,
                                step.PackageId,
                                StringComparison.OrdinalIgnoreCase) ||
                            !stepPackageIds.Contains(prerequisitePackageId))
                        {
                            errorMessage = "Pending package operation step " + step.PackageId +
                                           " has an invalid prerequisite " + prerequisitePackageId + ".";
                            return false;
                        }
                    }

                    foreach (string rootPackageId in step.RootPackageIds)
                    {
                        if (!rootRequestIds.Contains(rootPackageId))
                        {
                            errorMessage = "Pending package operation step " + step.PackageId +
                                           " references unknown root " + rootPackageId + ".";
                            return false;
                        }
                    }
                }

                foreach (string rootPackageId in rootRequestIds)
                {
                    if (!steps.Any(step => step.RootPackageIds.Contains(
                            rootPackageId,
                            StringComparer.OrdinalIgnoreCase)))
                    {
                        errorMessage = "Pending package operation root request " + rootPackageId +
                                       " is not referenced by any step.";
                        return false;
                    }
                }

                record = new PackageOperationRecoveryRecord(
                    storage.operationId,
                    storage.operationName,
                    storage.registryFingerprint,
                    storage.createdAtUtcTicks,
                    storage.updatedAtUtcTicks,
                    steps,
                    storage.messages,
                    rootRequests);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not read pending package operation state: " +
                               exception.GetBaseException().Message;
                return false;
            }
        }

        public bool Save(PackageOperationRecoveryRecord record, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (!TryValidateRecord(record, out errorMessage))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(_statePath);
            string tempPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                StorageRecord storage = new StorageRecord
                {
                    schemaVersion = CurrentSchemaVersion,
                    operationId = record.OperationId,
                    operationName = record.OperationName,
                    registryFingerprint = record.RegistryFingerprint,
                    createdAtUtcTicks = record.CreatedAtUtcTicks,
                    updatedAtUtcTicks = record.UpdatedAtUtcTicks,
                    messages = record.Messages.ToArray(),
                    rootRequests = record.RootRequests.Select(root => new StorageRootRequest
                    {
                        packageId = root.PackageId,
                        channel = (int)root.Channel
                    }).ToArray(),
                    steps = record.Steps.Select(ToStorageStep).ToArray()
                };

                File.WriteAllText(tempPath, JsonUtility.ToJson(storage, true));

                if (File.Exists(_statePath))
                {
                    File.Replace(tempPath, _statePath, null);
                }
                else
                {
                    File.Move(tempPath, _statePath);
                }

                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not save pending package operation state: " +
                               exception.GetBaseException().Message;
                return false;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void Clear()
        {
            if (!File.Exists(_statePath))
            {
                return;
            }

            try
            {
                File.Delete(_statePath);
            }
            catch (Exception exception)
            {
                PackageInstallerLog.Install.Warning(
                    "Could not clear pending package operation state: " + exception.GetBaseException().Message);
            }
        }

        private static StorageStep ToStorageStep(PackageOperationRecoveryStep step)
        {
            return new StorageStep
            {
                packageId = step.PackageId,
                displayName = step.DisplayName,
                channel = (int)step.Channel,
                targetUrl = step.TargetUrl,
                isDependency = step.IsDependency,
                prerequisitePackageIds = step.PrerequisitePackageIds.ToArray(),
                rootPackageIds = step.RootPackageIds.ToArray(),
                rootPaths = step.RootPaths.ToArray(),
                dependencyReason = step.DependencyReason,
                state = (int)step.State,
                message = step.Message,
                detectedCurrentSource = step.DetectedCurrentSource,
                detectedCurrentVersion = step.DetectedCurrentVersion,
                detectedCurrentIdentity = step.DetectedCurrentIdentity
            };
        }

        private static bool TryReadDistinctPackageIds(
            IEnumerable<string> values,
            bool allowEmpty,
            string relationshipName,
            string packageId,
            out string[] packageIds,
            out string errorMessage)
        {
            List<string> normalized = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    packageIds = Array.Empty<string>();
                    errorMessage = "Pending package operation step " + packageId +
                                   " contains an invalid " + relationshipName + " package ID.";
                    return false;
                }

                string normalizedValue = value.Trim();
                if (!seen.Add(normalizedValue))
                {
                    packageIds = Array.Empty<string>();
                    errorMessage = "Pending package operation step " + packageId +
                                   " contains duplicate " + relationshipName + " package IDs.";
                    return false;
                }

                normalized.Add(normalizedValue);
            }

            if (!allowEmpty && normalized.Count == 0)
            {
                packageIds = Array.Empty<string>();
                errorMessage = "Pending package operation step " + packageId +
                               " contains no " + relationshipName + " package IDs.";
                return false;
            }

            packageIds = normalized.ToArray();
            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateRecord(
            PackageOperationRecoveryRecord record,
            out string errorMessage)
        {
            if (record == null || record.Steps.Count == 0)
            {
                errorMessage = "Cannot save an empty package operation.";
                return false;
            }

            if (record.RootRequests.Count == 0)
            {
                errorMessage = "Cannot save a package operation without root requests.";
                return false;
            }

            HashSet<string> rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageOperationRootRequest root in record.RootRequests)
            {
                if (root == null || string.IsNullOrWhiteSpace(root.PackageId) ||
                    !Enum.IsDefined(typeof(PackageChannel), root.Channel) ||
                    !rootIds.Add(root.PackageId))
                {
                    errorMessage = "Cannot save a package operation with invalid or duplicate root requests.";
                    return false;
                }
            }

            HashSet<string> stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PackageOperationRecoveryStep step in record.Steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.PackageId) ||
                    string.IsNullOrWhiteSpace(step.TargetUrl) ||
                    !Enum.IsDefined(typeof(PackageChannel), step.Channel) ||
                    !Enum.IsDefined(typeof(PackageInstallProgressItemState), step.State) ||
                    !stepIds.Add(step.PackageId))
                {
                    errorMessage = "Cannot save a package operation with invalid or duplicate steps.";
                    return false;
                }
            }

            foreach (PackageOperationRecoveryStep step in record.Steps)
            {
                if (step.RootPackageIds.Count == 0 ||
                    step.RootPackageIds.Any(rootId => !rootIds.Contains(rootId)) ||
                    step.PrerequisitePackageIds.Any(prerequisiteId =>
                        string.Equals(prerequisiteId, step.PackageId, StringComparison.OrdinalIgnoreCase) ||
                        !stepIds.Contains(prerequisiteId)))
                {
                    errorMessage = "Cannot save a package operation with invalid prerequisites or roots.";
                    return false;
                }
            }

            if (rootIds.Any(rootId => !record.Steps.Any(step => step.RootPackageIds.Contains(
                    rootId,
                    StringComparer.OrdinalIgnoreCase))))
            {
                errorMessage = "Cannot save a package operation with unreferenced root requests.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static string GetProjectRoot()
        {
            if (string.IsNullOrWhiteSpace(Application.dataPath))
            {
                return Directory.GetCurrentDirectory();
            }

            DirectoryInfo parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }

        [Serializable]
        private sealed class StorageRecord
        {
            public int schemaVersion;
            public string operationId;
            public string operationName;
            public string registryFingerprint;
            public long createdAtUtcTicks;
            public long updatedAtUtcTicks;
            public StorageRootRequest[] rootRequests;
            public StorageStep[] steps;
            public string[] messages;
        }

        [Serializable]
        private sealed class StorageRootRequest
        {
            public string packageId;
            public int channel;
        }

        [Serializable]
        private sealed class StorageStep
        {
            public string packageId;
            public string displayName;
            public int channel;
            public string targetUrl;
            public bool isDependency;
            public string[] prerequisitePackageIds;
            public string[] rootPackageIds;
            public string[] rootPaths;
            public string dependencyReason;
            public int state;
            public string message;
            public string detectedCurrentSource;
            public string detectedCurrentVersion;
            public string detectedCurrentIdentity;
        }
    }
}
