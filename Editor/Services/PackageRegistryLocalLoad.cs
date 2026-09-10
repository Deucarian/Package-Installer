using System;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryLocalLoad
    {
        private readonly Task<Snapshot> task;

        internal PackageRegistryLocalLoad(PackageRegistryLoader loader, string bundledPath)
            : this(() => Read(loader, bundledPath)) { }

        internal PackageRegistryLocalLoad(Func<Snapshot> read)
        {
            task = Task.Run(read);
        }

        internal bool TryComplete(out Snapshot snapshot)
        {
            snapshot = null;
            if (!task.IsCompleted) return false;
            try { snapshot = task.GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                snapshot = new Snapshot(PackageRegistryLoadResult.Failure(
                    PackageRegistrySource.Bundled, exception.GetBaseException().Message), string.Empty);
            }
            return true;
        }

        internal void Abandon()
        {
            // Reads have no side effects. Observe failures without publishing an obsolete result.
            task.ContinueWith(completed => { var ignored = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        internal static Snapshot Read(PackageRegistryLoader loader, string bundledPath)
        {
            var bundled = loader.LoadBundled(bundledPath);
            bool cached = loader.TryLoadCached(out var result, out string warning);
            return new Snapshot(cached ? result : bundled, warning);
        }

        internal sealed class Snapshot
        {
            internal Snapshot(PackageRegistryLoadResult result, string warning)
            {
                Result = result;
                Warning = warning;
            }
            internal PackageRegistryLoadResult Result { get; }
            internal string Warning { get; }
        }
    }
}
