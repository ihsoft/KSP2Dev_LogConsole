// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using KSP2Dev.ConfigUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>Base class for any log aggregator.</summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
[SuppressMessage("ReSharper", "MemberCanBeProtected.Global")]
public abstract class BaseLogAggregator {
  /// <summary>Defines how many records of each type to keep in <see cref="LogRecords"/>.</summary>
  [PersistentField("maxLogRecords")]
  protected int MaxLogRecords = 300;
  
  /// <summary>Maximum number of cached (and non-aggregated) records.</summary>
  /// <remarks>
  /// Once the limit is reached all the cached records get aggregated via <see cref="AggregateLogRecord"/> method.
  /// </remarks>
  [PersistentField("rawBufferSize")]
  protected int RawBufferSize = 1000;

  /// <summary>A live list of the stored logs.</summary>
  /// <remarks>
  /// This list constantly updates so, *never* iterate over it! Make a copy and then do whatever readonly operations are
  /// needed. Write operations are only allowed from the specific methods.
  /// </remarks>
  protected LinkedList<LogRecord> LogRecords = new LinkedList<LogRecord>();
  
  /// <summary>A number of INFO logs that this aggregator currently holds.</summary>
  /// <remarks>Also counts anything that is not ERROR, WARNING or EXCEPTION.</remarks>
  public int InfoLogsCount { get; private set; }

  /// <summary>A number of WARNING logs that this aggregator currently holds.</summary>
  public int WarningLogsCount { get; private set; }

  /// <summary>A number of ERROR logs that this aggregator currently holds.</summary>
  public int ErrorLogsCount { get; private set; }

  /// <summary>A number of EXCEPTION logs that this aggregator currently holds.</summary>
  public int ExceptionLogsCount { get; private set; }

  /// <summary>A buffer to keep the non-aggregated <see cref="LogInterceptor"/> log records.</summary>
  /// <remarks>
  /// Call <see cref="FlushBufferedLogs"/> before accessing aggregated logs to have up to date state.
  /// </remarks>
  readonly List<LogInterceptor.Log> _rawLogsBuffer = new List<LogInterceptor.Log>();

  /// <summary>Returns the aggregated logs.</summary>
  /// <remarks>
  /// Implementation decides how exactly <see cref="LogRecords"/> are returned to the consumer. Main requirement: the
  /// collection must *NOT* change once returned. Returning a collection copy is highly encouraged.
  /// <p>Note: changing of the items in the collection is acceptable. Deep copy is not required.</p>
  /// </remarks>
  /// <returns>A list of records.</returns>
  public abstract IEnumerable<LogRecord> GetLogRecords();
  
  /// <summary>Clears all currently aggregated logs.</summary>
  /// <remarks>Must at least clear <see cref="LogRecords"/> and reset counters.</remarks>
  public abstract void ClearAllLogs();

  /// <summary>Drops an aggregated log in <see cref="LogRecords"/>.</summary>
  /// <remarks>Called by the parent when it decides a log record must be dropped. Implementation must obey.</remarks>
  /// <param name="node">A list node to remove.</param>
  protected abstract void DropAggregatedLogRecord(LinkedListNode<LogRecord> node);
  
  /// <summary>Adds a new log record to the aggregation.</summary>
  /// <remarks>
  /// Parent calls this method when it wants a record to be counted. It's up to the implementation what to do with the
  /// record.
  /// </remarks>
  /// <param name="logRecord">
  /// Log record from <see cref="LogInterceptor"/>. Do <i>not</i> store this instance! If this log record needs to be
  /// stored make a copy via <see cref="LogRecord"/> constructor.
  /// </param>
  protected abstract void AggregateLogRecord(LogRecord logRecord);
  
  /// <summary>Initiates log capturing by this aggregator.</summary>
  /// <remarks>It's ok to call this method multiple times.</remarks>
  public virtual void StartCapture() {
    LogInterceptor.RegisterPreviewCallback(LogPreview);
  }

  /// <summary>Stops log capturing by this aggregator.</summary>
  public virtual void StopCapture() {
    LogInterceptor.UnregisterPreviewCallback(LogPreview);
    FlushBufferedLogs();
  }
  
  /// <summary>Re-scans aggregated logs applying the current filters.</summary>
  /// <remarks>
  /// Call it when settings in <see cref="LogFilter"/> has changed, and log records that matched the new filters need to
  /// be removed.
  /// </remarks>
  public virtual void UpdateFilter() {
    FlushBufferedLogs();
    var node = LogRecords.First;
    while (node != null) {
      var removeNode = node;
      node = node.Next;
      if (CheckIsFiltered(removeNode.Value.SrcLog)) {
        DropAggregatedLogRecord(removeNode);
      }
    }
  }

  /// <summary>Flushes all non-aggregated logs.</summary>
  /// <returns><c>true</c> if there were pending changes.</returns>
  public virtual bool FlushBufferedLogs() {
    var res = _rawLogsBuffer.Count > 0;
    if (_rawLogsBuffer.Count > 0) {
      // Get a snapshot to not get affected by the updates.
      var rawLogsCopy = _rawLogsBuffer.ToArray();
      _rawLogsBuffer.Clear();
      foreach (var log in rawLogsCopy) {
        var logRecord = new LogRecord(log);
        AggregateLogRecord(logRecord);
      }
      DropExcessiveRecords();
    }
    return res;
  }

  /// <summary>Verifies if <paramref name="log"/> matches the filters.</summary>
  /// <param name="log">A log record to check.</param>
  /// <returns><c>true</c> if any of the filters matched.</returns>
  protected virtual bool CheckIsFiltered(LogInterceptor.Log log) {
    return LogFilter.CheckLogForFilter(log);
  }

  /// <summary>Resets all log counters to zero.</summary>
  /// <remarks>If implementation calls this method then all aggregated logs must be cleared as well.</remarks>
  protected virtual void ResetLogCounters() {
    InfoLogsCount = 0;
    WarningLogsCount = 0;
    ErrorLogsCount = 0;
    ExceptionLogsCount = 0;
  }
  
  /// <summary>Updates counters for the log record type.</summary>
  /// <remarks>Implementation must call this method every time when number of record in
  /// <see cref="LogRecords"/> changes.</remarks>
  /// <param name="logRecord">A log record to get type from.</param>
  /// <param name="delta">Delta to add to the current counter.</param>
  protected virtual void UpdateLogCounter(LogRecord logRecord, int delta) {
    switch (logRecord.SrcLog.Type) {
      case LogType.Log:
        InfoLogsCount += delta;
        break;
      case LogType.Warning:
        WarningLogsCount += delta;
        break;
      case LogType.Error: 
        ErrorLogsCount += delta; 
        break;
      case LogType.Exception: 
        ExceptionLogsCount += delta; 
        break;
    }
  }

  /// <summary>Cleanups extra log records.</summary>
  /// <remarks>Limit of <see cref="MaxLogRecords"/> is applied per type.</remarks>
  void DropExcessiveRecords() {
    var node = LogRecords.First;
    while (node != null
        && (InfoLogsCount > MaxLogRecords
            || WarningLogsCount > MaxLogRecords
            || ErrorLogsCount > MaxLogRecords
            || ExceptionLogsCount > MaxLogRecords)) {
      var removeNode = node;
      node = node.Next;
      var logType = removeNode.Value.SrcLog.Type;
      if (logType == LogType.Log && InfoLogsCount > MaxLogRecords
          || logType == LogType.Warning && WarningLogsCount > MaxLogRecords
          || logType == LogType.Error && ErrorLogsCount > MaxLogRecords
          || logType == LogType.Exception && ExceptionLogsCount > MaxLogRecords) {
        DropAggregatedLogRecord(removeNode);
      }
    }
  }

  /// <summary>A callback handler for incoming Unity log records.</summary>
  /// <remarks>
  /// <para>
  /// The record is only stored if it's not banned by <see cref="CheckIsFiltered"/>.
  /// </para>
  /// <para>
  /// The incoming records are buffered in a list, and get aggregated when the buffer is exhausted.
  /// Such approach saves CPU when no log console UI is presented.
  /// </para>
  /// </remarks>
  /// <param name="log">Raw log record.</param>
  void LogPreview(LogInterceptor.Log log) {
    if (CheckIsFiltered(log)) {
      return;
    }
    // Override unsupported log types into INFO.
    if (log.Type != LogType.Log && log.Type != LogType.Warning
        && log.Type != LogType.Error && log.Type != LogType.Exception) {
      log.Type = LogType.Log;
    }
    _rawLogsBuffer.Add(log);
    if (_rawLogsBuffer.Count >= RawBufferSize) {
      FlushBufferedLogs();
    }
  }
}

} // namespace KSPDev
