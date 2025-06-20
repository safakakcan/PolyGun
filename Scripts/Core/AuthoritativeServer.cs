using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class AuthoritativeServer : Node
{
	[Signal] public delegate void PlayerStateUpdatedEventHandler(int playerId);
	[Signal] public delegate void PlayerConnectedEventHandler(int playerId, string playerName);
	[Signal] public delegate void PlayerDisconnectedEventHandler(int playerId);
	[Signal] public delegate void HitConfirmedEventHandler(int shooterId, int targetId, int damage, Vector3 hitPosition);
	
	// Server Configuration
	[Export] public int MaxPlayers = 16;
	[Export] public float MaxLatencyMs = 1000.0f;
	[Export] public bool EnableLagCompensation = true;
	[Export] public bool EnableAntiCheat = true;
	[Export] public float MovementValidationTolerance = 1.5f;
	[Export] public int Port = 7777;
	
	// Server State
	private Dictionary<int, PlayerController> _players;
	private Dictionary<int, PlayerState> _currentPlayerStates;
	private Dictionary<int, string> _playerNames;
	private NetworkTick _networkTick;
	private bool _isServerRunning;
	
	// Lag Compensation
	private Dictionary<double, Dictionary<int, PlayerState>> _lagCompensationHistory;
	private double _maxHistoryTime;
	
	// Anti-Cheat
	private Dictionary<int, Vector3> _lastValidPositions;
	private Dictionary<int, double> _lastValidationTime;
	private Dictionary<int, int> _violationCounts;
	
	// Performance
	private double _totalSimulationTime;
	private int _simulationCount;
	
	public static AuthoritativeServer Instance { get; private set; }
	public bool IsServerRunning => _isServerRunning;
	public int ConnectedPlayerCount => _players.Count;
	
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
		
		// Initialize collections
		_players = new Dictionary<int, PlayerController>();
		_currentPlayerStates = new Dictionary<int, PlayerState>();
		_playerNames = new Dictionary<int, string>();
		_lagCompensationHistory = new Dictionary<double, Dictionary<int, PlayerState>>();
		_lastValidPositions = new Dictionary<int, Vector3>();
		_lastValidationTime = new Dictionary<int, double>();
		_violationCounts = new Dictionary<int, int>();
		
		// Find NetworkTick
		_networkTick = GetNode<NetworkTick>("../NetworkTick");
		if (_networkTick == null)
		{
			GD.PrintErr("AuthoritativeServer: NetworkTick not found!");
			return;
		}
		
		// Connect to NetworkTick signals
		_networkTick.TickProcessed += OnTickProcessed;
		_networkTick.CommandProcessor = OnCommandReceived;
		
		_maxHistoryTime = MaxLatencyMs / 1000.0;
		
		GD.Print("AuthoritativeServer initialized");
	}
	
	public bool StartServer()
	{
		if (_isServerRunning) return true;
		
		// Create multiplayer peer
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateServer(Port, MaxPlayers);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to start server: {error}");
			return false;
		}
		
		GetTree().GetMultiplayer().MultiplayerPeer = peer;
		GetTree().GetMultiplayer().PeerConnected += OnPeerConnected;
		GetTree().GetMultiplayer().PeerDisconnected += OnPeerDisconnected;
		
		_isServerRunning = true;
		GD.Print($"Server started on port {Port}");
		return true;
	}
	
	public void StopServer()
	{
		if (!_isServerRunning) return;
		
		// Disconnect all players
		var playerIds = new List<int>(_players.Keys);
		foreach (int playerId in playerIds)
		{
			DisconnectPlayer(playerId);
		}
		
		GetTree().GetMultiplayer().MultiplayerPeer = null;
		_isServerRunning = false;
		
		GD.Print("Server stopped");
	}
	
	private void OnPeerConnected(long id)
	{
		int playerId = (int)id;
		GD.Print($"Peer connected: {playerId}");
		
		// Player will be added when they send their first command
	}
	
	private void OnPeerDisconnected(long id)
	{
		int playerId = (int)id;
		DisconnectPlayer(playerId);
	}
	
	public bool ConnectPlayer(int playerId, string playerName, PlayerController playerController)
	{
		if (_players.ContainsKey(playerId))
		{
			GD.PrintErr($"Player {playerId} already connected");
			return false;
		}
		
		if (_players.Count >= MaxPlayers)
		{
			GD.PrintErr($"Server full ({MaxPlayers} players)");
			return false;
		}
		
		_players[playerId] = playerController;
		_playerNames[playerId] = playerName;
		_lastValidPositions[playerId] = playerController.GlobalPosition;
		_lastValidationTime[playerId] = Time.GetUnixTimeFromSystem();
		_violationCounts[playerId] = 0;
		
		// Initialize player state
		var initialState = CreatePlayerState(playerController);
		_currentPlayerStates[playerId] = initialState;
		
		EmitSignal(SignalName.PlayerConnected, playerId, playerName);
		GD.Print($"Player {playerName} (ID: {playerId}) connected");
		
		return true;
	}
	
	public void DisconnectPlayer(int playerId)
	{
		if (!_players.ContainsKey(playerId)) return;
		
		string playerName = _playerNames.ContainsKey(playerId) ? _playerNames[playerId] : "Unknown";
		
		_players.Remove(playerId);
		_playerNames.Remove(playerId);
		_currentPlayerStates.Remove(playerId);
		_lastValidPositions.Remove(playerId);
		_lastValidationTime.Remove(playerId);
		_violationCounts.Remove(playerId);
		
		EmitSignal(SignalName.PlayerDisconnected, playerId);
		GD.Print($"Player {playerName} (ID: {playerId}) disconnected");
	}
	
	private void OnTickProcessed(uint tickNumber)
	{
		double startTime = Time.GetUnixTimeFromSystem();
		
		// Update all player physics and game logic
		SimulateWorldState();
		
		// Store state for lag compensation
		if (EnableLagCompensation)
		{
			StoreHistorySnapshot();
		}
		
		// Broadcast states to all clients
		BroadcastPlayerStates();
		
		// Update performance metrics
		_simulationCount++;
		_totalSimulationTime += Time.GetUnixTimeFromSystem() - startTime;
	}
	
	private void OnCommandReceived(PlayerCommand command)
	{
		if (!_players.ContainsKey(command.PlayerId)) return;
		
		var player = _players[command.PlayerId];
		
		// Anti-cheat validation
		if (EnableAntiCheat && !ValidateCommand(command, player))
		{
			return;
		}
		
		// Apply command to player
		ApplyPlayerCommand(command, player);
		
		// Update stored state
		_currentPlayerStates[command.PlayerId] = CreatePlayerState(player);
		
		// Add to current tick snapshot
		_networkTick.AddPlayerToSnapshot(_networkTick.CurrentTick, command.PlayerId, _currentPlayerStates[command.PlayerId]);
	}
	
	private bool ValidateCommand(PlayerCommand command, PlayerController player)
	{
		// Validate movement bounds
		if (command.MoveInput.Length() > 1.1f) // Allow slight tolerance
		{
			LogViolation(command.PlayerId, "Invalid movement input magnitude");
			return false;
		}
		
		// Validate position changes
		Vector3 expectedMovement = command.MoveInput * player.MoveSpeed * (float)_networkTick.TickInterval;
		Vector3 actualMovement = player.GlobalPosition - _lastValidPositions[command.PlayerId];
		
		if (actualMovement.Length() > expectedMovement.Length() * MovementValidationTolerance)
		{
			LogViolation(command.PlayerId, "Excessive movement speed");
			return false;
		}
		
		// Validate timing
		double currentTime = Time.GetUnixTimeFromSystem();
		double timeSinceLastValidation = currentTime - _lastValidationTime[command.PlayerId];
		
		if (timeSinceLastValidation < _networkTick.TickInterval * 0.5) // Too frequent
		{
			LogViolation(command.PlayerId, "Command rate too high");
			return false;
		}
		
		// Update validation tracking
		_lastValidPositions[command.PlayerId] = player.GlobalPosition;
		_lastValidationTime[command.PlayerId] = currentTime;
		
		return true;
	}
	
	private void LogViolation(int playerId, string reason)
	{
		_violationCounts[playerId]++;
		GD.PrintErr($"Anti-cheat violation from player {playerId}: {reason} (Count: {_violationCounts[playerId]})");
		
		// Kick player after multiple violations
		if (_violationCounts[playerId] > 5)
		{
			DisconnectPlayer(playerId);
			GD.Print($"Player {playerId} kicked for repeated violations");
		}
	}
	
	private void ApplyPlayerCommand(PlayerCommand command, PlayerController player)
	{
		// Apply movement
		Vector3 movement = command.MoveInput * player.MoveSpeed;
		if (command.IsRunning)
		{
			movement *= player.RunSpeedMultiplier;
		}
		
		// Apply to player velocity (simplified - real implementation would use player's movement system)
		player.Velocity = new Vector3(movement.X, player.Velocity.Y, movement.Z);
		
		// Apply look direction
		// This would update player's rotation based on command.LookDirection
		
		// Handle combat
		if (command.Shoot && player.WeaponSystem?.CanFire() == true)
		{
			ProcessShootCommand(command, player);
		}
		
		if (command.Reload && player.WeaponSystem?.CanReload() == true)
		{
			player.WeaponSystem.Reload();
		}
		
		// Handle weapon switching
		if (command.WeaponSwitch >= 0)
		{
			player.WeaponSystem?.SwitchToWeapon(command.WeaponSwitch);
		}
		
		// Handle jump
		if (command.Jump && player.IsOnFloor())
		{
			player.Velocity = new Vector3(player.Velocity.X, player.JumpForce, player.Velocity.Z);
		}
	}
	
	private void ProcessShootCommand(PlayerCommand command, PlayerController shooter)
	{
		if (!EnableLagCompensation)
		{
			// Simple immediate shot
			ProcessShot(shooter, command.LookDirection);
			return;
		}
		
		// Lag compensated shooting
		double shotTime = command.Timestamp;
		var historicalState = GetHistoricalWorldState(shotTime);
		
		if (historicalState != null)
		{
			// Perform hit detection in historical world state
			var hitResult = PerformLagCompensatedShot(shooter, command.LookDirection, historicalState);
			
			if (hitResult.Hit)
			{
				// Apply damage to current world state
				var targetPlayer = _players[hitResult.TargetPlayerId];
				targetPlayer.TakeDamage(hitResult.Damage);
				
				EmitSignal(SignalName.HitConfirmed, shooter.GetMultiplayerAuthority(), 
						  hitResult.TargetPlayerId, hitResult.Damage, hitResult.HitPosition);
			}
		}
		else
		{
			// Fallback to immediate shot if no historical data
			ProcessShot(shooter, command.LookDirection);
		}
	}
	
	private void ProcessShot(PlayerController shooter, Vector3 direction)
	{
		// Use existing weapon system
		shooter.WeaponSystem?.TryFire(shooter.GlobalPosition, direction);
	}
	
	private Dictionary<int, PlayerState> GetHistoricalWorldState(double timestamp)
	{
		// Find closest historical state
		double closestTimeDiff = double.MaxValue;
		Dictionary<int, PlayerState> closestState = null;
		
		foreach (var kvp in _lagCompensationHistory)
		{
			double timeDiff = Math.Abs(kvp.Key - timestamp);
			if (timeDiff < closestTimeDiff)
			{
				closestTimeDiff = timeDiff;
				closestState = kvp.Value;
			}
		}
		
		// Only use if within acceptable range
		return closestTimeDiff < _maxHistoryTime ? closestState : null;
	}
	
	private (bool Hit, int TargetPlayerId, int Damage, Vector3 HitPosition) PerformLagCompensatedShot(
		PlayerController shooter, Vector3 direction, Dictionary<int, PlayerState> historicalStates)
	{
		// Perform raycast using historical player positions
		var spaceState = GetViewport().World3D.DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(
			shooter.GlobalPosition, 
			shooter.GlobalPosition + direction * 100f
		);
		
		var result = spaceState.IntersectRay(query);
		
		if (result != null && result.Count > 0)
		{
			var hitBody = result["collider"].AsGodotObject();
			
			if (hitBody is PlayerController targetPlayer && targetPlayer != shooter)
			{
				int targetId = targetPlayer.GetMultiplayerAuthority();
				
				// Verify hit using historical position
				if (historicalStates.ContainsKey(targetId))
				{
					var historicalPos = historicalStates[targetId].Position;
					var currentPos = targetPlayer.GlobalPosition;
					
					// Simple validation - in production you'd do more sophisticated checks
					if (historicalPos.DistanceTo(currentPos) < 2.0f) // Reasonable movement
					{
						return (true, targetId, shooter.WeaponSystem?.CurrentWeapon?.Damage ?? 15, 
								result["position"].AsVector3());
					}
				}
			}
		}
		
		return (false, -1, 0, Vector3.Zero);
	}
	
	private void SimulateWorldState()
	{
		foreach (var kvp in _players)
		{
			var player = kvp.Value;
			
			// Update player state
			_currentPlayerStates[kvp.Key] = CreatePlayerState(player);
			
			// Emit state update
			EmitSignal(SignalName.PlayerStateUpdated, kvp.Key);
		}
	}
	
	private void StoreHistorySnapshot()
	{
		double currentTime = Time.GetUnixTimeFromSystem();
		_lagCompensationHistory[currentTime] = new Dictionary<int, PlayerState>(_currentPlayerStates);
		
		// Cleanup old history
		var oldKeys = _lagCompensationHistory.Keys.Where(t => currentTime - t > _maxHistoryTime).ToList();
		foreach (var key in oldKeys)
		{
			_lagCompensationHistory.Remove(key);
		}
	}
	
	private PlayerState CreatePlayerState(PlayerController player)
	{
		return new PlayerState
		{
			Position = player.GlobalPosition,
			Velocity = player.Velocity,
			LookDirection = -player.GetNode<Camera3D>("Camera3D").GlobalTransform.Basis.Z,
			Health = player.CurrentHealth,
			CurrentAmmo = player.WeaponSystem?.CurrentWeapon?.CurrentAmmo ?? 0,
			CurrentWeapon = player.WeaponSystem?.CurrentWeaponIndex ?? 0,
			IsOnFloor = player.IsOnFloor(),
			IsReloading = player.WeaponSystem?.IsReloading() ?? false,
			IsDead = player.IsDead,
			Timestamp = Time.GetUnixTimeFromSystem(),
			SequenceNumber = _networkTick.CurrentTick
		};
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void BroadcastPlayerStates()
	{
		// This would be called to send state updates to all clients
		// Implementation depends on your specific networking approach
	}
	
	public void PrintServerStats()
	{
		double avgSimTime = _simulationCount > 0 ? _totalSimulationTime / _simulationCount : 0;
		
		GD.Print($"AuthoritativeServer Stats:");
		GD.Print($"  Connected Players: {_players.Count}/{MaxPlayers}");
		GD.Print($"  Average Simulation Time: {avgSimTime * 1000:F2}ms");
		GD.Print($"  Lag Compensation History: {_lagCompensationHistory.Count} snapshots");
		GD.Print($"  Total Violations: {_violationCounts.Values.Sum()}");
	}
	
	public override void _ExitTree()
	{
		StopServer();
		PrintServerStats();
	}
} 