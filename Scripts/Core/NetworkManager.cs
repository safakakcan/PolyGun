using Godot;
using System;
using System.Collections.Generic;

public enum NetworkMode
{
	None,
	Host,
	Server,
	Client
}

public partial class NetworkManager : Node
{
	[Signal] public delegate void ServerStartedEventHandler();
	[Signal] public delegate void ClientConnectedEventHandler();
	[Signal] public delegate void ClientDisconnectedEventHandler();
	[Signal] public delegate void ConnectionFailedEventHandler(string reason);
	
	// Network Configuration
	[Export] public int MaxPlayers = 16;
	[Export] public int ServerPort = 7777;
	[Export] public string ServerAddress = "127.0.0.1";
	[Export] public bool EnableLagCompensation = true;
	[Export] public bool EnableAntiCheat = true;
	[Export] public double TickRate = 90.0;
	
	// Network Components
	private NetworkTick _networkTick;
	private AuthoritativeServer _authoritativeServer;
	private ClientPrediction _clientPrediction;
	
	// Network State
	private NetworkMode _currentMode = NetworkMode.None;
	private Dictionary<int, PlayerController> _players;
	private bool _isInitialized;
	
	public static NetworkManager Instance { get; private set; }
	public NetworkMode CurrentMode => _currentMode;
	public bool IsServer => _currentMode == NetworkMode.Server || _currentMode == NetworkMode.Host;
	public bool IsClient => _currentMode == NetworkMode.Client || _currentMode == NetworkMode.Host;
	public new bool IsConnected => GetTree().GetMultiplayer().MultiplayerPeer != null;
	
	public override void _Ready()
	{
		// Singleton setup
		if (Instance == null)
		{
			Instance = this;
		}
		else
		{
			QueueFree();
			return;
		}
		
		_players = new Dictionary<int, PlayerController>();
		InitializeNetworkComponents();
		
		GD.Print("NetworkManager initialized");
	}
	
	private void InitializeNetworkComponents()
	{
		// Create NetworkTick
		_networkTick = new NetworkTick();
		_networkTick.Name = "NetworkTick";
		_networkTick.TickRate = TickRate;
		AddChild(_networkTick);
		
		// Create AuthoritativeServer
		_authoritativeServer = new AuthoritativeServer();
		_authoritativeServer.Name = "AuthoritativeServer";
		_authoritativeServer.MaxPlayers = MaxPlayers;
		_authoritativeServer.Port = ServerPort;
		_authoritativeServer.EnableLagCompensation = EnableLagCompensation;
		_authoritativeServer.EnableAntiCheat = EnableAntiCheat;
		AddChild(_authoritativeServer);
		
		// Create ClientPrediction
		_clientPrediction = new ClientPrediction();
		_clientPrediction.Name = "ClientPrediction";
		AddChild(_clientPrediction);
		
		// Connect signals
		_authoritativeServer.PlayerConnected += OnPlayerConnected;
		_authoritativeServer.PlayerDisconnected += OnPlayerDisconnected;
		_authoritativeServer.HitConfirmed += OnHitConfirmed;
		
		_isInitialized = true;
		GD.Print("Network components initialized");
	}
	
	// Host a server (server + local client)
	public bool StartHost(string playerName = "Host")
	{
		if (!_isInitialized) return false;
		
		GD.Print($"Starting host server on port {ServerPort}...");
		
		if (!_authoritativeServer.StartServer())
		{
			GD.PrintErr("Failed to start host server");
			return false;
		}
		
		_currentMode = NetworkMode.Host;
		
		// Create local player for host
		CreateLocalPlayer(1, playerName); // Host is always ID 1
		
		EmitSignal(SignalName.ServerStarted);
		GD.Print("Host server started successfully");
		return true;
	}
	
	// Start dedicated server
	public bool StartServer()
	{
		if (!_isInitialized) return false;
		
		GD.Print($"Starting dedicated server on port {ServerPort}...");
		
		if (!_authoritativeServer.StartServer())
		{
			GD.PrintErr("Failed to start dedicated server");
			return false;
		}
		
		_currentMode = NetworkMode.Server;
		
		EmitSignal(SignalName.ServerStarted);
		GD.Print("Dedicated server started successfully");
		return true;
	}
	
	// Connect as client
	public bool ConnectToServer(string address, int port, string playerName = "Player")
	{
		if (!_isInitialized) return false;
		
		GD.Print($"Connecting to server {address}:{port}...");
		
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient(address, port);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to create client: {error}");
			EmitSignal(SignalName.ConnectionFailed, $"Failed to create client: {error}");
			return false;
		}
		
		GetTree().GetMultiplayer().MultiplayerPeer = peer;
		GetTree().GetMultiplayer().ConnectedToServer += OnConnectedToServer;
		GetTree().GetMultiplayer().ConnectionFailed += OnConnectionFailed;
		GetTree().GetMultiplayer().ServerDisconnected += OnServerDisconnected;
		
		_currentMode = NetworkMode.Client;
		ServerAddress = address;
		ServerPort = port;
		
		return true;
	}
	
	// Disconnect from current session
	public void Disconnect()
	{
		if (_currentMode == NetworkMode.None) return;
		
		GD.Print($"Disconnecting from {_currentMode} session...");
		
		// Stop server if hosting
		if (IsServer)
		{
			_authoritativeServer?.StopServer();
		}
		
		// Clear multiplayer peer
		GetTree().GetMultiplayer().MultiplayerPeer = null;
		
		// Clear players
		_players.Clear();
		
		// Reset state
		_currentMode = NetworkMode.None;
		
		GD.Print("Disconnected successfully");
	}
	
	private void OnConnectedToServer()
	{
		GD.Print("Connected to server successfully!");
		
		// Create local player
		int playerId = GetTree().GetMultiplayer().GetUniqueId();
		CreateLocalPlayer(playerId, "Player"); // Would get name from UI
		
		EmitSignal(SignalName.ClientConnected);
	}
	
	private void OnConnectionFailed()
	{
		GD.PrintErr("Failed to connect to server");
		_currentMode = NetworkMode.None;
		EmitSignal(SignalName.ConnectionFailed, "Connection failed");
	}
	
	private void OnServerDisconnected()
	{
		GD.Print("Disconnected from server");
		_currentMode = NetworkMode.None;
		_players.Clear();
		EmitSignal(SignalName.ClientDisconnected);
	}
	
	private void OnPlayerConnected(int playerId, string playerName)
	{
		GD.Print($"Player connected: {playerName} (ID: {playerId})");
		
		// If this is not the server, create a remote player representation
		if (!IsServer)
		{
			CreateRemotePlayer(playerId, playerName);
		}
	}
	
	private void OnPlayerDisconnected(int playerId)
	{
		GD.Print($"Player disconnected: ID {playerId}");
		
		if (_players.ContainsKey(playerId))
		{
			_players[playerId].QueueFree();
			_players.Remove(playerId);
		}
	}
	
	private void OnHitConfirmed(int shooterId, int targetId, int damage, Vector3 hitPosition)
	{
		// Handle confirmed hits (effects, UI updates, etc.)
		GD.Print($"Hit confirmed: Player {shooterId} hit Player {targetId} for {damage} damage");
		
		// Apply damage via RPC to ensure all clients see the effect
		if (_players.ContainsKey(targetId))
		{
			_players[targetId].Rpc(PlayerController.MethodName.ApplyNetworkDamage, damage, shooterId);
		}
	}
	
	private void CreateLocalPlayer(int playerId, string playerName)
	{
		// Load player scene
		var playerScene = GD.Load<PackedScene>("res://Prefabs/player.tscn");
		if (playerScene == null)
		{
			GD.PrintErr("Could not load player scene!");
			return;
		}
		
		var player = playerScene.Instantiate<PlayerController>();
		player.PlayerName = playerName;
		player.NetworkId = playerId;
		player.IsLocal = true;
		player.SetMultiplayerAuthority(playerId);
		
		// Add to scene
		GetTree().CurrentScene.AddChild(player);
		_players[playerId] = player;
		
		// Initialize client prediction for local player
		if (IsClient && !IsServer)
		{
			_clientPrediction.Initialize(player);
		}
		
		GD.Print($"Local player created: {playerName} (ID: {playerId})");
	}
	
	private void CreateRemotePlayer(int playerId, string playerName)
	{
		// Load player scene
		var playerScene = GD.Load<PackedScene>("res://Prefabs/player.tscn");
		if (playerScene == null)
		{
			GD.PrintErr("Could not load player scene!");
			return;
		}
		
		var player = playerScene.Instantiate<PlayerController>();
		player.PlayerName = playerName;
		player.NetworkId = playerId;
		player.IsLocal = false;
		player.SetMultiplayerAuthority(playerId);
		
		// Add to scene
		GetTree().CurrentScene.AddChild(player);
		_players[playerId] = player;
		
		GD.Print($"Remote player created: {playerName} (ID: {playerId})");
	}
	
	public PlayerController GetPlayer(int playerId)
	{
		return _players.ContainsKey(playerId) ? _players[playerId] : null;
	}
	
	public PlayerController GetLocalPlayer()
	{
		foreach (var player in _players.Values)
		{
			if (player.IsLocal) return player;
		}
		return null;
	}
	
	public List<PlayerController> GetAllPlayers()
	{
		return new List<PlayerController>(_players.Values);
	}
	
	public void PrintNetworkStats()
	{
		GD.Print("=== Network Manager Stats ===");
		GD.Print($"Mode: {_currentMode}");
		GD.Print($"Players: {_players.Count}");
		GD.Print($"Is Connected: {IsConnected}");
		
		if (_networkTick != null)
		{
			_networkTick.PrintPerformanceStats();
		}
		
		if (_authoritativeServer != null && IsServer)
		{
			_authoritativeServer.PrintServerStats();
		}
		
		var localPlayer = GetLocalPlayer();
		if (localPlayer != null)
		{
			localPlayer.PrintNetworkStats();
		}
	}
	
	// RPC to spawn player on all clients
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SpawnPlayerRpc(int playerId, string playerName, Vector3 spawnPosition)
	{
		if (_players.ContainsKey(playerId)) return; // Already exists
		
		CreateRemotePlayer(playerId, playerName);
		
		if (_players.ContainsKey(playerId))
		{
			_players[playerId].GlobalPosition = spawnPosition;
		}
	}
	
	// RPC to despawn player on all clients
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DespawnPlayerRpc(int playerId)
	{
		if (_players.ContainsKey(playerId))
		{
			_players[playerId].QueueFree();
			_players.Remove(playerId);
		}
	}
	
	public override void _ExitTree()
	{
		Disconnect();
		
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 