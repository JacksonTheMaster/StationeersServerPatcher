# StationeersServerPatcher

A simple BepInEx/Harmony patch for Stationeers dedicated servers that currently fixes the auto-pause functionality on server startup. More fixes may be added if needed.

## What This Does

This is a **server-side only** mod. Clients don't need BepInEx, any mods, or special configuration — the server remains fully "vanilla" compatible for all players.

## The Problem

When starting a Stationeers dedicated server with `AutoPauseServer` enabled, the server does **not** enter the auto-pause state on initial startup.

The game's built-in auto-pause logic only triggers when the client count transitions from `>0` to `0` (i.e., when the last player disconnects via `OnClientRemoved()`). On a fresh server start with zero clients, this transition never occurs — so the server remains running and unpaused indefinitely.

### Why This Matters

Without this fix:
- The game loop continues running with no players connected
- In-game resources are consumed (stored energy, oxygen, etc.)
- Machines and systems operate unnecessarily

### Expected Behavior

1. Server starts with 0 clients
2. After 10 seconds: *"No clients connected. Will save and pause in 10 seconds."*
3. Auto-save is performed
4. Game loop pauses (`WorldManager.SetGamePause(true)`)

### Actual Behavior (Without This Patch)

1. Server starts
2. No clients connect → server stays unpaused forever
3. No auto-save or pause until at least one client connects and then disconnects

## How The Patch Works

This patch hooks into `NetworkServer.CreateNewGameSession()` and triggers the existing auto-pause logic that the game already has — just at the right time:

```csharp
// Reuses the game's existing auto-pause logic
if (ClientCount == 0 && AutoPauseServer is enabled)
{
    // Trigger AutoSaveOnLastClientLeave() - the same method called when last player leaves
}
```

Additional fixes included:
- **Stops the autosave timer when paused** — prevents unnecessary save cycles while server is idle
- **Syncs `NetworkBase.IsPaused` with `WorldManager.IsGamePaused`** — fixes a state desync bug
- **Unpauses when a client connects** — properly resumes the game when a player joins a paused server

## Installation

### Requirements

- Stationeers Dedicated Server
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx) or higher

### Steps

1. Install BepInEx on your dedicated server or use [SSUI](https://github.com/SteamServerUI/StationeersServerUI) (comes with BepInEx)
2. Copy `StationeersServerPatcher.dll` (can be found pre-compiled in the releases) to:
   ```
   [StationeersServerDir]/BepInEx/plugins/StationeersServerPatcher/StationeersServerPatcher.dll
   ```
3. Start your server — the patch applies automatically

## Building

This project uses .NET Framework 4.7.2 and can be built easily in the devcontainer using the VS Code tasks:

| Task | Description |
|------|-------------|
| `build` | Build (Debug) |
| `build-release` | Build (Release) |
| `publish` | Publish to `/ship` directory |
| `clean` | Clean build artifacts |

## Credits

- **Author:** [JacksonTheMaster](https://github.com/JacksonTheMaster)
- **Related Project:** [StationeersServerUI](https://github.com/SteamServerUI/StationeersServerUI)
