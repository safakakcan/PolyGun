using Godot;
using System;

public partial class LobbyUI : Control
{
	[Signal] public delegate void GameStartRequestedEventHandler();
	
	// UI Elements
	private LineEdit _playerNameEdit;
	private LineEdit _serverAddressEdit;
	private SpinBox _serverPortEdit;
	private Button _hostButton;
	private Button _serverButton;
	private Button _joinButton;
	private Button _disconnectButton;
	private Button _startGameButton;
	private Label _statusLabel;
	private ItemList _playerList;
	private RichTextLabel _networkStatsLabel;
	
	// Settings
	private string _playerName = "Player";
	private string _serverAddress = "127.0.0.1";
	private int _serverPort = 7777;
	
	public override void _Ready()
	{
		CreateUI();
		ConnectSignals();
		UpdateUI();
	}
	
	private void CreateUI()
	{
		// Main container
		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		vbox.AddThemeConstantOverride("separation", 10);
		AddChild(vbox);
		
		// Title
		var title = new Label();
		title.Text = "PolyGun Multiplayer Lobby";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 24);
		vbox.AddChild(title);
		
		// Connection panel
		CreateConnectionPanel(vbox);
		
		// Status panel
		CreateStatusPanel(vbox);
		
		// Players panel
		CreatePlayersPanel(vbox);
		
		// Network stats panel
		CreateStatsPanel(vbox);
		
		// Game controls
		CreateGameControls(vbox);
	}
	
	private void CreateConnectionPanel(VBoxContainer parent)
	{
		var groupBox = new VBoxContainer();
		var title = new Label();
		title.Text = "Connection Settings";
		title.AddThemeFontSizeOverride("font_size", 16);
		groupBox.AddChild(title);
		parent.AddChild(groupBox);
		
		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 5);
		groupBox.AddChild(grid);
		
		// Player name
		grid.AddChild(new Label { Text = "Player Name:" });
		_playerNameEdit = new LineEdit();
		_playerNameEdit.Text = _playerName;
		_playerNameEdit.PlaceholderText = "Enter your name";
		grid.AddChild(_playerNameEdit);
		
		// Server address
		grid.AddChild(new Label { Text = "Server Address:" });
		_serverAddressEdit = new LineEdit();
		_serverAddressEdit.Text = _serverAddress;
		_serverAddressEdit.PlaceholderText = "127.0.0.1";
		grid.AddChild(_serverAddressEdit);
		
		// Server port
		grid.AddChild(new Label { Text = "Server Port:" });
		_serverPortEdit = new SpinBox();
		_serverPortEdit.MinValue = 1024;
		_serverPortEdit.MaxValue = 65535;
		_serverPortEdit.Value = _serverPort;
		grid.AddChild(_serverPortEdit);
		
		// Buttons
		var buttonContainer = new HBoxContainer();
		buttonContainer.AddThemeConstantOverride("separation", 10);
		groupBox.AddChild(buttonContainer);
		
		_hostButton = new Button();
		_hostButton.Text = "Host Game";
		_hostButton.Pressed += OnHostPressed;
		buttonContainer.AddChild(_hostButton);
		
		_serverButton = new Button();
		_serverButton.Text = "Start Server";
		_serverButton.Pressed += OnServerPressed;
		buttonContainer.AddChild(_serverButton);
		
		_joinButton = new Button();
		_joinButton.Text = "Join Game";
		_joinButton.Pressed += OnJoinPressed;
		buttonContainer.AddChild(_joinButton);
		
		_disconnectButton = new Button();
		_disconnectButton.Text = "Disconnect";
		_disconnectButton.Pressed += OnDisconnectPressed;
		buttonContainer.AddChild(_disconnectButton);
	}
	
	private void CreateStatusPanel(VBoxContainer parent)
	{
		var groupBox = new VBoxContainer();
		var title = new Label();
		title.Text = "Connection Status";
		title.AddThemeFontSizeOverride("font_size", 16);
		groupBox.AddChild(title);
		parent.AddChild(groupBox);
		
		_statusLabel = new Label();
		_statusLabel.Text = "Not connected";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		groupBox.AddChild(_statusLabel);
	}
	
	private void CreatePlayersPanel(VBoxContainer parent)
	{
		var groupBox = new VBoxContainer();
		var title = new Label();
		title.Text = "Connected Players";
		title.AddThemeFontSizeOverride("font_size", 16);
		groupBox.AddChild(title);
		parent.AddChild(groupBox);
		
		_playerList = new ItemList();
		_playerList.CustomMinimumSize = new Vector2(0, 150);
		groupBox.AddChild(_playerList);
	}
	
	private void CreateStatsPanel(VBoxContainer parent)
	{
		var groupBox = new VBoxContainer();
		var title = new Label();
		title.Text = "Network Statistics";
		title.AddThemeFontSizeOverride("font_size", 16);
		groupBox.AddChild(title);
		groupBox.CustomMinimumSize = new Vector2(0, 100);
		parent.AddChild(groupBox);
		
		_networkStatsLabel = new RichTextLabel();
		_networkStatsLabel.FitContent = true;
		_networkStatsLabel.ScrollActive = false;
		groupBox.AddChild(_networkStatsLabel);
	}
	
	private void CreateGameControls(VBoxContainer parent)
	{
		var groupBox = new VBoxContainer();
		var title = new Label();
		title.Text = "Game Controls";
		title.AddThemeFontSizeOverride("font_size", 16);
		groupBox.AddChild(title);
		parent.AddChild(groupBox);
		
		_startGameButton = new Button();
		_startGameButton.Text = "Start Game";
		_startGameButton.Pressed += OnStartGamePressed;
		groupBox.AddChild(_startGameButton);
	}
	
	private void ConnectSignals()
	{
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			networkManager.ServerStarted += OnServerStarted;
			networkManager.ClientConnected += OnClientConnected;
			networkManager.ClientDisconnected += OnClientDisconnected;
			networkManager.ConnectionFailed += OnConnectionFailed;
		}
		
		// Update UI periodically
		var timer = new Timer();
		timer.WaitTime = 1.0; // Update every second
		timer.Timeout += UpdateNetworkStats;
		timer.Autostart = true;
		AddChild(timer);
	}
	
	private void OnHostPressed()
	{
		UpdateSettingsFromUI();
		
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			bool success = networkManager.StartHost(_playerName);
			if (!success)
			{
				_statusLabel.Text = "Failed to start host";
				_statusLabel.AddThemeColorOverride("font_color", Colors.Red);
			}
		}
	}
	
	private void OnServerPressed()
	{
		UpdateSettingsFromUI();
		
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			bool success = networkManager.StartServer();
			if (!success)
			{
				_statusLabel.Text = "Failed to start server";
				_statusLabel.AddThemeColorOverride("font_color", Colors.Red);
			}
		}
	}
	
	private void OnJoinPressed()
	{
		UpdateSettingsFromUI();
		
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			bool success = networkManager.ConnectToServer(_serverAddress, _serverPort, _playerName);
			if (!success)
			{
				_statusLabel.Text = "Failed to connect";
				_statusLabel.AddThemeColorOverride("font_color", Colors.Red);
			}
			else
			{
				_statusLabel.Text = "Connecting...";
				_statusLabel.AddThemeColorOverride("font_color", Colors.Yellow);
			}
		}
	}
	
	private void OnDisconnectPressed()
	{
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			networkManager.Disconnect();
		}
		
		UpdateUI();
	}
	
	private void OnStartGamePressed()
	{
		EmitSignal(SignalName.GameStartRequested);
	}
	
	private void OnServerStarted()
	{
		_statusLabel.Text = $"Server running on port {_serverPort}";
		_statusLabel.AddThemeColorOverride("font_color", Colors.Green);
		UpdateUI();
	}
	
	private void OnClientConnected()
	{
		_statusLabel.Text = $"Connected to {_serverAddress}:{_serverPort}";
		_statusLabel.AddThemeColorOverride("font_color", Colors.Green);
		UpdateUI();
	}
	
	private void OnClientDisconnected()
	{
		_statusLabel.Text = "Disconnected from server";
		_statusLabel.AddThemeColorOverride("font_color", Colors.Red);
		UpdateUI();
	}
	
	private void OnConnectionFailed(string reason)
	{
		_statusLabel.Text = $"Connection failed: {reason}";
		_statusLabel.AddThemeColorOverride("font_color", Colors.Red);
		UpdateUI();
	}
	
	private void UpdateSettingsFromUI()
	{
		_playerName = _playerNameEdit.Text.Trim();
		if (string.IsNullOrEmpty(_playerName))
		{
			_playerName = "Player";
			_playerNameEdit.Text = _playerName;
		}
		
		_serverAddress = _serverAddressEdit.Text.Trim();
		if (string.IsNullOrEmpty(_serverAddress))
		{
			_serverAddress = "127.0.0.1";
			_serverAddressEdit.Text = _serverAddress;
		}
		
		_serverPort = (int)_serverPortEdit.Value;
	}
	
	private void UpdateUI()
	{
		var networkManager = NetworkManager.Instance;
		bool isConnected = networkManager?.IsConnected ?? false;
		bool isServer = networkManager?.IsServer ?? false;
		
		// Enable/disable buttons based on connection state
		_hostButton.Disabled = isConnected;
		_serverButton.Disabled = isConnected;
		_joinButton.Disabled = isConnected;
		_disconnectButton.Disabled = !isConnected;
		_startGameButton.Disabled = !isServer;
		
		// Enable/disable settings when connected
		_playerNameEdit.Editable = !isConnected;
		_serverAddressEdit.Editable = !isConnected;
		_serverPortEdit.Editable = !isConnected;
		
		UpdatePlayerList();
	}
	
	private void UpdatePlayerList()
	{
		_playerList.Clear();
		
		var networkManager = NetworkManager.Instance;
		if (networkManager != null)
		{
			var players = networkManager.GetAllPlayers();
			foreach (var player in players)
			{
				string playerText = $"{player.PlayerName} (ID: {player.NetworkId})";
				if (player.IsLocal)
				{
					playerText += " [LOCAL]";
				}
				_playerList.AddItem(playerText);
			}
		}
	}
	
	private void UpdateNetworkStats()
	{
		var networkManager = NetworkManager.Instance;
		if (networkManager == null || !networkManager.IsConnected)
		{
			_networkStatsLabel.Text = "No network connection";
			return;
		}
		
		var localPlayer = networkManager.GetLocalPlayer();
		string stats = $"[b]Network Mode:[/b] {networkManager.CurrentMode}\n";
		stats += $"[b]Players:[/b] {networkManager.GetAllPlayers().Count}\n";
		
		// Get prediction stats from ClientPrediction component
		var clientPrediction = GetNode<ClientPrediction>("../ClientPrediction");
		if (clientPrediction != null)
		{
			stats += $"[b]Prediction Error:[/b] {clientPrediction.AveragePredictionError:F3}\n";
			stats += $"[b]Pending Commands:[/b] {clientPrediction.PendingCommands}\n";
		}
		
		if (networkManager.IsServer)
		{
			var authServer = AuthoritativeServer.Instance;
			if (authServer != null)
			{
				stats += $"[b]Server Players:[/b] {authServer.ConnectedPlayerCount}\n";
			}
		}
		
		_networkStatsLabel.Text = stats;
	}
	
	public override void _Input(InputEvent @event)
	{
		// Debug hotkeys
		if (@event.IsActionPressed("ui_accept") && Input.IsActionPressed("ui_select"))
		{
			// Print detailed network stats
			NetworkManager.Instance?.PrintNetworkStats();
		}
		
		// Quick disconnect with ESC
		if (@event.IsActionPressed("ui_cancel") && NetworkManager.Instance?.IsConnected == true)
		{
			OnDisconnectPressed();
		}
	}
} 