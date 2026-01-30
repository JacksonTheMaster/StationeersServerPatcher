using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class StationeersServerPatcher : BaseUnityPlugin
    {
        private const string PluginGUID = "com.jacksonthemaster.StationeersServerPatcher";
        private const string PluginName = "StationeersServerPatcher";
        private const string PluginVersion = "1.2.0";
        private const string LogPrefix = "[StationeersServerPatcher] ";

        private Harmony harmony;

        private void Awake()
        {
            LogInfo("Plugin initializing...");

            try
            {
                harmony = new Harmony(PluginGUID);
                harmony.PatchAll();
                LogInfo("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Failed to apply Harmony patches: {ex.Message}");
            }

            LogInfo("Initialization complete.");
        }

        public static void LogInfo(string message) => Debug.Log(LogPrefix + message);
        public static void LogWarning(string message) => Debug.LogWarning(LogPrefix + message);
        public static void LogError(string message) => Debug.LogError(LogPrefix + message);
    }

    /// <summary>
    /// Helper class for managing pause state and autosave timer via reflection
    /// </summary>
    public static class ServerPauseHelper
    {
        private static MethodInfo _pauseEventMethod;
        private static PropertyInfo _isPausedProperty;
        private static Type _networkBaseType;
        private static System.Timers.Timer _timerCache;
        private static MethodInfo _resetAutoSaveMethod;

        private static Type NetworkBaseType
        {
            get
            {
                if (_networkBaseType == null)
                    _networkBaseType = AccessTools.TypeByName("NetworkBase");
                return _networkBaseType;
            }
        }

        /// <summary>
        /// Calls NetworkBase.PauseEvent(pause) to properly set both NetworkBase.IsPaused 
        /// and WorldManager.IsGamePaused, and notify clients.
        /// </summary>
        public static void SetPauseState(bool pause)
        {
            try
            {
                if (_pauseEventMethod == null)
                {
                    _pauseEventMethod = AccessTools.Method(NetworkBaseType, "PauseEvent");
                    if (_pauseEventMethod == null)
                    {
                        StationeersServerPatcher.LogError("Could not find PauseEvent method.");
                        return;
                    }
                }

                _pauseEventMethod.Invoke(null, new object[] { pause });
                StationeersServerPatcher.LogInfo($"Called PauseEvent({pause}) - game is now {(pause ? "paused" : "resumed")}.");
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error calling PauseEvent: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current value of NetworkBase.IsPaused
        /// </summary>
        public static bool IsPaused
        {
            get
            {
                try
                {
                    if (_isPausedProperty == null)
                        _isPausedProperty = AccessTools.Property(NetworkBaseType, "IsPaused");
                    return (bool)(_isPausedProperty?.GetValue(null) ?? false);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if AutoPauseServer setting is enabled
        /// </summary>
        public static bool IsAutoPauseEnabled
        {
            get
            {
                try
                {
                    var settingsType = AccessTools.TypeByName("Assets.Scripts.Serialization.Settings");
                    var currentDataField = AccessTools.Field(settingsType, "CurrentData");
                    var currentData = currentDataField?.GetValue(null);
                    if (currentData == null) return false;

                    var autoPauseField = AccessTools.Field(currentData.GetType(), "AutoPauseServer");
                    return (bool)(autoPauseField?.GetValue(currentData) ?? false);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if running in batch/dedicated server mode
        /// </summary>
        public static bool IsBatchMode
        {
            get
            {
                try
                {
                    var gameManagerType = AccessTools.TypeByName("Assets.Scripts.GameManager");
                    var isBatchModeProperty = AccessTools.Property(gameManagerType, "IsBatchMode");
                    return (bool)(isBatchModeProperty?.GetValue(null) ?? false);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the client count from NetworkBase.Clients
        /// </summary>
        public static int GetClientCount()
        {
            try
            {
                var clientsField = AccessTools.Field(NetworkBaseType, "Clients");
                var clients = clientsField?.GetValue(null);
                if (clients == null) return -1;

                var countProperty = AccessTools.Property(clients.GetType(), "Count");
                return (int)(countProperty?.GetValue(clients) ?? -1);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Gets the StationAutoSave timer via reflection
        /// </summary>
        public static System.Timers.Timer GetAutoSaveTimer()
        {
            if (_timerCache != null)
                return _timerCache;

            var stationAutoSaveType = AccessTools.TypeByName("Assets.Scripts.Serialization.StationAutoSave");
            if (stationAutoSaveType == null)
            {
                StationeersServerPatcher.LogError("Could not find StationAutoSave type.");
                return null;
            }

            var timerField = AccessTools.Field(stationAutoSaveType, "_autoSaveTimer");
            _timerCache = timerField?.GetValue(null) as System.Timers.Timer;
            return _timerCache;
        }

        /// <summary>
        /// Stops the autosave timer
        /// </summary>
        public static void StopAutoSaveTimer()
        {
            var timer = GetAutoSaveTimer();
            if (timer != null)
            {
                timer.Stop();
                StationeersServerPatcher.LogInfo("Stopped StationAutoSave timer.");
            }
        }

        /// <summary>
        /// Restarts the autosave timer using the game's ResetAutoSave method
        /// </summary>
        public static void RestartAutoSaveTimer()
        {
            try
            {
                if (_resetAutoSaveMethod == null)
                {
                    var stationAutoSaveType = AccessTools.TypeByName("Assets.Scripts.Serialization.StationAutoSave");
                    _resetAutoSaveMethod = AccessTools.Method(stationAutoSaveType, "ResetAutoSave");
                }

                if (_resetAutoSaveMethod != null)
                {
                    _resetAutoSaveMethod.Invoke(null, null);
                    StationeersServerPatcher.LogInfo("Restarted StationAutoSave timer via ResetAutoSave().");
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error restarting autosave timer: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to fix: Dedicated Server Does Not Auto-Pause on Initial Startup
    /// 
    /// When starting a dedicated server with 0 clients and AutoPauseServer enabled,
    /// this patch triggers the auto-pause logic that would normally only run when
    /// the last client disconnects.
    /// </summary>
    [HarmonyPatch]
    public static class NetworkServerAutoPauseOnStartupPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            var networkServerType = AccessTools.TypeByName("Assets.Scripts.NetworkServer");
            if (networkServerType == null)
            {
                StationeersServerPatcher.LogError("Could not find NetworkServer type.");
                return null;
            }

            var method = AccessTools.Method(networkServerType, "CreateNewGameSession");
            if (method == null)
            {
                StationeersServerPatcher.LogError("Could not find CreateNewGameSession method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("Found CreateNewGameSession method for patching.");
            return method;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                TriggerAutoPauseIfNeeded().Forget();
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error in CreateNewGameSession postfix: {ex.Message}");
            }
        }

        private static async UniTaskVoid TriggerAutoPauseIfNeeded()
        {
            try
            {
                // Small delay to ensure server is fully initialized
                await Task.Delay(500);
                await UniTask.SwitchToMainThread(default);

                if (!ServerPauseHelper.IsAutoPauseEnabled)
                {
                    StationeersServerPatcher.LogInfo("AutoPauseServer is disabled, skipping startup auto-pause.");
                    return;
                }

                int clientCount = ServerPauseHelper.GetClientCount();
                if (clientCount != 0)
                {
                    StationeersServerPatcher.LogInfo($"Server has {clientCount} client(s) connected, skipping startup auto-pause.");
                    return;
                }

                StationeersServerPatcher.LogInfo("Server started with 0 clients and AutoPauseServer enabled. Triggering auto-pause logic...");

                // Get NetworkBase type and trigger the auto-save/pause logic
                var networkBaseType = AccessTools.TypeByName("NetworkBase");
                
                // Get _lastClientLeaveCancellation and call CancelAndInitialize()
                var cancellationField = AccessTools.Field(networkBaseType, "_lastClientLeaveCancellation");
                if (cancellationField == null)
                {
                    StationeersServerPatcher.LogError("Could not find _lastClientLeaveCancellation field.");
                    return;
                }

                var cancellationSource = cancellationField.GetValue(null);
                if (cancellationSource == null)
                {
                    StationeersServerPatcher.LogError("_lastClientLeaveCancellation is null.");
                    return;
                }

                var cancelAndInitMethod = AccessTools.Method(cancellationSource.GetType(), "CancelAndInitialize");
                if (cancelAndInitMethod == null)
                {
                    StationeersServerPatcher.LogError("Could not find CancelAndInitialize method.");
                    return;
                }
                cancelAndInitMethod.Invoke(cancellationSource, null);

                var tokenProperty = AccessTools.Property(cancellationSource.GetType(), "Token");
                if (tokenProperty == null)
                {
                    StationeersServerPatcher.LogError("Could not find Token property.");
                    return;
                }
                var token = tokenProperty.GetValue(cancellationSource);

                // Call AutoSaveOnLastClientLeave
                var autoSaveMethod = AccessTools.Method(networkBaseType, "AutoSaveOnLastClientLeave");
                if (autoSaveMethod == null)
                {
                    StationeersServerPatcher.LogError("Could not find AutoSaveOnLastClientLeave method.");
                    return;
                }

                var resultTask = autoSaveMethod.Invoke(null, new object[] { token });
                var forgetMethod = AccessTools.Method(resultTask.GetType(), "Forget");
                forgetMethod?.Invoke(resultTask, null);

                StationeersServerPatcher.LogInfo("Auto-pause logic triggered. Server will save and pause after 10 seconds.");
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error triggering auto-pause on startup: {ex.Message}");
                if (ex.InnerException != null)
                {
                    StationeersServerPatcher.LogError($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Patch WorldManager.SetGamePause to:
    /// 1. Stop the autosave timer when pausing in batch mode with AutoPauseServer
    /// 2. Ensure NetworkBase.IsPaused is also set (fixes the bug where only WorldManager.IsGamePaused is set)
    /// </summary>
    [HarmonyPatch]
    public static class SetGamePausePatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            var worldManagerType = AccessTools.TypeByName("WorldManager");
            var method = AccessTools.Method(worldManagerType, "SetGamePause");
            if (method != null)
            {
                StationeersServerPatcher.LogInfo("Found WorldManager.SetGamePause method for patching.");
            }
            return method;
        }

        [HarmonyPostfix]
        public static void Postfix(bool pauseGame)
        {
            try
            {
                // Only handle for batch mode servers with AutoPauseServer enabled
                if (!ServerPauseHelper.IsBatchMode || !ServerPauseHelper.IsAutoPauseEnabled)
                    return;

                // Only proceed if clients count is 0 (auto-pause scenario)
                int clientCount = ServerPauseHelper.GetClientCount();

                if (!ServerPauseHelper.IsAutoPauseEnabled)
                    return;

                if (pauseGame)
                {
                    if (clientCount == 0)  // Only for auto-pause, not manual pause with players
                    {
                        // When pausing, stop the autosave timer
                        ServerPauseHelper.StopAutoSaveTimer();

                        // Also ensure NetworkBase.IsPaused is set to true
                        // (fixes the bug where AutoSaveOnLastClientLeave only calls SetGamePause)
                        if (!ServerPauseHelper.IsPaused)
                        {
                            // Directly set the property to avoid recursion with PauseEvent
                            var networkBaseType = AccessTools.TypeByName("NetworkBase");
                            var isPausedProperty = AccessTools.Property(networkBaseType, "IsPaused");
                            isPausedProperty?.SetValue(null, true);
                            StationeersServerPatcher.LogInfo("Set NetworkBase.IsPaused = true to sync with WorldManager.IsGamePaused.");
                        }
                    }
                }
                else
                {
                    // When unpausing with 0 clients, don't restart the timer
                    // The timer should only restart when a client connects
                    if (clientCount > 0)
                    {
                        ServerPauseHelper.RestartAutoSaveTimer();
                    }
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error in SetGamePause postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to unpause the game when a client connects to a paused dedicated server.
    /// </summary>
    [HarmonyPatch]
    public static class UnpauseOnClientConnectPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            var networkBaseType = AccessTools.TypeByName("NetworkBase");
            var method = AccessTools.Method(networkBaseType, "OnClientAdded");
            if (method != null)
            {
                StationeersServerPatcher.LogInfo("Found OnClientAdded method for unpause patching.");
            }
            return method;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                // Only handle this for batch mode servers with AutoPauseServer enabled
                if (!ServerPauseHelper.IsBatchMode)
                    return;

                if (!ServerPauseHelper.IsAutoPauseEnabled)
                    return;

                // If the game is currently paused, unpause it using PauseEvent
                // This will properly set both flags and restart the autosave timer
                if (ServerPauseHelper.IsPaused)
                {
                    StationeersServerPatcher.LogInfo("Client connected to paused server. Unpausing game...");
                    ServerPauseHelper.SetPauseState(false);
                }
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogError($"Error unpausing on client connect: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch to block SpawnDynamicThingMaxStackMessage.Process unless in Creative mode
    /// </summary>
    [HarmonyPatch]
    public static class SpawnDynamicThingMaxStackMessagePatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("SpawnDynamicThingMaxStackMessage");
            if (type == null)
            {
                StationeersServerPatcher.LogError("Could not find SpawnDynamicThingMaxStackMessage type.");
                return null;
            }

            var method = AccessTools.Method(type, "Process");
            if (method == null)
            {
                StationeersServerPatcher.LogError("Could not find Process method.");
                return null;
            }

            StationeersServerPatcher.LogInfo("Found Process method for patching.");
            return method;
        }

        [HarmonyPrefix]
        public static bool Prefix(long hostId)
        {
            // Only allow in Creative mode
            if (WorldManager.Instance.GameMode != GameMode.Creative)
            {
                StationeersServerPatcher.LogError("Spawn blocked: not in Creative mode");
                return false; // Skip original method
            }
            return true; // Allow original to run
        }
    }
}