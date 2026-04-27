using MobiFlight;
using System;
using System.Security;
using System.Windows.Forms;

namespace MobiFlight.UI.Panels.Settings
{
    public partial class MqttServerSettingsPanel : UserControl
    {
        private MQTTServerSettings settings;
        private bool passwordChanged = false;

        public MqttServerSettingsPanel()
        {
            InitializeComponent();
        }

        public void LoadSettings()
        {
            settings = MQTTServerSettings.Load();

            addressTextBox.Text = settings.Address;
            encryptConectionCheckbox.Checked = settings.EncryptConnection;
            passwordTextBox.Text = "****";
            portTextBox.Text = settings.Port.ToString();
            usernameTextBox.Text = settings.Username;
            validateCertificateCheckbox.Checked = settings.ValidateCertificate;
            validateCertificateCheckbox.Enabled = encryptConectionCheckbox.Checked;

            haDiscoveryEnabledCheckbox.Checked = settings.HomeAssistantDiscoveryEnabled;
            haDiscoveryPrefixTextBox.Text = settings.HomeAssistantDiscoveryPrefix;
            haDiscoveryPrefixTextBox.Enabled = haDiscoveryEnabledCheckbox.Checked;

            // After setting the password text box to a placeholder value register for TextChanged events
            // so we can track whether the user changed the password and it needs to be saved after.
            this.passwordTextBox.TextChanged += new System.EventHandler(this.passwordTextBox_TextChanged);
        }

        public void SaveSettings()
        {
            settings.Address = addressTextBox.Text;
            settings.EncryptConnection = encryptConectionCheckbox.Checked;
            // Read the value from the textbox – previously this read settings.Port back
            // into itself, silently discarding any port edit the user made.
            if (int.TryParse(portTextBox.Text, out var port)) settings.Port = port;
            settings.Username = usernameTextBox.Text;
            settings.ValidateCertificate = validateCertificateCheckbox.Checked;

            settings.HomeAssistantDiscoveryEnabled = haDiscoveryEnabledCheckbox.Checked;
            var prefix = haDiscoveryPrefixTextBox.Text?.Trim();
            settings.HomeAssistantDiscoveryPrefix = string.IsNullOrEmpty(prefix)
                ? MQTTServerSettings.DefaultHomeAssistantDiscoveryPrefix
                : prefix;

            if (passwordChanged)
            {
                var securePassword = new SecureString();
                Array.ForEach(passwordTextBox.Text.ToCharArray(), securePassword.AppendChar);
                passwordTextBox.Text = ""; // Clear the password from the UI as soon as possible
                settings.SetPassword(securePassword);
            }

            settings.Save();
        }

        //Only allow numbers in the port text box.
        private void portTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        private void passwordTextBox_TextChanged(object sender, EventArgs e)
        {
            passwordChanged = true;
        }

        // Standard MQTT ports per IANA: 1883 plaintext, 8883 over TLS.
        private const int DefaultPlainPort = 1883;
        private const int DefaultTlsPort = 8883;

        private void encryptConectionCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            validateCertificateCheckbox.Enabled = encryptConectionCheckbox.Checked;

            // Auto-toggle the port between the two IANA defaults so users get the right
            // one out of the box. Only do this when the current value is exactly one of
            // the two well-known defaults; if the user typed a custom port (e.g. 1884 or
            // a corporate proxy port) we leave it alone so we don't clobber their input.
            if (!int.TryParse(portTextBox.Text, out var current)) return;
            if (encryptConectionCheckbox.Checked && current == DefaultPlainPort)
            {
                portTextBox.Text = DefaultTlsPort.ToString();
            }
            else if (!encryptConectionCheckbox.Checked && current == DefaultTlsPort)
            {
                portTextBox.Text = DefaultPlainPort.ToString();
            }
        }

        private void haDiscoveryEnabledCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            haDiscoveryPrefixTextBox.Enabled = haDiscoveryEnabledCheckbox.Checked;
        }
    }
}