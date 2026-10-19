using System;
using Steamworks;
using UnityEngine;

// Owns the Steamworks API lifetime. Created automatically before the first scene loads,
// so any script can check SteamManager.Initialized without scene setup.
[DefaultExecutionOrder(-20000)]
public class SteamManager : MonoBehaviour
{
    public const uint AppId = 3999910;

    // Lets a player opt out of Steam at launch (e.g. "Game.exe -nosteam") to use direct IP instead.
    private const string NoSteamArgument = "-nosteam";

    // Editor-only switch so Multiplayer Play Mode / local IP testing doesn't need a Steam client.
    private const string EditorPrefsUseSteamKey = "Prototype19.UseSteamInEditor";

    public static SteamManager Instance { get; private set; }
    public static bool Initialized => Instance != null && Instance.initialized;
    // Lives here rather than on LobbyManager so the lobby survives gameplay scene loads.
    public static SteamLobby Lobby => Initialized ? Instance.lobby : null;

    private bool initialized;
    private SteamLobby lobby;
    private SteamAPIWarningMessageHook_t warningHook;

#if UNITY_EDITOR
    public static bool UseSteamInEditor
    {
        get => UnityEditor.EditorPrefs.GetBool(EditorPrefsUseSteamKey, true);
        set => UnityEditor.EditorPrefs.SetBool(EditorPrefsUseSteamKey, value);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject(nameof(SteamManager));
        DontDestroyOnLoad(go);
        go.AddComponent<SteamManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!ShouldUseSteam(out string reason))
        {
            Debug.Log($"[Steam] Disabled ({reason}). Falling back to direct IP networking.");
            return;
        }

        if (!Packsize.Test())
        {
            Debug.LogError("[Steam] Packsize test failed: wrong Steamworks.NET binaries for this platform.");
            return;
        }

        if (!DllCheck.Test())
        {
            Debug.LogError("[Steam] DllCheck failed: one or more Steamworks binaries are the wrong version.");
            return;
        }

#if !UNITY_EDITOR
        if (Debug.isDebugBuild)
        {
            // Development builds run outside Steam (no steam_appid.txt needed next to the exe / .app).
            Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
        }
        else
        {
            try
            {
                // Release builds started from the exe directly are relaunched through Steam.
                if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
                {
                    Application.Quit();
                    return;
                }
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError("[Steam] steam_api native library not found: " + e.Message);
                return;
            }
        }
#endif

        initialized = SteamAPI.Init();

        if (!initialized)
        {
            Debug.LogWarning("[Steam] SteamAPI.Init() failed. Is the Steam client running and logged in, and does this account own app "
                + AppId + "? Falling back to direct IP networking.");
            return;
        }

        warningHook = SteamAPIDebugTextHook;
        SteamClient.SetWarningMessageHook(warningHook);

        // Warm up the relay network now so the first P2P connection doesn't stall on it.
        SteamNetworkingUtils.InitRelayNetworkAccess();
        lobby = new SteamLobby();

        Debug.Log($"[Steam] Initialized as {SteamFriends.GetPersonaName()} ({SteamUser.GetSteamID()}), app {SteamUtils.GetAppID()}.");
    }

    private static bool ShouldUseSteam(out string reason)
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), NoSteamArgument) >= 0)
        {
            reason = NoSteamArgument + " argument";
            return false;
        }

#if UNITY_EDITOR
        if (!UseSteamInEditor)
        {
            reason = "Use Steam In Editor is off (Tools > Steam)";
            return false;
        }

        // Virtual players share this machine's single Steam login, so only the main editor uses Steam.
        if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
        {
            reason = "Multiplayer Play Mode virtual player";
            return false;
        }
#endif

        reason = null;
        return true;
    }

    private static void SteamAPIDebugTextHook(int severity, System.Text.StringBuilder text)
    {
        Debug.LogWarning("[Steam] " + text);
    }

    private void Update()
    {
        if (initialized)
        {
            SteamAPI.RunCallbacks();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        Instance = null;

        if (initialized)
        {
            lobby?.Dispose();
            lobby = null;
            initialized = false;
            SteamAPI.Shutdown();
        }
    }
}
