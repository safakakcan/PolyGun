using Godot;
using System;

/// <summary>
/// Modern player controller designed for competitive FPS netcode
/// Separates visual representation from server simulation
/// Works with client-side prediction and server reconciliation
/// </summary>
public partial class ModernPlayerController : CharacterBody3D
{
	[Signal] public delegate void HealthChangedEventHandler(float health, float maxHealth);
	[Signal] public delegate void WeaponChangedEventHandler(int weaponIndex, string weaponName);
	[Signal] public delegate void PlayerDiedEventHandler();
	[Signal] public delegate void PlayerRespawnedEventHandler();

	// Player Identity
	[Export] public int NetworkId = 0;
	[Export] public string PlayerName = "Player";
	
	// Movement Configuration
	[Export] public float MoveSpeed = 5.0f;
	[Export] public float RunSpeedMultiplier = 1.6f;
	[Export] public float JumpForce = 8.0f;
	[Export] public float Gravity = 9.8f; // Positive value, will be applied downward
	[Export] public float AirAcceleration = 2.0f;
	[Export] public float GroundAcceleration = 10.0f;
	[Export] public float Friction = 6.0f;
	[Export] public float AirFriction = 0.2f;
	
	// Camera & Look
	[Export] public float MouseSensitivity = 0.1f;
	[Export] public float MaxLookAngle = 90.0f;
	
	// Health System
	[Export] public float MaxHealth = 100.0f;
	[Export] public float HealthRegenRate = 5.0f;
	[Export] public float HealthRegenDelay = 3.0f;
	
	// Components
	private Camera3D _camera;
	private Node3D _cameraHolder;
	private MeshInstance3D _visualMesh;
	private CollisionShape3D _collisionShape;
	private ModernWeaponSystem _weaponSystem;
	private Node3D _gunPoint;
	
	// State
	private Vector3 _viewAngles = Vector3.Zero;
	private float _health;
	private bool _isDead = false;
	private double _lastDamageTime = 0.0;
	private bool _isLocalPlayer = false;
	
	// Movement state
	private Vector3 _inputDirection = Vector3.Zero;
	private bool _isRunning = false;
	
	// Network interpolation
	private Vector3 _renderPosition;
	private Vector3 _renderRotation;
	private Vector3 _targetPosition;
	private Vector3 _targetRotation;
	private bool _isInterpolating = false;
	
	// Properties
	public bool IsLocalPlayer => _isLocalPlayer;
	public bool IsDead => _isDead;
	public float Health => _health;
	public Vector3 ViewAngles => _viewAngles;
	
	public override void _Ready()
	{
		// Player should pause when game is paused
		ProcessMode = ProcessModeEnum.Pausable;
		
		// Determine if this is the local player first
		if (Multiplayer.HasMultiplayerPeer())
		{
			var localId = Multiplayer.GetUniqueId();
			_isLocalPlayer = (NetworkId == localId) || (NetworkId == 1 && localId == 1);
		}
		else
		{
			_isLocalPlayer = true; // Single player
		}
		
		InitializeComponents();
		InitializePlayer();
		SetupNetwork();
	}
	
	private void InitializeComponents()
	{
		// Check if collision shape already exists (from scene)
		_collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
		if (_collisionShape == null)
		{
			// Create collision shape only if it doesn't exist
			SetupCollision();
		}
		else
		{
			GD.Print("Using existing collision shape from scene");
			// For scene-based collision shapes, don't modify the position
			// The scene should have the collision shape positioned correctly
			var shape = _collisionShape.Shape as CapsuleShape3D;
			if (shape != null)
			{
				GD.Print($"Scene collision shape - Height: {shape.Height}, Radius: {shape.Radius}, Position: {_collisionShape.Position}");
			}
		}
		
		// Don't force player position - let the scene determine initial position
		// GlobalPosition = new Vector3(0, 0, 0);
		
		// Create camera holder for smooth rotation
		_cameraHolder = new Node3D();
		_cameraHolder.Name = "CameraHolder";
		_cameraHolder.Position = new Vector3(0, 0.8f, 0); // Eye level height (lower than before)
		AddChild(_cameraHolder);
		
		// Create camera
		_camera = new Camera3D();
		_camera.Name = "Camera";
		_cameraHolder.AddChild(_camera);
		
		// Create gun point for weapon positioning
		_gunPoint = new Node3D();
		_gunPoint.Name = "GunPoint";
		_gunPoint.Position = new Vector3(0.3f, -0.2f, -0.5f); // Offset from camera
		_cameraHolder.AddChild(_gunPoint);
		
		// Create weapon system
		_weaponSystem = new ModernWeaponSystem();
		_weaponSystem.Name = "WeaponSystem";
		_gunPoint.AddChild(_weaponSystem);
		
		// Manually initialize the weapon system immediately
		// This ensures weapons are ready before any combat input
		_weaponSystem.Initialize(this);
		
		// Create visual representation only for remote players
		if (!_isLocalPlayer)
		{
			SetupVisualMesh();
		}
	}
	
	private void SetupVisualMesh()
	{
		_visualMesh = new MeshInstance3D();
		_visualMesh.Name = "VisualMesh";
		
		// Create a simple capsule mesh for the player
		var capsule = new CapsuleMesh();
		capsule.Height = 1.8f;
		capsule.RadialSegments = 8;
		_visualMesh.Mesh = capsule;
		
		// Position mesh to match collision shape
		_visualMesh.Position = new Vector3(0, 0.9f, 0);
		AddChild(_visualMesh);
	}
	
	private void SetupCollision()
	{
		_collisionShape = new CollisionShape3D();
		_collisionShape.Name = "CollisionShape";
		
		var capsuleShape = new CapsuleShape3D();
		capsuleShape.Height = 1.8f;
		capsuleShape.Radius = 0.3f;
		_collisionShape.Shape = capsuleShape;
		
		// Position collision shape properly - the bottom of the capsule should touch the ground
		// For a CharacterBody3D, position the shape so the bottom is at y=0
		_collisionShape.Position = new Vector3(0, 0.9f, 0); // Half the height up from ground
		AddChild(_collisionShape);
		
		// Set proper collision layers
		SetCollisionLayer(1); // Player layer
		SetCollisionMask(1);  // Collide with environment
		
		// Debug output
		GD.Print($"Collision shape set up: Height={capsuleShape.Height}, Radius={capsuleShape.Radius}, Position={_collisionShape.Position}");
	}
	
	private void InitializePlayer()
	{
		_health = MaxHealth;
		_isDead = false;
		_viewAngles = Vector3.Zero;
		_renderPosition = GlobalPosition;
		_renderRotation = Vector3.Zero;
	}
	
	private void SetupNetwork()
	{
		// Local player determination is now done in _Ready()
		
		// Setup camera and input for local player
		if (_isLocalPlayer)
		{
			_camera.Current = true;
			Input.MouseMode = Input.MouseModeEnum.Captured;
			
			// Initialize client prediction if in multiplayer
			if (Multiplayer.HasMultiplayerPeer())
			{
				ClientPrediction.Instance?.Initialize(this);
			}
		}
		else
		{
			_camera.Current = false;
			// Show visual mesh for remote players
			_visualMesh.Visible = true;
		}
		
		// Connect weapon system events
		if (_weaponSystem != null)
		{
			_weaponSystem.WeaponChanged += OnWeaponChanged;
			_weaponSystem.AmmoChanged += OnAmmoChanged;
			_weaponSystem.ReloadStarted += OnReloadStarted;
			_weaponSystem.ReloadCompleted += OnReloadCompleted;
			GD.Print("Weapon system events connected successfully");
		}
		else
		{
			GD.PrintErr("Failed to connect weapon system events - weapon system is null!");
		}
		
		GD.Print($"Player {PlayerName} ({NetworkId}) initialized - Local: {_isLocalPlayer}");
		GD.Print($"Weapon system: {(_weaponSystem != null ? "Ready" : "NULL")}");
		GD.Print($"Gun point: {(_gunPoint != null ? "Ready" : "NULL")}");
		GD.Print($"Camera: {(_camera != null ? "Ready" : "NULL")}");
	}
	
	public override void _Input(InputEvent @event)
	{
		if (!_isLocalPlayer || _isDead) return;
		
		// Handle mouse look
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			HandleMouseLook(mouseMotion);
		}
		
		// Handle weapon switching
		HandleWeaponSwitching(@event);
		
		// Don't handle ESC here - let UIManager handle it
		// This prevents conflicts with pause menu functionality
	}
	
	private void HandleWeaponSwitching(InputEvent @event)
	{
		if (_weaponSystem == null) 
		{
			GD.PrintErr("Weapon system null during weapon switching!");
			return;
		}
		
		// Mouse wheel weapon switching
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			if (mouseButton.ButtonIndex == MouseButton.WheelUp)
			{
				_weaponSystem.SwitchToPreviousWeapon();
				GD.Print("Switched to previous weapon");
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
			{
				_weaponSystem.SwitchToNextWeapon();
				GD.Print("Switched to next weapon");
			}
		}
		
		// Number key weapon switching
		for (int i = 1; i <= 5; i++)
		{
			if (@event.IsActionPressed($"weapon_{i}"))
			{
				_weaponSystem.SwitchToWeapon(i - 1);
				GD.Print($"Switched to weapon {i}");
				break;
			}
		}
		
		// Next/Previous weapon keys
		if (@event.IsActionPressed("weapon_next"))
		{
			_weaponSystem.SwitchToNextWeapon();
			GD.Print("Next weapon (Q key)");
		}
		else if (@event.IsActionPressed("weapon_prev"))
		{
			_weaponSystem.SwitchToPreviousWeapon();
			GD.Print("Previous weapon (E key)");
		}
	}
	
	private void HandleMouseLook(InputEventMouseMotion mouseMotion)
	{
		// Horizontal rotation (Y-axis)
		_viewAngles.Y -= mouseMotion.Relative.X * MouseSensitivity;
		
		// Vertical rotation (X-axis) with clamping
		_viewAngles.X -= mouseMotion.Relative.Y * MouseSensitivity;
		_viewAngles.X = Mathf.Clamp(_viewAngles.X, -MaxLookAngle, MaxLookAngle);
		
		// Apply rotation immediately for responsive camera
		UpdateCameraRotation();
	}
	
	private void UpdateCameraRotation()
	{
		// Apply body rotation (Y-axis only)
		RotationDegrees = new Vector3(0, _viewAngles.Y, 0);
		
		// Apply camera pitch
		_cameraHolder.RotationDegrees = new Vector3(_viewAngles.X, 0, 0);
	}
	
	public override void _Process(double delta)
	{
		if (_isDead) return;
		
		// Update health regeneration
		UpdateHealthRegeneration(delta);
		
		// Handle input for local player (combat needs to be in _Process for responsiveness)
		if (_isLocalPlayer)
		{
			HandleCombatInput();
		}
	}
	
	public override void _PhysicsProcess(double delta)
	{
		if (_isDead) return;
		
		// Handle movement for local player
		if (_isLocalPlayer)
		{
			HandleMovementInput();
			ApplyMovementPhysics(delta);
		}
		
		// Handle interpolation for remote players
		if (!_isLocalPlayer && _isInterpolating)
		{
			InterpolatePosition(delta);
		}
	}
	
	private void HandleMovementInput()
	{
		// Get input direction
		var inputDirection2D = Vector2.Zero;
		
		if (Input.IsActionPressed("move_forward"))
			inputDirection2D.Y -= 1.0f;
		if (Input.IsActionPressed("move_back"))
			inputDirection2D.Y += 1.0f;
		if (Input.IsActionPressed("move_left"))
			inputDirection2D.X -= 1.0f;
		if (Input.IsActionPressed("move_right"))
			inputDirection2D.X += 1.0f;
		
		// Normalize diagonal movement and convert to Vector3
		inputDirection2D = inputDirection2D.Normalized();
		_inputDirection = new Vector3(inputDirection2D.X, 0, inputDirection2D.Y);
		
		// Check if running
		_isRunning = Input.IsActionPressed("rush");
	}
	
	private void ApplyMovementPhysics(double delta)
	{
		// Debug physics state
		bool onFloor = IsOnFloor();
		if (GetPhysicsProcessDeltaTime() > 0) // Only log occasionally
		{
			var frameCount = Engine.GetProcessFrames();
			if (frameCount % 60 == 0) // Log every 60 frames (once per second at 60fps)
			{
				GD.Print($"Physics Debug - OnFloor: {onFloor}, Position: {GlobalPosition}, Velocity: {Velocity}");
			}
		}
		
		// Apply gravity
		if (!onFloor)
		{
			var newVelocityY = Velocity.Y - Gravity * (float)delta;
			Velocity = new Vector3(Velocity.X, newVelocityY, Velocity.Z);
		}
		else
		{
			// When on floor, reset Y velocity if it's negative
			if (Velocity.Y < 0)
			{
				Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
			}
		}
		
		// Handle jumping
		if (Input.IsActionJustPressed("jump") && onFloor)
		{
			Velocity = new Vector3(Velocity.X, JumpForce, Velocity.Z);
			GD.Print($"Jump! New velocity: {Velocity}");
		}
		
		// Calculate movement
		var transform = Transform;
		var basis = transform.Basis;
		
		// Get movement direction relative to player rotation
		var direction = (basis * _inputDirection).Normalized();
		
		// Calculate target speed
		var targetSpeed = MoveSpeed;
		if (_isRunning && IsOnFloor())
		{
			targetSpeed *= RunSpeedMultiplier;
		}
		
		// Apply movement
		if (IsOnFloor())
		{
			// Ground movement with acceleration and friction
			if (direction != Vector3.Zero)
			{
				// Accelerate towards target velocity
				var targetVelocity = direction * targetSpeed;
				var currentHorizontal = new Vector3(Velocity.X, 0, Velocity.Z);
				var newHorizontal = currentHorizontal.Lerp(targetVelocity, GroundAcceleration * (float)delta);
				Velocity = new Vector3(newHorizontal.X, Velocity.Y, newHorizontal.Z);
			}
			else
			{
				// Apply friction when not moving
				var currentHorizontal = new Vector3(Velocity.X, 0, Velocity.Z);
				var newHorizontal = currentHorizontal.Lerp(Vector3.Zero, Friction * (float)delta);
				Velocity = new Vector3(newHorizontal.X, Velocity.Y, newHorizontal.Z);
			}
		}
		else
		{
			// Air movement with reduced control
			if (direction != Vector3.Zero)
			{
				var targetVelocity = direction * targetSpeed;
				var currentHorizontal = new Vector3(Velocity.X, 0, Velocity.Z);
				var newHorizontal = currentHorizontal.Lerp(targetVelocity, AirAcceleration * (float)delta);
				Velocity = new Vector3(newHorizontal.X, Velocity.Y, newHorizontal.Z);
			}
		}
		
		// Apply movement
		MoveAndSlide();
	}
	
	private void HandleCombatInput()
	{
		if (_weaponSystem == null) 
		{
			GD.PrintErr("Weapon system is null!");
			return;
		}
		
		// Handle shooting - same as old system
		if (Input.IsActionPressed("shoot"))
		{
			// Check if weapon system is properly initialized
			if (_weaponSystem.CurrentWeapon == null)
			{
				GD.PrintErr($"CurrentWeapon is null! Weapon count: {_weaponSystem.WeaponCount}, Current index: {_weaponSystem.CurrentWeaponIndex}");
				return;
			}
			
			if (_weaponSystem.CanFire())
			{
				var fireDirection = GetFireDirection();
				var firePoint = _gunPoint?.GlobalPosition ?? _camera.GlobalPosition;
				bool fired = _weaponSystem.TryFire(firePoint, fireDirection);
				
				if (!fired)
				{
					GD.Print("Failed to fire weapon!");
				}
			}
			else
			{
				GD.Print($"Can't fire: weapon not ready or out of ammo. Weapon: {_weaponSystem.GetCurrentWeaponName()}, Ammo: {_weaponSystem.GetAmmoInfo().current}/{_weaponSystem.GetAmmoInfo().max}, Reloading: {_weaponSystem.IsReloading()}");
			}
		}
		
		// Handle reload - same as old system
		if (Input.IsActionJustPressed("reload"))
		{
			if (_weaponSystem.CurrentWeapon == null)
			{
				GD.PrintErr("Cannot reload: CurrentWeapon is null!");
				return;
			}
			
			if (_weaponSystem.CanReload())
			{
				_weaponSystem.StartReload();
				GD.Print("Starting reload...");
			}
			else
			{
				GD.Print("Can't reload: already reloading or ammo full");
			}
		}
	}
	
	private Vector3 GetFireDirection()
	{
		// Get direction from camera forward
		return -_camera.GlobalTransform.Basis.Z;
	}
	
	private void UpdateHealthRegeneration(double delta)
	{
		if (_health < MaxHealth && Time.GetUnixTimeFromSystem() - _lastDamageTime >= HealthRegenDelay)
		{
			_health = Math.Min(MaxHealth, _health + (float)(HealthRegenRate * delta));
			EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
		}
	}
	
	private void InterpolatePosition(double delta)
	{
		const float lerpSpeed = 12.0f;
		
		// Interpolate position
		_renderPosition = _renderPosition.Lerp(_targetPosition, (float)(lerpSpeed * delta));
		GlobalPosition = _renderPosition;
		
		// Interpolate rotation
		_renderRotation = _renderRotation.Lerp(_targetRotation, (float)(lerpSpeed * delta));
		RotationDegrees = new Vector3(0, _renderRotation.Y, 0);
		
		// Check if interpolation is complete
		var positionDiff = _renderPosition.DistanceTo(_targetPosition);
		var rotationDiff = Mathf.Abs(_renderRotation.Y - _targetRotation.Y);
		
		if (positionDiff < 0.01f && rotationDiff < 0.1f)
		{
			_isInterpolating = false;
			GlobalPosition = _targetPosition;
			RotationDegrees = new Vector3(0, _targetRotation.Y, 0);
		}
	}
	
	// Called by the server/networking system to update remote player state
	public void UpdateNetworkState(Vector3 position, Vector3 velocity, Vector3 viewAngles, float health, bool isDead)
	{
		if (_isLocalPlayer) return; // Local player handles their own state
		
		_targetPosition = position;
		_targetRotation = new Vector3(0, viewAngles.Y, 0);
		_viewAngles = viewAngles;
		_isInterpolating = true;
		
		// Update health
		if (_health != health)
		{
			_health = health;
			EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
		}
		
		// Update death state
		if (_isDead != isDead)
		{
			_isDead = isDead;
			if (isDead)
			{
				HandleDeath();
			}
			else
			{
				HandleRespawn();
			}
		}
		
		// Set velocity for visual effects
		Velocity = velocity;
	}
	
	public void TakeDamage(float damage, int attackerId = 0)
	{
		if (_isDead) return;
		
		_health = Math.Max(0, _health - damage);
		_lastDamageTime = Time.GetUnixTimeFromSystem();
		
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
		
		if (_health <= 0 && !_isDead)
		{
			HandleDeath();
		}
		
		GD.Print($"Player {PlayerName} took {damage} damage! Health: {_health}");
	}
	
	public void Heal(float amount)
	{
		if (_isDead) return;
		
		_health = Math.Min(MaxHealth, _health + amount);
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
	}
	
	private void HandleDeath()
	{
		_isDead = true;
		_health = 0;
		
		EmitSignal(SignalName.PlayerDied);
		
		// Disable collision and movement for dead players
		SetPhysicsProcess(false);
		_collisionShape.Disabled = true;
		
		// Visual effects for death
		if (_visualMesh.MaterialOverride == null)
		{
			_visualMesh.MaterialOverride = new StandardMaterial3D();
		}
		((StandardMaterial3D)_visualMesh.MaterialOverride).AlbedoColor = Colors.Red;
		
		GD.Print($"Player {PlayerName} died!");
	}
	
	private void HandleRespawn()
	{
		_isDead = false;
		_health = MaxHealth;
		
		EmitSignal(SignalName.PlayerRespawned);
		EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
		
		// Re-enable collision and movement
		SetPhysicsProcess(true);
		_collisionShape.Disabled = false;
		
		// Reset visual effects
		if (_visualMesh.MaterialOverride == null)
		{
			_visualMesh.MaterialOverride = new StandardMaterial3D();
		}
		((StandardMaterial3D)_visualMesh.MaterialOverride).AlbedoColor = Colors.White;
		
		GD.Print($"Player {PlayerName} respawned!");
	}
	
	public void Respawn()
	{
		HandleRespawn();
	}
	
	public new void SetPosition(Vector3 position)
	{
		GlobalPosition = position;
		_renderPosition = position;
		_targetPosition = position;
	}
	
	public void SetViewAngles(Vector3 angles)
	{
		_viewAngles = angles;
		UpdateCameraRotation();
	}
	
	// Getters for client prediction system
	public Vector3 GetViewAngles()
	{
		return _viewAngles;
	}
	
	public new bool IsOnFloor()
	{
		return base.IsOnFloor();
	}
	
	// Network state sharing for server
	public Godot.Collections.Dictionary GetNetworkState()
	{
		return new Godot.Collections.Dictionary
		{
			["player_id"] = NetworkId,
			["player_name"] = PlayerName,
			["position"] = new Godot.Collections.Array { GlobalPosition.X, GlobalPosition.Y, GlobalPosition.Z },
			["velocity"] = new Godot.Collections.Array { Velocity.X, Velocity.Y, Velocity.Z },
			["view_angles"] = new Godot.Collections.Array { _viewAngles.X, _viewAngles.Y, _viewAngles.Z },
			["health"] = _health,
			["is_dead"] = _isDead
		};
	}
	
	public void SetNetworkState(Godot.Collections.Dictionary state)
	{
		if (state.ContainsKey("position"))
		{
			var pos = state["position"].AsGodotArray();
			var position = new Vector3(pos[0].AsSingle(), pos[1].AsSingle(), pos[2].AsSingle());
			_targetPosition = position;
			_isInterpolating = true;
		}
		
		if (state.ContainsKey("velocity"))
		{
			var vel = state["velocity"].AsGodotArray();
			Velocity = new Vector3(vel[0].AsSingle(), vel[1].AsSingle(), vel[2].AsSingle());
		}
		
		if (state.ContainsKey("view_angles"))
		{
			var angles = state["view_angles"].AsGodotArray();
			_viewAngles = new Vector3(angles[0].AsSingle(), angles[1].AsSingle(), angles[2].AsSingle());
			
			if (!_isLocalPlayer)
			{
				_targetRotation = new Vector3(0, _viewAngles.Y, 0);
			}
		}
		
		if (state.ContainsKey("health"))
		{
			var health = state["health"].AsSingle();
			if (_health != health)
			{
				_health = health;
				EmitSignal(SignalName.HealthChanged, _health, MaxHealth);
			}
		}
		
		if (state.ContainsKey("is_dead"))
		{
			var isDead = state["is_dead"].AsBool();
			if (_isDead != isDead)
			{
				_isDead = isDead;
				if (isDead)
					HandleDeath();
				else
					HandleRespawn();
			}
		}
	}
	
	// Weapon event handlers
	private void OnWeaponChanged(string weaponName)
	{
		EmitSignal(SignalName.WeaponChanged, 0, weaponName); // Weapon index and name
	}
	
	private void OnAmmoChanged(int currentAmmo, int maxAmmo)
	{
		// This will be connected to UI later
		// For now, we'll let the UI connect directly to the weapon system
	}
	
	private void OnReloadStarted()
	{
		// Weapon reload started
	}
	
	private void OnReloadCompleted()
	{
		// Weapon reload completed
	}
} 