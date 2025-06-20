using Godot;
using System;

public partial class UIManager : Control
{
	[Signal] public delegate void GamePausedEventHandler();
	[Signal] public delegate void GameResumedEventHandler();

	// UI Elements
	private Label _healthLabel;
	private ProgressBar _healthBar;
	private Label _ammoLabel;
	private Label _reloadLabel;
	private Control _crosshair;
	private Control _pauseMenu;
	private Control _gameOverScreen;
	private Control _settingsMenu;
	
	// Game State
	private bool _isPaused = false;
	private bool _isGameOver = false;
	
	// References
	private PlayerController _player;

	public override void _Ready()
	{
		SetupUI();
		ConnectSignals();
	}

	private void SetupUI()
	{
		// Create main UI container
		var mainUI = new Control();
		mainUI.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(mainUI);

		// Setup HUD
		SetupHUD(mainUI);
		
		// Setup Crosshair
		SetupCrosshair(mainUI);
		
		// Setup Menus
		SetupPauseMenu(mainUI);
		SetupGameOverScreen(mainUI);
		SetupSettingsMenu(mainUI);
	}

	private void SetupHUD(Control parent)
	{
		// Health UI
		var healthContainer = new HBoxContainer();
		healthContainer.Position = new Vector2(20, 20);
		parent.AddChild(healthContainer);

		var healthIcon = new Label();
		healthIcon.Text = "❤️";
		healthContainer.AddChild(healthIcon);

		_healthBar = new ProgressBar();
		_healthBar.Size = new Vector2(200, 30);
		_healthBar.MinValue = 0;
		_healthBar.MaxValue = 100;
		_healthBar.Value = 100;
		_healthBar.ShowPercentage = false;
		healthContainer.AddChild(_healthBar);

		_healthLabel = new Label();
		_healthLabel.Text = "100/100";
		_healthLabel.Position = new Vector2(10, 0);
		healthContainer.AddChild(_healthLabel);

		// Ammo UI
		var ammoContainer = new VBoxContainer();
		ammoContainer.Position = new Vector2(GetViewport().GetVisibleRect().Size.X - 150, 
											GetViewport().GetVisibleRect().Size.Y - 100);
		parent.AddChild(ammoContainer);

		_ammoLabel = new Label();
		_ammoLabel.Text = "30/30";
		_ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_ammoLabel.AddThemeFontSizeOverride("font_size", 24);
		ammoContainer.AddChild(_ammoLabel);

		_reloadLabel = new Label();
		_reloadLabel.Text = "RELOADING...";
		_reloadLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_reloadLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		_reloadLabel.AddThemeFontSizeOverride("font_size", 18);
		_reloadLabel.Visible = false;
		ammoContainer.AddChild(_reloadLabel);
	}

	private void SetupCrosshair(Control parent)
	{
		_crosshair = new Control();
		_crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		parent.AddChild(_crosshair);

		// Create crosshair lines
		var crosshairSize = 20f;
		var crosshairThickness = 2f;
		
		// Horizontal line
		var hLine = new ColorRect();
		hLine.Color = Colors.White;
		hLine.Size = new Vector2(crosshairSize, crosshairThickness);
		hLine.Position = new Vector2(-crosshairSize / 2, -crosshairThickness / 2);
		_crosshair.AddChild(hLine);

		// Vertical line
		var vLine = new ColorRect();
		vLine.Color = Colors.White;
		vLine.Size = new Vector2(crosshairThickness, crosshairSize);
		vLine.Position = new Vector2(-crosshairThickness / 2, -crosshairSize / 2);
		_crosshair.AddChild(vLine);
	}

	private void SetupPauseMenu(Control parent)
	{
		_pauseMenu = new Control();
		_pauseMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_pauseMenu.Visible = false;
		parent.AddChild(_pauseMenu);

		// Semi-transparent background
		var background = new ColorRect();
		background.Color = new Color(0, 0, 0, 0.7f);
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_pauseMenu.AddChild(background);

		// Menu container
		var menuContainer = new VBoxContainer();
		menuContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_pauseMenu.AddChild(menuContainer);

		// Title
		var title = new Label();
		title.Text = "PAUSED";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 32);
		menuContainer.AddChild(title);

		// Resume button
		var resumeButton = new Button();
		resumeButton.Text = "Resume";
		resumeButton.Size = new Vector2(200, 50);
		resumeButton.Pressed += OnResumePressed;
		menuContainer.AddChild(resumeButton);

		// Settings button
		var settingsButton = new Button();
		settingsButton.Text = "Settings";
		settingsButton.Size = new Vector2(200, 50);
		settingsButton.Pressed += OnSettingsPressed;
		menuContainer.AddChild(settingsButton);

		// Quit button
		var quitButton = new Button();
		quitButton.Text = "Quit to Menu";
		quitButton.Size = new Vector2(200, 50);
		quitButton.Pressed += OnQuitPressed;
		menuContainer.AddChild(quitButton);
	}

	private void SetupGameOverScreen(Control parent)
	{
		_gameOverScreen = new Control();
		_gameOverScreen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_gameOverScreen.Visible = false;
		parent.AddChild(_gameOverScreen);

		// Semi-transparent background
		var background = new ColorRect();
		background.Color = new Color(0.2f, 0, 0, 0.8f);
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_gameOverScreen.AddChild(background);

		// Game Over container
		var gameOverContainer = new VBoxContainer();
		gameOverContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_gameOverScreen.AddChild(gameOverContainer);

		// Title
		var title = new Label();
		title.Text = "GAME OVER";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeColorOverride("font_color", Colors.Red);
		title.AddThemeFontSizeOverride("font_size", 36);
		gameOverContainer.AddChild(title);

		// Respawn button
		var respawnButton = new Button();
		respawnButton.Text = "Respawn";
		respawnButton.Size = new Vector2(200, 50);
		respawnButton.Pressed += OnRespawnPressed;
		gameOverContainer.AddChild(respawnButton);

		// Quit button
		var quitButton = new Button();
		quitButton.Text = "Quit";
		quitButton.Size = new Vector2(200, 50);
		quitButton.Pressed += OnQuitPressed;
		gameOverContainer.AddChild(quitButton);
	}

	private void SetupSettingsMenu(Control parent)
	{
		_settingsMenu = new Control();
		_settingsMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_settingsMenu.Visible = false;
		parent.AddChild(_settingsMenu);

		// Semi-transparent background
		var background = new ColorRect();
		background.Color = new Color(0, 0, 0, 0.8f);
		background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_settingsMenu.AddChild(background);

		// Settings container
		var settingsContainer = new VBoxContainer();
		settingsContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_settingsMenu.AddChild(settingsContainer);

		// Title
		var title = new Label();
		title.Text = "SETTINGS";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 28);
		settingsContainer.AddChild(title);

		// Mouse sensitivity slider
		var sensitivityLabel = new Label();
		sensitivityLabel.Text = "Mouse Sensitivity";
		settingsContainer.AddChild(sensitivityLabel);

		var sensitivitySlider = new HSlider();
		sensitivitySlider.MinValue = 0.01f;
		sensitivitySlider.MaxValue = 1.0f;
		sensitivitySlider.Value = 0.1f;
		sensitivitySlider.Step = 0.01f;
		sensitivitySlider.Size = new Vector2(300, 30);
		sensitivitySlider.ValueChanged += OnSensitivityChanged;
		settingsContainer.AddChild(sensitivitySlider);

		// Back button
		var backButton = new Button();
		backButton.Text = "Back";
		backButton.Size = new Vector2(200, 50);
		backButton.Pressed += OnSettingsBackPressed;
		settingsContainer.AddChild(backButton);
	}

	private void ConnectSignals()
	{
		// Find player and connect to its signals
		var scene = GetTree().CurrentScene;
		_player = scene.GetNode<PlayerController>("Player");
		
		if (_player != null)
		{
			_player.HealthChanged += OnHealthChanged;
			_player.AmmoChanged += OnAmmoChanged;
			_player.PlayerDied += OnPlayerDied;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel")) // ESC key
		{
			TogglePause();
		}
	}

	public void TogglePause()
	{
		_isPaused = !_isPaused;
		_pauseMenu.Visible = _isPaused;
		
		GetTree().Paused = _isPaused;
		
		if (_isPaused)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			EmitSignal(SignalName.GamePaused);
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			EmitSignal(SignalName.GameResumed);
		}
	}

	public void ShowReloadIndicator(bool show)
	{
		_reloadLabel.Visible = show;
	}

	private void OnHealthChanged(int newHealth)
	{
		_healthBar.Value = newHealth;
		_healthLabel.Text = $"{newHealth}/{_player.MaxHealth}";
		
		// Change color based on health
		if (newHealth < 30)
		{
			_healthBar.Modulate = Colors.Red;
		}
		else if (newHealth < 60)
		{
			_healthBar.Modulate = Colors.Yellow;
		}
		else
		{
			_healthBar.Modulate = Colors.Green;
		}
	}

	private void OnAmmoChanged(int currentAmmo, int maxAmmo)
	{
		_ammoLabel.Text = $"{currentAmmo}/{maxAmmo}";
		
		// Change color if low on ammo
		if (currentAmmo <= 5)
		{
			_ammoLabel.Modulate = Colors.Red;
		}
		else
		{
			_ammoLabel.Modulate = Colors.White;
		}
		
		// Don't show reload indicator here - the weapon system will call ShowReloadIndicator directly
		// when reload state changes via signals
	}

	private void OnPlayerDied()
	{
		_isGameOver = true;
		_gameOverScreen.Visible = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void OnResumePressed()
	{
		TogglePause();
	}

	private void OnSettingsPressed()
	{
		_pauseMenu.Visible = false;
		_settingsMenu.Visible = true;
	}

	private void OnSettingsBackPressed()
	{
		_settingsMenu.Visible = false;
		_pauseMenu.Visible = true;
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}

	private void OnRespawnPressed()
	{
		_isGameOver = false;
		_gameOverScreen.Visible = false;
		
		if (_player != null)
		{
			_player.Respawn();
		}
		
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void OnSensitivityChanged(double value)
	{
		if (_player != null)
		{
			_player.MouseSensitivity = (float)value;
		}
	}
} 