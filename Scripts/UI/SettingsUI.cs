using Godot;

/// <summary>
/// Settings UI for controlling game settings like mouse sensitivity, graphics, etc.
/// </summary>
public partial class SettingsUI : Control
{
	public static SettingsUI Instance { get; private set; }
	
	// UI Controls
	private Panel _settingsPanel;
	private VBoxContainer _settingsContainer;
	private HSlider _mouseSensitivitySlider;
	private Label _mouseSensitivityLabel;
	private HSlider _masterVolumeSlider;
	private Label _masterVolumeLabel;
	private HSlider _fovSlider;
	private Label _fovLabel;
	private CheckBox _vsyncCheckbox;
	private OptionButton _resolutionOption;
	private Button _closeButton;
	private Button _resetButton;
	
	// Settings values
	private float _mouseSensitivity = 1.0f;
	private float _masterVolume = 1.0f;
	private float _fieldOfView = 75.0f;
	private bool _vsyncEnabled = true;
	
	[Signal] public delegate void MouseSensitivityChangedEventHandler(float sensitivity);
	[Signal] public delegate void SettingsAppliedEventHandler();
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			InitializeUI();
			LoadSettings();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void InitializeUI()
	{
		Name = "SettingsUI";
		Visible = false;
		
		// Create main settings panel
		_settingsPanel = new Panel();
		_settingsPanel.AnchorLeft = 0.5f;
		_settingsPanel.AnchorTop = 0.5f;
		_settingsPanel.AnchorRight = 0.5f;
		_settingsPanel.AnchorBottom = 0.5f;
		_settingsPanel.OffsetLeft = -300;
		_settingsPanel.OffsetTop = -250;
		_settingsPanel.OffsetRight = 300;
		_settingsPanel.OffsetBottom = 250;
		AddChild(_settingsPanel);
		
		// Create container for settings
		_settingsContainer = new VBoxContainer();
		_settingsContainer.AnchorLeft = 0;
		_settingsContainer.AnchorTop = 0;
		_settingsContainer.AnchorRight = 1;
		_settingsContainer.AnchorBottom = 1;
		_settingsContainer.OffsetLeft = 20;
		_settingsContainer.OffsetTop = 20;
		_settingsContainer.OffsetRight = -20;
		_settingsContainer.OffsetBottom = -20;
		_settingsContainer.AddThemeConstantOverride("separation", 20);
		_settingsPanel.AddChild(_settingsContainer);
		
		// Title
		var titleLabel = new Label();
		titleLabel.Text = "SETTINGS";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		_settingsContainer.AddChild(titleLabel);
		
		// Mouse Sensitivity
		CreateSliderSetting("Mouse Sensitivity", 0.1f, 3.0f, _mouseSensitivity, 
			out _mouseSensitivitySlider, out _mouseSensitivityLabel, OnMouseSensitivityChanged);
		
		// Master Volume
		CreateSliderSetting("Master Volume", 0.0f, 1.0f, _masterVolume,
			out _masterVolumeSlider, out _masterVolumeLabel, OnMasterVolumeChanged);
		
		// Field of View
		CreateSliderSetting("Field of View", 60.0f, 120.0f, _fieldOfView,
			out _fovSlider, out _fovLabel, OnFOVChanged);
		
		// VSync
		_vsyncCheckbox = new CheckBox();
		_vsyncCheckbox.Text = "V-Sync";
		_vsyncCheckbox.ButtonPressed = _vsyncEnabled;
		_vsyncCheckbox.Toggled += OnVSyncToggled;
		_settingsContainer.AddChild(_vsyncCheckbox);
		
		// Resolution
		var resolutionContainer = new HBoxContainer();
		var resolutionLabel = new Label();
		resolutionLabel.Text = "Resolution: ";
		resolutionLabel.CustomMinimumSize = new Vector2(150, 0);
		_resolutionOption = new OptionButton();
		_resolutionOption.AddItem("1920x1080");
		_resolutionOption.AddItem("1680x1050");
		_resolutionOption.AddItem("1600x900");
		_resolutionOption.AddItem("1366x768");
		_resolutionOption.AddItem("1280x720");
		_resolutionOption.Selected = 0;
		_resolutionOption.ItemSelected += OnResolutionChanged;
		resolutionContainer.AddChild(resolutionLabel);
		resolutionContainer.AddChild(_resolutionOption);
		_settingsContainer.AddChild(resolutionContainer);
		
		// Buttons
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		
		_resetButton = new Button();
		_resetButton.Text = "Reset to Default";
		_resetButton.Pressed += OnResetPressed;
		buttonContainer.AddChild(_resetButton);
		
		_closeButton = new Button();
		_closeButton.Text = "Close";
		_closeButton.Pressed += OnClosePressed;
		buttonContainer.AddChild(_closeButton);
		
		_settingsContainer.AddChild(buttonContainer);
	}
	
	private void CreateSliderSetting(string labelText, float minValue, float maxValue, float defaultValue,
		out HSlider slider, out Label valueLabel, System.Action<double> callback)
	{
		var container = new HBoxContainer();
		
		var label = new Label();
		label.Text = labelText + ": ";
		label.CustomMinimumSize = new Vector2(150, 0);
		container.AddChild(label);
		
		slider = new HSlider();
		slider.MinValue = minValue;
		slider.MaxValue = maxValue;
		slider.Value = defaultValue;
		slider.Step = 0.1;
		slider.CustomMinimumSize = new Vector2(200, 0);
		slider.ValueChanged += (double value) => callback(value);
		container.AddChild(slider);
		
		valueLabel = new Label();
		valueLabel.Text = defaultValue.ToString("F1");
		valueLabel.CustomMinimumSize = new Vector2(50, 0);
		container.AddChild(valueLabel);
		
		_settingsContainer.AddChild(container);
	}
	
	private void OnMouseSensitivityChanged(double value)
	{
		_mouseSensitivity = (float)value;
		_mouseSensitivityLabel.Text = _mouseSensitivity.ToString("F1");
		
		// Apply to player controller immediately
		var players = GetTree().GetNodesInGroup("local_players");
		foreach (Node player in players)
		{
			if (player is ModernPlayerController controller)
			{
				controller.MouseSensitivity = _mouseSensitivity;
			}
		}
		
		EmitSignal(SignalName.MouseSensitivityChanged, _mouseSensitivity);
		SaveSettings();
	}
	
	private void OnMasterVolumeChanged(double value)
	{
		_masterVolume = (float)value;
		_masterVolumeLabel.Text = _masterVolume.ToString("F1");
		AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), 
			Mathf.LinearToDb(_masterVolume));
		SaveSettings();
	}
	
	private void OnFOVChanged(double value)
	{
		_fieldOfView = (float)value;
		_fovLabel.Text = _fieldOfView.ToString("F0") + "°";
		
		// Apply to camera immediately
		var players = GetTree().GetNodesInGroup("local_players");
		foreach (Node player in players)
		{
			if (player is ModernPlayerController controller && controller.IsLocalPlayer)
			{
				var camera = controller.GetNode<Camera3D>("CameraHolder/Camera");
				if (camera != null)
				{
					camera.Fov = _fieldOfView;
				}
			}
		}
		SaveSettings();
	}
	
	private void OnVSyncToggled(bool pressed)
	{
		_vsyncEnabled = pressed;
		DisplayServer.WindowSetVsyncMode(_vsyncEnabled ? 
			DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
		SaveSettings();
	}
	
	private void OnResolutionChanged(long index)
	{
		Vector2I resolution = index switch
		{
			0 => new Vector2I(1920, 1080),
			1 => new Vector2I(1680, 1050),
			2 => new Vector2I(1600, 900),
			3 => new Vector2I(1366, 768),
			4 => new Vector2I(1280, 720),
			_ => new Vector2I(1920, 1080)
		};
		
		DisplayServer.WindowSetSize(resolution);
		SaveSettings();
	}
	
	private void OnResetPressed()
	{
		_mouseSensitivity = 1.0f;
		_masterVolume = 1.0f;
		_fieldOfView = 75.0f;
		_vsyncEnabled = true;
		
		_mouseSensitivitySlider.Value = _mouseSensitivity;
		_masterVolumeSlider.Value = _masterVolume;
		_fovSlider.Value = _fieldOfView;
		_vsyncCheckbox.ButtonPressed = _vsyncEnabled;
		_resolutionOption.Selected = 0;
		
		SaveSettings();
		ApplyAllSettings();
	}
	
	private void OnClosePressed()
	{
		HideSettings();
	}
	
	public void ShowSettings()
	{
		Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}
	
	public void HideSettings()
	{
		Visible = false;
		// Don't change mouse mode here - let the main game handle it
	}
	
	private void ApplyAllSettings()
	{
		OnMouseSensitivityChanged(_mouseSensitivity);
		OnMasterVolumeChanged(_masterVolume);
		OnFOVChanged(_fieldOfView);
		OnVSyncToggled(_vsyncEnabled);
	}
	
	private void SaveSettings()
	{
		var config = new ConfigFile();
		config.SetValue("video", "mouse_sensitivity", _mouseSensitivity);
		config.SetValue("audio", "master_volume", _masterVolume);
		config.SetValue("video", "field_of_view", _fieldOfView);
		config.SetValue("video", "vsync_enabled", _vsyncEnabled);
		config.SetValue("video", "resolution", _resolutionOption.Selected);
		
		config.Save("user://settings.cfg");
	}
	
	private void LoadSettings()
	{
		var config = new ConfigFile();
		var error = config.Load("user://settings.cfg");
		
		if (error == Error.Ok)
		{
			_mouseSensitivity = (float)config.GetValue("video", "mouse_sensitivity", 1.0f);
			_masterVolume = (float)config.GetValue("audio", "master_volume", 1.0f);
			_fieldOfView = (float)config.GetValue("video", "field_of_view", 75.0f);
			_vsyncEnabled = (bool)config.GetValue("video", "vsync_enabled", true);
			var resolutionIndex = (int)config.GetValue("video", "resolution", 0);
			
			// Apply to UI
			_mouseSensitivitySlider.Value = _mouseSensitivity;
			_masterVolumeSlider.Value = _masterVolume;
			_fovSlider.Value = _fieldOfView;
			_vsyncCheckbox.ButtonPressed = _vsyncEnabled;
			_resolutionOption.Selected = resolutionIndex;
			
			// Apply settings
			ApplyAllSettings();
		}
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 