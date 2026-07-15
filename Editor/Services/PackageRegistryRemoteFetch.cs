using System;
using System.Net;
using System.Net.Http;
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
                               HttpCompletionOption.ResponseContentRead,
                               requestCancellation.Token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
