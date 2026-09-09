using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphLayoutAnimationTests
    {
        [Test]
        public void FirstLayoutSnapsWithoutDependingOnAnEditorClockOrWindow()
        {
            var target = Layout(new Rect(100, 40, 120, 60));
            var animation = new PackageGraphLayoutAnimation();
            bool animate = animation.Begin(target, new PackageGraphTransitionOrigins(null, target, Vector2.zero),
                null, null, null, null, null);
            Assert.That(animate, Is.False);
            Assert.That(animation.Nodes["package"], Is.EqualTo(target.NodeRects["package"]));
        }

        [Test]
        public void ExplicitProgressInterpolatesAndARepeatedEvaluationIsDeterministic()
        {
            var target = Layout(new Rect(100, 40, 120, 60));
            var previous = new Dictionary<string, Rect> { ["package"] = new Rect(0, 0, 120, 60) };
            var animation = new PackageGraphLayoutAnimation();
            Assert.That(animation.Begin(target, new PackageGraphTransitionOrigins(null, target, Vector2.zero),
                previous, null, null, null, null), Is.True);
            animation.Evaluate(0.5f);
            Rect midpoint = animation.Nodes["package"];
            Assert.That(midpoint, Is.EqualTo(new Rect(50, 20, 120, 60)));
            animation.Evaluate(0.5f);
            Assert.That(animation.Nodes["package"], Is.EqualTo(midpoint));
            animation.Evaluate(1f);
            Assert.That(animation.Nodes["package"], Is.EqualTo(target.NodeRects["package"]));
        }

        [Test]
        public void SnapDropsOldFrameStateWhenAReplacementLayoutIsEmpty()
        {
            var animation = new PackageGraphLayoutAnimation();
            animation.Snap(Layout(new Rect(0, 0, 120, 60)));
            Assert.That(animation.Nodes.Count, Is.EqualTo(1));
            animation.Snap(null);
            Assert.That(animation.Nodes, Is.Empty);
            Assert.That(animation.NodeStates, Is.Empty);
            animation.Evaluate(0.5f);
            Assert.That(animation.Nodes, Is.Empty);
        }

        private static PackageGraphLayoutResult Layout(Rect rect)
            => new PackageGraphLayoutResult(PackageGraphLayoutMode.Focus, "package", 800, 600,
                default, Vector2.zero, new Dictionary<string, Rect> { ["package"] = rect }, null, null);
    }
}
