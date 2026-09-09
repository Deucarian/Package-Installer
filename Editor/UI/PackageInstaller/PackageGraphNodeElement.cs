using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_2022_1_OR_NEWER
using PackageGraphPainter = UnityEngine.UIElements.Painter2D;
#else
using PackageGraphPainter = Deucarian.PackageInstaller.Editor.PackageGraphMeshPainter;
#endif

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageGraphNodeElement : VisualElement
    {
        private readonly PackageGraphNode _node;
        private readonly Action _activationAction;

        public PackageGraphNodeElement(
            PackageGraphNode node,
            PackageGraphLayoutRing ring,
            PackageGraphNodeVisualMode visualMode,
            PackageGraphNodePresentationLevel presentationLevel,
            bool selected,
            bool related,
            bool dimmed,
            bool hoverContext,
            bool hoverDimmed,
            bool previewed,
            bool showActions,
            bool actionsEnabled,
            bool interactionsEnabled,
            string categoryPathLabel,
            string backTooltip,
            string relationshipTooltip,
            Action<PackageDefinition> packageSelected,
            Action<PackageDefinition, PackageGraphNodeAction> packageAction,
            Action selectionCleared,
            Action<string> previewPackage,
            Action<string> clearPreviewPackage)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
            bool isIconOnly = presentationLevel == PackageGraphNodePresentationLevel.IconOnly;
            bool isCompact = presentationLevel == PackageGraphNodePresentationLevel.Compact;
            bool isFull = presentationLevel == PackageGraphNodePresentationLevel.Full;
            bool showTitle = !isIconOnly;
            bool showHierarchy = isCompact || isFull;
            bool showBadges = isCompact || isFull;
            bool showTypeBadge = isFull;
            bool showPackageId = isFull;
            bool showActionShortcut = (isCompact || isFull) &&
                                      showActions &&
                                      PackageGraphNodePresentation.IsGraphShortcutAction(node.PrimaryAction);
            bool showFooter = isFull || showActionShortcut;

            name = node.PackageId;
            AddToClassList("dpi-graph-node");
            AddToClassList("dpi-graph-node--" + PackageGraphNodePresentation.GetNodeClass(node.NodeType));
            AddToClassList("dpi-graph-node--" + PackageGraphNodePresentation.GetVisualModeClass(visualMode));
            AddToClassList("dpi-graph-node--presentation-" + PackageGraphNodePresentation.GetPresentationClass(presentationLevel));
            AddToClassList("dpi-graph-node--status-" + PackageGraphNodePresentation.GetStatusClass(node.Status));
            AddToClassList("dpi-graph-node--ring-" + PackageGraphNodePresentation.GetRingClass(ring));
            EnableInClassList("dpi-graph-node--installed", node.IsInstalled);
            EnableInClassList("dpi-graph-node--actionable", showActionShortcut);
            EnableInClassList("dpi-graph-node--selected", selected);
            EnableInClassList("dpi-graph-node--related", related && !selected);
            EnableInClassList("dpi-graph-node--dimmed", dimmed);
            EnableInClassList("dpi-graph-node--hover-context", hoverContext);
            EnableInClassList("dpi-graph-node--hover-dimmed", hoverDimmed);
            EnableInClassList("dpi-graph-node--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--missing", !node.IsRegistered);
            bool hasBackAffordance = selected && !string.IsNullOrWhiteSpace(backTooltip);
            EnableInClassList("dpi-graph-node--has-back", hasBackAffordance);
            tooltip = PackageGraphNodePresentation.GetCompactTooltip(node, relationshipTooltip);
            if (!node.IsRegistered)
            {
                tooltip += "\nPress Enter or Space to copy this diagnostic.";
            }
            focusable = interactionsEnabled;
            tabIndex = interactionsEnabled ? 0 : -1;

            VisualElement statusRail = new VisualElement();
            statusRail.AddToClassList("dpi-graph-node__status-rail");
            statusRail.AddToClassList("dpi-graph-node__status-rail--" + PackageGraphNodePresentation.GetStatusClass(node.Status));
            Add(statusRail);

            VisualElement glassHighlight = new VisualElement();
            glassHighlight.AddToClassList("dpi-graph-node__glass-highlight");
            glassHighlight.pickingMode = PickingMode.Ignore;
            Add(glassHighlight);

            VisualElement glassSheen = DeucarianEditorGlassSheen.Create();
            Add(glassSheen);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ =>
                {
                    previewPackage?.Invoke(node.PackageId);
                    DeucarianEditorGlassSheen.Play(glassSheen);
                });
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
                RegisterCallback<FocusInEvent>(_ => previewPackage?.Invoke(node.PackageId));
                RegisterCallback<FocusOutEvent>(_ => clearPreviewPackage?.Invoke(node.PackageId));
            }

            if (node.PackageDefinition != null && packageSelected != null)
            {
                Action activate = () =>
                {
                    if (!interactionsEnabled)
                    {
                        return;
                    }

                    if (selected)
                    {
                        selectionCleared?.Invoke();
                    }
                    else
                    {
                        packageSelected(node.PackageDefinition);
                    }
                };
                _activationAction = activate;
                RegisterCallback<ClickEvent>(evt =>
                {
                    activate();
                    evt.StopPropagation();
                });
                RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(evt, this, activate));
            }
            else if (!node.IsRegistered && interactionsEnabled)
            {
                Action copyDiagnostic = () =>
                    EditorGUIUtility.systemCopyBuffer = PackageGraphView.GetMissingPackageDiagnostic(node);
                _activationAction = copyDiagnostic;
                RegisterCallback<KeyDownEvent>(evt =>
                    PackageGraphKeyboard.Activate(evt, this, copyDiagnostic));
            }

            VisualElement header = new VisualElement();
            header.AddToClassList("dpi-graph-node__header");
            Add(header);

            if (hasBackAffordance)
            {
                Image backHint = PackageGraphNodeControls.CreateElementIcon(DeucarianEditorIconIds.Back);
                backHint.tooltip = backTooltip;
                backHint.AddToClassList("dpi-graph-back-hint");
                backHint.AddToClassList("dpi-graph-back-hint--package");
                backHint.AddToClassList("dpi-graph-node__back-hint");
                backHint.pickingMode = PickingMode.Ignore;
                header.Add(backHint);
            }

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(node.IconKey),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = DeucarianEditorTheme.Text
            };
            icon.AddToClassList("dpi-graph-node__icon");
            header.Add(icon);

            VisualElement titleBlock = null;

            if (showTitle)
            {
                titleBlock = new VisualElement();
                titleBlock.AddToClassList("dpi-graph-node__title-block");
                header.Add(titleBlock);

                Label title = new Label(PackageGraphPresentationPolicy.GetGraphTitle(node.DisplayName, presentationLevel));
                title.AddToClassList("dpi-graph-node__title");
                titleBlock.Add(title);
            }

            Image statusMarker = PackageGraphNodeControls.CreateElementIcon(PackageGraphNodePresentation.GetStatusIcon(node.Status));
            statusMarker.tintColor = PackageGraphNodePresentation.GetStatusColor(node.Status);
            statusMarker.AddToClassList("dpi-graph-node__status-icon");
            statusMarker.AddToClassList("dpi-graph-node__status-icon--" + PackageGraphNodePresentation.GetStatusClass(node.Status));
            header.Add(statusMarker);

            if (showPackageId && titleBlock != null)
            {
                Label packageId = new Label(node.PackageId);
                packageId.AddToClassList("dpi-graph-node__package-id");
                titleBlock.Add(packageId);
            }

            if (showHierarchy && !string.IsNullOrWhiteSpace(categoryPathLabel))
            {
                Label categoryPath = new Label(categoryPathLabel);
                categoryPath.AddToClassList("dpi-graph-node__category-path");
                Add(categoryPath);
            }

            if (showBadges)
            {
                VisualElement badges = new VisualElement();
                badges.AddToClassList("dpi-graph-node__badges");

                if (showTypeBadge)
                {
                    badges.Add(PackageGraphNodeControls.CreateBadge(PackageGraphNodePresentation.GetNodeTypeLabel(node.NodeType), "dpi-graph-node__badge--type"));
                }

                badges.Add(PackageGraphNodeControls.CreateBadge(PackageGraphNodePresentation.GetStatusLabel(node), PackageGraphNodePresentation.GetStatusBadgeClass(node.Status)));
                Add(badges);
            }

            if (!showFooter)
            {
                return;
            }

            VisualElement footer = new VisualElement();
            footer.AddToClassList("dpi-graph-node__footer");
            Add(footer);

            if (isFull)
            {
                Label channel = new Label(node.SelectedChannel.ToString());
                channel.AddToClassList("dpi-graph-node__channel");
                footer.Add(channel);
            }

            if (showActionShortcut)
            {
                Button actionButton = DeucarianEditorIconTextButton.Create(
                    PackageGraphNodePresentation.GetActionIcon(node.PrimaryAction),
                    node.PrimaryActionLabel,
                    () =>
                    {
                        if (actionsEnabled && interactionsEnabled)
                        {
                            packageAction?.Invoke(node.PackageDefinition, node.PrimaryAction);
                        }
                    },
                    node.PrimaryActionLabel);
                actionButton.AddToClassList("dpi-graph-node__action");
                float actionHeight = isCompact ? 18f : 24f;
                actionButton.style.height = actionHeight;
                actionButton.style.minHeight = actionHeight;
                actionButton.style.maxHeight = actionHeight;
                actionButton.SetEnabled(actionsEnabled);
                actionButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
                footer.Add(actionButton);
            }
        }

        public void SetPreviewState(bool previewed, bool hoverContext, bool hoverDimmed)
        {
            EnableInClassList("dpi-graph-node--previewed", previewed);
            EnableInClassList("dpi-graph-node--hover-context", hoverContext);
            EnableInClassList("dpi-graph-node--hover-dimmed", hoverDimmed);
        }

        internal bool HasKeyboardActivationForTests => _activationAction != null;

        internal void ActivateForTests()
        {
            _activationAction?.Invoke();
        }

    }
}
