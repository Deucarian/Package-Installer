using System;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageVisibilityFilterState
    {
        public PackageVisibilityFilterState()
            : this(string.Empty, showInstalled: true, showNotInstalled: true)
        {
        }

        public PackageVisibilityFilterState(
            string searchText,
            bool showInstalled,
            bool showNotInstalled)
        {
            Set(searchText, showInstalled, showNotInstalled);
        }

        public string SearchText { get; private set; }

        public bool ShowInstalled { get; private set; }

        public bool ShowNotInstalled { get; private set; }

        public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

        public bool HasAnyVisibilityEnabled => ShowInstalled || ShowNotInstalled;

        public bool IsDefault =>
            !HasSearch &&
            ShowInstalled &&
            ShowNotInstalled;

        public bool Set(
            string searchText,
            bool showInstalled,
            bool showNotInstalled)
        {
            string nextSearchText = searchText ?? string.Empty;

            if (string.Equals(SearchText, nextSearchText, StringComparison.Ordinal) &&
                ShowInstalled == showInstalled &&
                ShowNotInstalled == showNotInstalled)
            {
                return false;
            }

            SearchText = nextSearchText;
            ShowInstalled = showInstalled;
            ShowNotInstalled = showNotInstalled;
            return true;
        }

        public bool SetSearchText(string searchText)
        {
            return Set(searchText, ShowInstalled, ShowNotInstalled);
        }

        public bool SetShowInstalled(bool showInstalled)
        {
            return Set(SearchText, showInstalled, ShowNotInstalled);
        }

        public bool SetShowNotInstalled(bool showNotInstalled)
        {
            return Set(SearchText, ShowInstalled, showNotInstalled);
        }

        public bool Reset()
        {
            return Set(string.Empty, showInstalled: true, showNotInstalled: true);
        }
    }

    internal sealed class PackageVisibilityFilterCounts
    {
        public PackageVisibilityFilterCounts(
            int totalCount,
            int installedCount,
            int notInstalledCount,
            int visibleCount)
        {
            TotalCount = Math.Max(0, totalCount);
            InstalledCount = Math.Max(0, installedCount);
            NotInstalledCount = Math.Max(0, notInstalledCount);
            VisibleCount = Math.Max(0, visibleCount);
        }

        public int TotalCount { get; }

        public int InstalledCount { get; }

        public int NotInstalledCount { get; }

        public int VisibleCount { get; }
    }
}
