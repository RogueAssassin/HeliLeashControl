using System;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Heli Leash Control", "RogueAssassin", "1.0.15")]
    [Description("Keeps the Patrol Helicopter close to attackers when heavily damaged and announces it to the server")]

    public class HeliLeashControl : CovalencePlugin
    {
        #region --- Config ---

        private class ConfigData
        {
            [JsonProperty("Config Version")]
            public string Version { get; set; } = "1.0.15"; // Plugin version

            // Leash Settings
            [JsonProperty("Enable leash behavior")]
            public bool EnableLeash { get; set; } = true;

            [JsonProperty("Health threshold to enable leash (e.g. 400)")]
            public float HealthThreshold { get; set; } = 400f;

            [JsonProperty("Max allowed distance from attacker")]
            public float MaxDistance { get; set; } = 150f;

            // Debug Settings
            [JsonProperty("Enable debug messages in console")]
            public bool EnableDebug { get; set; } = false;

            // Messaging Settings
            [JsonProperty("Send global chat message when heli is leashed")]
            public bool SendChatMessage { get; set; } = true;

            [JsonProperty("Chat message color (hex)")]
            public string ChatMessageColor { get; set; } = "#ff4d4d";

            [JsonProperty("Global chat message format")]
            public string GlobalMessageFormat { get; set; } = "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>";

            // Placeholder for any additional/new config options
            /* Example:
            [JsonProperty("NewSettingName")]
            public bool NewSettingName { get; set; } = true;
            */
        }

        private ConfigData configData;

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
                ProcessConfig();
                LogEvent("Configuration loaded successfully.", "INFO");
            }
            catch
            {
                LogEvent("Failed to load config, creating default.", "ERROR");
                LoadDefaultConfig();
                LogEvent("Configuration file was invalid and has been regenerated.", "WARNING");
            }
        }

        protected override void SaveConfig() => Config.WriteObject(configData, true);

        private void UpdateChatMessageDefaults(ref bool changed)
        {
            if (string.IsNullOrEmpty(configData.ChatMessageColor)) 
            { 
                configData.ChatMessageColor = "#ff4d4d"; 
                changed = true; 
            }
            if (string.IsNullOrEmpty(configData.GlobalMessageFormat)) 
            { 
                configData.GlobalMessageFormat = "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>"; 
                changed = true; 
            }
        }

        private void ProcessConfig()
        {
            bool changed = false;

            if (string.IsNullOrEmpty(configData.Version) || configData.Version != this.Version.ToString())
            {
                LogEvent($"Config version {configData.Version} is outdated; upgrading to {this.Version}", "WARNING");

                // Migrate settings to support new versions
                if (configData.Version == "1.0.15")
                {
                    if (string.IsNullOrEmpty(configData.ChatMessageColor)) { configData.ChatMessageColor = "#ff4d4d"; changed = true; }
                    if (string.IsNullOrEmpty(configData.GlobalMessageFormat)) { configData.GlobalMessageFormat = "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>"; changed = true; }
                }

                // Update config version
                configData.Version = this.Version.ToString();
                changed = true;
            }

            if (changed)
            {
                SaveConfig();
            }
        }

        #endregion

        #region Data

        private readonly Dictionary<BaseHelicopter, BasePlayer> heliAttackers = new();

        #endregion

        #region Hooks

        private static readonly HashSet<string> HelicopterPrefabs = new HashSet<string>
        {
            "patrolhelicopter"
        };

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!configData.EnableLeash || entity == null || info?.InitiatorPlayer == null) return;

            if (!HelicopterPrefabs.Contains(entity.ShortPrefabName)) return;

            var heli = entity as BaseHelicopter;
            if (heli == null) return;

            float currentHealth = entity.Health();
            BasePlayer attacker = info.InitiatorPlayer;

            if (!heliAttackers.ContainsKey(heli))
                heliAttackers.Add(heli, attacker);
            else
                heliAttackers[heli] = attacker;

            if (currentHealth <= configData.HealthThreshold)
                EnforceLeash(heli, attacker);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var heli = entity as BaseHelicopter;
            if (heli != null && heliAttackers.ContainsKey(heli))
                heliAttackers.Remove(heli);
        }

        #endregion

        #region Leash Logic

        private void EnforceLeash(BaseHelicopter heli, BasePlayer attacker)
        {
            if (heli == null || attacker == null || attacker.IsDead() || !attacker.IsConnected) return;

            Vector3 heliPos = heli.transform.position;
            Vector3 attackerPos = attacker.transform.position;
            float distance = Vector3.Distance(heliPos, attackerPos);

            if (distance > configData.MaxDistance)
            {
                Vector3 direction = (attackerPos - heliPos).normalized;
                Vector3 newTarget = attackerPos - direction * (configData.MaxDistance * 0.5f);

                var heliAI = heli.GetComponent<PatrolHelicopterAI>();
                if (heliAI == null) return;

                heliAI.SetTargetDestination(newTarget);

                if (configData.EnableDebug)
                    LogEvent($"Pulled heli back to {attacker.displayName}, distance was {distance:F1}m.", "DEBUG");

                SendLeashMessage(attacker);
            }
        }

        #endregion

        #region Messaging

        private void SendLeashMessage(BasePlayer attacker)
        {
            if (attacker == null || !attacker.IsConnected || !configData.SendChatMessage)
                return;

            string grid = GetGridFromPosition(attacker.transform.position);
            string message = string.Format(configData.GlobalMessageFormat, attacker.displayName, grid);

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    player.ChatMessage("<size=18><sprite name=\"heli\" /></size> " + message);
                }
            }
        }

        private string GetGridFromPosition(Vector3 position)
        {
            const float gridSize = 146.3f;
            const float gridOffset = 3000f;

            int gridX = Mathf.FloorToInt((position.x + gridOffset) / gridSize);
            int gridZ = Mathf.FloorToInt((position.z + gridOffset) / gridSize);

            gridZ = Mathf.Clamp(gridZ, 0, 25); // Only 26 letters (A-Z)
            char letter = (char)('A' + gridZ);

            return $"{letter}{gridX}";
        }

        #endregion

        #region Initialization

        private void Init()
        {
            LoadConfig();
        }

        private void OnServerInitialized()
        {
            if (configData.EnableLeash)
                LogEvent($"HeliLeashControl initialized. Leash active below {configData.HealthThreshold} HP, Max distance: {configData.MaxDistance}m.", "INFO");
            else
                LogEvent("HeliLeashControl is disabled in config.", "INFO");
        }

        #endregion

        #region Logging Helper

        private void LogEvent(string message, string level = "INFO")
        {
            string logMessage = $"[{level.ToUpper()}] {message}";

            // Log the message based on the log level
            switch (level.ToUpper())
            {
                case "ERROR":
                    PrintError($"[Heli Leash Control] {logMessage}");  // Log as an error
                    break;
                case "WARNING":
                    PrintWarning($"[Heli Leash Control] {logMessage}");  // Log as a warning
                    break;
                case "DEBUG":
                    if (configData.EnableDebug)  // Only log debug messages if enabled
                        Puts($"[Heli Leash Control] {logMessage}");  // Log to the console
                    break;
                case "INFO":
                default:
                    Puts($"[Heli Leash Control] {logMessage}");  // Default is info level
                    break;
            }
        }
        
        #endregion
    }
}
