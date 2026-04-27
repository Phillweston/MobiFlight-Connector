namespace MobiFlight
{
    /// <summary>
    /// Tracks a topic on which an external system (typically Home Assistant) publishes a
    /// command that should drive a hardware-backed <see cref="InputConfigItem"/> as if the
    /// physical control had been actuated.
    ///
    /// Lives in <see cref="MQTTManager.reverseInputs"/>; one entry per command sub-topic
    /// (e.g. <c>&lt;topic&gt;/set</c>, <c>&lt;topic&gt;/inc/set</c>,
    /// <c>&lt;topic&gt;/dec/set</c>). The original config item is captured so dispatch can
    /// surface the hardware controller's serial/label to <c>OnButtonPressed</c> subscribers
    /// and hit the same row in the input cache that a physical actuation would.
    /// </summary>
    internal class ReverseInput
    {
        /// <summary>
        /// What kind of action a "1" payload on <see cref="Topic"/> represents.
        /// </summary>
        public ReverseInputKind Kind { get; set; }

        /// <summary>
        /// The full MQTT topic this entry was registered for. Stored for logging.
        /// </summary>
        public string Topic { get; set; }

        /// <summary>
        /// The hardware-backed input config to fire. Held by reference so the controller
        /// serial / device name / GUID stay in sync if the user edits the config.
        /// </summary>
        public InputConfigItem Config { get; set; }
    }

    internal enum ReverseInputKind
    {
        /// <summary>Latching toggle ("1" = press+release pulse, "0" = ignored).</summary>
        ButtonSet,
        /// <summary>Encoder one-tick clockwise.</summary>
        EncoderInc,
        /// <summary>Encoder one-tick counter-clockwise.</summary>
        EncoderDec,
    }
}
