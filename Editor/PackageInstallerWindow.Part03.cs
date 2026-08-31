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


        private VisualElement CreateGlobalChannelOverridePopup()
        {
            VisualElement popup = new VisualElement { name = GlobalChannelOverridePopupName };
            popup.AddToClassList("dpi-global-channel-popup");
            popup.style.display = DisplayStyle.None;

            VisualElement title = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.GitBranch,
                "Global Channel Override",
                true);
            title.AddToClassList("dpi-global-channel-popup__title");
            popup.Add(title);

            Label message = new Label(
                "This will override all package states. An individual package dropdown can take over again when changed later.");
            message.AddToClassList("dpi-global-channel-popup__message");
            popup.Add(message);

            _globalChannelDropdown = new DropdownField
            {
                label = "Channel",
                choices = GlobalChannelOptionLabels.ToList()
            };
            _globalChannelDropdown.AddToClassList("dpi-global-channel-popup__dropdown");
            popup.Add(_globalChannelDropdown);

            VisualElement actions = new VisualElement();
            actions.AddToClassList("dpi-global-channel-popup__actions");

            Button applyButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Check,
                "Apply Override",
                ApplyGlobalChannelOverrideFromPopup,
                "Apply the selected project-wide package channel override.");
            applyButton.AddToClassList("dpi-global-channel-popup__apply");
            applyButton.AddToClassList("dpi-global-channel-popup__apply--primary");

            _globalChannelResetButton = DeucarianEditorIconTextButton.Create(
                DeucarianEditorIconIds.Reset,
                "Use Default",
                ClearGlobalChannelOverrideFromPopup,
                "Remove the explicit project override and use inherited/default channel selection.");
            _globalChannelResetButton.name = GlobalChannelOverrideResetButtonName;
            _globalChannelResetButton.AddToClassList("dpi-global-channel-popup__apply");
            actions.Add(_globalChannelResetButton);
            actions.Add(applyButton);

            popup.Add(actions);
            return popup;
        }

        private void UpdateGlobalChannelOverridePopup()
        {
            if (_globalChannelDropdown == null)
            {
                return;
            }

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            _globalChannelDropdown.SetValueWithoutNotify(GetChannelLabel(selection.Channel));

            if (_globalChannelResetButton != null)
            {
                bool showReset = ShouldShowGlobalChannelReset(selection);
                _globalChannelResetButton.style.display = showReset
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                _globalChannelResetButton.SetEnabled(showReset);
            }
        }

        private PackageChannel GetGlobalProjectChannel()
        {
            return GetGlobalProjectChannelSelection().Channel;
        }

        private PackageChannelSelection GetGlobalProjectChannelSelection()
        {
            return _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.None;
        }

        private void UpdateGlobalChannelOverrideButton()
        {
            if (_graphGlobalChannelButton == null)
            {
                return;
            }

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            DeucarianEditorCommandBar.SetText(
                _graphGlobalChannelButton,
                FormatGlobalChannelButtonLabel(selection));
            _graphGlobalChannelButton.tooltip = GetGlobalChannelButtonTooltip(selection);
        }

        private void SetGlobalChannelOverride(PackageChannel channel)
        {
            PackageChannel safeChannel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;

            _stateRepository?.SetProjectChannel(safeChannel);

            PackageChannelSelection projectChannelSelection = _stateRepository != null
                ? _stateRepository.GetProjectChannelSelection()
                : PackageChannelSelection.Create(safeChannel, DateTime.UtcNow.Ticks);
            _lastObservedProjectChannel = projectChannelSelection.Channel;
            _lastObservedProjectChannelChangedAtUtcTicks = projectChannelSelection.ChangedAtUtcTicks;

            _packageUpdateCheckService?.InvalidateAll();
            InvalidateGraphModelCache("global channel override changed");
            UpdateGlobalChannelOverrideButton();
            UpdateGlobalChannelOverridePopup();
            RefreshGraphView("global channel override changed");
            Repaint();
        }

        private void ClearGlobalChannelOverrideFromPopup()
        {
            _stateRepository?.ClearProjectChannel();

            PackageChannelSelection selection = GetGlobalProjectChannelSelection();
            _lastObservedProjectChannel = selection.Channel;
            _lastObservedProjectChannelChangedAtUtcTicks = selection.ChangedAtUtcTicks;

            _packageUpdateCheckService?.InvalidateAll();
            InvalidateGraphModelCache("global channel override cleared");
            UpdateGlobalChannelOverrideButton();
            UpdateGlobalChannelOverridePopup();
            RefreshGraphView("global channel override cleared");
            HideGlobalChannelOverridePopup();
            Repaint();
        }

        private static string FormatGlobalChannelButtonLabel(PackageChannelSelection selection)
        {
            return (selection.HasValue ? "Override: " : "Channel: ") +
                   GetChannelLabel(selection.Channel);
        }

        private static string GetGlobalChannelButtonTooltip(PackageChannelSelection selection)
        {
            return selection.HasValue
                ? "An explicit project channel override is active. Open to change it or return to the inherited/default channel."
                : "No explicit project override is active. Open to set a project channel override.";
        }

        private static bool ShouldShowGlobalChannelReset(PackageChannelSelection selection)
        {
            return selection.HasValue;
        }

        private static PackageChannel ParseChannelLabel(string label)
        {
            return string.Equals(label, GetChannelLabel(PackageChannel.Development), StringComparison.OrdinalIgnoreCase)
                ? PackageChannel.Development
                : PackageChannel.Stable;
        }

        private void PositionGlobalChannelOverridePopup()
        {
            if (_globalChannelPopup == null ||
                _graphGlobalChannelButton == null ||
                rootVisualElement == null)
            {
                return;
            }

            Rect rootBounds = rootVisualElement.worldBound;
            Rect buttonBounds = _graphGlobalChannelButton.worldBound;
            float maxLeft = Mathf.Max(
                GlobalChannelOverridePopupMargin,
                rootBounds.width - GlobalChannelOverridePopupWidth - GlobalChannelOverridePopupMargin);
            float left = Mathf.Clamp(
                buttonBounds.xMin - rootBounds.xMin,
                GlobalChannelOverridePopupMargin,
                maxLeft);
            float top = Mathf.Max(
                GlobalChannelOverridePopupMargin,
                buttonBounds.yMax - rootBounds.yMin + 5f);

            _globalChannelPopup.style.left = left;
            _globalChannelPopup.style.top = top;
            _globalChannelPopup.style.width = GlobalChannelOverridePopupWidth;
        }

        private void HideGlobalChannelOverridePopup()
        {
            if (_globalChannelPopup != null)
            {
                _globalChannelPopup.style.display = DisplayStyle.None;
            }

            if (rootVisualElement != null)
            {
                rootVisualElement.UnregisterCallback<MouseDownEvent>(
                    HandleGlobalChannelOverrideRootMouseDown,
                    TrickleDown.TrickleDown);
                rootVisualElement.UnregisterCallback<KeyDownEvent>(
                    HandleGlobalChannelOverrideRootKeyDown,
                    TrickleDown.TrickleDown);
            }
        }

        private bool IsGlobalChannelOverridePopupVisible()
        {
            return _globalChannelPopup != null &&
                   _globalChannelPopup.style.display.value == DisplayStyle.Flex;
        }

        private void HandleGlobalChannelOverrideRootMouseDown(MouseDownEvent evt)
        {
            VisualElement target = evt.target as VisualElement;

            if (IsElementOrDescendant(_globalChannelPopup, target) ||
                IsElementOrDescendant(_graphGlobalChannelButton, target))
            {
                return;
            }

            HideGlobalChannelOverridePopup();
        }

        private void HandleGlobalChannelOverrideRootKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
            {
                return;
            }

            HideGlobalChannelOverridePopup();
            evt.StopPropagation();
        }

        private void ApplyGlobalChannelOverrideFromPopup()
        {
            PackageChannel channel = ParseChannelLabel(
                _globalChannelDropdown != null
                    ? _globalChannelDropdown.value
                    : GetChannelLabel(GetGlobalProjectChannel()));
            SetGlobalChannelOverride(channel);
            HideGlobalChannelOverridePopup();
        }

        private static bool IsElementOrDescendant(VisualElement root, VisualElement target)
        {
            for (VisualElement current = target; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return true;
                }
            }

            return false;
        }

        private static VisualElement CreateOperationDrawer(
            Action<bool> verboseLoggingChanged,
            Action retryAction,
            out ScrollView scrollView,
            out VisualElement content,
            out Label titleLabel,
            out Toggle verboseToggle,
            out Label verboseLabel,
            out Label messageLabel)
        {
            DeucarianEditorWorkbenchDrawer sharedDrawer =
                DeucarianEditorWorkbenchSurfaces.CreateDrawer(false);
            VisualElement drawer = sharedDrawer.Root;
            drawer.name = OperationDrawerName;

            scrollView = sharedDrawer.ScrollView;
            scrollView.name = OperationDrawerScrollViewName;
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            content = sharedDrawer.Content;
            content.name = OperationDrawerContentName;

            VisualElement header = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.HeaderRowClass);
            content.Add(header);

            VisualElement titleContent = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.Activity,
                "Last Operation Summary",
                true);
            titleLabel = titleContent.Q<Label>(
                className: DeucarianEditorIconTextButton.LabelClass);
            titleLabel.name = OperationDrawerTitleName;
            titleLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.PrimaryTextClass);
            titleLabel.AddToClassList("deucarian-workbench-operation-drawer__title");
            titleLabel.style.color = DeucarianEditorVisualShell.Text;
            header.Add(titleContent);

            VisualElement optionRow = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.OptionRowClass);
            content.Add(optionRow);

            Toggle localVerboseToggle = new Toggle { name = OperationDrawerVerboseToggleName };
            localVerboseToggle.AddToClassList(
                "deucarian-workbench-operation-drawer__toggle");
            localVerboseToggle.tooltip = "Send normal Package Installer info messages to the Unity Console. Warnings and errors are always logged.";
            if (verboseLoggingChanged != null)
            {
                localVerboseToggle.RegisterValueChangedCallback(evt => verboseLoggingChanged(evt.newValue));
            }
            optionRow.Add(localVerboseToggle);
            verboseToggle = localVerboseToggle;

            VisualElement verboseContent = DeucarianEditorIconTextButton.CreateContent(
                DeucarianEditorIconIds.Logging,
                "Verbose Console Logging",
                true);
            verboseLabel = verboseContent.Q<Label>(
                className: DeucarianEditorIconTextButton.LabelClass);
            verboseLabel.name = OperationDrawerVerboseLabelName;
            verboseLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.SecondaryTextClass);
            verboseLabel.AddToClassList(
                "deucarian-workbench-operation-drawer__option-label");
            verboseLabel.tooltip = localVerboseToggle.tooltip;
            verboseLabel.style.color = DeucarianEditorVisualShell.MutedText;
            verboseLabel.RegisterCallback<ClickEvent>(_ =>
            {
                localVerboseToggle.value = !localVerboseToggle.value;
            });
            optionRow.Add(verboseContent);

            Label localMessageLabel = new Label("No detailed operation report is available.")
            {
                name = OperationDrawerMessageName
            };
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.RowClass);
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.MessageRowClass);
            localMessageLabel.AddToClassList(DeucarianEditorWorkbenchSurfaces.SecondaryTextClass);
            localMessageLabel.AddToClassList(
                "deucarian-workbench-operation-drawer__message");
            localMessageLabel.style.color = DeucarianEditorVisualShell.MutedText;
            content.Add(localMessageLabel);
            messageLabel = localMessageLabel;

            VisualElement reportActions = DeucarianEditorWorkbenchSurfaces.CreateRow(
                DeucarianEditorWorkbenchSurfaces.OptionRowClass);
            Button retryButton = DeucarianEditorWorkbenchSurfaces.CreateDrawerAction(
                DeucarianEditorIconIds.Refresh,
                "Retry",
                retryAction,
                "Retry the latest failed or canceled activity.");
            retryButton.name = OperationDrawerRetryButtonName;
            retryButton.style.display = DisplayStyle.None;
            reportActions.Add(retryButton);
            Button copyDetailsButton = DeucarianEditorWorkbenchSurfaces.CreateDrawerAction(
                DeucarianEditorIconIds.Copy,
                "Copy details",
                () => GUIUtility.systemCopyBuffer = localMessageLabel.text ?? string.Empty,
                "Copy the chronological operation report to the clipboard.");
            reportActions.Add(copyDetailsButton);
            content.Add(reportActions);

            return drawer;
        }

        private void HandleVerboseConsoleLoggingChanged(bool enabled)
        {
            if (PackageInstallerLoggingPreferences.VerboseConsoleLogging == enabled)
            {
                return;
            }

            PackageInstallerLoggingPreferences.VerboseConsoleLogging = enabled;
            RefreshOperationDrawerContent();
        }

        private void RefreshOperationDrawerContent()
        {
            ApplyOperationDrawerData(
                _operationDrawerContainer,
                _operationDrawerScrollView,
                _operationDrawerContent,
                _operationDrawerTitleLabel,
                _operationDrawerVerboseToggle,
                _operationDrawerVerboseLabel,
                _operationDrawerMessageLabel,
                _operationDetailsExpanded,
                PackageInstallerLoggingPreferences.VerboseConsoleLogging,
                GetOperationDrawerReportText());
            UpdateActivityRetryButton();
        }
    }
}
