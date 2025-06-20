using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages the complete multiplayer game flow including lobby, readiness, and game start
/// Allows server host to play with other clients in a coordinated manner
/// </summary>
public partial class MultiplayerGameManager : Node
{
	public static MultiplayerGameManager Instance { get; private set; }
	
	// Game states
	public enum GameState
	{
		Lobby,          // Waiting for players to join
		WaitingReady,   // All players joined, waiting for ready status
		CountingDown,   // Countdown before game starts
		Playing,        // Game in progress
		GameOver        // Game finished
	}
	
	// Current state
	public GameState CurrentState { get; private set; } = GameState.Lobby;
	public bool IsHost => Multiplayer.IsServer();
	
	// Player management
	private Dictionary<int, PlayerInfo> _players = new();
	private int _maxPlayers = 16;
	private int _minPlayersToStart = 2;
	
	// Game start coordination
	private float _countdownTime = 5.0f;
	private float _currentCountdown = 0.0f;
	private bool _gameStarted = false;
	
	// Map and spawn management
	private Vector3[] _spawnPoints = new Vector3[]
	{
		new Vector3(0, 1, 0),
		new Vector3(5, 1, 5),
		new Vector3(-5, 1, -5),
		new Vector3(5, 1, -5),
		new Vector3(-5, 1, 5),
		new Vector3(10, 1, 0),
		new Vector3(-10, 1, 0),
		new Vector3(0, 1, 10),
		new Vector3(0, 1, -10),
		new Vector3(7, 1, 7),
		new Vector3(-7, 1, -7),
		new Vector3(7, 1, -7),
		new Vector3(-7, 1, 7),
		new Vector3(0, 1, 15),
		new Vector3(0, 1, -15),
		new Vector3(15, 1, 0)
	};
	
	// Events
	[Signal] public delegate void StateChangedEventHandler(GameState newState);
	[Signal] public delegate void PlayerJoinedEventHandler(int playerId, string playerName);
	[Signal] public delegate void PlayerLeftEventHandler(int playerId, string playerName);
	[Signal] public delegate void PlayerReadyChangedEventHandler(int playerId, bool isReady);
	[Signal] public delegate void CountdownUpdateEventHandler(float timeRemaining);
	[Signal] public delegate void GameStartedEventHandler();
	[Signal] public delegate void GameEndedEventHandler(string reason);
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			InitializeManager();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void InitializeManager()
	{
		// Connect multiplayer signals
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		
		// Connect to networking systems
		if (AuthoritativeServer.Instance != null)
		{
			AuthoritativeServer.Instance.PlayerConnected += OnServerPlayerConnected;
			AuthoritativeServer.Instance.PlayerDisconnected += OnServerPlayerDisconnected;
		}
		
		SetPhysicsProcess(true);
		GD.Print("MultiplayerGameManager initialized");
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (CurrentState == GameState.CountingDown)
		{
			UpdateCountdown((float)delta);
		}
	}
	
	#region Host Functions
	
	public void StartHost(string serverName, int maxPlayers = 16, int minPlayers = 2)
	{
		if (!IsHost) return;
		
		_maxPlayers = maxPlayers;
		_minPlayersToStart = minPlayers;
		
		// Remove any existing local player first
		RemoveLocalPlayers();
		
		// Add host as first player
		var hostId = Multiplayer.GetUniqueId();
		var hostInfo = new PlayerInfo
		{
			PlayerId = hostId,
			PlayerName = "Host",
			IsReady = false,
			IsHost = true,
			SpawnIndex = 0
		};
		
		_players[hostId] = hostInfo;
		
		// Start networking systems
		AuthoritativeServer.Instance?.StartServer();
		
		// Initialize host player
		SpawnPlayer(hostId, hostInfo.PlayerName, 0);
		
		ChangeState(GameState.Lobby);
		EmitSignal(SignalName.PlayerJoined, hostId, hostInfo.PlayerName);
		
		GD.Print($"Host started server: {serverName}, Max Players: {maxPlayers}");
	}
	
	public void SetHostReady(bool ready)
	{
		if (!IsHost) return;
		
		var hostId = Multiplayer.GetUniqueId();
		if (_players.ContainsKey(hostId))
		{
			_players[hostId].IsReady = ready;
			EmitSignal(SignalName.PlayerReadyChanged, hostId, ready);
			
			// Broadcast to all clients
			Rpc(nameof(ReceivePlayerReadyChanged), hostId, ready);
			
			CheckAllPlayersReady();
		}
	}
	
	#endregion
	
	#region Client Functions
	
	public void JoinAsClient(string playerName)
	{
		if (IsHost) return;
		
		// Request to join the game
		RpcId(1, nameof(RequestJoinGame), playerName);
	}
	
	public void SetClientReady(bool ready)
	{
		if (IsHost) return;
		
		// Send ready state to host
		RpcId(1, nameof(ReceiveClientReady), Multiplayer.GetUniqueId(), ready);
	}
	
	#endregion
	
	#region Network RPCs
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestJoinGame(string playerName)
	{
		if (!IsHost) return;
		
		var playerId = Multiplayer.GetRemoteSenderId();
		
		// Check if server is full
		if (_players.Count >= _maxPlayers)
		{
			RpcId(playerId, nameof(JoinRejected), "Server is full");
			return;
		}
		
		// Check if game already started
		if (CurrentState == GameState.Playing)
		{
			RpcId(playerId, nameof(JoinRejected), "Game already in progress");
			return;
		}
		
		// Add player
		var playerInfo = new PlayerInfo
		{
			PlayerId = playerId,
			PlayerName = playerName,
			IsReady = false,
			IsHost = false,
			SpawnIndex = _players.Count
		};
		
		_players[playerId] = playerInfo;
		
		// Spawn player
		SpawnPlayer(playerId, playerName, playerInfo.SpawnIndex);
		
		// Notify everyone about new player
		EmitSignal(SignalName.PlayerJoined, playerId, playerName);
		Rpc(nameof(ReceivePlayerJoined), playerId, playerName);
		
		// Send current game state to new player
		RpcId(playerId, nameof(ReceiveGameState), (int)CurrentState);
		
		// Send all current players to new player
		foreach (var kvp in _players)
		{
			if (kvp.Key != playerId) // Don't send the player to themselves
			{
				RpcId(playerId, nameof(ReceivePlayerJoined), kvp.Key, kvp.Value.PlayerName);
				RpcId(playerId, nameof(ReceivePlayerReadyChanged), kvp.Key, kvp.Value.IsReady);
			}
		}
		
		// Send player list and ready states to everyone
		BroadcastPlayerStates();
		
		GD.Print($"Player joined: {playerName} ({playerId})");
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void JoinRejected(string reason)
	{
		GD.Print($"Join rejected: {reason}");
		// Handle join rejection on client side
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveClientReady(int playerId, bool isReady)
	{
		if (!IsHost) return;
		
		if (_players.ContainsKey(playerId))
		{
			_players[playerId].IsReady = isReady;
			EmitSignal(SignalName.PlayerReadyChanged, playerId, isReady);
			
			// Broadcast to all clients
			Rpc(nameof(ReceivePlayerReadyChanged), playerId, isReady);
			
			CheckAllPlayersReady();
		}
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceivePlayerJoined(int playerId, string playerName)
	{
		if (!_players.ContainsKey(playerId))
		{
			var playerInfo = new PlayerInfo
			{
				PlayerId = playerId,
				PlayerName = playerName,
				IsReady = false,
				IsHost = false,
				SpawnIndex = _players.Count
			};
			_players[playerId] = playerInfo;
		}
		
		EmitSignal(SignalName.PlayerJoined, playerId, playerName);
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceivePlayerLeft(int playerId, string playerName)
	{
		if (_players.ContainsKey(playerId))
		{
			_players.Remove(playerId);
			EmitSignal(SignalName.PlayerLeft, playerId, playerName);
		}
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceivePlayerReadyChanged(int playerId, bool isReady)
	{
		if (_players.ContainsKey(playerId))
		{
			_players[playerId].IsReady = isReady;
		}
		EmitSignal(SignalName.PlayerReadyChanged, playerId, isReady);
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveGameState(int state)
	{
		ChangeState((GameState)state);
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveCountdownUpdate(float timeRemaining)
	{
		_currentCountdown = timeRemaining;
		EmitSignal(SignalName.CountdownUpdate, timeRemaining);
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveGameStarted()
	{
		if (!_gameStarted)
		{
			_gameStarted = true;
			ChangeState(GameState.Playing);
			EmitSignal(SignalName.GameStarted);
			GD.Print("Game started!");
		}
	}
	
	#endregion
	
	#region Game Flow
	
	private void CheckAllPlayersReady()
	{
		if (!IsHost || CurrentState != GameState.WaitingReady) return;
		
		// Check if we have minimum players
		if (_players.Count < _minPlayersToStart) return;
		
		// Check if all players are ready
		bool allReady = _players.Values.All(p => p.IsReady);
		
		if (allReady)
		{
			StartCountdown();
		}
	}
	
	private void StartCountdown()
	{
		if (!IsHost) return;
		
		ChangeState(GameState.CountingDown);
		_currentCountdown = _countdownTime;
		
		// Broadcast countdown start
		Rpc(nameof(ReceiveGameState), (int)GameState.CountingDown);
		Rpc(nameof(ReceiveCountdownUpdate), _currentCountdown);
		
		GD.Print("Starting countdown...");
	}
	
	private void UpdateCountdown(float delta)
	{
		if (!IsHost) return;
		
		_currentCountdown -= delta;
		
		// Broadcast countdown updates every 0.1 seconds
		if (Mathf.Floor(_currentCountdown * 10) != Mathf.Floor((_currentCountdown + delta) * 10))
		{
			Rpc(nameof(ReceiveCountdownUpdate), _currentCountdown);
			EmitSignal(SignalName.CountdownUpdate, _currentCountdown);
		}
		
		if (_currentCountdown <= 0.0f)
		{
			StartGame();
		}
	}
	
	private void StartGame()
	{
		if (!IsHost || _gameStarted) return;
		
		_gameStarted = true;
		ChangeState(GameState.Playing);
		
		// Respawn all players at their spawn points
		foreach (var kvp in _players)
		{
			var playerId = kvp.Key;
			var playerInfo = kvp.Value;
			RespawnPlayer(playerId, playerInfo.SpawnIndex);
		}
		
		// Broadcast game start
		Rpc(nameof(ReceiveGameStarted));
		EmitSignal(SignalName.GameStarted);
		
		GD.Print("Game started! All players spawned.");
	}
	
	private void ChangeState(GameState newState)
	{
		if (CurrentState == newState) return;
		
		CurrentState = newState;
		EmitSignal(SignalName.StateChanged, (int)newState);
		
		GD.Print($"Game state changed to: {newState}");
		
		// Handle state-specific logic
		switch (newState)
		{
			case GameState.Lobby:
				_gameStarted = false;
				break;
				
			case GameState.WaitingReady:
				CheckAllPlayersReady();
				break;
		}
	}
	
	#endregion
	
	#region Player Management
	
	private void SpawnPlayer(int playerId, string playerName, int spawnIndex)
	{
		// Connect player to authoritative server
		AuthoritativeServer.Instance?.ConnectPlayer(playerId, playerName);
		
		// Create visual player representation
		var playerScene = GD.Load<PackedScene>("res://Scenes/ModernPlayer.tscn") ?? CreatePlayerScene();
		var playerInstance = playerScene.Instantiate<ModernPlayerController>();
		
		playerInstance.NetworkId = playerId;
		playerInstance.PlayerName = playerName;
		playerInstance.Name = $"Player_{playerId}";
		
		// Set spawn position
		var spawnPos = GetSpawnPosition(spawnIndex);
		playerInstance.SetPosition(spawnPos);
		
		// Add to scene
		GetTree().CurrentScene.AddChild(playerInstance, true);
		
		GD.Print($"Spawned player: {playerName} ({playerId}) at {spawnPos}");
	}
	
	private void RespawnPlayer(int playerId, int spawnIndex)
	{
		// Find existing player node
		var playerNode = GetTree().CurrentScene.GetNode<ModernPlayerController>($"Player_{playerId}");
		if (playerNode != null)
		{
			var spawnPos = GetSpawnPosition(spawnIndex);
			playerNode.SetPosition(spawnPos);
			
			// Reset player state if needed
			if (playerNode.IsDead)
			{
				playerNode.TakeDamage(-playerNode.Health); // Heal to full
			}
		}
	}
	
	private Vector3 GetSpawnPosition(int spawnIndex)
	{
		if (spawnIndex >= 0 && spawnIndex < _spawnPoints.Length)
		{
			return _spawnPoints[spawnIndex];
		}
		
		// Generate random spawn if index out of bounds
		var random = new Random();
		return new Vector3(
			random.Next(-15, 15),
			1,
			random.Next(-15, 15)
		);
	}
	
	private PackedScene CreatePlayerScene()
	{
		// Create a basic player scene if the file doesn't exist
		var scene = new PackedScene();
		var player = new ModernPlayerController();
		scene.Pack(player);
		return scene;
	}
	
	#endregion
	
	#region Multiplayer Events
	
	private void OnPeerConnected(long id)
	{
		GD.Print($"Peer connected: {id}");
		// Peer connection is handled in RequestJoinGame RPC
	}
	
	private void OnPeerDisconnected(long id)
	{
		var playerId = (int)id;
		
		if (_players.ContainsKey(playerId))
		{
			var playerInfo = _players[playerId];
			_players.Remove(playerId);
			
			// Remove from authoritative server
			AuthoritativeServer.Instance?.DisconnectPlayer(playerId);
			
			// Remove player node
			var playerNode = GetTree().CurrentScene.GetNode<ModernPlayerController>($"Player_{playerId}");
			playerNode?.QueueFree();
			
			// Broadcast to all clients
			EmitSignal(SignalName.PlayerLeft, playerId, playerInfo.PlayerName);
			if (IsHost)
			{
				Rpc(nameof(ReceivePlayerLeft), playerId, playerInfo.PlayerName);
			}
			
			GD.Print($"Player disconnected: {playerInfo.PlayerName} ({playerId})");
			
			// Check if we need to change state
			if (IsHost && CurrentState == GameState.CountingDown && !AllPlayersReady())
			{
				ChangeState(GameState.WaitingReady);
			}
		}
	}
	
	private void OnServerPlayerConnected(int playerId)
	{
		// Player already handled in RequestJoinGame
	}
	
	private void OnServerPlayerDisconnected(int playerId)
	{
		// Player already handled in OnPeerDisconnected
	}
	
	#endregion
	
	#region Utility Functions
	
	private void BroadcastPlayerStates()
	{
		foreach (var kvp in _players)
		{
			Rpc(nameof(ReceivePlayerReadyChanged), kvp.Key, kvp.Value.IsReady);
		}
	}
	
	private bool AllPlayersReady()
	{
		return _players.Count >= _minPlayersToStart && _players.Values.All(p => p.IsReady);
	}
	
	public void TransitionToWaitingReady()
	{
		if (IsHost && CurrentState == GameState.Lobby && _players.Count >= _minPlayersToStart)
		{
			ChangeState(GameState.WaitingReady);
			Rpc(nameof(ReceiveGameState), (int)GameState.WaitingReady);
		}
	}
	
	public PlayerInfo GetPlayerInfo(int playerId)
	{
		return _players.GetValueOrDefault(playerId);
	}
	
	public PlayerInfo[] GetAllPlayers()
	{
		return _players.Values.ToArray();
	}
	
	public int GetPlayerCount()
	{
		return _players.Count;
	}
	
	public bool IsPlayerReady(int playerId)
	{
		return _players.ContainsKey(playerId) && _players[playerId].IsReady;
	}
	
	#endregion
	
	private void RemoveLocalPlayers()
	{
		// Remove any existing local players when starting multiplayer
		var localPlayers = GetTree().GetNodesInGroup("local_players");
		foreach (Node player in localPlayers)
		{
			player.QueueFree();
		}
		
		// Also remove any players with "Player_1" name (local player)
		var localPlayer = GetTree().CurrentScene.GetNode<ModernPlayerController>("GameWorld/Player_1");
		localPlayer?.QueueFree();
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
}

/// <summary>
/// Player information for lobby management
/// </summary>
public class PlayerInfo
{
	public int PlayerId { get; set; }
	public string PlayerName { get; set; } = "";
	public bool IsReady { get; set; } = false;
	public bool IsHost { get; set; } = false;
	public int SpawnIndex { get; set; } = 0;
	public float Score { get; set; } = 0.0f;
	public int Kills { get; set; } = 0;
	public int Deaths { get; set; } = 0;
} 
