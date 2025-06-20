using Godot;
using System;

public partial class PlayerController : CharacterBody3D
{
	// Network
	[Export] public bool IsLocal = true;
	[Export] public string Name = "";
	[Export] public byte Health = 100;
	[Export] public byte ActiveWeaponId = 0;

	// Local
	[Export] public byte Ammo = 24;
	[Export] public AudioStream FireSound;

	// Control
	[Export] public float MoveSpeed = 5f;
	[Export] public float MouseSensitivity = 0.1f;
	[Export] public float JumpForce = 6f;
	[Export] public float Gravity = -9.8f;
	private float _yaw;
	private float _pitch;
	private double _fireCooldown;
	private string _lastPressedDirectionKey;
	private double _lastPressedDirectionDelta;
	private double _dodgeCooldown;
	private Camera3D _camera;
	private Node3D _gunPoint;
	private AudioStreamPlayer3D _audioStreamPlayer3D;

	// Prefabs
	[Export] public PackedScene BulletScene;

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mm && IsLocal)
		{
			_yaw -= mm.Relative.X * MouseSensitivity;
			_pitch -= mm.Relative.Y * MouseSensitivity;
			_pitch = Mathf.Clamp(_pitch, -90f, 90f);

			RotationDegrees = new Vector3(0f, _yaw, 0f);
			_camera.RotationDegrees = new Vector3(_pitch, 0f, 0f);
		}
	}

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		_gunPoint = GetNode<Node3D>("GunPoint");
		if (IsLocal) Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Process(double delta)
	{
		_fireCooldown -= delta;
		_dodgeCooldown -= delta;
		_lastPressedDirectionDelta -= delta;

		if (IsLocal)
		{
			Vector3 direction = new Vector3(
				Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left"),
				0,
				Input.GetActionStrength("move_back") - Input.GetActionStrength("move_forward")
			).Normalized();
			
			Vector3 velocity = Transform.Basis * direction * MoveSpeed;

			#region Check Dodge
			if (Input.IsActionJustPressed("move_forward") && _lastPressedDirectionKey == "move_forward" && _dodgeCooldown <= 0 && _lastPressedDirectionDelta >= 0)
			{
				_dodgeCooldown = 1.5;
				velocity *= 100;
			}
			else if (Input.IsActionJustPressed("move_back") && _lastPressedDirectionKey == "move_back" && _dodgeCooldown <= 0 && _lastPressedDirectionDelta >= 0)
			{
				_dodgeCooldown = 1.5;
				velocity *= 100;
			}
			else if (Input.IsActionJustPressed("move_left") && _lastPressedDirectionKey == "move_left" && _dodgeCooldown <= 0 && _lastPressedDirectionDelta >= 0)
			{
				_dodgeCooldown = 1.5;
				velocity *= 100;
			}
			else if (Input.IsActionJustPressed("move_right") && _lastPressedDirectionKey == "move_right" && _dodgeCooldown <= 0 && _lastPressedDirectionDelta >= 0)
			{
				_dodgeCooldown = 1.5;
				velocity *= 100;
			}
			else
			{
				velocity *= Input.IsActionPressed("rush") ? 2 : 1;
			}

			if (Input.IsActionJustPressed("move_forward"))
			{
				_lastPressedDirectionKey = "move_forward";
				_lastPressedDirectionDelta = 0.25;
			}
			else if (Input.IsActionJustPressed("move_back"))
			{
				_lastPressedDirectionKey = "move_back";
				_lastPressedDirectionDelta = 0.25;
			}
			else if (Input.IsActionJustPressed("move_left"))
			{
				_lastPressedDirectionKey = "move_left";
				_lastPressedDirectionDelta = 0.25;
			}
			else if (Input.IsActionJustPressed("move_right"))
			{
				_lastPressedDirectionKey = "move_right";
				_lastPressedDirectionDelta = 0.25;
			}
  #endregion

			Velocity = new Vector3(Mathf.Lerp(Velocity.X, velocity.X, 0.25f), Velocity.Y, Mathf.Lerp(Velocity.Z, velocity.Z, 0.25f));

			if (Input.IsActionPressed("shoot") && _fireCooldown <= 0)
			{
				Shoot();
			}
		}

		MoveAndSlide();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsOnFloor())
		{
			Velocity = new Vector3(Velocity.X, Velocity.Y + Gravity * (float)delta, Velocity.Z);
		}
		else if (Input.IsActionJustPressed("jump") && IsLocal)
		{
			Velocity = new Vector3(Velocity.X, JumpForce, Velocity.Z);
		}
	}

	private void Shoot()
	{
		PlayOneShot3D(FireSound, _gunPoint.GlobalTransform.Origin, GD.Randf() * 0.1f + 0.95f);
		var bullet = (RigidBody3D)BulletScene.Instantiate();
		bullet.GlobalTransform = _gunPoint.GlobalTransform;
		var direction = (-_camera.GlobalTransform.Basis.Z + new Vector3((GD.Randf() - 0.5f) * 0.05f, (GD.Randf() - 0.5f) * 0.05f, (GD.Randf() - 0.5f) * 0.05f)).Normalized();
		bullet.ApplyImpulse(direction * 100);
		GetTree().Root.AddChild(bullet);
		_fireCooldown = 0.1;
	}
	
	public void TakeDamage(byte amount)
	{
		Health -= amount;
		GD.Print($"{Name} hasar aldı: {amount} | Kalan: {Health}");
		//if (Health <= 0) Die();
	}
	
	public void PlayOneShot3D(AudioStream stream, Vector3 position, float pitch = 1.0f)
	{
		_audioStreamPlayer3D = new AudioStreamPlayer3D();
		_audioStreamPlayer3D.PlaybackType = AudioServer.PlaybackType.Max;
		_audioStreamPlayer3D.Stream = stream;
		_audioStreamPlayer3D.GlobalTransform = new Transform3D(Basis.Identity, position);
		_audioStreamPlayer3D.PitchScale = pitch;
		_audioStreamPlayer3D.VolumeDb = 0;
		_audioStreamPlayer3D.PanningStrength = 0.5f;

		GetTree().Root.AddChild(_audioStreamPlayer3D);
		_audioStreamPlayer3D.Play();

		float duration = (float)stream.GetLength() / pitch;
		_audioStreamPlayer3D.CallDeferred("queue_free", duration);
	}
}
