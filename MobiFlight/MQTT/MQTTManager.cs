using MQTTnet.Client;
using MQTTnet;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using static MobiFlight.MobiFlightButton;
using SharpDX.DirectInput;
using System.Diagnostics;
using MobiFlight.Base;

namespace MobiFlight
{
    public class MQTTManager
    {
        public static readonly string Serial = "MQTTServer";
        private static readonly string MobiFlightTopicPrefix = "mobiflight";
        public static readonly string MobiFlightRunningStateTopic = $"{MobiFlightTopicPrefix}/state/running";
        public static readonly string MobiFlightCurrentAircraftTopic = $"{MobiFlightTopicPrefix}/state/currentAircraft";

        public event Func<MqttClientConnectedEventArgs, Task> ConnectedAsync;
        public event Func<MqttClientDisconnectedEventArgs, Task> DisconnectedAsync;
        public event ButtonEventHandler OnButtonPressed;

        /// <summary>
        /// True if the underlying MQTT client is currently connected to the broker.
        /// </summary>
        public bool IsConnected => mqttClient?.IsConnected ?? false;

        /// <summary>
        /// Returns "host:port" of the broker the manager is connected (or last attempted to connect) to,
        /// or an empty string if no settings are loaded yet. Used by the UI to populate the status bar tooltip.
        /// </summary>
        public string BrokerEndpoint
        {
            get
            {
                try
                {
                    var s = MQTTServerSettings.Load();
                    return s == null ? string.Empty : $"{s.Address}:{s.Port}";
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private Dictionary<string, MQTTInput> Inputs = new Dictionary<string, MQTTInput>();

        // Reverse-direction subscriptions: topics that HA (or any controller) publishes TO
        // us so we can drive a hardware-backed input via MQTT. Keyed by full topic, e.g.
        //   "<topic>/set"     -> latching button toggle
        //   "<topic>/inc/set" -> encoder increment
        //   "<topic>/dec/set" -> encoder decrement
        // Kept separate from <see cref="Inputs"/> so the MQTT-controller flow (which uses
        // the bare topic) is unaffected and we can dispatch to the right config item with
        // the original hardware controller's serial/label preserved.
        private Dictionary<string, ReverseInput> reverseInputs = new Dictionary<string, ReverseInput>();

        private MqttFactory mqttFactory;
        private IMqttClient mqttClient;
        private readonly Dictionary<string, string> outputCache = new Dictionary<string, string>();

        // Last project handed to us via SyncInputsFromProject; needed so we can (re)publish
        // Home Assistant discovery messages every time the broker connection comes up.
        private Project currentProject;

        // Discovery config topics we have already published as retained messages on the
        // current broker. Used to send empty retained payloads (which removes the entity
        // in HA) for items that disappeared from the project on the next sync.
        private readonly HashSet<string> publishedDiscoveryTopics = new HashSet<string>();

        public MQTTManager()
        {
        }

        /// <summary>
        /// Compares the specified serial to the serial used to identify MQTT Server configurations.
        /// </summary>
        /// <param name="serial">The serial to verify.</param>
        /// <returns>True if the serial is an MQTT Server configuration.</returns>
        public static bool IsMQTTSerial(string serial)
        {
            return serial == Serial;
        }

        /// <summary>
        /// Provides the list of MQTT input events currently registered. The list is rebuilt
        /// from the active <see cref="Project"/> via <see cref="SyncInputsFromProject"/>;
        /// MQTT inputs live inside each <c>InputConfigItem</c> (DeviceName = topic,
        /// DeviceType = Button/AnalogInput), mirroring how output MQTT data is stored in
        /// <c>OutputConfigItem.MqttMessage</c>.
        /// </summary>
        /// <returns>A dictionary keyed by MQTT topic.</returns>
        public Dictionary<string, MQTTInput> GetMqttInputs()
        {
            return Inputs;
        }

        /// <summary>
        /// Adds or updates an MQTT input subscription in memory only. Used by the input
        /// wizard so that newly typed topics are subscribed to immediately without waiting
        /// for the project to be saved. The authoritative storage is the project file (.mcc):
        /// the entry will be re-derived from the corresponding InputConfigItem on the next
        /// project change.
        /// </summary>
        /// <param name="topic">The MQTT topic to subscribe to. Must be non-empty.</param>
        /// <param name="input">The input definition (Type + Label) associated with the topic.</param>
        public void AddOrUpdateInput(string topic, MQTTInput input)
        {
            if (string.IsNullOrWhiteSpace(topic) || input == null)
                return;

            if (Inputs == null)
                Inputs = new Dictionary<string, MQTTInput>();

            var isNew = !Inputs.ContainsKey(topic);
            Inputs[topic] = input;

            if (isNew && (mqttClient?.IsConnected ?? false))
            {
                // Fire-and-forget subscribe for the new topic so live editing takes effect immediately.
                _ = SubscribeToTopic(topic);
            }
        }

        /// <summary>
        /// Rebuilds the in-memory MQTT input list from the supplied project. Walks every
        /// <see cref="InputConfigItem"/> across all configuration files; an item belongs to
        /// MQTT when its controller serial matches <see cref="Serial"/>. The item's
        /// <c>DeviceName</c> is the topic and its <c>DeviceType</c> determines the MQTT input
        /// type (Button or AnalogInput).
        ///
        /// If the client is currently connected, subscriptions are reconciled: topics that
        /// disappeared from the project are unsubscribed and freshly added topics are
        /// subscribed.
        /// </summary>
        /// <param name="project">The project whose input configs should drive the MQTT subscriptions. Null is treated as an empty project.</param>
        public void SyncInputsFromProject(Project project)
        {
            currentProject = project;

            var newInputs = new Dictionary<string, MQTTInput>();
            var newReverse = new Dictionary<string, ReverseInput>();
            if (project?.ConfigFiles != null)
            {
                foreach (var file in project.ConfigFiles)
                {
                    if (file?.ConfigItems == null) continue;
                    foreach (var item in file.ConfigItems.OfType<InputConfigItem>())
                    {
                        // MQTT-controller items: forward subscription to the user-defined topic.
                        if (IsMQTTSerial(item.Controller?.Serial))
                        {
                            // Topic field (DeviceName) may be empty: fall back to a topic
                            // auto-derived from the config Name (Plan B – default-fill).
                            var topic = MqttTopics.ForInput(item);
                            if (string.IsNullOrWhiteSpace(topic)) continue;

                            var deviceType = MapInputDeviceType(item.DeviceType);
                            if (!deviceType.HasValue) continue;

                            // Last write wins on duplicate topics; the same topic shared by
                            // multiple input configs is intentionally collapsed to a single
                            // subscription.
                            newInputs[topic] = new MQTTInput
                            {
                                Type = deviceType.Value,
                                Label = topic
                            };
                            continue;
                        }

                        // Hardware items: register reverse-direction command topics so HA
                        // (or any external system) can drive the input back through us.
                        // Only do this when the user opted the input in to MQTT publishing
                        // - otherwise no HA entity exists for the topic in the first place.
                        if (!item.Active) continue;
                        if (!item.PublishToMQTT) continue;
                        var pubTopic = MqttTopics.ForInputPublish(item);
                        if (string.IsNullOrWhiteSpace(pubTopic)) continue;

                        if (item.DeviceType == InputConfigItem.TYPE_BUTTON && !item.MomentaryButton)
                        {
                            newReverse[$"{pubTopic}/set"] = new ReverseInput
                            {
                                Kind = ReverseInputKind.ButtonSet,
                                Topic = $"{pubTopic}/set",
                                Config = item
                            };
                        }
                        else if (item.DeviceType == InputConfigItem.TYPE_ENCODER)
                        {
                            newReverse[$"{pubTopic}/inc/set"] = new ReverseInput
                            {
                                Kind = ReverseInputKind.EncoderInc,
                                Topic = $"{pubTopic}/inc/set",
                                Config = item
                            };
                            newReverse[$"{pubTopic}/dec/set"] = new ReverseInput
                            {
                                Kind = ReverseInputKind.EncoderDec,
                                Topic = $"{pubTopic}/dec/set",
                                Config = item
                            };
                        }
                    }
                }
            }

            // Reconcile both maps in a single sub/unsub pass so we don't churn the broker.
            var oldTopics = new HashSet<string>((Inputs?.Keys ?? Enumerable.Empty<string>())
                .Concat(reverseInputs?.Keys ?? Enumerable.Empty<string>()));
            var newTopics = new HashSet<string>(newInputs.Keys.Concat(newReverse.Keys));
            Inputs = newInputs;
            reverseInputs = newReverse;

            Log.Instance.log($"MQTT: Synced {Inputs.Count} input topic(s) and {reverseInputs.Count} reverse-command topic(s) from project.", LogSeverity.Debug);

            if (mqttClient?.IsConnected ?? false)
            {
                var toUnsubscribe = oldTopics.Where(t => !newTopics.Contains(t)).ToList();
                var toSubscribe = newTopics.Where(t => !oldTopics.Contains(t)).ToList();

                if (toUnsubscribe.Count > 0 || toSubscribe.Count > 0)
                {
                    _ = ReconcileSubscriptionsAsync(toUnsubscribe, toSubscribe);
                }
            }

            // The set of MQTT-bound config items can change with every project edit, so
            // republish (or retract) Home Assistant discovery records to keep HA in sync.
            if (mqttClient?.IsConnected ?? false)
            {
                _ = PublishHomeAssistantDiscoveryAsync();
            }
        }

        /// <summary>
        /// Maps an <see cref="InputConfigItem.DeviceType"/> string constant to a
        /// <see cref="DeviceType"/> understood by the MQTT message dispatcher. Only the
        /// types currently supported over MQTT (Button and AnalogInput) yield a value;
        /// other input device types (encoder, shift register, multiplexer) intentionally
        /// return null so they are skipped.
        /// </summary>
        private static DeviceType? MapInputDeviceType(string deviceType)
        {
            if (deviceType == InputConfigItem.TYPE_BUTTON) return DeviceType.Button;
            if (deviceType == InputConfigItem.TYPE_ANALOG) return DeviceType.AnalogInput;
            return null;
        }

        private async Task ReconcileSubscriptionsAsync(List<string> toUnsubscribe, List<string> toSubscribe)
        {
            try
            {
                if (toUnsubscribe.Count > 0)
                {
                    var unsubOptions = mqttFactory.CreateUnsubscribeOptionsBuilder();
                    foreach (var topic in toUnsubscribe)
                    {
                        unsubOptions.WithTopicFilter(topic);
                        Log.Instance.log($"MQTT: Unsubscribing from {topic} (no longer in project).", LogSeverity.Debug);
                    }
                    await mqttClient.UnsubscribeAsync(unsubOptions.Build(), CancellationToken.None);
                }

                if (toSubscribe.Count > 0)
                {
                    var subOptions = mqttFactory.CreateSubscribeOptionsBuilder();
                    foreach (var topic in toSubscribe)
                    {
                        subOptions.WithTopicFilter(f => { f.WithTopic(topic); });
                        Log.Instance.log($"MQTT: Subscribing to {topic} (newly added in project).", LogSeverity.Debug);
                    }
                    await mqttClient.SubscribeAsync(subOptions.Build(), CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Failed to reconcile subscriptions: {ex.Message}", LogSeverity.Error);
            }
        }

        private async Task SubscribeToTopic(string topic)
        {
            try
            {
                var options = mqttFactory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => { f.WithTopic(topic); })
                    .Build();
                await mqttClient.SubscribeAsync(options, CancellationToken.None);
                Log.Instance.log($"MQTT: Subscribed to newly added topic {topic}", LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Failed to subscribe to {topic}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Connects to an MQTT server using the settings saved in the app config.
        /// </summary>
        /// <returns>A task.</returns>
        public async Task Connect()
        {
            if (mqttClient?.IsConnected ?? false)
                return;

            var settings = MQTTServerSettings.Load();

            mqttFactory = new MqttFactory();
            mqttClient = mqttFactory.CreateMqttClient();

            var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(settings.Address, settings.Port);

            // Only use username/password authentication if the username setting is set to something.
            if (!string.IsNullOrWhiteSpace(settings.Username))
            {
                var unsecurePassword = settings.GetUnsecurePassword();
                mqttClientOptions.WithCredentials(settings.Username, unsecurePassword);
                unsecurePassword = "";
            }

            // Only use TLS and validate the certificate if requested.
            if (settings.EncryptConnection)
            {
                if (settings.ValidateCertificate)
                {
                    mqttClientOptions.WithTlsOptions(
                        o =>
                        {
                            o.UseTls();
                        });
                }
                else
                {
                    // From https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Connection_Samples.cs
                    mqttClientOptions.WithTlsOptions(
                    o =>
                    {
                        o.UseTls();
                        // The used public broker sometimes has invalid certificates. This sample accepts all
                        // certificates. This should not be used in live environments.
                        o.WithCertificateValidationHandler(_ => true);
                    });
                }
            }

            // Add incoming message handler prior to connecting so queued events are processed.
            mqttClient.ApplicationMessageReceivedAsync += MqttClient_ApplicationMessageReceivedAsync;
            mqttClient.ConnectedAsync += MqttClient_ConnectedAsync;
            mqttClient.DisconnectedAsync += MqttClient_DisconnectedAsync;

            // This will throw an exception if the server is not available.
            // The result from this message returns additional data which was sent 
            // from the server. Please refer to the MQTT protocol specification for details.
            try
            {
                await mqttClient.ConnectAsync(mqttClientOptions.Build(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Unable to connect to {settings.Address}:{settings.Port}: {ex.Message}", LogSeverity.Error);
                return;
            }

            Log.Instance.log($"MQTT: Connected to {settings.Address}:{settings.Port}.", LogSeverity.Info);
        }

        /// <summary>
        /// Subscribes to the MQTT topics for each registered input event (topics are sourced
        /// from the active project via <see cref="SyncInputsFromProject"/>).
        /// </summary>
        /// <returns>A task that completes once all topics are subscribed to.</returns>
        private async Task SubscribeToInputs()
        {
            if (!mqttClient?.IsConnected ?? true)
                return;

            try
            {
                var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder();
                var subscribed = false;

                foreach (var input in Inputs)
                {
                    Log.Instance.log($"MQTT: Subscribing to {input.Key}", LogSeverity.Debug);
                    mqttSubscribeOptions.WithTopicFilter(f => { f.WithTopic(input.Key); });
                    subscribed = true;
                }

                // Reverse-direction command topics live in their own dictionary but share
                // the same subscription channel - the dispatcher distinguishes by looking
                // up the incoming topic in both maps.
                foreach (var rev in reverseInputs)
                {
                    Log.Instance.log($"MQTT: Subscribing to reverse-command topic {rev.Key}", LogSeverity.Debug);
                    mqttSubscribeOptions.WithTopicFilter(f => { f.WithTopic(rev.Key); });
                    subscribed = true;
                }

                if (!subscribed) return;

                var response = await mqttClient.SubscribeAsync(mqttSubscribeOptions.Build(), CancellationToken.None);

                Log.Instance.log($"MQTT: Subscribing to all input topics complete.", LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Error subscribing to topics. {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Event handler for when the MQTT client completes its connection. Fires a ConnectedAsync
        /// event to any registered listeners, then subscribes to the MQTT input topics.
        /// </summary>
        /// <param name="arg">The connected event arguments</param>
        /// <returns>A task that completes after the topics are subscribed to.</returns>
        private async Task MqttClient_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            ConnectedAsync?.Invoke(arg);
            await Publish(MQTTManager.MobiFlightRunningStateTopic, "1");

            await SubscribeToInputs();

            // Newly connected (or reconnected) broker has no memory of any previous
            // discovery messages we sent in this session, so always (re)publish them.
            publishedDiscoveryTopics.Clear();
            await PublishHomeAssistantDiscoveryAsync();
        }

        /// <summary>
        /// Event handler for when the MQTT client connection is dropped (network error, broker
        /// shutdown, explicit disconnect, etc.). Forwards a DisconnectedAsync event so the UI
        /// (e.g. the bottom status bar Home Assistant indicator) can react.
        /// </summary>
        private Task MqttClient_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            DisconnectedAsync?.Invoke(arg);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Event handler when MQTT messages are received on subscribed topics. Processes the retrieved message,
        /// converts it to a MobiFlight InputEventArgs object, then fires the OnbuttonPressed event.
        /// </summary>
        /// <param name="arg">The received message information</param>
        /// <returns>A task that completes once the received message is processed.</returns>
        private Task MqttClient_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            var topic = arg.ApplicationMessage.Topic;
            var payloadString = System.Text.Encoding.UTF8.GetString(arg.ApplicationMessage.PayloadSegment.ToArray());

            // Reverse-direction commands (HA -> hardware input) take precedence: they live
            // on /set-suffixed topics that are distinct from the parent state topic the
            // hardware input publishes to, so there's no overlap with the Inputs map.
            if (reverseInputs != null && reverseInputs.TryGetValue(topic, out var reverse))
            {
                DispatchReverseCommand(reverse, payloadString);
                return Task.CompletedTask;
            }

            if (!Inputs.TryGetValue(topic, out var input) || input == null)
            {
                Log.Instance.log($"MQTT: Received an incoming message for {topic} but it's not in the list of topics being watched. This should never happen.", LogSeverity.Error);
                return Task.CompletedTask;
            }

            // Absolute nonsense to parse the incoming message value as a number.
            if (int.TryParse(payloadString, out int value))
            {
                // All device types share these three properties so set them first.
                var eventArgs = new InputEventArgs
                {
                    DeviceLabel = input.Label,
                    Serial = Serial,
                    DeviceId = input.Label,
                };

                // Set specific type and value properties based on the input's type stored on the originating InputConfigItem.
                if (input.Type == DeviceType.Button)
                {
                    eventArgs.Type = DeviceType.Button;
                    eventArgs.Value = value == 0 ? (int)InputEvent.RELEASE : (int)InputEvent.PRESS;                       
                }
                else if (input.Type == DeviceType.AnalogInput)
                {
                    eventArgs.Type = DeviceType.AnalogInput;
                    eventArgs.Value = value;
                }
                else
                {
                    Log.Instance.log($"MQTT: Received incoming message {topic} for a type that isn't understhood. This should never happen.", LogSeverity.Error);
                    return Task.CompletedTask;
                }

                Log.Instance.log($"MQTT: Received incoming message: {topic} {value}", LogSeverity.Debug);
                // sender = this so ExecutionManager can detect the loopback origin and
                // avoid republishing the same event back to MQTT.
                OnButtonPressed?.Invoke(this, eventArgs);
            }
            else
            {
                Log.Instance.log($"MQTT: Unable to parse {payloadString} from {topic} as a number.", LogSeverity.Error);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Translates a reverse-direction command (HA -> hardware) into the equivalent
        /// MobiFlight input event(s) so the configured InputAction fires exactly as if
        /// the physical control had been actuated. Latching button toggles emit a press
        /// followed by a release; encoder inc/dec emit a single tick in the matching
        /// direction.
        /// </summary>
        private void DispatchReverseCommand(ReverseInput reverse, string payload)
        {
            if (reverse?.Config == null) return;

            // For the latching switch we only act on "1"; an explicit "0" would otherwise
            // cause a phantom release-without-press when HA echoes the off state. The
            // InputAction itself is responsible for whatever toggling the user wants.
            switch (reverse.Kind)
            {
                case ReverseInputKind.ButtonSet:
                    var pressed = payload == "1" || string.Equals(payload, "true", StringComparison.OrdinalIgnoreCase);
                    EmitInputEvent(reverse.Config, DeviceType.Button,
                        pressed ? (int)InputEvent.PRESS : (int)InputEvent.RELEASE);
                    break;

                case ReverseInputKind.EncoderInc:
                    EmitInputEvent(reverse.Config, DeviceType.Encoder,
                        (int)MobiFlightEncoder.InputEvent.RIGHT);
                    break;

                case ReverseInputKind.EncoderDec:
                    EmitInputEvent(reverse.Config, DeviceType.Encoder,
                        (int)MobiFlightEncoder.InputEvent.LEFT);
                    break;
            }
        }

        private void EmitInputEvent(InputConfigItem cfg, DeviceType type, int value)
        {
            var eventArgs = new InputEventArgs
            {
                // Use the hardware controller's serial/label so ExecutionManager's
                // inputCache lookup hits the same row a physical actuation would.
                Serial = cfg.Controller?.Serial ?? string.Empty,
                DeviceLabel = cfg.DeviceName,
                DeviceId = cfg.DeviceName,
                Type = type,
                Value = value
            };

            Log.Instance.log($"MQTT: Reverse-dispatch {type} value={value} for cfg \"{cfg.Name}\".", LogSeverity.Debug);
            // sender = this so ExecutionManager skips the publish-back to prevent loops.
            OnButtonPressed?.Invoke(this, eventArgs);
        }

        public async void PublishCurrentAircraft(object _, string e)
        {
            await Publish(MQTTManager.MobiFlightCurrentAircraftTopic, e);
        }

        /// <summary>
        /// Publishes the value of a hardware input event (button press/release, encoder
        /// rotation, analog value) to MQTT so external systems (e.g. Home Assistant) can
        /// react to physical panel changes.
        ///
        /// Topic is derived from the config Name via <see cref="MqttTopics.ForInputPublish"/>
        /// so it stays in lock-step with the Home Assistant discovery state_topic. Buttons
        /// publish as retained "1"/"0" so HA picks up the current state on (re)connect;
        /// encoder ticks publish to <c>&lt;topic&gt;/inc</c> or <c>&lt;topic&gt;/dec</c> as
        /// non-retained "1" pulses (rotation has no persistent state); analog values publish
        /// the raw numeric value retained.
        /// </summary>
        /// <param name="cfg">The input config item that produced the event.</param>
        /// <param name="e">The originating hardware event.</param>
        public async Task PublishInputEvent(InputConfigItem cfg, InputEventArgs e)
        {
            if (cfg == null || e == null) return;
            if (!mqttClient?.IsConnected ?? true) return;

            // Per-input opt-in: only publish hardware events for inputs the user has
            // explicitly ticked "Publish to MQTT" on, so we don't spam the broker (or
            // HA dashboard) with every active input by default.
            if (!cfg.PublishToMQTT) return;

            var topic = MqttTopics.ForInputPublish(cfg);
            if (string.IsNullOrWhiteSpace(topic)) return;

            try
            {
                if (e.Type == DeviceType.Button)
                {
                    // Press = 1, release = 0. Retained so a (re)connecting subscriber sees
                    // the current physical state without having to wait for the next event.
                    var payload = e.Value == (int)InputEvent.RELEASE ? "0" : "1";
                    await PublishRetainedRaw(topic, payload);
                }
                else if (e.Type == DeviceType.Encoder)
                {
                    // Encoder rotation has no resting state - fire a single "1" pulse on
                    // the appropriate sub-topic so HA can trigger automations per tick.
                    var dir = (e.Value == (int)MobiFlightEncoder.InputEvent.LEFT ||
                               e.Value == (int)MobiFlightEncoder.InputEvent.LEFT_FAST)
                                ? "dec" : "inc";
                    await Publish($"{topic}/{dir}", "1");
                }
                else if (e.Type == DeviceType.AnalogInput)
                {
                    await PublishRetainedRaw(topic, e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                // Other input types (shift register, multiplexer) deliberately not published -
                // they share the underlying button semantics already handled above.
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: PublishInputEvent failed for {topic}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Publishes data to an MQTT topic, caching payload values to avoid sending the same
        /// message repeatedly when the data doesn't change.
        /// </summary>
        /// <param name="topic">The topic to publish to.</param>
        /// <param name="payload">The paylod to send.</param>
        /// <returns>A task that completes once the payload is published to the topic.</returns>
        public async Task Publish(string topic, string payload)
        {
            if (!mqttClient?.IsConnected ?? true)
                return;

            // Don't spam MQTT server if the payload is the same as last time for the topic.
            if (outputCache.ContainsKey(topic) && outputCache[topic] == payload)
                return;

            // Don't send anything if the topic is empty.
            if (String.IsNullOrEmpty(topic))
            {
                Log.Instance.log($"MQTT: Received a blank topic, not sending {payload}", LogSeverity.Warn);
                return;
            }

            try
            {
                var applicationMessage = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload);

                await mqttClient.PublishAsync(applicationMessage.Build(), CancellationToken.None);
                outputCache[topic] = payload;

                Log.Instance.log($"MQTT: Published {payload} to {topic}.", LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Unable to publish {payload} to {topic}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Publishes a payload as a retained MQTT message, bypassing the deduplication
        /// cache used by <see cref="Publish"/>. Required for Home Assistant Discovery
        /// configuration messages: HA only learns about an entity when it sees a retained
        /// message on the discovery topic, and an empty retained payload removes the entity.
        /// </summary>
        private async Task PublishRetainedRaw(string topic, string payload)
        {
            if (!mqttClient?.IsConnected ?? true) return;
            if (string.IsNullOrEmpty(topic)) return;

            try
            {
                var builder = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload ?? string.Empty)
                    .WithRetainFlag(true);

                await mqttClient.PublishAsync(builder.Build(), CancellationToken.None);
                Log.Instance.log($"MQTT: Published retained {(string.IsNullOrEmpty(payload) ? "(empty)" : "discovery")} message to {topic}.", LogSeverity.Debug);
            }
            catch (Exception ex)
            {
                Log.Instance.log($"MQTT: Unable to publish retained message to {topic}: {ex.Message}", LogSeverity.Error);
            }
        }

        /// <summary>
        /// Walks the current project and publishes a Home Assistant MQTT Discovery config
        /// message (retained) for every MQTT-bound output and input. Items that were
        /// previously announced but are no longer present receive an empty retained payload
        /// so HA removes the corresponding entity.
        /// </summary>
        private async Task PublishHomeAssistantDiscoveryAsync()
        {
            if (!mqttClient?.IsConnected ?? true) return;

            var settings = MQTTServerSettings.Load();
            if (!settings.HomeAssistantDiscoveryEnabled)
            {
                // If discovery was previously enabled and we already announced entities,
                // proactively retract them so HA doesn't keep stale entries around.
                if (publishedDiscoveryTopics.Count > 0)
                {
                    foreach (var staleTopic in publishedDiscoveryTopics.ToList())
                    {
                        await PublishRetainedRaw(staleTopic, string.Empty);
                    }
                    publishedDiscoveryTopics.Clear();
                }
                return;
            }

            var messages = HomeAssistantDiscovery.Build(currentProject, settings.HomeAssistantDiscoveryPrefix);
            var newTopics = new HashSet<string>(messages.Select(m => m.Topic));

            // Retract any entities that disappeared since the last publish.
            foreach (var stale in publishedDiscoveryTopics.Where(t => !newTopics.Contains(t)).ToList())
            {
                await PublishRetainedRaw(stale, string.Empty);
            }

            // Publish (or refresh) the current set.
            foreach (var msg in messages)
            {
                await PublishRetainedRaw(msg.Topic, msg.Payload);
            }

            publishedDiscoveryTopics.Clear();
            foreach (var t in newTopics) publishedDiscoveryTopics.Add(t);

            Log.Instance.log($"MQTT: Published {messages.Count} Home Assistant discovery message(s).", LogSeverity.Info);
        }

        /// <summary>
        /// Disconnects from the MQTT server.
        /// </summary>
        /// <returns>A task.</returns>
        public async Task Disconnect()
        {
            if (!mqttClient?.IsConnected ?? true)
                return;

            await Publish(MQTTManager.MobiFlightRunningStateTopic, "0");

            // Send a clean disconnect to the server by calling _DisconnectAsync_. Without this the TCP connection
            // gets dropped and the server will handle this as a non clean disconnect (see MQTT spec for details).
            var mqttClientDisconnectOptions = mqttFactory.CreateClientDisconnectOptionsBuilder();

            await mqttClient.DisconnectAsync(mqttClientDisconnectOptions.Build(), CancellationToken.None);

            Log.Instance.log($"MQTT: Disconnected from server.", LogSeverity.Info);
        }
    }
}