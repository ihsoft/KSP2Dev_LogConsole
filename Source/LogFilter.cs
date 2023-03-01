// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using KSP2Dev.ConfigUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>Keeps and controls filters to apply to the incoming logs.</summary>
[PersistentFieldsFile("KSP2Dev_LogConsole/settings.json", "LogFilter")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Global")]
static class LogFilter {
  /// <summary>Sources that starts from any of the strings in the filter will be ignored.</summary>
  /// <remarks>
  /// Walking through this filter requires full scan (in a worst case) so, it should be of a reasonable size.
  /// </remarks>
  [PersistentField("PrefixMatchFilter")]
  public static List<string> PrefixFilter = new List<string>();
  
  /// <summary>Sources that exactly matches the filter will be ignored.</summary>
  [PersistentField("ExactMatchFilter")]
  public static HashSet<string> ExactFilter = new HashSet<string>();

  /// <summary>Adds a new filter by exact match of the source.</summary>
  public static void AddSilenceBySource(string source) {
    if (!ExactFilter.Contains(source)) {
      ExactFilter.Add(source);
      Debug.LogWarningFormat("Added exact match silence: {0}", source);
    }
  }

  /// <summary>Adds a new filter by prefix match of the source.</summary>
  public static void AddSilenceByPrefix(string prefix) {
    if (!PrefixFilter.Contains(prefix)) {
      PrefixFilter.Add(prefix);
      Debug.LogWarningFormat("Added prefix match silence: {0}", prefix);
    }
  }
  
  /// <summary>Verifies if <paramref name="log"/> matches the filters.</summary>
  /// <param name="log">A log record to check.</param>
  /// <returns><c>true</c> if any of the filters matched.</returns>
  public static bool CheckLogForFilter(LogInterceptor.Log log) {
    return ExactFilter.Contains(log.Source) || PrefixFilter.Any(log.Source.StartsWith);
  }
}

} // namespace KSPDev
