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

	// Add these properties after the existing exports
	[Export] public int NetworkId = 0;
	
	// Add network sync variables
	private Vector3 _networkPosition;
	private Vector3 _networkRotation;
	private Vector3 _networkVelocity;
	private bool _networkShooting;
	private int _networkWeaponIndex;
	private int _networkHealth;

	// Network authority check
	public bool IsNetworkOwner => Multiplayer.GetUniqueId() == NetworkId || (NetworkId == 0 && IsLocal);

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
		
		// Network setup
		if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnline)
		{
			SetupNetworkPlayer();
		}
		else
		{
			// Single player setup
			IsLocal = true;
			NetworkId = 1;
		}
		
		if (IsLocal && IsNetworkOwner) 
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
		else
		{
			_camera.Current = false;
		}
	}

	private void SetupNetworkPlayer()
	{
		// Set network ID based on multiplayer peer ID
		var multiplayerId = Multiplayer.GetUniqueId();
		
		if (NetworkManager.Instance.IsServer && NetworkId == 0)
		{
			// Server assigns network IDs
			NetworkId = multiplayerId;
		}
		
		// Only the owner of this player can control it
		IsLocal = IsNetworkOwner;
		
		// Set multiplayer authority
		SetMultiplayerAuthority(NetworkId);
		
		GD.Print($"Player setup: NetworkId={NetworkId}, IsLocal={IsLocal}, MultiplayerId={multiplayerId}");
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

		if (IsLocal && IsNetworkOwner)
		{
			HandleInput(delta);
			SyncNetworkState();
		}
		else if (NetworkManager.Instance?.IsOnline == true)
		{
			InterpolateNetworkState();
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
		if (!IsNetworkOwner) return;
		
		if (_weaponSystem == null) return;

		Vector3 fireDirection = CalculateBulletDirection();
		bool fired = _weaponSystem.TryFire(_gunPoint.GlobalPosition, fireDirection);
		
		if (fired)
		{
			// Sync shooting across network
			Rpc(MethodName.OnPlayerShoot, _gunPoint.GlobalPosition, fireDirection);
		}
		else if (_weaponSystem.CurrentWeapon?.CurrentAmmo <= 0)
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

	public void TakeDamage(int amount, int attackerId = 0)
	{
		if (_isDead) return;
		
		// Only server processes damage
		if (NetworkManager.Instance?.IsServer == true || !NetworkManager.Instance?.IsOnline == true)
		{
			_currentHealth = Mathf.Max(0, _currentHealth - amount);
			_lastDamageTime = Time.GetUnixTimeFromSystem();
			
			// Sync damage across network
			if (NetworkManager.Instance?.IsOnline == true)
			{
				Rpc(MethodName.OnPlayerDamaged, _currentHealth, amount, attackerId);
			}
			else
			{
				EmitSignal(SignalName.HealthChanged, _currentHealth);
			}
			
			GD.Print($"{PlayerName} took {amount} damage! Health: {_currentHealth}");
			
			if (_currentHealth <= 0)
			{
				Die();
			}
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
		
		// Sync death across network
		if (NetworkManager.Instance?.IsOnline == true && IsNetworkOwner)
		{
			Rpc(MethodName.OnPlayerDied, NetworkId);
		}
		
		// Disable input and physics
		SetPhysicsProcess(false);
		if (IsNetworkOwner)
		{
			SetProcess(false);
		}
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

	private void SyncNetworkState()
	{
		// Send player state to other clients
		_networkPosition = GlobalPosition;
		_networkRotation = GlobalRotationDegrees;
		_networkVelocity = Velocity;
		_networkHealth = _currentHealth;
		_networkWeaponIndex = _weaponSystem?.CurrentWeaponIndex ?? 0;
		
		// Use unreliable RPC for frequent updates
		Rpc(MethodName.UpdatePlayerState, _networkPosition, _networkRotation, _networkVelocity, _networkHealth, _networkWeaponIndex);
	}

	private void InterpolateNetworkState()
	{
		// Smoothly interpolate to network state for non-local players
		var lerpSpeed = 10.0f;
		GlobalPosition = GlobalPosition.Lerp(_networkPosition, (float)(lerpSpeed * GetProcessDeltaTime()));
		GlobalRotationDegrees = GlobalRotationDegrees.Lerp(_networkRotation, (float)(lerpSpeed * GetProcessDeltaTime()));
		
		// Update health if changed
		if (_currentHealth != _networkHealth)
		{
			_currentHealth = _networkHealth;
			EmitSignal(SignalName.HealthChanged, _currentHealth);
		}
		
		// Update weapon if changed
		if (_weaponSystem?.CurrentWeaponIndex != _networkWeaponIndex)
		{
			_weaponSystem?.SwitchToWeapon(_networkWeaponIndex);
		}
	}

	// Add network RPC methods:
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void UpdatePlayerState(Vector3 position, Vector3 rotation, Vector3 velocity, int health, int weaponIndex)
	{
		if (IsNetworkOwner) return; // Don't update own state
		
		_networkPosition = position;
		_networkRotation = rotation;
		_networkVelocity = velocity;
		_networkHealth = health;
		_networkWeaponIndex = weaponIndex;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerShoot(Vector3 firePosition, Vector3 fireDirection)
	{
		// Create visual/audio effects for shooting
		if (_weaponSystem?.CurrentWeapon != null)
		{
			// Play fire sound
			if (_weaponSystem.CurrentWeapon.FireSound != null)
			{
				AudioPool.Instance?.PlayOneShot3D(_weaponSystem.CurrentWeapon.FireSound, firePosition, 1.0f, 0f);
			}
			
			// Create muzzle flash or other effects here
		}
		
		// Only server spawns actual bullets
		if (NetworkManager.Instance?.IsServer == true || !NetworkManager.Instance?.IsOnline == true)
		{
			var bullet = BulletScene?.Instantiate() as Bullet;
			if (bullet != null)
			{
				bullet.GlobalPosition = firePosition;
				bullet.Initialize(fireDirection, this);
				GetTree().Root.AddChild(bullet);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerDamaged(int newHealth, int damageAmount, int attackerId)
	{
		_currentHealth = newHealth;
		EmitSignal(SignalName.HealthChanged, _currentHealth);
		
		// Show damage effects
		GD.Print($"{PlayerName} took {damageAmount} damage from player {attackerId}! Health: {_currentHealth}");
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerDied(int killerId)
	{
		if (!_isDead)
		{
			Die();
			GD.Print($"{PlayerName} was killed by player {killerId}");
		}
	}

	// Add method to set player info from network
	public void SetPlayerInfo(PlayerInfo playerInfo)
	{
		NetworkId = playerInfo.PlayerId;
		PlayerName = playerInfo.PlayerName;
		_currentHealth = playerInfo.Health;
		GlobalPosition = playerInfo.Position;
		GlobalRotationDegrees = playerInfo.Rotation;
		
		if (_weaponSystem != null && playerInfo.CurrentWeaponIndex >= 0)
		{
			_weaponSystem.SwitchToWeapon(playerInfo.CurrentWeaponIndex);
		}
		
		SetupNetworkPlayer();
	}

	// Add method to get current player info for network sync
	public PlayerInfo GetPlayerInfo()
	{
		return new PlayerInfo
		{
			PlayerId = NetworkId,
			PlayerName = PlayerName,
			IsReady = !_isDead,
			Position = GlobalPosition,
			Rotation = GlobalRotationDegrees,
			Health = _currentHealth,
			CurrentWeaponIndex = _weaponSystem?.CurrentWeaponIndex ?? 0
		};
	}
}
