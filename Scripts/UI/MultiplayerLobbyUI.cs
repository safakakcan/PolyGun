using Godot;
using System;
using System.Linq;

/// <summary>
/// UI controller for the multiplayer lobby system
/// Provides interface for hosting, joining, ready status, and game start
/// </summary>
public partial class MultiplayerLobbyUI : Control
{
	// UI Elements
	private Button _hostButton;
	private Button _joinButton;
	private Button _readyButton;
	private Button _startGameButton;
	private LineEdit _serverAddressInput;
	private LineEdit _playerNameInput;
	private LineEdit _serverNameInput;
	private SpinBox _maxPlayersSpinBox;
	private SpinBox _minPlayersSpinBox;
	private RichTextLabel _playerListLabel;
	private Label _statusLabel;
	private Label _countdownLabel;
	private VBoxContainer _lobbyContainer;
	private VBoxContainer _gameContainer;
	
	// Network
	private ENetMultiplayerPeer _peer;
	private bool _isReady = false;
	
	public override void _Ready()
	{
		CreateUI();
		ConnectSignals();
		UpdateUI();
	}
	
	private void CreateUI()
	{
		// Main container
		var mainVBox = new VBoxContainer();
		AddChild(mainVBox);
		
		// Title
		var titleLabel = new Label();
		titleLabel.Text = "PolyGun Multiplayer";
		titleLabel.AddThemeStyleboxOverride("normal", new StyleBoxFlat());
		mainVBox.AddChild(titleLabel);
		
		// Lobby container
		_lobbyContainer = new VBoxContainer();
		mainVBox.AddChild(_lobbyContainer);
		
		// Server setup section
		var serverGroup = new VBoxContainer();
		var serverGroupLabel = new Label();
		serverGroupLabel.Text = "Server Setup";
		serverGroup.AddChild(serverGroupLabel);
		
		// Player name input
		var nameHBox = new HBoxContainer();
		var nameLabel = new Label();
		nameLabel.Text = "Player Name:";
		nameLabel.CustomMinimumSize = new Vector2(100, 0);
		_playerNameInput = new LineEdit();
		_playerNameInput.Text = "Player_" + new Random().Next(1000, 9999);
		_playerNameInput.CustomMinimumSize = new Vector2(200, 0);
		nameHBox.AddChild(nameLabel);
		nameHBox.AddChild(_playerNameInput);
		serverGroup.AddChild(nameHBox);
		
		// Server name input
		var serverNameHBox = new HBoxContainer();
		var serverNameLabel = new Label();
		serverNameLabel.Text = "Server Name:";
		serverNameLabel.CustomMinimumSize = new Vector2(100, 0);
		_serverNameInput = new LineEdit();
		_serverNameInput.Text = "PolyGun Server";
		_serverNameInput.CustomMinimumSize = new Vector2(200, 0);
		serverNameHBox.AddChild(serverNameLabel);
		serverNameHBox.AddChild(_serverNameInput);
		serverGroup.AddChild(serverNameHBox);
		
		// Max players
		var maxPlayersHBox = new HBoxContainer();
		var maxPlayersLabel = new Label();
		maxPlayersLabel.Text = "Max Players:";
		maxPlayersLabel.CustomMinimumSize = new Vector2(100, 0);
		_maxPlayersSpinBox = new SpinBox();
		_maxPlayersSpinBox.MinValue = 2;
		_maxPlayersSpinBox.MaxValue = 16;
		_maxPlayersSpinBox.Value = 8;
		maxPlayersHBox.AddChild(maxPlayersLabel);
		maxPlayersHBox.AddChild(_maxPlayersSpinBox);
		serverGroup.AddChild(maxPlayersHBox);
		
		// Min players
		var minPlayersHBox = new HBoxContainer();
		var minPlayersLabel = new Label();
		minPlayersLabel.Text = "Min Players:";
		minPlayersLabel.CustomMinimumSize = new Vector2(100, 0);
		_minPlayersSpinBox = new SpinBox();
		_minPlayersSpinBox.MinValue = 2;
		_minPlayersSpinBox.MaxValue = 16;
		_minPlayersSpinBox.Value = 2;
		minPlayersHBox.AddChild(minPlayersLabel);
		minPlayersHBox.AddChild(_minPlayersSpinBox);
		serverGroup.AddChild(minPlayersHBox);
		
		_lobbyContainer.AddChild(serverGroup);
		
		// Connection buttons
		var buttonHBox = new HBoxContainer();
		_hostButton = new Button();
		_hostButton.Text = "Host Server";
		_hostButton.CustomMinimumSize = new Vector2(120, 40);
		buttonHBox.AddChild(_hostButton);
		
		// Client section
		var clientGroup = new VBoxContainer();
		var addressHBox = new HBoxContainer();
		var addressLabel = new Label();
		addressLabel.Text = "Server Address:";
		addressLabel.CustomMinimumSize = new Vector2(100, 0);
		_serverAddressInput = new LineEdit();
		_serverAddressInput.Text = "127.0.0.1";
		_serverAddressInput.CustomMinimumSize = new Vector2(150, 0);
		addressHBox.AddChild(addressLabel);
		addressHBox.AddChild(_serverAddressInput);
		clientGroup.AddChild(addressHBox);
		
		_joinButton = new Button();
		_joinButton.Text = "Join Server";
		_joinButton.CustomMinimumSize = new Vector2(120, 40);
		clientGroup.AddChild(_joinButton);
		
		buttonHBox.AddChild(clientGroup);
		_lobbyContainer.AddChild(buttonHBox);
		
		// Status section
		_statusLabel = new Label();
		_statusLabel.Text = "Ready to connect...";
		_lobbyContainer.AddChild(_statusLabel);
		
		// Game container (shown when connected)
		_gameContainer = new VBoxContainer();
		_gameContainer.Visible = false;
		mainVBox.AddChild(_gameContainer);
		
		// Player list
		var playerListGroup = new VBoxContainer();
		var playerListTitle = new Label();
		playerListTitle.Text = "Connected Players";
		playerListGroup.AddChild(playerListTitle);
		
		_playerListLabel = new RichTextLabel();
		_playerListLabel.CustomMinimumSize = new Vector2(400, 200);
		_playerListLabel.BbcodeEnabled = true;
		playerListGroup.AddChild(_playerListLabel);
		_gameContainer.AddChild(playerListGroup);
		
		// Ready and start buttons
		var gameButtonHBox = new HBoxContainer();
		_readyButton = new Button();
		_readyButton.Text = "Ready";
		_readyButton.CustomMinimumSize = new Vector2(100, 40);
		gameButtonHBox.AddChild(_readyButton);
		
		_startGameButton = new Button();
		_startGameButton.Text = "Start Game";
		_startGameButton.CustomMinimumSize = new Vector2(100, 40);
		_startGameButton.Visible = false; // Only visible to host
		gameButtonHBox.AddChild(_startGameButton);
		
		_gameContainer.AddChild(gameButtonHBox);
		
		// Countdown label
		_countdownLabel = new Label();
		_countdownLabel.Text = "";
		_countdownLabel.Visible = false;
		_gameContainer.AddChild(_countdownLabel);
	}
	
	private void ConnectSignals()
	{
		// Button connections
		_hostButton.Pressed += OnHostPressed;
		_joinButton.Pressed += OnJoinPressed;
		_readyButton.Pressed += OnReadyPressed;
		_startGameButton.Pressed += OnStartGamePressed;
		
		// Connect to multiplayer game manager events when it becomes available
		CallDeferred(nameof(ConnectToGameManager));
	}
	
	private void ConnectToGameManager()
	{
		if (MultiplayerGameManager.Instance != null)
		{
			var manager = MultiplayerGameManager.Instance;
			manager.StateChanged += OnGameStateChanged;
			manager.PlayerJoined += OnPlayerJoined;
			manager.PlayerLeft += OnPlayerLeft;
			manager.PlayerReadyChanged += OnPlayerReadyChanged;
			manager.CountdownUpdate += OnCountdownUpdate;
			manager.GameStarted += OnGameStarted;
		}
		else
		{
			// Try again next frame
			CallDeferred(nameof(ConnectToGameManager));
		}
	}
	
	private void OnHostPressed()
	{
		var playerName = _playerNameInput.Text.Trim();
		if (string.IsNullOrEmpty(playerName))
		{
			_statusLabel.Text = "Please enter a player name";
			return;
		}
		
		var serverName = _serverNameInput.Text.Trim();
		if (string.IsNullOrEmpty(serverName))
		{
			serverName = "PolyGun Server";
		}
		
		// Create host
		_peer = new ENetMultiplayerPeer();
		var error = _peer.CreateServer(7000, (int)_maxPlayersSpinBox.Value);
		
		if (error != Error.Ok)
		{
			_statusLabel.Text = $"Failed to create server: {error}";
			return;
		}
		
		Multiplayer.MultiplayerPeer = _peer;
		
		// Start game manager as host
		if (MultiplayerGameManager.Instance != null)
		{
			MultiplayerGameManager.Instance.StartHost(serverName, (int)_maxPlayersSpinBox.Value, (int)_minPlayersSpinBox.Value);
		}
		
		_statusLabel.Text = $"Hosting server: {serverName}";
		ShowGameLobby(true);
	}
	
	private void OnJoinPressed()
	{
		var playerName = _playerNameInput.Text.Trim();
		if (string.IsNullOrEmpty(playerName))
		{
			_statusLabel.Text = "Please enter a player name";
			return;
		}
		
		var address = _serverAddressInput.Text.Trim();
		if (string.IsNullOrEmpty(address))
		{
			_statusLabel.Text = "Please enter server address";
			return;
		}
		
		// Create client
		_peer = new ENetMultiplayerPeer();
		var error = _peer.CreateClient(address, 7000);
		
		if (error != Error.Ok)
		{
			_statusLabel.Text = $"Failed to connect: {error}";
			return;
		}
		
		Multiplayer.MultiplayerPeer = _peer;
		GetTree().GetMultiplayer().ConnectedToServer += OnConnectedToServer;
		GetTree().GetMultiplayer().ConnectionFailed += OnConnectionFailed;
		
		_statusLabel.Text = $"Connecting to {address}...";
	}
	
	private void OnConnectedToServer()
	{
		var playerName = _playerNameInput.Text.Trim();
		_statusLabel.Text = "Connected! Joining game...";
		
		// Join the game
		if (MultiplayerGameManager.Instance != null)
		{
			MultiplayerGameManager.Instance.JoinAsClient(playerName);
		}
		
		ShowGameLobby(false);
	}
	
	private void OnConnectionFailed()
	{
		_statusLabel.Text = "Failed to connect to server";
		_peer = null;
		Multiplayer.MultiplayerPeer = null;
	}
	
	private void OnReadyPressed()
	{
		_isReady = !_isReady;
		
		if (MultiplayerGameManager.Instance != null)
		{
			if (MultiplayerGameManager.Instance.IsHost)
			{
				MultiplayerGameManager.Instance.SetHostReady(_isReady);
			}
			else
			{
				MultiplayerGameManager.Instance.SetClientReady(_isReady);
			}
		}
		
		UpdateReadyButton();
	}
	
	private void OnStartGamePressed()
	{
		if (MultiplayerGameManager.Instance != null && MultiplayerGameManager.Instance.IsHost)
		{
			MultiplayerGameManager.Instance.TransitionToWaitingReady();
		}
	}
	
	private void ShowGameLobby(bool isHost)
	{
		_lobbyContainer.Visible = false;
		_gameContainer.Visible = true;
		_startGameButton.Visible = isHost;
		UpdatePlayerList();
	}
	
	private void UpdateReadyButton()
	{
		_readyButton.Text = _isReady ? "Not Ready" : "Ready";
		_readyButton.Modulate = _isReady ? Colors.Green : Colors.White;
	}
	
	private void UpdatePlayerList()
	{
		if (MultiplayerGameManager.Instance == null)
		{
			_playerListLabel.Text = "No players connected";
			return;
		}
		
		var players = MultiplayerGameManager.Instance.GetAllPlayers();
		var playerText = "[color=yellow]Players (" + players.Length + "):[/color]\n\n";
		
		foreach (var player in players)
		{
			var statusColor = player.IsReady ? "green" : "red";
			var hostIndicator = player.IsHost ? " [HOST]" : "";
			playerText += $"[color={statusColor}]● {player.PlayerName}{hostIndicator}[/color]\n";
		}
		
		_playerListLabel.Text = playerText;
	}
	
	private void UpdateUI()
	{
		if (MultiplayerGameManager.Instance != null)
		{
			var state = MultiplayerGameManager.Instance.CurrentState;
			_statusLabel.Text = $"Game State: {state}";
			
			switch (state)
			{
				case MultiplayerGameManager.GameState.Lobby:
					_startGameButton.Text = "Waiting for Players...";
					_startGameButton.Disabled = true;
					break;
					
				case MultiplayerGameManager.GameState.WaitingReady:
					_startGameButton.Text = "Waiting for Ready...";
					_startGameButton.Disabled = true;
					break;
					
				case MultiplayerGameManager.GameState.CountingDown:
					_startGameButton.Text = "Starting...";
					_startGameButton.Disabled = true;
					break;
					
				case MultiplayerGameManager.GameState.Playing:
					_startGameButton.Text = "Game Started";
					_startGameButton.Disabled = true;
					break;
			}
		}
	}
	
	#region Game Manager Events
	
	private void OnGameStateChanged(MultiplayerGameManager.GameState newState)
	{
		CallDeferred(nameof(UpdateUI));
		
		switch (newState)
		{
			case MultiplayerGameManager.GameState.Playing:
				// Hide UI when game starts
				CallDeferred(nameof(HideUI));
				break;
				
			case MultiplayerGameManager.GameState.WaitingReady:
				// Enable start game button for host
				if (MultiplayerGameManager.Instance.IsHost)
				{
					_startGameButton.Text = "Force Start";
					_startGameButton.Disabled = false;
				}
				break;
		}
	}
	
	private void OnPlayerJoined(int playerId, string playerName)
	{
		_statusLabel.Text = $"{playerName} joined the game";
		CallDeferred(nameof(UpdatePlayerList));
		CallDeferred(nameof(UpdateUI));
	}
	
	private void OnPlayerLeft(int playerId, string playerName)
	{
		_statusLabel.Text = $"{playerName} left the game";
		CallDeferred(nameof(UpdatePlayerList));
		CallDeferred(nameof(UpdateUI));
	}
	
	private void OnPlayerReadyChanged(int playerId, bool isReady)
	{
		CallDeferred(nameof(UpdatePlayerList));
		CallDeferred(nameof(UpdateUI));
	}
	
	private void OnCountdownUpdate(float timeRemaining)
	{
		_countdownLabel.Visible = true;
		_countdownLabel.Text = $"Game starting in: {Mathf.Ceil(timeRemaining)}";
		_countdownLabel.Modulate = Colors.Yellow;
	}
	
	private void OnGameStarted()
	{
		_countdownLabel.Text = "Game Started!";
		_countdownLabel.Modulate = Colors.Green;
		
		// Hide UI after a short delay
		GetTree().CreateTimer(2.0).Timeout += HideUI;
	}
	
	#endregion
	
	public void HideUI()
	{
		Visible = false;
		// Don't change mouse mode here - let GameMain handle it
	}
	
	public void ShowUI()
	{
		Visible = true;
		// Don't change mouse mode here - let GameMain handle it
	}
	
	public override void _Input(InputEvent @event)
	{
		// Only handle input when the UI is visible to avoid conflicts
		if (!Visible) return;
		
		// Handle escape key to close UI
		if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			HideUI();
			GetViewport().SetInputAsHandled();
		}
	}
} 