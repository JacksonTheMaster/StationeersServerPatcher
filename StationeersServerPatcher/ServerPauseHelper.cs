using System;
using HarmonyLib;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher
{
    /// Helper class for managing pause state and autosave timer via reflection
    public static class ServerPauseHelper
    {
        private static System.Reflection.MethodInfo _pauseEventMethod;
        private static System.Reflection.PropertyInfo _isPausedProperty;
        private static Type _networkBaseType;
        private static System.Timers.Timer _timerCache;
        private static System.Reflection.MethodInfo _resetAutoSaveMethod;

        private static Type NetworkBaseType
        {
            get
            {
                if (_networkBaseType == null)
                    _networkBaseType = AccessTools.TypeByName("NetworkBase");
                return _networkBaseType;
            }
        }

        /// Calls NetworkBase.PauseEvent(pause) to properly set both NetworkBase.IsPaused 
        /// and WorldManager.IsGamePaused, and notify clients.
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

        /// Gets the current value of NetworkBase.IsPaused
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

        /// Checks if AutoPauseServer setting is enabled
        
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


        /// Checks if running in batch/dedicated server mode

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


        /// Gets the client count from NetworkBase.Clients

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


        /// Gets the StationAutoSave timer via reflection
        
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


        /// Stops the autosave timer

        public static void StopAutoSaveTimer()
        {
            var timer = GetAutoSaveTimer();
            if (timer != null)
            {
                timer.Stop();
                StationeersServerPatcher.LogInfo("Stopped StationAutoSave timer.");
            }
        }


        /// Restarts the autosave timer using the game's ResetAutoSave method

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
}
