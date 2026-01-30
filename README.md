# StationeersServerPatcher

<img width="1235" height="566" alt="image" src="https://github.com/user-attachments/assets/eb5f2650-49cf-4c1a-a31c-2a08bc9d3466" />

A comprehensive BepInEx/Harmony patch suite for Stationeers dedicated servers that fixes auto-pause functionality, prevents unauthorized spawning, and provides extensive configuration options.

## What This Does

This is a **server-side only** mod. Clients don't need BepInEx, any mods, or special configuration — the server remains fully "vanilla" compatible for all players.

## Features

### 🔄 Auto-Pause Fix
Fixes the auto-pause functionality on server startup when `AutoPauseServer` is enabled.

**The Problem**: When starting a Stationeers dedicated server with `AutoPauseServer` enabled, the server does **not** enter the auto-pause state on initial startup.

**The Fix**: Triggers the existing auto-pause logic at server startup, ensuring proper pause behavior with zero clients.

### 🚫 Spawn Blocker
Prevents players from using "thing spawn" commands unless the server is in Creative mode.

**Why**: Prevents unauthorized item spawning on survival servers, maintaining game balance and avoiding griefers on public servers.

### ⚙️ Configuration System
Full BepInEx configuration support with individual patch toggles and remote killswitch capabilities.

### 🛡️ Remote Killswitch
Emergency disable system that can remotely disable broken patches for future versions without requiring mod updates.

## Installation

### Requirements

- Stationeers Dedicated Server
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx)

### Steps

1. Install BepInEx on your dedicated server or use [SSUI](https://github.com/SteamServerUI/StationeersServerUI) (comes with BepInEx)
2. Copy `StationeersServerPatcher.dll` to:
   ```
   [StationeersServerDir]/BepInEx/plugins/StationeersServerPatcher/StationeersServerPatcher.dll
   ```
3. Start your server — patches apply automatically

## Configuration

Configuration is available at `/BepInEx/config/com.jacksonthemaster.StationeersServerPatcher.cfg`:

```ini
[Patches]
EnableAutoPausePatch = true
EnableSpawnBlockerPatch = true

[Remote]
EnableRemoteKillswitch = true
RemoteConfigUrl = https://raw.githubusercontent.com/SteamServerUI/StationeersServerUI/main/patcher-config.xml
```

### Remote Killswitch

The remote killswitch allows emergency disabling of patches via XML configuration. Local settings always take precedence — remote config cannot force-enable locally disabled features.

Example remote config (`patcher-config.xml`):
```xml
<PatcherConfig>
  <Message>Emergency maintenance</Message>
  <Features>
    <Feature>
      <Id>AutoPausePatch</Id>
      <Enabled>false</Enabled>
      <Reason>Game update compatibility issue</Reason>
    </Feature>
  </Features>
</PatcherConfig>
```

## API Usage

The mod exposes a public API for other plugins:

```csharp
// Execute server commands
StationeersServerPatcher.RunCommand("say Hello World");
StationeersServerPatcher.RunCommand("kick PlayerName");

// Async execution
await StationeersServerPatcher.RunCommandAsync("say Async message");

// Send messages to server chat
StationeersServerPatcher.LogToServerChat("Server message");
await StationeersServerPatcher.LogToServerChatAsync("Async server message");

// Check configuration
bool autoPauseEnabled = PluginConfig.IsAutoPausePatchEnabled;
bool spawnBlockerEnabled = PluginConfig.IsSpawnBlockerPatchEnabled;
```

## Building

This project uses .NET Framework 4.7.2 and can be built easily in the devcontainer using the VS Code tasks:

| Task | Description |
|------|-------------|
| `build` | Build (Debug) |
| `build-release` | Build (Release) |
| `publish` | Publish to `/ship` directory |
| `clean` | Clean build artifacts |

## Project Structure

```
StationeersServerPatcher/
├── StationeersServerPatcher.cs    # Main plugin class
├── PluginConfig.cs                # Configuration management
├── RemoteConfig.cs                # Remote killswitch
├── ServerPauseHelper.cs           # Pause state utilities
└── Patches/
    ├── AutoPausePatches.cs        # Auto-pause fixes
    └── SpawnBlockerPatch.cs       # Spawn blocking
```

## Credits

- **Author:** [JacksonTheMaster](https://github.com/JacksonTheMaster)
- **Related Project:** [StationeersServerUI](https://github.com/SteamServerUI/StationeersServerUI)
