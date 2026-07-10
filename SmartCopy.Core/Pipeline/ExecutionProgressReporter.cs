using System.Diagnostics;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Progress;

namespace SmartCopy.Core.Pipeline;

public sealed partial class PipelineRunner
{
    private sealed class ExecutionProgressReporter
    {
        private readonly PipelineJob _job;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly long _inFlightProgressMinIntervalTicks = Stopwatch.Frequency / 2; // ~2 Hz
        private readonly long _completionProgressMinIntervalTicks;
        private readonly int _completionProgressMaxFiles;

        private long _totalBytes;
        private int _totalFiles;
        private long _completedBytes;
        private int _filesCompleted;
        private DirectoryTreeNode? _inFlightNode;
        private long _inFlightNodeBytes;
        private long _inFlightNodeTotalBytes;
        private long _lastInFlightProgressReportTick;
        private long _lastCompletionProgressReportTick;
        private int _filesSinceLastCompletionProgressReport;

        public ExecutionProgressReporter(PipelineJob job)
        {
            _job = job;
            _completionProgressMinIntervalTicks =
                job.OperationalSettings.CompletionProgressIntervalMs > 0
                    ? (long)(Stopwatch.Frequency * job.OperationalSettings.CompletionProgressIntervalMs / 1000.0)
                    : long.MaxValue;
            _completionProgressMaxFiles = job.OperationalSettings.CompletionProgressBatchFiles > 0
                ? job.OperationalSettings.CompletionProgressBatchFiles
                : int.MaxValue;
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void BeginExecutableStep()
        {
            _totalBytes = _job.RootNode.TotalSelectedBytes;
            _totalFiles = _job.RootNode.NumSelectedFiles;
            _completedBytes = 0;
            _filesCompleted = 0;
            _inFlightNode = null;
            _inFlightNodeBytes = 0;
            _inFlightNodeTotalBytes = 0;
            _lastInFlightProgressReportTick = 0;
            _lastCompletionProgressReportTick = 0;
            _filesSinceLastCompletionProgressReport = 0;
            _stopwatch.Restart();
        }

        public void ReportTransferBytes(DirectoryTreeNode node, long bytesDelta, long fileTotalBytes)
        {
            if (_totalBytes <= 0)
            {
                return;
            }

            if (!ReferenceEquals(_inFlightNode, node))
            {
                _inFlightNode = node;
                _inFlightNodeBytes = 0;
                _inFlightNodeTotalBytes = Math.Max(0, fileTotalBytes);
            }

            if (bytesDelta <= 0)
            {
                return;
            }

            var isWholeFileOneShot =
                _inFlightNodeBytes == 0 &&
                _inFlightNodeTotalBytes > 0 &&
                bytesDelta >= _inFlightNodeTotalBytes;
            if (isWholeFileOneShot)
            {
                return;
            }

            _inFlightNodeBytes += bytesDelta;
            if (_inFlightNodeTotalBytes > 0 && _inFlightNodeBytes > _inFlightNodeTotalBytes)
            {
                _inFlightNodeBytes = _inFlightNodeTotalBytes;
            }

            var completedWithTransfer = Math.Min(_completedBytes + _inFlightNodeBytes, _totalBytes);
            var nowTick = Stopwatch.GetTimestamp();
            if (_lastInFlightProgressReportTick != 0 &&
                nowTick - _lastInFlightProgressReportTick < _inFlightProgressMinIntervalTicks)
            {
                return;
            }

            _lastInFlightProgressReportTick = nowTick;
            var elapsed = _stopwatch.Elapsed;
            _job.Progress?.Report(new OperationProgress(
                CurrentFile: node.CanonicalRelativePath,
                CurrentFileBytes: _inFlightNodeBytes,
                CurrentFileTotalBytes: _inFlightNodeTotalBytes,
                FilesCompleted: _filesCompleted,
                FilesTotal: _totalFiles,
                TotalBytesCompleted: completedWithTransfer,
                TotalBytes: _totalBytes,
                Elapsed: elapsed,
                EstimatedRemaining: EstimateRemaining(elapsed, completedWithTransfer, _totalBytes)));
        }

        public void CompleteResult(TransformResult result, bool isExecutableStep, TimeSpan currentElapsed)
        {
            ClearInFlightNode(result.SourceNode);

            if (!isExecutableStep || !result.IsSuccess || result.SourceNodeResult == SourceResult.None)
            {
                return;
            }

            _filesCompleted += result.NumberOfFilesAffected;
            _completedBytes += result.InputBytes;
            _filesSinceLastCompletionProgressReport += result.NumberOfFilesAffected;

            var nowTick = Stopwatch.GetTimestamp();
            var isFirst = _lastCompletionProgressReportTick == 0;
            var isLast = _filesCompleted >= _totalFiles;
            var enoughFiles = _filesSinceLastCompletionProgressReport >= _completionProgressMaxFiles;
            var enoughTime = !isFirst &&
                             nowTick - _lastCompletionProgressReportTick >= _completionProgressMinIntervalTicks;

            if (!isFirst && !isLast && !enoughFiles && !enoughTime)
            {
                return;
            }

            _job.Progress?.Report(new OperationProgress(
                CurrentFile: result.SourceNode.CanonicalRelativePath,
                CurrentFileBytes: result.InputBytes,
                CurrentFileTotalBytes: result.InputBytes,
                FilesCompleted: _filesCompleted,
                FilesTotal: _totalFiles,
                TotalBytesCompleted: _completedBytes,
                TotalBytes: _totalBytes,
                Elapsed: currentElapsed,
                EstimatedRemaining: EstimateRemaining(currentElapsed, _completedBytes, _totalBytes)));
            _lastCompletionProgressReportTick = nowTick;
            _filesSinceLastCompletionProgressReport = 0;
        }

        private void ClearInFlightNode(DirectoryTreeNode sourceNode)
        {
            if (!ReferenceEquals(_inFlightNode, sourceNode))
            {
                return;
            }

            _inFlightNode = null;
            _inFlightNodeBytes = 0;
            _inFlightNodeTotalBytes = 0;
            _lastInFlightProgressReportTick = 0;
        }
    }
}
