using System;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Heli Leash Control", "RogueAssassin", "1.1.4")]
    [Description("Keeps the Patrol Helicopter close to attackers when heavily damaged and announces it to the server")]

    public class HeliLeashControl : CovalencePlugin
    {
        #region Configuration

        private ConfigData config;

        public class ConfigData
        {
            [JsonProperty(Order = int.MaxValue)]
            public VersionNumber Version = new VersionNumber(1, 1, 4);

            [JsonProperty("Enable leash behavior")]
            public bool EnableLeash { get; set; } = true;

            [JsonProperty("Health threshold to enable leash (e.g. 400)")]
            public float HealthThreshold { get; set; } = 400f;

            [JsonProperty("Max allowed distance from attacker")]
            public float MaxDistance { get; set; } = 150f;

            [JsonProperty("Enable debug messages in console")]
            public bool EnableDebug { get; set; } = false;

            [JsonProperty("Send global chat message when heli is leashed")]
            public bool SendChatMessage { get; set; } = true;

            [JsonProperty("Chat message color (hex)")]
            public string ChatMessageColor { get; set; } = "#ff4d4d";

            [JsonProperty("Global chat message format")]
            public string GlobalMessageFormat { get; set; } = "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>";
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config?.Version == null || config.Version < new VersionNumber(1, 1, 4))
                {
                    PrintWarning("Outdated or invalid config detected, loading defaults.");
                    LoadDefaultConfig();
                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Error loading config: {ex.Message}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Data

        private readonly Dictionary<BaseHelicopter, BasePlayer> heliAttackers = new();

        #endregion

        #region Hooks

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (!config.EnableLeash) return;

            if (entity == null || info?.InitiatorPlayer == null) return;

            if (entity.ShortPrefabName == null || !entity.ShortPrefabName.Contains("patrolhelicopter")) return;

            var heli = entity as BaseHelicopter;
            if (heli == null) return;

            float currentHealth = entity.Health();

            BasePlayer attacker = info.InitiatorPlayer;

            if (!heliAttackers.ContainsKey(heli))
                heliAttackers.Add(heli, attacker);
            else
                heliAttackers[heli] = attacker;

            if (currentHealth <= config.HealthThreshold)
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

            if (distance > config.MaxDistance)
            {
                Vector3 direction = (attackerPos - heliPos).normalized;
                Vector3 newTarget = attackerPos - direction * (config.MaxDistance * 0.5f);

                var heliAI = heli.GetComponent<PatrolHelicopterAI>();
                if (heliAI == null) return;

                // Correct method to move the heli
                heliAI.SetTargetDestination(newTarget);

                if (config.EnableDebug)
                    Puts($"[HeliLeashControl] Pulled heli back to {attacker.displayName}, distance was {distance:F1}m.");

                SendLeashMessage(attacker);
            }
        }

        #endregion

        #region Messaging

        private void SendLeashMessage(BasePlayer attacker)
        {
            if (attacker == null || !attacker.IsConnected || !config.SendChatMessage)
                return;

            string grid = GetGridFromPosition(attacker.transform.position);
            string message = string.Format(config.GlobalMessageFormat, attacker.displayName, grid);

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
            int gridX = Mathf.FloorToInt((position.x + 3000f) / 146.3f);
            int gridZ = Mathf.FloorToInt((position.z + 3000f) / 146.3f);
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
            if (config.EnableLeash)
                Puts($"HeliLeashControl initialized. Leash active below {config.HealthThreshold} HP, Max distance: {config.MaxDistance}m.");
            else
                Puts("HeliLeashControl is disabled in config.");
        }

        #endregion
    }
}
