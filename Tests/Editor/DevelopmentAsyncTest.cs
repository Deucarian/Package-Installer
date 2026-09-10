using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    // Unity 2021's test framework understands IEnumerator. Yielding keeps the editor's
    // synchronization context pumping; NUnit's synchronous ThrowsAsync does not.
    internal static class DevelopmentAsyncTest
    {
        internal static IEnumerator Run(Func<Task> action)
        {
            Task task = action();
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (elapsed.Elapsed > TimeSpan.FromSeconds(120))
                    Assert.Fail("The development test did not settle within its bounded timeout.");
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }

        internal static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
        {
            Exception observed = null;
            try { await action(); }
            catch (Exception exception) { observed = exception; }
            Assert.That(observed, Is.InstanceOf<T>(), "The asynchronous operation must report its expected failure.");
            return (T)observed;
        }
    }
}
