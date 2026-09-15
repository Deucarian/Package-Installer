using Deucarian.Editor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow : IDeucarianEditorReloadState
    {
        // Per-controller state prevents two independent workspaces consuming each other's snapshot.
        [SerializeField] private string _windowReloadState;
        private string _workspaceSearchText = string.Empty;
        private int _workspaceCategory;
        private PackageOperationQueueView _queueView;
        private PackageOperationQueueSnapshot _restoredQueue;
        private PackageOperationQueueSnapshot _recoveryQueue;

        private PackageOperationQueueSnapshot CaptureQueue()
        {
            var live = PackageOperationQueueSnapshot.Capture(_packageInstallService);
            if (live != null) return live;
            if (_recoveryQueue != null && !_packageInstallService.HasSavedOperation)
            {
                _recoveryQueue = null;
                _restoredQueue = null;
            }
            return _recoveryQueue ?? _restoredQueue;
        }

        private void CreateQueueView()
        {
            _recoveryQueue = _packageInstallService.TryGetSavedOperation(out var recovery, out _)
                ? PackageOperationQueueSnapshot.Capture(recovery) : null;
            _queueView = new PackageOperationQueueView();
            _windowContentRoot.Insert(0, _queueView.Root);
            _queueView.Refresh(CaptureQueue());
        }
    }
}
