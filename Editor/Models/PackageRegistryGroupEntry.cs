using System;

namespace Deucarian.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageRegistryGroupEntry
    {
        public string id;
        public string displayName;
        public string parentGroupId;
        public string description;
        public int sortOrder;
        public string iconKey;
        public string styleKey;
    }
}
