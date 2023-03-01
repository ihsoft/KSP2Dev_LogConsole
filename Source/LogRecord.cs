// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using UnityEngine;
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>A wrapper class to hold log record(s).</summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class LogRecord {
  // Log text generation constants.
  const string InfoPrefix = "INFO";
  const string WarningPrefix = "WARNING";
  const string ErrorPrefix = "ERROR";
  const string ExceptionPrefix = "EXCEPTION";
  const string RepeatedPrefix = "REPEATED:";
  
  /// <summary>Format of a timestamp in the logs.</summary>
  const string TimestampFormat = "yyMMdd\\THHmmss.fff";

  /// <summary>A maximum size of title of a regular log record.</summary>
  /// <remarks>
  /// Used to reserve memory when building log text. Too big value will waste memory and too small value may impact
  /// performance. Keep it reasonable.
  /// </remarks>
  const int TitleMaxSize = 200;

  /// <summary>An original Unity log record.</summary>  
  public readonly LogInterceptor.Log SrcLog;

  /// <summary>A unique ID of the log.</summary>
  /// <remarks>Don't use it for ordering since it's not defined how this ID is generated.</remarks>
  public int LastId { get; private set; }

  /// <summary>Timestamp of the log in local world (non-game) time.</summary>
  public DateTime Timestamp { get; private set; }

  /// <summary>Number of logs merged into this record so far.</summary>   
  int _mergedLogs = 1;
  
  /// <summary>A lazy cache for the log hash code.</summary>
  int? _similarityHash;
  
  /// <summary>A generic wrapper for Unity log records.</summary>
  /// <param name="log">A Unity log record.</param>
  public LogRecord(LogInterceptor.Log log) {
    SrcLog = log;
    LastId = log.Id;
    Timestamp = log.Timestamp;
  }

  /// <summary>Makes a copy of the existing LogRecord.</summary>
  public LogRecord(LogRecord logRecord) {
    SrcLog = logRecord.SrcLog;
    LastId = logRecord.LastId;
    Timestamp = logRecord.Timestamp;
    _mergedLogs = logRecord._mergedLogs;
    _similarityHash = logRecord._similarityHash;
  }

  /// <summary>Returns a hash code that is identical for the *similar* log records.</summary>
  /// <remarks>This method is supposed to be called very frequently so, caching the code is a good idea.</remarks>
  /// <returns>A hash code of the *similar* fields.</returns>
  public int GetSimilarityHash() {
    if (!_similarityHash.HasValue) {
      _similarityHash = (SrcLog.Source + SrcLog.Type + SrcLog.Message + SrcLog.StackTrace).GetHashCode();
    }
    return _similarityHash.Value;
  }

  /// <summary>Merges repeated log into an existing record.</summary>
  /// <remarks>Only does merging of ID and the timestamp. caller is responsible for updating other fields.</remarks>
  /// <param name="log">A log record to merge. This is a readonly parameter!</param>
  public void MergeRepeated(LogRecord log) {
    LastId = log.SrcLog.Id;
    // Math.Max() won't work for DateTime.
    Timestamp = log.Timestamp > Timestamp ? log.Timestamp : Timestamp;
    ++_mergedLogs;
  }

  /// <summary>Gives log's timestamp in a unified <see cref="TimestampFormat"/>.</summary>
  /// <returns>A human readable timestamp string.</returns>
  public string FormatTimestamp() {
    return Timestamp.ToString(TimestampFormat);
  }

  /// <summary>Returns a text form of the log.</summary>
  /// <remarks>Not supposed to have stack trace.</remarks>
  /// <returns>A string that describes the event.</returns>
  public string MakeTitle() {
    var titleBuilder = new StringBuilder(TitleMaxSize);
    titleBuilder.Append(FormatTimestamp()).Append(" [");
    // Not using a dict lookup to save performance.
    switch (SrcLog.Type) {
      case LogType.Log:
        titleBuilder.Append(InfoPrefix);
        break;
      case LogType.Warning:
        titleBuilder.Append(WarningPrefix);
        break;
      case LogType.Error:
        titleBuilder.Append(ErrorPrefix);
        break;
      case LogType.Exception:
        titleBuilder.Append(ExceptionPrefix);
        break;
      default:
        titleBuilder.Append(SrcLog.Type);
        break;
    }
    titleBuilder.Append("] ");
    if (_mergedLogs > 1) {
      titleBuilder.Append('[').Append(RepeatedPrefix).Append(_mergedLogs).Append("] ");
    }
    if (SrcLog.Source.Length > 0) {
      titleBuilder.Append('[').Append(SrcLog.Source).Append("] ");
    }
    titleBuilder.Append(SrcLog.Message);
    return titleBuilder.ToString();
  }

  /// <summary>Resolves the file paths on the stack trace records.</summary>
  /// <remarks>This method is not performance efficient.</remarks>
  public void ResolveStackFilenames() {
    if (SrcLog.FilenamesResolved) {
      return;  // Nothing to do.
    }
    var lines = SrcLog.StackTrace.Split('\n');
    if (SrcLog.StackFrames == null || lines.Length != SrcLog.StackFrames.Length) {
      SrcLog.FilenamesResolved = true;  // Cannot resolve.
      return;
    }
    var gameRoot = Path.GetFullPath(new Uri(KSPUtil.ApplicationRootPath).LocalPath);
    var matches = new List<string>();
    for (var i = 0; i < lines.Length; i++) {
      var assembly = SrcLog.StackFrames[i].GetMethod().DeclaringType?.Assembly;
      var relativePath = new Uri(gameRoot)
          .MakeRelativeUri(new Uri(assembly?.Location ?? ""))
          .ToString()
          .Replace(Path.DirectorySeparatorChar, '/');
      matches.Add($"{lines[i]} in {relativePath} [v{assembly?.GetName().Version}]");
    }
    SrcLog.StackTrace = string.Join("\n", matches.ToArray());
    SrcLog.FilenamesResolved = true;
  }
}

} // namespace KSPDev
