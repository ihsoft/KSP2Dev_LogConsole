// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>A simple wrapper to hold static logs copy originated from any other aggregator.</summary>
sealed class SnapshotLogAggregator : BaseLogAggregator {
  /// <summary>Tells if the loaded records were "flushed".</summary>
  bool _dirtyState;

  /// <summary>Makes copies of the log records from <paramref name="srcAggregator"/>.</summary>
  /// <remarks>Does a deep copy of every record.</remarks>
  /// <param name="srcAggregator">An aggregator to get the log records from.</param>
  public void LoadLogs(BaseLogAggregator srcAggregator) {
    ClearAllLogs();
    foreach (var log in srcAggregator.GetLogRecords()) {
      AggregateLogRecord(log);
    }
    _dirtyState = true;
  }

  /// <inheritdoc/>
  public override void StartCapture() {
    // Do nothing since no capturing is needed.
  }

  /// <inheritdoc/>
  public override void StopCapture() {
    // Nothing to stop.
  }

  /// <inheritdoc/>
  public override bool FlushBufferedLogs() {
    var oldState = _dirtyState;
    _dirtyState = false;
    return oldState;
  }

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    // Return logs exactly as they were copied from the source. No need to make a copy since this collection is static
    // anyways.
    return LogRecords;
  }
  
  /// <inheritdoc/>
  public override void ClearAllLogs() {
    LogRecords.Clear();
    ResetLogCounters();
  }
  
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    LogRecords.Remove(node);
    UpdateLogCounter(node.Value, -1);
  }

  protected override void AggregateLogRecord(LogRecord logRecord) {
    LogRecords.AddLast(new LogRecord(logRecord));
    UpdateLogCounter(logRecord, 1);
  }
}

} // namespace KSPDev
