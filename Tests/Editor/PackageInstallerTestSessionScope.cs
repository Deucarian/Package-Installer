using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    // The controlled-client fixture can run in an idle live Editor. Preserve only its
    // known global collaborators in memory, without adding production mutation APIs.
    internal sealed class PackageInstallerTestSessionScope : IDisposable
    {
        private readonly object activityGate;
        private readonly List<PackageInstallerActivityEntry> activityList;
        private readonly PackageInstallerActivityEntry[] activity;
        private readonly FieldInfo activitySequenceField;
        private readonly long activitySequence;
        private readonly FieldInfo activityChangedField;
        private readonly Action activityChanged;
        private readonly List<SavedField> fields = new List<SavedField>();
        private readonly List<SavedSession> sessions = new List<SavedSession>();
        private bool disposed;

        internal PackageInstallerTestSessionScope()
        {
            // Resolve and capture every exact field before changing any shared state.
            var activityType = typeof(PackageInstallerActivityService);
            activityGate = RequiredField(activityType, "Gate", typeof(object)).GetValue(null);
            activityList = (List<PackageInstallerActivityEntry>)RequiredField(activityType,
                "Entries", typeof(List<PackageInstallerActivityEntry>)).GetValue(null);
            activitySequenceField = RequiredField(activityType, "_sequence", typeof(long));
            activityChangedField = RequiredField(activityType, "Changed", typeof(Action));
            lock (activityGate)
            {
                activity = activityList.ToArray();
                activitySequence = (long)activitySequenceField.GetValue(null);
                activityChanged = (Action)activityChangedField.GetValue(null);
            }
            CaptureField(typeof(PackageOperationAutoResumeState), "_activeOperationId", typeof(string));
            CaptureField(typeof(PackageOperationAutoResumeState), "_activeRegistryFingerprint", typeof(string));
            CaptureField(typeof(PackageOperationAutoResumeState), "_isAssemblyReloading", typeof(bool));
            CaptureField(typeof(PackageInstallerEditorStatus), "hasInstalledPackageSnapshot", typeof(bool));
            CaptureField(typeof(PackageInstallerEditorStatus), "installedPackageCount", typeof(int));
            CaptureField(typeof(PackageInstallerEditorStatus), "installedRefreshInProgress", typeof(bool));
            CaptureField(typeof(PackageInstallerEditorStatus), "operationInProgress", typeof(bool));
            CaptureField(typeof(PackageInstallerEditorStatus), "completedOperationSteps", typeof(int));
            CaptureField(typeof(PackageInstallerEditorStatus), "totalOperationSteps", typeof(int));
            CaptureField(typeof(PackageInstallerEditorStatus), "failedOperationSteps", typeof(int));
            CaptureSession(typeof(PackageInstallerSelfUpdateState), "StateKey");
            CaptureSession(typeof(PackageOperationAutoResumeState), "ReloadMarkerKey");
            CaptureSession(typeof(PackageInstallService), "PendingOperationNameKey");
            CaptureSession(typeof(PackageInstallService), "PendingQueueKey");

            // Test operation events must not paint synthetic activity into the user's window.
            activityChangedField.SetValue(null, null);
            foreach (var saved in sessions) SessionState.EraseString(saved.Key);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                foreach (var saved in fields) saved.Field.SetValue(null, saved.Value);
                foreach (var saved in sessions)
                {
                    if (saved.Exists) SessionState.SetString(saved.Key, saved.Value);
                    else SessionState.EraseString(saved.Key);
                }
            }
            finally
            {
                lock (activityGate)
                {
                    activityList.Clear();
                    activityList.AddRange(activity);
                    activitySequenceField.SetValue(null, activitySequence);
                    activityChangedField.SetValue(null, activityChanged);
                }
            }
        }

        private void CaptureField(Type type, string name, Type expectedType)
        {
            var field = RequiredField(type, name, expectedType);
            fields.Add(new SavedField { Field = field, Value = field.GetValue(null) });
        }

        private void CaptureSession(Type type, string constantName)
        {
            var field = RequiredField(type, constantName, typeof(string));
            if (!field.IsLiteral) throw new InvalidOperationException("Expected a session-key constant: " + constantName);
            string key = (string)field.GetRawConstantValue();
            string missing = Guid.NewGuid().ToString("N");
            string value = SessionState.GetString(key, missing);
            sessions.Add(new SavedSession { Key = key, Value = value, Exists = value != missing });
        }

        private static FieldInfo RequiredField(Type owner, string name, Type expectedType)
        {
            var field = owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new InvalidOperationException("Update the exact test-state contract for " + owner.Name + "." + name);
            return field;
        }

        private sealed class SavedField { internal FieldInfo Field; internal object Value; }
        private sealed class SavedSession { internal string Key; internal string Value; internal bool Exists; }
    }
}
