using Godot;
using System;

/// <summary>
/// Represents a single frame of player input for authoritative processing
/// Contains all input data needed for one simulation tick
/// </summary>
[System.Serializable]
public class PlayerCommand
{
	// Timing information
	public uint ClientTick { get; set; }
	public uint ServerTick { get; set; }
	public float ClientTime { get; set; }
	public float ServerTime { get; set; }
	public float DeltaTime { get; set; } = NetworkTick.TICK_INTERVAL;
	
	// Player identification
	public int PlayerId { get; set; }
	
	// Input data
	public Vector3 ViewAngles { get; set; } = Vector3.Zero;
	public Vector2 Movement { get; set; } = Vector2.Zero; // Forward/backward, left/right
	public float UpMovement { get; set; } = 0.0f; // Jump/crouch
	
	// Button states (bit flags)
	public PlayerButtons Buttons { get; set; } = PlayerButtons.None;
	
	// Weapon selection
	public int WeaponSlot { get; set; } = -1;
	
	// Network validation
	public uint SequenceNumber { get; set; }
	public bool IsValid { get; set; } = true;
	public float Latency { get; set; } = 0.0f;
	
	public PlayerCommand()
	{
	}
	
	public PlayerCommand(PlayerCommand other)
	{
		ClientTick = other.ClientTick;
		ServerTick = other.ServerTick;
		ClientTime = other.ClientTime;
		ServerTime = other.ServerTime;
		DeltaTime = other.DeltaTime;
		PlayerId = other.PlayerId;
		ViewAngles = other.ViewAngles;
		Movement = other.Movement;
		UpMovement = other.UpMovement;
		Buttons = other.Buttons;
		WeaponSlot = other.WeaponSlot;
		SequenceNumber = other.SequenceNumber;
		IsValid = other.IsValid;
		Latency = other.Latency;
	}
	
	public bool HasButton(PlayerButtons button)
	{
		return (Buttons & button) != 0;
	}
	
	public void SetButton(PlayerButtons button, bool pressed)
	{
		if (pressed)
			Buttons |= button;
		else
			Buttons &= ~button;
	}
	
	/// <summary>
	/// Validate command for basic sanity checks
	/// </summary>
	public bool Validate()
	{
		// Check movement bounds
		if (Movement.LengthSquared() > 4.0f) // Max normalized movement is sqrt(2)
			return false;
			
		if (Mathf.Abs(UpMovement) > 1.0f)
			return false;
			
		// Check view angles for reasonable bounds
		if (Mathf.Abs(ViewAngles.X) > 90.0f) // Pitch limit
			return false;
			
		// Check delta time is reasonable
		if (DeltaTime <= 0.0f || DeltaTime > 0.1f) // Max 100ms
			return false;
			
		return true;
	}
	
	/// <summary>
	/// Serialize command to dictionary for network transmission
	/// </summary>
	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			["client_tick"] = ClientTick,
			["client_time"] = ClientTime,
			["delta_time"] = DeltaTime,
			["player_id"] = PlayerId,
			["view_angles"] = new Godot.Collections.Array { ViewAngles.X, ViewAngles.Y, ViewAngles.Z },
			["movement"] = new Godot.Collections.Array { Movement.X, Movement.Y },
			["up_movement"] = UpMovement,
			["buttons"] = (int)Buttons,
			["weapon_slot"] = WeaponSlot,
			["sequence"] = SequenceNumber
		};
	}
	
	/// <summary>
	/// Deserialize command from dictionary
	/// </summary>
	public static PlayerCommand FromDict(Godot.Collections.Dictionary dict)
	{
		var command = new PlayerCommand();
		
		if (dict.ContainsKey("client_tick"))
			command.ClientTick = dict["client_tick"].AsUInt32();
		if (dict.ContainsKey("client_time"))
			command.ClientTime = dict["client_time"].AsSingle();
		if (dict.ContainsKey("delta_time"))
			command.DeltaTime = dict["delta_time"].AsSingle();
		if (dict.ContainsKey("player_id"))
			command.PlayerId = dict["player_id"].AsInt32();
		
		if (dict.ContainsKey("view_angles"))
		{
			var angles = dict["view_angles"].AsGodotArray();
			command.ViewAngles = new Vector3(
				angles[0].AsSingle(),
				angles[1].AsSingle(),
				angles[2].AsSingle()
			);
		}
		
		if (dict.ContainsKey("movement"))
		{
			var movement = dict["movement"].AsGodotArray();
			command.Movement = new Vector2(
				movement[0].AsSingle(),
				movement[1].AsSingle()
			);
		}
		
		if (dict.ContainsKey("up_movement"))
			command.UpMovement = dict["up_movement"].AsSingle();
		if (dict.ContainsKey("buttons"))
			command.Buttons = (PlayerButtons)dict["buttons"].AsInt32();
		if (dict.ContainsKey("weapon_slot"))
			command.WeaponSlot = dict["weapon_slot"].AsInt32();
		if (dict.ContainsKey("sequence"))
			command.SequenceNumber = dict["sequence"].AsUInt32();
		
		command.IsValid = command.Validate();
		return command;
	}
}

/// <summary>
/// Player button flags for input state
/// </summary>
[System.Flags]
public enum PlayerButtons : int
{
	None = 0,
	Fire = 1 << 0,
	AltFire = 1 << 1,
	Reload = 1 << 2,
	Jump = 1 << 3,
	Crouch = 1 << 4,
	Walk = 1 << 5,
	Use = 1 << 6,
	WeaponNext = 1 << 7,
	WeaponPrev = 1 << 8,
	Weapon1 = 1 << 9,
	Weapon2 = 1 << 10,
	Weapon3 = 1 << 11,
	Weapon4 = 1 << 12,
	Weapon5 = 1 << 13
} 