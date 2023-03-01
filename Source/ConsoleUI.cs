// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using KSP2Dev.GUIUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using KSP2Dev.ConfigUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

/// <summary>A console to display Unity's debug logs in-game.</summary>
[PersistentFieldsFileAttribute("KSP2Dev_LogConsole/settings.json", "UI")]
[PersistentFieldsFileAttribute("KSP2Dev_LogConsole/session.json", "UI", StdPersistentGroups.SessionGroup)]
[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
sealed class ConsoleUI : MonoBehaviour {
  #region Session settings
  [PersistentField("showInfo", Group = StdPersistentGroups.SessionGroup)]
  static bool _showInfo;

  [PersistentField("showWarning", Group = StdPersistentGroups.SessionGroup)]
  static bool _showWarning = true;

  [PersistentField("showErrors", Group = StdPersistentGroups.SessionGroup)]
  static bool _showError = true;

  [PersistentField("showExceptions", Group = StdPersistentGroups.SessionGroup)]
  static bool _showException = true;

  [PersistentField("logMode", Group = StdPersistentGroups.SessionGroup)]
  static ShowMode _logShowMode = ShowMode.Smart;

  [PersistentField("quickFilter", Group = StdPersistentGroups.SessionGroup)]
  static string _quickFilterStr = "";
  #endregion  

  #region Mod's settings
  [PersistentField("consoleToggleKey")]
  static KeyCode _toggleKey = KeyCode.BackQuote;

  [PersistentField("ColorSchema/infoLog")]
  static Color _infoLogColor = Color.white;
  
  [PersistentField("ColorSchema/warningLog")]
  static Color _warningLogColor = Color.yellow;

  [PersistentField("ColorSchema/errorLog")]
  static Color _errorLogColor = Color.red;

  [PersistentField("ColorSchema/exceptionLog")]
  static Color _exceptionLogColor = Color.magenta;
  #endregion

  #region UI constants
  /// <summary>Console window margin on the screen.</summary>
  const int Margin = 20;

  /// <summary>For every UI window Unity needs a unique ID. This is the one.</summary>
  const int WindowId = 19450509;

  /// <summary>Actual screen position of the console window.</summary>
  static Rect _windowRect = new Rect(Margin, Margin, Screen.width - (Margin * 2), Screen.height - (Margin * 2));

  /// <summary>A title bar location.</summary>
  static Rect _titleBarRect = new Rect(0, 0, 10000, 20);

  /// <summary>Style to draw a control of the minimum size.</summary>
  static readonly GUILayoutOption MinSizeLayout = GUILayout.ExpandWidth(false);

  /// <summary>Mode names.</summary>
  static readonly string[] LOGShowingModes = { "Raw", "Collapsed", "Smart" };

  /// <summary>Box style ot use to present a single record.</summary>
  /// <remarks>It's re-populated on each GUI update call. See <see cref="OnGUI"/>.</remarks>
  GUIStyle _logRecordStyle;
  #endregion

  /// <summary>Display mode constants. Must match <see cref="ConsoleUI.LOGShowingModes"/>.</summary>
  enum ShowMode {
    /// <summary>Simple list of log records.</summary>
    Raw = 0,
    /// <summary>List where identical consecutive records are grouped.</summary>
    Collapsed = 1,
    /// <summary>
    /// List where identical records are grouped globally. If group get updated with a new log record then its timestamp
    /// is updated.
    /// </summary>
    Smart = 2
  }
  
  /// <summary>Log scroll box position.</summary>
  static Vector2 _scrollPosition;

  /// <summary>Specifies if the debug console is visible.</summary>
  static bool _isConsoleVisible;

  /// <summary>ID of the currently selected log record.</summary>
  /// <remarks>It shows expanded.</remarks>
  static int _selectedLogRecordId = -1;

  /// <summary>Indicates that the visible log records should be queried from a
  /// <see cref="_snapshotLogAggregator"/>.</summary>
  static bool _logUpdateIsPaused;

  /// <summary>Indicates that the logs from the current aggregator need to be re-queried.</summary>
  static bool _logsViewChanged;

  #region Log aggregators
  /// <summary>A logger that keeps records on th disk.</summary>
  static PersistentLogAggregator _diskLogAggregator = new PersistentLogAggregator();
  /// <summary>A logger to show when <see cref="ShowMode.Raw"/> is selected.</summary>
  static PlainLogAggregator _rawLogAggregator = new PlainLogAggregator();
  /// <summary>A logger to show when <see cref="ShowMode.Collapsed"/> is selected.</summary>
  static CollapseLogAggregator _collapseLogAggregator = new CollapseLogAggregator();
  /// <summary>A logger to show when <see cref="ShowMode.Smart"/> is selected.</summary>
  static SmartLogAggregator _smartLogAggregator = new SmartLogAggregator();
  /// <summary>A logger to show a static snapshot.</summary>
  static SnapshotLogAggregator _snapshotLogAggregator = new SnapshotLogAggregator();
  #endregion

  /// <summary>A snapshot of the logs for the current view.</summary>
  static IEnumerable<LogRecord> _logsToShow = Array.Empty<LogRecord>();

  /// <summary>Number of the INFO records in the <see cref="_logsToShow"/> collection.</summary>
  static int _infoLogs;
  /// <summary>Number of the WARNING records in the <see cref="_logsToShow"/> collection.</summary>
  static int _warningLogs;
  /// <summary>Number of the ERROR records in the <see cref="_logsToShow"/> collection.</summary>
  static int _errorLogs;
  /// <summary>Number of the EXCEPTION records in the <see cref="_logsToShow"/> collection.</summary>
  static int _exceptionLogs;

  /// <summary>A list of actions to apply at the end of the GUI frame.</summary>
  static readonly GuiActionsList GuiActions = new GuiActionsList();

  /// <summary>Tells if the controls should be shown at the bottom of the dialog.</summary>
  bool _isToolbarAtTheBottom = true;

  #region Quick filter fields
  /// <summary>Tells if the quick filter editing is active.</summary>
  /// <remarks>Log console update is frozen until the mode is ended.</remarks>
  static bool _quickFilterInputEnabled;

  /// <summary>Tells the last known quick filter status.</summary>
  /// <remarks>It's updated in every <c>OnGUI</c> call. Used to detect the mode change.</remarks>
  static bool _oldQuickFilterInputEnabled;

  /// <summary>The old value of the quick filter before the edit mode has started.</summary>
  static string _oldQuickFilterStr;

  /// <summary>The size for the quick filter input field.</summary>
  static readonly GUILayoutOption QuickFilterSizeLayout = GUILayout.Width(100);
  #endregion

  #region Session persistence
  /// <summary>Only loads the session settings.</summary>
  void Awake() {
    DontDestroyOnLoad(gameObject);

    // Read the configs for all the aggregators.
    ConfigAccessor.ReadFieldsInType(typeof(LogInterceptor), null /* instance */);
    ConfigAccessor.ReadFieldsInType(typeof(LogFilter), null /* instance */);
    ConfigAccessor.ReadFieldsInType(_diskLogAggregator.GetType(), _diskLogAggregator);
    ConfigAccessor.ReadFieldsInType(_rawLogAggregator.GetType(), _rawLogAggregator);
    ConfigAccessor.ReadFieldsInType(_collapseLogAggregator.GetType(), _collapseLogAggregator);
    ConfigAccessor.ReadFieldsInType(_smartLogAggregator.GetType(), _smartLogAggregator);

    // Start all aggregators.
    _rawLogAggregator.StartCapture();
    _collapseLogAggregator.StartCapture();
    _smartLogAggregator.StartCapture();
    _diskLogAggregator.StartCapture();
    LogInterceptor.StartIntercepting();

    // Load UI configs.
    ConfigAccessor.ReadFieldsInType(GetType(), null /* instance */);
    ConfigAccessor.ReadFieldsInType(GetType(), this, group: StdPersistentGroups.SessionGroup);
  }
  
  /// <summary>Only stores the session settings.</summary>
  void OnDestroy() {
    ConfigAccessor.WriteFieldsFromType(typeof(ConsoleUI), this, group: StdPersistentGroups.SessionGroup);
  }
  #endregion

  /// <summary>Actually renders the console window.</summary>
  void OnGUI() {
    // Init skin styles.
    _logRecordStyle = new GUIStyle(GUI.skin.box) {
        alignment = TextAnchor.MiddleLeft,
    };

    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == _toggleKey) {
      _isConsoleVisible = !_isConsoleVisible;
      Event.current.Use();
    }
    if (_isConsoleVisible) {
      var title = "KSPDev Logs Console";
      if (!string.IsNullOrEmpty(_quickFilterStr)) {
        title += " (filter: <i>" + _quickFilterStr + "</i>)";
      }
      if (_logUpdateIsPaused) {
        title += " <i>(PAUSED)</i>";
      }
      _windowRect = GUILayout.Window(WindowId, _windowRect, ConsoleWindowFunc, title);
    }
  }

  /// <summary>Shows a window that displays the recorded logs.</summary>
  /// <param name="windowId">Window ID.</param>
  void ConsoleWindowFunc(int windowId) {
    // Only show the logs snapshot when it's safe to change the GUI layout.
    if (GuiActions.ExecutePendingGuiActions()) {
      UpdateLogsView();
      // Check if the toolbar goes out of the screen.
      _isToolbarAtTheBottom = _windowRect.yMax < Screen.height;
    }

    if (!_isToolbarAtTheBottom) {
      GUICreateToolbar();
    }

    // Main scrolling view.
    using (var logsScrollView = new GUILayout.ScrollViewScope(_scrollPosition)) {
      _scrollPosition = logsScrollView.scrollPosition;

      // Report conditions.
      if (!LogInterceptor.IsStarted) {
        using (new GuiColorScope(contentColor: _errorLogColor)) {
          GUILayout.Label("KSPDev is not handling system logs. Open standard in-game debug console"
                          + " to see the current logs");
        }
      }
      if (_quickFilterInputEnabled) {
        using (new GuiColorScope(contentColor: Color.gray)) {
          GUILayout.Label("<i>Logs update is PAUSED due to the quick filter editing is active."
                          + " Hit ENTER to accept the filter, or ESC to discard.</i>");
        }
      }

      GUIShowLogRecords();
    }

    if (_isToolbarAtTheBottom) {
      GUICreateToolbar();
    }

    // Allow the window to be dragged by its title bar.
    GuiWindow.DragWindow(ref _windowRect, _titleBarRect);
  }

  /// <summary>Shows the records from the the currently selected aggregator.</summary>
  void GUIShowLogRecords() {
    var capturedRecords = _logsToShow.Where(LogLevelFilter).ToList();
    var showRecords = capturedRecords.Where(LogQuickFilter).ToList();

    // Warn if there are now records to show.
    if (!_quickFilterInputEnabled && !showRecords.Any()) {
      var msg = "No records available for the selected levels";
      if (capturedRecords.Any()) {
        msg += " and quick filter \"" + _quickFilterStr + "\"";
      }
      using (new GuiColorScope(contentColor: Color.gray)) {
        GUILayout.Label(msg);
      }
    }

    // Dump the records.
    foreach (var log in showRecords) {
      using (new GuiColorScope(contentColor: GetLogTypeColor(log.SrcLog.Type))) {
        var recordMsg = log.MakeTitle() + (_selectedLogRecordId == log.SrcLog.Id ? ":\n" + log.SrcLog.StackTrace : "");
        GUILayout.Box(recordMsg, _logRecordStyle);

        // Check if log record is selected.
        if (Event.current.type == EventType.MouseDown) {
          if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) {
            // Toggle selection.
            var newSelectedId = _selectedLogRecordId == log.SrcLog.Id ? -1 : log.SrcLog.Id;
            GuiActions.Add(() => GuiActionSelectLog(newSelectedId));
          }
        }
      }

      // Present log record details when it's selected.
      if (_selectedLogRecordId == log.SrcLog.Id && log.SrcLog.Source.Any()) {
        GUICreateLogRecordControls(log);
      }
    }
  }

  /// <summary>Displays log records details and creates the relevant controls.</summary>
  /// <param name="log">The selected log record.</param>
  static void GUICreateLogRecordControls(LogRecord log) {
    using (new GUILayout.HorizontalScope()) {
      // Add stack trace utils.
      using (new GuiEnabledStateScope(!log.SrcLog.FilenamesResolved)) {
        if (GUILayout.Button("Resolve file names", MinSizeLayout)) {
          log.ResolveStackFilenames();
        }
      }

      // Add source and filter controls when expanded.
      GUILayout.Label("Silence: source", MinSizeLayout);
      if (GUILayout.Button(log.SrcLog.Source, MinSizeLayout)) {
        GuiActions.Add(() => GuiActionAddSilence(log.SrcLog.Source, isPrefix: false));
      }
      var sourceParts = log.SrcLog.Source.Split('.');
      if (sourceParts.Length > 1) {
        GUILayout.Label("or by prefix", MinSizeLayout);
        for (var i = sourceParts.Length - 1; i > 0; --i) {
          var prefix = string.Join(".", sourceParts.Take(i).ToArray()) + '.';
          if (GUILayout.Button(prefix, MinSizeLayout)) {
            GuiActions.Add(() => GuiActionAddSilence(prefix, isPrefix: true));
          }
        }
      }
    }
  }

  /// <summary>Creates controls for the console.</summary>
  void GUICreateToolbar() {
    using (new GUILayout.HorizontalScope()) {
      // Window size/snap.
      if (GUILayout.Button("\u21d5", MinSizeLayout)) {
        _windowRect = new Rect(Margin, Margin, Screen.width - Margin * 2, Screen.height - Margin * 2);
      }
      if (GUILayout.Button("\u21d1", MinSizeLayout)) {
        _windowRect = new Rect(Margin, Margin, Screen.width - Margin * 2, (Screen.height - Margin * 2.0f) / 3);
      }
      if (GUILayout.Button("\u21d3", MinSizeLayout)) {
        var clientHeight = (Screen.height - 2.0f * Margin) / 3;
        _windowRect = new Rect(Margin, Screen.height - Margin - clientHeight, Screen.width - Margin * 2, clientHeight);
      }

      // Quick filter.
      // Due to Unity GUI behavior, any change to the layout resets the text field focus. We do some// tricks here to
      // set initial focus to the field but not locking it permanently.
      GUILayout.Label("Quick filter:", MinSizeLayout);
      if (_quickFilterInputEnabled) {
        GUI.SetNextControlName("quickFilter");
        _quickFilterStr = GUILayout.TextField(_quickFilterStr, QuickFilterSizeLayout);
        if (Event.current.type == EventType.KeyUp) {
          if (Event.current.keyCode == KeyCode.Return) {
            GuiActions.Add(GuiActionAcceptQuickFilter);
          } else if (Event.current.keyCode == KeyCode.Escape) {
            GuiActions.Add(GuiActionCancelQuickFilter);
          }
        } else if (Event.current.type == EventType.Layout && GUI.GetNameOfFocusedControl() != "quickFilter") {
          if (_oldQuickFilterInputEnabled != _quickFilterInputEnabled && !_oldQuickFilterInputEnabled) {
            GUI.FocusControl("quickFilter");  // Initial set of the focus.
          } else {
            GuiActions.Add(GuiActionCancelQuickFilter);  // The field has lost the focus.
          }
        }  
      } else {
        var title = _quickFilterStr == "" ? "<i>NONE</i>" : _quickFilterStr;
        if (GUILayout.Button(title, QuickFilterSizeLayout)) {
          GuiActions.Add(GuiActionStartQuickFilter);
        }
      }
      _oldQuickFilterInputEnabled = _quickFilterInputEnabled;

      using (new GuiEnabledStateScope(!_quickFilterInputEnabled)) {
        // Clear logs in the current aggregator.
        if (GUILayout.Button("Clear")) {
          GuiActions.Add(GuiActionClearLogs);
        }

        // Log mode selection. 
        GUI.changed = false;
        var showMode = GUILayout.SelectionGrid(
            (int) _logShowMode, LOGShowingModes, LOGShowingModes.Length, MinSizeLayout);
        _logsViewChanged |= GUI.changed;
        if (GUI.changed) {
          GuiActions.Add(() => GuiActionSetMode((ShowMode) showMode));
        }

        // Paused state selection.
        GUI.changed = false;
        var isPaused = GUILayout.Toggle(_logUpdateIsPaused, "PAUSED", MinSizeLayout);
        if (GUI.changed) {
          GuiActions.Add(() => GuiActionSetPaused(isPaused));
        }
        
        // Draw logs filter by level and refresh logs when filter changes.
        GUI.changed = false;
        using (new GuiColorScope()) {
          GUI.contentColor = _infoLogColor;
          _showInfo = GUILayout.Toggle(_showInfo, $"INFO ({_infoLogs})", MinSizeLayout);
          GUI.contentColor = _warningLogColor;
          _showWarning = GUILayout.Toggle(_showWarning, $"WARNING ({_warningLogs})", MinSizeLayout);
          GUI.contentColor = _errorLogColor;
          _showError = GUILayout.Toggle(_showError, $"ERROR ({_errorLogs})", MinSizeLayout);
          GUI.contentColor = _exceptionLogColor;
          _showException = GUILayout.Toggle(_showException, $"EXCEPTION ({_exceptionLogs})", MinSizeLayout);
        }
        _logsViewChanged |= GUI.changed;
      }
    }
  }

  /// <summary>Verifies if level of the log record is needed by the UI.</summary>
  /// <param name="log">The log record to verify.</param>
  /// <returns><c>true</c> if this level is visible.</returns>
  static bool LogLevelFilter(LogRecord log) {
    return log.SrcLog.Type == LogType.Exception && _showException
        || log.SrcLog.Type == LogType.Error && _showError
        || log.SrcLog.Type == LogType.Warning && _showWarning
        || log.SrcLog.Type == LogType.Log && _showInfo;
  }

  /// <summary>Gives a color for the requested log type.</summary>
  /// <param name="type">A log type to get color for.</param>
  /// <returns>A color for the type.</returns>
  static Color GetLogTypeColor(LogType type) {
    switch (type) {
      case LogType.Log: return _infoLogColor;
      case LogType.Warning: return _warningLogColor;
      case LogType.Error: return _errorLogColor;
      case LogType.Exception: return _exceptionLogColor;
    }
    return Color.gray;
  }

  /// <summary>Verifies if the log record matches the quick filter criteria.</summary>
  /// <remarks>The quick filter string is a case-insensitive prefix of the log's source.</remarks>
  /// <param name="log">The log record to verify.</param>
  /// <returns><c>true</c> if this log passes the quick filter check.</returns>
  static bool LogQuickFilter(LogRecord log) {
    var filter = _quickFilterInputEnabled ? _oldQuickFilterStr : _quickFilterStr;
    return log.SrcLog.Source.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Populates <see cref="_logsToShow"/> and the stats numbers.</summary>
  /// <remarks>
  /// The current aggregator is determined from <see cref="_logShowMode"/> and <see cref="_logUpdateIsPaused"/> state.
  /// </remarks>
  static void UpdateLogsView() {
    var currentAggregator = _logUpdateIsPaused ? _snapshotLogAggregator : GetCurrentAggregator();
    if (currentAggregator.FlushBufferedLogs() || _logsViewChanged) {
      _logsToShow = currentAggregator.GetLogRecords();
      _infoLogs = currentAggregator.InfoLogsCount;
      _warningLogs = currentAggregator.WarningLogsCount;
      _errorLogs = currentAggregator.ErrorLogsCount;
      _exceptionLogs = currentAggregator.ExceptionLogsCount;
    }
    _logsViewChanged = false;
  }
  
  /// <summary>Returns an aggregator for the currently selected mode.</summary>
  /// <returns>An aggregator.</returns>
  static BaseLogAggregator GetCurrentAggregator() {
    BaseLogAggregator currentAggregator;
    switch (_logShowMode) {
      case ShowMode.Raw:
        currentAggregator = _rawLogAggregator;
        break;
      case ShowMode.Collapsed:
        currentAggregator = _collapseLogAggregator;
        break;
      case ShowMode.Smart:
        currentAggregator = _smartLogAggregator;
        break;
      default:
        currentAggregator = _rawLogAggregator;
        break;
    }
    return currentAggregator;
  }

  #region GUI action handlers
  void GuiActionSetPaused(bool isPaused) {
    if (isPaused == _logUpdateIsPaused) {
      return;  // Prevent refreshing of the snapshot if the mode hasn't changed.
    }
    if (isPaused) {
      _snapshotLogAggregator.LoadLogs(GetCurrentAggregator());
    }
    _logUpdateIsPaused = isPaused;
    _logsViewChanged = true;
  }

  void GuiActionCancelQuickFilter() {
    if (_quickFilterInputEnabled) {
      _quickFilterInputEnabled = false;
      _quickFilterStr = _oldQuickFilterStr;
      _oldQuickFilterStr = null;
      GuiActionSetPaused(false);
    }
  }

  void GuiActionAcceptQuickFilter() {
    _quickFilterInputEnabled = false;
    _oldQuickFilterStr = null;
    GuiActionSetPaused(false);
  }

  void GuiActionStartQuickFilter() {
    _quickFilterInputEnabled = true;
    _oldQuickFilterStr = _quickFilterStr;
    GuiActionSetPaused(true);
  }

  void GuiActionClearLogs() {
    GuiActionSetPaused(false);
    GetCurrentAggregator().ClearAllLogs();
    _logsViewChanged = true;
  }

  static void GuiActionSelectLog(int newSelectedId) {
    _selectedLogRecordId = newSelectedId;
  }

  static void GuiActionAddSilence(string pattern, bool isPrefix) {
    if (isPrefix) {
      LogFilter.AddSilenceByPrefix(pattern);
    } else {
      LogFilter.AddSilenceBySource(pattern);
    }
    ConfigAccessor.WriteFieldsFromType(typeof(LogFilter), null /* instance */);

    _rawLogAggregator.UpdateFilter();
    _collapseLogAggregator.UpdateFilter();
    _smartLogAggregator.UpdateFilter();
    _snapshotLogAggregator.UpdateFilter();
    _logsViewChanged = true;
  }
  
  void GuiActionSetMode(ShowMode mode) {
    _logShowMode = mode;
    GuiActionSetPaused(false);  // New mode invalidates the snapshot.
    _logsViewChanged = true;
  }
  #endregion
}

} // namespace KSPDev
