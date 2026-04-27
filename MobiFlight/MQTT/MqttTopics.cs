using System.Text;

namespace MobiFlight
{
    /// <summary>
    /// Resolves the effective MQTT topic for an output or input configuration item.
    ///
    /// Behaviour (Plan B – default-fill):
    ///   • If the user explicitly typed a topic, that value wins.
    ///   • If the topic field is empty, fall back to a topic auto-derived from the
    ///     config item's <c>Name</c> ("<see cref="AutoTopicPrefix"/><slug>").
    ///   • If both the topic field and the name are empty, returns <c>null</c> so
    ///     callers can skip publishing/subscribing for that item.
    ///
    /// Centralising this lets the publisher (<see cref="ExecutionManager"/>), the
    /// subscriber (<see cref="MQTTManager.SyncInputsFromProject"/>) and the Home
    /// Assistant Discovery announcer all agree on the same effective topic, so a
    /// renamed config item stays consistent end-to-end without the user having to
    /// edit the topic field separately.
    /// </summary>
    internal static class MqttTopics
    {
        /// <summary>Prefix used when auto-deriving a topic from the config name.</summary>
        public const string AutoTopicPrefix = "mobiflight/";

        /// <summary>
        /// Returns the effective MQTT topic to publish to for the given output config item,
        /// or <c>null</c> if neither an explicit topic nor a usable name exists.
        /// </summary>
        public static string ForOutput(OutputConfigItem item)
        {
            if (item == null) return null;
            var explicitTopic = item.MqttMessage?.Topic;
            if (!string.IsNullOrWhiteSpace(explicitTopic)) return explicitTopic.Trim();
            return Auto(item.Name);
        }

        /// <summary>
        /// Returns the effective MQTT topic to subscribe to for the given input config item,
        /// or <c>null</c> if neither an explicit topic nor a usable name exists. The MQTT
        /// "topic" for an input lives in <see cref="InputConfigItem.DeviceName"/>.
        /// </summary>
        public static string ForInput(InputConfigItem item)
        {
            if (item == null) return null;
            var explicitTopic = item.DeviceName;
            if (!string.IsNullOrWhiteSpace(explicitTopic) &&
                explicitTopic != InputConfigItem.TYPE_NOTSET)
            {
                return explicitTopic.Trim();
            }
            return Auto(item.Name);
        }

        /// <summary>
        /// Returns the auto-derived topic for the given config name, or <c>null</c> if
        /// the name is empty/produces no usable slug. Exposed so the UI can preview the
        /// fallback topic (e.g. as a textbox cue banner) without having to instantiate
        /// a config item.
        /// </summary>
        public static string AutoFromName(string name) => Auto(name);

        /// <summary>
        /// Slugifies a config item name into a safe MQTT topic segment. Spaces become
        /// underscores; letters/digits/<c>-</c>/<c>_</c>/<c>/</c> are kept (lower-cased);
        /// everything else is dropped. Returns <c>null</c> if nothing meaningful remains.
        /// </summary>
        private static string Auto(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '/')
                {
                    sb.Append(ch);
                }
                else if (char.IsWhiteSpace(ch))
                {
                    sb.Append('_');
                }
                // Anything else (punctuation, MQTT-illegal '+' '#') is intentionally dropped.
            }

            if (sb.Length == 0) return null;
            return AutoTopicPrefix + sb.ToString();
        }
    }
}
