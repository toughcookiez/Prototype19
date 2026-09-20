# Multiplayer Setup

Unity will resolve the new Netcode packages from `Packages/manifest.json` the next time the project opens or refreshes.

## Scene

1. Create an empty GameObject named `NetworkManager`.
2. Add `NetworkManager`.
3. Add `UnityTransport`.
4. Add `MultiplayerLauncher` if you want the temporary Host / Client / Server buttons in Play Mode.

## Player Prefab

Use `Assets/Imports/ModularFirstPersonController/FirstPersonController/FirstPersonController.prefab`.

1. Add `NetworkObject`.
2. Add `OwnerNetworkTransform`.
3. In the `NetworkManager`, set this prefab as the Player Prefab.
4. In `FirstPersonController`, assign `Body Root` or `Body Renderers` to the visible body mesh renderers.
5. Keep the local body renderers set to `Shadows Only`; remote spawned players will switch those renderers to `On` automatically.

## Bullet Prefab

Use `Assets/Prefabs/Bullet.prefab`.

1. Add `NetworkObject`.
2. Add `NetworkTransform`.
3. Register the bullet prefab in the `NetworkManager` Network Prefabs list.

## Testing

1. Start Play Mode and click `Host`.
2. Start a second Editor instance, a build, or a Multiplayer Play Mode clone and click `Client`.
3. The local player should keep their body shadow-only, while remote players should render normally.