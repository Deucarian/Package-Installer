using System;
using System.IO;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor
{
    internal interface IPackageManifestScratch : IDisposable
    {
        string Root { get; }
        void AssertOwned();
    }

    internal interface IPackageManifestScratchFactory
    {
        IPackageManifestScratch Create();
    }

    // Every request owns one absent GUID directory. No shared repository, config or checkout is modified.
    internal sealed class PackageManifestScratchFactory : IPackageManifestScratchFactory
    {
        internal const long MaximumScratchBytes = 32L * 1024 * 1024;
        private readonly string storage;
        internal PackageManifestScratchFactory(string storage)
        {
            if (string.IsNullOrWhiteSpace(storage) || !Path.IsPathRooted(storage))
                throw new ArgumentException("An absolute package metadata storage directory is required.");
            this.storage = Path.GetFullPath(storage);
        }

        public IPackageManifestScratch Create() => new Scratch(storage);

        private sealed class Scratch : IPackageManifestScratch
        {
            private readonly string storage;
            private readonly string marker;
            private readonly string id = Guid.NewGuid().ToString("N");
            private bool disposed;
            public string Root { get; }

            internal Scratch(string storage)
            {
                this.storage = storage;
                RequireUnlinkedAncestors(storage);
                Directory.CreateDirectory(storage);
                Root = Path.Combine(storage, "manifest-" + id);
                if (Directory.Exists(Root) || File.Exists(Root)) throw new IOException("Metadata scratch directory already exists.");
                Directory.CreateDirectory(Root);
                marker = Path.Combine(Root, ".deucarian-manifest-owner");
                using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream)) writer.Write(id);
                Directory.CreateDirectory(Path.Combine(Root, "disabled-hooks"));
                AssertOwned();
            }

            public void AssertOwned()
            {
                RequireOwnership();
                long bytes = 0;
                foreach (string entry in EnumerateUnlinked(Root))
                {
                    var file = new FileInfo(entry);
                    try { if (file.Exists) bytes += file.Length; }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                    if (bytes > MaximumScratchBytes) throw new PackageManifestScratchLimitException();
                }
                RequireOwnership();
            }

            private void RequireOwnership()
            {
                if (disposed || Path.GetDirectoryName(Path.GetFullPath(Root)) != storage ||
                    Path.GetFileName(Root) != "manifest-" + id) throw new IOException("Metadata scratch ownership changed.");
                RequireUnlinkedAncestors(Root);
                if (!File.Exists(marker) || (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0 || new FileInfo(marker).Length != 32 ||
                    File.ReadAllText(marker) != id) throw new IOException("Metadata scratch ownership changed.");
                if (Directory.GetFileSystemEntries(Path.Combine(Root, "disabled-hooks")).Length != 0)
                    throw new IOException("Metadata scratch hooks directory changed.");
            }

            public void Dispose()
            {
                if (disposed) return;
                // Check the entire tree before deleting anything. A changed/linked directory is retained.
                try
                {
                    RequireOwnership();
                    var entries = EnumerateUnlinked(Root).ToArray();
                    foreach (string file in entries.Where(File.Exists))
                        File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                    Directory.Delete(Root, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally { disposed = true; }
            }

            private static System.Collections.Generic.IEnumerable<string> EnumerateUnlinked(string directory)
                => EnumerateUnlinked(directory, 0, new ScanBudget());

            private static System.Collections.Generic.IEnumerable<string> EnumerateUnlinked(string directory, int depth, ScanBudget budget)
            {
                if (depth > 32) throw new IOException("Metadata scratch tree exceeds the ownership scan depth.");
                using (var entries = OpenEntries(directory, depth > 0))
                {
                    if (entries == null) yield break;
                    while (TryNextEntry(entries, depth > 0, out string entry))
                    {
                        if (++budget.Entries > 10000) throw new IOException("Metadata scratch tree exceeds the ownership scan limit.");
                        FileAttributes attributes;
                        try { attributes = File.GetAttributes(entry); }
                        catch (FileNotFoundException) { continue; }
                        catch (DirectoryNotFoundException) { continue; }
                        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked metadata scratch entry retained.");
                        yield return entry;
                        if ((attributes & FileAttributes.Directory) != 0)
                            foreach (string nested in EnumerateUnlinked(entry, depth + 1, budget)) yield return nested;
                    }
                }
            }

            private static System.Collections.Generic.IEnumerator<string> OpenEntries(string directory, bool allowVanishedDirectory)
            {
                try { return Directory.EnumerateFileSystemEntries(directory).GetEnumerator(); }
                catch (DirectoryNotFoundException) when (allowVanishedDirectory) { return null; }
            }

            private static bool TryNextEntry(System.Collections.Generic.IEnumerator<string> entries, bool allowVanishedDirectory, out string entry)
            {
                entry = null;
                try { if (!entries.MoveNext()) return false; entry = entries.Current; return true; }
                catch (DirectoryNotFoundException) when (allowVanishedDirectory) { return false; }
            }

            private sealed class ScanBudget { internal int Entries; }

            private static void RequireUnlinkedAncestors(string directory)
            {
                for (string current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                    if ((Directory.Exists(current) || File.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Choose package metadata storage outside linked directories.");
            }
        }
    }

    internal sealed class PackageManifestScratchLimitException : IOException { }
}
