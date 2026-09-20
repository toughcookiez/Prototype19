# Handover Summary: Prototype19 Multiplayer Bug Fix

## Context
- **Project**: Unity multiplayer prototype using Netcode for GameObjects (NGO) and Unity Transport (UTP).
- **Task**: Fix a critical multiplayer bug where picking a card caused UDP socket failure errors (`UnityTransport:OnEarlyUpdate`) on clients, and sometimes only one player would transition to the new scene.

## Bug Identified
The UDP socket receive failures occurred because:
1. **Duplicate NetworkManagers**: When loading a new scene that also contained a `NetworkManager`, duplicates were created before the persistent one could clean them up, leading to routing errors.
2. **State Sync Conflict**: The server was calling `ResetPlayerForNextRound()` (syncing position/health/UI) immediately before triggering a `NetworkSceneManager.LoadScene()`. This resulted in RPCs being sent for objects that were in the process of being destroyed for the scene transition.

## Solutions Implemented

### 1. `Assets/Scripts/PersistentNetworkManager.cs`
- Added a guard in `Awake` to destroy itself if a `NetworkManager.Singleton` already exists.
- Subscribed to `NetworkManager.Singleton.SceneManager.OnSceneEvent`.
- Implemented `OnSceneEvent` to detect `SceneEventType.LoadComplete` and destroy any duplicate `NetworkManager` instances in the scene hierarchy, ensuring only the persistent singleton remains.

### 2. `Assets/Imports/ModularFirstPersonController/FirstPersonController/FirstPersonController.cs`
- Modified `ResetLevelAfterCardPick()` to prioritize the scene switch.
- If a scene switch is scheduled, it now **skips** the immediate player reset. This prevents sending network state updates for objects that are about to be destroyed.
- Relies on the existing logic in `OnNetworkSpawn()` to reset player state (UI, health, controls) once they respawn in the new scene.
- Reverted `mapPoolSceneIndices` to `new int[0]` to ensure it is never null.

## Remaining Notes
- **Obsolete Warnings**: There are warnings regarding `Object.FindObjectsOfType<T>(bool)`. These are harmless but can be updated to `Object.FindObjectsByType<T>(FindObjectsSortMode.None)` for future-proofing.
- **Scene Setup**: Ensure that `Enable Scene Management` is checked in the `NetworkManager` configuration for all scenes in the map pool.

## Files Modified
- `Assets/Scripts/PersistentNetworkManager.cs`
- `Assets/Imports/ModularFirstPersonController/FirstPersonController/FirstPersonController.cs`
