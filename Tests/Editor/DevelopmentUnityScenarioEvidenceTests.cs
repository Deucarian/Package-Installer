using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class DevelopmentUnityScenarioEvidenceTests
    {
        [Test]
        public void IndexFingerprintIncludesTrailingEntriesAndSurvivesUnityJournalRoundTrip()
        {
            const string first = "100644 aaaa 0\tPackages/manifest.json\0";
            const string original = first + "100644 bbbb 0\tPackages/packages-lock.json\0";
            const string changed = first + "100644 cccc 0\tPackages/packages-lock.json\0";
            string originalHash = DevelopmentUnityScenarioRunner.IndexHash(original);
            Assert.AreNotEqual(DevelopmentUnityScenarioRunner.IndexHash(first), originalHash);
            Assert.AreNotEqual(DevelopmentUnityScenarioRunner.IndexHash(changed), originalHash);
            var restored = JsonUtility.FromJson<DevelopmentUnityScenarioState>(JsonUtility.ToJson(
                new DevelopmentUnityScenarioState { ConsumerIndexHash = originalHash }));
            Assert.AreEqual(originalHash, restored.ConsumerIndexHash);
            Assert.AreEqual(64, restored.ConsumerIndexHash.Length);
        }
    }
}
