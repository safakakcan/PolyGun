# PolyGun - Multiplayer FPS Gameplay Guide

## 🎮 **Getting Started**

### **Immediate Single-Player Mode**
When you run the game, you'll start with a local player that you can control immediately:

- **Movement**: WASD keys
- **Mouse Look**: Move mouse to look around
- **Jump**: Space bar
- **Sprint**: Shift (hold while moving)
- **Shoot**: Left mouse button
- **Aim**: Right mouse button  
- **Reload**: R key

### **UI & Menus**
- **ESC**: Toggle between game and multiplayer menu
- **F1**: Quick access to multiplayer menu
- The game starts with mouse captured for FPS gameplay
- Your health and FPS are displayed on screen

## 🌐 **Multiplayer Setup**

### **Hosting a Game**
1. Press **ESC** or **F1** to open the multiplayer menu
2. Enter your **Player Name**
3. Set **Server Name** (optional)
4. Configure **Max Players** (2-16) and **Min Players** (2-16)
5. Click **"Host Server"**
6. You'll be in the lobby - mark yourself as **"Ready"**
7. When other players join and are ready, you can start the game

### **Joining a Game**
1. Press **ESC** or **F1** to open the multiplayer menu
2. Enter your **Player Name**
3. Enter the **Server Address** (ask the host for their IP)
   - For local testing: use `127.0.0.1`
   - For LAN: use the host's local IP address
4. Click **"Join Server"**
5. Mark yourself as **"Ready"** in the lobby
6. Wait for the host to start the game

### **Game Flow**
1. **Lobby**: Players join and mark themselves ready
2. **Countdown**: 5-second countdown when all players are ready
3. **Playing**: All players spawn and can move/fight
4. **Respawn**: Players respawn automatically when they die

## 🎯 **Gameplay Features**

### **Combat System**
- **Health**: 100 HP with regeneration after 3 seconds without damage
- **Shooting**: Hitscan weapons with lag compensation
- **Fair Combat**: Server-authoritative with rollback for accurate hits
- **Damage**: Weapons deal realistic damage

### **Movement System**
- **Responsive Controls**: Zero-lag movement feel
- **Client Prediction**: Your movement feels instant
- **Physics**: Realistic movement with acceleration and friction
- **Jump Mechanics**: Standard FPS jumping

### **Networking Features**
- **64Hz Tick Rate**: Competitive-grade server simulation
- **Lag Compensation**: Fair hit detection up to 1000ms latency
- **Anti-Cheat**: Server validates all critical actions
- **Smooth Interpolation**: Other players move smoothly

## 🎮 **Controls Reference**

| Action | Key/Button |
|--------|------------|
| Move Forward | W |
| Move Backward | S |
| Move Left | A |
| Move Right | D |
| Jump | Space |
| Sprint | Shift (hold) |
| Shoot | Left Mouse |
| Aim | Right Mouse |
| Reload | R |
| Toggle Menu | ESC |
| Quick Multiplayer | F1 |

## 🖥️ **Technical Information**

### **Performance**
- **Target FPS**: 60+ FPS
- **Network Usage**: ~5KB/s per player
- **Memory Usage**: ~50MB for full game state history
- **Latency**: Optimized for up to 1 second of network latency

### **Server Requirements**
- The host runs the authoritative server
- Host can play normally alongside other players
- Automatic player cleanup when players disconnect
- Robust state synchronization

## 🔧 **Troubleshooting**

### **Connection Issues**
- Make sure firewall allows the game (port 7000)
- For LAN play, ensure all players are on the same network
- Check that the server address is correct

### **Performance Issues**
- Lower graphics settings if needed
- Close other applications for better performance
- Ensure stable internet connection for multiplayer

### **Gameplay Issues**
- If stuck, try pressing ESC to toggle mouse capture
- If UI doesn't appear, try pressing F1
- For movement issues, ensure you're not in menu mode

## 🚀 **Advanced Features**

### **Network Debugging**
- FPS counter shows in top-right corner
- Ping display shows when in multiplayer
- Console output shows network events

### **Map Features**
- Spawn points spread across the map
- Cover objects and walls for tactical gameplay
- Open areas for combat and movement

---

**Enjoy your modern multiplayer FPS experience!** 🎯

The game features industry-standard networking with client-side prediction, lag compensation, and server authority - just like professional FPS games! 