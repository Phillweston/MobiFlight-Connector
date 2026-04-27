using MobiFlight.Base;
using MobiFlight.OutputConfig;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MobiFlight
{
    /// <summary>
    /// Builds Home Assistant MQTT Discovery payloads for MobiFlight outputs and inputs.
    ///
    /// HA listens on <c>{prefix}/{component}/{node_id}/{object_id}/config</c> (default
    /// <c>homeassistant/...</c>) and creates an entity from each retained config message
    /// it sees. Publishing an empty retained payload to the same topic removes the entity.
    ///
    /// All entities published by MobiFlight share a single <c>device</c> block so they
    /// appear together in the Home Assistant UI.
    /// </summary>
    internal static class HomeAssistantDiscovery
    {
        /// <summary>
        /// Logical "node id" used in discovery topics and as the device identifier so
        /// every MobiFlight entity rolls up under one device card in HA.
        /// </summary>
        public const string DeviceNodeId = "mobiflight";

        private const string DeviceName = "MobiFlight Connector";
        private const string DeviceManufacturer = "MobiFlight";

        /// <summary>
        /// One discovery message ready to be published.
        /// </summary>
        public class DiscoveryMessage
        {
            public string Topic { get; set; }
            public string Payload { get; set; }
        }

        /// <summary>
        /// Builds the full set of HA discovery messages that should be retained on the
        /// broker for the given project. Items without a usable MQTT topic are skipped.
        /// </summary>
        /// <param name="project">The active project; null is treated as empty.</param>
        /// <param name="discoveryPrefix">HA discovery topic prefix (typically "homeassistant").</param>
        public static List<DiscoveryMessage> Build(Project project, string discoveryPrefix)
        {
            var result = new List<DiscoveryMessage>();
            if (project?.ConfigFiles == null) return result;

            var prefix = string.IsNullOrWhiteSpace(discoveryPrefix)
                ? MQTTServerSettings.DefaultHomeAssistantDiscoveryPrefix
                : discoveryPrefix.Trim().Trim('/');

            foreach (var file in project.ConfigFiles)
            {
                if (file?.ConfigItems == null) continue;

                foreach (var item in file.ConfigItems)
                {
                    if (item == null || !item.Active) continue;

                    DiscoveryMessage message = null;

                    if (item is OutputConfigItem output)
                    {
                        message = BuildOutput(output, prefix);
                    }
                    else if (item is InputConfigItem input)
                    {
                        message = BuildInput(input, prefix);
                    }

                    if (message != null) result.Add(message);
                }
            }

            return result;
        }

        private static DiscoveryMessage BuildOutput(OutputConfigItem output, string prefix)
        {
            if (output.DeviceType != MqttMessageConfig.TYPE) return null;
            // Resolve through MqttTopics so the discovery state_topic matches what the
            // publisher actually publishes to (including the auto-from-Name fallback).
            var topic = MqttTopics.ForOutput(output);
            if (string.IsNullOrWhiteSpace(topic)) return null;

            // Outputs with a numeric value prefix (e.g. "GEAR_") cannot be parsed as raw
            // numbers; in that case we still expose them as plain text sensors.
            var component = "sensor";
            var uniqueId = MakeUniqueId(output.GUID);

            var payload = new JObject
            {
                ["name"] = SafeName(output.Name, fallback: topic),
                ["unique_id"] = uniqueId,
                ["object_id"] = uniqueId,
                ["state_topic"] = topic,
                ["device"] = BuildDeviceBlock()
            };

            // If there's no value prefix the payload is a clean number we can let HA
            // treat as a numeric state.
            if (string.IsNullOrEmpty(output.MqttMessage.ValuePrefix))
            {
                payload["state_class"] = "measurement";
            }

            return new DiscoveryMessage
            {
                Topic = $"{prefix}/{component}/{DeviceNodeId}/{uniqueId}/config",
                Payload = payload.ToString(Formatting.None)
            };
        }

        private static DiscoveryMessage BuildInput(InputConfigItem input, string prefix)
        {
            if (!MQTTManager.IsMQTTSerial(input.Controller?.Serial)) return null;
            // Resolve through MqttTopics so the discovery state_topic matches the topic
            // we actually subscribe to (including the auto-from-Name fallback).
            var topic = MqttTopics.ForInput(input);
            if (string.IsNullOrWhiteSpace(topic)) return null;

            string component;
            JObject payload;
            var uniqueId = MakeUniqueId(input.GUID);

            if (input.DeviceType == InputConfigItem.TYPE_BUTTON)
            {
                component = "binary_sensor";
                payload = new JObject
                {
                    ["name"] = SafeName(input.Name, fallback: topic),
                    ["unique_id"] = uniqueId,
                    ["object_id"] = uniqueId,
                    ["state_topic"] = topic,
                    // MobiFlight publishes "1" on press and "0" on release.
                    ["payload_on"] = "1",
                    ["payload_off"] = "0",
                    ["device"] = BuildDeviceBlock()
                };
            }
            else if (input.DeviceType == InputConfigItem.TYPE_ANALOG)
            {
                component = "sensor";
                payload = new JObject
                {
                    ["name"] = SafeName(input.Name, fallback: topic),
                    ["unique_id"] = uniqueId,
                    ["object_id"] = uniqueId,
                    ["state_topic"] = topic,
                    ["state_class"] = "measurement",
                    ["device"] = BuildDeviceBlock()
                };
            }
            else
            {
                return null;
            }

            return new DiscoveryMessage
            {
                Topic = $"{prefix}/{component}/{DeviceNodeId}/{uniqueId}/config",
                Payload = payload.ToString(Formatting.None)
            };
        }

        private static JObject BuildDeviceBlock()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            return new JObject
            {
                ["identifiers"] = new JArray { DeviceNodeId },
                ["name"] = DeviceName,
                ["manufacturer"] = DeviceManufacturer,
                ["sw_version"] = version ?? string.Empty
            };
        }

        private static string MakeUniqueId(string guid)
        {
            // GUIDs already contain dashes which HA accepts, but stripping them keeps the
            // resulting object_id (used as part of the entity_id) tidy.
            var safe = (guid ?? Guid.NewGuid().ToString()).Replace("-", "");
            return $"{DeviceNodeId}_{safe}";
        }

        private static string SafeName(string preferred, string fallback)
        {
            return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        }
    }
}
