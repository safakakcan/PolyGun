using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Authoritative server with lag compensation and rollback
/// Manages game state, player validation, and hit detection
/// </summary>
public partial class AuthoritativeServer : Node
{
	public static AuthoritativeServer Instance { get; private set; }
	
	// Server state
	public bool IsRunning { get; private set; } = false;
	public uint CurrentTick => NetworkTick.Instance?.CurrentTick ?? 0;
	
	// Player management
	private Dictionary<int, NetworkPlayer> _players = new();
	private Dictionary<uint, Dictionary<int, PlayerSnapshot>> _playerHistory = new();
	
	// Configuration
	[Export] public int MaxPlayers = 16;
	[Export] public float MaxLatencyMs = 1000.0f; // 1 second max lag comp
	[Export] public bool EnableLagCompensation = true;
	[Export] public bool EnableAntiCheat = true;
	
	// Rollback state
	private WorldSnapshot _currentWorldState;
	private Dictionary<uint, WorldSnapshot> _worldHistory = new();
	
	// Events
	[Signal] public delegate void PlayerConnectedEventHandler(int playerId);
	[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
	[Signal] public delegate void PlayerHitEventHandler(int shooterId, int targetId, Vector3 hitPoint, float damage);
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			InitializeServer();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void InitializeServer()
	{
		// Connect to tick system
		if (NetworkTick.Instance != null)
		{
			NetworkTick.Instance.TickProcessed += OnTickProcessed;
		}
		
		_currentWorldState = new WorldSnapshot();
		IsRunning = true;
		
		GD.Print($"AuthoritativeServer initialized - Max Players: {MaxPlayers}, Lag Comp: {EnableLagCompensation}");
	}
	
	public void StartServer()
	{
		if (IsRunning) return;
		
		IsRunning = true;
		_players.Clear();
		_playerHistory.Clear();
		_worldHistory.Clear();
		
		GD.Print("Authoritative server started");
	}
	
	public void StopServer()
	{
		if (!IsRunning) return;
		
		IsRunning = false;
		
		// Disconnect all players
		var playerIds = _players.Keys.ToArray();
		foreach (var playerId in playerIds)
		{
			DisconnectPlayer(playerId);
		}
		
		GD.Print("Authoritative server stopped");
	}
	
	private void OnTickProcessed(uint tick)
	{
		if (!IsRunning) return;
		
		// Process all pending commands for this tick
		ProcessTickCommands(tick);
		
		// Save current world state for rollback
		SaveWorldSnapshot(tick);
		
		// Process player movements and actions
		ProcessPlayerActions(tick);
		
		// Send world state update to clients
		SendWorldUpdate(tick);
		
		// Clean up old snapshots
		CleanupOldSnapshots(tick);
	}
	
	private void ProcessTickCommands(uint tick)
	{
		var commands = NetworkTick.Instance.GetAllCommandsForTick(tick);
		
		foreach (var kvp in commands)
		{
			int playerId = kvp.Key;
			var command = kvp.Value;
			
			if (!_players.ContainsKey(playerId)) continue;
			
			var player = _players[playerId];
			
			// Validate command
			if (!ValidateCommand(player, command)) continue;
			
			// Apply lag compensation if needed
			if (EnableLagCompensation && command.HasButton(PlayerButtons.Fire))
			{
				ProcessLagCompensatedAction(player, command);
			}
			else
			{
				ProcessPlayerCommand(player, command);
			}
		}
	}
	
	private bool ValidateCommand(NetworkPlayer player, PlayerCommand command)
	{
		if (!command.IsValid) return false;
		
		// Anti-cheat validation
		if (EnableAntiCheat)
		{
			// Check command timing
			if (command.DeltaTime > NetworkTick.TICK_INTERVAL * 2.0f)
			{
				GD.PrintErr($"Player {player.PlayerId} sent invalid delta time: {command.DeltaTime}");
				return false;
			}
			
			// Check movement bounds
			if (command.Movement.LengthSquared() > 2.0f)
			{
				GD.PrintErr($"Player {player.PlayerId} sent invalid movement: {command.Movement}");
				return false;
			}
			
			// Check sequence number (prevent replay attacks)
			if (command.SequenceNumber <= player.LastSequenceNumber)
			{
				GD.PrintErr($"Player {player.PlayerId} sent old sequence number: {command.SequenceNumber}");
				return false;
			}
			
			player.LastSequenceNumber = command.SequenceNumber;
		}
		
		return true;
	}
	
	private void ProcessLagCompensatedAction(NetworkPlayer player, PlayerCommand command)
	{
		// Calculate which tick this command should be processed at
		uint compensatedTick = CalculateCompensatedTick(player, command);
		
		// Rollback world to that tick
		var originalState = SaveCurrentState();
		RollbackToTick(compensatedTick);
		
		// Process the command in the rolled back state
		ProcessPlayerCommand(player, command);
		
		// Check for hits in the compensated state
		if (command.HasButton(PlayerButtons.Fire))
		{
			ProcessWeaponFire(player, command);
		}
		
		// Restore current state
		RestoreState(originalState);
	}
	
	private uint CalculateCompensatedTick(NetworkPlayer player, PlayerCommand command)
	{
		// Calculate total latency (network + interpolation)
		float totalLatency = player.Latency + (100.0f / 1000.0f); // 100ms interpolation
		uint latencyTicks = NetworkTick.Instance.GetLatencyTicks(totalLatency * 1000.0f);
		
		// Clamp to reasonable bounds
		uint maxCompensationTicks = NetworkTick.Instance.GetLatencyTicks(MaxLatencyMs);
		latencyTicks = Math.Min(latencyTicks, maxCompensationTicks);
		
		return CurrentTick > latencyTicks ? CurrentTick - latencyTicks : 0;
	}
	
	private void ProcessPlayerCommand(NetworkPlayer player, PlayerCommand command)
	{
		// Update player state based on command
		ApplyMovement(player, command);
		ApplyRotation(player, command);
		ProcessButtons(player, command);
	}
	
	private void ApplyMovement(NetworkPlayer player, PlayerCommand command)
	{
		var movement = new Vector3(command.Movement.X, command.UpMovement, command.Movement.Y);
		var deltaTime = command.DeltaTime;
		
		// Apply basic movement physics (this would be more complex in practice)
		var velocity = player.Transform.Basis * movement * player.MoveSpeed * deltaTime;
		
		// Handle jumping
		if (command.HasButton(PlayerButtons.Jump) && player.IsOnGround)
		{
			velocity.Y = player.JumpForce;
			player.IsOnGround = false;
		}
		
		// Apply gravity
		if (!player.IsOnGround)
		{
			velocity.Y += player.Gravity * deltaTime;
		}
		
		// Update position
		player.Position += velocity;
		player.Velocity = velocity;
		
		// Simple ground check (would be more sophisticated in practice)
		if (player.Position.Y <= 0.0f)
		{
			player.Position = new Vector3(player.Position.X, 0.0f, player.Position.Z);
			player.IsOnGround = true;
			player.Velocity = new Vector3(player.Velocity.X, 0.0f, player.Velocity.Z);
		}
	}
	
	private void ApplyRotation(NetworkPlayer player, PlayerCommand command)
	{
		player.ViewAngles = command.ViewAngles;
		player.Transform = Transform3D.Identity.Rotated(Vector3.Up, Mathf.DegToRad(command.ViewAngles.Y));
	}
	
	private void ProcessButtons(NetworkPlayer player, PlayerCommand command)
	{
		// Handle weapon switching
		if (command.WeaponSlot >= 0 && command.WeaponSlot < player.Weapons.Count)
		{
			player.CurrentWeapon = command.WeaponSlot;
		}
		
		// Handle reload
		if (command.HasButton(PlayerButtons.Reload))
		{
			StartReload(player);
		}
	}
	
	private void ProcessWeaponFire(NetworkPlayer player, PlayerCommand command)
	{
		if (!CanPlayerFire(player)) return;
		
		// Perform ray cast from player position in view direction
		var fireDirection = GetFireDirection(player);
		var hitResult = PerformHitscan(player.Position, fireDirection, player.PlayerId);
		
		if (hitResult.Hit)
		{
			// Apply damage to target
			var target = _players.GetValueOrDefault(hitResult.TargetPlayerId);
			if (target != null)
			{
				ApplyDamage(target, hitResult.Damage, player.PlayerId);
				EmitSignal(SignalName.PlayerHit, player.PlayerId, target.PlayerId, hitResult.HitPoint, hitResult.Damage);
			}
		}
		
		// Consume ammo
		if (player.CurrentWeapon < player.Weapons.Count)
		{
			var weapon = player.Weapons[player.CurrentWeapon];
			weapon.CurrentAmmo = Math.Max(0, weapon.CurrentAmmo - 1);
		}
	}
	
	private Vector3 GetFireDirection(NetworkPlayer player)
	{
		var pitch = Mathf.DegToRad(player.ViewAngles.X);
		var yaw = Mathf.DegToRad(player.ViewAngles.Y);
		
		return new Vector3(
			Mathf.Sin(yaw) * Mathf.Cos(pitch),
			-Mathf.Sin(pitch),
			Mathf.Cos(yaw) * Mathf.Cos(pitch)
		).Normalized();
	}
	
	private HitResult PerformHitscan(Vector3 origin, Vector3 direction, int shooterId)
	{
		var result = new HitResult();
		float maxDistance = 1000.0f;
		
		// Check against all other players
		foreach (var kvp in _players)
		{
			var playerId = kvp.Key;
			var player = kvp.Value;
			
			if (playerId == shooterId) continue;
			
			// Simple sphere collision (would be more sophisticated in practice)
			var playerRadius = 0.5f;
			var distanceToPlayer = origin.DistanceTo(player.Position);
			
			if (distanceToPlayer < maxDistance)
			{
				// Check if ray intersects player sphere
				var toPlayer = (player.Position - origin);
				var projectedDistance = toPlayer.Dot(direction);
				
				if (projectedDistance > 0 && projectedDistance < maxDistance)
				{
					var closestPoint = origin + direction * projectedDistance;
					var distanceToRay = closestPoint.DistanceTo(player.Position);
					
					if (distanceToRay <= playerRadius)
					{
						result.Hit = true;
						result.TargetPlayerId = playerId;
						result.HitPoint = closestPoint;
						result.Damage = 25.0f; // Base damage
						break;
					}
				}
			}
		}
		
		return result;
	}
	
	private bool CanPlayerFire(NetworkPlayer player)
	{
		if (player.IsDead) return false;
		if (player.CurrentWeapon >= player.Weapons.Count) return false;
		
		var weapon = player.Weapons[player.CurrentWeapon];
		return weapon.CurrentAmmo > 0 && !weapon.IsReloading;
	}
	
	private void ApplyDamage(NetworkPlayer player, float damage, int attackerId)
	{
		player.Health = Math.Max(0, player.Health - damage);
		
		if (player.Health <= 0 && !player.IsDead)
		{
			player.IsDead = true;
			// Handle player death
		}
	}
	
	private void StartReload(NetworkPlayer player)
	{
		if (player.CurrentWeapon >= player.Weapons.Count) return;
		
		var weapon = player.Weapons[player.CurrentWeapon];
		if (!weapon.IsReloading && weapon.CurrentAmmo < weapon.MaxAmmo)
		{
			weapon.IsReloading = true;
			weapon.ReloadStartTime = NetworkTick.Instance.ServerTime;
		}
	}
	
	public void ConnectPlayer(int playerId, string playerName)
	{
		if (_players.ContainsKey(playerId)) return;
		
		var player = new NetworkPlayer
		{
			PlayerId = playerId,
			PlayerName = playerName,
			Position = GetSpawnPosition(),
			Health = 100.0f,
			MoveSpeed = 5.0f,
			JumpForce = 6.0f,
			Gravity = -9.8f,
			Weapons = CreateDefaultWeapons()
		};
		
		_players[playerId] = player;
		EmitSignal(SignalName.PlayerConnected, playerId);
		
		GD.Print($"Player connected: {playerName} ({playerId})");
	}
	
	public void DisconnectPlayer(int playerId)
	{
		if (!_players.ContainsKey(playerId)) return;
		
		_players.Remove(playerId);
		EmitSignal(SignalName.PlayerDisconnected, playerId);
		
		GD.Print($"Player disconnected: {playerId}");
	}
	
	private Vector3 GetSpawnPosition()
	{
		// Simple spawn position logic
		var spawnPoints = new Vector3[]
		{
			new Vector3(0, 1, 0),
			new Vector3(5, 1, 5),
			new Vector3(-5, 1, -5),
			new Vector3(5, 1, -5),
			new Vector3(-5, 1, 5)
		};
		
		return spawnPoints[_players.Count % spawnPoints.Length];
	}
	
	private List<NetworkWeapon> CreateDefaultWeapons()
	{
		return new List<NetworkWeapon>
		{
			new NetworkWeapon { Name = "Pistol", CurrentAmmo = 12, MaxAmmo = 12, ReloadTime = 1.5f },
			new NetworkWeapon { Name = "Rifle", CurrentAmmo = 30, MaxAmmo = 30, ReloadTime = 2.0f }
		};
	}
	
	private void SaveWorldSnapshot(uint tick)
	{
		var snapshot = new WorldSnapshot
		{
			Tick = tick,
			ServerTime = NetworkTick.Instance.ServerTime,
			Players = new Dictionary<int, PlayerSnapshot>()
		};
		
		foreach (var kvp in _players)
		{
			snapshot.Players[kvp.Key] = new PlayerSnapshot(kvp.Value);
		}
		
		_worldHistory[tick] = snapshot;
		_playerHistory[tick] = snapshot.Players;
	}
	
	private void UpdatePlayerSnapshot(NetworkPlayer player, PlayerCommand command)
	{
		// Update player snapshot is handled in SaveWorldSnapshot
	}
	
	private WorldSnapshot SaveCurrentState()
	{
		return _currentWorldState?.Clone() ?? new WorldSnapshot();
	}
	
	private void RollbackToTick(uint tick)
	{
		if (_worldHistory.ContainsKey(tick))
		{
			var snapshot = _worldHistory[tick];
			foreach (var kvp in snapshot.Players)
			{
				if (_players.ContainsKey(kvp.Key))
				{
					_players[kvp.Key].RestoreFromSnapshot(kvp.Value);
				}
			}
		}
	}
	
	private void RestoreState(WorldSnapshot state)
	{
		// Restore current state from snapshot
		foreach (var kvp in state.Players)
		{
			if (_players.ContainsKey(kvp.Key))
			{
				_players[kvp.Key].RestoreFromSnapshot(kvp.Value);
			}
		}
	}
	
	private void ProcessPlayerActions(uint tick)
	{
		// Additional processing per tick
		foreach (var player in _players.Values)
		{
			UpdatePlayerReload(player);
			UpdatePlayerPhysics(player);
		}
	}
	
	private void UpdatePlayerReload(NetworkPlayer player)
	{
		foreach (var weapon in player.Weapons)
		{
			if (weapon.IsReloading)
			{
				if (NetworkTick.Instance.ServerTime >= weapon.ReloadStartTime + weapon.ReloadTime)
				{
					weapon.IsReloading = false;
					weapon.CurrentAmmo = weapon.MaxAmmo;
				}
			}
		}
	}
	
	private void UpdatePlayerPhysics(NetworkPlayer player)
	{
		// Additional physics updates per tick
	}
	
	private void SendWorldUpdate(uint tick)
	{
		// Send world state to all clients
		var updateData = new Godot.Collections.Dictionary
		{
			["tick"] = tick,
			["server_time"] = NetworkTick.Instance.ServerTime,
			["players"] = new Godot.Collections.Array()
		};
		
		var playersArray = updateData["players"].AsGodotArray();
		foreach (var player in _players.Values)
		{
			playersArray.Add(player.ToDict());
		}
		
		// Send via RPC to all clients
		GetTree().CallGroup("clients", "ReceiveWorldUpdate", updateData);
	}
	
	private void CleanupOldSnapshots(uint tick)
	{
		uint oldestTick = tick > NetworkTick.MAX_ROLLBACK_TICKS ? tick - NetworkTick.MAX_ROLLBACK_TICKS : 0;
		
		var ticksToRemove = new List<uint>();
		foreach (var snapshotTick in _worldHistory.Keys)
		{
			if (snapshotTick < oldestTick)
				ticksToRemove.Add(snapshotTick);
		}
		
		foreach (var tickToRemove in ticksToRemove)
		{
			_worldHistory.Remove(tickToRemove);
			_playerHistory.Remove(tickToRemove);
		}
	}
	
	public NetworkPlayer GetPlayer(int playerId)
	{
		return _players.GetValueOrDefault(playerId);
	}
	
	public NetworkPlayer[] GetAllPlayers()
	{
		return _players.Values.ToArray();
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			StopServer();
			Instance = null;
		}
	}
}

// Supporting classes
public class HitResult
{
	public bool Hit { get; set; }
	public int TargetPlayerId { get; set; }
	public Vector3 HitPoint { get; set; }
	public float Damage { get; set; }
} 