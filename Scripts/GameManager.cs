using Godot;
using System;
using System.Collections.Generic;

public enum GameState
{
	MainMenu,
	Playing,
	Paused,
	GameOver,
	Loading
}

public partial class GameManager : Node
{
	[Signal] public delegate void GameStateChangedEventHandler(GameState newState);
	[Signal] public delegate void ScoreChangedEventHandler(int newScore);
	
	// Game State
	private GameState _currentState = GameState.Playing;
	private int _score = 0;
	private int _kills = 0;
	private float _gameTime = 0f;
	
	// Settings
	[Export] public float RespawnDelay = 3.0f;
	[Export] public bool EnableAutoSave = true;
	[Export] public string SaveFileName = "polygun_save.dat";
	
	// References
	private PlayerController _player;
	private UIManager _uiManager;
	private List<PlayerController> _allPlayers = new List<PlayerController>();
	
	// Singleton pattern
	public static GameManager Instance { get; private set; }
	
	public GameState CurrentState => _currentState;
	public int Score => _score;
	public int Kills => _kills;
	public float GameTime => _gameTime;
	
	public override void _Ready()
	{
		// Singleton setup
		if (Instance == null)
		{
			Instance = this;
		}
		else
		{
			QueueFree();
			return;
		}
		
		// Don't pause this node
		ProcessMode = ProcessModeEnum.Always;
		
		InitializeGame();
	}
	
	private void InitializeGame()
	{
		// Find and setup player
		_player = GetTree().CurrentScene.GetNode<PlayerController>("Player");
		if (_player != null)
		{
			_player.PlayerDied += OnPlayerDied;
			_allPlayers.Add(_player);
		}
		
		// Find UI Manager
		_uiManager = GetTree().CurrentScene.GetNode<UIManager>("UIManager");
		if (_uiManager != null)
		{
			_uiManager.GamePaused += OnGamePaused;
			_uiManager.GameResumed += OnGameResumed;
		}
		
		LoadGameSettings();
		ChangeGameState(GameState.Playing);
	}
	
	public override void _Process(double delta)
	{
		if (_currentState == GameState.Playing)
		{
			_gameTime += (float)delta;
		}
		
		HandleInput();
	}
	
	private void HandleInput()
	{
		if (Input.IsActionJustPressed("ui_cancel") && _currentState == GameState.Playing)
		{
			PauseGame();
		}
	}
	
	public void ChangeGameState(GameState newState)
	{
		if (_currentState == newState) return;
		
		var oldState = _currentState;
		_currentState = newState;
		
		HandleStateTransition(oldState, newState);
		EmitSignal(SignalName.GameStateChanged, (int)newState);
		
		GD.Print($"Game state changed from {oldState} to {newState}");
	}
	
	private void HandleStateTransition(GameState oldState, GameState newState)
	{
		switch (newState)
		{
			case GameState.Playing:
				GetTree().Paused = false;
				if (_player != null && Input.MouseMode != Input.MouseModeEnum.Captured)
				{
					Input.MouseMode = Input.MouseModeEnum.Captured;
				}
				break;
				
			case GameState.Paused:
				GetTree().Paused = true;
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
				
			case GameState.GameOver:
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Visible;
				SaveGameStats();
				break;
				
			case GameState.MainMenu:
				GetTree().Paused = false;
				Input.MouseMode = Input.MouseModeEnum.Visible;
				break;
		}
	}
	
	public void PauseGame()
	{
		if (_currentState == GameState.Playing)
		{
			ChangeGameState(GameState.Paused);
		}
	}
	
	public void ResumeGame()
	{
		if (_currentState == GameState.Paused)
		{
			ChangeGameState(GameState.Playing);
		}
	}
	
	public void StartNewGame()
	{
		ResetGameStats();
		
		// Respawn player
		if (_player != null)
		{
			_player.Respawn();
		}
		
		ChangeGameState(GameState.Playing);
	}
	
	public void EndGame()
	{
		ChangeGameState(GameState.GameOver);
	}
	
	public void AddScore(int points)
	{
		_score += points;
		EmitSignal(SignalName.ScoreChanged, _score);
	}
	
	public void AddKill()
	{
		_kills++;
		AddScore(100); // Award points for kills
	}
	
	private void ResetGameStats()
	{
		_score = 0;
		_kills = 0;
		_gameTime = 0f;
		
		EmitSignal(SignalName.ScoreChanged, _score);
	}
	
	private void OnPlayerDied()
	{
		GD.Print("Player died!");
		
		// Start respawn timer
		var timer = GetTree().CreateTimer(RespawnDelay);
		timer.Timeout += OnRespawnTimerTimeout;
	}
	
	private void OnRespawnTimerTimeout()
	{
		if (_currentState == GameState.GameOver)
		{
			// Player chose to respawn from game over screen
			StartNewGame();
		}
		else
		{
			// Auto-respawn
			if (_player != null)
			{
				_player.Respawn();
			}
		}
	}
	
	private void OnGamePaused()
	{
		ChangeGameState(GameState.Paused);
	}
	
	private void OnGameResumed()
	{
		ChangeGameState(GameState.Playing);
	}
	
	public void SaveGameSettings()
	{
		if (!EnableAutoSave) return;
		
		var config = new ConfigFile();
		
		// Save player settings
		config.SetValue("player", "mouse_sensitivity", Variant.From(_player?.MouseSensitivity ?? 0.1f));
		config.SetValue("game", "score", Variant.From(_score));
		config.SetValue("game", "high_score", Variant.From(GetHighScore()));
		config.SetValue("game", "total_kills", Variant.From(GetTotalKills()));
		config.SetValue("game", "total_playtime", Variant.From(GetTotalPlaytime()));
		
		var error = config.Save($"user://{SaveFileName}");
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to save game: {error}");
		}
	}
	
	public void LoadGameSettings()
	{
		var config = new ConfigFile();
		var error = config.Load($"user://{SaveFileName}");
		
		if (error != Error.Ok)
		{
			GD.Print("No save file found, using defaults");
			return;
		}
		
		// Apply loaded settings
		if (_player != null)
		{
			_player.MouseSensitivity = config.GetValue("player", "mouse_sensitivity", 0.1f).AsSingle();
		}
		
		_score = config.GetValue("game", "score", 0).AsInt32();
		_kills = config.GetValue("game", "total_kills", 0).AsInt32();
		_gameTime = config.GetValue("game", "total_playtime", 0.0f).AsSingle();
		
		EmitSignal(SignalName.ScoreChanged, _score);
		
		GD.Print("Game settings loaded");
	}
	
	private void SaveGameStats()
	{
		// Update persistent stats
		var currentHighScore = GetHighScore();
		if (_score > currentHighScore)
		{
			SetHighScore(_score);
		}
		
		AddToTotalKills(_kills);
		AddToTotalPlaytime(_gameTime);
		
		SaveGameSettings();
	}
	
	// Persistent stats methods (would typically use a proper save system)
	private int GetHighScore()
	{
		return (int)ProjectSettings.GetSetting("game/high_score", 0);
	}
	
	private void SetHighScore(int score)
	{
		ProjectSettings.SetSetting("game/high_score", score);
	}
	
	private int GetTotalKills()
	{
		return (int)ProjectSettings.GetSetting("game/total_kills", 0);
	}
	
	private void AddToTotalKills(int kills)
	{
		var total = GetTotalKills() + kills;
		ProjectSettings.SetSetting("game/total_kills", total);
	}
	
	private float GetTotalPlaytime()
	{
		return (float)ProjectSettings.GetSetting("game/total_playtime", 0.0f);
	}
	
	private void AddToTotalPlaytime(float time)
	{
		var total = GetTotalPlaytime() + time;
		ProjectSettings.SetSetting("game/total_playtime", total);
	}
	
	public override void _ExitTree()
	{
		SaveGameSettings();
		
		if (Instance == this)
		{
			Instance = null;
		}
	}
} 