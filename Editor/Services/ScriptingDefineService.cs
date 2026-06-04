using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using UnityEngine;

namespace JorisHoef.PackageInstaller.Editor
{
    internal sealed class ScriptingDefineService
    {
        private const string LogPrefix = "[JorisHoef Package Installer]";

        public BuildTargetGroup SelectedBuildTargetGroup
        {
            get
            {
                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
                return group == BuildTargetGroup.Unknown ? EditorUserBuildSettings.selectedBuildTargetGroup : group;
            }
        }

        public string GetSymbols(BuildTargetGroup buildTargetGroup)
        {
            if (buildTargetGroup == BuildTargetGroup.Unknown)
            {
                return string.Empty;
            }

            return GetScriptingDefineSymbols(buildTargetGroup);
        }

        public bool HasSymbols(BuildTargetGroup buildTargetGroup, IEnumerable<string> symbols)
        {
            string[] requiredSymbols = NormalizeSymbols(symbols).ToArray();

            if (requiredSymbols.Length == 0)
            {
                return true;
            }

            HashSet<string> existingSymbols = GetSymbolSet(buildTargetGroup);
            return requiredSymbols.All(existingSymbols.Contains);
        }

        public bool AddSymbolsToSelectedBuildTargetGroup(IEnumerable<string> symbols)
        {
            return AddSymbols(SelectedBuildTargetGroup, symbols);
        }

        public bool AddSymbols(BuildTargetGroup buildTargetGroup, IEnumerable<string> symbols)
        {
            string[] requiredSymbols = NormalizeSymbols(symbols).ToArray();

            if (requiredSymbols.Length == 0)
            {
                return false;
            }

            if (buildTargetGroup == BuildTargetGroup.Unknown)
            {
                Debug.LogWarning(LogPrefix + " Cannot add scripting define symbols for an unknown build target group.");
                return false;
            }

            List<string> orderedSymbols = NormalizeSymbols(
                    GetSymbols(buildTargetGroup).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            HashSet<string> existingSymbols = new HashSet<string>(orderedSymbols, StringComparer.Ordinal);
            bool changed = false;

            foreach (string symbol in requiredSymbols)
            {
                if (existingSymbols.Add(symbol))
                {
                    orderedSymbols.Add(symbol);
                    changed = true;
                }
            }

            if (!changed)
            {
                Debug.Log(LogPrefix + " Scripting define symbols already present for " + buildTargetGroup + ": " + string.Join(", ", requiredSymbols) + ".");
                return false;
            }

            string nextSymbols = string.Join(";", orderedSymbols);
            SetScriptingDefineSymbols(buildTargetGroup, nextSymbols);

            Debug.Log(LogPrefix + " Added scripting define symbols for " + buildTargetGroup + ": " + string.Join(", ", requiredSymbols) + ".");
            return true;
        }

        private static string GetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup)
        {
#if UNITY_2021_2_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
#endif
        }

        private static void SetScriptingDefineSymbols(BuildTargetGroup buildTargetGroup, string symbols)
        {
#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup), symbols);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, symbols);
#endif
        }

        private HashSet<string> GetSymbolSet(BuildTargetGroup buildTargetGroup)
        {
            string currentSymbols = GetSymbols(buildTargetGroup);
            return new HashSet<string>(
                NormalizeSymbols(currentSymbols.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)),
                StringComparer.Ordinal);
        }

        private static IEnumerable<string> NormalizeSymbols(IEnumerable<string> symbols)
        {
            if (symbols == null)
            {
                yield break;
            }

            foreach (string symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                {
                    continue;
                }

                yield return symbol.Trim();
            }
        }
    }
}
