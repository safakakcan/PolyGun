using Godot;
using System;
using System.Collections.Generic;

public interface IPoolable
{
	void OnPoolSpawn();
	void OnPoolDespawn();
	bool IsPooled { get; set; }
}

public partial class ObjectPool<T> : Node where T : Node, IPoolable, new()
{
	private Queue<T> _pool = new Queue<T>();
	private HashSet<T> _activeObjects = new HashSet<T>();
	private PackedScene _objectScene;
	private int _maxPoolSize;
	private int _initialPoolSize;
	
	public int PoolSize => _pool.Count;
	public int ActiveCount => _activeObjects.Count;
	public int TotalCount => PoolSize + ActiveCount;
	
	public void Initialize(PackedScene objectScene, int initialSize = 10, int maxSize = 100)
	{
		_objectScene = objectScene;
		_initialPoolSize = initialSize;
		_maxPoolSize = maxSize;
		
		// Pre-populate pool
		for (int i = 0; i < _initialPoolSize; i++)
		{
			var obj = CreateNewObject();
			if (obj != null)
			{
				obj.OnPoolDespawn();
				obj.IsPooled = true;
				_pool.Enqueue(obj);
			}
		}
		
		GD.Print($"ObjectPool initialized with {_pool.Count} objects");
	}
	
	public T Get()
	{
		T obj;
		
		if (_pool.Count > 0)
		{
			obj = _pool.Dequeue();
		}
		else
		{
			obj = CreateNewObject();
			if (obj == null) return null;
		}
		
		obj.IsPooled = false;
		_activeObjects.Add(obj);
		obj.OnPoolSpawn();
		
		return obj;
	}
	
	public void Return(T obj)
	{
		if (obj == null || obj.IsPooled) return;
		
		_activeObjects.Remove(obj);
		obj.OnPoolDespawn();
		obj.IsPooled = true;
		
		if (_pool.Count < _maxPoolSize)
		{
			_pool.Enqueue(obj);
		}
		else
		{
			// Pool is full, destroy the object
			obj.QueueFree();
		}
	}
	
	public void ReturnAll()
	{
		var activeList = new List<T>(_activeObjects);
		foreach (var obj in activeList)
		{
			Return(obj);
		}
	}
	
	public void Clear()
	{
		ReturnAll();
		
		while (_pool.Count > 0)
		{
			var obj = _pool.Dequeue();
			obj.QueueFree();
		}
		
		_pool.Clear();
		_activeObjects.Clear();
	}
	
	private T CreateNewObject()
	{
		if (_objectScene == null) return null;
		
		var instance = _objectScene.Instantiate();
		if (instance is T typedInstance)
		{
			GetTree().Root.AddChild(typedInstance);
			return typedInstance;
		}
		
		instance.QueueFree();
		return null;
	}
}

public partial class PooledBullet : Bullet, IPoolable
{
	public bool IsPooled { get; set; }
	
	private Vector3 _initialPosition;
	private Vector3 _initialDirection;
	private PlayerController _initialShooter;

	public override void _Ready()
	{
		// Don't call base._Ready() when pooled
		if (!IsPooled)
		{
			base._Ready();
		}
	}
	
	public void OnPoolSpawn()
	{
		// Reset bullet state
		_hasHit = false;
		Lifespan = 3.0;
		
		// Re-enable physics and collision
		SetDeferred("freeze", false);
		SetDeferred("sleeping", false);
		
		// Reset physics properties
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		
		// Ensure collision detection is enabled
		ContactMonitor = true;
		MaxContactsReported = 10;
		
		// Make sure the bullet is visible
		Visible = true;
		
		// Re-connect signals if needed
		ConnectBodyEnteredSignal();
	}
	
	public void OnPoolDespawn()
	{
		// Hide and disable the bullet
		Visible = false;
		SetDeferred("freeze", true);
		
		// Disconnect signals to prevent issues
		DisconnectBodyEnteredSignal();
		
		// Reset position to avoid visual artifacts
		GlobalPosition = Vector3.Zero;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
	}
	
	private void ConnectBodyEnteredSignal()
	{
		BodyEntered += OnBodyEnteredPooled;
	}
	
	private void DisconnectBodyEnteredSignal()
	{
		BodyEntered -= OnBodyEnteredPooled;
	}
	
	private void OnBodyEnteredPooled(Node body)
	{
		if (_hasHit) return;
		
		// Call the protected base method
		OnBodyEntered(body);
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
		
		GD.Print($"Pooled bullet hit {player.PlayerName} for {Damage} damage!");
	}

	private void HandleEnvironmentHit(Node body)
	{
		// Create ricochet effect for hard surfaces
		if (body is StaticBody3D)
		{
			// Play ricochet sound or effects here
			GD.Print("Pooled bullet hit environment!");
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

	public void InitializePooled(Vector3 position, Vector3 direction, PlayerController shooter = null)
	{
		GlobalPosition = position;
		_direction = direction.Normalized();
		_shooter = shooter;
		
		// Apply initial velocity
		ApplyImpulse(_direction * Speed);
		
		// Align bullet with direction
		LookAt(GlobalPosition + _direction, Vector3.Up);
	}

	protected override void DestroyBullet()
	{
		// Return to pool instead of destroying
		if (BulletPool.Instance != null)
		{
			BulletPool.Instance.ReturnBullet(this);
		}
		else
		{
			base.DestroyBullet();
		}
	}
}

public partial class BulletPool : Node
{
	public static BulletPool Instance { get; private set; }
	
	[Export] public PackedScene BulletScene;
	[Export] public int InitialPoolSize = 50;
	[Export] public int MaxPoolSize = 200;
	
	private ObjectPool<PooledBullet> _bulletPool;
	
	public override void _Ready()
	{
		// Singleton pattern
		if (Instance == null)
		{
			Instance = this;
		}
		else
		{
			QueueFree();
			return;
		}
		
		SetupPool();
	}
	
	private void SetupPool()
	{
		_bulletPool = new ObjectPool<PooledBullet>();
		AddChild(_bulletPool);
		
		if (BulletScene != null)
		{
			_bulletPool.Initialize(BulletScene, InitialPoolSize, MaxPoolSize);
		}
		else
		{
			GD.PrintErr("BulletScene not assigned to BulletPool!");
		}
	}
	
	public PooledBullet GetBullet()
	{
		if (_bulletPool == null) return null;
		return _bulletPool.Get();
	}
	
	public void ReturnBullet(PooledBullet bullet)
	{
		if (_bulletPool == null || bullet == null) return;
		_bulletPool.Return(bullet);
	}
	
	public void ClearAllBullets()
	{
		_bulletPool?.ReturnAll();
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
	
	// Debug information
	public void PrintPoolStats()
	{
		if (_bulletPool != null)
		{
			GD.Print($"Bullet Pool Stats - Pool: {_bulletPool.PoolSize}, Active: {_bulletPool.ActiveCount}, Total: {_bulletPool.TotalCount}");
		}
	}
}

// Audio Pool for managing audio players
public partial class PooledAudioPlayer : AudioStreamPlayer3D, IPoolable
{
	public bool IsPooled { get; set; }
	private Timer _cleanupTimer;
	
	public override void _Ready()
	{
		if (!IsPooled)
		{
			_cleanupTimer = new Timer();
			AddChild(_cleanupTimer);
			_cleanupTimer.OneShot = true;
			_cleanupTimer.Timeout += OnCleanupTimer;
		}
	}
	
	public void OnPoolSpawn()
	{
		// Reset audio player state
		Stop();
		Stream = null;
		PitchScale = 1.0f;
		VolumeDb = 0.0f;
		
		// Make sure it's enabled
		Autoplay = false;
	}
	
	public void OnPoolDespawn()
	{
		Stop();
		Stream = null;
		
		if (_cleanupTimer != null)
		{
			_cleanupTimer.Stop();
		}
	}
	
	public void PlayOneShot(AudioStream audioStream, Vector3 position, float pitch = 1.0f, float volume = 0.0f)
	{
		Stream = audioStream;
		GlobalPosition = position;
		PitchScale = pitch;
		VolumeDb = volume;
		
		Play();
		
		// Set cleanup timer
		if (_cleanupTimer != null && audioStream != null)
		{
			float duration = (float)audioStream.GetLength() / pitch;
			_cleanupTimer.WaitTime = duration + 0.1f; // Small buffer
			_cleanupTimer.Start();
		}
	}
	
	private void OnCleanupTimer()
	{
		if (AudioPool.Instance != null)
		{
			AudioPool.Instance.ReturnAudioPlayer(this);
		}
		else
		{
			QueueFree();
		}
	}
}

public partial class AudioPool : Node
{
	public static AudioPool Instance { get; private set; }
	
	[Export] public int InitialPoolSize = 20;
	[Export] public int MaxPoolSize = 50;
	
	private ObjectPool<PooledAudioPlayer> _audioPool;
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
		}
		else
		{
			QueueFree();
			return;
		}
		
		SetupPool();
	}
	
	private void SetupPool()
	{
		_audioPool = new ObjectPool<PooledAudioPlayer>();
		AddChild(_audioPool);
		
		// Create a dummy scene for the audio players
		var audioScene = new PackedScene();
		var audioPlayer = new PooledAudioPlayer();
		audioScene.Pack(audioPlayer);
		
		_audioPool.Initialize(audioScene, InitialPoolSize, MaxPoolSize);
	}
	
	public void PlayOneShot3D(AudioStream audioStream, Vector3 position, float pitch = 1.0f, float volume = 0.0f)
	{
		if (_audioPool == null || audioStream == null) return;
		
		var audioPlayer = _audioPool.Get();
		if (audioPlayer != null)
		{
			audioPlayer.PlayOneShot(audioStream, position, pitch, volume);
		}
	}
	
	public void ReturnAudioPlayer(PooledAudioPlayer audioPlayer)
	{
		if (_audioPool == null || audioPlayer == null) return;
		_audioPool.Return(audioPlayer);
	}
	
	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 