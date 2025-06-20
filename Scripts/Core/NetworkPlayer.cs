using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Server-side representation of a player for authoritative simulation
/// Contains all state needed for rollback and prediction
/// </summary>
public class NetworkPlayer
{
	// Identity
	public int PlayerId { get; set; }
	public string PlayerName { get; set; } = "";
	
	// Transform
	public Vector3 Position { get; set; } = Vector3.Zero;
	public Vector3 Velocity { get; set; } = Vector3.Zero;
	public Vector3 ViewAngles { get; set; } = Vector3.Zero;
	public Transform3D Transform { get; set; } = Transform3D.Identity;
	
	// Health & Status
	public float Health { get; set; } = 100.0f;
	public float MaxHealth { get; set; } = 100.0f;
	public bool IsDead { get; set; } = false;
	public bool IsOnGround { get; set; } = true;
	
	// Movement
	public float MoveSpeed { get; set; } = 5.0f;
	public float JumpForce { get; set; } = 6.0f;
	public float Gravity { get; set; } = -9.8f;
	
	// Weapons
	public List<NetworkWeapon> Weapons { get; set; } = new();
	public int CurrentWeapon { get; set; } = 0;
	
	// Network
	public float Latency { get; set; } = 0.0f;
	public uint LastSequenceNumber { get; set; } = 0;
	public float LastUpdateTime { get; set; } = 0.0f;
	
	public NetworkPlayer()
	{
	}
	
	public void RestoreFromSnapshot(PlayerSnapshot snapshot)
	{
		Position = snapshot.Position;
		Velocity = snapshot.Velocity;
		ViewAngles = snapshot.ViewAngles;
		Transform = snapshot.Transform;
		Health = snapshot.Health;
		IsDead = snapshot.IsDead;
		IsOnGround = snapshot.IsOnGround;
		CurrentWeapon = snapshot.CurrentWeapon;
		
		// Restore weapon states
		for (int i = 0; i < Weapons.Count && i < snapshot.WeaponStates.Count; i++)
		{
			Weapons[i].RestoreFromSnapshot(snapshot.WeaponStates[i]);
		}
	}
	
	public Godot.Collections.Dictionary ToDict()
	{
		var weaponsArray = new Godot.Collections.Array();
		foreach (var weapon in Weapons)
		{
			weaponsArray.Add(weapon.ToDict());
		}
		
		return new Godot.Collections.Dictionary
		{
			["player_id"] = PlayerId,
			["player_name"] = PlayerName,
			["position"] = new Godot.Collections.Array { Position.X, Position.Y, Position.Z },
			["velocity"] = new Godot.Collections.Array { Velocity.X, Velocity.Y, Velocity.Z },
			["view_angles"] = new Godot.Collections.Array { ViewAngles.X, ViewAngles.Y, ViewAngles.Z },
			["health"] = Health,
			["is_dead"] = IsDead,
			["is_on_ground"] = IsOnGround,
			["current_weapon"] = CurrentWeapon,
			["weapons"] = weaponsArray,
			["latency"] = Latency
		};
	}
	
	public static NetworkPlayer FromDict(Godot.Collections.Dictionary dict)
	{
		var player = new NetworkPlayer();
		
		if (dict.ContainsKey("player_id"))
			player.PlayerId = dict["player_id"].AsInt32();
		if (dict.ContainsKey("player_name"))
			player.PlayerName = dict["player_name"].AsString();
		
		if (dict.ContainsKey("position"))
		{
			var pos = dict["position"].AsGodotArray();
			player.Position = new Vector3(pos[0].AsSingle(), pos[1].AsSingle(), pos[2].AsSingle());
		}
		
		if (dict.ContainsKey("velocity"))
		{
			var vel = dict["velocity"].AsGodotArray();
			player.Velocity = new Vector3(vel[0].AsSingle(), vel[1].AsSingle(), vel[2].AsSingle());
		}
		
		if (dict.ContainsKey("view_angles"))
		{
			var angles = dict["view_angles"].AsGodotArray();
			player.ViewAngles = new Vector3(angles[0].AsSingle(), angles[1].AsSingle(), angles[2].AsSingle());
		}
		
		if (dict.ContainsKey("health"))
			player.Health = dict["health"].AsSingle();
		if (dict.ContainsKey("is_dead"))
			player.IsDead = dict["is_dead"].AsBool();
		if (dict.ContainsKey("is_on_ground"))
			player.IsOnGround = dict["is_on_ground"].AsBool();
		if (dict.ContainsKey("current_weapon"))
			player.CurrentWeapon = dict["current_weapon"].AsInt32();
		if (dict.ContainsKey("latency"))
			player.Latency = dict["latency"].AsSingle();
		
		if (dict.ContainsKey("weapons"))
		{
			var weaponsArray = dict["weapons"].AsGodotArray();
			player.Weapons.Clear();
			foreach (var weaponDict in weaponsArray)
			{
				player.Weapons.Add(NetworkWeapon.FromDict(weaponDict.AsGodotDictionary()));
			}
		}
		
		return player;
	}
}

/// <summary>
/// Network weapon representation for server simulation
/// </summary>
public class NetworkWeapon
{
	public string Name { get; set; } = "";
	public int CurrentAmmo { get; set; } = 0;
	public int MaxAmmo { get; set; } = 30;
	public float ReloadTime { get; set; } = 2.0f;
	public bool IsReloading { get; set; } = false;
	public float ReloadStartTime { get; set; } = 0.0f;
	public float FireRate { get; set; } = 0.1f; // Seconds between shots
	public float LastFireTime { get; set; } = 0.0f;
	
	public void RestoreFromSnapshot(WeaponSnapshot snapshot)
	{
		CurrentAmmo = snapshot.CurrentAmmo;
		IsReloading = snapshot.IsReloading;
		ReloadStartTime = snapshot.ReloadStartTime;
		LastFireTime = snapshot.LastFireTime;
	}
	
	public Godot.Collections.Dictionary ToDict()
	{
		return new Godot.Collections.Dictionary
		{
			["name"] = Name,
			["current_ammo"] = CurrentAmmo,
			["max_ammo"] = MaxAmmo,
			["reload_time"] = ReloadTime,
			["is_reloading"] = IsReloading,
			["reload_start_time"] = ReloadStartTime,
			["fire_rate"] = FireRate,
			["last_fire_time"] = LastFireTime
		};
	}
	
	public static NetworkWeapon FromDict(Godot.Collections.Dictionary dict)
	{
		var weapon = new NetworkWeapon();
		
		if (dict.ContainsKey("name"))
			weapon.Name = dict["name"].AsString();
		if (dict.ContainsKey("current_ammo"))
			weapon.CurrentAmmo = dict["current_ammo"].AsInt32();
		if (dict.ContainsKey("max_ammo"))
			weapon.MaxAmmo = dict["max_ammo"].AsInt32();
		if (dict.ContainsKey("reload_time"))
			weapon.ReloadTime = dict["reload_time"].AsSingle();
		if (dict.ContainsKey("is_reloading"))
			weapon.IsReloading = dict["is_reloading"].AsBool();
		if (dict.ContainsKey("reload_start_time"))
			weapon.ReloadStartTime = dict["reload_start_time"].AsSingle();
		if (dict.ContainsKey("fire_rate"))
			weapon.FireRate = dict["fire_rate"].AsSingle();
		if (dict.ContainsKey("last_fire_time"))
			weapon.LastFireTime = dict["last_fire_time"].AsSingle();
		
		return weapon;
	}
}

/// <summary>
/// Snapshot of player state for rollback
/// </summary>
public class PlayerSnapshot
{
	public uint Tick { get; set; }
	public float ServerTime { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Velocity { get; set; }
	public Vector3 ViewAngles { get; set; }
	public Transform3D Transform { get; set; }
	public float Health { get; set; }
	public bool IsDead { get; set; }
	public bool IsOnGround { get; set; }
	public int CurrentWeapon { get; set; }
	public List<WeaponSnapshot> WeaponStates { get; set; } = new();
	
	public PlayerSnapshot()
	{
	}
	
	public PlayerSnapshot(NetworkPlayer player)
	{
		Position = player.Position;
		Velocity = player.Velocity;
		ViewAngles = player.ViewAngles;
		Transform = player.Transform;
		Health = player.Health;
		IsDead = player.IsDead;
		IsOnGround = player.IsOnGround;
		CurrentWeapon = player.CurrentWeapon;
		
		WeaponStates.Clear();
		foreach (var weapon in player.Weapons)
		{
			WeaponStates.Add(new WeaponSnapshot(weapon));
		}
	}
	
	public PlayerSnapshot Clone()
	{
		var clone = new PlayerSnapshot
		{
			Tick = Tick,
			ServerTime = ServerTime,
			Position = Position,
			Velocity = Velocity,
			ViewAngles = ViewAngles,
			Transform = Transform,
			Health = Health,
			IsDead = IsDead,
			IsOnGround = IsOnGround,
			CurrentWeapon = CurrentWeapon
		};
		
		foreach (var weaponState in WeaponStates)
		{
			clone.WeaponStates.Add(weaponState.Clone());
		}
		
		return clone;
	}
}

/// <summary>
/// Snapshot of weapon state for rollback
/// </summary>
public class WeaponSnapshot
{
	public int CurrentAmmo { get; set; }
	public bool IsReloading { get; set; }
	public float ReloadStartTime { get; set; }
	public float LastFireTime { get; set; }
	
	public WeaponSnapshot()
	{
	}
	
	public WeaponSnapshot(NetworkWeapon weapon)
	{
		CurrentAmmo = weapon.CurrentAmmo;
		IsReloading = weapon.IsReloading;
		ReloadStartTime = weapon.ReloadStartTime;
		LastFireTime = weapon.LastFireTime;
	}
	
	public WeaponSnapshot Clone()
	{
		return new WeaponSnapshot
		{
			CurrentAmmo = CurrentAmmo,
			IsReloading = IsReloading,
			ReloadStartTime = ReloadStartTime,
			LastFireTime = LastFireTime
		};
	}
}

/// <summary>
/// World snapshot for rollback
/// </summary>
public class WorldSnapshot
{
	public uint Tick { get; set; }
	public float ServerTime { get; set; }
	public Dictionary<int, PlayerSnapshot> Players { get; set; } = new();
	
	public WorldSnapshot Clone()
	{
		var clone = new WorldSnapshot
		{
			Tick = Tick,
			ServerTime = ServerTime
		};
		
		foreach (var kvp in Players)
		{
			clone.Players[kvp.Key] = kvp.Value.Clone();
		}
		
		return clone;
	}
} 