using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Xml;

// StationeersServerPatcher - Stationeers Dedicated Server Patches
// Copyright (c) 2025 JacksonTheMaster
// All rights reserved.
// https://github.com/SteamServerUI/StationeersServerUI

namespace StationeersServerPatcher
{
    /// <summary>
    /// Represents a single feature's remote configuration
    /// </summary>
    public class FeatureConfig
    {
        public string id;
        public bool enabled;
        public string reason;
    }

    /// <summary>
    /// Handles fetching and applying remote configuration for patch killswitches
    /// </summary>
    public static class RemoteConfig
    {
        // Feature IDs - must match exactly in the remote XML
        public const string FEATURE_AUTO_PAUSE = "AutoPausePatch";
        public const string FEATURE_SPAWN_BLOCKER = "SpawnBlockerPatch";

        private static Dictionary<string, FeatureConfig> _remoteFeatures = new Dictionary<string, FeatureConfig>();
        private static bool _initialized = false;
        private static string _remoteMessage = null;

        /// <summary>
        /// Fetches the remote configuration from the configured URL
        /// </summary>
        public static void FetchRemoteConfig()
        {
            if (!PluginConfig.EnableRemoteKillswitch.Value)
            {
                StationeersServerPatcher.LogInfo("Remote killswitch is disabled in config.");
                _initialized = true;
                return;
            }

            string url = PluginConfig.RemoteConfigUrl.Value;
            if (string.IsNullOrWhiteSpace(url))
            {
                StationeersServerPatcher.LogWarning("Remote config URL is empty, skipping remote config fetch.");
                _initialized = true;
                return;
            }

            try
            {
                //StationeersServerPatcher.LogInfo($"Fetching remote config from: {url}");

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 5000; // 5 second timeout
                request.UserAgent = $"StationeersServerPatcher/{PluginConfig.PluginVersion}";

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    string xml = reader.ReadToEnd();
                    ParseRemoteConfig(xml);
                }

                //StationeersServerPatcher.LogInfo("Remote config fetched successfully.");
            }
            catch (WebException ex)
            {
                StationeersServerPatcher.LogWarning($"Failed to fetch remote config (network error): {ex.Message}");
                StationeersServerPatcher.LogInfo("Continuing with local configuration only.");
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogWarning($"Failed to fetch remote config: {ex.Message}");
                StationeersServerPatcher.LogInfo("Continuing with local configuration only.");
            }

            _initialized = true;
        }

        private static void ParseRemoteConfig(string xml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);

                // Parse message
                XmlNode messageNode = doc.SelectSingleNode("/PatcherConfig/Message");
                if (messageNode != null)
                {
                    _remoteMessage = messageNode.InnerText;
                }

                // Parse features
                XmlNodeList featureNodes = doc.SelectNodes("/PatcherConfig/Features/Feature");
                if (featureNodes != null)
                {
                    foreach (XmlNode featureNode in featureNodes)
                    {
                        var feature = new FeatureConfig();

                        XmlNode idNode = featureNode.SelectSingleNode("Id");
                        XmlNode enabledNode = featureNode.SelectSingleNode("Enabled");
                        XmlNode reasonNode = featureNode.SelectSingleNode("Reason");

                        feature.id = idNode?.InnerText;
                        feature.enabled = enabledNode == null || bool.Parse(enabledNode.InnerText);
                        feature.reason = reasonNode?.InnerText;

                        if (!string.IsNullOrEmpty(feature.id))
                        {
                            _remoteFeatures[feature.id] = feature;

                            if (!feature.enabled)
                            {
                                string reason = string.IsNullOrEmpty(feature.reason) 
                                    ? "No reason provided" 
                                    : feature.reason;
                                StationeersServerPatcher.LogWarning($"Feature '{feature.id}' remotely disabled: {reason}");
                            }
                        }
                    }
                }

                StationeersServerPatcher.LogInfo($"Loaded {_remoteFeatures.Count} feature configuration(s) from remote killswitch.");
            }
            catch (Exception ex)
            {
                StationeersServerPatcher.LogWarning($"Failed to parse remote config XML: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a feature is enabled, considering both local config and remote killswitch
        /// </summary>
        /// <param name="featureId">The feature ID to check</param>
        /// <param name="localEnabled">The local config value for this feature</param>
        /// <returns>True if the feature should be enabled</returns>
        public static bool IsFeatureEnabled(string featureId, bool localEnabled)
        {
            // If locally disabled, respect that
            if (!localEnabled)
                return false;

            // If remote killswitch is disabled, use local config
            if (!PluginConfig.EnableRemoteKillswitch.Value)
                return localEnabled;

            // Check remote config
            if (_remoteFeatures.TryGetValue(featureId, out FeatureConfig remoteConfig))
            {
                if (!remoteConfig.enabled)
                {
                    // Feature is remotely disabled
                    return false;
                }
            }

            return localEnabled;
        }

        /// <summary>
        /// Gets the reason why a feature was remotely disabled
        /// </summary>
        public static string GetDisabledReason(string featureId)
        {
            if (_remoteFeatures.TryGetValue(featureId, out FeatureConfig remoteConfig))
            {
                if (!remoteConfig.enabled && !string.IsNullOrEmpty(remoteConfig.reason))
                {
                    return remoteConfig.reason;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if the remote config has been fetched
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Gets any message from the remote config
        /// </summary>
        public static string RemoteMessage => _remoteMessage;
    }
}
