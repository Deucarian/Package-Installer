using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageSampleImportState
    {
        Unknown,
        Importing,
        Imported,
        AlreadyImported,
        Canceled,
        Failed
    }

    internal sealed class PackageSampleImportStatus
    {
        public PackageSampleImportStatus(PackageSampleImportState state, string message)
        {
            State = state;
            Message = message ?? string.Empty;
        }

        public PackageSampleImportState State { get; }

        public string Message { get; }
    }

    internal sealed class PackageSampleImportService : IDisposable
    {
        private readonly Dictionary<string, PackageSampleImportStatus> _statuses =
            new Dictionary<string, PackageSampleImportStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly PackageSampleStagingImporter _stagingImporter;
        private readonly Func<string, PackageManagerPackageInfo> _packageInfoResolver;
        private CancellationTokenSource _copyCancellation;
        private Task<PackageSampleStageResult> _copyTask;
        private string _activeStatusKey = string.Empty;
        private string _activeSampleDisplayName = string.Empty;
        private PackageDefinition _lastPackageDefinition;
        private PackageExtraDefinition _lastExtraDefinition;

        public PackageSampleImportService(
            PackageSampleStagingImporter stagingImporter = null,
            Func<string, PackageManagerPackageInfo> packageInfoResolver = null)
        {
            _stagingImporter = stagingImporter ?? new PackageSampleStagingImporter();
            _packageInfoResolver = packageInfoResolver ?? ResolveCurrentPackageInfo;
        }

        public event Action StateChanged;

        public bool IsBusy { get; private set; }

        public string CurrentOperationName { get; private set; } = string.Empty;

        public string CurrentExtraName { get; private set; } = string.Empty;

        public string LastStatusMessage { get; private set; } = string.Empty;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public PackageSampleImportStatus GetStatus(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            string key = GetStatusKey(packageDefinition, extraDefinition);

            if (_statuses.TryGetValue(key, out PackageSampleImportStatus status))
            {
                return status;
            }

            if (IsSampleImported(packageDefinition, extraDefinition, packageInfo))
            {
                return new PackageSampleImportStatus(
                    PackageSampleImportState.AlreadyImported,
                    "Sample already imported.");
            }

            return new PackageSampleImportStatus(PackageSampleImportState.Unknown, string.Empty);
        }

        public bool IsSampleImported(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageDefinition == null || extraDefinition == null || packageInfo == null)
            {
                return false;
            }

            if (TryFindUnitySample(packageDefinition, extraDefinition, packageInfo, out object sample) &&
                TryGetBoolMember(sample, "isImported", out bool isImported) &&
                isImported)
            {
                return true;
            }

            return DestinationExists(GetDestinationPath(packageDefinition, extraDefinition, packageInfo));
        }

        public void ImportSample(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageDefinition == null || extraDefinition == null || IsBusy)
            {
                return;
            }

            _lastPackageDefinition = packageDefinition;
            _lastExtraDefinition = extraDefinition;

            string key = GetStatusKey(packageDefinition, extraDefinition);
            IsBusy = true;
            CurrentOperationName = "Import Sample";
            CurrentExtraName = extraDefinition.DisplayName;
            LastStatusMessage = "Importing sample " + extraDefinition.DisplayName + "...";
            LastErrorMessage = string.Empty;
            _statuses[key] = new PackageSampleImportStatus(PackageSampleImportState.Importing, LastStatusMessage);
            NotifyStateChanged();

            try
            {
                if (extraDefinition.RequiresPackageInstalled && packageInfo == null)
                {
                    CompleteImport(
                        key,
                        PackageSampleImportState.Failed,
                        "Install the package before importing this sample.");
                    return;
                }

                if (IsSampleImported(packageDefinition, extraDefinition, packageInfo))
                {
                    CompleteImport(
                        key,
                        PackageSampleImportState.AlreadyImported,
                        "Sample already imported.");
                    return;
                }

                if (TryImportWithUnitySampleApi(packageDefinition, extraDefinition, packageInfo, out string unityMessage))
                {
                    CompleteImport(key, PackageSampleImportState.Imported, unityMessage);
                    return;
                }

                if (!TryPrepareCopyImport(
                        packageDefinition,
                        extraDefinition,
                        packageInfo,
                        out string sourcePath,
                        out string destinationPath,
                        out string stagingRootPath,
                        out string copyMessage))
                {
                    string message = string.IsNullOrWhiteSpace(unityMessage)
                        ? copyMessage
                        : unityMessage + " " + copyMessage;
                    CompleteImport(
                        key,
                        PackageSampleImportState.Failed,
                        string.IsNullOrWhiteSpace(message) ? "Import failed." : message.Trim());
                    return;
                }

                _activeStatusKey = key;
                _activeSampleDisplayName = extraDefinition.DisplayName;
                _copyCancellation = new CancellationTokenSource();
                CancellationToken cancellationToken = _copyCancellation.Token;
                _copyTask = Task.Run(() => _stagingImporter.Import(
                    sourcePath,
                    destinationPath,
                    stagingRootPath,
                    cancellationToken));
                EditorApplication.update -= UpdateCopyImport;
                EditorApplication.update += UpdateCopyImport;
            }
            catch (Exception exception)
            {
                CompleteImport(
                    key,
                    PackageSampleImportState.Failed,
                    "Sample import failed: " + exception.GetBaseException().Message);
            }
        }

        public bool RetryLastImport()
        {
            if (IsBusy || _lastPackageDefinition == null || _lastExtraDefinition == null)
            {
                return false;
            }

            PackageManagerPackageInfo currentPackageInfo =
                _packageInfoResolver(_lastPackageDefinition.PackageId);
            ImportSample(_lastPackageDefinition, _lastExtraDefinition, currentPackageInfo);
            return IsBusy || !string.IsNullOrWhiteSpace(LastStatusMessage);
        }

        public bool CancelCurrentImport()
        {
            if (!IsBusy || _copyTask == null || _copyCancellation == null)
            {
                return false;
            }

            if (!_copyCancellation.IsCancellationRequested)
            {
                _copyCancellation.Cancel();
                LastStatusMessage = "Cancel requested. Staged files will be discarded before commit.";
                NotifyStateChanged();
            }

            return true;
        }

        public void Dispose()
        {
            EditorApplication.update -= UpdateCopyImport;
            _copyCancellation?.Cancel();
            _copyCancellation?.Dispose();
            _copyCancellation = null;
            _copyTask = null;
        }

        public string GetDestinationPath(PackageDefinition packageDefinition, PackageExtraDefinition extraDefinition)
        {
            return GetDestinationPath(packageDefinition, extraDefinition, null);
        }

        public string GetDestinationPath(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (extraDefinition != null && !string.IsNullOrWhiteSpace(extraDefinition.DestinationPath))
            {
                return NormalizeAssetPath(extraDefinition.DestinationPath);
            }

            string packageFolder = SanitizeAssetPathSegment(GetPackageDisplayName(packageDefinition, packageInfo), "Package");
            string sampleFolder = SanitizeAssetPathSegment(extraDefinition != null
                ? extraDefinition.DisplayName
                : "Sample", "Sample");

            if (packageInfo != null)
            {
                string versionFolder = SanitizeAssetPathSegment(GetPackageVersion(packageDefinition, packageInfo), "Unknown Version");
                return "Assets/Samples/" + packageFolder + "/" + versionFolder + "/" + sampleFolder;
            }

            return "Assets/Samples/" + packageFolder + "/" + sampleFolder;
        }

        private bool TryImportWithUnitySampleApi(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out string message)
        {
            message = string.Empty;

            if (packageInfo == null ||
                !TryFindUnitySample(packageDefinition, extraDefinition, packageInfo, out object sample))
            {
                return false;
            }

            try
            {
                if (TryGetBoolMember(sample, "isImported", out bool isImported) && isImported)
                {
                    message = "Sample already imported.";
                    return true;
                }

                if (!TryInvokeUnitySampleImport(sample, out message))
                {
                    PackageInstallerLog.Samples.Warning(message);
                    return false;
                }

                AssetDatabase.Refresh();
                message = "Imported sample " + extraDefinition.DisplayName + ".";
                PackageInstallerLog.Samples.Info(message);
                return true;
            }
            catch (Exception exception)
            {
                message = "Unity sample import failed: " + exception.GetBaseException().Message;
                PackageInstallerLog.Samples.Warning(message);
                return false;
            }
        }

        internal static bool TryInvokeUnitySampleImportForTests(object sample, out string message)
        {
            return TryInvokeUnitySampleImport(sample, out message);
        }

        private static bool TryInvokeUnitySampleImport(object sample, out string message)
        {
            message = string.Empty;
            if (sample == null)
            {
                message = "Unity sample import API is unavailable for this sample.";
                return false;
            }

            MethodInfo importMethod = sample
                .GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "Import")
                    {
                        return false;
                    }

                    ParameterInfo[] candidateParameters = method.GetParameters();
                    return candidateParameters.Length == 0 ||
                           (candidateParameters.Length == 1 &&
                            candidateParameters[0].ParameterType.IsEnum);
                });
            if (importMethod == null)
            {
                message = "Unity sample import API has no supported import signature.";
                return false;
            }

            ParameterInfo[] parameters = importMethod.GetParameters();
            object invocationResult = parameters.Length == 0
                ? importMethod.Invoke(sample, null)
                : importMethod.Invoke(
                    sample,
                    new[] { Enum.ToObject(parameters[0].ParameterType, 0) });
            if (importMethod.ReturnType == typeof(bool) &&
                (!(invocationResult is bool imported) || !imported))
            {
                message = "Unity sample import API returned false; trying the staged fallback import.";
                return false;
            }

            return true;
        }

        private bool TryPrepareCopyImport(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out string sourcePath,
            out string destinationPath,
            out string stagingRootPath,
            out string message)
        {
            sourcePath = string.Empty;
            destinationPath = string.Empty;
            stagingRootPath = string.Empty;
            message = string.Empty;

            if (packageInfo == null)
            {
                message = "Installed package information is unavailable.";
                return false;
            }

            if (!TryGetSourcePath(packageInfo, extraDefinition, out sourcePath, out message))
            {
                return false;
            }

            string requestedDestinationAssetPath = GetDestinationPath(
                packageDefinition,
                extraDefinition,
                packageInfo);

            if (!TryCanonicalizeDestinationAssetPath(
                    requestedDestinationAssetPath,
                    out string destinationAssetPath))
            {
                message = "Sample destination must be inside the project's Assets folder.";
                return false;
            }

            destinationPath = GetAbsoluteProjectPath(destinationAssetPath);
            string assetsRootPath = Path.Combine(GetProjectRootPath(), "Assets");

            if (!IsPathInsideDirectory(destinationPath, assetsRootPath))
            {
                message = "Sample destination resolves outside the project's Assets folder.";
                return false;
            }

            if (!Directory.Exists(sourcePath))
            {
                message = "Sample folder was not found: " + sourcePath;
                return false;
            }

            if (DestinationExists(destinationAssetPath))
            {
                message = "Sample already imported.";
                return false;
            }

            stagingRootPath = Path.Combine(
                GetProjectRootPath(),
                "Library",
                "Deucarian",
                "PackageInstaller",
                "SampleImports");
            return true;
        }

        private void UpdateCopyImport()
        {
            if (_copyTask == null || !_copyTask.IsCompleted)
            {
                return;
            }

            EditorApplication.update -= UpdateCopyImport;
            PackageSampleStageResult result;

            try
            {
                result = _copyTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                result = new PackageSampleStageResult(
                    PackageSampleStageResultState.Failed,
                    "Sample import failed: " + exception.GetBaseException().Message);
            }

            PackageSampleImportState state;
            switch (result.State)
            {
                case PackageSampleStageResultState.Imported:
                    state = PackageSampleImportState.Imported;
                    AssetDatabase.Refresh();
                    result = new PackageSampleStageResult(
                        result.State,
                        "Imported sample " + _activeSampleDisplayName + ".");
                    PackageInstallerLog.Samples.Info(result.Message);
                    break;
                case PackageSampleStageResultState.AlreadyExists:
                    state = PackageSampleImportState.AlreadyImported;
                    break;
                case PackageSampleStageResultState.Canceled:
                    state = PackageSampleImportState.Canceled;
                    PackageInstallerLog.Samples.Info(result.Message);
                    break;
                default:
                    state = PackageSampleImportState.Failed;
                    PackageInstallerLog.Samples.Warning(result.Message);
                    break;
            }

            CompleteImport(_activeStatusKey, state, result.Message);
        }

        private void CompleteImport(string key, PackageSampleImportState state, string message)
        {
            SetStatus(key, state, message);
            PackageInstallerActivitySeverity severity = state == PackageSampleImportState.Failed
                ? PackageInstallerActivitySeverity.Error
                : state == PackageSampleImportState.Canceled
                    ? PackageInstallerActivitySeverity.Warning
                    : PackageInstallerActivitySeverity.Success;
            PackageInstallerActivityService.Record(
                "Samples",
                severity,
                message,
                packageId: _lastPackageDefinition != null
                    ? _lastPackageDefinition.PackageId
                    : string.Empty,
                retryKind: state == PackageSampleImportState.Failed || state == PackageSampleImportState.Canceled
                    ? PackageInstallerRetryKind.ImportSample
                    : PackageInstallerRetryKind.None);
            IsBusy = false;
            CurrentOperationName = string.Empty;
            CurrentExtraName = string.Empty;
            _activeStatusKey = string.Empty;
            _activeSampleDisplayName = string.Empty;
            _copyTask = null;
            _copyCancellation?.Dispose();
            _copyCancellation = null;
            NotifyStateChanged();
        }

        private bool TryFindUnitySample(
            PackageDefinition packageDefinition,
            PackageExtraDefinition extraDefinition,
            PackageManagerPackageInfo packageInfo,
            out object sample)
        {
            sample = null;

            if (packageDefinition == null || extraDefinition == null || packageInfo == null)
            {
                return false;
            }

            Type sampleType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.PackageManager.UI.Sample"))
                .FirstOrDefault(type => type != null);

            if (sampleType == null)
            {
                return false;
            }

            MethodInfo findByPackageMethod = sampleType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "FindByPackage" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(string) &&
                    method.GetParameters()[1].ParameterType == typeof(string));

            if (findByPackageMethod == null)
            {
                return false;
            }

            string packageName = !string.IsNullOrWhiteSpace(packageInfo.name)
                ? packageInfo.name
                : packageDefinition.PackageId;

            object samples = findByPackageMethod.Invoke(null, new object[] { packageName, packageInfo.version });

            if (!(samples is IEnumerable enumerableSamples))
            {
                return false;
            }

            foreach (object candidate in enumerableSamples)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (SampleMatches(candidate, extraDefinition))
                {
                    sample = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool SampleMatches(object sample, PackageExtraDefinition extraDefinition)
        {
            string displayName = GetStringMember(sample, "displayName");

            if (string.Equals(displayName, extraDefinition.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(displayName, extraDefinition.SampleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedSamplePath = NormalizeAssetPath(extraDefinition.SamplePath);
            string resolvedPath = NormalizeAssetPath(GetStringMember(sample, "resolvedPath"));
            string importPath = NormalizeAssetPath(GetStringMember(sample, "importPath"));

            return !string.IsNullOrWhiteSpace(normalizedSamplePath) &&
                   (resolvedPath.EndsWith(normalizedSamplePath, StringComparison.OrdinalIgnoreCase) ||
                    importPath.EndsWith(normalizedSamplePath, StringComparison.OrdinalIgnoreCase));
        }

        private void SetStatus(string key, PackageSampleImportState state, string message)
        {
            PackageSampleImportStatus status = new PackageSampleImportStatus(state, message);
            _statuses[key] = status;
            LastStatusMessage = message ?? string.Empty;
            LastErrorMessage = state == PackageSampleImportState.Failed ? LastStatusMessage : string.Empty;
            NotifyStateChanged();
        }

        private static bool TryGetBoolMember(object target, string memberName, out bool value)
        {
            value = false;

            if (target == null)
            {
                return false;
            }

            PropertyInfo property = target.GetType().GetProperty(memberName);

            if (property == null || property.PropertyType != typeof(bool))
            {
                FieldInfo field = target.GetType().GetField(memberName);

                if (field == null || field.FieldType != typeof(bool))
                {
                    return false;
                }

                value = (bool)field.GetValue(target);
                return true;
            }

            value = (bool)property.GetValue(target, null);
            return true;
        }

        private static string GetStringMember(object target, string memberName)
        {
            if (target == null)
            {
                return string.Empty;
            }

            PropertyInfo property = target.GetType().GetProperty(memberName);

            if (property == null || property.PropertyType != typeof(string))
            {
                FieldInfo field = target.GetType().GetField(memberName);

                if (field == null || field.FieldType != typeof(string))
                {
                    return string.Empty;
                }

                return field.GetValue(target) as string ?? string.Empty;
            }

            return property.GetValue(target, null) as string ?? string.Empty;
        }

        private static bool TryGetSourcePath(
            PackageManagerPackageInfo packageInfo,
            PackageExtraDefinition extraDefinition,
            out string sourcePath,
            out string message)
        {
            sourcePath = string.Empty;
            message = string.Empty;

            if (packageInfo == null || extraDefinition == null)
            {
                message = "Installed package information is unavailable.";
                return false;
            }

            return TryResolveSampleSourcePath(
                packageInfo.resolvedPath,
                extraDefinition.SamplePath,
                out sourcePath,
                out message);
        }

        internal static bool TryResolveSampleSourcePathForTests(
            string resolvedPackagePath,
            string samplePath,
            out string sourcePath,
            out string message)
        {
            return TryResolveSampleSourcePath(
                resolvedPackagePath,
                samplePath,
                out sourcePath,
                out message);
        }

        internal static bool TryCanonicalizeDestinationAssetPathForTests(
            string destinationAssetPath,
            out string canonicalAssetPath)
        {
            return TryCanonicalizeDestinationAssetPath(
                destinationAssetPath,
                out canonicalAssetPath);
        }

        private static bool TryResolveSampleSourcePath(
            string resolvedPackagePath,
            string samplePath,
            out string sourcePath,
            out string message)
        {
            sourcePath = string.Empty;
            message = string.Empty;

            string normalizedSamplePath = NormalizeAssetPath(samplePath);

            if (string.IsNullOrWhiteSpace(normalizedSamplePath))
            {
                message = "Sample path is missing from package.json.";
                return false;
            }

            if (!IsSamplesFolderPath(normalizedSamplePath))
            {
                message = "Sample path must be inside the package's Samples~ folder.";
                return false;
            }

            string packagePath = GetPackageRootPath(resolvedPackagePath);

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                message = "Installed package path is unavailable.";
                return false;
            }

            const string samplesRootName = "Samples~";
            string relativeSamplePath = normalizedSamplePath.Length > samplesRootName.Length
                ? normalizedSamplePath.Substring(samplesRootName.Length).TrimStart('/')
                : string.Empty;
            if (!TryNormalizeRelativePathWithinRoot(
                    relativeSamplePath,
                    out string normalizedRelativeSamplePath))
            {
                message = "Sample path resolves outside the installed package's Samples~ folder.";
                return false;
            }

            string samplesRootPath = Path.GetFullPath(Path.Combine(packagePath, samplesRootName));
            sourcePath = Path.GetFullPath(Path.Combine(
                samplesRootPath,
                normalizedRelativeSamplePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!IsPathInsideDirectory(sourcePath, samplesRootPath))
            {
                message = "Sample path resolves outside the installed package's Samples~ folder.";
                sourcePath = string.Empty;
                return false;
            }

            return true;
        }

        private static bool DestinationExists(string destinationAssetPath)
        {
            if (!TryCanonicalizeDestinationAssetPath(
                    destinationAssetPath,
                    out string normalizedPath))
            {
                return false;
            }

            string absolutePath = GetAbsoluteProjectPath(normalizedPath);
            string assetsRootPath = Path.Combine(GetProjectRootPath(), "Assets");

            if (!IsPathInsideDirectory(absolutePath, assetsRootPath))
            {
                return false;
            }

            return AssetDatabase.IsValidFolder(normalizedPath) ||
                   Directory.Exists(absolutePath) ||
                   File.Exists(absolutePath);
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(GetProjectRootPath(), NormalizeAssetPath(assetPath)));
        }

        private static string SanitizeAssetPathSegment(string segment)
        {
            return SanitizeAssetPathSegment(segment, "Sample");
        }

        private static string SanitizeAssetPathSegment(string segment, string fallback)
        {
            string sanitized = segment ?? string.Empty;

            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter.ToString(), string.Empty);
            }

            return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized.Trim();
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        }

        private static string GetPackageDisplayName(
            PackageDefinition packageDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.displayName))
            {
                return packageInfo.displayName;
            }

            return packageDefinition != null ? packageDefinition.DisplayName : "Package";
        }

        private static string GetPackageVersion(
            PackageDefinition packageDefinition,
            PackageManagerPackageInfo packageInfo)
        {
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version))
            {
                return packageInfo.version;
            }

            return packageDefinition != null ? packageDefinition.DisplayVersion : string.Empty;
        }

        private static PackageManagerPackageInfo ResolveCurrentPackageInfo(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return null;
            }

            return (PackageManagerPackageInfo.GetAllRegisteredPackages() ??
                    Array.Empty<PackageManagerPackageInfo>())
                .FirstOrDefault(packageInfo =>
                    packageInfo != null &&
                    string.Equals(
                        packageInfo.name,
                        packageId,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string GetPackageRootPath(string resolvedPath)
        {
            string packagePath = resolvedPath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return string.Empty;
            }

            if (!Path.IsPathRooted(packagePath))
            {
                packagePath = Path.Combine(GetProjectRootPath(), packagePath);
            }

            return Path.GetFullPath(packagePath);
        }

        private static string GetProjectRootPath()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            return projectRoot != null ? projectRoot.FullName : Application.dataPath;
        }

        private static bool TryCanonicalizeDestinationAssetPath(
            string assetPath,
            out string canonicalAssetPath)
        {
            canonicalAssetPath = string.Empty;
            string normalizedPath = NormalizeAssetPath(assetPath);

            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            const string assetsRootName = "Assets";
            if (!string.Equals(normalizedPath, assetsRootName, StringComparison.OrdinalIgnoreCase) &&
                !normalizedPath.StartsWith(assetsRootName + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relativeAssetPath = normalizedPath.Length > assetsRootName.Length
                ? normalizedPath.Substring(assetsRootName.Length).TrimStart('/')
                : string.Empty;
            if (!TryNormalizeRelativePathWithinRoot(
                    relativeAssetPath,
                    out string normalizedRelativeAssetPath))
            {
                return false;
            }

            canonicalAssetPath = string.IsNullOrEmpty(normalizedRelativeAssetPath)
                ? assetsRootName
                : assetsRootName + "/" + normalizedRelativeAssetPath;
            return true;
        }

        private static bool IsSamplesFolderPath(string samplePath)
        {
            string normalizedPath = NormalizeAssetPath(samplePath);

            return string.Equals(normalizedPath, "Samples~", StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith("Samples~/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryNormalizeRelativePathWithinRoot(
            string relativePath,
            out string normalizedRelativePath)
        {
            normalizedRelativePath = string.Empty;
            List<string> segments = new List<string>();
            foreach (string segment in NormalizeAssetPath(relativePath).Split('/'))
            {
                if (string.IsNullOrEmpty(segment) || string.Equals(segment, ".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    if (segments.Count == 0)
                    {
                        return false;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            normalizedRelativePath = string.Join("/", segments.ToArray());
            return true;
        }

        private static bool IsPathInsideDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string fullPath = AppendDirectorySeparator(Path.GetFullPath(path));
            string fullDirectory = AppendDirectorySeparator(Path.GetFullPath(directory));

            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string GetStatusKey(PackageDefinition packageDefinition, PackageExtraDefinition extraDefinition)
        {
            string packageId = packageDefinition != null ? packageDefinition.PackageId : string.Empty;
            string samplePath = extraDefinition != null ? extraDefinition.SamplePath : string.Empty;
            string sampleName = extraDefinition != null ? extraDefinition.SampleName : string.Empty;
            return packageId + "|" + samplePath + "|" + sampleName;
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
