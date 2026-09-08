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
    internal static class PackageGraphKeyboard
    {
        public static bool Activate(KeyDownEvent evt, VisualElement owner, Action action)
        {
            if (evt == null || owner == null || evt.target != owner || !IsActivationKey(evt.keyCode))
            {
                return false;
            }

            action?.Invoke();
            evt.StopPropagation();
            return true;
        }

        internal static bool IsActivationKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.Return ||
                   keyCode == KeyCode.KeypadEnter ||
                   keyCode == KeyCode.Space;
        }
    }

    internal sealed class PackageGraphOverflowSummaryElement : Label
    {
        private readonly string _diagnostic;

        public PackageGraphOverflowSummaryElement(string text, string diagnostic)
            : base(text)
        {
            _diagnostic = diagnostic ?? string.Empty;
            focusable = true;
            tabIndex = 0;
            pickingMode = PickingMode.Position;
            RegisterCallback<ClickEvent>(evt =>
            {
                Activate();
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
                PackageGraphKeyboard.Activate(evt, this, Activate));
        }

        internal bool HasKeyboardActivationForTests => !string.IsNullOrEmpty(_diagnostic);

        internal void ActivateForTests(Action<string> clipboardWriter = null)
        {
            CopyDiagnostic(clipboardWriter ?? WriteToSystemClipboard);
        }

        private void Activate()
        {
            CopyDiagnostic(WriteToSystemClipboard);
        }

        private void CopyDiagnostic(Action<string> clipboardWriter)
        {
            clipboardWriter?.Invoke(_diagnostic);
        }

        private static void WriteToSystemClipboard(string diagnostic)
        {
            EditorGUIUtility.systemCopyBuffer = diagnostic;
        }
    }
}
