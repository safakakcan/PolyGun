using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Modern weapon system integrated with the new netcode architecture
/// Handles weapon selection, firing, and ammo management for networked gameplay
/// </summary>
public partial class ModernWeaponSystem : Node3D
{
	[Signal] public delegate void WeaponChangedEventHandler(string weaponName);
	[Signal] public delegate void AmmoChangedEventHandler(int currentAmmo, int maxAmmo);
	[Signal] public delegate void ReloadStartedEventHandler();
	[Signal] public delegate void ReloadCompletedEventHandler();
	[Signal] public delegate void WeaponFiredEventHandler(Vector3 origin, Vector3 direction);
	
	// Weapon Configuration
	[Export] public PackedScene BulletScene;
	[Export] public AudioStream[] FireSounds;
	[Export] public AudioStream[] ReloadSounds;
	
	// Resources - will be loaded at runtime if not assigned
	private PackedScene _defaultBulletScene;
	
	// Current weapon state
	private int _currentWeaponIndex = 0;
	private List<ModernWeapon> _weapons = new();
	private ModernPlayerController _player;
	
	// Network timing
	private float _lastFireTime = 0.0f;
	
	public ModernWeapon CurrentWeapon => _weapons.Count > 0 ? _weapons[_currentWeaponIndex] : null;
	public int CurrentWeaponIndex => _currentWeaponIndex;
	public int WeaponCount => _weapons.Count;
	
	public override void _Ready()
	{
		// Only initialize if not already initialized
		if (_weapons.Count == 0)
		{
			Initialize();
		}
	}
	
	public void Initialize(ModernPlayerController player = null)
	{
		_player = player ?? GetParent<ModernPlayerController>();
		LoadResources();
		InitializeWeapons();
		
		if (_weapons.Count > 0)
		{
			_currentWeaponIndex = 0; // Set initial weapon
			GD.Print($"Initial weapon set to: {CurrentWeapon.Name} ({CurrentWeapon.CurrentAmmo}/{CurrentWeapon.MaxAmmo})");
			
			// Emit initial signals
			EmitSignal(SignalName.WeaponChanged, CurrentWeapon.Name);
			EmitSignal(SignalName.AmmoChanged, CurrentWeapon.CurrentAmmo, CurrentWeapon.MaxAmmo);
		}
		else
		{
			GD.PrintErr("No weapons were initialized!");
		}
	}
	
	private void LoadResources()
	{
		// Load bullet scene if not assigned
		if (BulletScene == null)
		{
			_defaultBulletScene = GD.Load<PackedScene>("res://Prefabs/bullet.tscn");
			BulletScene = _defaultBulletScene;
		}
		
		// Load fire sound if not assigned
		if (FireSounds == null || FireSounds.Length == 0)
		{
			var fireSound = GD.Load<AudioStream>("res://Sounds/fire.wav");
			if (fireSound != null)
			{
				FireSounds = new AudioStream[] { fireSound };
			}
		}
	}
	
	private void InitializeWeapons()
	{
		// Create default weapons
		var pistol = new ModernWeapon
		{
			Name = "Pistol",
			Damage = 25.0f,
			MaxAmmo = 12,
			CurrentAmmo = 12,
			FireRate = 0.5f,
			ReloadTime = 1.5f,
			Range = 50.0f,
			Spread = 0.02f
		};
		
		var rifle = new ModernWeapon
		{
			Name = "Assault Rifle",
			Damage = 20.0f,
			MaxAmmo = 30,
			CurrentAmmo = 30,
			FireRate = 0.1f,
			ReloadTime = 2.0f,
			Range = 100.0f,
			Spread = 0.03f
		};
		
		var sniper = new ModernWeapon
		{
			Name = "Sniper Rifle",
			Damage = 75.0f,
			MaxAmmo = 5,
			CurrentAmmo = 5,
			FireRate = 1.5f, // Much slower fire rate
			ReloadTime = 3.0f, // Longer reload time
			Range = 200.0f, // Long range
			Spread = 0.005f // Very high accuracy
		};
		
		_weapons.Add(pistol);
		_weapons.Add(rifle);
		_weapons.Add(sniper);
		
		GD.Print($"Initialized {_weapons.Count} weapons");
		GD.Print($"Pistol: {pistol.CurrentAmmo}/{pistol.MaxAmmo} ammo, FireRate: {pistol.FireRate}, Damage: {pistol.Damage}");
		GD.Print($"Assault Rifle: {rifle.CurrentAmmo}/{rifle.MaxAmmo} ammo, FireRate: {rifle.FireRate}, Damage: {rifle.Damage}");
		GD.Print($"Sniper Rifle: {sniper.CurrentAmmo}/{sniper.MaxAmmo} ammo, FireRate: {sniper.FireRate}, Damage: {sniper.Damage}");
	}
	
	public bool TryFire(Vector3 firePoint, Vector3 direction)
	{
		if (CurrentWeapon == null) 
		{
			GD.PrintErr("TryFire failed: CurrentWeapon is null");
			return false;
		}
		if (!CanFire()) 
		{
			GD.Print($"TryFire failed: CanFire() returned false. Ammo: {CurrentWeapon.CurrentAmmo}, Reloading: {CurrentWeapon.IsReloading}");
			return false;
		}
		
		// Apply spread
		var spreadDirection = ApplySpread(direction);
		
		// Create bullet or hitscan
		CreateBullet(firePoint, spreadDirection);
		
		// Update weapon state
		CurrentWeapon.CurrentAmmo--;
		CurrentWeapon.LastFireTime = Time.GetUnixTimeFromSystem();
		_lastFireTime = (float)Time.GetUnixTimeFromSystem();
		
		// Play fire sound
		PlayFireSound();
		
		// Emit signals
		EmitSignal(SignalName.WeaponFired, firePoint, spreadDirection);
		EmitSignal(SignalName.AmmoChanged, CurrentWeapon.CurrentAmmo, CurrentWeapon.MaxAmmo);
		
		// Auto-reload if empty
		if (CurrentWeapon.CurrentAmmo <= 0)
		{
			StartReload();
		}
		
		return true;
	}
	
	public void StartReload()
	{
		if (CurrentWeapon == null) return;
		if (!CanReload()) return;
		
		CurrentWeapon.IsReloading = true;
		CurrentWeapon.ReloadStartTime = Time.GetUnixTimeFromSystem();
		
		PlayReloadSound();
		EmitSignal(SignalName.ReloadStarted);
		
		// Create reload timer
		var timer = GetTree().CreateTimer(CurrentWeapon.ReloadTime);
		timer.Timeout += CompleteReload;
	}
	
	private void CompleteReload()
	{
		if (CurrentWeapon == null) return;
		
		CurrentWeapon.IsReloading = false;
		CurrentWeapon.CurrentAmmo = CurrentWeapon.MaxAmmo;
		
		EmitSignal(SignalName.AmmoChanged, CurrentWeapon.CurrentAmmo, CurrentWeapon.MaxAmmo);
		EmitSignal(SignalName.ReloadCompleted);
	}
	
	public void SwitchToWeapon(int index)
	{
		if (index < 0 || index >= _weapons.Count) 
		{
			GD.PrintErr($"Invalid weapon index: {index}, available weapons: {_weapons.Count}");
			return;
		}
		if (index == _currentWeaponIndex) 
		{
			GD.Print($"Already on weapon {index}");
			return;
		}
		
		_currentWeaponIndex = index;
		EmitSignal(SignalName.WeaponChanged, CurrentWeapon.Name);
		EmitSignal(SignalName.AmmoChanged, CurrentWeapon.CurrentAmmo, CurrentWeapon.MaxAmmo);
		
		GD.Print($"Switched to weapon {index}: {CurrentWeapon.Name} ({CurrentWeapon.CurrentAmmo}/{CurrentWeapon.MaxAmmo})");
	}
	
	public void SwitchToNextWeapon()
	{
		if (_weapons.Count <= 1) return;
		
		int nextIndex = (_currentWeaponIndex + 1) % _weapons.Count;
		SwitchToWeapon(nextIndex);
	}
	
	public void SwitchToPreviousWeapon()
	{
		if (_weapons.Count <= 1) return;
		
		int prevIndex = (_currentWeaponIndex - 1 + _weapons.Count) % _weapons.Count;
		SwitchToWeapon(prevIndex);
	}
	
	public bool CanFire()
	{
		if (CurrentWeapon == null) 
		{
			GD.Print("CanFire: CurrentWeapon is null");
			return false;
		}
		if (CurrentWeapon.IsReloading) 
		{
			GD.Print("CanFire: Weapon is reloading");
			return false;
		}
		if (CurrentWeapon.CurrentAmmo <= 0) 
		{
			GD.Print("CanFire: Out of ammo");
			return false;
		}
		
		var timeSinceLastFire = Time.GetUnixTimeFromSystem() - CurrentWeapon.LastFireTime;
		bool canFire = timeSinceLastFire >= CurrentWeapon.FireRate;
		
		if (!canFire)
		{
			GD.Print($"CanFire: Fire rate not ready. Time since last: {timeSinceLastFire:F3}, Required: {CurrentWeapon.FireRate}");
		}
		
		return canFire;
	}
	
	public bool CanReload()
	{
		if (CurrentWeapon == null) return false;
		if (CurrentWeapon.IsReloading) return false;
		return CurrentWeapon.CurrentAmmo < CurrentWeapon.MaxAmmo;
	}
	
	private Vector3 ApplySpread(Vector3 direction)
	{
		if (CurrentWeapon == null) return direction;
		
		var spread = CurrentWeapon.Spread;
		var spreadVector = new Vector3(
			(GD.Randf() - 0.5f) * spread,
			(GD.Randf() - 0.5f) * spread,
			(GD.Randf() - 0.5f) * spread
		);
		
		return (direction + spreadVector).Normalized();
	}
	
	private void CreateBullet(Vector3 origin, Vector3 direction)
	{
		// Try to use the bullet scene first
		if (BulletScene != null)
		{
			var bulletNode = BulletScene.Instantiate();
			if (bulletNode != null)
			{
				// Position and orient bullet
				if (bulletNode is Node3D bullet3D)
				{
					bullet3D.GlobalPosition = origin;
					bullet3D.LookAt(origin + direction, Vector3.Up);
				}
				
				// Initialize bullet - check for both old and new bullet scripts
				if (bulletNode.HasMethod("Initialize"))
				{
					bulletNode.Call("Initialize", direction, _player);
				}
				else if (bulletNode.HasMethod("SetDirection"))
				{
					bulletNode.Call("SetDirection", direction);
				}
				
				// Add to scene tree
				GetTree().CurrentScene.AddChild(bulletNode);
				GD.Print($"Bullet fired from bullet scene at {origin}");
				return;
			}
		}
		
		// Fallback: Create a simple visual bullet with physics
		CreateSimpleBullet(origin, direction);
	}
	
	private void CreateSimpleBullet(Vector3 origin, Vector3 direction)
	{
		// Create a simple bullet with RigidBody3D for physics
		var bullet = new RigidBody3D();
		bullet.Name = "SimpleBullet";
		bullet.Position = origin;
		
		// Create visual representation
		var mesh = new MeshInstance3D();
		var sphere = new SphereMesh();
		sphere.Radius = 0.02f;
		sphere.Height = 0.04f;
		mesh.Mesh = sphere;
		
		// Create material for bullet
		var material = new StandardMaterial3D();
		material.AlbedoColor = Colors.Yellow;
		material.Emission = Colors.Yellow * 0.3f;
		mesh.MaterialOverride = material;
		bullet.AddChild(mesh);
		
		// Create collision shape
		var collision = new CollisionShape3D();
		var shape = new SphereShape3D();
		shape.Radius = 0.02f;
		collision.Shape = shape;
		bullet.AddChild(collision);
		
		// Set up physics
		bullet.Mass = 0.01f;
		bullet.GravityScale = 0.1f; // Light gravity
		
		// Apply initial velocity
		var speed = 50.0f; // Bullet speed
		bullet.LinearVelocity = direction * speed;
		
		// Add to scene
		GetTree().CurrentScene.AddChild(bullet);
		
		// Remove bullet after 5 seconds
		var timer = GetTree().CreateTimer(5.0);
		timer.Timeout += () => {
			if (IsInstanceValid(bullet))
				bullet.QueueFree();
		};
		
		GD.Print($"Simple bullet created at {origin} with velocity {bullet.LinearVelocity}");
	}
	
	private void PlayFireSound()
	{
		if (FireSounds == null || FireSounds.Length == 0) return;
		
		var randomIndex = GD.RandRange(0, FireSounds.Length - 1);
		var sound = FireSounds[randomIndex];
		
		if (sound != null)
		{
			// Use a simple AudioStreamPlayer3D for now
			var audioPlayer = new AudioStreamPlayer3D();
			audioPlayer.Stream = sound;
			audioPlayer.PitchScale = GD.Randf() * 0.2f + 0.9f; // Slight pitch variation
			
			AddChild(audioPlayer);
			audioPlayer.Play();
			
			// Remove after playing
			audioPlayer.Finished += () => audioPlayer.QueueFree();
		}
	}
	
	private void PlayReloadSound()
	{
		if (ReloadSounds == null || ReloadSounds.Length == 0) return;
		
		var randomIndex = GD.RandRange(0, ReloadSounds.Length - 1);
		var sound = ReloadSounds[randomIndex];
		
		if (sound != null)
		{
			var audioPlayer = new AudioStreamPlayer3D();
			audioPlayer.Stream = sound;
			
			AddChild(audioPlayer);
			audioPlayer.Play();
			
			// Remove after playing
			audioPlayer.Finished += () => audioPlayer.QueueFree();
		}
	}
	
	// Get weapon info for UI
	public (int current, int max) GetAmmoInfo()
	{
		if (CurrentWeapon == null) return (0, 0);
		return (CurrentWeapon.CurrentAmmo, CurrentWeapon.MaxAmmo);
	}
	
	public string GetCurrentWeaponName()
	{
		return CurrentWeapon?.Name ?? "None";
	}
	
	public bool IsReloading()
	{
		return CurrentWeapon?.IsReloading ?? false;
	}
}

/// <summary>
/// Modern weapon data structure for the netcode system
/// </summary>
public class ModernWeapon
{
	public string Name { get; set; } = "";
	public float Damage { get; set; } = 25.0f;
	public int CurrentAmmo { get; set; } = 30;
	public int MaxAmmo { get; set; } = 30;
	public float FireRate { get; set; } = 0.1f; // Seconds between shots
	public float ReloadTime { get; set; } = 2.0f;
	public float Range { get; set; } = 100.0f;
	public float Spread { get; set; } = 0.05f;
	public bool IsReloading { get; set; } = false;
	public double LastFireTime { get; set; } = 0.0;
	public double ReloadStartTime { get; set; } = 0.0;
} 