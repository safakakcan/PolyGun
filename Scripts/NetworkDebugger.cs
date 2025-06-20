using Godot;
using System;

public partial class NetworkDebugger : Control
{
	private VBoxContainer _container;
	private Button _testBroadcastButton;
	private Button _testListenButton;
	private Button _stopTestButton;
	private Label _statusLabel;
	private RichTextLabel _logOutput;
	
	private PacketPeerUdp _testUdp;
	private Timer _testTimer;
	private bool _isListening = false;
	private bool _isBroadcasting = false;
	
	public override void _Ready()
	{
		SetupUI();
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		Visible = false; // Hidden by default
	}
	
	private void SetupUI()
	{
		_container = new VBoxContainer();
		_container.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
		AddChild(_container);
		
		var title = new Label();
		title.Text = "Network Debugger";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontSizeOverride("font_size", 20);
		_container.AddChild(title);
		
		_testBroadcastButton = new Button();
		_testBroadcastButton.Text = "Test UDP Broadcast";
		_testBroadcastButton.Pressed += OnTestBroadcastPressed;
		_container.AddChild(_testBroadcastButton);
		
		_testListenButton = new Button();
		_testListenButton.Text = "Test UDP Listen";
		_testListenButton.Pressed += OnTestListenPressed;
		_container.AddChild(_testListenButton);
		
		_stopTestButton = new Button();
		_stopTestButton.Text = "Stop Test";
		_stopTestButton.Pressed += OnStopTestPressed;
		_container.AddChild(_stopTestButton);
		
		_statusLabel = new Label();
		_statusLabel.Text = "Ready (Press F12 to toggle)";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_container.AddChild(_statusLabel);
		
		_logOutput = new RichTextLabel();
		_logOutput.CustomMinimumSize = new Vector2(700, 400);
		_logOutput.BbcodeEnabled = true;
		_container.AddChild(_logOutput);
		
		var closeButton = new Button();
		closeButton.Text = "Close";
		closeButton.Pressed += () => Visible = false;
		_container.AddChild(closeButton);
		
		_testTimer = new Timer();
		_testTimer.WaitTime = 2.0f;
		_testTimer.Timeout += OnTestTimerTimeout;
		AddChild(_testTimer);
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") && Input.IsKeyPressed(Key.F12))
		{
			Visible = !Visible;
			if (Visible)
			{
				LogMessage("Network Debugger opened. This tool helps test UDP connectivity.");
			}
		}
	}
	
	private void OnTestBroadcastPressed()
	{
		if (_isBroadcasting) return;
		
		_isBroadcasting = true;
		_statusLabel.Text = "Broadcasting test packets...";
		LogMessage("Starting UDP broadcast test...");
		
		_testTimer.Start();
	}
	
	private void OnTestListenPressed()
	{
		if (_isListening) return;
		
		_testUdp = new PacketPeerUdp();
		var result = _testUdp.Bind(7001);
		
		if (result == Error.Ok)
		{
			_isListening = true;
			_statusLabel.Text = "Listening for UDP packets on port 7001...";
			LogMessage("Started UDP listener on port 7001");
		}
		else
		{
			LogMessage($"[color=red]Failed to bind UDP listener: {result}[/color]");
		}
	}
	
	private void OnStopTestPressed()
	{
		_testTimer.Stop();
		_testUdp?.Close();
		_testUdp = null;
		_isListening = false;
		_isBroadcasting = false;
		_statusLabel.Text = "Test stopped";
		LogMessage("Test stopped");
	}
	
	private void OnTestTimerTimeout()
	{
		if (_isBroadcasting)
		{
			BroadcastTestPacket();
		}
	}
	
	public override void _Process(double delta)
	{
		if (_isListening && _testUdp != null)
		{
			while (_testUdp.GetAvailablePacketCount() > 0)
			{
				var packet = _testUdp.GetPacket();
				var message = packet.GetStringFromUtf8();
				var senderIP = _testUdp.GetPacketIP();
				var senderPort = _testUdp.GetPacketPort();
				
				LogMessage($"[color=green]Received: {message} from {senderIP}:{senderPort}[/color]");
			}
		}
	}
	
	private void BroadcastTestPacket()
	{
		var testUdp = new PacketPeerUdp();
		var message = $"Test packet from {OS.GetEnvironment("USERNAME")} at {DateTime.Now:HH:mm:ss}";
		var packet = message.ToUtf8Buffer();
		
		// Try different broadcast addresses (same as NetworkManager)
		string[] broadcastAddresses = { 
			"127.0.0.1",        // Localhost
			"224.0.0.1",        // Multicast
			"192.168.1.255",    // Common subnet
			"10.0.0.255",       // Common subnet
			"255.255.255.255"   // Global broadcast (will likely fail on macOS)
		};
		
		foreach (var addr in broadcastAddresses)
		{
			testUdp.ConnectToHost(addr, 7001);
			var result = testUdp.PutPacket(packet);
			
			if (result == Error.Ok)
			{
				LogMessage($"[color=green]✓ Sent to {addr}: Success[/color]");
			}
			else
			{
				LogMessage($"[color=red]✗ Failed to send to {addr}: {result}[/color]");
			}
			testUdp.Close();
		}
	}
	
	private void LogMessage(string message)
	{
		_logOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
		_logOutput.ScrollToLine(_logOutput.GetLineCount() - 1);
	}
} 
