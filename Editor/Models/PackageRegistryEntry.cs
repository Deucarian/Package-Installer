using System;

namespace Deucarian.PackageInstaller.Editor
{
    [Serializable]
    internal sealed class PackageRegistryEntry
    {
        public string id;
        public string displayName;
        public string category;
        public string type;
        public string description;
        public string stableUrl;
        public string developmentUrl;
        public string ecosystemGroup;
        public string groupId;
        public int overviewOrder;
        public string[] dependencies;
        public string[] optionalCompanions;
        public string[] optionalIntegrations;
        public string[] integrationTargets;
        public string[] suiteMembers;
        public string[] recommendedWith;
    }
}
