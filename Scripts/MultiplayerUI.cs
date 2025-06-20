using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class MultiplayerUI : Control
{
	[Signal] public delegate void StartGameRequestedEventHandler();

	// UI References
	private Control _mainMenu;
	private Control _serverBrowser;
	private Control _createServerMenu;
	private Control _lobbyMenu;
	private Control _connectingMenu;

	// Main Menu
	private Button _hostButton;
	private Button _joinButton;
	private Button _backToMainButton;

	// Server Browser
	private ItemList _serverList;
	private Button _refreshButton;
	private Button _joinSelectedButton;
	private Button _directConnectButton;
	private LineEdit _ipAddressEdit;
	private SpinBox _connectPortSpinBox;
	private Button _backToBrowserButton;
	private Label _refreshStatus;

	// Create Server
	private LineEdit _serverNameEdit;
	private LineEdit _playerNameEdit;
	private SpinBox _maxPlayersSpinBox;
	private SpinBox _portSpinBox;
	private LineEdit _passwordEdit;
	private Button _createButton;
	private Button _cancelCreateButton;

	// Lobby
	private Label _lobbyTitle;
	private ItemList _playerList;
	private Button _startGameButton;
	private Button _leaveLobbyButton;
	private Label _lobbyStatus;

	// Connecting
	private Label _connectingLabel;
	private Button _cancelConnectButton;

	private List<ServerInfo> _currentServers = new List<ServerInfo>();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		SetupUI();
		ConnectSignals();
		ShowMainMenu();
	}

	private void SetupUI()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Main container
		var mainContainer = new Control();
		mainContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(mainContainer);

		SetupMainMenu(mainContainer);
		SetupServerBrowser(mainContainer);
		SetupCreateServerMenu(mainContainer);
		SetupLobbyMenu(mainContainer);
		SetupConnectingMenu(mainContainer);
	}

	private void SetupMainMenu(Control parent)
	{
		_mainMenu = new Control();
		_mainMenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		parent.AddChild(_mainMenu);

		// Background
		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_mainMenu.AddChild(background);

		// Main container
		var container = new VBoxContainer();
		container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		_mainMenu.AddChild(container);

		// Title
		var title = new Label();
		title.Text = "MULTIPLAYER";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 32);
		container.AddChild(title);

		// Spacer
		var spacer = new Control();
		spacer.CustomMinimumSize = new Vector2(0, 20);
		container.AddChild(spacer);

		// Host button
		_hostButton = new Button();
		_hostButton.Text = "Host Game";
		_hostButton.CustomMinimumSize = new Vector2(200, 50);
		_hostButton.Pressed += OnHostPressed;
		container.AddChild(_hostButton);

		// Join button
		_joinButton = new Button();
		_joinButton.Text = "Join Game";
		_joinButton.CustomMinimumSize = new Vector2(200, 50);
		_joinButton.Pressed += OnJoinPressed;
		container.AddChild(_joinButton);

		// Back button
		_backToMainButton = new Button();
		_backToMainButton.Text = "Back to Main Menu";
		_backToMainButton.CustomMinimumSize = new Vector2(200, 50);
		_backToMainButton.Pressed += OnBackToMainPressed;
		container.AddChild(_backToMainButton);
	}

	private void SetupServerBrowser(Control parent)
	{
		_serverBrowser = new Control();
		_serverBrowser.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_serverBrowser.Visible = false;
		parent.AddChild(_serverBrowser);

		// Background
		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_serverBrowser.AddChild(background);

		// Main container
		var container = new VBoxContainer();
		container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		container.CustomMinimumSize = new Vector2(600, 400);
		_serverBrowser.AddChild(container);

		// Title
		var title = new Label();
		title.Text = "SERVER BROWSER";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 24);
		container.AddChild(title);

		// Status label
		_refreshStatus = new Label();
		_refreshStatus.Text = "Searching for servers...";
		_refreshStatus.HorizontalAlignment = HorizontalAlignment.Center;
		container.AddChild(_refreshStatus);

		// Server list
		_serverList = new ItemList();
		_serverList.CustomMinimumSize = new Vector2(580, 250);
		_serverList.ItemSelected += OnServerSelected;
		container.AddChild(_serverList);

		// Button container
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		container.AddChild(buttonContainer);

		// Refresh button
		_refreshButton = new Button();
		_refreshButton.Text = "Refresh";
		_refreshButton.Pressed += OnRefreshPressed;
		buttonContainer.AddChild(_refreshButton);

		// Join button
		_joinSelectedButton = new Button();
		_joinSelectedButton.Text = "Join Server";
		_joinSelectedButton.Disabled = true;
		_joinSelectedButton.Pressed += OnJoinSelectedPressed;
		buttonContainer.AddChild(_joinSelectedButton);

		// Back button
		_backToBrowserButton = new Button();
		_backToBrowserButton.Text = "Back";
		_backToBrowserButton.Pressed += ShowMainMenu;
		buttonContainer.AddChild(_backToBrowserButton);

		// Separator
		container.AddChild(new HSeparator());

		// Direct connect section
		var directLabel = new Label();
		directLabel.Text = "Direct Connect";
		directLabel.HorizontalAlignment = HorizontalAlignment.Center;
		directLabel.AddThemeFontSizeOverride("font_size", 16);
		container.AddChild(directLabel);

		var directContainer = new HBoxContainer();
		directContainer.Alignment = BoxContainer.AlignmentMode.Center;
		container.AddChild(directContainer);

		directContainer.AddChild(new Label { Text = "IP:" });
		_ipAddressEdit = new LineEdit();
		_ipAddressEdit.PlaceholderText = "127.0.0.1";
		_ipAddressEdit.CustomMinimumSize = new Vector2(150, 0);
		directContainer.AddChild(_ipAddressEdit);

		directContainer.AddChild(new Label { Text = "Port:" });
		_connectPortSpinBox = new SpinBox();
		_connectPortSpinBox.MinValue = 1000;
		_connectPortSpinBox.MaxValue = 65535;
		_connectPortSpinBox.Value = 7000;
		directContainer.AddChild(_connectPortSpinBox);

		_directConnectButton = new Button();
		_directConnectButton.Text = "Connect";
		_directConnectButton.Pressed += OnDirectConnectPressed;
		directContainer.AddChild(_directConnectButton);
	}

	private void SetupCreateServerMenu(Control parent)
	{
		_createServerMenu = new Control();
		_createServerMenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_createServerMenu.Visible = false;
		parent.AddChild(_createServerMenu);

		// Background
		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_createServerMenu.AddChild(background);

		// Main container
		var container = new VBoxContainer();
		container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		_createServerMenu.AddChild(container);

		// Title
		var title = new Label();
		title.Text = "CREATE SERVER";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 24);
		container.AddChild(title);

		// Form container
		var formContainer = new GridContainer();
		formContainer.Columns = 2;
		container.AddChild(formContainer);

		// Server name
		var serverNameLabel = new Label();
		serverNameLabel.Text = "Server Name:";
		formContainer.AddChild(serverNameLabel);

		_serverNameEdit = new LineEdit();
		_serverNameEdit.Text = "PolyGun Server";
		_serverNameEdit.CustomMinimumSize = new Vector2(200, 0);
		formContainer.AddChild(_serverNameEdit);

		// Player name
		var playerNameLabel = new Label();
		playerNameLabel.Text = "Player Name:";
		formContainer.AddChild(playerNameLabel);

		_playerNameEdit = new LineEdit();
		_playerNameEdit.Text = OS.GetEnvironment("USERNAME");
		formContainer.AddChild(_playerNameEdit);

		// Max players
		var maxPlayersLabel = new Label();
		maxPlayersLabel.Text = "Max Players:";
		formContainer.AddChild(maxPlayersLabel);

		_maxPlayersSpinBox = new SpinBox();
		_maxPlayersSpinBox.MinValue = 2;
		_maxPlayersSpinBox.MaxValue = 16;
		_maxPlayersSpinBox.Value = 8;
		formContainer.AddChild(_maxPlayersSpinBox);

		// Port
		var portLabel = new Label();
		portLabel.Text = "Port:";
		formContainer.AddChild(portLabel);

		_portSpinBox = new SpinBox();
		_portSpinBox.MinValue = 1024;
		_portSpinBox.MaxValue = 65535;
		_portSpinBox.Value = 7000;
		formContainer.AddChild(_portSpinBox);

		// Password (optional)
		var passwordLabel = new Label();
		passwordLabel.Text = "Password (optional):";
		formContainer.AddChild(passwordLabel);

		_passwordEdit = new LineEdit();
		_passwordEdit.Secret = true;
		formContainer.AddChild(_passwordEdit);

		// Button container
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		container.AddChild(buttonContainer);

		// Create button
		_createButton = new Button();
		_createButton.Text = "Create Server";
		_createButton.Pressed += OnCreateServerPressed;
		buttonContainer.AddChild(_createButton);

		// Cancel button
		_cancelCreateButton = new Button();
		_cancelCreateButton.Text = "Cancel";
		_cancelCreateButton.Pressed += ShowMainMenu;
		buttonContainer.AddChild(_cancelCreateButton);
	}

	private void SetupLobbyMenu(Control parent)
	{
		_lobbyMenu = new Control();
		_lobbyMenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_lobbyMenu.Visible = false;
		parent.AddChild(_lobbyMenu);

		// Background
		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_lobbyMenu.AddChild(background);

		// Main container
		var container = new VBoxContainer();
		container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		container.CustomMinimumSize = new Vector2(400, 300);
		_lobbyMenu.AddChild(container);

		// Title
		_lobbyTitle = new Label();
		_lobbyTitle.Text = "LOBBY";
		_lobbyTitle.HorizontalAlignment = HorizontalAlignment.Center;
		_lobbyTitle.AddThemeFontSizeOverride("font_size", 24);
		container.AddChild(_lobbyTitle);

		// Status
		_lobbyStatus = new Label();
		_lobbyStatus.Text = "Waiting for players...";
		_lobbyStatus.HorizontalAlignment = HorizontalAlignment.Center;
		container.AddChild(_lobbyStatus);

		// Player list
		_playerList = new ItemList();
		_playerList.CustomMinimumSize = new Vector2(380, 150);
		container.AddChild(_playerList);

		// Button container
		var buttonContainer = new HBoxContainer();
		buttonContainer.Alignment = BoxContainer.AlignmentMode.Center;
		container.AddChild(buttonContainer);

		// Start game button (server only)
		_startGameButton = new Button();
		_startGameButton.Text = "Start Game";
		_startGameButton.Visible = false;
		_startGameButton.Pressed += OnStartGamePressed;
		buttonContainer.AddChild(_startGameButton);

		// Leave button
		_leaveLobbyButton = new Button();
		_leaveLobbyButton.Text = "Leave";
		_leaveLobbyButton.Pressed += OnLeaveLobbyPressed;
		buttonContainer.AddChild(_leaveLobbyButton);
	}

	private void SetupConnectingMenu(Control parent)
	{
		_connectingMenu = new Control();
		_connectingMenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingMenu.Visible = false;
		parent.AddChild(_connectingMenu);

		// Background
		var background = new ColorRect();
		background.Color = new Color(0.1f, 0.1f, 0.2f, 0.9f);
		background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_connectingMenu.AddChild(background);

		// Main container
		var container = new VBoxContainer();
		container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		_connectingMenu.AddChild(container);

		// Connecting label
		_connectingLabel = new Label();
		_connectingLabel.Text = "Connecting to server...";
		_connectingLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_connectingLabel.AddThemeFontSizeOverride("font_size", 20);
		container.AddChild(_connectingLabel);

		// Cancel button
		_cancelConnectButton = new Button();
		_cancelConnectButton.Text = "Cancel";
		_cancelConnectButton.Pressed += OnCancelConnectPressed;
		container.AddChild(_cancelConnectButton);
	}

	private void ConnectSignals()
	{
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.ServerCreated += OnServerCreated;
			NetworkManager.Instance.ServerJoined += OnServerJoined;
			NetworkManager.Instance.ServerListUpdated += OnServerListUpdated;
			NetworkManager.Instance.PlayerJoined += OnPlayerJoined;
			NetworkManager.Instance.PlayerLeft += OnPlayerLeft;
			NetworkManager.Instance.ConnectionFailed += OnConnectionFailed;
		}
	}

	#region UI Navigation

	public void ShowMainMenu()
	{
		HideAllMenus();
		_mainMenu.Visible = true;
		NetworkManager.Instance?.StopServerDiscovery();
	}

	public void ShowServerBrowser()
	{
		HideAllMenus();
		_serverBrowser.Visible = true;
		_refreshStatus.Text = "Searching for servers...";
		RefreshServerList();
	}

	public void ShowCreateServerMenu()
	{
		HideAllMenus();
		_createServerMenu.Visible = true;
	}

	public void ShowLobby()
	{
		HideAllMenus();
		_lobbyMenu.Visible = true;
		UpdateLobby();
	}

	public void ShowConnecting(string message = "Connecting to server...")
	{
		HideAllMenus();
		_connectingMenu.Visible = true;
		_connectingLabel.Text = message;
	}

	private void HideAllMenus()
	{
		_mainMenu.Visible = false;
		_serverBrowser.Visible = false;
		_createServerMenu.Visible = false;
		_lobbyMenu.Visible = false;
		_connectingMenu.Visible = false;
	}

	#endregion

	#region Button Handlers

	private void OnHostPressed()
	{
		ShowCreateServerMenu();
	}

	private void OnJoinPressed()
	{
		ShowServerBrowser();
	}

	private void OnBackToMainPressed()
	{
		// Return to main game menu (handled by GameManager)
		Visible = false;
	}

	private void OnRefreshPressed()
	{
		RefreshServerList();
	}

	private void OnServerSelected(long index)
	{
		_joinSelectedButton.Disabled = false;
	}

	private void OnJoinSelectedPressed()
	{
		var selectedIndex = _serverList.GetSelectedItems().FirstOrDefault();
		if (selectedIndex >= 0 && selectedIndex < _currentServers.Count)
		{
			var server = _currentServers[selectedIndex];
			ShowConnecting($"Connecting to {server.ServerName}...");
			NetworkManager.Instance?.JoinServer(server.IPAddress, server.Port);
		}
	}

	private void OnDirectConnectPressed()
	{
		var ipAddress = _ipAddressEdit.Text.Trim();
		var port = (int)_connectPortSpinBox.Value;

		if (string.IsNullOrEmpty(ipAddress))
		{
			ipAddress = "127.0.0.1";
		}

		GD.Print($"Attempting direct connection to {ipAddress}:{port}");
		ShowConnecting($"Connecting to {ipAddress}:{port}...");
		NetworkManager.Instance?.JoinServer(ipAddress, port);
	}

	private void OnCreateServerPressed()
	{
		var serverName = _serverNameEdit.Text;
		var password = _passwordEdit.Text;
		var port = (int)_portSpinBox.Value;
		var maxPlayers = (int)_maxPlayersSpinBox.Value;

		if (string.IsNullOrEmpty(serverName))
		{
			serverName = "PolyGun Server";
		}

		GD.Print($"Creating server: {serverName} on port {port} with {maxPlayers} max players");
		
		NetworkManager.Instance.MaxPlayers = maxPlayers;
		var result = NetworkManager.Instance?.CreateServer(serverName, password, port);
		
		if (result == Error.Ok)
		{
			ShowConnecting("Creating server...");
		}
		else
		{
			GD.PrintErr($"Failed to create server: {result}");
			_refreshStatus.Text = $"Failed to create server: {result}";
		}
	}

	private void OnStartGamePressed()
	{
		EmitSignal(SignalName.StartGameRequested);
	}

	private void OnLeaveLobbyPressed()
	{
		NetworkManager.Instance?.DisconnectFromServer();
		ShowMainMenu();
	}

	private void OnCancelConnectPressed()
	{
		NetworkManager.Instance?.DisconnectFromServer();
		ShowMainMenu();
	}

	#endregion

	#region Network Event Handlers

	private void OnServerCreated()
	{
		GD.Print("Server created successfully");
		var serverInfo = NetworkManager.Instance?.CurrentServerInfo;
		if (serverInfo != null)
		{
			GD.Print($"Server IP: {serverInfo.IPAddress}:{serverInfo.Port}");
			GD.Print($"Other players can connect directly using: {serverInfo.IPAddress}:{serverInfo.Port}");
		}
		ShowLobby();
	}

	private void OnServerJoined()
	{
		GD.Print("Joined server successfully");
		ShowLobby();
	}

	private void OnServerListUpdated()
	{
		// Get servers from NetworkManager since we can't pass them via signal
		var servers = NetworkManager.Instance?.DiscoveredServers ?? new List<ServerInfo>();
		_currentServers = servers.ToList();
		GD.Print($"Server list updated: {_currentServers.Count} servers found");
		UpdateServerList();
	}

	private void OnPlayerJoined(int playerId, string playerName)
	{
		GD.Print($"Player joined lobby: {playerName} ({playerId})");
		UpdateLobby();
	}

	private void OnPlayerLeft(int playerId)
	{
		GD.Print($"Player left lobby: {playerId}");
		UpdateLobby();
	}

	private void OnConnectionFailed(string reason)
	{
		GD.PrintErr($"Connection failed: {reason}");
		ShowMainMenu();
		
		// Show error dialog (you could implement this)
		GD.Print($"Connection failed: {reason}");
	}

	#endregion

	#region UI Updates

	private void RefreshServerList()
	{
		_serverList.Clear();
		_currentServers.Clear();
		_joinSelectedButton.Disabled = true;
		_refreshStatus.Text = "Searching for servers...";
		
		GD.Print("Starting server discovery...");
		NetworkManager.Instance?.StartServerDiscovery();
		
		// Auto-refresh after a few seconds if no servers found
		GetTree().CreateTimer(5.0).Timeout += () => {
			if (_currentServers.Count == 0)
			{
				_refreshStatus.Text = "No servers found. Click Refresh to try again.";
			}
		};
	}

	private void UpdateServerList()
	{
		_serverList.Clear();
		
		foreach (var server in _currentServers)
		{
			var serverText = $"{server.ServerName} ({server.PlayerCount}/{server.MaxPlayers}) - {server.HostName}";
			if (server.HasPassword)
			{
				serverText += " 🔒";
			}
			_serverList.AddItem(serverText);
		}
		
		_refreshStatus.Text = _currentServers.Count > 0 
			? $"Found {_currentServers.Count} server(s)" 
			: "No servers found";
	}

	private void UpdateLobby()
	{
		if (NetworkManager.Instance?.CurrentServerInfo != null)
		{
			var serverInfo = NetworkManager.Instance.CurrentServerInfo;
			_lobbyTitle.Text = $"LOBBY - {serverInfo.ServerName}";
		}

		_playerList.Clear();
		var players = NetworkManager.Instance?.ConnectedPlayers ?? new Dictionary<int, PlayerInfo>();
		
		foreach (var player in players.Values)
		{
			var playerText = player.PlayerName;
			if (player.PlayerId == 1) // Server host
			{
				playerText += " (Host)";
			}
			if (player.IsReady)
			{
				playerText += " ✓";
			}
			_playerList.AddItem(playerText);
		}

		// Show start game button only for server host
		_startGameButton.Visible = NetworkManager.Instance?.IsServer == true;
		
		var readyCount = players.Values.Count(p => p.IsReady);
		_lobbyStatus.Text = $"Players ready: {readyCount}/{players.Count}";
	}

	#endregion

	public override void _ExitTree()
	{
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.ServerCreated -= OnServerCreated;
			NetworkManager.Instance.ServerJoined -= OnServerJoined;
			NetworkManager.Instance.ServerListUpdated -= OnServerListUpdated;
			NetworkManager.Instance.PlayerJoined -= OnPlayerJoined;
			NetworkManager.Instance.PlayerLeft -= OnPlayerLeft;
			NetworkManager.Instance.ConnectionFailed -= OnConnectionFailed;
		}
	}
} 