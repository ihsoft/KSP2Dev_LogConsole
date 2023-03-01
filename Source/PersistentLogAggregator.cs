// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System;
using System.Diagnostics.CodeAnalysis;
using KSP2Dev.ConfigUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>A log capture that writes logs on disk.</summary>
/// <remarks>
/// <p>Three files are created: <list type="bullet">
/// <item><c>INFO</c> that includes all logs;</item>
/// <item><c>WARNING</c> which captures warnings and errors;</item>
/// <item><c>ERROR</c> for the errors (including exceptions).</item>
/// </list>
/// </p>
/// <p>Persistent logging must be explicitly enabled via <c>PersistentLogs-settings.cfg</c></p>
/// </remarks>
[PersistentFieldsFile("KSP2Dev_LogConsole/settings.json", "PersistentLog")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
[SuppressMessage("ReSharper", "ConvertToConstant.Local")]
sealed class PersistentLogAggregator : BaseLogAggregator {
  [PersistentField("enableLogger")]
  bool _enableLogger = true;
  
  [PersistentField("logFilesPath")]
  string _logFilePath = "KSPDev_logs";
  
  /// <summary>Prefix for every log file name.</summary>
  [PersistentField("logFilePrefix")]
  string _logFilePrefix = "KSPDev-LOG";
  
  /// <summary>Format of the timestamp in the file.</summary>
  [PersistentField("logTsFormat")]
  string _logTsFormat = "yyMMdd\\THHmmss";

  /// <summary>Specifies if INFO file should be written.</summary>
  [PersistentField("writeInfoFile")]
  bool _writeInfoFile = true;

  /// <summary>Specifies if WARNING file should be written.</summary>
  [PersistentField("writeWarningFile")]
  bool _writeWarningFile = true;

  /// <summary>Specifies if ERROR file should be written.</summary>
  [PersistentField("writeErrorFile")]
  bool _writeErrorFile = true;

  /// <summary>Limits total number of log files in the directory.</summary>
  /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
  [PersistentField("cleanupPolicy/totalFiles")]
  int _totalFiles = 30;

  /// <summary>Limits total size of all log files in the directory.</summary>
  /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
  [PersistentField("cleanupPolicy/totalSizeMb")]
  int _totalSizeMb = 100;

  /// <summary>Maximum age of the log files in the directory.</summary>
  /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
  [PersistentField("cleanupPolicy/maxAgeHours")]
  float _maxAgeHours = 168;

  /// <summary>Specifies if new record should be aggregated and persisted.</summary>
  bool _writeLogsToDisk;

  /// <summary>A writer that gets all the logs.</summary>
  StreamWriter _infoLogWriter;
  
  /// <summary>A writer for <c>WARNING</c>, <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter _warningLogWriter;

  /// <summary>Writer for <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter _errorLogWriter;

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return LogRecords;  // It's always empty.
  }

  /// <inheritdoc/>
  public override void ClearAllLogs() {
    // Cannot clear persistent log so, restart the files instead.
    StartLogFiles();
  }

  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    // Do nothing since there is no memory state in the aggregator.
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    if (!_writeLogsToDisk) {
      return;
    }
    var message = logRecord.MakeTitle();
    var type = logRecord.SrcLog.Type;
    if (type == LogType.Exception && logRecord.SrcLog.StackTrace.Length > 0) {
      message += "\n" + logRecord.SrcLog.StackTrace;
    }
    try {
      if (_infoLogWriter != null) {
        _infoLogWriter.WriteLine(message);
      }
      if (_warningLogWriter != null
          && (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)) {
        _warningLogWriter.WriteLine(message);
      }
      if (_errorLogWriter != null && (type == LogType.Error || type == LogType.Exception)) {
        _errorLogWriter.WriteLine(message);
      }
    } catch (Exception ex) {
      _writeLogsToDisk = false;
      Debug.LogException(ex);
      Debug.LogError("Persistent log aggregator failed to write a record. Logging disabled");
    }
  }

  /// <inheritdoc/>
  public override void StartCapture() {
    base.StartCapture();
    StartLogFiles();
    PersistentLogAggregatorFlusher.ActiveAggregators.Add(this);
    if (_writeLogsToDisk) {
      Debug.Log("Persistent aggregator started");
    } else {
      Debug.LogWarning("Persistent aggregator disabled");
    }
  }

  /// <inheritdoc/>
  public override void StopCapture() {
    Debug.Log("Stopping a persistent aggregator...");
    base.StopCapture();
    StopLogFiles();
    PersistentLogAggregatorFlusher.ActiveAggregators.Remove(this);
  }

  /// <inheritdoc/>
  public override bool FlushBufferedLogs() {
    // Flushes accumulated logs to disk. In case of disk error the logging is disabled.
    var res = base.FlushBufferedLogs();
    if (res && _writeLogsToDisk) {
      try {
        if (_infoLogWriter != null) {
          _infoLogWriter.Flush();
        }
        if (_warningLogWriter != null) {
          _warningLogWriter.Flush();
        }
        if (_errorLogWriter != null) {
          _errorLogWriter.Flush();
        }
      } catch (Exception ex) {
        _writeLogsToDisk = false;  // Must be the first statement in the catch section!
        Debug.LogException(ex);
        Debug.LogError("Something went wrong when flushing data to disk. Disabling logging.");
      }
    }
    return res;
  }

  /// <inheritdoc/>
  protected override bool CheckIsFiltered(LogInterceptor.Log log) {
    return false;  // Persist any log!
  }

  /// <summary>Creates new logs files and redirects logs to there.</summary>
  void StartLogFiles() {
    StopLogFiles();  // In case something was opened.
    try {
      CleanupLogFiles();
    } catch (Exception ex) {
      Debug.LogException(ex);
      Debug.LogError("Error happen while cleaning up old logs");
    }
    try {
      if (_enableLogger) {
        if (_logFilePath.Length > 0) {
          Directory.CreateDirectory(_logFilePath);
        }
        var tsSuffix = DateTime.Now.ToString(_logTsFormat);
        if (_writeInfoFile) {
          _infoLogWriter = new StreamWriter(Path.Combine(
              _logFilePath, $"{_logFilePrefix}.{tsSuffix}.INFO.txt"));
        }
        if (_writeWarningFile) {
          _warningLogWriter = new StreamWriter(Path.Combine(
              _logFilePath, $"{_logFilePrefix}.{tsSuffix}.WARNING.txt"));
        }
        if (_writeErrorFile) {
          _errorLogWriter = new StreamWriter(Path.Combine(
              _logFilePath, $"{_logFilePrefix}.{tsSuffix}.ERROR.txt"));
        }
      }
      _writeLogsToDisk = _infoLogWriter != null || _warningLogWriter != null || _errorLogWriter != null;
    } catch (Exception ex) {
      _writeLogsToDisk = false;  // Must be the first statement in the catch section!
      Debug.LogException(ex);
      Debug.LogError("Not enabling logging to disk due to errors");
    }
  }

  /// <summary>Flushes and closes all opened log files.</summary>
  void StopLogFiles() {
    try {
      if (_infoLogWriter != null) {
        _infoLogWriter.Close();
      }
      if (_warningLogWriter != null) {
        _warningLogWriter.Close();
      }
      if (_errorLogWriter != null) {
        _errorLogWriter.Close();
      }
    } catch (Exception ex) {
      Debug.LogException(ex);
    }
    _infoLogWriter = null;
    _warningLogWriter = null;
    _errorLogWriter = null;
    _writeLogsToDisk = false;
  }

  /// <summary>Drops extra log files.</summary>
  void CleanupLogFiles() {
    if (_totalFiles < 0 && _totalSizeMb < 0 && _maxAgeHours < 0) {
      return;
    }
    var logFiles = Directory.GetFiles(_logFilePath, _logFilePrefix + ".*")
        .Select(x => new FileInfo(x))
        .OrderBy(x => x.CreationTimeUtc)
        .ToArray();
    if (logFiles.Length == 0) {
      Debug.Log("No log files found. Nothing to do");
      return;
    }
    var limitTotalSize = logFiles.Sum(x => x.Length);
    var limitExtraFiles = logFiles.Count() - _totalFiles;
    var limitAge = DateTime.UtcNow.AddHours(-_maxAgeHours);
    Debug.LogFormat("Found persistent logs: totalFiles={0}, totalSize={1}, oldestDate={2}",
                    logFiles.Count(), limitTotalSize, logFiles.Min(x => x.CreationTimeUtc));
    for (var i = 0; i < logFiles.Count(); i++) {
      var fieldInfo = logFiles[i];
      if (_totalFiles > 0 && limitExtraFiles > 0) {
        Debug.LogFormat("Drop log file due to too many log files exist: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      } else if (_totalSizeMb > 0 && limitTotalSize > _totalSizeMb * 1024 * 1024) {
        Debug.LogFormat("Drop log file due to too large total size: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      } else if (_maxAgeHours > 0 && fieldInfo.CreationTimeUtc < limitAge) {
        Debug.LogFormat("Drop log file due to it's too old: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      }
      --limitExtraFiles;
      limitTotalSize -= fieldInfo.Length;
    }
  }
}

/// <summary>A helper class to periodically flush logs to disk.</summary>
/// <remarks>Also, does flush on scene change or game exit.</remarks>
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "ConvertToConstant.Global")]
sealed class PersistentLogAggregatorFlusher : MonoBehaviour {
  /// <summary>A list of persistent aggregators that need state flushing.</summary>
  public static HashSet<PersistentLogAggregator> ActiveAggregators = new HashSet<PersistentLogAggregator>();

  /// <summary>A delay between flushes.</summary>
  public static float PersistentLogsFlushPeriod = 0.2f;  // Seconds.

  void Awake() {
    StartCoroutine(FlushLogsCoroutine());
  }

  void OnDestroy() {
    FlushAllAggregators();
  }

  /// <summary>Flushes all registered persistent aggregators.</summary>
  static void FlushAllAggregators() {
    var aggregators = ActiveAggregators.ToArray();
    foreach (var aggregator in aggregators) {
      aggregator.FlushBufferedLogs();
    }
  }

  /// <summary>Flushes logs to disk periodically.</summary>
  /// <remarks>This method never returns.</remarks>
  /// <returns>Delay till next flush.</returns>
  // ReSharper disable once MemberCanBeMadeStatic.Local
  IEnumerator FlushLogsCoroutine() {
    while (true) {
      yield return new WaitForSeconds(PersistentLogsFlushPeriod);
      FlushAllAggregators();
    }
    // ReSharper disable once IteratorNeverReturns
  }
}

} // namespace KSPDev
