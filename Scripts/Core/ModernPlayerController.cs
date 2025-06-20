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
	[Export] public float JumpForce = 6.0f;
	[Export] public float Gravity = -20.0f;
	[Export] public float AirAcceleration = 2.0f;
	[Export] public float GroundAcceleration = 10.0f;
	[Export] public float Friction = 6.0f;
	[Export] public float AirFriction = 0.2f;
	
	// Camera & Look
	[Export] public float MouseSensitivity = 0.5f;
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
	
	// State
	private Vector3 _viewAngles = Vector3.Zero;
	private float _health;
	private bool _isDead = false;
	private double _lastDamageTime = 0.0;
	private bool _isLocalPlayer = false;
	
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
		InitializeComponents();
		InitializePlayer();
		SetupNetwork();
	}
	
	private void InitializeComponents()
	{
		// Create camera holder for smooth rotation
		_cameraHolder = new Node3D();
		_cameraHolder.Name = "CameraHolder";
		AddChild(_cameraHolder);
		
		// Create camera
		_camera = new Camera3D();
		_camera.Name = "Camera";
		_cameraHolder.AddChild(_camera);
		
		// Create visual representation
		SetupVisualMesh();
		
		// Setup collision
		SetupCollision();
	}
	
	private void SetupVisualMesh()
	{
		_visualMesh = new MeshInstance3D();
		_visualMesh.Name = "VisualMesh";
		
		// Create a simple capsule mesh for the player
		var capsule = new CapsuleMesh();
		capsule.Height = 1.8f;
		_visualMesh.Mesh = capsule;
		
		// Position mesh properly
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
		
		_collisionShape.Position = new Vector3(0, 0.9f, 0);
		AddChild(_collisionShape);
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
		// Determine if this is the local player
		if (Multiplayer.HasMultiplayerPeer())
		{
			var localId = Multiplayer.GetUniqueId();
			_isLocalPlayer = (NetworkId == localId) || (NetworkId == 1 && localId == 1);
		}
		else
		{
			_isLocalPlayer = true; // Single player
		}
		
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
			// Hide visual mesh for remote players initially
			_visualMesh.Visible = true;
		}
		
		GD.Print($"Player {PlayerName} ({NetworkId}) initialized - Local: {_isLocalPlayer}");
	}
	
	public override void _Input(InputEvent @event)
	{
		if (!_isLocalPlayer || _isDead) return;
		
		// Handle mouse look
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			HandleMouseLook(mouseMotion);
		}
		
		// Handle ESC to release mouse
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
				Input.MouseMode = Input.MouseModeEnum.Visible;
			else
				Input.MouseMode = Input.MouseModeEnum.Captured;
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
	
	public override void _PhysicsProcess(double delta)
	{
		if (_isDead) return;
		
		// Update health regeneration
		UpdateHealthRegeneration(delta);
		
		// Handle interpolation for remote players
		if (!_isLocalPlayer && _isInterpolating)
		{
			InterpolatePosition(delta);
		}
		
		// Local players handle movement through prediction system
		// Remote players get their position from server updates
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
		_visualMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Red };
		
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
		_visualMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White };
		
		GD.Print($"Player {PlayerName} respawned!");
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
} 