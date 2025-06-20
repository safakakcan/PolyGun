using Godot;
using System;

/// <summary>
/// Main game controller that manages networking systems and overall game flow
/// Handles initialization of multiplayer systems and scene management
/// </summary>
public partial class GameMain : Node
{
	public static GameMain Instance { get; private set; }
	
	// Core systems
	private NetworkTick _networkTick;
	private AuthoritativeServer _authoritativeServer;
	private ClientPrediction _clientPrediction;
	private MultiplayerGameManager _gameManager;
	
	// UI
	private MultiplayerLobbyUI _lobbyUI;
	private SettingsUI _settingsUI;
	
	// Game world
	private Node3D _gameWorld;
	private Godot.Environment _environment;
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			InitializeGame();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void InitializeGame()
	{
		GD.Print("PolyGun: Initializing game systems...");
		
		// Initialize networking systems
		InitializeNetworkingSystems();
		
		// Create UI
		CreateUI();
		
		// Setup input handling
		SetupInput();
		
		// Start with a local player for immediate gameplay
		SpawnLocalPlayer();
		
		// Debug: Print input actions
		GD.Print("Available input actions:");
		foreach (var action in InputMap.GetActions())
		{
			GD.Print($"  - {action}");
		}
		
		GD.Print("PolyGun: Game initialization complete!");
	}
	
	private void InitializeNetworkingSystems()
	{
		// Create NetworkTick (handles timing)
		_networkTick = new NetworkTick();
		_networkTick.Name = "NetworkTick";
		AddChild(_networkTick);
		
		// Create AuthoritativeServer (server-side simulation)
		_authoritativeServer = new AuthoritativeServer();
		_authoritativeServer.Name = "AuthoritativeServer";
		AddChild(_authoritativeServer);
		
		// Create ClientPrediction (client-side prediction)
		_clientPrediction = new ClientPrediction();
		_clientPrediction.Name = "ClientPrediction";
		AddChild(_clientPrediction);
		
		// Create MultiplayerGameManager (lobby and game flow)
		_gameManager = new MultiplayerGameManager();
		_gameManager.Name = "MultiplayerGameManager";
		AddChild(_gameManager);
		
		// Connect systems
		ConnectNetworkingSystems();
	}
	
	private void ConnectNetworkingSystems()
	{
		// NetworkTick connects to both server and client systems automatically
		// AuthoritativeServer handles server-side logic
		// ClientPrediction handles client-side prediction
		// MultiplayerGameManager handles lobby and game state
		
		GD.Print("Networking systems connected successfully");
	}
	
	private void CreateUI()
	{
		// Create multiplayer game manager
		_gameManager = new MultiplayerGameManager();
		_gameManager.Name = "MultiplayerGameManager";
		AddChild(_gameManager);
		
		// Create lobby UI
		_lobbyUI = new MultiplayerLobbyUI();
		_lobbyUI.Name = "MultiplayerLobbyUI";
		AddChild(_lobbyUI);
		
		// Create settings UI
		_settingsUI = new SettingsUI();
		_settingsUI.Name = "SettingsUI";
		AddChild(_settingsUI);
		
		// Create game HUD
		var gameHUD = new GameHUD();
		gameHUD.Name = "GameHUD";
		AddChild(gameHUD);
		
		// Connect UI systems
		if (_gameManager != null && _lobbyUI != null)
		{
			// UI systems are connected through GameMain instance
			GD.Print("UI systems ready");
		}
		
		GD.Print("UI systems created and connected");
	}
	
	private void SetupInput()
	{
		// Ensure mouse is visible for UI interaction initially
		Input.MouseMode = Input.MouseModeEnum.Visible;
		
		// Set up input map if needed
		SetupInputMap();
	}
	
	private void SetupInputMap()
	{
		// Check if actions exist, if not create them
		if (!InputMap.HasAction("move_forward"))
		{
			InputMap.AddAction("move_forward");
			var keyW = new InputEventKey();
			keyW.Keycode = Key.W;
			InputMap.ActionAddEvent("move_forward", keyW);
		}
		
		if (!InputMap.HasAction("move_backward"))
		{
			InputMap.AddAction("move_backward");
			var keyS = new InputEventKey();
			keyS.Keycode = Key.S;
			InputMap.ActionAddEvent("move_backward", keyS);
		}
		
		if (!InputMap.HasAction("move_left"))
		{
			InputMap.AddAction("move_left");
			var keyA = new InputEventKey();
			keyA.Keycode = Key.A;
			InputMap.ActionAddEvent("move_left", keyA);
		}
		
		if (!InputMap.HasAction("move_right"))
		{
			InputMap.AddAction("move_right");
			var keyD = new InputEventKey();
			keyD.Keycode = Key.D;
			InputMap.ActionAddEvent("move_right", keyD);
		}
		
		if (!InputMap.HasAction("jump"))
		{
			InputMap.AddAction("jump");
			var keySpace = new InputEventKey();
			keySpace.Keycode = Key.Space;
			InputMap.ActionAddEvent("jump", keySpace);
		}
		
		if (!InputMap.HasAction("sprint"))
		{
			InputMap.AddAction("sprint");
			var keyShift = new InputEventKey();
			keyShift.Keycode = Key.Shift;
			InputMap.ActionAddEvent("sprint", keyShift);
		}
		
		if (!InputMap.HasAction("fire"))
		{
			InputMap.AddAction("fire");
			var mouseLeft = new InputEventMouseButton();
			mouseLeft.ButtonIndex = MouseButton.Left;
			InputMap.ActionAddEvent("fire", mouseLeft);
		}
		
		if (!InputMap.HasAction("aim"))
		{
			InputMap.AddAction("aim");
			var mouseRight = new InputEventMouseButton();
			mouseRight.ButtonIndex = MouseButton.Right;
			InputMap.ActionAddEvent("aim", mouseRight);
		}
		
		if (!InputMap.HasAction("reload"))
		{
			InputMap.AddAction("reload");
			var keyR = new InputEventKey();
			keyR.Keycode = Key.R;
			InputMap.ActionAddEvent("reload", keyR);
		}
		
		// Add "shoot" action as alias for "fire"
		if (!InputMap.HasAction("shoot"))
		{
			InputMap.AddAction("shoot");
			var mouseLeft2 = new InputEventMouseButton();
			mouseLeft2.ButtonIndex = MouseButton.Left;
			InputMap.ActionAddEvent("shoot", mouseLeft2);
		}
		
		// Add weapon switching actions
		if (!InputMap.HasAction("weapon_next"))
		{
			InputMap.AddAction("weapon_next");
			var keyQ = new InputEventKey();
			keyQ.Keycode = Key.Q;
			InputMap.ActionAddEvent("weapon_next", keyQ);
		}
		
		if (!InputMap.HasAction("weapon_prev"))
		{
			InputMap.AddAction("weapon_prev");
			var keyE = new InputEventKey();
			keyE.Keycode = Key.E;
			InputMap.ActionAddEvent("weapon_prev", keyE);
		}
		
		// Add direct weapon selection
		for (int i = 1; i <= 3; i++)
		{
			string actionName = $"weapon_{i}";
			if (!InputMap.HasAction(actionName))
			{
				InputMap.AddAction(actionName);
				var key = new InputEventKey();
				key.Keycode = (Key)(48 + i); // Key.Key1, Key.Key2, Key.Key3
				InputMap.ActionAddEvent(actionName, key);
			}
		}
	}
	
	public override void _Input(InputEvent @event)
	{
		// Handle ESC key specifically
		if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			GD.Print($"ESC pressed! Current mouse mode: {Input.MouseMode}");
			
			// If settings is open, close it first
			if (_settingsUI != null && _settingsUI.Visible)
			{
				_settingsUI.HideSettings();
				// Determine what mouse mode to return to
				if (_lobbyUI != null && _lobbyUI.Visible)
				{
					Input.MouseMode = Input.MouseModeEnum.Visible;
				}
				else
				{
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
				GetViewport().SetInputAsHandled();
				return;
			}
			
			// Toggle UI visibility and mouse mode
			if (_lobbyUI != null)
			{
				bool wasUIVisible = _lobbyUI.Visible;
				GD.Print($"UI was visible: {wasUIVisible}");
				
				if (wasUIVisible)
				{
					_lobbyUI.HideUI();
					Input.MouseMode = Input.MouseModeEnum.Captured;
					GD.Print("UI hidden, mouse captured");
				}
				else
				{
					_lobbyUI.ShowUI();
					Input.MouseMode = Input.MouseModeEnum.Visible;
					GD.Print("UI shown, mouse visible");
				}
				
				// Prevent other input handlers from processing this event
				GetViewport().SetInputAsHandled();
			}
			return;
		}
		
		// Handle Tab key for settings
		if (@event is InputEventKey tabKey && tabKey.Pressed && tabKey.Keycode == Key.Tab)
		{
			if (_settingsUI != null)
			{
				if (_settingsUI.Visible)
				{
					_settingsUI.HideSettings();
					// Return to previous mouse mode
					if (_lobbyUI != null && _lobbyUI.Visible)
					{
						Input.MouseMode = Input.MouseModeEnum.Visible;
					}
					else
					{
						Input.MouseMode = Input.MouseModeEnum.Captured;
					}
				}
				else
				{
					_settingsUI.ShowSettings();
				}
				GetViewport().SetInputAsHandled();
			}
			return;
		}
		
		// Handle ui_cancel action as backup
		if (@event.IsActionPressed("ui_cancel"))
		{
			GD.Print("ui_cancel action pressed as backup");
			if (_lobbyUI != null)
			{
				bool wasUIVisible = _lobbyUI.Visible;
				if (wasUIVisible)
				{
					_lobbyUI.HideUI();
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
				else
				{
					_lobbyUI.ShowUI();
					Input.MouseMode = Input.MouseModeEnum.Visible;
				}
				GetViewport().SetInputAsHandled();
			}
			return;
		}
		
		// F1 key for quick multiplayer menu access
		if (@event is InputEventKey keyF1 && keyF1.Pressed)
		{
			if (keyF1.Keycode == Key.F1)
			{
				if (_lobbyUI != null)
				{
					_lobbyUI.ShowUI();
					Input.MouseMode = Input.MouseModeEnum.Visible;
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}
	
	public void StartMultiplayerGame()
	{
		// Called when the game actually starts
		if (_lobbyUI != null)
		{
			_lobbyUI.HideUI();
		}
		
		// Capture mouse for FPS gameplay
		Input.MouseMode = Input.MouseModeEnum.Captured;
		
		GD.Print("Multiplayer game started!");
	}
	
	public void EndMultiplayerGame()
	{
		// Called when the game ends
		if (_lobbyUI != null)
		{
			_lobbyUI.ShowUI();
		}
		
		// Release mouse
		Input.MouseMode = Input.MouseModeEnum.Visible;
		
		GD.Print("Multiplayer game ended!");
	}
	
	private void SpawnLocalPlayer()
	{
		// Create a local player for immediate gameplay
		var localPlayer = CreatePlayerInstance(1, "Local Player");
		localPlayer.Position = new Vector3(0, 1, 0);
		
		// Add to local players group for easy cleanup
		localPlayer.AddToGroup("local_players");
		
		// Hide UI initially and capture mouse for FPS gameplay
		if (_lobbyUI != null)
		{
			_lobbyUI.HideUI();
		}
		Input.MouseMode = Input.MouseModeEnum.Captured;
		
		GD.Print("Local player spawned for immediate gameplay!");
		GD.Print("Press ESC or F1 to open multiplayer menu!");
	}
	
	private ModernPlayerController CreatePlayerInstance(int playerId, string playerName)
	{
		// Create player instance
		var player = new ModernPlayerController();
		player.NetworkId = playerId;
		player.PlayerName = playerName;
		player.Name = $"Player_{playerId}";
		
		// Add to game world
		_gameWorld.AddChild(player, true);
		
		return player;
	}
	
	// Utility methods for accessing game systems
	public Node3D GetGameWorld() => _gameWorld;
	public NetworkTick GetNetworkTick() => _networkTick;
	public AuthoritativeServer GetAuthoritativeServer() => _authoritativeServer;
	public ClientPrediction GetClientPrediction() => _clientPrediction;
	public MultiplayerGameManager GetGameManager() => _gameManager;
	public MultiplayerLobbyUI GetLobbyUI() => _lobbyUI;
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 
