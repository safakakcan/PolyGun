using Godot;
using System;

public partial class Bullet : RigidBody3D
{
	[Export] public int Damage = 15;
	[Export] public double Lifespan = 3.0;
	[Export] public float Speed = 100f;
	[Export] public PackedScene ImpactEffectScene;
	[Export] public AudioStream ImpactSound;
	
	protected bool _hasHit = false;
	protected Vector3 _direction;
	protected PlayerController _shooter;

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 10;
		BodyEntered += OnBodyEntered;
		
		// Set up physics properties
		GravityScale = 0.1f; // Slight gravity for realism
		LinearDamp = 0.1f;   // Air resistance
	}

	public void Initialize(Vector3 direction, PlayerController shooter = null)
	{
		_direction = direction.Normalized();
		_shooter = shooter;
		
		// Apply initial velocity
		ApplyImpulse(_direction * Speed);
		
		// Align bullet with direction
		LookAt(GlobalPosition + _direction, Vector3.Up);
	}

	public override void _Process(double delta)
	{
		Lifespan -= delta;
		
		if (Lifespan <= 0)
		{
			DestroyBullet();
		}
	}

	protected virtual void OnBodyEntered(Node body)
	{
		if (_hasHit) return;
		
		_hasHit = true;
		
		// Handle different collision types
		if (body is PlayerController player && player != _shooter)
		{
			HandlePlayerHit(player);
		}
		else if (body is StaticBody3D || body is RigidBody3D)
		{
			HandleEnvironmentHit(body);
		}
		
		CreateImpactEffect();
		DestroyBullet();
	}

	private void HandlePlayerHit(PlayerController player)
	{
		player.TakeDamage(Damage);
		
		// Add slight knockback
		Vector3 knockback = _direction * 2f;
		if (player.IsOnFloor())
		{
			knockback.Y = Mathf.Max(knockback.Y, 1f); // Minimum upward force
		}
		
		player.Velocity += knockback;
		
		GD.Print($"Bullet hit {player.PlayerName} for {Damage} damage!");
	}

	private void HandleEnvironmentHit(Node body)
	{
		// Create ricochet effect for hard surfaces
		if (body is StaticBody3D)
		{
			// Play ricochet sound or effects here
			GD.Print("Bullet hit environment!");
		}
	}

	private void CreateImpactEffect()
	{
		if (ImpactEffectScene != null)
		{
			var effect = ImpactEffectScene.Instantiate();
			GetTree().Root.AddChild(effect);
			
			if (effect is Node3D effect3D)
			{
				effect3D.GlobalPosition = GlobalPosition;
				effect3D.LookAt(GlobalPosition - _direction, Vector3.Up);
			}
		}
		
		PlayImpactSound();
	}

	private void PlayImpactSound()
	{
		if (ImpactSound == null) return;
		
		// Use audio pool for better performance
		AudioPool.Instance?.PlayOneShot3D(ImpactSound, GlobalPosition, GD.Randf() * 0.2f + 0.9f, -10f);
	}

	protected virtual void DestroyBullet()
	{
		// Remove from bullet pool or tracking system if implemented
		QueueFree();
	}

	public override void _ExitTree()
	{
		// Clean up any remaining connections
		BodyEntered -= OnBodyEntered;
	}
}
