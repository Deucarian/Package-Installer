namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class PackageListFilterOptions
    {
        public PackageListFilterOptions(
            string searchText,
            bool showInstalled,
            bool showNotInstalled)
        {
            SearchText = searchText ?? string.Empty;
            ShowInstalled = showInstalled;
            ShowNotInstalled = showNotInstalled;
        }

        public string SearchText { get; }

        public bool ShowInstalled { get; }

        public bool ShowNotInstalled { get; }
    }
}
