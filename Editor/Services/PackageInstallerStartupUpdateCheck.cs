using System;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallerStartupCheckDecision
    {
        DoNotSchedule,
        WaitForEditor,
        Start
    }

    [InitializeOnLoad]
    internal static class PackageInstallerStartupUpdateCheck
    {
        private const string AttemptedThisSessionKey =
            "Deucarian.PackageInstaller.StartupUpdateCheckAttempted";

        private static PackageInstallerStateRepository _stateRepository;
        private static PackageDetectionService _packageDetectionService;
        private static PackageUpdateCheckService _packageUpdateCheckService;
        private static bool _checkStarted;

        static PackageInstallerStartupUpdateCheck()
        {
            ScheduleIfNeeded();
        }

        internal static bool ShouldScheduleForTests(
            bool enabled,
            bool alreadyAttempted,
            bool isBatchMode)
        {
            return GetDecisionForTests(
                       enabled,
                       alreadyAttempted,
                       isBatchMode,
                       isCompiling: false,
                       isUpdating: false) == PackageInstallerStartupCheckDecision.Start;
        }

        internal static PackageInstallerStartupCheckDecision GetDecisionForTests(
            bool enabled,
            bool alreadyAttempted,
            bool isBatchMode,
            bool isCompiling,
            bool isUpdating)
        {
            if (!enabled || alreadyAttempted || isBatchMode)
            {
                return PackageInstallerStartupCheckDecision.DoNotSchedule;
            }

            return isCompiling || isUpdating
                ? PackageInstallerStartupCheckDecision.WaitForEditor
                : PackageInstallerStartupCheckDecision.Start;
        }

        private static void ScheduleIfNeeded()
        {
            bool alreadyAttempted = SessionState.GetBool(AttemptedThisSessionKey, false);
            if (!ShouldScheduleForTests(
                    PackageUpdateCheckPreferences.CheckOnEditorStart,
                    alreadyAttempted,
                    Application.isBatchMode))
            {
                return;
            }

            EditorApplication.delayCall -= TryStart;
            EditorApplication.delayCall += TryStart;
        }

        private static void TryStart()
        {
            EditorApplication.delayCall -= TryStart;

            PackageInstallerStartupCheckDecision decision = GetDecisionForTests(
                PackageUpdateCheckPreferences.CheckOnEditorStart,
                SessionState.GetBool(AttemptedThisSessionKey, false),
                Application.isBatchMode,
                EditorApplication.isCompiling,
                EditorApplication.isUpdating);

            if (decision == PackageInstallerStartupCheckDecision.WaitForEditor)
            {
                EditorApplication.delayCall += TryStart;
                return;
            }

            if (decision != PackageInstallerStartupCheckDecision.Start)
            {
                return;
            }

            SessionState.SetBool(AttemptedThisSessionKey, true);

            try
            {
                _stateRepository = new PackageInstallerStateRepository();
                _packageDetectionService = new PackageDetectionService();
                _packageUpdateCheckService = new PackageUpdateCheckService(_packageDetectionService);
                _checkStarted = false;

                PackageRegistryProvider.RefreshRemote();
                _packageDetectionService.Refresh();
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                PackageInstallerLog.UpdateChecks.DiagnosticInfo(
                    "Scheduled the once-per-session Package Installer update check.");
            }
            catch (Exception exception)
            {
                PackageInstallerLog.UpdateChecks.Warning(
                    "Could not start the session update check: " + exception.GetBaseException().Message);
                Cleanup();
            }
        }

        private static void Update()
        {
            if (_packageDetectionService == null || _packageUpdateCheckService == null)
            {
                Cleanup();
                return;
            }

            if (!_checkStarted)
            {
                if (_packageDetectionService.IsRefreshing ||
                    PackageRegistryProvider.IsRemoteRefreshing ||
                    PackageUpdateCheckService.IsAnyCheckRunning)
                {
                    return;
                }

                _checkStarted = true;
                _packageUpdateCheckService.PrepareForUpdateCheck();
                _packageUpdateCheckService.CheckForUpdates(
                    PackageRegistryProvider.All,
                    ResolveSelectedChannel);

                if (!_packageUpdateCheckService.IsChecking)
                {
                    LogCompletionAndCleanup();
                }

                return;
            }

            if (!_packageUpdateCheckService.IsChecking)
            {
                LogCompletionAndCleanup();
            }
        }

        private static void LogCompletionAndCleanup()
        {
            if (_packageUpdateCheckService != null &&
                !string.IsNullOrWhiteSpace(_packageUpdateCheckService.LastStatusMessage))
            {
                PackageInstallerLog.UpdateChecks.DiagnosticInfo(
                    "Startup update check: " + _packageUpdateCheckService.LastStatusMessage);
            }

            Cleanup();
        }

        private static PackageChannel ResolveSelectedChannel(PackageDefinition packageDefinition)
        {
            PackageChannelSelection projectSelection = _stateRepository.GetProjectChannelSelection();
            PackageChannelSelection packageSelection =
                _stateRepository.GetPackageChannelSelection(packageDefinition.PackageId);
            bool hasInstalledChannel = _packageDetectionService.TryGetInstalledPackageChannel(
                packageDefinition,
                out PackageChannel installedChannel,
                out _);

            return PackageInstallerWindow.ResolveSelectedChannel(
                packageDefinition,
                projectSelection,
                packageSelection,
                hasInstalledChannel,
                installedChannel);
        }

        private static void Cleanup()
        {
            EditorApplication.delayCall -= TryStart;
            EditorApplication.update -= Update;

            if (_packageUpdateCheckService != null)
            {
                _packageUpdateCheckService.Dispose();
                _packageUpdateCheckService = null;
            }

            if (_packageDetectionService != null)
            {
                _packageDetectionService.Dispose();
                _packageDetectionService = null;
            }

            _stateRepository = null;
            _checkStarted = false;
        }
    }
}
