using Godot;
using System;

public partial class PlayerController : CharacterBody3D
{
	[Signal] public delegate void HealthChangedEventHandler(int newHealth);
	[Signal] public delegate void AmmoChangedEventHandler(int currentAmmo, int maxAmmo);
	[Signal] public delegate void PlayerDiedEventHandler();
	[Signal] public delegate void WeaponSwitchedEventHandler(string weaponName);

	// Network
	[Export] public bool IsLocal = true;
	[Export] public string PlayerName = "Player";
	
	// Health System
	[Export] public int MaxHealth = 100;
	[Export] public float HealthRegenerationRate = 2.0f;
	[Export] public float HealthRegenerationDelay = 5.0f;
	private int _currentHealth;
	private double _lastDamageTime;
	private bool _isDead;

	// Movement
	[Export] public float MoveSpeed = 5f;
	[Export] public float RunSpeedMultiplier = 2f;
	[Export] public float MouseSensitivity = 0.1f;
	[Export] public float JumpForce = 6f;
	[Export] public float Gravity = -9.8f;
	
	// Dodge System
	[Export] public float DodgeForce = 20f;
	[Export] public float DodgeCooldown = 1.5f;
	[Export] public float DoubleTapWindow = 0.25f;
	
	private float _yaw;
	private float _pitch;
	private string _lastPressedDirectionKey = "";
	private double _lastPressedDirectionDelta;
	private double _dodgeCooldown;
	
	// Components
	private Camera3D _camera;
	private Node3D _gunPoint;
	private WeaponSystem _weaponSystem;

	// Legacy support - will be removed once weapon system is fully integrated
	[Export] public PackedScene BulletScene;
	[Export] public AudioStream FireSound;
	[Export] public AudioStream ReloadSound;
	[Export] public AudioStream EmptyClipSound;

	public int CurrentHealth => _currentHealth;
	public bool IsDead => _isDead;
	public WeaponSystem WeaponSystem => _weaponSystem;

	public override void _Ready()
	{
		InitializeComponents();
		InitializeStats();
		SetupUI();
	}

	private void InitializeComponents()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_gunPoint = GetNode<Node3D>("GunPoint");
		
		// Initialize weapon system
		_weaponSystem = GetNode<WeaponSystem>("WeaponSystem");
		if (_weaponSystem == null)
		{
			// Create weapon system if it doesn't exist
			_weaponSystem = new WeaponSystem();
			AddChild(_weaponSystem);
		}
		
		if (IsLocal) 
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
		else
		{
			_camera.Current = false;
		}
	}

	private void InitializeStats()
	{
		_currentHealth = MaxHealth;
		_isDead = false;
	}

	private void SetupUI()
	{
		EmitSignal(SignalName.HealthChanged, _currentHealth);
		
		// Get ammo info from weapon system
		if (_weaponSystem?.CurrentWeapon != null)
		{
			EmitSignal(SignalName.AmmoChanged, 
					  _weaponSystem.CurrentWeapon.CurrentAmmo, 
					  _weaponSystem.CurrentWeapon.MaxAmmo);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!IsLocal || _isDead) return;

		if (@event is InputEventMouseMotion mm)
		{
			HandleMouseLook(mm);
		}
		
		// Handle weapon switching
		HandleWeaponSwitching(@event);
	}

	private void HandleMouseLook(InputEventMouseMotion mouseMotion)
	{
		_yaw -= mouseMotion.Relative.X * MouseSensitivity;
		_pitch -= mouseMotion.Relative.Y * MouseSensitivity;
		_pitch = Mathf.Clamp(_pitch, -90f, 90f);

		RotationDegrees = new Vector3(0f, _yaw, 0f);
		_camera.RotationDegrees = new Vector3(_pitch, 0f, 0f);
	}

	private void HandleWeaponSwitching(InputEvent @event)
	{
		if (_weaponSystem == null || _weaponSystem.WeaponCount == 0) return;

		// Mouse wheel weapon switching
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.Pressed)
			{
				if (mouseButton.ButtonIndex == MouseButton.WheelUp)
				{
					_weaponSystem.SwitchToPreviousWeapon();
				}
				else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
				{
					_weaponSystem.SwitchToNextWeapon();
				}
			}
		}

		// Number key weapon switching - only check for existing weapons
		int weaponCount = _weaponSystem?.WeaponCount ?? 0;
		for (int i = 1; i <= weaponCount && i <= 3; i++) // Only check up to weapon_3 since that's what we have in Input Map
		{
			if (@event.IsActionPressed($"weapon_{i}"))
			{
				_weaponSystem.SwitchToWeapon(i - 1);
				break;
			}
		}

		// Next/Previous weapon keys
		if (@event.IsActionPressed("weapon_next"))
		{
			_weaponSystem.SwitchToNextWeapon();
		}
		else if (@event.IsActionPressed("weapon_prev"))
		{
			_weaponSystem.SwitchToPreviousWeapon();
		}
	}

	public override void _Process(double delta)
	{
		if (_isDead) return;

		UpdateCooldowns(delta);
		UpdateHealthRegeneration(delta);

		if (IsLocal)
		{
			HandleInput(delta);
		}

		MoveAndSlide();
	}

	private void UpdateCooldowns(double delta)
	{
		_dodgeCooldown -= delta;
		_lastPressedDirectionDelta -= delta;
	}

	private void UpdateHealthRegeneration(double delta)
	{
		if (_currentHealth < MaxHealth && 
			Time.GetUnixTimeFromSystem() - _lastDamageTime >= HealthRegenerationDelay)
		{
			_currentHealth = Mathf.Min(MaxHealth, 
				_currentHealth + Mathf.CeilToInt((float)(HealthRegenerationRate * delta)));
			EmitSignal(SignalName.HealthChanged, _currentHealth);
		}
	}

	private void HandleInput(double delta)
	{
		HandleMovement(delta);
		HandleCombat();
		HandleReload();
	}

	private void HandleMovement(double delta)
	{
		Vector3 direction = GetMovementDirection();
		Vector3 velocity = CalculateMovementVelocity(direction);
		
		// Apply movement with smooth interpolation
		Velocity = new Vector3(
			Mathf.Lerp(Velocity.X, velocity.X, 0.25f), 
			Velocity.Y, 
			Mathf.Lerp(Velocity.Z, velocity.Z, 0.25f)
		);
	}

	private Vector3 GetMovementDirection()
	{
		return new Vector3(
			Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
			0,
			Input.GetActionStrength("move_back") - Input.GetActionStrength("move_forward")
		).Normalized();
	}

	private Vector3 CalculateMovementVelocity(Vector3 direction)
	{
		Vector3 velocity = Transform.Basis * direction * MoveSpeed;
		
		// Check for dodge
		if (CheckDodgeInput())
		{
			velocity *= DodgeForce;
		}
		else
		{
			// Apply run multiplier
			velocity *= Input.IsActionPressed("rush") ? RunSpeedMultiplier : 1f;
		}

		return velocity;
	}

	private bool CheckDodgeInput()
	{
		string[] directions = { "move_forward", "move_back", "move_left", "move_right" };
		
		foreach (string direction in directions)
		{
			if (Input.IsActionJustPressed(direction))
			{
				if (_lastPressedDirectionKey == direction && 
					_dodgeCooldown <= 0 && 
					_lastPressedDirectionDelta >= 0)
				{
					_dodgeCooldown = DodgeCooldown;
					return true;
				}
				
				_lastPressedDirectionKey = direction;
				_lastPressedDirectionDelta = DoubleTapWindow;
				break;
			}
		}
		
		return false;
	}

	private void HandleCombat()
	{
		if (Input.IsActionPressed("shoot") && CanShoot())
		{
			Shoot();
		}
	}

	private void HandleReload()
	{
		if (Input.IsActionJustPressed("reload") && CanReload())
		{
			StartReload();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isDead) return;

		ApplyGravity(delta);
		HandleJump();
	}

	private void ApplyGravity(double delta)
	{
		if (!IsOnFloor())
		{
			Velocity = new Vector3(Velocity.X, Velocity.Y + Gravity * (float)delta, Velocity.Z);
		}
	}

	private void HandleJump()
	{
		if (Input.IsActionJustPressed("jump") && IsLocal && IsOnFloor())
		{
			Velocity = new Vector3(Velocity.X, JumpForce, Velocity.Z);
		}
	}

	private bool CanShoot()
	{
		return _weaponSystem?.CanFire() ?? false;
	}

	private bool CanReload()
	{
		return _weaponSystem?.CanReload() ?? false;
	}

	private void Shoot()
	{
		if (_weaponSystem == null) return;

		Vector3 fireDirection = CalculateBulletDirection();
		bool fired = _weaponSystem.TryFire(_gunPoint.GlobalPosition, fireDirection);
		
		if (!fired && _weaponSystem.CurrentWeapon?.CurrentAmmo <= 0)
		{
			// Play empty sound using audio pool
			if (EmptyClipSound != null)
			{
				AudioPool.Instance?.PlayOneShot3D(EmptyClipSound, _gunPoint.GlobalPosition, 1.0f, -5f);
			}
		}
	}

	private Vector3 CalculateBulletDirection()
	{
		return -_camera.GlobalTransform.Basis.Z;
	}

	private void StartReload()
	{
		_weaponSystem?.Reload();
	}

	public void TakeDamage(int amount)
	{
		if (_isDead) return;
		
		_currentHealth = Mathf.Max(0, _currentHealth - amount);
		_lastDamageTime = Time.GetUnixTimeFromSystem();
		
		EmitSignal(SignalName.HealthChanged, _currentHealth);
		GD.Print($"{PlayerName} took {amount} damage! Health: {_currentHealth}");
		
		if (_currentHealth <= 0)
		{
			Die();
		}
	}

	public void Heal(int amount)
	{
		if (_isDead) return;
		
		_currentHealth = Mathf.Min(MaxHealth, _currentHealth + amount);
		EmitSignal(SignalName.HealthChanged, _currentHealth);
	}

	private void Die()
	{
		if (_isDead) return;
		
		_isDead = true;
		EmitSignal(SignalName.PlayerDied);
		GD.Print($"{PlayerName} has died!");
		
		// Disable input and physics
		SetPhysicsProcess(false);
		SetProcess(false);
	}

	public void Respawn()
	{
		_isDead = false;
		_currentHealth = MaxHealth;
		
		EmitSignal(SignalName.HealthChanged, _currentHealth);
		
		// Reset weapon system
		if (_weaponSystem?.CurrentWeapon != null)
		{
			EmitSignal(SignalName.AmmoChanged, 
					  _weaponSystem.CurrentWeapon.CurrentAmmo, 
					  _weaponSystem.CurrentWeapon.MaxAmmo);
		}
		
		SetPhysicsProcess(true);
		SetProcess(true);
		
		GD.Print($"{PlayerName} respawned!");
	}

	// Legacy method for backward compatibility
	private void PlaySound(AudioStream sound, float pitch = 1.0f)
	{
		if (sound == null) return;
		
		// Use audio pool for better performance
		AudioPool.Instance?.PlayOneShot3D(sound, _gunPoint.GlobalPosition, pitch, 0f);
	}
}
