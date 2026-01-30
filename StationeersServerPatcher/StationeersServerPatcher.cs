using BepInEx;
using HarmonyLib;
using System;
using System.Reflection;
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
        private const string PluginVersion = "2.0.0";
        private const string LogPrefix = "[StationeersServerPatcher] ";

        private Harmony harmony;

        // Cached reflection references for CommandLine.Process
        private static Type _commandLineType;
        private static MethodInfo _processMethod;
        private static bool _commandLineInitialized;

        private void Awake()
        {
            LogInfo("Plugin initializing...");

            // Initialize configuration
            PluginConfig.Initialize(Config);
            LogInfo($"Configuration loaded - AutoPausePatch: {PluginConfig.EnableAutoPausePatch.Value}, SpawnBlockerPatch: {PluginConfig.EnableSpawnBlockerPatch.Value}");

            RemoteConfig.FetchRemoteConfig();

            // Log effective configuration after remote config is applied
            LogInfo($"Effective configuration - AutoPausePatch: {PluginConfig.IsAutoPausePatchEnabled}, SpawnBlockerPatch: {PluginConfig.IsSpawnBlockerPatchEnabled}");

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

            // Initialize CommandLine reflection references
            InitializeCommandLine();

            LogInfo("Initialization complete.");
        }

        private static void InitializeCommandLine()
        {
            if (_commandLineInitialized)
                return;

            try
            {
                _commandLineType = Type.GetType("Util.Commands.CommandLine, Assembly-CSharp");
                if (_commandLineType == null)
                {
                    LogError("CommandLine type not found!");
                    return;
                }

                _processMethod = _commandLineType.GetMethod("Process", new[] { typeof(string) });
                if (_processMethod == null)
                {
                    LogError("CommandLine.Process(string) method not found!");
                    return;
                }

                _commandLineInitialized = true;
                LogInfo("CommandLine reflection initialized successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize CommandLine reflection: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes a server command via CommandLine.Process.
        /// This must be called from the main Unity thread.
        /// </summary>
        /// <param name="command">The command to execute (e.g., "say Hello" or "kick PlayerName")</param>
        /// <returns>True if the command was executed, false if initialization failed</returns>
        public static bool RunCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                LogWarning("RunCommand called with empty command.");
                return false;
            }

            if (!_commandLineInitialized)
            {
                InitializeCommandLine();
                if (!_commandLineInitialized)
                {
                    LogError("Cannot run command: CommandLine not initialized.");
                    return false;
                }
            }

            try
            {
                // Ensure command starts with "-" as expected by CommandLine.Process
                string formattedCommand = command.StartsWith("-") ? command : "-" + command;
                _processMethod.Invoke(null, new object[] { formattedCommand });
                // only log the command if the command is not a say command
                if (!command.StartsWith("say "))
                {
                    LogInfo($"Executed command: {command}");
                }
                
                return true;
            }
            catch (TargetInvocationException tex)
            {
                LogError($"Error executing command '{command}': {tex.Message}");
                if (tex.InnerException != null)
                    LogError($"Inner error: {tex.InnerException.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Unexpected error executing command '{command}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Executes a server command asynchronously.
        /// Automatically switches to the main Unity thread before executing.
        /// </summary>
        /// <param name="command">The command to execute (e.g., "say Hello" or "kick PlayerName")</param>
        public static async void RunCommandAsync(string command)
        {
            try
            {
                await UniTask.SwitchToMainThread();
                RunCommand(command);
            }
            catch (Exception ex)
            {
                LogError($"Error in RunCommandAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a message to all connected players via the server chat.
        /// This must be called from the main Unity thread.
        /// </summary>
        /// <param name="message">The message to send to server chat</param>
        public static void LogToServerChat(string message)
        {
            RunCommand($"say {message}");
        }

        /// <summary>
        /// Sends a message to all connected players via the server chat asynchronously.
        /// Automatically switches to the main Unity thread before sending.
        /// </summary>
        /// <param name="message">The message to send to server chat</param>
        public static async void LogToServerChatAsync(string message)
        {
            try
            {
                await UniTask.SwitchToMainThread();
                LogToServerChat(message);
            }
            catch (Exception ex)
            {
                LogError($"Error in LogToServerChatAsync: {ex.Message}");
            }
        }

        public static void LogInfo(string message) => Debug.Log(LogPrefix + message);
        public static void LogWarning(string message) => Debug.LogWarning(LogPrefix + message);
        public static void LogError(string message) => Debug.LogError(LogPrefix + message);
    }
}