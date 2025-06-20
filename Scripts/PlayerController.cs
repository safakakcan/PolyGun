using Godot;

/// <summary>
/// Compatibility wrapper for the old PlayerController
/// Redirects to ModernPlayerController for better networking support
/// </summary>
public partial class PlayerController : ModernPlayerController
{
	// Legacy exports that may be set in the scene
	[Export] public bool IsLocal { get; set; } = true;
	[Export] public PackedScene BulletScene { get; set; }
	[Export] public AudioStream FireSound { get; set; }
	
	public override void _Ready()
	{
		// Set up legacy compatibility
		if (IsLocal)
		{
			NetworkId = 1; // Local player ID
		}
		
		// Call the modern implementation
		base._Ready();
		
		// Setup weapon system with legacy properties
		SetupLegacyWeaponSystem();
	}
	
	private void SetupLegacyWeaponSystem()
	{
		// Find weapon system and configure it with legacy properties
		var weaponSystem = GetNode<WeaponSystem>("WeaponSystem");
		if (weaponSystem != null)
		{
			if (BulletScene != null)
			{
				weaponSystem.BulletScene = BulletScene;
			}
			
			if (FireSound != null)
			{
				weaponSystem.FireSound = FireSound;
			}
		}
	}
} 
