using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Cysharp.Threading.Tasks;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher.Patches
{
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
            if (!PluginConfig.IsAutoPausePatchEnabled)
            {
                StationeersServerPatcher.LogInfo("AutoPausePatch is disabled, skipping NetworkServerAutoPauseOnStartupPatch.");
                return null;
            }

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
            if (!PluginConfig.IsAutoPausePatchEnabled)
                return;

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
            if (!PluginConfig.IsAutoPausePatchEnabled)
            {
                StationeersServerPatcher.LogInfo("AutoPausePatch is disabled, skipping SetGamePausePatch.");
                return null;
            }

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
            if (!PluginConfig.IsAutoPausePatchEnabled)
                return;

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
                            //StationeersServerPatcher.LogInfo("Set NetworkBase.IsPaused = true to sync with WorldManager.IsGamePaused.");
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
            if (!PluginConfig.IsAutoPausePatchEnabled)
            {
                StationeersServerPatcher.LogInfo("AutoPausePatch is disabled, skipping UnpauseOnClientConnectPatch.");
                return null;
            }

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
            if (!PluginConfig.IsAutoPausePatchEnabled)
                return;

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
}
