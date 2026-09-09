using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class PackageGraphPanGestureTests
    {
        [Test]
        public void DragStartsOnlyPastThresholdAndUsesLastPointerDelta()
        {
            var gesture = new PackageGraphPanGesture();
            gesture.Begin(0, Vector2.zero);
            Assert.That(gesture.Move(new Vector2(3f, 4f)), Is.EqualTo(Vector2.zero));
            Assert.That(gesture.HasMoved, Is.False);
            Assert.That(gesture.Move(new Vector2(6f, 4f)), Is.EqualTo(new Vector2(3f, 0f)));
            Assert.That(gesture.HasMoved, Is.True);
            Assert.That(gesture.Move(Vector2.zero), Is.EqualTo(new Vector2(-6f, -4f)));
        }

        [Test]
        public void ResetAndNewGestureDoNotReusePreviousDragState()
        {
            var gesture = new PackageGraphPanGesture();
            Assert.That(gesture.Move(Vector2.one * 20f), Is.EqualTo(Vector2.zero));
            gesture.Begin(1, Vector2.zero);
            gesture.Move(Vector2.one * 20f);
            gesture.Reset();
            Assert.That(gesture.IsCandidate, Is.False);
            Assert.That(gesture.HasMoved, Is.False);
            Assert.That(gesture.Button, Is.EqualTo(-1));
            gesture.Begin(2, Vector2.one * 100f);
            Assert.That(gesture.Button, Is.EqualTo(2));
            Assert.That(gesture.Move(Vector2.one * 101f), Is.EqualTo(Vector2.zero));
        }
    }
}
