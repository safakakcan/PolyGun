using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Client-side prediction system with rollback and reconciliation
/// Provides responsive gameplay while maintaining server authority
/// </summary>
public partial class ClientPrediction : Node
{
	public static ClientPrediction Instance { get; private set; }
	
	// Prediction state
	private Dictionary<uint, PlayerCommand> _pendingCommands = new();
	private Dictionary<uint, PredictionSnapshot> _predictionHistory = new();
	private uint _lastAcknowledgedTick = 0;
	private uint _currentCommandSequence = 0;
	
	// Input buffering
	private Queue<PlayerCommand> _inputBuffer = new();
	private PlayerCommand _currentInput = new();
	
	// Reconciliation
	private bool _needsReconciliation = false;
	private Vector3 _serverPosition = Vector3.Zero;
	private Vector3 _serverVelocity = Vector3.Zero;
	private uint _serverTick = 0;
	
	// Configuration
	[Export] public float PredictionTolerance = 0.1f; // Position error tolerance in units
	[Export] public int MaxPredictionFrames = 64; // Max frames to predict ahead
	[Export] public bool EnableSmoothing = true;
	[Export] public float SmoothingRate = 10.0f;
	
	// References
	private ModernPlayerController _localPlayer;
	
	// Events
	[Signal] public delegate void PredictionCorrectedEventHandler(Vector3 errorAmount);
	[Signal] public delegate void CommandSentEventHandler(Godot.Collections.Dictionary command);
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			SetPhysicsProcess(true);
		}
		else
		{
			QueueFree();
		}
	}
	
	public void Initialize(ModernPlayerController localPlayer)
	{
		_localPlayer = localPlayer;
		_pendingCommands.Clear();
		_predictionHistory.Clear();
		_inputBuffer.Clear();
		_lastAcknowledgedTick = 0;
		_currentCommandSequence = 0;
		
		GD.Print("ClientPrediction initialized");
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (_localPlayer == null) return;
		
		// Capture input for this frame
		CaptureInput((float)delta);
		
		// Apply prediction
		ApplyPrediction();
		
		// Send command to server
		SendCommand();
		
		// Apply reconciliation if needed
		if (_needsReconciliation)
		{
			PerformReconciliation();
			_needsReconciliation = false;
		}
	}
	
	private void CaptureInput(float deltaTime)
	{
		var command = new PlayerCommand
		{
			ClientTick = NetworkTick.Instance?.CurrentTick ?? 0u,
			ClientTime = (float)Time.GetUnixTimeFromSystem(),
			DeltaTime = deltaTime,
			SequenceNumber = ++_currentCommandSequence,
			PlayerId = _localPlayer.NetworkId
		};
		
		// Capture view angles
		command.ViewAngles = _localPlayer.GetViewAngles();
		
		// Capture movement input
		var movement = Vector2.Zero;
		if (Input.IsActionPressed("move_forward"))
			movement.Y -= 1.0f;
		if (Input.IsActionPressed("move_back"))
			movement.Y += 1.0f;
		if (Input.IsActionPressed("move_left"))
			movement.X -= 1.0f;
		if (Input.IsActionPressed("move_right"))
			movement.X += 1.0f;
		
		command.Movement = movement.Normalized();
		
		// Capture vertical movement
		if (Input.IsActionPressed("jump"))
			command.UpMovement = 1.0f;
		else if (Input.IsActionPressed("crouch"))
			command.UpMovement = -1.0f;
		
		// Capture buttons
		command.SetButton(PlayerButtons.Fire, Input.IsActionPressed("shoot") || Input.IsActionPressed("fire"));
		command.SetButton(PlayerButtons.AltFire, Input.IsActionPressed("alt_fire") || Input.IsActionPressed("aim"));
		command.SetButton(PlayerButtons.Reload, Input.IsActionJustPressed("reload"));
		command.SetButton(PlayerButtons.Jump, Input.IsActionJustPressed("jump"));
		command.SetButton(PlayerButtons.Crouch, Input.IsActionPressed("crouch"));
		command.SetButton(PlayerButtons.Use, Input.IsActionJustPressed("use"));
		
		// Weapon switching
		for (int i = 1; i <= 5; i++)
		{
			if (Input.IsActionJustPressed($"weapon_{i}"))
			{
				command.WeaponSlot = i - 1;
				break;
			}
		}
		
		_currentInput = command;
		_inputBuffer.Enqueue(command);
	}
	
	private void ApplyPrediction()
	{
		if (_currentInput == null) return;
		
		// Store prediction snapshot before applying
		var snapshot = new PredictionSnapshot
		{
			Tick = _currentInput.ClientTick,
			Position = _localPlayer.GlobalPosition,
			Velocity = _localPlayer.Velocity,
			ViewAngles = _currentInput.ViewAngles,
			Command = new PlayerCommand(_currentInput)
		};
		
		_predictionHistory[_currentInput.ClientTick] = snapshot;
		_pendingCommands[_currentInput.ClientTick] = new PlayerCommand(_currentInput);
		
		// Apply movement prediction
		ApplyMovementPrediction(_currentInput);
		
		// Clean up old predictions
		CleanupOldPredictions();
	}
	
	private void ApplyMovementPrediction(PlayerCommand command)
	{
		// This mirrors the server's movement logic for prediction
		var movement = new Vector3(command.Movement.X, command.UpMovement, command.Movement.Y);
		var deltaTime = command.DeltaTime;
		
		// Get current transform for basis calculation
		var transform = _localPlayer.Transform;
		var velocity = transform.Basis * movement * _localPlayer.MoveSpeed * deltaTime;
		
		// Handle jumping
		if (command.HasButton(PlayerButtons.Jump) && _localPlayer.IsOnFloor())
		{
			velocity.Y = _localPlayer.JumpForce;
		}
		
		// Apply gravity
		if (!_localPlayer.IsOnFloor())
		{
			velocity.Y += _localPlayer.Gravity * deltaTime;
		}
		
		// Update velocity
		_localPlayer.Velocity = new Vector3(
			Mathf.Lerp(_localPlayer.Velocity.X, velocity.X, 0.25f),
			velocity.Y,
			Mathf.Lerp(_localPlayer.Velocity.Z, velocity.Z, 0.25f)
		);
		
		// Apply movement
		_localPlayer.MoveAndSlide();
	}
	
	private void SendCommand()
	{
		if (_currentInput == null) return;
		
		// Send command to server via RPC
		if (Multiplayer.HasMultiplayerPeer())
		{
			Rpc(nameof(ReceivePlayerCommand), _currentInput.ToDict());
		}
		
		EmitSignal(SignalName.PredictionCorrected, _currentInput.ToDict());
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void ReceivePlayerCommand(Godot.Collections.Dictionary commandDict)
	{
		// This is received on the server side
		var command = PlayerCommand.FromDict(commandDict);
		command.Latency = 0.050f; // Default 50ms latency estimation
		
		// Buffer command in tick system
		NetworkTick.Instance?.BufferCommand(command.PlayerId, command);
	}
	
	public void OnServerUpdate(uint serverTick, Vector3 position, Vector3 velocity, Vector3 viewAngles)
	{
		_serverTick = serverTick;
		_serverPosition = position;
		_serverVelocity = velocity;
		
		// Check if we need reconciliation
		if (_predictionHistory.ContainsKey(serverTick))
		{
			var predictedSnapshot = _predictionHistory[serverTick];
			var positionError = position.DistanceTo(predictedSnapshot.Position);
			
			if (positionError > PredictionTolerance)
			{
				_needsReconciliation = true;
				EmitSignal(SignalName.PredictionCorrected, position - predictedSnapshot.Position);
			}
		}
		
		_lastAcknowledgedTick = serverTick;
		
		// Remove acknowledged commands
		var commandsToRemove = new List<uint>();
		foreach (var tick in _pendingCommands.Keys)
		{
			if (tick <= serverTick)
				commandsToRemove.Add(tick);
		}
		
		foreach (var tick in commandsToRemove)
			_pendingCommands.Remove(tick);
	}
	
	private void PerformReconciliation()
	{
		if (!_predictionHistory.ContainsKey(_serverTick)) return;
		
		// Restore to server state
		var currentPosition = _localPlayer.GlobalPosition;
		var targetPosition = _serverPosition;
		
		if (EnableSmoothing)
		{
			// Smooth correction to avoid jarring movement
			var correctionDistance = currentPosition.DistanceTo(targetPosition);
			if (correctionDistance > PredictionTolerance * 2.0f)
			{
				// Large error - snap immediately
				_localPlayer.GlobalPosition = targetPosition;
				_localPlayer.Velocity = _serverVelocity;
			}
			else
			{
				// Small error - smooth correction
				var deltaTime = (float)GetPhysicsProcessDeltaTime();
				_localPlayer.GlobalPosition = currentPosition.Lerp(targetPosition, SmoothingRate * deltaTime);
			}
		}
		else
		{
			// Immediate correction
			_localPlayer.GlobalPosition = targetPosition;
			_localPlayer.Velocity = _serverVelocity;
		}
		
		// Re-apply pending commands
		ReapplyPendingCommands();
	}
	
	private void ReapplyPendingCommands()
	{
		var sortedCommands = new List<PlayerCommand>();
		foreach (var kvp in _pendingCommands)
		{
			if (kvp.Key > _serverTick)
				sortedCommands.Add(kvp.Value);
		}
		
		sortedCommands.Sort((a, b) => a.SequenceNumber.CompareTo(b.SequenceNumber));
		
		foreach (var command in sortedCommands)
		{
			ApplyMovementPrediction(command);
		}
	}
	
	private void CleanupOldPredictions()
	{
		uint oldestTick = _lastAcknowledgedTick > (uint)MaxPredictionFrames ? 
			_lastAcknowledgedTick - (uint)MaxPredictionFrames : 0u;
		
		var ticksToRemove = new List<uint>();
		foreach (var tick in _predictionHistory.Keys)
		{
			if (tick < oldestTick)
				ticksToRemove.Add(tick);
		}
		
		foreach (var tick in ticksToRemove)
		{
			_predictionHistory.Remove(tick);
			_pendingCommands.Remove(tick);
		}
	}
	
	public Vector3 GetPredictedPosition()
	{
		return _localPlayer?.GlobalPosition ?? Vector3.Zero;
	}
	
	public Vector3 GetPredictedVelocity()
	{
		return _localPlayer?.Velocity ?? Vector3.Zero;
	}
	
	public bool IsPredicting()
	{
		return _pendingCommands.Count > 0;
	}
	
	public int GetPendingCommandCount()
	{
		return _pendingCommands.Count;
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}
}

/// <summary>
/// Snapshot of predicted state for reconciliation
/// </summary>
public class PredictionSnapshot
{
	public uint Tick { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Velocity { get; set; }
	public Vector3 ViewAngles { get; set; }
	public PlayerCommand Command { get; set; }
} 