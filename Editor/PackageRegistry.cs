using System;

namespace JorisHoef.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageRegistry
    {
        public int schemaVersion;
        public string updatedAt;
        public PackageRegistryEntry[] packages;
    }
}
