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
	private ModernPlayerController _player;

	public override void _Ready()
	{
		// Allow UI to process input even when the game is paused
		ProcessMode = ProcessModeEnum.Always;
		
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
		_pauseMenu.ProcessMode = ProcessModeEnum.Always; // Allow processing when paused
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
		_gameOverScreen.ProcessMode = ProcessModeEnum.Always; // Allow processing when paused
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
		_settingsMenu.ProcessMode = ProcessModeEnum.Always; // Allow processing when paused
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
		// Find and connect to player
		_player = GetTree().CurrentScene.GetNode<ModernPlayerController>("Player");
		if (_player != null)
		{
			_player.HealthChanged += OnHealthChanged;
			_player.PlayerDied += OnPlayerDied;
			_player.PlayerRespawned += OnPlayerRespawned;
			
			// Connect to weapon system
			var weaponSystem = _player.GetNode<ModernWeaponSystem>("CameraHolder/GunPoint/WeaponSystem");
			if (weaponSystem != null)
			{
				weaponSystem.AmmoChanged += OnAmmoChanged;
				weaponSystem.ReloadStarted += OnReloadStarted;
				weaponSystem.ReloadCompleted += OnReloadCompleted;
				weaponSystem.WeaponChanged += OnWeaponChanged;
			}
		}

		// Connect to GameManager
		if (GameManager.Instance != null)
		{
			GameManager.Instance.GameStateChanged += OnGameStateChanged;
			GameManager.Instance.ScoreChanged += OnScoreChanged;
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			TogglePause();
		}
	}

	private void TogglePause()
	{
		if (_isGameOver) return;

		if (_isPaused)
		{
			ResumeGame();
		}
		else
		{
			PauseGame();
		}
	}

	private void PauseGame()
	{
		if (_isPaused) return;

		_isPaused = true;
		_pauseMenu.Visible = true;
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		EmitSignal(SignalName.GamePaused);
	}

	private void ResumeGame()
	{
		if (!_isPaused) return;

		_isPaused = false;
		_pauseMenu.Visible = false;
		_settingsMenu.Visible = false;
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		EmitSignal(SignalName.GameResumed);
	}



	private void OnHealthChanged(float health, float maxHealth)
	{
		_healthBar.MaxValue = maxHealth;
		_healthBar.Value = health;
		_healthLabel.Text = $"Health: {health:F0}/{maxHealth:F0}";
		
		// Change color based on health percentage
		var healthPercent = health / maxHealth;
		if (healthPercent > 0.6f)
		{
			_healthBar.Modulate = Colors.Green;
		}
		else if (healthPercent > 0.3f)
		{
			_healthBar.Modulate = Colors.Yellow;
		}
		else
		{
			_healthBar.Modulate = Colors.Red;
		}
	}

	private void OnAmmoChanged(int currentAmmo, int maxAmmo)
	{
		_ammoLabel.Text = $"Ammo: {currentAmmo}/{maxAmmo}";
		
		// Change color based on ammo level
		var ammoPercent = (float)currentAmmo / maxAmmo;
		if (ammoPercent > 0.3f)
		{
			_ammoLabel.Modulate = Colors.White;
		}
		else if (ammoPercent > 0.1f)
		{
			_ammoLabel.Modulate = Colors.Yellow;
		}
		else
		{
			_ammoLabel.Modulate = Colors.Red;
		}
	}

	private void OnReloadStarted()
	{
		_reloadLabel.Text = "RELOADING...";
		_reloadLabel.Visible = true;
	}

	private void OnReloadCompleted()
	{
		_reloadLabel.Visible = false;
	}
	
	private void OnWeaponChanged(string weaponName)
	{
		// Could show weapon name in UI
		GD.Print($"Weapon changed to: {weaponName}");
	}

	private void OnPlayerDied()
	{
		_isGameOver = true;
		_gameOverScreen.Visible = true;
		_pauseMenu.Visible = false;
	}

	private void OnPlayerRespawned()
	{
		_isGameOver = false;
		_gameOverScreen.Visible = false;
	}

	private void OnGameStateChanged(GameState newState)
	{
		switch (newState)
		{
			case GameState.Paused:
				_pauseMenu.Visible = true;
				break;
			case GameState.Playing:
				_pauseMenu.Visible = false;
				_settingsMenu.Visible = false;
				break;
			case GameState.GameOver:
				_gameOverScreen.Visible = true;
				break;
		}
	}

	private void OnScoreChanged(int newScore)
	{
		// Could add score display here
	}

	// Button event handlers
	private void OnResumePressed()
	{
		ResumeGame();
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

	private void OnRespawnPressed()
	{
		GameManager.Instance?.StartNewGame();
	}

	private void OnQuitPressed()
	{
		GetTree().Quit();
	}

	private void OnSensitivityChanged(double value)
	{
		if (_player != null)
		{
			_player.MouseSensitivity = (float)value;
		}
	}
}
