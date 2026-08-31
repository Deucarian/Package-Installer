using System;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    public sealed class BootstrapEditorOpenBridgeTests
    {
        private static bool opened;

        [SetUp]
        public void SetUp()
        {
            opened = false;
        }

        [Test]
        public void BridgeUsesTheStableBootstrapPublicApiIdentity()
        {
            Assert.AreEqual(
                "Deucarian.Bootstrap.Editor.DeucarianBootstrap, Deucarian.Bootstrap.Editor",
                BootstrapEditorOpenBridge.ApiTypeNameForTests);
        }

        [Test]
        public void TryOpenInvokesOnlyTheExpectedPublicStaticVoidShape()
        {
            Assert.IsTrue(BootstrapEditorOpenBridge.TryOpen(typeof(ValidBootstrapApi)));
            Assert.IsTrue(opened);

            Assert.IsFalse(BootstrapEditorOpenBridge.TryOpen(null));
            Assert.IsFalse(BootstrapEditorOpenBridge.TryOpen(typeof(InstanceBootstrapApi)));
            Assert.IsFalse(BootstrapEditorOpenBridge.TryOpen(typeof(ReturningBootstrapApi)));
        }

        [Test]
        public void TryOpenIsolatesBootstrapInvocationFailure()
        {
            Assert.IsFalse(BootstrapEditorOpenBridge.TryOpen(typeof(ThrowingBootstrapApi)));
        }

        public static class ValidBootstrapApi
        {
            public static void Open()
            {
                opened = true;
            }
        }

        public sealed class InstanceBootstrapApi
        {
            public void Open()
            {
            }
        }

        public static class ReturningBootstrapApi
        {
            public static bool Open()
            {
                return true;
            }
        }

        public static class ThrowingBootstrapApi
        {
            public static void Open()
            {
                throw new InvalidOperationException("expected test failure");
            }
        }
    }
}
