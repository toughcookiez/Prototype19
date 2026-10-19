using System;
using Steamworks;
using UnityEngine;

// Steam matchmaking lobby used to find the host: by lobby code search, friend invite,
// or "Join Game" from the Steam friends list / overlay.
public class SteamLobby : IDisposable
{
    private const string CodeKey = "code";
    private const string GameKey = "game";
    private const string GameValue = "Prototype19";
    private const string ConnectLobbyArgument = "+connect_lobby";

    // Raised when this player is inside a lobby they don't own: (host Steam ID, lobby code).
    public event Action<ulong, string> OnJoinedLobbyAsClient;
    // Raised when Steam asks us to join a lobby (invite accepted, "Join Game" clicked).
    public event Action<CSteamID> OnJoinRequested;
    public event Action<string> OnFailed;

    public CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;
    public bool InLobby => CurrentLobby != CSteamID.Nil;

    private readonly CallResult<LobbyCreated_t> lobbyCreated;
    private readonly CallResult<LobbyMatchList_t> lobbyMatchList;
    private readonly CallResult<LobbyEnter_t> lobbyEntered;
    private readonly Callback<GameLobbyJoinRequested_t> joinRequested;
    private readonly Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;

    private string pendingCode;

    public SteamLobby()
    {
        lobbyCreated = CallResult<LobbyCreated_t>.Create(HandleLobbyCreated);
        lobbyMatchList = CallResult<LobbyMatchList_t>.Create(HandleLobbyMatchList);
        lobbyEntered = CallResult<LobbyEnter_t>.Create(HandleLobbyEntered);
        joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => OnJoinRequested?.Invoke(r.m_steamIDLobby));
        richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(r =>
        {
            if (TryParseConnectLobby(r.m_rgchConnect, out CSteamID lobby))
            {
                OnJoinRequested?.Invoke(lobby);
            }
        });
    }

    // Lobby the game was launched to join (friend clicked "Join Game" while we weren't running).
    public static bool TryGetLaunchLobby(out CSteamID lobby)
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == ConnectLobbyArgument && ulong.TryParse(args[i + 1], out ulong id))
            {
                lobby = new CSteamID(id);
                return true;
            }
        }

        SteamApps.GetLaunchCommandLine(out string commandLine, 1024);
        return TryParseConnectLobby(commandLine, out lobby);
    }

    private static bool TryParseConnectLobby(string connect, out CSteamID lobby)
    {
        lobby = CSteamID.Nil;

        if (string.IsNullOrEmpty(connect))
        {
            return false;
        }

        string[] parts = connect.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int index = Array.IndexOf(parts, ConnectLobbyArgument);

        if (index >= 0 && index < parts.Length - 1 && ulong.TryParse(parts[index + 1], out ulong id))
        {
            lobby = new CSteamID(id);
            return true;
        }

        return false;
    }

    public void Create(string code, int maxPlayers)
    {
        pendingCode = code;
        // Public so the code search can find it; NGO connection approval still checks the code.
        lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxPlayers));
    }

    public void JoinByCode(string code)
    {
        SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, GameValue, ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListStringFilter(CodeKey, code.ToUpperInvariant(), ELobbyComparison.k_ELobbyComparisonEqual);
        SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
        SteamMatchmaking.AddRequestLobbyListResultCountFilter(1);
        lobbyMatchList.Set(SteamMatchmaking.RequestLobbyList());
    }

    public void Join(CSteamID lobby)
    {
        lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
    }

    public void SetJoinable(bool joinable)
    {
        if (InLobby)
        {
            SteamMatchmaking.SetLobbyJoinable(CurrentLobby, joinable);
        }
    }

    public void OpenInviteOverlay()
    {
        if (InLobby)
        {
            SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        }
    }

    public void Leave()
    {
        if (InLobby)
        {
            SteamMatchmaking.LeaveLobby(CurrentLobby);
            CurrentLobby = CSteamID.Nil;
        }

        SteamFriends.ClearRichPresence();
    }

    private void HandleLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            OnFailed?.Invoke("Could not create Steam lobby: " + (ioFailure ? "IO failure" : result.m_eResult.ToString()));
            return;
        }

        CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(CurrentLobby, GameKey, GameValue);
        SteamMatchmaking.SetLobbyData(CurrentLobby, CodeKey, pendingCode);
        SetRichPresence();
        Debug.Log($"[Steam] Lobby {CurrentLobby} created with code {pendingCode}.");
    }

    private void HandleLobbyMatchList(LobbyMatchList_t result, bool ioFailure)
    {
        if (ioFailure || result.m_nLobbiesMatching == 0)
        {
            OnFailed?.Invoke("No Steam lobby found with that code.");
            return;
        }

        CSteamID lobby = SteamMatchmaking.GetLobbyByIndex(0);
        Debug.Log($"[Steam] Lobby code matched lobby {lobby}, joining.");
        Join(lobby);
    }

    private void HandleLobbyEntered(LobbyEnter_t result, bool ioFailure)
    {
        if (ioFailure || result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            OnFailed?.Invoke("Could not join Steam lobby: " + (ioFailure ? "IO failure" : ((EChatRoomEnterResponse)result.m_EChatRoomEnterResponse).ToString()));
            return;
        }

        CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
        CSteamID owner = SteamMatchmaking.GetLobbyOwner(CurrentLobby);

        if (owner == SteamUser.GetSteamID())
        {
            return;
        }

        SetRichPresence();
        string code = SteamMatchmaking.GetLobbyData(CurrentLobby, CodeKey);
        Debug.Log($"[Steam] Entered lobby {CurrentLobby}, host {owner}.");
        OnJoinedLobbyAsClient?.Invoke(owner.m_SteamID, code);
    }

    // Lets friends use "Join Game" on us from the Steam friends list.
    private void SetRichPresence()
    {
        SteamFriends.SetRichPresence("connect", ConnectLobbyArgument + " " + CurrentLobby.m_SteamID);
    }

    public void Dispose()
    {
        Leave();
        lobbyCreated.Dispose();
        lobbyMatchList.Dispose();
        lobbyEntered.Dispose();
        joinRequested.Dispose();
        richPresenceJoinRequested.Dispose();
    }
}
