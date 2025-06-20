using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public enum NetworkState
{
	Offline,
	Server,
	Client,
	Connecting
}

[System.Serializable]
public class ServerInfo
{
	public string ServerName;
	public string HostName;
	public int PlayerCount;
	public int MaxPlayers;
	public string MapName;
	public bool HasPassword;
	public string IPAddress;
	public int Port;
}

[System.Serializable]
public class PlayerInfo
{
	public int PlayerId;
	public string PlayerName;
	public bool IsReady;
	public Vector3 Position;
	public Vector3 Rotation;
	public int Health;
	public int CurrentWeaponIndex;
}

public partial class NetworkManager : Node
{
	[Signal] public delegate void ServerCreatedEventHandler();
	[Signal] public delegate void ServerJoinedEventHandler();
	[Signal] public delegate void ServerListUpdatedEventHandler();
	[Signal] public delegate void PlayerJoinedEventHandler(int playerId, string playerName);
	[Signal] public delegate void PlayerLeftEventHandler(int playerId);
	[Signal] public delegate void ConnectionFailedEventHandler(string reason);

	public static NetworkManager Instance { get; private set; }

	[Export] public int DefaultPort = 7000;
	[Export] public int MaxPlayers = 8;
	[Export] public string DefaultServerName = "PolyGun Server";
	[Export] public float ServerBroadcastInterval = 2.0f;
	[Export] public float ServerDiscoveryTimeout = 10.0f;

	private NetworkState _currentState = NetworkState.Offline;
	private MultiplayerApi _multiplayer;
	private PacketPeerUdp _udpBroadcaster; // For server to broadcast its presence
	private PacketPeerUdp _udpListener; // For client to listen for broadcasts
	private Timer _broadcastTimer;
	private Timer _discoveryTimer;
	
	private ServerInfo _currentServerInfo;
	private Dictionary<int, PlayerInfo> _connectedPlayers = new Dictionary<int, PlayerInfo>();
	private List<ServerInfo> _discoveredServers = new List<ServerInfo>();

	public NetworkState CurrentState => _currentState;
	public ServerInfo CurrentServerInfo => _currentServerInfo;
	public Dictionary<int, PlayerInfo> ConnectedPlayers => _connectedPlayers;
	public List<ServerInfo> DiscoveredServers => _discoveredServers;
	public bool IsServer => _currentState == NetworkState.Server;
	public bool IsClient => _currentState == NetworkState.Client;
	public bool IsOnline => _currentState != NetworkState.Offline;

	public override void _Ready()
	{
		if (Instance == null)
		{
			Instance = this;
			ProcessMode = ProcessModeEnum.Always;
			InitializeNetworking();
		}
		else
		{
			QueueFree();
		}
	}

	private void InitializeNetworking()
	{
		_multiplayer = GetTree().GetMultiplayer();
		
		// Connect multiplayer signals
		_multiplayer.PeerConnected += OnPeerConnected;
		_multiplayer.PeerDisconnected += OnPeerDisconnected;
		_multiplayer.ConnectedToServer += OnConnectedToServer;
		_multiplayer.ConnectionFailed += OnConnectionFailed;
		_multiplayer.ServerDisconnected += OnServerDisconnected;

		// Setup timers
		_broadcastTimer = new Timer();
		_broadcastTimer.WaitTime = ServerBroadcastInterval;
		_broadcastTimer.Timeout += BroadcastServer;
		AddChild(_broadcastTimer);

		_discoveryTimer = new Timer();
		_discoveryTimer.WaitTime = 0.5f; // Check for servers every 0.5 seconds
		_discoveryTimer.Timeout += DiscoverServers;
		AddChild(_discoveryTimer);

		GD.Print("NetworkManager initialized");
	}

	#region Server Management

	public Error CreateServer(string serverName = "", string password = "", int port = 0)
	{
		if (_currentState != NetworkState.Offline)
		{
			GD.PrintErr("Cannot create server: Already connected");
			return Error.AlreadyInUse;
		}

		port = port == 0 ? DefaultPort : port;
		serverName = string.IsNullOrEmpty(serverName) ? DefaultServerName : serverName;

		var peer = new ENetMultiplayerPeer();
		var result = peer.CreateServer(port, MaxPlayers);
		if (result != Error.Ok)
		{
			GD.PrintErr($"Failed to create server on port {port}: {result}");
			return result;
		}

		_multiplayer.MultiplayerPeer = peer;
		_currentState = NetworkState.Server;

		_currentServerInfo = new ServerInfo
		{
			ServerName = serverName,
			HostName = OS.GetEnvironment("USERNAME"),
			PlayerCount = 1,
			MaxPlayers = MaxPlayers,
			MapName = "Default",
			HasPassword = !string.IsNullOrEmpty(password),
			IPAddress = GetLocalIPAddress(),
			Port = port
		};

		// Add server host as first player
		var hostPlayer = new PlayerInfo
		{
			PlayerId = 1,
			PlayerName = _currentServerInfo.HostName,
			IsReady = true
		};
		_connectedPlayers[1] = hostPlayer;

		StartServerBroadcast();
		EmitSignal(SignalName.ServerCreated);
		GD.Print($"Server created: {serverName} on port {port}");

		return Error.Ok;
	}

	public Error JoinServer(string ipAddress, int port, string password = "")
	{
		if (_currentState != NetworkState.Offline)
		{
			GD.PrintErr("Cannot join server: Already connected");
			return Error.AlreadyInUse;
		}

		_currentState = NetworkState.Connecting;

		var peer = new ENetMultiplayerPeer();
		var result = peer.CreateClient(ipAddress, port);
		if (result != Error.Ok)
		{
			_currentState = NetworkState.Offline;
			GD.PrintErr($"Failed to create client for {ipAddress}:{port}: {result}");
			return result;
		}

		_multiplayer.MultiplayerPeer = peer;
		GD.Print($"Attempting to connect to {ipAddress}:{port}");

		return Error.Ok;
	}

	public void DisconnectFromServer()
	{
		if (_currentState == NetworkState.Offline) return;

		if (_currentState == NetworkState.Server)
		{
			StopServerBroadcast();
			
			// Notify all clients of server shutdown
			Rpc(nameof(OnServerShutdown));
		}

		_multiplayer.MultiplayerPeer?.Close();
		_multiplayer.MultiplayerPeer = null;
		
		_currentState = NetworkState.Offline;
		_connectedPlayers.Clear();
		_currentServerInfo = null;

		GD.Print("Disconnected from server");
	}

	#endregion

	#region Server Discovery

	public void StartServerDiscovery()
	{
		if (_udpListener != null)
		{
			_udpListener.Close();
		}

		_udpListener = new PacketPeerUdp();
		var result = _udpListener.Bind(DefaultPort + 1);
		if (result != Error.Ok)
		{
			GD.PrintErr($"Failed to bind UDP listener: {result}");
			return;
		}
		
		_discoveredServers.Clear();
		_discoveryTimer.Start();
		
		GD.Print($"Started server discovery on port {DefaultPort + 1}");
	}

	public void StopServerDiscovery()
	{
		_discoveryTimer.Stop();
		_udpListener?.Close();
		_udpListener = null;
		
		GD.Print("Stopped server discovery");
	}

	private void DiscoverServers()
	{
		if (_udpListener == null) return;

		// Check for server broadcasts
		bool foundNewServer = false;
		while (_udpListener.GetAvailablePacketCount() > 0)
		{
			var packet = _udpListener.GetPacket();
			var message = packet.GetStringFromUtf8();
			var serverIP = _udpListener.GetPacketIP();

			try
			{
				var serverInfo = Json.ParseString(message).AsGodotDictionary();
				
				// Check if this is a server broadcast
				if (serverInfo.ContainsKey("server_name"))
				{
					var server = new ServerInfo
					{
						ServerName = serverInfo["server_name"].AsString(),
						HostName = serverInfo["host_name"].AsString(),
						PlayerCount = serverInfo["player_count"].AsInt32(),
						MaxPlayers = serverInfo["max_players"].AsInt32(),
						MapName = serverInfo["map_name"].AsString(),
						HasPassword = serverInfo["has_password"].AsBool(),
						IPAddress = serverIP,
						Port = serverInfo["port"].AsInt32()
					};

					// Update or add server
					var existing = _discoveredServers.FirstOrDefault(s => s.IPAddress == server.IPAddress && s.Port == server.Port);
					if (existing != null)
					{
						var index = _discoveredServers.IndexOf(existing);
						_discoveredServers[index] = server;
					}
					else
					{
						_discoveredServers.Add(server);
						GD.Print($"Discovered server: {server.ServerName} at {server.IPAddress}:{server.Port}");
						foundNewServer = true;
					}
				}
			}
			catch (Exception e)
			{
				GD.PrintErr($"Failed to parse server broadcast: {e.Message}");
			}
		}
		
		if (foundNewServer)
		{
			EmitSignal(SignalName.ServerListUpdated);
		}
	}

	private void StartServerBroadcast()
	{
		if (_currentState != NetworkState.Server) return;

		if (_udpBroadcaster != null)
		{
			_udpBroadcaster.Close();
		}

		_udpBroadcaster = new PacketPeerUdp();
		_broadcastTimer.Start();

		GD.Print("Started server broadcast");
	}

	private void StopServerBroadcast()
	{
		_broadcastTimer.Stop();
		_udpBroadcaster?.Close();
		_udpBroadcaster = null;

		GD.Print("Stopped server broadcast");
	}

	private void BroadcastServer()
	{
		if (_currentState != NetworkState.Server || _udpBroadcaster == null) return;

		var serverData = new Godot.Collections.Dictionary
		{
			["server_name"] = _currentServerInfo.ServerName,
			["host_name"] = _currentServerInfo.HostName,
			["player_count"] = _connectedPlayers.Count,
			["max_players"] = _currentServerInfo.MaxPlayers,
			["map_name"] = _currentServerInfo.MapName,
			["has_password"] = _currentServerInfo.HasPassword,
			["port"] = _currentServerInfo.Port
		};

		var json = Json.Stringify(serverData);
		var packet = json.ToUtf8Buffer();
		
		try
		{
			bool broadcastSuccessful = false;
			
			// Try local subnet broadcast first (more likely to work)
			var localIP = GetLocalIPAddress();
			if (localIP != "127.0.0.1")
			{
				var subnetBroadcast = GetSubnetBroadcast(localIP);
				if (subnetBroadcast != localIP)
				{
					_udpBroadcaster.ConnectToHost(subnetBroadcast, DefaultPort + 1);
					var result = _udpBroadcaster.PutPacket(packet);
					_udpBroadcaster.Close();
					
					if (result == Error.Ok)
					{
						GD.Print($"Broadcasted server to {subnetBroadcast}: {_currentServerInfo.ServerName}");
						broadcastSuccessful = true;
					}
					else
					{
						GD.PrintErr($"Failed to broadcast to {subnetBroadcast}: {result}");
					}
				}
			}
			
			// Try multicast address (more reliable than broadcast)
			_udpBroadcaster.ConnectToHost("224.0.0.1", DefaultPort + 1);
			var multicastResult = _udpBroadcaster.PutPacket(packet);
			_udpBroadcaster.Close();
			
			if (multicastResult == Error.Ok)
			{
				GD.Print($"Multicasted server to 224.0.0.1: {_currentServerInfo.ServerName}");
				broadcastSuccessful = true;
			}
			
			// Try direct localhost for same-machine testing
			_udpBroadcaster.ConnectToHost("127.0.0.1", DefaultPort + 1);
			var localhostResult = _udpBroadcaster.PutPacket(packet);
			_udpBroadcaster.Close();
			
			if (localhostResult == Error.Ok)
			{
				GD.Print($"Sent server info to localhost: {_currentServerInfo.ServerName}");
				broadcastSuccessful = true;
			}
			
			// Only try global broadcast if others failed and it might work
			if (!broadcastSuccessful)
			{
				_udpBroadcaster.ConnectToHost("255.255.255.255", DefaultPort + 1);
				var globalResult = _udpBroadcaster.PutPacket(packet);
				_udpBroadcaster.Close();
				
				if (globalResult == Error.Ok)
				{
					GD.Print($"Broadcasted server to 255.255.255.255: {_currentServerInfo.ServerName}");
				}
				else
				{
					GD.Print($"Note: Global broadcast blocked (normal on macOS). Using local discovery methods.");
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Error broadcasting server: {e.Message}");
		}
	}

	#endregion

	#region Network Events

	private void OnPeerConnected(long id)
	{
		GD.Print($"Peer connected: {id}");
		
		if (IsServer)
		{
			// Send current server state to new player
			RpcId((int)id, nameof(ReceiveServerInfo), 
				_currentServerInfo.ServerName, _currentServerInfo.HostName, 
				_currentServerInfo.PlayerCount, _currentServerInfo.MaxPlayers,
				_currentServerInfo.MapName, _currentServerInfo.HasPassword,
				_currentServerInfo.IPAddress, _currentServerInfo.Port);
			
			// Send existing players to new player
			foreach (var player in _connectedPlayers.Values)
			{
				RpcId((int)id, nameof(ReceivePlayerInfo), 
					player.PlayerId, player.PlayerName, player.IsReady,
					player.Position, player.Rotation, player.Health, player.CurrentWeaponIndex);
			}
		}
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"Peer disconnected: {id}");
		
		if (_connectedPlayers.ContainsKey((int)id))
		{
			var player = _connectedPlayers[(int)id];
			_connectedPlayers.Remove((int)id);
			
			EmitSignal(SignalName.PlayerLeft, (int)id);
			
			if (IsServer)
			{
				// Notify other clients about player leaving
				Rpc(nameof(OnPlayerLeft), (int)id);
			}
		}
	}

	private void OnConnectedToServer()
	{
		GD.Print("Connected to server");
		_currentState = NetworkState.Client;
		EmitSignal(SignalName.ServerJoined);
		
		// Request to join with player info
		var playerName = OS.GetEnvironment("USERNAME");
		RpcId(1, nameof(RequestJoin), playerName);
	}

	private void OnConnectionFailed()
	{
		GD.PrintErr("Connection to server failed");
		_currentState = NetworkState.Offline;
		EmitSignal(SignalName.ConnectionFailed, "Failed to connect to server");
	}

	private void OnServerDisconnected()
	{
		GD.Print("Disconnected from server");
		_currentState = NetworkState.Offline;
		_connectedPlayers.Clear();
		_currentServerInfo = null;
	}

	#endregion

	#region RPCs

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestJoin(string playerName)
	{
		if (!IsServer) return;

		var senderId = _multiplayer.GetRemoteSenderId();
		var newPlayer = new PlayerInfo
		{
			PlayerId = senderId,
			PlayerName = playerName,
			IsReady = false
		};

		_connectedPlayers[senderId] = newPlayer;
		_currentServerInfo.PlayerCount = _connectedPlayers.Count;

		// Notify all clients about new player
		Rpc(nameof(OnPlayerJoined), newPlayer.PlayerId, newPlayer.PlayerName, newPlayer.IsReady,
			newPlayer.Position, newPlayer.Rotation, newPlayer.Health, newPlayer.CurrentWeaponIndex);
		
		GD.Print($"Player joined: {playerName} ({senderId})");
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceiveServerInfo(string serverName, string hostName, int playerCount, int maxPlayers, 
		string mapName, bool hasPassword, string ipAddress, int port)
	{
		_currentServerInfo = new ServerInfo
		{
			ServerName = serverName,
			HostName = hostName,
			PlayerCount = playerCount,
			MaxPlayers = maxPlayers,
			MapName = mapName,
			HasPassword = hasPassword,
			IPAddress = ipAddress,
			Port = port
		};
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReceivePlayerInfo(int playerId, string playerName, bool isReady, Vector3 position, Vector3 rotation, int health, int currentWeaponIndex)
	{
		var playerInfo = new PlayerInfo
		{
			PlayerId = playerId,
			PlayerName = playerName,
			IsReady = isReady,
			Position = position,
			Rotation = rotation,
			Health = health,
			CurrentWeaponIndex = currentWeaponIndex
		};
		_connectedPlayers[playerId] = playerInfo;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerJoined(int playerId, string playerName, bool isReady, Vector3 position, Vector3 rotation, int health, int currentWeaponIndex)
	{
		var playerInfo = new PlayerInfo
		{
			PlayerId = playerId,
			PlayerName = playerName,
			IsReady = isReady,
			Position = position,
			Rotation = rotation,
			Health = health,
			CurrentWeaponIndex = currentWeaponIndex
		};
		_connectedPlayers[playerId] = playerInfo;
		EmitSignal(SignalName.PlayerJoined, playerId, playerName);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnPlayerLeft(int playerId)
	{
		_connectedPlayers.Remove(playerId);
		EmitSignal(SignalName.PlayerLeft, playerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void OnServerShutdown()
	{
		GD.Print("Server is shutting down");
		DisconnectFromServer();
	}

	#endregion

	#region Utility

	private string GetLocalIPAddress()
	{
		var addresses = IP.GetLocalAddresses();
		foreach (var address in addresses)
		{
			// Return first non-loopback IPv4 address
			if (address != "127.0.0.1" && address.Contains('.') && !address.Contains(':'))
			{
				return address;
			}
		}
		return "127.0.0.1";
	}
	
	private string GetSubnetBroadcast(string localIP)
	{
		// Simple subnet broadcast calculation for common /24 networks
		// For a more robust solution, you'd want to check actual subnet mask
		var parts = localIP.Split('.');
		if (parts.Length == 4)
		{
			return $"{parts[0]}.{parts[1]}.{parts[2]}.255";
		}
		return localIP;
	}

	public override void _ExitTree()
	{
		DisconnectFromServer();
		StopServerDiscovery();
		
		if (Instance == this)
		{
			Instance = null;
		}
	}

	#endregion
} 