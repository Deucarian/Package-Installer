using System;

namespace Deucarian.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageCompositionPresetEntry
    {
        public string id;
        public string displayName;
        public string description;
        public string[] packageIds;
        public bool recommended;
    }
}
