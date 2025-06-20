using Godot;
using System;

public partial class Bullet : RigidBody3D
{
	[Export] public byte Damage = 15;
	[Export] public double Lifespan = 2;

	public override void _Ready()
	{
		ContactMonitor = true;
		MaxContactsReported = 1;
	}

	public override void _Process(double delta)
	{
		Lifespan -= delta;
		if (Lifespan <= 0) QueueFree();
	}

	public override void _PhysicsProcess(double delta)
	{
		var collidingBodies = GetCollidingBodies();
		foreach (var body in collidingBodies)
		{
			if (body is PlayerController player)
			{
				player.TakeDamage(Damage);
				break;
			}
		}
		if (collidingBodies.Count > 0) QueueFree();
	}
}
