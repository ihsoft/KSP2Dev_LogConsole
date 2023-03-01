// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using BepInEx;

// ReSharper disable once CheckNamespace
namespace KSP2Dev.LogConsole {

[BepInPlugin("0747F7F6-E0DE-44E2-B818-F7ED638A5570", "KSP2Dev_Utils - LogConsole", "1.0")]
public class Plugin : BaseUnityPlugin {
  void Awake() {
    Logger.LogInfo($"Plugin is loaded!");
    gameObject.AddComponent<PersistentLogAggregatorFlusher>();
    gameObject.AddComponent<ConsoleUI>();
  }
  
  void OnDestroy() {
    Logger.LogInfo($"Plugin is unloaded!");
  }
}

}
