# Steamworks Integration

Online multiplayer runs over Steam. Netcode for GameObjects (NGO) traffic goes peer-to-peer through Valve's relay
network, and Steam lobbies are used to find the host (lobby code, friend invite, or "Join Game"). When Steam is not
available, the game falls back to the original direct-IP path over `UnityTransport`.

| | |
|---|---|
| Steam App ID | `3999910` (`SteamManager.AppId`) |
| Library | [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) `2025.164.1` (UPM git package `com.rlabrecque.steamworks.net`) |
| Netcode | Netcode for GameObjects `2.4.4` |
| Platforms | Windows x64, macOS (universal), via the native libraries shipped in the package |

## Why Steamworks.NET

We chose it over Facepunch.Steamworks because the game targets both Windows and macOS:

- One managed assembly for all platforms. Facepunch ships separate `Win64` / `Win32` / `Posix` assemblies that
  need per-platform import settings, which breaks easily when the team is on both Windows and Mac.
- It's actively maintained and installs as a UPM package (one line in `Packages/manifest.json`).
- It maps one-to-one onto Valve's C++ API, so Valve's docs and forum answers apply directly.

## Files

All Steam code lives in `Assets/Scripts/Networking/Steam/`.

| File | Responsibility |
|---|---|
| `SteamManager.cs` | Starts and shuts down the Steam API, pumps callbacks, and owns the `SteamLobby`. Created automatically before the first scene loads, so no scene setup is needed. |
| `SteamNetworkingSocketsTransport.cs` | NGO `NetworkTransport` over `ISteamNetworkingSockets` P2P. Adapted for NGO 2.x from the MIT-licensed [community transport](https://github.com/Unity-Technologies/multiplayer-community-contributions). |
| `SteamLobby.cs` | Steam matchmaking: create a lobby, search by code, join, invite overlay, rich presence, `+connect_lobby` launch arguments. |
| `Editor/SteamEditorTools.cs` | **Tools → Steam → Use Steam In Editor** toggle. |

Integration points outside that folder:

| File | Change |
|---|---|
| `Networking/LobbyManager.cs` | Picks the transport, hosts and joins through the Steam lobby, connection approval, leaving the lobby. |
| `Networking/LobbyUI.cs`, `UI/MainMenuUI.uxml` | **Invite Friends** button in the waiting room (shown while connected with Steam). |
| `steam_appid.txt` (project root) | Lets the Unity Editor start Steam with our App ID. |

## Flow

### Startup

1. `SteamManager.Bootstrap()` (`RuntimeInitializeOnLoadMethod`, `BeforeSceneLoad`) creates a `DontDestroyOnLoad`
   `SteamManager` GameObject.
2. `Awake` decides whether to use Steam (see [When Steam is disabled](#when-steam-is-disabled)), runs the
   Steamworks.NET binary checks, then:
   - **Editor:** relies on `steam_appid.txt` in the project root.
   - **Development build:** sets the `SteamAppId` / `SteamGameId` environment variables so the build runs outside
     Steam without a `steam_appid.txt` file.
   - **Release build:** calls `SteamAPI.RestartAppIfNecessary`. A release build started from the exe directly is
     relaunched through the Steam client.
3. `SteamAPI.Init()`. If it succeeds, `InitRelayNetworkAccess()` warms up the relay and a `SteamLobby` is created.
   If it fails, the game logs a warning and uses direct IP.
4. `Update` calls `SteamAPI.RunCallbacks()` every frame. `OnDestroy` disposes the lobby and calls `SteamAPI.Shutdown()`.

Anything can check `SteamManager.Initialized` and use `SteamManager.Lobby` (which is `null` when Steam is off).

### Hosting ("Create Lobby")

`LobbyManager.StartHostLobby()`:

1. Generates a 6-character lobby code.
2. `SelectTransport(useSteam: true)` adds `SteamNetworkingSocketsTransport` to the NetworkManager GameObject (if
   needed) and assigns it to `NetworkConfig.NetworkTransport`.
3. `NetworkManager.StartHost()`. The transport opens a P2P listen socket (`CreateListenSocketP2P`, virtual port 0).
4. `SteamLobby.Create(code, maxPlayers)` creates a **public** Steam lobby and sets the lobby data
   `game=Prototype19` and `code=<CODE>`, plus the rich presence `connect=+connect_lobby <lobbyId>`.

The lobby is public only so that the code search can find it. Access is still gated by NGO connection approval (below).

### Joining

All three entry points end in `SteamLobby.HandleLobbyEntered`. That handler reads the host from
`GetLobbyOwner` and the code from the lobby data, then raises `OnJoinedLobbyAsClient(hostSteamId, code)`.
`LobbyManager.HandleSteamLobbyJoined` points the Steam transport at the host and calls `StartClient` with the code
as `ConnectionData`.

| Entry point | Path |
|---|---|
| Typed code | `StartClientLobby(code)` → `SteamLobby.JoinByCode` → `RequestLobbyList` filtered on `game` + `code` (worldwide) → `JoinLobby` |
| Friend invite / "Join Game" while running | `GameLobbyJoinRequested_t` or `GameRichPresenceJoinRequested_t` → `LobbyManager.HandleSteamJoinRequested` → `JoinLobby` |
| "Join Game" while not running | Steam launches the game with `+connect_lobby <id>`. `LobbyManager.Start` checks `SteamLobby.TryGetLaunchLobby` once per run. |
| Typed IP address | Always direct IP over `UnityTransport` (see below), even when Steam is running. |

### Connection approval

`LobbyManager.ApprovalCheck` approves a client when:

- it's the local host, **or**
- its `ConnectionData` matches `LobbyCode` (Steam and code joins), **or** Steam is off and the payload is empty (typed-IP join),
- **and** there's a free slot (`ConnectedClientsIds.Count < maxPlayers`, default 2).

Rejected clients get `response.Reason` ("Wrong lobby code." / "Lobby is full."), which they log from
`NetworkManager.DisconnectReason`.

### Starting the game and leaving

- `StartGame()` calls `SteamLobby.SetJoinable(false)` before loading the gameplay scene. The P2P connections
  don't depend on the lobby, so gameplay is unaffected.
- `LeaveLobby()` and a local disconnect call `SteamLobby.Leave()`, which leaves the lobby and clears rich presence.
- `SteamLobby` lives on `SteamManager`, not on `LobbyManager`, because `LobbyManager` is destroyed when the
  gameplay scene loads.

## Transport details

`SteamNetworkingSocketsTransport` adapts NGO's `NetworkTransport` to `ISteamNetworkingSockets`:

- **IDs:** on the server, each client's transport ID is its SteamID64. On the client, the server is `ServerClientId`
  (`0`), which is never a valid SteamID.
- **Connections:** incoming connections are accepted automatically on `Connecting`. NGO approval does the real
  gatekeeping. `Connect` is raised on `Connected`. `ClosedByPeer` / `ProblemDetectedLocally` close the handle (which is
  required to free it) and raise `Disconnect`. A failed outgoing connect also produces a `Disconnect`, so NGO's
  client start fails cleanly.
- **Delivery mapping:** `Unreliable` / `UnreliableSequenced` → `k_nSteamNetworkingSend_UnreliableNoNagle`. All
  reliable modes → `k_nSteamNetworkingSend_ReliableNoNagle`. Steam's reliable channel is ordered, and NGO already
  batches per tick, so Nagle is skipped.
- **Receive:** up to 64 messages per connection per poll are copied out and queued, then returned one per `PollEvent`.
- **RTT:** `GetConnectionRealTimeStatus(...).m_nPing`.
- **Message size:** Steam's limit is 512 KB per message, which is plenty for current NGO traffic.
- **Shutdown and disconnect:** close with linger so NGO's final disconnect message still gets delivered.

Changes from the community transport: NGO 2.x compatible, payloads sent without the extra trailing channel byte,
batched receive, connection handles are always closed (the original leaked them), real RTT, and failed
`StartClient` / `StartServer` are reported.

## When Steam is disabled

`SteamManager` skips Steam (and `LobbyManager` uses `UnityTransport` / direct IP) when any of these is true:

| Condition | Use |
|---|---|
| `-nosteam` command-line argument | Players or testers who want LAN / direct IP |
| **Tools → Steam → Use Steam In Editor** is off (EditorPrefs, per machine) | Local testing in the editor without Steam |
| Running as a Multiplayer Play Mode virtual player | Virtual players share the machine's single Steam login, so only the main editor uses Steam |
| `SteamAPI.Init()` fails | Steam isn't running or logged in, or the account doesn't own the app |

In direct-IP mode, typing an IP address connects there with an empty code, and a typed code connects to the
transport's configured address (default `127.0.0.1`).

## Testing

### In the editor (one machine, one Steam account)

1. Start the Steam client and log in with an account that has access to app 3999910.
2. Play `MainMenu`. The console should show `[Steam] Initialized as <name> (<steamid>), app 3999910.`
3. Create Lobby. The console should show `[Steam] Lobby <id> created with code <CODE>.`

A single account can't connect to its own host, so testing the actual client connection needs a second Steam
account on another machine.

### With two players

1. Make a **Development Build** (it runs without being launched through Steam) for Windows and/or macOS.
2. Player A: Create Lobby, then **Invite Friends** or share the code.
3. Player B: type the code, accept the invite, or use "Join Game" on Player A in the friends list.

The Steam overlay (and so the **Invite Friends** dialog) doesn't appear in the Unity editor. Test it in a build.

### Local testing without Steam

Turn off **Tools → Steam → Use Steam In Editor**, then use Multiplayer Play Mode as before.

### Log tags

Search the console for `[Steam]`. Useful lines: initialization result, lobby created, `Lobby code matched lobby`,
`No Steam lobby found with that code.`, connection end reasons (`Connection with <id> ended: ...`), and
`Disconnected: <reason>` from approval rejections.

## Shipping checklist

- [ ] Release builds are **not** Development builds (otherwise `RestartAppIfNecessary` is skipped).
- [ ] Don't ship `steam_appid.txt` next to the executable.
- [ ] In the Steamworks partner site, configure launch options for Windows and macOS for app 3999910 and publish.
- [ ] Test invites and "Join Game" from a Steam-launched build on both platforms.
- [ ] If the player count changes, update `LobbyManager.maxPlayers` (it is also the Steam lobby's member limit).

## Changing the App ID

Update `SteamManager.AppId` and `steam_appid.txt` in the project root.
