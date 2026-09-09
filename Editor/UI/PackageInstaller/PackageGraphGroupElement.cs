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
    internal sealed class PackageGraphGroupElement : VisualElement
    {
        public PackageGraphGroupElement(
            PackageGraphGroupLayoutNode groupNode,
            PackageGraphLayoutMode layoutMode,
            bool hoverContext,
            bool hoverDimmed,
            string backTooltip,
            bool interactionsEnabled,
            Action<PackageGraphGroup> groupFocused,
            Action<string> previewGroup,
            Action<string> clearPreviewGroup,
            int searchMatchCount = 0)
        {
            if (groupNode == null || groupNode.Group == null)
            {
                throw new ArgumentNullException(nameof(groupNode));
            }

            name = "group-" + groupNode.GroupId;
            AddToClassList("dpi-graph-group");
            AddToClassList("dpi-graph-group--" + GetRingClass(groupNode.Ring));
            EnableInClassList("dpi-graph-group--focused", groupNode.Focused);
            EnableInClassList("dpi-graph-group--collapsed", groupNode.Collapsed);
            EnableInClassList(
                "dpi-graph-group--overview",
                layoutMode == PackageGraphLayoutMode.Overview);
            EnableInClassList("dpi-graph-group--locked", !interactionsEnabled);
            EnableInClassList("dpi-graph-group--attention", groupNode.AttentionCount > 0);
            EnableInClassList("dpi-graph-group--empty", groupNode.PackageCount == 0);
            EnableInClassList("dpi-graph-group--hover-context", hoverContext);
            EnableInClassList("dpi-graph-group--hover-dimmed", hoverDimmed);
            bool hasBackAffordance = !string.IsNullOrWhiteSpace(backTooltip);
            EnableInClassList("dpi-graph-group--has-back", hasBackAffordance);
            tooltip = hasBackAffordance ? backTooltip : groupNode.Group.DisplayName;
            focusable = interactionsEnabled;
            tabIndex = interactionsEnabled ? 0 : -1;

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => previewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<MouseLeaveEvent>(_ => clearPreviewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<FocusInEvent>(_ => previewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<FocusOutEvent>(_ => clearPreviewGroup?.Invoke(groupNode.GroupId));
                RegisterCallback<ClickEvent>(evt =>
                {
                    groupFocused?.Invoke(groupNode.Group);
                    evt.StopPropagation();
                });
                RegisterCallback<KeyDownEvent>(evt => PackageGraphKeyboard.Activate(
                    evt,
                    this,
                    () => groupFocused?.Invoke(groupNode.Group)));
            }

            float symbolSize = Mathf.Min(groupNode.HubRect.width, groupNode.HubRect.height);
            float symbolLeft = groupNode.HubRect.x - groupNode.Rect.x;
            float symbolTop = groupNode.HubRect.y - groupNode.Rect.y;

            VisualElement symbol = new VisualElement();
            symbol.AddToClassList("dpi-graph-group__symbol");
            symbol.style.position = Position.Absolute;
            symbol.style.left = symbolLeft;
            symbol.style.top = symbolTop;
            symbol.style.width = symbolSize;
            symbol.style.height = symbolSize;
            Add(symbol);

            VisualElement symbolHighlight = new VisualElement();
            symbolHighlight.AddToClassList("dpi-graph-group__glass-highlight");
            symbolHighlight.pickingMode = PickingMode.Ignore;
            symbol.Add(symbolHighlight);

            VisualElement symbolSheen = DeucarianEditorGlassSheen.Create();
            symbol.Add(symbolSheen);

            if (interactionsEnabled)
            {
                RegisterCallback<MouseEnterEvent>(_ => DeucarianEditorGlassSheen.Play(symbolSheen));
            }

            Image icon = new Image
            {
                image = DeucarianEditorIcons.GetPackageIcon(groupNode.Group.IconKey),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = DeucarianEditorTheme.Text
            };
            icon.AddToClassList("dpi-graph-group__icon");
            symbol.Add(icon);

            VisualElement caption = new VisualElement();
            caption.AddToClassList("dpi-graph-group__caption");
            caption.style.position = Position.Absolute;
            caption.style.left = 0f;
            caption.style.top = symbolTop + symbolSize + 7f;
            caption.style.width = groupNode.Rect.width;
            Add(caption);

            VisualElement titleRow = new VisualElement();
            titleRow.AddToClassList("dpi-graph-category-caption-row");
            caption.Add(titleRow);

            if (hasBackAffordance)
            {
                Image back = CreateElementIcon(DeucarianEditorIconIds.Back);
                back.AddToClassList("dpi-graph-back-hint");
                back.AddToClassList("dpi-graph-back-hint--category");
                back.AddToClassList("dpi-graph-group__back-hint");
                back.pickingMode = PickingMode.Ignore;
                back.tooltip = backTooltip;
                titleRow.Add(back);
            }

            Label title = new Label(GetTitle(groupNode));
            title.AddToClassList("dpi-graph-group__title");
            titleRow.Add(title);

            string subtitleText = layoutMode == PackageGraphLayoutMode.Overview && searchMatchCount > 0
                ? searchMatchCount + (searchMatchCount == 1 ? " match" : " matches")
                : (groupNode.Collapsed && !string.IsNullOrWhiteSpace(groupNode.SummaryLabel)
                    ? groupNode.SummaryLabel
                    : PackageGraphCategoryStatusVisuals.FormatTotal(groupNode.PackageCount));
            Label subtitle = new Label(subtitleText);
            subtitle.AddToClassList("dpi-graph-group__subtitle");
            caption.Add(subtitle);

            VisualElement stats = new VisualElement();
            stats.AddToClassList("dpi-graph-group__stats");
            stats.Add(CreateStat("Installed", groupNode.InstalledCount, "installed"));
            stats.Add(CreateStat("Not installed", groupNode.NotInstalledCount, "available"));
            if (groupNode.AttentionCount > 0)
            {
                stats.Add(CreateStat("Attention", groupNode.AttentionCount, "attention"));
            }

            if (groupNode.UnknownCount > 0)
            {
                stats.Add(CreateStat("Unknown", groupNode.UnknownCount, "unknown"));
            }

            caption.Add(stats);

        }

        public void SetHoverState(bool hoverContext, bool hoverDimmed)
        {
            EnableInClassList("dpi-graph-group--hover-context", hoverContext);
            EnableInClassList("dpi-graph-group--hover-dimmed", hoverDimmed);
        }

        private static string GetTitle(PackageGraphGroupLayoutNode groupNode)
        {
            return groupNode.Group.DisplayName;
        }

        private static VisualElement CreateStat(string label, int count, string className)
        {
            VisualElement stat = DeucarianEditorIconTextButton.CreateContent(
                GetStatusIcon(className),
                count + " " + label.ToLowerInvariant(),
                leading: true);
            Color color = GetStatusColor(className);
            Image icon = stat.Q<Image>(className: DeucarianEditorIconTextButton.IconClass);
            Label text = stat.Q<Label>(className: DeucarianEditorIconTextButton.LabelClass);
            if (icon != null)
            {
                icon.tintColor = color;
            }
            if (text != null)
            {
                text.style.color = color;
            }
            stat.AddToClassList("dpi-graph-group__stat");
            stat.AddToClassList("dpi-graph-group__stat--" + className);
            return stat;
        }

        private static string GetStatusIcon(string className)
        {
            switch (className)
            {
                case "installed":
                    return DeucarianEditorIconIds.Success;
                case "update":
                case "attention":
                    return DeucarianEditorIconIds.Warning;
                case "unknown":
                    return DeucarianEditorIconIds.Info;
                default:
                    return DeucarianEditorIconIds.Available;
            }
        }

        private static Color GetStatusColor(string className)
        {
            switch (className)
            {
                case "installed":
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Installed, 0.90f);
                case "attention":
                case "update":
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Update, 0.95f);
                case "unknown":
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Unknown, 0.84f);
                default:
                    return DeucarianEditorGraphTheme.WithAlpha(DeucarianEditorGraphTheme.Available, 0.90f);
            }
        }

        private static Image CreateElementIcon(string iconId)
        {
            return new Image
            {
                image = DeucarianEditorIcons.GetIcon(iconId),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = DeucarianEditorTheme.Text,
                pickingMode = PickingMode.Ignore
            };
        }

        private static string GetRingClass(PackageGraphLayoutRing ring)
        {
            switch (ring)
            {
                case PackageGraphLayoutRing.Runtime:
                    return "runtime";
                case PackageGraphLayoutRing.Integration:
                    return "integration";
                case PackageGraphLayoutRing.Suite:
                    return "suite";
                default:
                    return "infrastructure";
            }
        }
    }
}
