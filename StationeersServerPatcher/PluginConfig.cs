using BepInEx.Configuration;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher
{
    /// <summary>
    /// Configuration settings for StationeersServerPatcher
    /// </summary>
    public static class PluginConfig
    {
        public const string PluginVersion = "2.1.0";
        private const string DefaultRemoteConfigUrl = "https://raw.githubusercontent.com/JacksonTheMaster/StationeersServerPatcher/refs/heads/main/patcher-config.xml";

        public static ConfigEntry<bool> EnableAutoPausePatch { get; private set; }
        public static ConfigEntry<bool> EnableSpawnBlockerPatch { get; private set; }
        public static ConfigEntry<bool> EnableTerrainMemoryLeakPatch { get; private set; }
        public static ConfigEntry<bool> EnableRemoteKillswitch { get; private set; }
        public static ConfigEntry<string> RemoteConfigUrl { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            // Remote killswitch settings
            EnableRemoteKillswitch = config.Bind(
                "Remote",
                "EnableRemoteKillswitch",
                true,
                "Enable fetching remote configuration to allow emergency disabling of patches if they cause issues with game updates. This helps prevent server problems without requiring an immediate mod update."
            );

            RemoteConfigUrl = config.Bind(
                "Remote",
                "RemoteConfigUrl",
                DefaultRemoteConfigUrl,
                "URL to fetch the remote killswitch configuration from. Only change this if you know what you're doing."
            );

            // Patch settings
            EnableAutoPausePatch = config.Bind(
                "Patches",
                "EnableAutoPausePatch",
                true,
                "Enable the auto-pause patch that fixes dedicated server auto-pause on startup and handles pause/unpause when clients connect/disconnect."
            );

            EnableSpawnBlockerPatch = config.Bind(
                "Patches",
                "EnableSpawnBlockerPatch",
                true,
                "Enable the spawn blocker patch that prevents item spawning via SpawnDynamicThingMaxStackMessage unless the server is in Creative mode."
            );

            EnableTerrainMemoryLeakPatch = config.Bind(
                "Patches",
                "EnableTerrainMemoryLeakPatch",
                true,
                "Enable the terrain memory leak patch that properly destroys Unity Mesh objects when terrain is modified (mining). Fixes severe memory leaks during prolonged server sessions with terrain activity."
            );
        }

        /// <summary>
        /// Checks if the AutoPause patch should be enabled (considering both local and remote config)
        /// </summary>
        public static bool IsAutoPausePatchEnabled => 
            RemoteConfig.IsFeatureEnabled(RemoteConfig.FEATURE_AUTO_PAUSE, EnableAutoPausePatch.Value);

        /// <summary>
        /// Checks if the SpawnBlocker patch should be enabled (considering both local and remote config)
        /// </summary>
        public static bool IsSpawnBlockerPatchEnabled => 
            RemoteConfig.IsFeatureEnabled(RemoteConfig.FEATURE_SPAWN_BLOCKER, EnableSpawnBlockerPatch.Value);

        /// <summary>
        /// Checks if the TerrainMemoryLeak patch should be enabled (considering both local and remote config)
        /// </summary>
        public static bool IsTerrainMemoryLeakPatchEnabled => 
            RemoteConfig.IsFeatureEnabled(RemoteConfig.FEATURE_TERRAIN_MEMORY_LEAK, EnableTerrainMemoryLeakPatch.Value);
    }
}
