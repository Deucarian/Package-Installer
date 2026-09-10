using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryRemoteFetchResponse
    {
        public PackageRegistryRemoteFetchResponse(string content, string entityTag = "")
        {
            Content = content ?? string.Empty;
            EntityTag = entityTag ?? string.Empty;
        }

        public string Content { get; }

        public string EntityTag { get; }
    }

    internal delegate Task<PackageRegistryRemoteFetchResponse> PackageRegistryRemoteFetchDelegate(
        string url,
        CancellationToken cancellationToken,
        TimeSpan timeout);

    internal static class PackageRegistryRemoteFetch
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        private static readonly HttpClient HttpClient = CreateHttpClient();

        public static PackageRegistryRemoteFetchDelegate WrapLegacy(
            Func<string, Task<string>> fetcher)
        {
            if (fetcher == null)
            {
                return FetchAsync;
            }

            return async (url, cancellationToken, timeout) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string content = await fetcher(url);
                cancellationToken.ThrowIfCancellationRequested();
                return new PackageRegistryRemoteFetchResponse(content);
            };
        }

        public static async Task<PackageRegistryRemoteFetchResponse> ExecuteAsync(
            PackageRegistryRemoteFetchDelegate fetcher,
            string url,
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            if (fetcher == null)
            {
                throw new InvalidOperationException("Remote fetcher is unavailable.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan effectiveTimeout = timeout > TimeSpan.Zero ? timeout : DefaultTimeout;

            using (CancellationTokenSource timeoutCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCancellation.CancelAfter(effectiveTimeout);
                Task<PackageRegistryRemoteFetchResponse> fetchTask =
                    fetcher(url, timeoutCancellation.Token, effectiveTimeout);
                Task cancellationTask = Task.Delay(
                    System.Threading.Timeout.Infinite,
                    timeoutCancellation.Token);
                Task completedTask = await Task.WhenAny(fetchTask, cancellationTask).ConfigureAwait(false);

                if (completedTask == fetchTask)
                {
                    try
                    {
                        return await fetchTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            "Remote request timed out after " + effectiveTimeout.TotalSeconds +
                            " seconds: " + url);
                    }
                    finally
                    {
                        timeoutCancellation.Cancel();
                    }
                }

                ObserveFault(fetchTask);

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw new TimeoutException(
                    "Remote request timed out after " + effectiveTimeout.TotalSeconds + " seconds: " + url);
            }
        }

        public static async Task<PackageRegistryRemoteFetchResponse> FetchAsync(
            string url,
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            return await FetchAsync(url, cancellationToken, timeout, 0).ConfigureAwait(false);
        }

        internal static Task<PackageRegistryRemoteFetchResponse> FetchManifestAsync(
            string url, CancellationToken cancellationToken, TimeSpan timeout) =>
            FetchAsync(url, cancellationToken, timeout, PackageManifestReader.MaximumManifestLength);

        private static async Task<PackageRegistryRemoteFetchResponse> FetchAsync(
            string url, CancellationToken cancellationToken, TimeSpan timeout, int maximumBytes)
        {
            using (CancellationTokenSource requestCancellation =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                requestCancellation.CancelAfter(timeout > TimeSpan.Zero ? timeout : DefaultTimeout);
                request.Headers.TryAddWithoutValidation("User-Agent", "Deucarian-Package-Installer");

                try
                {
                    using (HttpResponseMessage response = await HttpClient.SendAsync(
                               request,
                               maximumBytes > 0 ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                               requestCancellation.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string content;
                        if (maximumBytes > 0)
                        {
                            if (response.Content.Headers.ContentLength > maximumBytes)
                                throw new PackageManifestReadException("Public package metadata exceeds the 256 KiB size limit.");
                            using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                content = await ReadBoundedManifestAsync(stream, maximumBytes, requestCancellation.Token).ConfigureAwait(false);
                        }
                        else content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string entityTag = response.Headers.ETag != null
                            ? response.Headers.ETag.ToString()
                            : string.Empty;
                        return new PackageRegistryRemoteFetchResponse(content, entityTag);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        "Remote request timed out after " + timeout.TotalSeconds + " seconds: " + url);
                }
            }
        }

        internal static async Task<string> ReadBoundedManifestAsync(Stream stream, int maximumBytes, CancellationToken token)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] block = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(block, 0, Math.Min(block.Length, maximumBytes - (int)buffer.Length + 1), token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + count > maximumBytes)
                        throw new PackageManifestReadException("Public package metadata exceeds the 256 KiB size limit.");
                    buffer.Write(block, 0, count);
                }
                token.ThrowIfCancellationRequested();
                byte[] bytes = buffer.ToArray();
                int start = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
            };
            HttpClient client = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            return client;
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(
                completed =>
                {
                    Exception ignored = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
