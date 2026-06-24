using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageGraphOpenTiming
    {
        RegistryLookup,
        PackageLookup,
        InstalledPackageLookup,
        DependencyResolution,
        GraphRebuild,
        VisibilitySearch,
        Layout,
        VisualNodeCreation,
        EdgeCreation,
        LayoutRepaintScheduling
    }

    internal sealed class PackageGraphOpenProfiler : IDisposable
    {
        private static readonly IDisposable NullScope = new NullTimingScope();
        private static PackageGraphOpenProfiler _current;

        private readonly Dictionary<PackageGraphOpenTiming, long> _elapsedTicks =
            new Dictionary<PackageGraphOpenTiming, long>();
        private readonly Stopwatch _totalStopwatch;
        private readonly string _reason;
        private readonly string _focusedPackageId;
        private readonly string _focusedGroupId;
        private readonly bool _graphCacheDirtyAtStart;
        private bool _logged;
        private bool _graphRebuilt;
        private int _nodeCount;
        private int _edgeCount;
        private int _renderedNodeCount;
        private int _routeCount;

        private PackageGraphOpenProfiler(
            string reason,
            string focusedPackageId,
            string focusedGroupId,
            bool graphCacheDirtyAtStart)
        {
            _reason = string.IsNullOrWhiteSpace(reason) ? "refresh" : reason.Trim();
            _focusedPackageId = focusedPackageId ?? string.Empty;
            _focusedGroupId = focusedGroupId ?? string.Empty;
            _graphCacheDirtyAtStart = graphCacheDirtyAtStart;
            _totalStopwatch = Stopwatch.StartNew();
        }

        public static bool IsEnabled => PackageInstallerLoggingPreferences.GraphOpenDiagnosticsLogging;

        public static PackageGraphOpenProfiler Current => _current;

        public static PackageGraphOpenProfiler Begin(
            string reason,
            string focusedPackageId,
            string focusedGroupId,
            bool graphCacheDirtyAtStart)
        {
            if (!IsEnabled)
            {
                return null;
            }

            PackageGraphOpenProfiler profiler = new PackageGraphOpenProfiler(
                reason,
                focusedPackageId,
                focusedGroupId,
                graphCacheDirtyAtStart);
            _current = profiler;
            return profiler;
        }

        public static IDisposable Measure(PackageGraphOpenTiming timing)
        {
            return _current == null ? NullScope : new TimingScope(_current, timing);
        }

        public void MarkGraphRebuilt()
        {
            _graphRebuilt = true;
        }

        public void SetGraphCounts(PackageGraphModel graph)
        {
            _nodeCount = graph != null ? graph.Nodes.Count : 0;
            _edgeCount = graph != null ? graph.Edges.Count : 0;
        }

        public void SetRenderCounts(int renderedNodeCount, int routeCount)
        {
            _renderedNodeCount = Math.Max(_renderedNodeCount, renderedNodeCount);
            _routeCount = Math.Max(_routeCount, routeCount);
        }

        public void Dispose()
        {
            if (_current == this)
            {
                _current = null;
            }

            LogSummary();
        }

        private void AddElapsed(PackageGraphOpenTiming timing, long ticks)
        {
            if (_elapsedTicks.TryGetValue(timing, out long existingTicks))
            {
                _elapsedTicks[timing] = existingTicks + ticks;
                return;
            }

            _elapsedTicks[timing] = ticks;
        }

        private void LogSummary()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            _totalStopwatch.Stop();

            StringBuilder message = new StringBuilder(320);
            message.Append("[Graph Open Timing] ");
            message.Append(_reason);
            message.Append(" total=");
            AppendMilliseconds(message, _totalStopwatch.ElapsedTicks);
            message.Append(" cache=");
            message.Append(_graphRebuilt ? "rebuilt" : "hit");
            message.Append(_graphCacheDirtyAtStart ? " dirty" : " clean");

            if (!string.IsNullOrWhiteSpace(_focusedPackageId))
            {
                message.Append(" package=");
                message.Append(_focusedPackageId);
            }
            else if (!string.IsNullOrWhiteSpace(_focusedGroupId))
            {
                message.Append(" group=");
                message.Append(_focusedGroupId);
            }
            else
            {
                message.Append(" root");
            }

            message.Append(" nodes=");
            message.Append(_nodeCount);
            message.Append(" edges=");
            message.Append(_edgeCount);
            message.Append(" renderedNodes=");
            message.Append(_renderedNodeCount);
            message.Append(" routes=");
            message.Append(_routeCount);

            AppendTiming(message, PackageGraphOpenTiming.RegistryLookup, " registry");
            AppendTiming(message, PackageGraphOpenTiming.PackageLookup, " packageLookup");
            AppendTiming(message, PackageGraphOpenTiming.InstalledPackageLookup, " installedLookup");
            AppendTiming(message, PackageGraphOpenTiming.DependencyResolution, " dependencyResolution");
            AppendTiming(message, PackageGraphOpenTiming.GraphRebuild, " graphRebuild");
            AppendTiming(message, PackageGraphOpenTiming.VisibilitySearch, " visibilitySearch");
            AppendTiming(message, PackageGraphOpenTiming.Layout, " layout");
            AppendTiming(message, PackageGraphOpenTiming.VisualNodeCreation, " visualNodes");
            AppendTiming(message, PackageGraphOpenTiming.EdgeCreation, " edgeCreation");
            AppendTiming(message, PackageGraphOpenTiming.LayoutRepaintScheduling, " layoutRepaint");

            PackageInstallerLog.Graph.DiagnosticInfo(message.ToString());
        }

        private void AppendTiming(
            StringBuilder message,
            PackageGraphOpenTiming timing,
            string label)
        {
            message.Append(label);
            message.Append('=');

            if (_elapsedTicks.TryGetValue(timing, out long ticks))
            {
                AppendMilliseconds(message, ticks);
                return;
            }

            message.Append("0.00ms");
        }

        private static void AppendMilliseconds(StringBuilder message, long ticks)
        {
            double milliseconds = ticks * 1000d / Stopwatch.Frequency;
            message.Append(milliseconds.ToString("0.00"));
            message.Append("ms");
        }

        private sealed class TimingScope : IDisposable
        {
            private readonly PackageGraphOpenProfiler _profiler;
            private readonly PackageGraphOpenTiming _timing;
            private readonly long _startTicks;
            private bool _disposed;

            public TimingScope(PackageGraphOpenProfiler profiler, PackageGraphOpenTiming timing)
            {
                _profiler = profiler;
                _timing = timing;
                _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _profiler.AddElapsed(_timing, Stopwatch.GetTimestamp() - _startTicks);
            }
        }

        private sealed class NullTimingScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
