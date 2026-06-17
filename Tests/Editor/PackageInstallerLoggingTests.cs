using System;
using System.Collections.Generic;
using Deucarian.Logging;
using NUnit.Framework;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerLoggingTests
    {
        private CapturingSink _sink;
        private bool _hadVerboseConsoleLoggingKey;
        private bool _originalVerboseConsoleLogging;

        [SetUp]
        public void SetUp()
        {
            _hadVerboseConsoleLoggingKey = EditorPrefs.HasKey(PackageInstallerLoggingPreferences.VerboseConsoleLoggingKey);
            _originalVerboseConsoleLogging = PackageInstallerLoggingPreferences.VerboseConsoleLogging;
            PackageInstallerLoggingPreferences.VerboseConsoleLogging = false;
            DeucarianLogSettings.ResetToDefaults();
            DeucarianLog.ClearSinks();
            _sink = new CapturingSink();
            DeucarianLog.RegisterSink(_sink);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadVerboseConsoleLoggingKey)
            {
                PackageInstallerLoggingPreferences.VerboseConsoleLogging = _originalVerboseConsoleLogging;
            }
            else
            {
                EditorPrefs.DeleteKey(PackageInstallerLoggingPreferences.VerboseConsoleLoggingKey);
            }

            DeucarianLogSettings.ResetToDefaults();
            DeucarianLog.ResetSinksToDefault();
        }

        [Test]
        public void DefaultVerboseConsoleLoggingSettingIsOff()
        {
            EditorPrefs.DeleteKey(PackageInstallerLoggingPreferences.VerboseConsoleLoggingKey);

            Assert.IsFalse(PackageInstallerLoggingPreferences.VerboseConsoleLogging);
        }

        [Test]
        public void InfoMessagesDoNotReachConsoleSinkByDefault()
        {
            PackageInstallerLog.Install.Info("Queued Deucarian Logging.");

            Assert.AreEqual(0, _sink.Entries.Count);
        }

        [Test]
        public void VerboseConsoleLoggingAllowsInfoMessages()
        {
            PackageInstallerLoggingPreferences.VerboseConsoleLogging = true;

            PackageInstallerLog.Install.Info("Queued Deucarian Logging.");

            Assert.AreEqual(1, _sink.Entries.Count);
            Assert.AreEqual(DeucarianLogLevel.Info, _sink.Entries[0].Level);
            Assert.AreEqual("Queued Deucarian Logging.", _sink.Entries[0].Message);
        }

        [Test]
        public void WarningsAndErrorsReachConsoleSinkByDefault()
        {
            PackageInstallerLog.UpdateChecks.Warning("Update check could not read optional metadata.");
            PackageInstallerLog.UpdateChecks.Error("Update check failed.");

            Assert.AreEqual(2, _sink.Entries.Count);
            Assert.AreEqual(DeucarianLogLevel.Warning, _sink.Entries[0].Level);
            Assert.AreEqual(DeucarianLogLevel.Error, _sink.Entries[1].Level);
        }

        [Test]
        public void UpdateAvailableStatusDoesNotReachConsoleSinkByDefault()
        {
            PackageUpdateCheckService.LogStatusForTests(CreateUpdateAvailableStatus());

            Assert.AreEqual(0, _sink.Entries.Count);
        }

        [Test]
        public void FailedUpdateCheckStatusStillLogsError()
        {
            PackageDefinition packageDefinition = CreatePackage();
            PackageUpdateStatus status = PackageUpdateStatus.Failed(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                string.Empty,
                "Update check failed: remote revision could not be resolved.");

            PackageUpdateCheckService.LogStatusForTests(status);

            Assert.AreEqual(1, _sink.Entries.Count);
            Assert.AreEqual(DeucarianLogLevel.Error, _sink.Entries[0].Level);
            StringAssert.Contains("Update check failed for Deucarian Object Loading", _sink.Entries[0].Message);
        }

        [Test]
        public void VerboseConsoleLoggingAllowsUpdateAvailableStatusInfo()
        {
            PackageInstallerLoggingPreferences.VerboseConsoleLogging = true;

            PackageUpdateCheckService.LogStatusForTests(CreateUpdateAvailableStatus());

            Assert.AreEqual(1, _sink.Entries.Count);
            Assert.AreEqual(DeucarianLogLevel.Info, _sink.Entries[0].Level);
            StringAssert.Contains("Update available for Deucarian Object Loading", _sink.Entries[0].Message);
        }

        [Test]
        public void InstallSummaryMessagesAreRetainedWithoutConsoleInfo()
        {
            using (PackageInstallService installService = new PackageInstallService())
            {
                installService.RecordCompletedOperation(
                    "Install Deucarian Logging",
                    "Install Deucarian Logging completed successfully.",
                    new[]
                    {
                        "Skipped dependency Deucarian Editor; already installed.",
                        "Installed Deucarian Logging."
                    });

                Assert.AreEqual("Install Deucarian Logging completed successfully.", installService.LastStatusMessage);
                CollectionAssert.Contains(
                    installService.OperationMessages,
                    "Skipped dependency Deucarian Editor; already installed.");
                CollectionAssert.Contains(
                    installService.OperationMessages,
                    "Installed Deucarian Logging.");
                Assert.AreEqual(0, _sink.Entries.Count);
            }
        }

        private static PackageUpdateStatus CreateUpdateAvailableStatus()
        {
            PackageDefinition packageDefinition = CreatePackage();
            return PackageUpdateStatus.UpdateAvailable(
                packageDefinition,
                PackageChannel.Stable,
                packageDefinition.StableUrl,
                "0123456789abcdef0123456789abcdef01234567",
                "fedcba9876543210fedcba9876543210fedcba98");
        }

        private static PackageDefinition CreatePackage()
        {
            return new PackageDefinition(
                "Deucarian Object Loading",
                "com.deucarian.object-loading",
                "https://github.com/Deucarian/Object-Loading.git#main",
                "Reusable runtime loading pipeline.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/Object-Loading.git#develop",
                category: "Core");
        }

        private sealed class CapturingSink : IDeucarianLogSink
        {
            private readonly List<DeucarianLogEntry> _entries = new List<DeucarianLogEntry>();

            public IReadOnlyList<DeucarianLogEntry> Entries => _entries;

            public void Log(in DeucarianLogEntry entry)
            {
                _entries.Add(entry);
            }
        }
    }
}
