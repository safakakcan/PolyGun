using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public abstract partial class Weapon : Node3D
{
	[Export] public string WeaponName = "";
	[Export] public int Damage = 15;
	[Export] public int MaxAmmo = 30;
	[Export] public float FireRate = 0.1f; // Time between shots
	[Export] public float ReloadTime = 2.0f;
	[Export] public float Range = 100f;
	[Export] public float Spread = 0.05f;
	[Export] public AudioStream FireSound;
	[Export] public AudioStream ReloadSound;
	[Export] public AudioStream EmptySound;
	[Export] public PackedScene BulletScene;
	
	protected int _currentAmmo;
	protected bool _isReloading;
	protected double _lastFireTime;
	protected double _reloadStartTime;
	
	[Signal] public delegate void AmmoChangedEventHandler(int currentAmmo, int maxAmmo);
	[Signal] public delegate void ReloadStartedEventHandler();
	[Signal] public delegate void ReloadCompletedEventHandler();
	
	public int CurrentAmmo => _currentAmmo;
	public bool IsReloading => _isReloading;
	public bool CanFire => !_isReloading && _currentAmmo > 0 && Time.GetUnixTimeFromSystem() - _lastFireTime >= FireRate;
	public bool CanReload => !_isReloading && _currentAmmo < MaxAmmo;
	
	public override void _Ready()
	{
		_currentAmmo = MaxAmmo;
		EmitSignal(SignalName.AmmoChanged, _currentAmmo, MaxAmmo);
	}
	
	public override void _Process(double delta)
	{
		UpdateReload();
	}
	
	protected virtual void UpdateReload()
	{
		if (_isReloading && Time.GetUnixTimeFromSystem() - _reloadStartTime >= ReloadTime)
		{
			CompleteReload();
		}
	}
	
	public virtual bool TryFire(Vector3 firePoint, Vector3 direction, PlayerController shooter = null)
	{
		if (!CanFire) return false;
		
		Fire(firePoint, direction, shooter);
		_currentAmmo--;
		_lastFireTime = Time.GetUnixTimeFromSystem();
		
		EmitSignal(SignalName.AmmoChanged, _currentAmmo, MaxAmmo);
		
		if (_currentAmmo <= 0)
		{
			StartReload();
		}
		
		return true;
	}
	
	protected abstract void Fire(Vector3 firePoint, Vector3 direction, PlayerController shooter);
	
	public virtual void StartReload()
	{
		if (!CanReload) return;
		
		_isReloading = true;
		_reloadStartTime = Time.GetUnixTimeFromSystem();
		EmitSignal(SignalName.ReloadStarted);
		
		PlaySound(ReloadSound);
	}
	
	protected virtual void CompleteReload()
	{
		_isReloading = false;
		_currentAmmo = MaxAmmo;
		EmitSignal(SignalName.AmmoChanged, _currentAmmo, MaxAmmo);
		EmitSignal(SignalName.ReloadCompleted);
	}
	
	protected virtual void PlaySound(AudioStream sound, float pitch = 1.0f)
	{
		if (sound == null) return;
		
		// Use audio pool for better performance
		AudioPool.Instance?.PlayOneShot3D(sound, GlobalPosition, pitch, 0f);
	}
	
	protected Vector3 ApplySpread(Vector3 direction)
	{
		Vector3 spread = new Vector3(
			(GD.Randf() - 0.5f) * Spread,
			(GD.Randf() - 0.5f) * Spread,
			(GD.Randf() - 0.5f) * Spread
		);
		
		return (direction + spread).Normalized();
	}
}

public partial class AssaultRifle : Weapon
{
	public override void _Ready()
	{
		WeaponName = "Assault Rifle";
		Damage = 15;
		MaxAmmo = 30;
		FireRate = 0.1f;
		ReloadTime = 2.0f;
		Spread = 0.05f;
		
		base._Ready();
	}
	
	protected override void Fire(Vector3 firePoint, Vector3 direction, PlayerController shooter)
	{
		CreateBullet(firePoint, ApplySpread(direction), shooter);
		PlaySound(FireSound, GD.Randf() * 0.1f + 0.95f);
	}
	
	private void CreateBullet(Vector3 position, Vector3 direction, PlayerController shooter)
	{
		if (BulletScene == null) return;
		
		var bullet = (Bullet)BulletScene.Instantiate();
		bullet.GlobalPosition = position;
		bullet.Initialize(direction, shooter);
		bullet.Damage = Damage;
		
		GetTree().Root.AddChild(bullet);
	}
}

public partial class Shotgun : Weapon
{
	[Export] public int PelletCount = 8;
	[Export] public float PelletSpread = 0.15f;
	
	public override void _Ready()
	{
		WeaponName = "Shotgun";
		Damage = 8; // Per pellet
		MaxAmmo = 8;
		FireRate = 0.8f; // Slower fire rate
		ReloadTime = 3.0f; // Longer reload
		
		base._Ready();
	}
	
	protected override void Fire(Vector3 firePoint, Vector3 direction, PlayerController shooter)
	{
		// Fire multiple pellets
		for (int i = 0; i < PelletCount; i++)
		{
			Vector3 pelletDirection = ApplyPelletSpread(direction);
			CreateBullet(firePoint, pelletDirection, shooter);
		}
		
		PlaySound(FireSound, GD.Randf() * 0.1f + 0.95f);
	}
	
	private Vector3 ApplyPelletSpread(Vector3 direction)
	{
		Vector3 spread = new Vector3(
			(GD.Randf() - 0.5f) * PelletSpread,
			(GD.Randf() - 0.5f) * PelletSpread,
			(GD.Randf() - 0.5f) * PelletSpread
		);
		
		return (direction + spread).Normalized();
	}
	
	private void CreateBullet(Vector3 position, Vector3 direction, PlayerController shooter)
	{
		if (BulletScene == null) return;
		
		var bullet = (Bullet)BulletScene.Instantiate();
		bullet.GlobalPosition = position;
		bullet.Initialize(direction, shooter);
		bullet.Damage = Damage; // Each pellet does individual damage
		
		GetTree().Root.AddChild(bullet);
	}
}

public partial class WeaponSystem : Node3D
{
	[Export] public PackedScene[] WeaponScenes;
	
	private List<Weapon> _weapons = new List<Weapon>();
	private int _currentWeaponIndex = 0;
	private Weapon _currentWeapon;
	private PlayerController _player;
	
	[Signal] public delegate void WeaponChangedEventHandler(string weaponName);
	
	public Weapon CurrentWeapon => _currentWeapon;
	public int CurrentWeaponIndex => _currentWeaponIndex;
	public int WeaponCount => _weapons.Count;
	
	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		InitializeWeapons();
	}
	
	private void InitializeWeapons()
	{
		// If no weapon scenes are configured, create default weapons
		if (WeaponScenes == null || WeaponScenes.Length == 0)
		{
			CreateDefaultWeapons();
		}
		else
		{
			// Instantiate all weapons from scenes
			foreach (var weaponScene in WeaponScenes)
			{
				if (weaponScene != null)
				{
					var weapon = (Weapon)weaponScene.Instantiate();
					AddChild(weapon);
					_weapons.Add(weapon);
					weapon.Visible = false;
					
					// Connect weapon signals
					weapon.AmmoChanged += OnWeaponAmmoChanged;
					weapon.ReloadStarted += OnWeaponReloadStarted;
					weapon.ReloadCompleted += OnWeaponReloadCompleted;
				}
			}
		}
		
		// Set initial weapon
		if (_weapons.Count > 0)
		{
			SwitchToWeapon(0);
		}
	}
	
	private void CreateDefaultWeapons()
	{
		// Create default Assault Rifle
		var assaultRifle = new AssaultRifle();
		assaultRifle.BulletScene = _player?.BulletScene; // Use player's bullet scene if available
		assaultRifle.FireSound = _player?.FireSound; // Use player's fire sound if available
		AddChild(assaultRifle);
		_weapons.Add(assaultRifle);
		assaultRifle.Visible = false;
		
		// Connect weapon signals
		assaultRifle.AmmoChanged += OnWeaponAmmoChanged;
		assaultRifle.ReloadStarted += OnWeaponReloadStarted;
		assaultRifle.ReloadCompleted += OnWeaponReloadCompleted;
		
		// Create default Shotgun
		var shotgun = new Shotgun();
		shotgun.BulletScene = _player?.BulletScene; // Use player's bullet scene if available
		shotgun.FireSound = _player?.FireSound; // Use player's fire sound if available
		AddChild(shotgun);
		_weapons.Add(shotgun);
		shotgun.Visible = false;
		
		// Connect weapon signals
		shotgun.AmmoChanged += OnWeaponAmmoChanged;
		shotgun.ReloadStarted += OnWeaponReloadStarted;
		shotgun.ReloadCompleted += OnWeaponReloadCompleted;
		
		GD.Print("Created default weapons: Assault Rifle and Shotgun");
	}
	
	public void SwitchToWeapon(int index)
	{
		if (index < 0 || index >= _weapons.Count) return;
		
		// Hide current weapon
		if (_currentWeapon != null)
		{
			_currentWeapon.Visible = false;
		}
		
		// Switch to new weapon
		_currentWeaponIndex = index;
		_currentWeapon = _weapons[_currentWeaponIndex];
		_currentWeapon.Visible = true;
		
		EmitSignal(SignalName.WeaponChanged, _currentWeapon.WeaponName);
		
		// Update UI
		if (_player != null)
		{
			_player.EmitSignal(PlayerController.SignalName.AmmoChanged, 
							  _currentWeapon.CurrentAmmo, _currentWeapon.MaxAmmo);
		}
	}
	
	public void SwitchToNextWeapon()
	{
		if (_weapons.Count == 0) return;
		
		int nextIndex = (_currentWeaponIndex + 1) % _weapons.Count;
		SwitchToWeapon(nextIndex);
	}
	
	public void SwitchToPreviousWeapon()
	{
		if (_weapons.Count == 0) return;
		
		int prevIndex = (_currentWeaponIndex - 1 + _weapons.Count) % _weapons.Count;
		SwitchToWeapon(prevIndex);
	}
	
	public bool TryFire(Vector3 firePoint, Vector3 direction)
	{
		if (_currentWeapon == null) return false;
		
		return _currentWeapon.TryFire(firePoint, direction, _player);
	}
	
	public void Reload()
	{
		_currentWeapon?.StartReload();
	}
	
	public bool CanFire()
	{
		return _currentWeapon?.CanFire ?? false;
	}
	
	public bool CanReload()
	{
		return _currentWeapon?.CanReload ?? false;
	}
	
	public bool IsReloading()
	{
		return _currentWeapon?.IsReloading ?? false;
	}
	
	private void OnWeaponAmmoChanged(int currentAmmo, int maxAmmo)
	{
		if (_player != null)
		{
			_player.EmitSignal(PlayerController.SignalName.AmmoChanged, currentAmmo, maxAmmo);
		}
	}
	
	private void OnWeaponReloadStarted()
	{
		GD.Print($"Reloading {_currentWeapon.WeaponName}...");
	}
	
	private void OnWeaponReloadCompleted()
	{
		GD.Print($"{_currentWeapon.WeaponName} reload complete!");
	}
}

[System.Serializable]
public class WeaponAttachment
{
	public string Name;
	public WeaponAttachmentType Type;
	public float DamageModifier = 1.0f;
	public float AccuracyModifier = 1.0f;
	public float FireRateModifier = 1.0f;
	public float ReloadSpeedModifier = 1.0f;
}

public enum WeaponAttachmentType
{
	Scope,
	Barrel,
	Grip,
	Magazine
}

public partial class AdvancedWeapon : Weapon
{
	// Note: Cannot export custom arrays in Godot, use code initialization instead
	public WeaponAttachment[] AvailableAttachments;
	private List<WeaponAttachment> _equippedAttachments = new List<WeaponAttachment>();
	
	[Export] public int BaseExperience = 0;
	[Export] public int CurrentExperience = 0;
	[Export] public int ExperienceToNextLevel = 100;
	[Export] public int WeaponLevel = 1;
	
	public float EffectiveDamage => Damage * GetAttachmentModifier(WeaponAttachmentType.Barrel);
	public float EffectiveAccuracy => (1.0f / Spread) * GetAttachmentModifier(WeaponAttachmentType.Scope);
	public float EffectiveFireRate => FireRate / GetAttachmentModifier(WeaponAttachmentType.Grip);
	
	public void EquipAttachment(WeaponAttachment attachment)
	{
		// Remove existing attachment of same type
		_equippedAttachments.RemoveAll(a => a.Type == attachment.Type);
		_equippedAttachments.Add(attachment);
		UpdateWeaponStats();
	}
	
	public void AddExperience(int amount)
	{
		CurrentExperience += amount;
		CheckLevelUp();
	}
	
	private void CheckLevelUp()
	{
		while (CurrentExperience >= ExperienceToNextLevel)
		{
			WeaponLevel++;
			CurrentExperience -= ExperienceToNextLevel;
			ExperienceToNextLevel = Mathf.RoundToInt(ExperienceToNextLevel * 1.2f);
			GD.Print($"{WeaponName} leveled up to {WeaponLevel}!");
		}
	}
	
	private float GetAttachmentModifier(WeaponAttachmentType type)
	{
		var attachment = _equippedAttachments.FirstOrDefault(a => a.Type == type);
		return type switch
		{
			WeaponAttachmentType.Scope => attachment?.AccuracyModifier ?? 1.0f,
			WeaponAttachmentType.Barrel => attachment?.DamageModifier ?? 1.0f,
			WeaponAttachmentType.Grip => attachment?.FireRateModifier ?? 1.0f,
			WeaponAttachmentType.Magazine => attachment?.ReloadSpeedModifier ?? 1.0f,
			_ => 1.0f
		};
	}
	
	private void UpdateWeaponStats()
	{
		// Update weapon properties based on attachments
		// This would trigger UI updates and visual changes
	}
	
	protected override void Fire(Vector3 firePoint, Vector3 direction, PlayerController shooter)
	{
		// Default implementation for advanced weapon
		CreateBullet(firePoint, ApplySpread(direction), shooter);
		PlaySound(FireSound, GD.Randf() * 0.1f + 0.95f);
	}
	
	private void CreateBullet(Vector3 position, Vector3 direction, PlayerController shooter)
	{
		if (BulletScene == null) return;
		
		var bullet = (Bullet)BulletScene.Instantiate();
		bullet.GlobalPosition = position;
		bullet.Initialize(direction, shooter);
		bullet.Damage = (int)EffectiveDamage; // Use effective damage with attachments
		
		GetTree().Root.AddChild(bullet);
	}
} 