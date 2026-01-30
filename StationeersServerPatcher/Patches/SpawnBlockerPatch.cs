using System.Reflection;
using HarmonyLib;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher.Patches
{
    /// <summary>
    /// Patch to block SpawnDynamicThingMaxStackMessage.Process unless in Creative mode
    /// </summary>
    [HarmonyPatch]
    public static class SpawnDynamicThingMaxStackMessagePatch
    {
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            if (!PluginConfig.IsSpawnBlockerPatchEnabled)
            {
                StationeersServerPatcher.LogInfo("SpawnBlockerPatch is disabled, skipping SpawnDynamicThingMaxStackMessagePatch.");
                return null;
            }

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
            if (!PluginConfig.IsSpawnBlockerPatchEnabled)
                return true; // Allow original to run if patch is disabled

            // Only allow in Creative mode
            if (WorldManager.Instance.GameMode != GameMode.Creative)
            {
                StationeersServerPatcher.LogToServerChatAsync("[ServerPatcher] Spawn using thing spawn blocked: Server not in Creative mode");
                return false; // Skip original method
            }
            return true; // Allow original to run
        }
    }
}
