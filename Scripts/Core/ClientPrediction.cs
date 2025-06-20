using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ClientPrediction : Node
{
	[Signal] public delegate void PredictionErrorEventHandler(float error);
	[Signal] public delegate void ReconciliationEventHandler(Vector3 correction);
	
	// Prediction Configuration
	[Export] public float PredictionTolerance = 0.1f;
	[Export] public int MaxPredictionFrames = 90; // 1 second at 90Hz
	[Export] public bool EnableSmoothing = true;
	[Export] public float SmoothingRate = 10.0f;
	[Export] public bool EnableDebugOutput = false;
	
	// Prediction State
	private PlayerController _localPlayer;
	private uint _commandSequence;
	private Dictionary<uint, PlayerCommand> _sentCommands;
	private Dictionary<uint, PlayerState> _predictedStates;
	private uint _lastConfirmedSequence;
	private Vector3 _smoothingOffset;
	
	// Input Management
	private Vector3 _currentMoveInput;
	private Vector3 _currentLookDirection;
	private bool _jumpPressed;
	private bool _shootPressed;
	private bool _reloadPressed;
	private int _weaponSwitchInput = -1;
	private bool _isRunning;
	
	// Network References
	private NetworkTick _networkTick;
	private MultiplayerApi _multiplayer;
	
	// Performance Metrics
	private Queue<float> _predictionErrors;
	private float _averagePredictionError;
	private int _reconciliationCount;
	private float _maxPredictionError;
	
	public float AveragePredictionError => _averagePredictionError;
	public float MaxPredictionError => _maxPredictionError;
	public int PendingCommands => _sentCommands.Count;
	
	public override void _Ready()
	{
		_sentCommands = new Dictionary<uint, PlayerCommand>();
		_predictedStates = new Dictionary<uint, PlayerState>();
		_predictionErrors = new Queue<float>();
		_commandSequence = 0;
		_lastConfirmedSequence = 0;
		_smoothingOffset = Vector3.Zero;
		
		// Find NetworkTick
		_networkTick = GetNode<NetworkTick>("../NetworkTick");
		if (_networkTick == null)
		{
			GD.PrintErr("ClientPrediction: NetworkTick not found!");
			return;
		}
		
		_multiplayer = GetTree().GetMultiplayer();
		
		GD.Print("ClientPrediction initialized");
	}
	
	public void Initialize(PlayerController localPlayer)
	{
		_localPlayer = localPlayer;
		GD.Print($"ClientPrediction initialized for player: {localPlayer.PlayerName}");
	}
	
	public override void _Process(double delta)
	{
		if (_localPlayer == null || !_localPlayer.IsLocal) return;
		
		// Capture input
		CaptureInput();
		
		// Send command to server
		SendCommandToServer();
		
		// Apply prediction locally
		ApplyPrediction();
		
		// Apply smoothing
		if (EnableSmoothing)
		{
			ApplySmoothing((float)delta);
		}
		
		// Cleanup old data
		CleanupOldData();
	}
	
	private void CaptureInput()
	{
		// Capture movement input
		_currentMoveInput = new Vector3(
			Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
			0,
			Input.GetActionStrength("move_back") - Input.GetActionStrength("move_forward")
		).Normalized();
		
		// Capture look direction
		var camera = _localPlayer.GetNode<Camera3D>("Camera3D");
		_currentLookDirection = -camera.GlobalTransform.Basis.Z;
		
		// Capture action inputs
		_jumpPressed = Input.IsActionJustPressed("jump");
		_shootPressed = Input.IsActionPressed("shoot");
		_reloadPressed = Input.IsActionJustPressed("reload");
		_isRunning = Input.IsActionPressed("rush");
		
		// Capture weapon switching
		_weaponSwitchInput = -1;
		for (int i = 1; i <= 3; i++)
		{
			if (Input.IsActionJustPressed($"weapon_{i}"))
			{
				_weaponSwitchInput = i - 1;
				break;
			}
		}
		
		if (Input.IsActionJustPressed("weapon_next"))
		{
			_weaponSwitchInput = (_localPlayer.WeaponSystem?.CurrentWeaponIndex ?? 0) + 1;
		}
		else if (Input.IsActionJustPressed("weapon_prev"))
		{
			_weaponSwitchInput = (_localPlayer.WeaponSystem?.CurrentWeaponIndex ?? 0) - 1;
		}
	}
	
	private void SendCommandToServer()
	{
		_commandSequence++;
		
		var command = new PlayerCommand
		{
			SequenceNumber = _commandSequence,
			Timestamp = Time.GetUnixTimeFromSystem(),
			PlayerId = _localPlayer.GetMultiplayerAuthority(),
			MoveInput = _currentMoveInput,
			LookDirection = _currentLookDirection,
			Jump = _jumpPressed,
			Shoot = _shootPressed,
			Reload = _reloadPressed,
			WeaponSwitch = _weaponSwitchInput,
			IsRunning = _isRunning,
			IsDodging = false // TODO: Implement dodge detection
		};
		
		// Store command for prediction and reconciliation
		_sentCommands[_commandSequence] = command;
		
		// Send to server (this would be implemented based on your networking setup)
		SendCommandRpc(command);
		
		if (EnableDebugOutput)
		{
			GD.Print($"Sent command {_commandSequence}: Move={_currentMoveInput}, Shoot={_shootPressed}");
		}
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SendCommandRpc(PlayerCommand command)
	{
		// This will be received by the server
		if (_multiplayer.IsServer())
		{
			_networkTick?.BufferCommand(command);
		}
	}
	
	private void ApplyPrediction()
	{
		if (!_sentCommands.ContainsKey(_commandSequence)) return;
		
		var command = _sentCommands[_commandSequence];
		
		// Apply command to local player immediately
		ApplyCommandToPlayer(command, _localPlayer);
		
		// Store predicted state
		var predictedState = CreatePlayerState(_localPlayer);
		_predictedStates[_commandSequence] = predictedState;
		
		if (EnableDebugOutput)
		{
			GD.Print($"Applied prediction {_commandSequence}: Pos={predictedState.Position}");
		}
	}
	
	private void ApplyCommandToPlayer(PlayerCommand command, PlayerController player)
	{
		// Apply movement (simplified - matches server logic)
		Vector3 movement = command.MoveInput * player.MoveSpeed * (float)_networkTick.TickInterval;
		if (command.IsRunning)
		{
			movement *= player.RunSpeedMultiplier;
		}
		
		// Apply movement with physics
		player.Velocity = new Vector3(
			movement.X / (float)_networkTick.TickInterval,
			player.Velocity.Y,
			movement.Z / (float)_networkTick.TickInterval
		);
		
		// Handle jump
		if (command.Jump && player.IsOnFloor())
		{
			player.Velocity = new Vector3(player.Velocity.X, player.JumpForce, player.Velocity.Z);
		}
		
		// Handle combat
		if (command.Shoot && player.WeaponSystem?.CanFire() == true)
		{
			// Visual/audio feedback only - server handles actual hits
			player.WeaponSystem.TryFire(player.GlobalPosition, command.LookDirection);
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
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveServerState(uint sequenceNumber, PlayerState serverState)
	{
		if (_localPlayer == null) return;
		
		// Update last confirmed sequence
		_lastConfirmedSequence = sequenceNumber;
		
		// Check if we have a prediction for this sequence
		if (!_predictedStates.ContainsKey(sequenceNumber))
		{
			// No prediction to compare against
			return;
		}
		
		var predictedState = _predictedStates[sequenceNumber];
		
		// Calculate prediction error
		float positionError = predictedState.Position.DistanceTo(serverState.Position);
		RecordPredictionError(positionError);
		
		// Check if reconciliation is needed
		if (positionError > PredictionTolerance)
		{
			PerformReconciliation(sequenceNumber, serverState);
		}
		
		if (EnableDebugOutput)
		{
			GD.Print($"Server state {sequenceNumber}: Error={positionError:F3}, Pos={serverState.Position}");
		}
	}
	
	private void PerformReconciliation(uint serverSequence, PlayerState serverState)
	{
		Vector3 positionCorrection = serverState.Position - _localPlayer.GlobalPosition;
		
		// Apply correction
		if (EnableSmoothing)
		{
			// Add to smoothing offset for gradual correction
			_smoothingOffset += positionCorrection;
		}
		else
		{
			// Immediate correction
			_localPlayer.GlobalPosition = serverState.Position;
		}
		
		// Re-apply all commands after the corrected state
		ReplayPredictions(serverSequence, serverState);
		
		_reconciliationCount++;
		EmitSignal(SignalName.Reconciliation, positionCorrection);
		
		if (EnableDebugOutput)
		{
			GD.Print($"Reconciliation: Correction={positionCorrection}, Replaying from {serverSequence}");
		}
	}
	
	private void ReplayPredictions(uint fromSequence, PlayerState authorityState)
	{
		// Set player to authority state
		ApplyServerStateToPlayer(authorityState, _localPlayer);
		
		// Re-apply all commands after this sequence
		var commandsToReplay = _sentCommands
			.Where(kvp => kvp.Key > fromSequence)
			.OrderBy(kvp => kvp.Key);
		
		foreach (var kvp in commandsToReplay)
		{
			ApplyCommandToPlayer(kvp.Value, _localPlayer);
			
			// Update predicted state
			_predictedStates[kvp.Key] = CreatePlayerState(_localPlayer);
		}
	}
	
	private void ApplyServerStateToPlayer(PlayerState state, PlayerController player)
	{
		player.GlobalPosition = state.Position;
		player.Velocity = state.Velocity;
		
		// Apply other state properties
		if (state.Health != player.CurrentHealth)
		{
			// Handle health changes (damage/healing)
			int healthDiff = (int)state.Health - player.CurrentHealth;
			if (healthDiff < 0)
			{
				player.TakeDamage(-healthDiff);
			}
			else if (healthDiff > 0)
			{
				player.Heal(healthDiff);
			}
		}
	}
	
	private void ApplySmoothing(float delta)
	{
		if (_smoothingOffset.Length() < 0.01f)
		{
			_smoothingOffset = Vector3.Zero;
			return;
		}
		
		// Gradually apply the correction
		Vector3 correction = _smoothingOffset * SmoothingRate * delta;
		_localPlayer.GlobalPosition += correction;
		_smoothingOffset -= correction;
	}
	
	private void RecordPredictionError(float error)
	{
		_predictionErrors.Enqueue(error);
		
		// Keep only recent errors
		while (_predictionErrors.Count > 30) // Last 30 errors
		{
			_predictionErrors.Dequeue();
		}
		
		// Update metrics
		_averagePredictionError = _predictionErrors.Average();
		_maxPredictionError = Mathf.Max(_maxPredictionError, error);
		
		EmitSignal(SignalName.PredictionError, error);
	}
	
	private PlayerState CreatePlayerState(PlayerController player)
	{
		return new PlayerState
		{
			Position = player.GlobalPosition,
			Velocity = player.Velocity,
			LookDirection = _currentLookDirection,
			Health = player.CurrentHealth,
			CurrentAmmo = player.WeaponSystem?.CurrentWeapon?.CurrentAmmo ?? 0,
			CurrentWeapon = player.WeaponSystem?.CurrentWeaponIndex ?? 0,
			IsOnFloor = player.IsOnFloor(),
			IsReloading = player.WeaponSystem?.IsReloading() ?? false,
			IsDead = player.IsDead,
			Timestamp = Time.GetUnixTimeFromSystem(),
			SequenceNumber = _commandSequence
		};
	}
	
	private void CleanupOldData()
	{
		// Remove old commands and predictions
		var sequencesToRemove = _sentCommands.Keys
			.Where(seq => seq < _lastConfirmedSequence - MaxPredictionFrames)
			.ToList();
		
		foreach (uint seq in sequencesToRemove)
		{
			_sentCommands.Remove(seq);
			_predictedStates.Remove(seq);
		}
	}
	
	public void PrintPredictionStats()
	{
		GD.Print($"ClientPrediction Stats:");
		GD.Print($"  Average Prediction Error: {_averagePredictionError:F3}");
		GD.Print($"  Max Prediction Error: {_maxPredictionError:F3}");
		GD.Print($"  Reconciliations: {_reconciliationCount}");
		GD.Print($"  Pending Commands: {_sentCommands.Count}");
		GD.Print($"  Current Sequence: {_commandSequence}");
		GD.Print($"  Last Confirmed: {_lastConfirmedSequence}");
	}
	
	public override void _ExitTree()
	{
		if (EnableDebugOutput)
		{
			PrintPredictionStats();
		}
	}
} 