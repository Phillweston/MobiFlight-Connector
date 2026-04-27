using MobiFlight.Properties;
using System;
using System.IO;
using System.Security;
using System.Text;
using System.Xml.Serialization;

namespace MobiFlight
{
    public class MQTTServerSettings
    {
        /// <summary>
        /// Default Home Assistant MQTT discovery prefix (matches HA's own default).
        /// </summary>
        public const string DefaultHomeAssistantDiscoveryPrefix = "homeassistant";

        public string Address { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public bool EncryptConnection { get; set; }
        public bool ValidateCertificate { get; set; }

        /// <summary>
        /// When true, MobiFlight publishes Home Assistant MQTT Discovery configuration
        /// messages so that every MQTT-bound output / input becomes a HA entity
        /// automatically. See https://www.home-assistant.io/integrations/mqtt/#mqtt-discovery
        /// </summary>
        public bool HomeAssistantDiscoveryEnabled { get; set; } = true;

        /// <summary>
        /// Topic prefix used when publishing Home Assistant MQTT Discovery messages.
        /// HA listens on "homeassistant/" by default.
        /// </summary>
        public string HomeAssistantDiscoveryPrefix { get; set; } = DefaultHomeAssistantDiscoveryPrefix;

        public byte[] EncryptedPassword { get; set; }

        /// <summary>
        /// Loads the settings from the app Settings object.
        /// </summary>
        /// <returns>The MQTT server settings</returns>
        public static MQTTServerSettings Load()
        {
            var config = Settings.Default.MqttServerConfig;

            // Treat null / empty / whitespace-only / non-XML content as "no config yet"
            // and fall back to defaults. This guards against a corrupted or partially
            // written setting that would otherwise throw
            // System.Xml.XmlException: Data at the root level is invalid.
            if (String.IsNullOrWhiteSpace(config) || !config.TrimStart().StartsWith("<"))
            {
                return new MQTTServerSettings()
                {
                    Port = 1883
                };
            }

            try
            {
                var serializer = new XmlSerializer(typeof(MQTTServerSettings));
                using (var reader = new StringReader(config))
                {
                    var loaded = (MQTTServerSettings)serializer.Deserialize(reader);
                    // Backwards compatibility: older configs don't have a discovery prefix
                    // serialised, which would round-trip to null. Fall back to the HA default.
                    if (string.IsNullOrWhiteSpace(loaded.HomeAssistantDiscoveryPrefix))
                    {
                        loaded.HomeAssistantDiscoveryPrefix = DefaultHomeAssistantDiscoveryPrefix;
                    }
                    return loaded;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Xml.XmlException)
            {
                // Stored config is corrupted; log and fall back to defaults instead of crashing the UI.
                Log.Instance.log($"Failed to deserialize MQTT server settings, using defaults. {ex.Message}", LogSeverity.Warn);
                return new MQTTServerSettings()
                {
                    Port = 1883
                };
            }
        }

        public SecureString GetPassword()
        {
            var password = new SecureString();
            password.AppendProtected(EncryptedPassword, null, SecureStringExtensions.DataProtectionScope.CurrentUser);
            password.MakeReadOnly();
            return password;
        }

        public string GetUnsecurePassword()
        {
            // This takes the SecureString version of the password and converts it to plain text. Yes, this largely defeats the purpose
            // of using SecureString. Passwords in WinForms apps are terrible so we're doing the best we can. This approach comes from
            // https://stackoverflow.com/questions/818704/how-to-convert-securestring-to-system-string
            return new System.Net.NetworkCredential(string.Empty, GetPassword()).Password; ;
        }

        public void SetPassword(SecureString password)
        {
            EncryptedPassword = password.Protect(null, SecureStringExtensions.DataProtectionScope.CurrentUser);
        }

        /// <summary>
        /// Saves the MQTT settings to the app Settings object.
        /// </summary>
        public void Save()
        {
            var serializer = new XmlSerializer(typeof(MQTTServerSettings));
            var builder = new StringBuilder();
            var writer = new StringWriter(builder);
            serializer.Serialize(writer, this);
            var s = writer.ToString();
            Settings.Default.MqttServerConfig = s;
        }
    }
}