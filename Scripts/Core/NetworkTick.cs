using Godot;
using System;
using System.Collections.Generic;

public struct PlayerCommand
{
	public uint SequenceNumber;
	public double Timestamp;
	public int PlayerId;
	public Vector3 MoveInput;
	public Vector3 LookDirection;
	public bool Jump;
	public bool Shoot;
	public bool Reload;
	public int WeaponSwitch; // -1 = no switch, 0+ = weapon index
	public bool IsRunning;
	public bool IsDodging;
}

public struct PlayerState
{
	public Vector3 Position;
	public Vector3 Velocity;
	public Vector3 LookDirection;
	public float Health;
	public int CurrentAmmo;
	public int CurrentWeapon;
	public bool IsOnFloor;
	public bool IsReloading;
	public bool IsDead;
	public double Timestamp;
	public uint SequenceNumber;
}

public struct WorldSnapshot
{
	public double Timestamp;
	public uint TickNumber;
	public Dictionary<int, PlayerState> PlayerStates;
	
	public WorldSnapshot(double timestamp, uint tickNumber)
	{
		Timestamp = timestamp;
		TickNumber = tickNumber;
		PlayerStates = new Dictionary<int, PlayerState>();
	}
}

public partial class NetworkTick : Node
{
	[Signal] public delegate void TickProcessedEventHandler(uint tickNumber);
	[Signal] public delegate void CommandReceivedEventHandler();
	
	// Network Configuration
	[Export] public double TickRate = 90.0; // 90Hz for competitive play
	[Export] public int MaxCommandsPerTick = 3;
	[Export] public int HistoryBufferSize = 180; // 2 seconds at 90Hz
	[Export] public double MaxLatencyMs = 1000.0;
	
	// Tick Management
	private double _tickInterval;
	private double _accumulator;
	private uint _currentTick;
	private double _lastTickTime;
	
	// Command Buffering
	private Queue<PlayerCommand>[] _commandBuffers;
	private Dictionary<int, uint> _lastProcessedSequence;
	
	// History Management
	private Queue<WorldSnapshot> _worldHistory;
	private Dictionary<uint, WorldSnapshot> _tickSnapshots;
	
	// Performance Metrics
	private double _averageTickTime;
	private int _ticksProcessed;
	private double _maxTickTime;
	
	public double TickInterval => _tickInterval;
	public uint CurrentTick => _currentTick;
	public double CurrentTickTime => _lastTickTime;
	
	public override void _Ready()
	{
		_tickInterval = 1.0 / TickRate;
		_accumulator = 0.0;
		_currentTick = 0;
		_lastTickTime = Time.GetUnixTimeFromSystem();
		
		// Initialize command buffers for max players
		_commandBuffers = new Queue<PlayerCommand>[16];
		for (int i = 0; i < _commandBuffers.Length; i++)
		{
			_commandBuffers[i] = new Queue<PlayerCommand>();
		}
		
		_lastProcessedSequence = new Dictionary<int, uint>();
		_worldHistory = new Queue<WorldSnapshot>();
		_tickSnapshots = new Dictionary<uint, WorldSnapshot>();
		
		GD.Print($"NetworkTick initialized: {TickRate}Hz ({_tickInterval * 1000:F2}ms intervals)");
	}
	
	public override void _Process(double delta)
	{
		_accumulator += delta;
		
		// Fixed timestep simulation
		while (_accumulator >= _tickInterval)
		{
			ProcessTick();
			_accumulator -= _tickInterval;
		}
	}
	
	private void ProcessTick()
	{
		double tickStartTime = Time.GetUnixTimeFromSystem();
		
		_currentTick++;
		_lastTickTime = tickStartTime;
		
		// Process all buffered commands for this tick
		ProcessBufferedCommands();
		
		// Create world snapshot
		CreateWorldSnapshot();
		
		// Emit tick processed signal
		EmitSignal(SignalName.TickProcessed, _currentTick);
		
		// Update performance metrics
		UpdatePerformanceMetrics(tickStartTime);
		
		// Cleanup old history
		CleanupHistory();
	}
	
	public void BufferCommand(PlayerCommand command)
	{
		// Validate command timing
		if (!IsCommandValid(command))
		{
			GD.PrintErr($"Invalid command from player {command.PlayerId}");
			return;
		}
		
		// Buffer command for processing
		if (command.PlayerId >= 0 && command.PlayerId < _commandBuffers.Length)
		{
			_commandBuffers[command.PlayerId].Enqueue(command);
			
			// Limit commands per tick to prevent spam
			while (_commandBuffers[command.PlayerId].Count > MaxCommandsPerTick)
			{
				_commandBuffers[command.PlayerId].Dequeue();
			}
		}
	}
	
	private bool IsCommandValid(PlayerCommand command)
	{
		// Check sequence number progression
		if (_lastProcessedSequence.ContainsKey(command.PlayerId))
		{
			uint lastSeq = _lastProcessedSequence[command.PlayerId];
			if (command.SequenceNumber <= lastSeq)
			{
				return false; // Old or duplicate command
			}
		}
		
		// Check timestamp is within acceptable range
		double currentTime = Time.GetUnixTimeFromSystem();
		double commandAge = (currentTime - command.Timestamp) * 1000.0;
		
		if (commandAge > MaxLatencyMs)
		{
			return false; // Too old
		}
		
		return true;
	}
	
	private void ProcessBufferedCommands()
	{
		for (int playerId = 0; playerId < _commandBuffers.Length; playerId++)
		{
			var buffer = _commandBuffers[playerId];
			
			while (buffer.Count > 0)
			{
				var command = buffer.Dequeue();
				
				// Update last processed sequence
				_lastProcessedSequence[command.PlayerId] = command.SequenceNumber;
				
				// Emit command for processing
				EmitSignal(SignalName.CommandReceived);
				
				// Process command directly since we can't pass it via signal
				ProcessCommand(command);
			}
		}
	}
	
	private void CreateWorldSnapshot()
	{
		var snapshot = new WorldSnapshot(_lastTickTime, _currentTick);
		
		// Collect all player states from AuthoritativeServer
		// This will be populated by the AuthoritativeServer
		
		_tickSnapshots[_currentTick] = snapshot;
		_worldHistory.Enqueue(snapshot);
	}
	
	public WorldSnapshot? GetSnapshotAtTime(double timestamp)
	{
		foreach (var snapshot in _worldHistory)
		{
			if (Mathf.Abs(snapshot.Timestamp - timestamp) < _tickInterval * 0.5)
			{
				return snapshot;
			}
		}
		
		return null;
	}
	
	public WorldSnapshot? GetSnapshotAtTick(uint tickNumber)
	{
		return _tickSnapshots.ContainsKey(tickNumber) ? _tickSnapshots[tickNumber] : null;
	}
	
	public void AddPlayerToSnapshot(uint tickNumber, int playerId, PlayerState playerState)
	{
		if (_tickSnapshots.ContainsKey(tickNumber))
		{
			var snapshot = _tickSnapshots[tickNumber];
			snapshot.PlayerStates[playerId] = playerState;
			_tickSnapshots[tickNumber] = snapshot;
		}
	}
	
	private void UpdatePerformanceMetrics(double tickStartTime)
	{
		double tickTime = Time.GetUnixTimeFromSystem() - tickStartTime;
		_ticksProcessed++;
		
		_averageTickTime = (_averageTickTime * (_ticksProcessed - 1) + tickTime) / _ticksProcessed;
		_maxTickTime = Mathf.Max(_maxTickTime, tickTime);
		
		// Log performance warnings
		if (tickTime > _tickInterval * 0.8) // 80% of tick budget
		{
			GD.PrintErr($"Tick {_currentTick} took {tickTime * 1000:F2}ms (>{_tickInterval * 800:F1}ms warning threshold)");
		}
	}
	
	private void CleanupHistory()
	{
		// Remove old snapshots beyond history buffer
		while (_worldHistory.Count > HistoryBufferSize)
		{
			var oldSnapshot = _worldHistory.Dequeue();
			_tickSnapshots.Remove(oldSnapshot.TickNumber);
		}
	}
	
	// Add command processing delegate for AuthoritativeServer
	public System.Action<PlayerCommand> CommandProcessor { get; set; }
	
	private void ProcessCommand(PlayerCommand command)
	{
		CommandProcessor?.Invoke(command);
	}
	
	public void PrintPerformanceStats()
	{
		GD.Print($"NetworkTick Performance:");
		GD.Print($"  Current Tick: {_currentTick}");
		GD.Print($"  Average Tick Time: {_averageTickTime * 1000:F2}ms");
		GD.Print($"  Max Tick Time: {_maxTickTime * 1000:F2}ms");
		GD.Print($"  History Buffer: {_worldHistory.Count}/{HistoryBufferSize}");
		GD.Print($"  Target Interval: {_tickInterval * 1000:F2}ms");
	}
	
	public override void _ExitTree()
	{
		PrintPerformanceStats();
	}
} 