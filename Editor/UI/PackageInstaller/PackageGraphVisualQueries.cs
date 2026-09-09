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
    internal static class PackageGraphVisualQueries
    {
        internal static bool HasAncestorClass(VisualElement target, string className)
        {
            return FindAncestorWithClass(target, className) != null;
        }

        internal static VisualElement FindAncestorWithClass(VisualElement target, string className)
        {
            VisualElement current = target;

            while (current != null)
            {
                if (current.ClassListContains(className))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }
    }
}
