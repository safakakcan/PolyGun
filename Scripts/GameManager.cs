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
	private MultiplayerUI _multiplayerUI;
	private PlayerSpawner _playerSpawner;
	private List<PlayerController> _allPlayers = new List<PlayerController>();
	
	// Singleton pattern
	public static GameManager Instance { get; private set; }
	
	public GameState CurrentState => _currentState;
	public int Score => _score;
	public int Kills => _kills;
	public float GameTime => _gameTime;
	public PlayerController Player => _player;
	
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
		// Find UI Manager
		_uiManager = GetTree().CurrentScene.GetNode<UIManager>("UIManager");
		if (_uiManager != null)
		{
			_uiManager.GamePaused += OnGamePaused;
			_uiManager.GameResumed += OnGameResumed;
		}
		
		// Find Multiplayer UI
		_multiplayerUI = GetTree().CurrentScene.GetNode<MultiplayerUI>("MultiplayerUI");
		if (_multiplayerUI != null)
		{
			_multiplayerUI.StartGameRequested += OnStartGameRequested;
		}
		
		// Find Player Spawner
		_playerSpawner = GetTree().CurrentScene.GetNode<PlayerSpawner>("PlayerSpawner");
		
		LoadGameSettings();
		
		// Start in multiplayer menu instead of directly playing
		ShowMultiplayerMenu();
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
		// Let UIManager handle ESC key for pause/resume logic
		// GameManager will respond to UIManager signals instead
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
	
	// Add new methods for multiplayer integration
	public void ShowMultiplayerMenu()
	{
		ChangeGameState(GameState.MainMenu);
		_multiplayerUI.Visible = true;
		_uiManager.Visible = false;
	}
	
	public void StartMultiplayerGame()
	{
		ChangeGameState(GameState.Playing);
		_multiplayerUI.Visible = false;
		_uiManager.Visible = true;
		
		// Get local player reference
		if (_playerSpawner != null)
		{
			_player = _playerSpawner.GetLocalPlayer();
			if (_player != null)
			{
				_player.PlayerDied += OnPlayerDied;
			}
		}
	}
	
	private void OnStartGameRequested()
	{
		// Server starts the game for all players
		if (NetworkManager.Instance?.IsServer == true)
		{
			StartMultiplayerGame();
			
			// Notify all clients to start the game
			Rpc(MethodName.StartGame);
		}
	}
	
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void StartGame()
	{
		StartMultiplayerGame();
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

[System.Serializable]
public class GameSaveData
{
	public int Version = 1;
	public DateTime SaveTime;
	public PlayerSaveData Player;
	public GameStatsSaveData GameStats;
	public SettingsSaveData Settings;
	public List<WeaponSaveData> Weapons = new List<WeaponSaveData>();
}

[System.Serializable]
public class PlayerSaveData
{
	public float MouseSensitivity = 0.1f;
	public int CurrentHealth = 100;
	public Vector3 Position;
	public Vector3 Rotation;
	public int CurrentWeaponIndex = 0;
}

[System.Serializable]
public class GameStatsSaveData
{
	public int HighScore = 0;
	public int TotalKills = 0;
	public float TotalPlaytime = 0;
	public int GamesPlayed = 0;
	public float AverageAccuracy = 0;
	public Dictionary<string, int> WeaponKills = new Dictionary<string, int>();
}

[System.Serializable]
public class SettingsSaveData
{
	public float MasterVolume = 1.0f;
	public float SFXVolume = 1.0f;
	public float MusicVolume = 1.0f;
	public int GraphicsQuality = 2; // 0=Low, 1=Medium, 2=High
	public bool FullScreen = false;
	public Vector2I Resolution = new Vector2I(1920, 1080);
}

[System.Serializable]
public class WeaponSaveData
{
	public string WeaponType;
	public int Experience = 0;
	public int Level = 1;
	public List<string> UnlockedAttachments = new List<string>();
	public List<string> EquippedAttachments = new List<string>();
}

public partial class AdvancedSaveSystem : Node
{
	public static AdvancedSaveSystem Instance { get; private set; }
	
	[Export] public bool AutoSaveEnabled = true;
	[Export] public float AutoSaveInterval = 300f; // 5 minutes
	[Export] public int MaxSaveSlots = 5;
	
	private const string SAVE_DIRECTORY = "user://saves/";
	private const string SAVE_EXTENSION = ".json";
	private Timer _autoSaveTimer;
	
	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			InitializeAutoSave();
			EnsureSaveDirectory();
		}
		else
		{
			QueueFree();
		}
	}
	
	private void InitializeAutoSave()
	{
		if (!AutoSaveEnabled) return;
		
		_autoSaveTimer = new Timer();
		_autoSaveTimer.WaitTime = AutoSaveInterval;
		_autoSaveTimer.Timeout += OnAutoSave;
		_autoSaveTimer.Autostart = true;
		AddChild(_autoSaveTimer);
	}
	
	private void EnsureSaveDirectory()
	{
		if (!DirAccess.DirExistsAbsolute(SAVE_DIRECTORY))
		{
			DirAccess.MakeDirRecursiveAbsolute(SAVE_DIRECTORY);
		}
	}
	
	public Error SaveGame(int slot = 0)
	{
		var saveData = CollectSaveData();
		var saveDict = SaveDataToDict(saveData);
		var json = Json.Stringify(saveDict);
		var filePath = $"{SAVE_DIRECTORY}save_{slot}{SAVE_EXTENSION}";
		
		var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PrintErr($"Failed to open save file: {filePath}");
			return FileAccess.GetOpenError();
		}
		
		file.StoreString(json);
		file.Close();
		
		GD.Print($"Game saved to slot {slot}");
		return Error.Ok;
	}
	
	private Godot.Collections.Dictionary SaveDataToDict(GameSaveData saveData)
	{
		var dict = new Godot.Collections.Dictionary();
		dict["version"] = saveData.Version;
		dict["save_time"] = saveData.SaveTime.ToBinary();
		
		if (saveData.Player != null)
		{
			var playerDict = new Godot.Collections.Dictionary();
			playerDict["mouse_sensitivity"] = saveData.Player.MouseSensitivity;
			playerDict["current_health"] = saveData.Player.CurrentHealth;
			playerDict["position"] = saveData.Player.Position;
			playerDict["rotation"] = saveData.Player.Rotation;
			playerDict["current_weapon_index"] = saveData.Player.CurrentWeaponIndex;
			dict["player"] = playerDict;
		}
		
		if (saveData.GameStats != null)
		{
			var statsDict = new Godot.Collections.Dictionary();
			statsDict["high_score"] = saveData.GameStats.HighScore;
			statsDict["total_kills"] = saveData.GameStats.TotalKills;
			statsDict["total_playtime"] = saveData.GameStats.TotalPlaytime;
			dict["game_stats"] = statsDict;
		}
		
		if (saveData.Settings != null)
		{
			var settingsDict = new Godot.Collections.Dictionary();
			settingsDict["master_volume"] = saveData.Settings.MasterVolume;
			settingsDict["sfx_volume"] = saveData.Settings.SFXVolume;
			settingsDict["music_volume"] = saveData.Settings.MusicVolume;
			dict["settings"] = settingsDict;
		}
		
		return dict;
	}
	
	public Error LoadGame(int slot = 0)
	{
		var filePath = $"{SAVE_DIRECTORY}save_{slot}{SAVE_EXTENSION}";
		
		if (!FileAccess.FileExists(filePath))
		{
			GD.Print($"Save file not found: {filePath}");
			return Error.FileNotFound;
		}
		
		var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"Failed to open save file: {filePath}");
			return FileAccess.GetOpenError();
		}
		
		var jsonString = file.GetAsText();
		file.Close();
		
		var json = new Json();
		var parseResult = json.Parse(jsonString);
		
		if (parseResult != Error.Ok)
		{
			GD.PrintErr($"Failed to parse save file: {parseResult}");
			return parseResult;
		}
		
		var saveData = json.Data.AsGodotDictionary();
		ApplySaveData(saveData);
		
		GD.Print($"Game loaded from slot {slot}");
		return Error.Ok;
	}
	
	private GameSaveData CollectSaveData()
	{
		var saveData = new GameSaveData();
		saveData.SaveTime = DateTime.Now;
		
		// Collect player data
		var player = GameManager.Instance?.Player;
		if (player != null)
		{
			saveData.Player = new PlayerSaveData
			{
				MouseSensitivity = player.MouseSensitivity,
				CurrentHealth = player.CurrentHealth,
				Position = player.GlobalPosition,
				Rotation = player.GlobalRotationDegrees,
				CurrentWeaponIndex = player.WeaponSystem?.CurrentWeaponIndex ?? 0
			};
		}
		
		// Collect game stats
		var gameManager = GameManager.Instance;
		if (gameManager != null)
		{
			saveData.GameStats = new GameStatsSaveData
			{
				HighScore = gameManager.Score,
				TotalKills = gameManager.Kills,
				TotalPlaytime = gameManager.GameTime
			};
		}
		
		// Collect settings
		saveData.Settings = new SettingsSaveData
		{
			MasterVolume = AudioServer.GetBusVolumeDb(0),
			// Add other settings as needed
		};
		
		return saveData;
	}
	
	private void ApplySaveData(Godot.Collections.Dictionary saveData)
	{
		// Apply the loaded save data to game systems
		// This would restore player state, settings, etc.
		GD.Print("Applying save data...");
	}
	
	private void OnAutoSave()
	{
		SaveGame(0); // Auto-save to slot 0
	}
	
	public List<DateTime> GetSaveSlotTimes()
	{
		var times = new List<DateTime>();
		
		for (int i = 0; i < MaxSaveSlots; i++)
		{
			var filePath = $"{SAVE_DIRECTORY}save_{i}{SAVE_EXTENSION}";
			if (FileAccess.FileExists(filePath))
			{
				var file = FileAccess.Open(filePath, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					var content = file.GetAsText();
					file.Close();
					
					// Extract save time from JSON
					times.Add(DateTime.Now); // Placeholder
				}
			}
			else
			{
				times.Add(DateTime.MinValue);
			}
		}
		
		return times;
	}
} 