namespace MobiFlight.UI.Panels.Settings
{
    partial class MqttServerSettingsPanel
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.addressTextBox = new System.Windows.Forms.TextBox();
            this.portTextBox = new System.Windows.Forms.TextBox();
            this.usernameTextBox = new System.Windows.Forms.TextBox();
            this.passwordTextBox = new System.Windows.Forms.MaskedTextBox();
            this.encryptConectionCheckbox = new System.Windows.Forms.CheckBox();
            this.validateCertificateCheckbox = new System.Windows.Forms.CheckBox();
            this.haDiscoveryGroupBox = new System.Windows.Forms.GroupBox();
            this.haDiscoveryEnabledCheckbox = new System.Windows.Forms.CheckBox();
            this.haDiscoveryPrefixLabel = new System.Windows.Forms.Label();
            this.haDiscoveryPrefixTextBox = new System.Windows.Forms.TextBox();
            this.haDiscoveryGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(7, 13);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(32, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Host:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 40);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(29, 13);
            this.label2.TabIndex = 1;
            this.label2.Text = "Port:";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(7, 67);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(58, 13);
            this.label3.TabIndex = 2;
            this.label3.Text = "Username:";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(7, 94);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(56, 13);
            this.label4.TabIndex = 3;
            this.label4.Text = "Password:";
            // 
            // addressTextBox
            // 
            this.addressTextBox.Location = new System.Drawing.Point(68, 10);
            this.addressTextBox.Name = "addressTextBox";
            this.addressTextBox.Size = new System.Drawing.Size(100, 20);
            this.addressTextBox.TabIndex = 4;
            // 
            // portTextBox
            // 
            this.portTextBox.Location = new System.Drawing.Point(68, 37);
            this.portTextBox.Name = "portTextBox";
            this.portTextBox.Size = new System.Drawing.Size(100, 20);
            this.portTextBox.TabIndex = 5;
            this.portTextBox.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.portTextBox_KeyPress);
            // 
            // usernameTextBox
            // 
            this.usernameTextBox.Location = new System.Drawing.Point(68, 64);
            this.usernameTextBox.Name = "usernameTextBox";
            this.usernameTextBox.Size = new System.Drawing.Size(100, 20);
            this.usernameTextBox.TabIndex = 6;
            // 
            // passwordTextBox
            // 
            this.passwordTextBox.Location = new System.Drawing.Point(68, 91);
            this.passwordTextBox.Name = "passwordTextBox";
            this.passwordTextBox.PasswordChar = '*';
            this.passwordTextBox.Size = new System.Drawing.Size(100, 20);
            this.passwordTextBox.TabIndex = 7;
            this.passwordTextBox.UseSystemPasswordChar = true;
            // 
            // encryptConectionCheckbox
            // 
            this.encryptConectionCheckbox.AutoSize = true;
            this.encryptConectionCheckbox.Location = new System.Drawing.Point(10, 123);
            this.encryptConectionCheckbox.Name = "encryptConectionCheckbox";
            this.encryptConectionCheckbox.Size = new System.Drawing.Size(147, 17);
            this.encryptConectionCheckbox.TabIndex = 8;
            this.encryptConectionCheckbox.Text = "Encrypt connection (TLS)";
            this.encryptConectionCheckbox.UseVisualStyleBackColor = true;
            this.encryptConectionCheckbox.CheckedChanged += new System.EventHandler(this.encryptConectionCheckbox_CheckedChanged);
            // 
            // validateCertificateCheckbox
            // 
            this.validateCertificateCheckbox.AutoSize = true;
            this.validateCertificateCheckbox.Location = new System.Drawing.Point(10, 146);
            this.validateCertificateCheckbox.Name = "validateCertificateCheckbox";
            this.validateCertificateCheckbox.Size = new System.Drawing.Size(113, 17);
            this.validateCertificateCheckbox.TabIndex = 9;
            this.validateCertificateCheckbox.Text = "Validate certificate";
            this.validateCertificateCheckbox.UseVisualStyleBackColor = true;
            // 
            // haDiscoveryGroupBox
            // 
            this.haDiscoveryGroupBox.Controls.Add(this.haDiscoveryEnabledCheckbox);
            this.haDiscoveryGroupBox.Controls.Add(this.haDiscoveryPrefixLabel);
            this.haDiscoveryGroupBox.Controls.Add(this.haDiscoveryPrefixTextBox);
            this.haDiscoveryGroupBox.Location = new System.Drawing.Point(10, 180);
            this.haDiscoveryGroupBox.Name = "haDiscoveryGroupBox";
            this.haDiscoveryGroupBox.Size = new System.Drawing.Size(380, 90);
            this.haDiscoveryGroupBox.TabIndex = 10;
            this.haDiscoveryGroupBox.TabStop = false;
            this.haDiscoveryGroupBox.Text = "Home Assistant MQTT Discovery";
            // 
            // haDiscoveryEnabledCheckbox
            // 
            this.haDiscoveryEnabledCheckbox.AutoSize = true;
            this.haDiscoveryEnabledCheckbox.Location = new System.Drawing.Point(10, 22);
            this.haDiscoveryEnabledCheckbox.Name = "haDiscoveryEnabledCheckbox";
            this.haDiscoveryEnabledCheckbox.Size = new System.Drawing.Size(225, 17);
            this.haDiscoveryEnabledCheckbox.TabIndex = 0;
            this.haDiscoveryEnabledCheckbox.Text = "Publish discovery messages to Home Assistant";
            this.haDiscoveryEnabledCheckbox.UseVisualStyleBackColor = true;
            this.haDiscoveryEnabledCheckbox.CheckedChanged += new System.EventHandler(this.haDiscoveryEnabledCheckbox_CheckedChanged);
            // 
            // haDiscoveryPrefixLabel
            // 
            this.haDiscoveryPrefixLabel.AutoSize = true;
            this.haDiscoveryPrefixLabel.Location = new System.Drawing.Point(10, 53);
            this.haDiscoveryPrefixLabel.Name = "haDiscoveryPrefixLabel";
            this.haDiscoveryPrefixLabel.Size = new System.Drawing.Size(85, 13);
            this.haDiscoveryPrefixLabel.TabIndex = 1;
            this.haDiscoveryPrefixLabel.Text = "Discovery prefix:";
            // 
            // haDiscoveryPrefixTextBox
            // 
            this.haDiscoveryPrefixTextBox.Location = new System.Drawing.Point(140, 50);
            this.haDiscoveryPrefixTextBox.Name = "haDiscoveryPrefixTextBox";
            this.haDiscoveryPrefixTextBox.Size = new System.Drawing.Size(220, 20);
            this.haDiscoveryPrefixTextBox.TabIndex = 2;
            // 
            // MqttServerSettingsPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.haDiscoveryGroupBox);
            this.Controls.Add(this.validateCertificateCheckbox);
            this.Controls.Add(this.encryptConectionCheckbox);
            this.Controls.Add(this.passwordTextBox);
            this.Controls.Add(this.usernameTextBox);
            this.Controls.Add(this.portTextBox);
            this.Controls.Add(this.addressTextBox);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Name = "MqttServerSettingsPanel";
            this.Size = new System.Drawing.Size(455, 516);
            this.haDiscoveryGroupBox.ResumeLayout(false);
            this.haDiscoveryGroupBox.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox addressTextBox;
        private System.Windows.Forms.TextBox portTextBox;
        private System.Windows.Forms.TextBox usernameTextBox;
        private System.Windows.Forms.MaskedTextBox passwordTextBox;
        private System.Windows.Forms.CheckBox encryptConectionCheckbox;
        private System.Windows.Forms.CheckBox validateCertificateCheckbox;
        private System.Windows.Forms.GroupBox haDiscoveryGroupBox;
        private System.Windows.Forms.CheckBox haDiscoveryEnabledCheckbox;
        private System.Windows.Forms.Label haDiscoveryPrefixLabel;
        private System.Windows.Forms.TextBox haDiscoveryPrefixTextBox;
    }
}