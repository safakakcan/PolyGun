using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Modular weapon system for handling multiple weapon types
/// Integrates with the networking system for multiplayer gameplay
/// </summary>
public partial class WeaponSystem : Node3D
{
	[Signal] public delegate void WeaponFiredEventHandler(Vector3 origin, Vector3 direction);
	[Signal] public delegate void WeaponChangedEventHandler(int weaponIndex, string weaponName);
	[Signal] public delegate void AmmoChangedEventHandler(int currentAmmo, int totalAmmo);
	
	// Weapon configuration
	[Export] public PackedScene BulletScene;
	[Export] public AudioStream FireSound;
	[Export] public int InitialAmmo = 120;
	
	// Weapon definitions
	private List<WeaponData> _weapons = new();
	private int _currentWeapon = 0;
	private ModernPlayerController _player;
	private Node3D _firePoint;
	private AudioStreamPlayer3D _audioPlayer;
	
	// Fire rate limiting
	private double _lastFireTime = 0.0;
	private bool _isReloading = false;
	private double _reloadStartTime = 0.0;
	
	public override void _Ready()
	{
		_player = GetParent<ModernPlayerController>();
		
		// Find fire point
		_firePoint = GetParent().GetNode<Node3D>("GunPoint");
		if (_firePoint == null)
		{
			// Create fire point if it doesn't exist
			_firePoint = new Node3D();
			_firePoint.Name = "GunPoint";
			_firePoint.Position = new Vector3(0, 1.6f, -0.5f);
			GetParent().AddChild(_firePoint);
		}
		
		// Find audio player
		_audioPlayer = _firePoint.GetNode<AudioStreamPlayer3D>("AudioStreamPlayer3D");
		if (_audioPlayer == null)
		{
			_audioPlayer = new AudioStreamPlayer3D();
			_audioPlayer.Name = "AudioStreamPlayer3D";
			_firePoint.AddChild(_audioPlayer);
		}
		
		InitializeWeapons();
	}
	
	private void InitializeWeapons()
	{
		// Create default weapons
		_weapons.Add(new WeaponData
		{
			Name = "Assault Rifle",
			Damage = 25,
			FireRate = 0.1f, // 10 rounds per second
			MaxAmmo = 30,
			CurrentAmmo = 30,
			ReloadTime = 2.5f,
			Range = 1000f,
			Spread = 2f
		});
		
		_weapons.Add(new WeaponData
		{
			Name = "Shotgun",
			Damage = 15, // Per pellet
			FireRate = 0.8f, // Slower fire rate
			MaxAmmo = 8,
			CurrentAmmo = 8,
			ReloadTime = 3.0f,
			Range = 50f,
			Spread = 15f,
			PelletCount = 6
		});
		
		// Initialize total ammo
		foreach (var weapon in _weapons)
		{
			weapon.TotalAmmo = InitialAmmo;
		}
		
		EmitSignal(SignalName.WeaponChanged, _currentWeapon, _weapons[_currentWeapon].Name);
		EmitSignal(SignalName.AmmoChanged, _weapons[_currentWeapon].CurrentAmmo, _weapons[_currentWeapon].TotalAmmo);
	}
	
	public void HandleFire()
	{
		if (!CanFire()) return;
		
		var weapon = _weapons[_currentWeapon];
		var currentTime = Time.GetUnixTimeFromSystem();
		
		// Check fire rate
		if (currentTime - _lastFireTime < weapon.FireRate) return;
		_lastFireTime = currentTime;
		
		// Fire the weapon
		FireWeapon(weapon);
		
		// Consume ammo
		weapon.CurrentAmmo--;
		EmitSignal(SignalName.AmmoChanged, weapon.CurrentAmmo, weapon.TotalAmmo);
		
		// Auto-reload if empty
		if (weapon.CurrentAmmo <= 0 && weapon.TotalAmmo > 0)
		{
			StartReload();
		}
	}
	
	private void FireWeapon(WeaponData weapon)
	{
		var fireOrigin = _firePoint.GlobalPosition;
		var fireDirection = -_firePoint.GlobalTransform.Basis.Z; // Forward direction
		
		// Add spread
		if (weapon.Spread > 0)
		{
			var spreadX = (GD.Randf() - 0.5f) * weapon.Spread * Mathf.Pi / 180.0f;
			var spreadY = (GD.Randf() - 0.5f) * weapon.Spread * Mathf.Pi / 180.0f;
			
			var spreadRotation = Basis.Identity.Rotated(Vector3.Up, spreadX).Rotated(Vector3.Right, spreadY);
			fireDirection = spreadRotation * fireDirection;
		}
		
		// Handle multiple pellets for shotgun
		int pelletCount = weapon.PelletCount > 0 ? weapon.PelletCount : 1;
		
		for (int i = 0; i < pelletCount; i++)
		{
			var pelletDirection = fireDirection;
			
			// Add additional spread for multiple pellets
			if (pelletCount > 1)
			{
				var pelletSpreadX = (GD.Randf() - 0.5f) * weapon.Spread * Mathf.Pi / 180.0f;
				var pelletSpreadY = (GD.Randf() - 0.5f) * weapon.Spread * Mathf.Pi / 180.0f;
				var pelletSpreadRotation = Basis.Identity.Rotated(Vector3.Up, pelletSpreadX).Rotated(Vector3.Right, pelletSpreadY);
				pelletDirection = pelletSpreadRotation * pelletDirection;
			}
			
			// Create bullet
			if (BulletScene != null)
			{
				var bullet = BulletScene.Instantiate() as Bullet;
				if (bullet != null)
				{
					GetTree().Root.AddChild(bullet);
					bullet.GlobalPosition = fireOrigin;
					bullet.Initialize(pelletDirection, _player);
					bullet.Damage = weapon.Damage;
				}
			}
			else if (BulletPool.Instance != null)
			{
				// Use object pool if available
				var bullet = BulletPool.Instance.GetBullet();
				if (bullet != null)
				{
					bullet.GlobalPosition = fireOrigin;
					bullet.InitializePooled(fireOrigin, pelletDirection, _player);
					bullet.Damage = weapon.Damage;
				}
			}
		}
		
		// Play fire sound
		if (FireSound != null && _audioPlayer != null)
		{
			_audioPlayer.Stream = FireSound;
			_audioPlayer.PitchScale = GD.Randf() * 0.2f + 0.9f; // Slight pitch variation
			_audioPlayer.Play();
		}
		else if (AudioPool.Instance != null && FireSound != null)
		{
			AudioPool.Instance.PlayOneShot3D(FireSound, fireOrigin, GD.Randf() * 0.2f + 0.9f);
		}
		
		// Emit signal for networking
		EmitSignal(SignalName.WeaponFired, fireOrigin, fireDirection);
	}
	
	public void HandleReload()
	{
		if (!CanReload()) return;
		StartReload();
	}
	
	private void StartReload()
	{
		if (_isReloading) return;
		
		var weapon = _weapons[_currentWeapon];
		if (weapon.CurrentAmmo >= weapon.MaxAmmo || weapon.TotalAmmo <= 0) return;
		
		_isReloading = true;
		_reloadStartTime = Time.GetUnixTimeFromSystem();
		
		GD.Print($"Reloading {weapon.Name}...");
	}
	
	public void SwitchWeapon(int weaponIndex)
	{
		if (weaponIndex < 0 || weaponIndex >= _weapons.Count) return;
		if (weaponIndex == _currentWeapon) return;
		
		_currentWeapon = weaponIndex;
		_isReloading = false; // Cancel reload when switching
		
		EmitSignal(SignalName.WeaponChanged, _currentWeapon, _weapons[_currentWeapon].Name);
		EmitSignal(SignalName.AmmoChanged, _weapons[_currentWeapon].CurrentAmmo, _weapons[_currentWeapon].TotalAmmo);
		
		GD.Print($"Switched to {_weapons[_currentWeapon].Name}");
	}
	
	public void NextWeapon()
	{
		var nextIndex = (_currentWeapon + 1) % _weapons.Count;
		SwitchWeapon(nextIndex);
	}
	
	public void PreviousWeapon()
	{
		var prevIndex = (_currentWeapon - 1 + _weapons.Count) % _weapons.Count;
		SwitchWeapon(prevIndex);
	}
	
	public override void _Process(double delta)
	{
		// Handle reload completion
		if (_isReloading)
		{
			var weapon = _weapons[_currentWeapon];
			if (Time.GetUnixTimeFromSystem() - _reloadStartTime >= weapon.ReloadTime)
			{
				CompleteReload();
			}
		}
	}
	
	private void CompleteReload()
	{
		_isReloading = false;
		var weapon = _weapons[_currentWeapon];
		
		var ammoNeeded = weapon.MaxAmmo - weapon.CurrentAmmo;
		var ammoToReload = Mathf.Min(ammoNeeded, weapon.TotalAmmo);
		
		weapon.CurrentAmmo += ammoToReload;
		weapon.TotalAmmo -= ammoToReload;
		
		EmitSignal(SignalName.AmmoChanged, weapon.CurrentAmmo, weapon.TotalAmmo);
		GD.Print($"{weapon.Name} reloaded!");
	}
	
	private bool CanFire()
	{
		if (_isReloading) return false;
		if (_player?.IsDead == true) return false;
		
		var weapon = _weapons[_currentWeapon];
		return weapon.CurrentAmmo > 0;
	}
	
	private bool CanReload()
	{
		if (_isReloading) return false;
		if (_player?.IsDead == true) return false;
		
		var weapon = _weapons[_currentWeapon];
		return weapon.CurrentAmmo < weapon.MaxAmmo && weapon.TotalAmmo > 0;
	}
	
	// Public getters
	public string GetCurrentWeaponName() => _weapons[_currentWeapon].Name;
	public int GetCurrentAmmo() => _weapons[_currentWeapon].CurrentAmmo;
	public int GetTotalAmmo() => _weapons[_currentWeapon].TotalAmmo;
	public bool IsReloading() => _isReloading;
	public int GetWeaponCount() => _weapons.Count;
	public int GetCurrentWeaponIndex() => _currentWeapon;
}

/// <summary>
/// Data structure for weapon properties
/// </summary>
[System.Serializable]
public class WeaponData
{
	public string Name = "";
	public int Damage = 25;
	public float FireRate = 0.1f; // Seconds between shots
	public int MaxAmmo = 30;
	public int CurrentAmmo = 30;
	public int TotalAmmo = 120;
	public float ReloadTime = 2.5f;
	public float Range = 1000f;
	public float Spread = 2f; // Degrees
	public int PelletCount = 1; // For shotguns
} 