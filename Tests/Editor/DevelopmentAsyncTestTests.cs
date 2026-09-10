using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentAsyncTestTests
    {
        [UnityTest]
        public IEnumerator AwaitedFailureAssertionPumpsQueuedEditorContinuation() =>
            DevelopmentAsyncTest.Run(AwaitedFailureAssertionPumpsQueuedEditorContinuationAsync);

        public async Task AwaitedFailureAssertionPumpsQueuedEditorContinuationAsync()
        {
            bool resumed = false;
            InvalidOperationException result = await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await Task.Yield();
                resumed = true;
                throw new InvalidOperationException("Expected asynchronous failure.");
            });
            Assert.IsTrue(resumed);
            Assert.AreEqual("Expected asynchronous failure.", result.Message);
        }

        [Test]
        public void CoroutineBridgeYieldsWhileTaskIsPending()
        {
            var pending = new TaskCompletionSource<bool>();
            IEnumerator bridge = DevelopmentAsyncTest.Run(() => pending.Task);
            Assert.IsTrue(bridge.MoveNext());
            Assert.IsNull(bridge.Current);
            pending.SetResult(true);
            Assert.IsFalse(bridge.MoveNext());
        }
    }
}
