using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Core tick system for deterministic server simulation
/// Runs at fixed 64Hz (15.625ms intervals) for competitive FPS gameplay
/// </summary>
public partial class NetworkTick : Node
{
	public static NetworkTick Instance { get; private set; }
	
	// Tick Configuration
	public const float TICK_RATE = 64.0f; // 64Hz for competitive FPS
	public const float TICK_INTERVAL = 1.0f / TICK_RATE; // ~15.625ms
	public const int MAX_ROLLBACK_TICKS = 64; // 1 second of rollback capacity
	
	// Current tick state
	public uint CurrentTick { get; private set; } = 0;
	public float ServerTime { get; private set; } = 0.0f;
	
	// Timing
	private double _accumulatedTime = 0.0;
	private double _lastTickTime = 0.0;
	
	// Command buffering
	private Dictionary<int, Queue<PlayerCommand>> _commandBuffer = new();
	private Dictionary<uint, Dictionary<int, PlayerCommand>> _tickCommands = new();
	
	// Events
	[Signal] public delegate void TickProcessedEventHandler(uint tick);
	[Signal] public delegate void CommandReceivedEventHandler(int playerId, Godot.Collections.Dictionary command);
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			SetPhysicsProcess(true);
			_lastTickTime = Time.GetUnixTimeFromSystem();
			GD.Print($"NetworkTick initialized - Tick Rate: {TICK_RATE}Hz, Interval: {TICK_INTERVAL * 1000:F1}ms");
		}
		else
		{
			QueueFree();
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (!AuthoritativeServer.Instance?.IsRunning == true) return;
		
		double currentTime = Time.GetUnixTimeFromSystem();
		_accumulatedTime += currentTime - _lastTickTime;
		_lastTickTime = currentTime;
		
		// Process ticks at fixed intervals
		while (_accumulatedTime >= TICK_INTERVAL)
		{
			ProcessTick();
			_accumulatedTime -= TICK_INTERVAL;
		}
	}
	
	private void ProcessTick()
	{
		CurrentTick++;
		ServerTime = CurrentTick * TICK_INTERVAL;
		
		// Process buffered commands for this tick
		ProcessTickCommands();
		
		// Emit tick processed signal
		EmitSignal(SignalName.TickProcessed, CurrentTick);
		
		// Clean up old tick data beyond rollback window
		CleanupOldTicks();
	}
	
	private void ProcessTickCommands()
	{
		if (!_tickCommands.ContainsKey(CurrentTick))
			_tickCommands[CurrentTick] = new Dictionary<int, PlayerCommand>();
		
		// Process commands from buffer for each player
		foreach (var kvp in _commandBuffer)
		{
			int playerId = kvp.Key;
			var commands = kvp.Value;
			
			if (commands.Count > 0)
			{
				var command = commands.Dequeue();
				_tickCommands[CurrentTick][playerId] = command;
				EmitSignal(SignalName.TickProcessed, CurrentTick);
			}
		}
	}
	
	public void BufferCommand(int playerId, PlayerCommand command)
	{
		if (!_commandBuffer.ContainsKey(playerId))
			_commandBuffer[playerId] = new Queue<PlayerCommand>();
		
		command.ClientTick = CurrentTick;
		command.ServerTime = ServerTime;
		_commandBuffer[playerId].Enqueue(command);
	}
	
	public PlayerCommand GetCommandForTick(int playerId, uint tick)
	{
		if (_tickCommands.ContainsKey(tick) && _tickCommands[tick].ContainsKey(playerId))
			return _tickCommands[tick][playerId];
		
		return null;
	}
	
	public Dictionary<int, PlayerCommand> GetAllCommandsForTick(uint tick)
	{
		return _tickCommands.GetValueOrDefault(tick, new Dictionary<int, PlayerCommand>());
	}
	
	private void CleanupOldTicks()
	{
		uint oldestTick = CurrentTick > MAX_ROLLBACK_TICKS ? CurrentTick - MAX_ROLLBACK_TICKS : 0;
		
		var ticksToRemove = new List<uint>();
		foreach (var tick in _tickCommands.Keys)
		{
			if (tick < oldestTick)
				ticksToRemove.Add(tick);
		}
		
		foreach (var tick in ticksToRemove)
			_tickCommands.Remove(tick);
	}
	
	public uint GetTickForTime(float time)
	{
		return (uint)(time / TICK_INTERVAL);
	}
	
	public float GetTimeForTick(uint tick)
	{
		return tick * TICK_INTERVAL;
	}
	
	public uint GetLatencyTicks(float latencyMs)
	{
		return (uint)Mathf.CeilToInt(latencyMs * 0.001f / TICK_INTERVAL);
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}
} 