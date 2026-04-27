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

                    if (item is OutputConfigItem output)
                    {
                        var outMsg = BuildOutput(output, prefix);
                        if (outMsg != null) result.Add(outMsg);
                    }
                    else if (item is InputConfigItem input)
                    {
                        // An input may produce more than one HA entity (e.g. an encoder
                        // expands to two button-style binary_sensors for inc/dec).
                        foreach (var inMsg in BuildInput(input, prefix))
                        {
                            if (inMsg != null) result.Add(inMsg);
                        }
                    }
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

        private static IEnumerable<DiscoveryMessage> BuildInput(InputConfigItem input, string prefix)
        {
            // Two source flavours of input items end up in HA discovery:
            //   • MQTT-controller items: MobiFlight subscribes to the user-defined topic;
            //     state is whatever the broker sends us. Topic resolved via ForInput.
            //   • Hardware items (Mega, joystick, MIDI, ...): MobiFlight publishes physical
            //     events to a Name-derived topic via PublishInputEvent. Topic resolved via
            //     ForInputPublish so HA listens to exactly what we publish.
            //
            // Hardware items are gated behind the per-input "Publish to MQTT" opt-in so
            // enabling the global HA Discovery toggle on an existing project doesn't
            // suddenly flood the dashboard with every active input. MQTT-controller items
            // are exempt because their MQTT topic IS their input source, so exposing them
            // is the whole point of the entry.
            var isMqttSourced = MQTTManager.IsMQTTSerial(input.Controller?.Serial);
            if (!isMqttSourced && !input.PublishToMQTT) yield break;

            var topic = isMqttSourced
                ? MqttTopics.ForInput(input)
                : MqttTopics.ForInputPublish(input);

            if (string.IsNullOrWhiteSpace(topic)) yield break;

            var uniqueId = MakeUniqueId(input.GUID);
            var displayName = SafeName(input.Name, fallback: topic);

            if (input.DeviceType == InputConfigItem.TYPE_BUTTON)
            {
                if (input.MomentaryButton)
                {
                    // Momentary push button: binary_sensor reflects the current physical
                    // state (1=pressed, 0=released). HA can react to the press/release
                    // edges but cannot drive the button back.
                    var payload = new JObject
                    {
                        ["name"] = displayName,
                        ["unique_id"] = uniqueId,
                        ["object_id"] = uniqueId,
                        ["state_topic"] = topic,
                        ["payload_on"] = "1",
                        ["payload_off"] = "0",
                        ["device"] = BuildDeviceBlock()
                    };
                    yield return new DiscoveryMessage
                    {
                        Topic = $"{prefix}/binary_sensor/{DeviceNodeId}/{uniqueId}/config",
                        Payload = payload.ToString(Formatting.None)
                    };
                }
                else
                {
                    // Latching toggle: expose as `switch` so HA can both observe AND drive
                    // the state. command_topic uses /set so MQTTManager can distinguish
                    // commands from the retained state echo on the parent topic (otherwise
                    // a self-publish would loop back as a command).
                    var payload = new JObject
                    {
                        ["name"] = displayName,
                        ["unique_id"] = uniqueId,
                        ["object_id"] = uniqueId,
                        ["state_topic"] = topic,
                        ["command_topic"] = $"{topic}/set",
                        ["payload_on"] = "1",
                        ["payload_off"] = "0",
                        ["state_on"] = "1",
                        ["state_off"] = "0",
                        ["optimistic"] = false,
                        ["device"] = BuildDeviceBlock()
                    };
                    yield return new DiscoveryMessage
                    {
                        Topic = $"{prefix}/switch/{DeviceNodeId}/{uniqueId}/config",
                        Payload = payload.ToString(Formatting.None)
                    };
                }
            }
            else if (input.DeviceType == InputConfigItem.TYPE_ENCODER)
            {
                // Rotary encoders have no resting state, so we expose two HA `button`
                // entities (one per direction). Pressing the HA button publishes the
                // configured payload to <topic>/inc/set or <topic>/dec/set, which
                // MQTTManager translates back into an encoder tick. The /set suffix
                // keeps HA→MF commands on a different topic than the MF→HA state pulses
                // (<topic>/inc and <topic>/dec) to prevent the publish from looping back
                // through our own subscription.
                foreach (var dir in new[] { "inc", "dec" })
                {
                    var dirUid = $"{uniqueId}_{dir}";
                    var dirPayload = new JObject
                    {
                        ["name"] = $"{displayName} ({dir})",
                        ["unique_id"] = dirUid,
                        ["object_id"] = dirUid,
                        ["command_topic"] = $"{topic}/{dir}/set",
                        ["payload_press"] = "1",
                        ["device"] = BuildDeviceBlock()
                    };
                    yield return new DiscoveryMessage
                    {
                        Topic = $"{prefix}/button/{DeviceNodeId}/{dirUid}/config",
                        Payload = dirPayload.ToString(Formatting.None)
                    };
                }
            }
            else if (input.DeviceType == InputConfigItem.TYPE_ANALOG)
            {
                var payload = new JObject
                {
                    ["name"] = displayName,
                    ["unique_id"] = uniqueId,
                    ["object_id"] = uniqueId,
                    ["state_topic"] = topic,
                    ["state_class"] = "measurement",
                    ["device"] = BuildDeviceBlock()
                };
                yield return new DiscoveryMessage
                {
                    Topic = $"{prefix}/sensor/{DeviceNodeId}/{uniqueId}/config",
                    Payload = payload.ToString(Formatting.None)
                };
            }
            // Other device types (shift register, multiplexer, NOTSET) are intentionally
            // skipped: they're either redundant with Button semantics or have no state.
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
