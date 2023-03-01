// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

//using KSPDev.ConfigUtils;
using System.Collections.Generic;
using System.Linq;
using KSP2Dev.ConfigUtils;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>A log capture that just accumulates all logs in a plain list.</summary>
[PersistentFieldsFile("KSP2Dev_LogConsole/settings.json", "PlainLogAggregator")]
sealed class PlainLogAggregator : BaseLogAggregator {
  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return LogRecords.ToArray().Reverse();
  }
  
  /// <inheritdoc/>
  public override void ClearAllLogs() {
    LogRecords.Clear();
    ResetLogCounters();
  }
  
  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    LogRecords.Remove(node);
    UpdateLogCounter(node.Value, -1);
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    LogRecords.AddLast(new LogRecord(logRecord));
    UpdateLogCounter(logRecord, 1);
  }
}

} // namespace KSPDev
