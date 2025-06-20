using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerSpawner : Node
{
	[Export] public PackedScene PlayerScene;
	[Export] public Node3D[] SpawnPoints;
	[Export] public float SpawnProtectionTime = 3.0f;

	private Dictionary<int, PlayerController> _spawnedPlayers = new Dictionary<int, PlayerController>();
	private List<Vector3> _defaultSpawnPositions = new List<Vector3>
	{
		new Vector3(0, 1, 0),
		new Vector3(5, 1, 0),
		new Vector3(-5, 1, 0),
		new Vector3(0, 1, 5),
		new Vector3(0, 1, -5),
		new Vector3(5, 1, 5),
		new Vector3(-5, 1, -5),
		new Vector3(5, 1, -5),
		new Vector3(-5, 1, 5)
	};

	public override void _Ready()
	{
		// Connect to NetworkManager signals
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.PlayerJoined += OnPlayerJoined;
			NetworkManager.Instance.PlayerLeft += OnPlayerLeft;
			NetworkManager.Instance.ServerCreated += OnServerCreated;
			NetworkManager.Instance.ServerJoined += OnClientJoined;
		}

		// Get spawn points from scene if not set
		if (SpawnPoints == null || SpawnPoints.Length == 0)
		{
			FindSpawnPoints();
		}
	}

	private void FindSpawnPoints()
	{
		var spawnPointsGroup = GetTree().GetNodesInGroup("spawn_points");
		if (spawnPointsGroup.Count > 0)
		{
			SpawnPoints = spawnPointsGroup.Cast<Node3D>().ToArray();
		}
	}

	private void OnServerCreated()
	{
		GD.Print("Server created - spawning host player");
		SpawnPlayer(1, "Host");
	}

	private void OnClientJoined()
	{
		GD.Print("Joined server - requesting spawn");
		// Request spawn from server
		RpcId(1, nameof(RequestPlayerSpawn), Multiplayer.GetUniqueId(), OS.GetEnvironment("USERNAME"));
	}

	private void OnPlayerJoined(int playerId, string playerName)
	{
		GD.Print($"Player joined: {playerName} ({playerId})");
		
		if (NetworkManager.Instance?.IsServer == true)
		{
			// Server spawns the player for everyone
			SpawnPlayer(playerId, playerName);
		}
	}

	private void OnPlayerLeft(int playerId)
	{
		GD.Print($"Player left: {playerId}");
		DespawnPlayer(playerId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestPlayerSpawn(int playerId, string playerName)
	{
		if (!NetworkManager.Instance?.IsServer == true) return;

		GD.Print($"Spawn requested for player {playerId}: {playerName}");
		SpawnPlayer(playerId, playerName);
	}

	public void SpawnPlayer(int playerId, string playerName)
	{
		// Don't spawn if already exists
		if (_spawnedPlayers.ContainsKey(playerId))
		{
			GD.Print($"Player {playerId} already spawned");
			return;
		}

		var spawnPosition = GetSpawnPosition(playerId);
		
		// Create player instance
		var player = PlayerScene.Instantiate() as PlayerController;
		if (player == null)
		{
			GD.PrintErr("Failed to instantiate player");
			return;
		}

		// Set up player
		player.NetworkId = playerId;
		player.PlayerName = playerName;
		player.GlobalPosition = spawnPosition;
		player.Name = $"Player_{playerId}";

		// Add to scene
		GetTree().CurrentScene.AddChild(player);
		_spawnedPlayers[playerId] = player;

		GD.Print($"Spawned player {playerName} at {spawnPosition}");

		// Notify all clients about the spawn
		if (NetworkManager.Instance?.IsOnline == true)
		{
			Rpc(nameof(OnPlayerSpawned), playerId, playerName, spawnPosition);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerSpawned(int playerId, string playerName, Vector3 position)
	{
		// Clients receive notification of player spawn
		if (_spawnedPlayers.ContainsKey(playerId)) return;

		var player = PlayerScene.Instantiate() as PlayerController;
		if (player == null) return;

		player.NetworkId = playerId;
		player.PlayerName = playerName;
		player.GlobalPosition = position;
		player.Name = $"Player_{playerId}";

		GetTree().CurrentScene.AddChild(player);
		_spawnedPlayers[playerId] = player;

		GD.Print($"Remote player spawned: {playerName}");
	}

	public void DespawnPlayer(int playerId)
	{
		if (!_spawnedPlayers.ContainsKey(playerId)) return;

		var player = _spawnedPlayers[playerId];
		_spawnedPlayers.Remove(playerId);

		if (IsInstanceValid(player))
		{
			player.QueueFree();
		}

		// Notify all clients
		if (NetworkManager.Instance?.IsServer == true)
		{
			Rpc(nameof(OnPlayerDespawned), playerId);
		}

		GD.Print($"Despawned player {playerId}");
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerDespawned(int playerId)
	{
		DespawnPlayer(playerId);
	}

	public void RespawnPlayer(int playerId)
	{
		if (!_spawnedPlayers.ContainsKey(playerId)) return;

		var player = _spawnedPlayers[playerId];
		var spawnPosition = GetSpawnPosition(playerId);
		
		player.GlobalPosition = spawnPosition;
		player.Respawn();

		GD.Print($"Respawned player {playerId} at {spawnPosition}");

		// Notify all clients
		if (NetworkManager.Instance?.IsOnline == true)
		{
			Rpc(nameof(OnPlayerRespawned), playerId, spawnPosition);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerRespawned(int playerId, Vector3 position)
	{
		if (_spawnedPlayers.ContainsKey(playerId))
		{
			var player = _spawnedPlayers[playerId];
			player.GlobalPosition = position;
			player.Respawn();
		}
	}

	private Vector3 GetSpawnPosition(int playerId)
	{
		// Use spawn points if available
		if (SpawnPoints != null && SpawnPoints.Length > 0)
		{
			var index = (playerId - 1) % SpawnPoints.Length;
			return SpawnPoints[index].GlobalPosition;
		}
		
		// Use default positions
		var defaultIndex = (playerId - 1) % _defaultSpawnPositions.Count;
		return _defaultSpawnPositions[defaultIndex];
	}

	public PlayerController GetLocalPlayer()
	{
		var localId = Multiplayer.GetUniqueId();
		return _spawnedPlayers.GetValueOrDefault(localId);
	}

	public PlayerController GetPlayer(int playerId)
	{
		return _spawnedPlayers.GetValueOrDefault(playerId);
	}

	public PlayerController[] GetAllPlayers()
	{
		return _spawnedPlayers.Values.ToArray();
	}

	public void DespawnAllPlayers()
	{
		var playerIds = _spawnedPlayers.Keys.ToArray();
		foreach (var playerId in playerIds)
		{
			DespawnPlayer(playerId);
		}
	}

	public override void _ExitTree()
	{
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.PlayerJoined -= OnPlayerJoined;
			NetworkManager.Instance.PlayerLeft -= OnPlayerLeft;
			NetworkManager.Instance.ServerCreated -= OnServerCreated;
			NetworkManager.Instance.ServerJoined -= OnClientJoined;
		}
	}
} 