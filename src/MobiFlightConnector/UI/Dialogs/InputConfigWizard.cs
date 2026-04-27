using MobiFlight.Base;
using MobiFlight.Config;
using MobiFlight.UI.Panels.Config;
using MobiFlight.UI.Panels.Input;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MobiFlight.UI.Dialogs
{
    public partial class InputConfigWizard : Form
    {
        struct ButtonStyle
        {
            public Color BackColor;
            public Color ForeColor;
            public Color BorderColor;
        }

        public event EventHandler PreconditionTreeNodeChanged;
        public event EventHandler SettingsDialogRequested;

        protected bool ScanningForInput = false;

        static int lastTabActive = 0;

        IExecutionManager _execManager = null;
        int displayPanelHeight = -1;
        List<UserControl> displayPanels = new List<UserControl>();

        InputConfigItem config = null;
        public InputConfigItem Config { get { return config; } }

        InputConfigItem originalConfig = null;

        ErrorProvider errorProvider = new ErrorProvider();
        Dictionary<String, String> arcazeFirmware = new Dictionary<String, String>();
        List<OutputConfigItem> outputConfigItems = null;
#if ARCAZE
        Dictionary<string, ArcazeModuleSettings> moduleSettings;
#endif

        ButtonStyle ScanForInputButtonDefaultStyle;
        Dictionary<string, int> ScanForInputThreshold = new Dictionary<string, int>();

        bool IsShown = false;
        public PreconditionPanel PreconditionPanel { get { return preconditionPanel; } }
        ProjectInfo ProjectInfo { get; set; }

        public InputConfigWizard(IExecutionManager executionManager,
                             InputConfigItem cfg,
#if ARCAZE
                             ArcazeCache arcazeCache,
                             Dictionary<string, ArcazeModuleSettings> moduleSettings,
#endif
                             List<OutputConfigItem> outputConfigItems,
                             Dictionary<string, MobiFlightVariable> scopedVariables,
            ProjectInfo projectInfo)
        {
            Init(executionManager, cfg);
#if ARCAZE
            this.moduleSettings = moduleSettings;
            initWithArcazeCache(arcazeCache);
#else
            initWithoutArcazeCache();
#endif
            // copy this so that no filtering will 
            // impact the list of displayed items
            // https://github.com/MobiFlight/MobiFlight-Connector/issues/1447
            this.outputConfigItems = outputConfigItems.ToArray().ToList();

            var list = outputConfigItems.Where(c => c.GUID != cfg.GUID)
                                     .Select(c => new ListItem() { Label = c.Name, Value = c.GUID }).ToList();

            preconditionPanel.SetAvailableConfigs(list);
            preconditionPanel.SetAvailableVariables(scopedVariables);
            initConfigRefDropDowns(this.outputConfigItems, cfg.GUID);
            _loadPresets();

            // remember the default style of the button
            ScanForInputButtonDefaultStyle.BackColor = ScanForInputButton.BackColor;
            ScanForInputButtonDefaultStyle.ForeColor = ScanForInputButton.ForeColor;
            ScanForInputButtonDefaultStyle.BorderColor = ScanForInputButton.FlatAppearance.BorderColor;

            // Append the row description to the window title if one was provided.
            if (!String.IsNullOrEmpty(cfg.Name))
            {
                this.Text = $"{this.Text} - {cfg.Name}";
            }

            ProjectInfo = projectInfo;
        }

        private void initConfigRefDropDowns(List<OutputConfigItem> dataSetConfig, string filterGuid)
        {
            configRefPanel.SetConfigRefsDataView(dataSetConfig, filterGuid);
        }

        private void ConfigWizard_Load(object sender, EventArgs e)
        {
            _syncConfigToForm(config);
        }

        public bool ConfigHasChanged()
        {
            return !originalConfig.Equals(config);
        }

        protected void Init(IExecutionManager executionManager, InputConfigItem cfg)
        {
            this._execManager = executionManager;
            // create a clone so that we don't edit 
            // the original item
            config = cfg.Clone() as InputConfigItem;

            // this is only stored to be able
            // to check for modifications
            originalConfig = cfg;

            InitializeComponent();

            ActivateCorrectTab(config);

            // PRECONDITION PANEL
            preconditionPanel.Init();
            preconditionPanel.ErrorOnValidating += (sender, e) =>
            {
                tabControlFsuipc.SelectedTab = preconditionTabPage;
            };
        }

        private void ActivateCorrectTab(InputConfigItem cfg)
        {
            // by default always the first tab is activated
            // if one opens the dialog for an existing config
            // we use the lastTabActive
            if (cfg?.Controller != null)
            {
                tabControlFsuipc.SelectedIndex = lastTabActive;
            }
        }

        private void _loadPresets()
        {
            bool isLoaded = true;

            if (!System.IO.File.Exists(Properties.Settings.Default.PresetFileOutputs))
            {
                isLoaded = false;
                MessageBox.Show(i18n._tr("uiMessageConfigWizard_PresetsNotFound"), i18n._tr("Hint"));
            }
            else
            {

                try
                {
                    presetsDataSet.Clear();
                    presetsDataSet.ReadXml(Properties.Settings.Default.PresetFileOutputs);
                    DataRow[] rows = presetDataTable.Select("", "description");
                }
                catch (Exception e)
                {
                    isLoaded = false;
                    MessageBox.Show(i18n._tr("uiMessageConfigWizard_ErrorLoadingPresets"), i18n._tr("Hint"));
                }
            }
        }

#if ARCAZE
        /// <summary>
        /// sync the config wizard with the provided settings from arcaze cache such as available modules, ports, etc.
        /// </summary>
        /// <param name="arcazeCache"></param>
        public void initWithArcazeCache(ArcazeCache arcazeCache)
        {
            if (arcazeCache == null) return;

            var PreconditionModuleList = new List<ListItem<Controller>>();
            var controllerComboBoxItems = new List<ListItem<Controller>>()
            {
                new ListItem<Controller>() { Value = null, Label = "-" }
            };

            inputModuleNameComboBox.Items.Clear();
            inputModuleNameComboBox.DisplayMember = "Label";
            inputModuleNameComboBox.ValueMember = "Value";

            foreach (IModuleInfo module in arcazeCache.getModuleInfo())
            {
                arcazeFirmware[module.Serial] = module.Version;

                PreconditionModuleList.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = module.Name, Serial = module.Serial },
                    Label = $"{module.Name} ({module.Serial})"
                });
            }

            foreach (IModuleInfo module in _execManager.getMobiFlightModuleCache().GetModuleInfo())
            {
                controllerComboBoxItems.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = module.Name, Serial = module.Serial },
                    Label = $"{module.Name} ({module.Port})"
                });
            }

            foreach (Joystick joystick in _execManager.GetJoystickManager().GetJoysticks())
            {
                if (joystick.GetAvailableDevicesAsListItems().Count > 0)
                    controllerComboBoxItems.Add(new ListItem<Controller>()
                    {
                        Value = new Controller() { Name = joystick.Name, Serial = joystick.Serial },
                        Label = $"{joystick.Name}"
                    });
            }

            foreach (MidiBoard midiBoard in _execManager.GetMidiBoardManager().GetMidiBoards())
            {
                controllerComboBoxItems.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = midiBoard.Name, Serial = midiBoard.Serial },
                    Label = $"{midiBoard.Name}"
                });
            }

            // Always offer MQTT as a controller; users can add new topics inline.
            controllerComboBoxItems.Add(new ListItem<Controller>()
            {
                Value = new Controller() { Name = "MQTTServer", Serial = MQTTManager.Serial },
                Label = "MQTT topic"
            });

            inputModuleNameComboBox.DataSource = controllerComboBoxItems;
            inputModuleNameComboBox.SelectedIndex = 0;
            preconditionPanel.SetModules(PreconditionModuleList);
        }
#endif
        public void initWithoutArcazeCache()
        {
            var PreconditionModuleList = new List<ListItem<Controller>>();
            var controllerComboBoxItems = new List<ListItem<Controller>>()
            {
                new ListItem<Controller>() { Value = null, Label = "-" }
            };

            // update the display box with
            // modules
            inputModuleNameComboBox.Items.Clear();
            inputModuleNameComboBox.DisplayMember = "Label";
            inputModuleNameComboBox.ValueMember = "Value";

            foreach (IModuleInfo module in _execManager.getMobiFlightModuleCache().GetModuleInfo())
            {
                controllerComboBoxItems.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = module.Name, Serial = module.Serial },
                    Label = $"{module.Name}{SerialNumber.SerialSeparator}({module.Port})"
                });
            }

            foreach (Joystick joystick in _execManager.GetJoystickManager().GetJoysticks())
            {
                controllerComboBoxItems.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = joystick.Name, Serial = joystick.Serial },
                    Label = $"{joystick.Name}"
                });
            }

            foreach (MidiBoard midiBoard in _execManager.GetMidiBoardManager().GetMidiBoards())
            {
                controllerComboBoxItems.Add(new ListItem<Controller>()
                {
                    Value = new Controller() { Name = midiBoard.Name, Serial = midiBoard.Serial },
                    Label = $"{midiBoard.Name}"
                });
            }

            // Always offer MQTT as a controller; users can add new topics inline.
            controllerComboBoxItems.Add(new ListItem<Controller>()
            {
                Value = new Controller() { Name = "MQTTServer", Serial = MQTTManager.Serial },
                Label = "MQTT topic"
            });

            inputModuleNameComboBox.DataSource = controllerComboBoxItems;
            inputModuleNameComboBox.SelectedIndex = 0;
            preconditionPanel.SetModules(PreconditionModuleList);
        }

        /// <summary>
        /// sync the values from config with the config wizard form
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        protected bool _syncConfigToForm(InputConfigItem config)
        {
            if (config == null) throw new Exception(i18n._tr("uiException_ConfigItemNotFound"));

            // first tab                        
            if (config.Controller != null)
            {
                if (!ComboBoxHelper.SetSelectedListItemByValue<Controller>(inputModuleNameComboBox, config.Controller))
                {
                    // TODO: provide error message
                }
            }

            // second tab
            if (config.DeviceName != null && !ComboBoxHelper.SetSelectedDeviceByDeviceName(inputTypeComboBox, config.DeviceName))
            {
                // TODO: provide error message
                Log.Instance.log($"Exception on selecting item in input type ComboBox. {config.DeviceName}", LogSeverity.Error);
            }

            // Reflect persisted momentary/latching choice. Visibility is reapplied later
            // by inputTypeComboBox_SelectedIndexChanged based on the resolved device type.
            momentaryButtonCheckBox.Checked = config.MomentaryButton;
            publishToMqttCheckBox.Checked = config.PublishToMQTT;

            preconditionPanel.syncFromConfig(config);

            configRefPanel.syncFromConfig(config);

            return true;
        }

        private void PopulateInputPinDropdown(int numModules, int? selectedPin)
        {
            // Originally added for Input shift registers
            // Also used for digital input multiplexers, usually with numModules=2 (CD4067) or 1 (CD4051)
            // The selected input in the dropdown is the shift register details, which includes the
            // number of connected modules. That gets multiplied by 8 pins per module to get the total
            // number of available pins to populate.
            int totalPins = numModules * 8;

            inputPinDropDown.Items.Clear();
            for (int i = 0; i < totalPins; i++)
            {
                inputPinDropDown.Items.Add(i);
            }

            inputPinDropDown.SelectedItem = selectedPin ?? 0;
        }

        /// <summary>
        /// Clears all config objects except the specified device type to prevent incorrect input event matching
        /// </summary>
        /// <param name="keepDeviceType">The device type to keep, all others will be cleared</param>
        private void ClearUnusedConfigObjects(DeviceType keepDeviceType)
        {
            if (keepDeviceType != DeviceType.Button)
                config.button = null;
            if (keepDeviceType != DeviceType.Encoder)
                config.encoder = null;
            if (keepDeviceType != DeviceType.InputShiftRegister)
                config.inputShiftRegister = null;
            if (keepDeviceType != DeviceType.InputMultiplexer)
                config.inputMultiplexer = null;
            if (keepDeviceType != DeviceType.AnalogInput)
                config.analog = null;
        }

        /// <summary>
        /// sync current status of form values to config
        /// </summary>
        /// <returns></returns>
        protected bool _syncFormToConfig()
        {
            var selectedControllerListItem = inputModuleNameComboBox.SelectedItem as ListItem<Controller>;
            config.Controller = selectedControllerListItem.Value;

            configRefPanel.syncToConfig(config);

            preconditionPanel.syncToConfig(config);

            if (config.Controller == null) return true;

            // MQTT inline editor: register the (possibly newly typed) topic with MQTTManager and
            // make sure inputTypeComboBox.SelectedItem points at a matching ListItem<IBaseDevice>
            // so the rest of this method (which assumes a SelectedItem) can run unchanged.
            if (MQTTManager.IsMQTTSerial(config.Controller.Serial))
            {
                SyncMqttInputFromEditor();
            }

            IBaseDevice device = ((ListItem<IBaseDevice>)inputTypeComboBox.SelectedItem).Value;
            if (device.Label != InputConfigItem.TYPE_NOTSET)
                config.DeviceName = device.Name;

            DeviceType currentInputType = determineCurrentDeviceType(config.Controller.Serial);

            //if (groupBoxInputSettings.Controls.Count == 0) return false;

            switch (currentInputType)
            {
                case DeviceType.Button:
                    config.DeviceType = InputConfigItem.TYPE_BUTTON;
                    if (config.button == null) config.button = new InputConfig.ButtonInputConfig();
                    if (groupBoxInputSettings.Controls[0] != null)
                        (groupBoxInputSettings.Controls[0] as ButtonPanel).ToConfig(config.button);
                    break;

                case DeviceType.Encoder:
                    config.DeviceType = InputConfigItem.TYPE_ENCODER;
                    if (config.encoder == null) config.encoder = new InputConfig.EncoderInputConfig();
                    if (groupBoxInputSettings.Controls[0] != null)
                        (groupBoxInputSettings.Controls[0] as EncoderPanel).ToConfig(config.encoder);
                    break;

                case DeviceType.InputShiftRegister:
                    config.DeviceType = InputConfigItem.TYPE_INPUT_SHIFT_REGISTER;
                    if (config.inputShiftRegister == null) config.inputShiftRegister = new InputConfig.InputShiftRegisterConfig();
                    config.inputShiftRegister.ExtPin = (int)inputPinDropDown.SelectedItem;
                    if (groupBoxInputSettings.Controls[0] != null)
                        (groupBoxInputSettings.Controls[0] as ButtonPanel).ToConfig(config.inputShiftRegister);
                    break;

                case DeviceType.InputMultiplexer:
                    config.DeviceType = InputConfigItem.TYPE_INPUT_MULTIPLEXER;
                    if (config.inputMultiplexer == null) config.inputMultiplexer = new InputConfig.InputMultiplexerConfig();
                    config.inputMultiplexer.DataPin = (int)inputPinDropDown.SelectedItem;
                    if (groupBoxInputSettings.Controls[0] != null)
                        (groupBoxInputSettings.Controls[0] as ButtonPanel).ToConfig(config.inputMultiplexer);
                    break;

                case DeviceType.AnalogInput:
                    config.DeviceType = InputConfigItem.TYPE_ANALOG;
                    if (config.analog == null) config.analog = new InputConfig.AnalogInputConfig();
                    if (groupBoxInputSettings.Controls[0] != null)
                        (groupBoxInputSettings.Controls[0] as AnalogPanel).ToConfig(config.analog);
                    break;

                case DeviceType.NotSet:
                    config.DeviceType = InputConfigItem.TYPE_NOTSET;
                    config.DeviceName = InputConfigItem.TYPE_NOTSET;
                    break;
            }

            config.Device = InputConfigItem.CreateInputDevice(config);

            // Persist momentary/latching choice. Only meaningful for Button-type inputs;
            // for everything else we force the safe default so the field doesn't carry a
            // stale value when the user switches device types within the same dialog.
            config.MomentaryButton = (currentInputType == DeviceType.Button)
                ? momentaryButtonCheckBox.Checked
                : true;

            // Per-input MQTT publish opt-in is independent of device type.
            config.PublishToMQTT = publishToMqttCheckBox.Checked;

            // Clear unused config objects after switching device type to prevent incorrect input event matching
            ClearUnusedConfigObjects(currentInputType);

            return true;
        }

        private void okButton_Click(object sender, EventArgs e)
        {
            if (!ValidateChildren())
            {
                return;
            }
            _syncFormToConfig();
            DialogResult = DialogResult.OK;
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
        }

        private void ModuleSerialComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Hide the input shifter / dig. input mux pin dropdown whenever the module changes. 
            // It will be made visible again in inputTypeComboBox_SelectedIndexChanged() 
            // when the user selects an input type.
            inputPinDropDown.Visible = false;

            // check which extension type is available to current serial
            ComboBox cb = (sender as ComboBox);
            try
            {
                var controllerListItem = (cb.SelectedItem as ListItem<Controller>);
                var serial = controllerListItem?.Value?.Serial;

                inputTypeComboBox.Items.Clear();
                inputTypeComboBox.ValueMember = "Value";
                inputTypeComboBox.DisplayMember = "Label";

                IBaseDevice emptyDevice = new BaseDevice() { Name = InputConfigItem.TYPE_NOTSET };
                inputTypeComboBox.Items.Add(new ListItem<IBaseDevice>() { Label = InputConfigItem.TYPE_NOTSET, Value = emptyDevice });
                inputTypeComboBox.SelectedIndex = 0;
                inputTypeComboBox.Enabled = true;

                if (string.IsNullOrEmpty(serial))
                {
                    BaseDevice device = null;

                    if (config.button != null)
                    {
                        device = new Config.Button();
                    }
                    else if (config.encoder != null)
                    {
                        device = new Encoder();
                    }
                    else if (config.analog != null)
                    {
                        device = new AnalogInput();
                    }
                    else if (config.inputShiftRegister != null)
                    {
                        device = new InputShiftRegister();
                    }
                    else if (config.inputMultiplexer != null)
                    {
                        device = new InputMultiplexer();
                    }

                    if (device != null)
                    {
                        device.Name = config.DeviceName;
                        inputTypeComboBox.Items.Add(new ListItem<IBaseDevice>() { Label = device.Label, Value = device });
                    }
                    inputTypeComboBox.Enabled = false;
                }
                // Add all Joysticks
                else if (Joystick.IsJoystickSerial(serial))
                {
                    Joystick joystick = _execManager.GetJoystickManager().GetJoystickBySerial(serial);
                    inputTypeComboBox.Items.AddRange(joystick.GetAvailableDevicesAsListItems().ToArray());
                }
                // Add all MidiBoards
                else if (MidiBoard.IsMidiBoardSerial(serial))
                {
                    MidiBoard midiBoard = _execManager.GetMidiBoardManager().GetMidiBoardBySerial(serial);
                    var devices = midiBoard.GetAvailableDevices();
                    foreach (var device in devices)
                    {
                        inputTypeComboBox.Items.Add(new ListItem<IBaseDevice>() { Label = device.Label, Value = device });
                    }
                }
                // Add MQTT inputs
                else if (MQTTManager.IsMQTTSerial(serial))
                {
                    foreach (var input in _execManager.GetMQTTManager().GetMqttInputs())
                    {
                        inputTypeComboBox.Items.Add(new ListItem<IBaseDevice>() { Label = input.Value.Label, Value = input.Value});
                    }
                }
                else
                {
                    MobiFlightModule module = _execManager.getMobiFlightModuleCache().GetModuleBySerial(serial);

                    if (module != null)
                    {
                        foreach (Config.BaseDevice device in module.GetConnectedInputDevices())
                        {
                            switch (device.Type)
                            {
                                case DeviceType.Button:
                                case DeviceType.AnalogInput:
                                case DeviceType.Encoder:
                                case DeviceType.InputShiftRegister:
                                case DeviceType.InputMultiplexer:
                                    inputTypeComboBox.Items.Add(new ListItem<IBaseDevice>() { Label = device.Name, Value = device });
                                    break;
                            }
                        }
                    }

                    if (inputTypeComboBox.Items.Count == 0 && this.IsShown)
                    {
                        if (MessageBox.Show(
                                i18n._tr("uiMessageSelectedModuleDoesNotContainAnyInputDevices"),
                                i18n._tr("Hint"),
                                MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes
                            )
                        {
                            if (SettingsDialogRequested != null)
                            {
                                SettingsDialogRequested(module, new EventArgs());

                                // trigger reload of Type ComboBox
                                int CurrentIdx = inputModuleNameComboBox.SelectedIndex;
                                inputModuleNameComboBox.SelectedIndex = 0;
                                inputModuleNameComboBox.SelectedIndex = CurrentIdx;
                            }
                        }
                    }
                }

                // third tab
                if (config.DeviceName != null && !ComboBoxHelper.SetSelectedDeviceByDeviceName(inputTypeComboBox, config.DeviceName))
                {
                    Log.Instance.log($"Problem setting input type ComboBox. {config.DeviceName}", LogSeverity.Error);
                }

                // Show or hide the MQTT inline topic editor based on the selected controller.
                SetMqttEditorVisibility(MQTTManager.IsMQTTSerial(serial));
            }
            catch (Exception ex)
            {
                Log.Instance.log(ex.Message, LogSeverity.Error);
            }
        }

        private DeviceType determineCurrentDeviceType(String serial)
        {
            if (MQTTManager.IsMQTTSerial(serial))
            {
                IBaseDevice mqttDevice = (inputTypeComboBox.SelectedItem as ListItem<IBaseDevice>)?.Value;
                if (mqttDevice != null && !string.IsNullOrEmpty(mqttDevice.Name) && mqttDevice.Label != InputConfigItem.TYPE_NOTSET)
                    return mqttDevice.Type;
                // Free-typed new topic: fall back to the inline Type combo selection.
                return (mqttTypeComboBox?.SelectedItem as ListItem<DeviceType>)?.Value ?? DeviceType.NotSet;
            }

            IBaseDevice device = (inputTypeComboBox.SelectedItem as ListItem<IBaseDevice>)?.Value;
            if (device != null && !string.IsNullOrEmpty(device?.Name))
                return device.Type;
            else
                return DeviceType.NotSet;
        }

        // ---------------------------------------------------------------------
        // MQTT inline topic editor
        //
        // When the user picks the "MQTT topic" controller in Add Input Config we want them to
        // be able to type a brand-new topic right inside the wizard. The topic is persisted
        // as part of the InputConfigItem inside the .mcc project file (DeviceName=topic,
        // DeviceType=Button/AnalogInput) -- mirroring how OutputConfigItem.MqttMessage stores
        // the output side. We also need to capture the input Type (Button / AnalogInput) and
        // an optional Label, since both pieces are required by MQTTManager to interpret
        // incoming messages at runtime.
        //
        // The extra controls are added at runtime to avoid touching the .Designer.cs / .resx
        // files (which makes the Designer view brittle).
        // ---------------------------------------------------------------------
        private System.Windows.Forms.Label mqttTypeLabel;
        private System.Windows.Forms.ComboBox mqttTypeComboBox;
        private System.Windows.Forms.Label mqttTopicLabel;
        private System.Windows.Forms.TextBox mqttLabelTextBox;
        private bool mqttEditorBuilt = false;

        private void BuildMqttEditorIfNeeded()
        {
            if (mqttEditorBuilt) return;
            mqttEditorBuilt = true;

            var parent = inputTypeComboBox.Parent;
            if (parent == null) return;

            // Lay the MQTT helper controls out on a NEW row directly below Device using the
            // SAME 25 px row pitch as Module->Device (defined by inputModuleNameComboBox.Y=20
            // and inputTypeComboBox.Y=45 in the .resx). Mirroring the existing pitch keeps
            // all rows visually equidistant.
            var rowTop = inputTypeComboBox.Top + 25;            // Module=20, Device=45, MQTT=70
            var labelTop = rowTop + 3;                          // labels visually centered against combos
            var leftEdge = inputTypeComboBox.Left;              // align with Module/Device combos (X=98)

            mqttTypeLabel = new System.Windows.Forms.Label
            {
                Text = "MQTT Type",
                AutoSize = true,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                // Same right-edge as "Device" label so the column alignment stays consistent.
                Location = new System.Drawing.Point(leftEdge - 70, labelTop),
                Visible = false,
            };

            mqttTypeComboBox = new System.Windows.Forms.ComboBox
            {
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(leftEdge, rowTop),
                Width = 110,
                Visible = false,
                DisplayMember = "Label",
                ValueMember = "Value",
            };
            mqttTypeComboBox.Items.Add(new ListItem<DeviceType> { Label = "Button", Value = DeviceType.Button });
            mqttTypeComboBox.Items.Add(new ListItem<DeviceType> { Label = "AnalogInput", Value = DeviceType.AnalogInput });
            mqttTypeComboBox.SelectedIndex = 0;
            mqttTypeComboBox.SelectedIndexChanged += (s, e) =>
            {
                // Re-render the Input Settings panel when the MQTT type switches between Button/AnalogInput.
                inputTypeComboBox_SelectedIndexChanged(this, EventArgs.Empty);
            };

            mqttTopicLabel = new System.Windows.Forms.Label
            {
                Text = "Label",
                AutoSize = true,
                Location = new System.Drawing.Point(mqttTypeComboBox.Right + 16, labelTop),
                Visible = false,
            };

            mqttLabelTextBox = new System.Windows.Forms.TextBox
            {
                // Position after the Label caption ("Label" is ~33 px wide at default font).
                Location = new System.Drawing.Point(mqttTypeComboBox.Right + 16 + 38, rowTop),
                Width = 200,
                Visible = false,
            };

            parent.Controls.Add(mqttTypeLabel);
            parent.Controls.Add(mqttTypeComboBox);
            parent.Controls.Add(mqttTopicLabel);
            parent.Controls.Add(mqttLabelTextBox);

            // When the user picks an existing topic from the dropdown, populate the helper
            // fields so they reflect what is currently stored for that topic.
            inputTypeComboBox.SelectedIndexChanged += MqttSyncEditorFromSelection;
            // When the user types a brand-new topic, refresh the Input Settings panel so it
            // matches the currently selected MQTT type.
            inputTypeComboBox.TextUpdate += (s, e) => inputTypeComboBox_SelectedIndexChanged(this, EventArgs.Empty);
        }

        private void MqttSyncEditorFromSelection(object sender, EventArgs e)
        {
            if (mqttTypeComboBox == null || mqttLabelTextBox == null) return;

            var serial = (inputModuleNameComboBox.SelectedItem as ListItem<Controller>)?.Value?.Serial;
            if (!MQTTManager.IsMQTTSerial(serial)) return;

            if (inputTypeComboBox.SelectedItem is ListItem<IBaseDevice> li && li.Value is MQTTInput mi)
            {
                for (int i = 0; i < mqttTypeComboBox.Items.Count; i++)
                {
                    if ((mqttTypeComboBox.Items[i] as ListItem<DeviceType>)?.Value == mi.Type)
                    {
                        mqttTypeComboBox.SelectedIndex = i;
                        break;
                    }
                }
                mqttLabelTextBox.Text = mi.Label ?? string.Empty;
            }
        }

        private void SetMqttEditorVisibility(bool visible)
        {
            BuildMqttEditorIfNeeded();
            if (!mqttEditorBuilt) return;

            mqttTypeLabel.Visible = visible;
            mqttTypeComboBox.Visible = visible;
            mqttTopicLabel.Visible = visible;
            mqttLabelTextBox.Visible = visible;

            // The MQTT inline editor (Type combo + Label textbox) is positioned to the right
            // of inputTypeComboBox at roughly the same X range as the Scan-for-input button
            // (X 388..473) and the Board-not-connected warning (X 385..653). For non-MQTT
            // controllers those two are useful, but for MQTT they make no sense AND their
            // text/buttons end up visually overlapping the dynamically-added MQTT labels
            // ("MQTT Type", "Label"), causing fragments like "p" / "el" to leak around the
            // Scan button. So we hide them whenever MQTT is the active controller.
            ScanForInputButton.Visible = !visible;
            if (visible)
            {
                DeviceNotAvailableWarningLabel.Visible = false;
            }

            // When MQTT is selected, allow free-text entry of new topics. Otherwise enforce
            // pick-from-list semantics like the rest of the controllers expect.
            inputTypeComboBox.DropDownStyle = visible
                ? System.Windows.Forms.ComboBoxStyle.DropDown
                : System.Windows.Forms.ComboBoxStyle.DropDownList;
        }

        /// <summary>
        /// Reads the topic / type / label out of the MQTT inline editor, registers the topic
        /// with the MQTTManager in-memory (so the broker subscribes immediately, even before
        /// the project is saved) and ensures inputTypeComboBox.SelectedItem ends up pointing
        /// at a matching ListItem so the rest of _syncFormToConfig can run unchanged. The
        /// authoritative storage is the InputConfigItem itself (DeviceName=topic,
        /// DeviceType=Button/AnalogInput), which is serialized into the .mcc project file --
        /// MQTTManager rebuilds its in-memory list from the project on every project change.
        /// </summary>
        private void SyncMqttInputFromEditor()
        {
            if (mqttTypeComboBox == null) return;

            var topic = (inputTypeComboBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(topic) || topic == InputConfigItem.TYPE_NOTSET)
                return;

            var deviceType = (mqttTypeComboBox.SelectedItem as ListItem<DeviceType>)?.Value ?? DeviceType.Button;
            var label = string.IsNullOrWhiteSpace(mqttLabelTextBox?.Text) ? topic : mqttLabelTextBox.Text.Trim();
            var input = new MQTTInput { Type = deviceType, Label = label };

            _execManager.GetMQTTManager().AddOrUpdateInput(topic, input);

            // Make sure the dropdown contains a ListItem<IBaseDevice> wrapping this input
            // and select it, so downstream code in _syncFormToConfig sees a valid SelectedItem.
            ListItem<IBaseDevice> match = null;
            foreach (var item in inputTypeComboBox.Items)
            {
                if (item is ListItem<IBaseDevice> existing && existing.Value is MQTTInput existingMi && existingMi.Label == input.Label)
                {
                    match = existing;
                    match.Value = input; // refresh the underlying type/label
                    break;
                }
            }
            if (match == null)
            {
                match = new ListItem<IBaseDevice> { Label = input.Label, Value = input };
                inputTypeComboBox.Items.Add(match);
            }
            inputTypeComboBox.SelectedItem = match;
        }

        private void inputTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Control panel = null;
            groupBoxInputSettings.Controls.Clear();
            inputPinDropDown.Visible = false;

            try
            {
                bool panelEnabled = true;
                // get the deviceinfo for the current controller
                var selectedItem = inputModuleNameComboBox.SelectedItem as ListItem<Controller>;
                String serial = selectedItem?.Value?.Serial;

                // we remove the callback method to ensure, that it is not added more than once
                // displayLedDisplayPanel.displayLedAddressComboBox.SelectedIndexChanged -= displayLedAddressComboBox_SelectedIndexChanged;

                DeviceType currentInputType = determineCurrentDeviceType(serial);

                switch (currentInputType)
                {
                    case DeviceType.Button:
                        panel = new Panels.Input.ButtonPanel()
                        {
                            Enabled = (serial != ""),
                            ProjectInfo = this.ProjectInfo,
                            CurrentConfig = Config
                        };
                        (panel as Panels.Input.ButtonPanel).syncFromConfig(config.button);
                        break;

                    case DeviceType.Encoder:
                        panel = new Panels.Input.EncoderPanel()
                        {
                            Enabled = (serial != ""),
                            ProjectInfo = this.ProjectInfo,
                            CurrentConfig = Config,
                        };
                        (panel as Panels.Input.EncoderPanel).syncFromConfig(config.encoder);
                        break;

                    case DeviceType.InputShiftRegister:
                        Config.InputShiftRegister selectedInputShifter = (inputTypeComboBox.SelectedItem as ListItem<Config.IBaseDevice>).Value as Config.InputShiftRegister;
                        panel = new Panels.Input.ButtonPanel()
                        {
                            Enabled = (serial != ""),
                            ProjectInfo = this.ProjectInfo,
                            CurrentConfig = Config
                        };
                        (panel as Panels.Input.ButtonPanel).syncFromConfig(config.inputShiftRegister);
                        
                        var srSubCount = Convert.ToInt32(selectedInputShifter.NumModules);
                        var srSelectedSubIndex = (config.Device as MobiFlight.InputConfig.InputShiftRegister)?.SubIndex;
                        PopulateInputPinDropdown(srSubCount, srSelectedSubIndex);
                        
                        inputPinDropDown.Visible = true;
                        break;

                    case DeviceType.InputMultiplexer:
                        Config.InputMultiplexer selectedInputMultiplexer = (inputTypeComboBox.SelectedItem as ListItem<Config.IBaseDevice>).Value as Config.InputMultiplexer;
                        panel = new Panels.Input.ButtonPanel()
                        {
                            Enabled = (serial != ""),
                            ProjectInfo = this.ProjectInfo,
                            CurrentConfig = Config
                        };
                        (panel as Panels.Input.ButtonPanel).syncFromConfig(config.inputMultiplexer);

                        var subCount = Convert.ToInt32(selectedInputMultiplexer.NumBytes);
                        var selectedSubIndex = (config.Device as MobiFlight.InputConfig.InputMultiplexer)?.SubIndex;
                        PopulateInputPinDropdown(subCount, selectedSubIndex);
                        
                        inputPinDropDown.Visible = true;
                        break;

                    case DeviceType.AnalogInput:
                        panel = new Panels.Input.AnalogPanel()
                        {
                            Enabled = (serial != ""),
                            ProjectInfo = this.ProjectInfo,
                            CurrentConfig = Config
                        };
                        (panel as Panels.Input.AnalogPanel).syncFromConfig(config.analog);
                        break;
                }

                // Initialize panels that implement IInputPanel interface
                if (panel is IInputPanel inputPanel)
                {
                    inputPanel.Init(_execManager);
                    inputPanel.SetVariableReferences(_execManager.GetAvailableVariables());
                }

                DeviceNotAvailableWarningLabel.Visible = (serial == "") && currentInputType != DeviceType.NotSet;

                // Momentary/latching is meaningful only for plain Button-type inputs (it
                // controls how the input is exposed to Home Assistant: binary_sensor vs
                // switch). For all other device types the field is irrelevant, so hide it
                // and reset to the safe default so a stale "false" doesn't leak into a
                // freshly-saved encoder/analog/etc. config.
                momentaryButtonCheckBox.Visible = currentInputType == DeviceType.Button;
                if (currentInputType != DeviceType.Button)
                {
                    momentaryButtonCheckBox.Checked = true;
                }

                if (panel != null)
                {
                    panel.Padding = new Padding(2, 0, 2, 0);
                    groupBoxInputSettings.Controls.Add(panel);
                    panel.Dock = DockStyle.Fill;
                }
            }
            catch (Exception ex)
            {
                Log.Instance.log(ex.Message, LogSeverity.Error);
                MessageBox.Show(i18n._tr("uiMessageNotImplementedYet"),
                                i18n._tr("Hint"),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
            }
        }

        private void displayError(Control control, String message)
        {
            errorProvider.SetError(
                    control,
                    message);
            MessageBox.Show(message, i18n._tr("Hint"));
        }

        private void removeError(Control control)
        {
            errorProvider.SetError(
                    control,
                    "");
        }

        private void portComboBox_Validating(object sender, CancelEventArgs e)
        {
            ComboBox cb = (sender as ComboBox);
            if (!cb.Parent.Visible) return;
            if (null == cb.SelectedItem) return;
            if (cb.SelectedItem.ToString() == "-----")
            {
                e.Cancel = true;
                tabControlFsuipc.SelectedTab = displayTabPage;
                cb.Focus();
                displayError(cb, i18n._tr("Please_select_a_port"));
            }
            else
            {
                removeError(cb);
            }
        }

        private void tabControlFsuipc_SelectedIndexChanged(object sender, EventArgs e)
        {
            lastTabActive = (sender as TabControl).SelectedIndex;
        }

        private void InputConfigWizard_Shown(object sender, EventArgs e)
        {
            IsShown = true;
        }

        private void InputConfigWizard_FormClosing(object sender, FormClosingEventArgs e)
        {
            DeactivateScanForInputMode();
            groupBoxInputSettings.Dispose();
        }

        private void ScanForInputButton_Click(object sender, EventArgs e)
        {
            if (!ScanningForInput)
            {
                ActivateScanForInputMode();
            }
            else
            {
                DeactivateScanForInputMode();
            }
        }

        private void ActivateScanForInputMode()
        {
            ScanningForInput = true;
            ScanForInputButton.BackColor = Color.FromArgb(55, 110, 220);
            ScanForInputButton.ForeColor = Color.White;
            ScanForInputButton.FlatAppearance.BorderColor = Color.FromArgb(55, 110, 220);
            ScanForInputButton.Text = "Scanning...";
            _execManager.getMobiFlightModuleCache().OnButtonPressed += ScanforInput_OnButtonPressed;
            _execManager.GetJoystickManager().OnButtonPressed += ScanforInput_OnButtonPressed;
            _execManager.GetMidiBoardManager().OnButtonPressed += ScanforInput_OnButtonPressed;
        }

        private void DeactivateScanForInputMode()
        {
            ScanningForInput = false;
            _execManager.getMobiFlightModuleCache().OnButtonPressed -= ScanforInput_OnButtonPressed;
            _execManager.GetJoystickManager().OnButtonPressed -= ScanforInput_OnButtonPressed;
            _execManager.GetMidiBoardManager().OnButtonPressed -= ScanforInput_OnButtonPressed;

            ScanForInputButton.BackColor = ScanForInputButtonDefaultStyle.BackColor;
            ScanForInputButton.ForeColor = ScanForInputButtonDefaultStyle.ForeColor;
            ScanForInputButton.FlatAppearance.BorderColor = ScanForInputButtonDefaultStyle.BorderColor;
            ScanForInputButton.Text = "Scan for input";

            ScanForInputThreshold.Clear();
            // remove focus from button
            // to make it look like before clicking on it
            ScanForInputButton.Parent.Focus();
        }

        // required for correct thread-safe entry
        // into the ScanforInput_OnButtonPressed method
        delegate void ScanforInput_OnButtonPressedCallback(object sender, InputEventArgs e);

        private void ScanforInput_OnButtonPressed(object sender, InputEventArgs e)
        {
            if (!InputThresholdIsExceeded(e)) return;

            // Only the "positive" PRESS events matter for buttons
            if (e.Type == DeviceType.Button)
            {
                if (e.Value != (int)MobiFlightButton.InputEvent.PRESS)
                    return;
            }

            if (inputModuleNameComboBox.InvokeRequired)
            {
                inputModuleNameComboBox.BeginInvoke(new ScanforInput_OnButtonPressedCallback(ScanforInput_OnButtonPressed), new object[] { sender, e });
                return;
            }

            ListItem<Controller> controllerListItem = inputModuleNameComboBox.Items.Cast<ListItem<Controller>>().FirstOrDefault(i => i.Value?.Serial == e.Serial);

            if (controllerListItem == null) return;

            inputModuleNameComboBox.SelectedItem = controllerListItem;

            // try to set the device
            if (SerialNumber.IsJoystickSerial(e.Serial))
            {
                ComboBoxHelper.SetSelectedItem(inputTypeComboBox, e.DeviceLabel);
            }
            else if (SerialNumber.IsMidiBoardSerial(e.Serial))
            {
                // Add item to device list if not yet there
                if (!inputTypeComboBox.Items.OfType<ListItem<IBaseDevice>>().Any(i => i.Value.Name == e.DeviceId))
                {
                    MidiBoardDevice mbd = new MidiBoardDevice();
                    mbd.Label = e.DeviceLabel;
                    mbd.Name = e.DeviceId;
                    mbd.Type = DeviceType.Button;
                    inputTypeComboBox.Items.Add(new ListItem<IBaseDevice> { Label = mbd.Label, Value = mbd });
                }
                ComboBoxHelper.SetSelectedItem(inputTypeComboBox, e.DeviceLabel);
            }
            else
            {
                ComboBoxHelper.SetSelectedItem(inputTypeComboBox, e.DeviceId);
                // if multiplexer or inputshiftregister set the sub item too
                if (e.ExtPin.HasValue)
                {
                    ComboBoxHelper.SetSelectedItem(inputPinDropDown, e.ExtPin.ToString());
                }
            }

            DeactivateScanForInputMode();
        }

        private bool InputThresholdIsExceeded(InputEventArgs e)
        {
            const int JoystickThreshold = 2000;
            const int AnalogInputThreshold = 20;

            if ((SerialNumber.IsJoystickSerial(e.Serial) &&
                e.DeviceId.Contains(Joystick.AxisPrefix)) || e.Type == DeviceType.AnalogInput)
            {
                if (ScanForInputThreshold.ContainsKey(e.Serial + e.DeviceId))
                {
                    if (Math.Abs(e.Value - ScanForInputThreshold[e.Serial + e.DeviceId]) < (SerialNumber.IsJoystickSerial(e.Serial) ? JoystickThreshold : AnalogInputThreshold))
                    {
                        return false;
                    }
                }
                else
                {
                    ScanForInputThreshold[e.Serial + e.DeviceId] = e.Value;
                    return false;
                }
            }

            return true;
        }
    }
}
