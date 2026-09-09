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
    internal static class PackageChannelPolicy
    {
        internal static PackageChannel ResolveSelectedChannel(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel)
        {
            if (packageDefinition == null)
            {
                return PackageChannel.Stable;
            }

            PackageChannelSelection latestExplicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (latestExplicitSelection.HasValue)
            {
                return ResolveConfiguredChannel(packageDefinition, latestExplicitSelection.Channel);
            }

            return PackageChannel.Stable;
        }

        internal static PackageChannelSelection GetLatestExplicitChannelSelection(
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection)
        {
            if (packageSelection.HasValue &&
                (!projectSelection.HasValue ||
                 packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks))
            {
                return packageSelection;
            }

            return projectSelection.HasValue
                ? projectSelection
                : PackageChannelSelection.None;
        }

        internal static PackageChannel ResolveConfiguredChannel(
            PackageDefinition packageDefinition,
            PackageChannel channel)
        {
            if (channel == PackageChannel.Development &&
                packageDefinition != null &&
                packageDefinition.HasDevelopmentUrl)
            {
                return PackageChannel.Development;
            }

            return PackageChannel.Stable;
        }

        internal static string GetContextualChannelProvenance(
            PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection,
            PackageChannelSelection packageSelection,
            bool hasInstalledChannel,
            PackageChannel installedChannel,
            string installedSourceReason)
        {
            if (packageDefinition == null)
            {
                return string.Empty;
            }

            if (hasInstalledChannel && installedChannel == PackageChannel.Custom)
            {
                return string.IsNullOrWhiteSpace(installedSourceReason)
                    ? "Custom installed source"
                    : "Custom installed source - " + installedSourceReason.Trim();
            }

            PackageChannelSelection explicitSelection = GetLatestExplicitChannelSelection(
                projectSelection,
                packageSelection);

            if (!explicitSelection.HasValue)
            {
                return string.Empty;
            }

            bool packageOverride = packageSelection.HasValue &&
                                   (!projectSelection.HasValue ||
                                    packageSelection.ChangedAtUtcTicks > projectSelection.ChangedAtUtcTicks);
            string scope = packageOverride ? "Package override" : "Project override";

            if (explicitSelection.Channel == PackageChannel.Development &&
                !packageDefinition.HasDevelopmentUrl)
            {
                return scope + " requested Development - using Stable fallback";
            }

            return scope + " - " + GetChannelLabel(explicitSelection.Channel);
        }

        internal static PackageChannel[] GetChannelOptions(
            PackageDefinition packageDefinition,
            PackageChannel selectedChannel)
        {
            List<PackageChannel> channels = new List<PackageChannel>
            {
                PackageChannel.Stable
            };

            if (packageDefinition != null && packageDefinition.HasDevelopmentUrl)
            {
                channels.Add(PackageChannel.Development);
            }

            if (selectedChannel == PackageChannel.Custom)
            {
                channels.Add(PackageChannel.Custom);
            }

            return channels.Distinct().ToArray();
        }

        internal static string GetChannelLabel(PackageChannel channel)
        {
            switch (channel)
            {
                case PackageChannel.Development:
                    return "Development";
                case PackageChannel.Custom:
                    return "Custom";
                default:
                    return "Stable";
            }
        }

        internal static string FormatGlobalChannelButtonLabel(PackageChannelSelection selection)
        {
            return (selection.HasValue ? "Override: " : "Channel: ") +
                   GetChannelLabel(selection.Channel);
        }

        internal static string GetGlobalChannelButtonTooltip(PackageChannelSelection selection)
        {
            return selection.HasValue
                ? "An explicit project channel override is active. Open to change it or return to the inherited/default channel."
                : "No explicit project override is active. Open to set a project channel override.";
        }

        internal static bool ShouldShowGlobalChannelReset(PackageChannelSelection selection)
        {
            return selection.HasValue;
        }

        internal static PackageChannel ParseChannelLabel(string label)
        {
            return string.Equals(label, GetChannelLabel(PackageChannel.Development), StringComparison.OrdinalIgnoreCase)
                ? PackageChannel.Development
                : PackageChannel.Stable;
        }
    }
}
