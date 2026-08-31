using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {


        private void InstallGraphGroupPackages(
            PackageGraphGroup group,
            IReadOnlyCollection<PackageDefinition> packageDefinitions)
        {
            if (_packageDependencyInstaller == null || packageDefinitions == null || packageDefinitions.Count == 0)
            {
                return;
            }

            _activeActionKind = PackageInstallerActionKind.InstallAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.InstallManyWithDependencies(
                packageDefinitions,
                GetSelectedChannel,
                "Install missing in " + group.DisplayName);
            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void UpdateGraphGroupPackages(
            PackageGraphGroup group,
            IReadOnlyCollection<PackageDefinition> packageDefinitions)
        {
            if (_packageDependencyInstaller == null || packageDefinitions == null || packageDefinitions.Count == 0)
            {
                return;
            }

            TrackPendingUpdateStatusInvalidations(packageDefinitions);
            _activeActionKind = PackageInstallerActionKind.UpdateAll;
            _cancelingActionKind = PackageInstallerActionKind.None;
            _packageDependencyInstaller.UpdateManyWithDependencies(
                packageDefinitions,
                GetSelectedChannel,
                "Update available in " + group.DisplayName);

            if (!ShouldRetainPendingUpdateStatusInvalidations(
                    _packageInstallService != null && _packageInstallService.IsBusy,
                    _packageDependencyInstaller != null &&
                    _packageDependencyInstaller.IsAwaitingPreflight))
            {
                _pendingUpdateStatusInvalidationPackageIds.Clear();
            }

            ClearActiveActionIfIdle();
            UpdateViewVisibility();
        }

        private void ConfirmContextualOperation(
            PackageDependencyInstallPlan plan,
            string operationName,
            Action<bool> completed)
        {
            if (plan == null || !plan.IsValid)
            {
                ShowInformationDialog(
                    "Package operation unavailable",
                    plan != null && !string.IsNullOrWhiteSpace(plan.ErrorMessage)
                        ? plan.ErrorMessage
                        : "The package operation could not be planned.",
                    DeucarianEditorIconIds.Error,
                    () => completed?.Invoke(false));
                return;
            }

            if (!plan.RequiresPreflight)
            {
                completed?.Invoke(true);
                return;
            }

            List<string> riskLabels = new List<string>();
            if (plan.IsMultiStep) riskLabels.Add("multiple package steps");
            if (plan.IsBulk) riskLabels.Add("multiple requested packages");
            if (plan.HasMigrationRisk) riskLabels.Add("source/channel migration");
            if (plan.HasDowngradeRisk) riskLabels.Add("possible downgrade");
            if (plan.HasChannelFallback) riskLabels.Add("channel fallback");
            if (plan.HasConflict) riskLabels.Add("channel conflict");
            if (plan.HasDestructiveRisk) riskLabels.Add("destructive reinstall/remove behavior");

            List<string> lines = new List<string>
            {
                "Review " + plan.Steps.Count + " planned package step(s).",
                riskLabels.Count == 0 ? string.Empty : "Attention: " + string.Join(", ", riskLabels.ToArray()),
                string.Empty
            };
            foreach (PackageDependencyInstallStep step in plan.Steps)
            {
                string channelLabel = step.RequestedChannel == step.Channel
                    ? GetChannelLabel(step.Channel)
                    : GetChannelLabel(step.RequestedChannel) + " requested -> " +
                      GetChannelLabel(step.Channel) + " target";
                lines.Add(
                    "- " + step.PackageDefinition.DisplayName +
                    " [" + channelLabel + "]" +
                    (step.IsDependency ? " - dependency" : string.Empty));
                lines.Add("  " + step.TargetUrl);
            }

            if (plan.Messages.Count > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(plan.Messages.Where(message => !string.IsNullOrWhiteSpace(message)));
            }

            var continueAction = new DeucarianEditorDialogAction(
                "continue",
                "Continue",
                DeucarianEditorIconIds.Play,
                DeucarianEditorDialogActionStyle.Primary);
            var cancelAction = new DeucarianEditorDialogAction(
                "cancel",
                "Cancel",
                DeucarianEditorIconIds.Stop);
            var options = new DeucarianEditorDialogOptions(
                operationName,
                lines[0] + (string.IsNullOrWhiteSpace(lines[1]) ? string.Empty : "\n" + lines[1]),
                DeucarianEditorIconIds.Warning,
                new[] { continueAction, cancelAction })
            {
                Details = string.Join("\n", lines.Skip(3).Where(line => line != null).ToArray()).Trim(),
                DefaultActionId = continueAction.Id,
                CancelActionId = cancelAction.Id
            };
            if (!TryShowManagedDialog(
                    options,
                    result => completed?.Invoke(
                        !result.WasCanceled &&
                        string.Equals(result.ActionId, continueAction.Id, StringComparison.Ordinal))))
            {
                completed?.Invoke(false);
            }
        }

        private void ShowInformationDialog(
            string title,
            string message,
            string iconId,
            Action completed = null)
        {
            var okAction = new DeucarianEditorDialogAction(
                "ok",
                "OK",
                DeucarianEditorIconIds.Check,
                DeucarianEditorDialogActionStyle.Primary);
            var options = new DeucarianEditorDialogOptions(
                title,
                message,
                iconId,
                new[] { okAction })
            {
                DefaultActionId = okAction.Id,
                CancelActionId = okAction.Id
            };
            if (!TryShowManagedDialog(options, _ => completed?.Invoke()))
            {
                completed?.Invoke();
            }
        }

        private bool TryShowManagedDialog(
            DeucarianEditorDialogOptions options,
            Action<DeucarianEditorDialogResult> completed)
        {
            if (options == null || this == null)
            {
                return false;
            }

            if (_confirmationState == null)
            {
                _confirmationState = new PackageInstallerConfirmationState();
            }

            if (!_confirmationState.TryBegin(out long generation))
            {
                return false;
            }

            try
            {
                EditorWindow dialogWindow = DeucarianEditorDialog.Show(options, result =>
                {
                    if (_confirmationState == null ||
                        !_confirmationState.TryComplete(generation))
                    {
                        return;
                    }

                    _activeConfirmationWindow = null;
                    if (this == null)
                    {
                        return;
                    }

                    try
                    {
                        completed?.Invoke(result);
                    }
                    finally
                    {
                        UpdateViewVisibility();
                        ClearActiveActionIfIdle();
                        Repaint();
                    }
                });

                if (_confirmationState.IsCurrent(generation))
                {
                    _activeConfirmationWindow = dialogWindow;
                }
                else if (dialogWindow != null)
                {
                    dialogWindow.Close();
                }

                UpdateViewVisibility();
                Repaint();
                return true;
            }
            catch
            {
                _confirmationState.CancelPending();
                _activeConfirmationWindow = null;
                throw;
            }
        }

        private bool DismissPendingConfirmation(bool refreshUi = true)
        {
            if (_confirmationState == null || !_confirmationState.CancelPending())
            {
                return false;
            }

            EditorWindow dialogWindow = _activeConfirmationWindow;
            _activeConfirmationWindow = null;
            if (dialogWindow != null)
            {
                dialogWindow.Close();
            }

            if (refreshUi && this != null)
            {
                UpdateViewVisibility();
                Repaint();
            }

            return true;
        }

        private float GetDetailsContentWidth()
        {
            float graphDetailsContentWidth = _graphDetailsContainer == null
                ? 0f
                : _graphDetailsContainer.contentRect.width;

            return ResolveDetailsContentWidth(
                position.width,
                _viewMode == InstallerViewMode.EcosystemGraph,
                graphDetailsContentWidth);
        }

        private static float ResolveDetailsContentWidth(
            float windowWidth,
            bool isEcosystemGraph,
            float graphDetailsContentWidth)
        {
            if (isEcosystemGraph &&
                graphDetailsContentWidth > 0f &&
                !float.IsNaN(graphDetailsContentWidth) &&
                !float.IsInfinity(graphDetailsContentWidth))
            {
                return graphDetailsContentWidth;
            }

            return Mathf.Max(0f, windowWidth - SidebarWidth - 56f);
        }

        private static bool ShouldStackDetailsActions(float detailsContentWidth)
        {
            return detailsContentWidth < DetailsActionsStackWidth;
        }

        private void DrawDetailHeader(PackageDefinition packageDefinition)
        {
            VisualStatus status = GetPackageVisualStatus(packageDefinition);

            DrawPanel("Overview", () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect iconRect = GUILayoutUtility.GetRect(48f, 42f, GUILayout.Width(48f), GUILayout.Height(42f));
                    DrawPackageIcon(
                        iconRect,
                        packageDefinition,
                        packageDefinition.IsIntegration ? VisualStatusKind.Integration : status.Kind);

                    GUILayout.Space(8f);

                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        string displayName = GetDetailDisplayName(packageDefinition);
                        EditorGUILayout.LabelField(
                            new GUIContent(displayName, displayName),
                            _titleStyle,
                            GUILayout.ExpandWidth(true));

                        if (!string.IsNullOrWhiteSpace(packageDefinition.Description))
                        {
                            EditorGUILayout.LabelField(
                                new GUIContent(packageDefinition.Description, packageDefinition.Description),
                                _subtitleStyle);
                        }

                        if (packageDefinition.HasDisplayVersion)
                        {
                            DrawKeyValueRow("Version", packageDefinition.DisplayVersion);
                        }
                    }

                    GUILayout.Space(8f);
                    DrawStatusBadge(status.Label, status.Kind, GUILayout.Width(132f));
                }
            }, GUILayout.ExpandWidth(true));
        }

        private void DrawPackageIcon(Rect rect, PackageDefinition packageDefinition, VisualStatusKind statusKind)
        {
            if (Event.current.type == EventType.Repaint)
            {
                Color color = GetStatusColor(statusKind);
                DeucarianEditorVisualShell.DrawInsetSurface(
                    rect,
                    DeucarianEditorColors.WithAlpha(color, 0.12f),
                    DeucarianEditorColors.WithAlpha(color, 0.58f),
                    6f);
            }

            Texture2D icon = DeucarianEditorIcons.GetPackageIcon(GetPackageIconKey(packageDefinition));
            Rect iconRect = new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, rect.height - 6f);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        }

        private static string GetPackageIconKey(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return DeucarianEditorIconIds.Package;
            }

            if (!string.IsNullOrWhiteSpace(packageDefinition.IconKey))
            {
                return packageDefinition.IconKey.Trim();
            }

            if (string.IsNullOrWhiteSpace(packageDefinition.PackageId))
            {
                return DeucarianEditorIconIds.Package;
            }

            const string prefix = "com.deucarian.";
            string packageId = packageDefinition.PackageId.Trim();
            return packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? packageId.Substring(prefix.Length)
                : packageId;
        }

        private static string GetDetailDisplayName(PackageDefinition packageDefinition)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            return packageDefinition.DisplayName;
        }

        private void DrawStatusPanel(PackageDefinition packageDefinition)
        {
            DrawPanel(packageDefinition.IsIntegration ? "Integration Status" : "Status", () =>
            {
                DrawPackageStatusContent(packageDefinition);
            }, GUILayout.ExpandWidth(true));
        }
    }
}
