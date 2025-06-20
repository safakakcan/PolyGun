using Godot;
using System;

/// <summary>
/// Simple game HUD that displays health, ammo, and game information
/// </summary>
public partial class GameHUD : Control
{
	public static GameHUD Instance { get; private set; }
	
	// UI Elements
	private Label _healthLabel;
	private Label _ammoLabel;
	private Label _scoreLabel;
	private Label _fpsLabel;
	private Label _pingLabel;
	private Panel _crosshair;
	private Label _controlsInfo;
	
	// Data
	private int _health = 100;
	private int _ammo = 30;
	private int _score = 0;
	private float _ping = 0.0f;
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			CreateHUD();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void CreateHUD()
	{
		// Set up as overlay
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		MouseFilter = Control.MouseFilterEnum.Ignore;
		
		// Create health display (bottom left)
		_healthLabel = new Label();
		_healthLabel.Text = "Health: 100";
		_healthLabel.Position = new Vector2(20, GetViewportRect().Size.Y - 80);
		_healthLabel.AddThemeColorOverride("font_color", Colors.Green);
		_healthLabel.AddThemeFontSizeOverride("font_size", 24);
		AddChild(_healthLabel);
		
		// Create ammo display (bottom right)
		_ammoLabel = new Label();
		_ammoLabel.Text = "Ammo: 30/120";
		_ammoLabel.Position = new Vector2(GetViewportRect().Size.X - 200, GetViewportRect().Size.Y - 80);
		_ammoLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		_ammoLabel.AddThemeFontSizeOverride("font_size", 24);
		AddChild(_ammoLabel);
		
		// Create score display (top left)
		_scoreLabel = new Label();
		_scoreLabel.Text = "Score: 0";
		_scoreLabel.Position = new Vector2(20, 20);
		_scoreLabel.AddThemeColorOverride("font_color", Colors.White);
		_scoreLabel.AddThemeFontSizeOverride("font_size", 20);
		AddChild(_scoreLabel);
		
		// Create FPS display (top right)
		_fpsLabel = new Label();
		_fpsLabel.Text = "FPS: 60";
		_fpsLabel.Position = new Vector2(GetViewportRect().Size.X - 120, 20);
		_fpsLabel.AddThemeColorOverride("font_color", Colors.Cyan);
		_fpsLabel.AddThemeFontSizeOverride("font_size", 16);
		AddChild(_fpsLabel);
		
		// Create ping display (top right, below FPS)
		_pingLabel = new Label();
		_pingLabel.Text = "Ping: 0ms";
		_pingLabel.Position = new Vector2(GetViewportRect().Size.X - 120, 50);
		_pingLabel.AddThemeColorOverride("font_color", Colors.LightGray);
		_pingLabel.AddThemeFontSizeOverride("font_size", 14);
		AddChild(_pingLabel);
		
		// Create crosshair (center)
		CreateCrosshair();
		
		// Create controls info (top center)
		CreateControlsInfo();
		
		GD.Print("Game HUD created");
	}
	
	private void CreateCrosshair()
	{
		var centerX = GetViewportRect().Size.X / 2;
		var centerY = GetViewportRect().Size.Y / 2;
		
		// Main crosshair panel
		_crosshair = new Panel();
		_crosshair.Position = new Vector2(centerX - 15, centerY - 15);
		_crosshair.Size = new Vector2(30, 30);
		_crosshair.MouseFilter = Control.MouseFilterEnum.Ignore;
		
		// Make panel transparent
		var style = new StyleBoxFlat();
		style.BgColor = Colors.Transparent;
		_crosshair.AddThemeStyleboxOverride("panel", style);
		
		// Create crosshair lines
		var lineColor = new Color(1, 1, 1, 0.8f);
		
		// Horizontal line
		var hLine = new ColorRect();
		hLine.Position = new Vector2(10, 14);
		hLine.Size = new Vector2(10, 2);
		hLine.Color = lineColor;
		_crosshair.AddChild(hLine);
		
		// Vertical line
		var vLine = new ColorRect();
		vLine.Position = new Vector2(14, 10);
		vLine.Size = new Vector2(2, 10);
		vLine.Color = lineColor;
		_crosshair.AddChild(vLine);
		
		AddChild(_crosshair);
	}
	
	private void CreateControlsInfo()
	{
		// Create controls info container
		var controlsContainer = new VBoxContainer();
		controlsContainer.Position = new Vector2(GetViewportRect().Size.X / 2 - 250, 60);
		controlsContainer.AddThemeConstantOverride("separation", 5);
		
		// Create multiple lines of controls info
		var controls = new string[]
		{
			"WASD - Move | Shift - Sprint | Space - Jump | Mouse - Look",
			"ESC - Menu | Tab - Settings | F1 - Multiplayer"
		};
		
		foreach (var control in controls)
		{
			var label = new Label();
			label.Text = control;
			label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.7f));
			label.AddThemeFontSizeOverride("font_size", 14);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			controlsContainer.AddChild(label);
		}
		
		AddChild(controlsContainer);
		_controlsInfo = controlsContainer.GetChild<Label>(0); // Store reference for fading
		
		// Create a timer to fade out the controls info
		var timer = new Timer();
		timer.WaitTime = 8.0f;
		timer.OneShot = true;
		timer.Timeout += () => FadeOutControlsInfo(controlsContainer);
		AddChild(timer);
		timer.Start();
	}
	
	private void FadeOutControlsInfo(Control controlsContainer)
	{
		// Create a tween to fade out the controls info
		var tween = CreateTween();
		tween.TweenProperty(controlsContainer, "modulate:a", 0.0f, 2.0f);
		tween.TweenCallback(Callable.From(() => controlsContainer.QueueFree()));
	}
	
	public override void _Process(double delta)
	{
		// Update FPS counter
		_fpsLabel.Text = $"FPS: {Engine.GetFramesPerSecond()}";
		
		// Update ping if in multiplayer
		if (Multiplayer.HasMultiplayerPeer())
		{
			// Simple ping calculation (this is a placeholder)
			_pingLabel.Text = $"Ping: {Mathf.RoundToInt(_ping)}ms";
			_pingLabel.Visible = true;
		}
		else
		{
			_pingLabel.Visible = false;
		}
	}
	
	public void UpdateHealth(int health)
	{
		_health = health;
		_healthLabel.Text = $"Health: {_health}";
		
		// Change color based on health
		if (_health > 75)
			_healthLabel.AddThemeColorOverride("font_color", Colors.Green);
		else if (_health > 50)
			_healthLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		else if (_health > 25)
			_healthLabel.AddThemeColorOverride("font_color", Colors.Orange);
		else
			_healthLabel.AddThemeColorOverride("font_color", Colors.Red);
	}
	
	public void UpdateAmmo(int currentAmmo, int totalAmmo)
	{
		_ammo = currentAmmo;
		_ammoLabel.Text = $"Ammo: {currentAmmo}/{totalAmmo}";
		
		// Change color based on ammo
		if (currentAmmo > 10)
			_ammoLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		else if (currentAmmo > 5)
			_ammoLabel.AddThemeColorOverride("font_color", Colors.Orange);
		else
			_ammoLabel.AddThemeColorOverride("font_color", Colors.Red);
	}
	
	public void UpdateScore(int score)
	{
		_score = score;
		_scoreLabel.Text = $"Score: {_score}";
	}
	
	public void UpdatePing(float ping)
	{
		_ping = ping;
	}
	
	public void SetCrosshairVisible(bool visible)
	{
		if (_crosshair != null)
		{
			_crosshair.Visible = visible;
		}
	}
	
	public void ShowMessage(string message, float duration = 3.0f)
	{
		// Create temporary message label
		var messageLabel = new Label();
		messageLabel.Text = message;
		messageLabel.Position = new Vector2(GetViewportRect().Size.X / 2 - 100, GetViewportRect().Size.Y / 2 + 50);
		messageLabel.AddThemeColorOverride("font_color", Colors.White);
		messageLabel.AddThemeFontSizeOverride("font_size", 20);
		AddChild(messageLabel);
		
		// Remove after duration
		GetTree().CreateTimer(duration).Timeout += () => messageLabel.QueueFree();
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 