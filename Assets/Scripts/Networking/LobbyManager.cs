using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private string gameplaySceneName = "ThatLevel";
    [SerializeField] private int maxPlayers = 2;

    public event Action<ulong> OnPlayerJoinedSlot;
    public event Action<ulong> OnPlayerLeftSlot;
    public event Action OnLobbyStateChanged;
    public event Action OnLobbyStarted;
    public event Action OnGameStarted;
    public event Action OnLobbyLeft;

    public bool IsLobbyActive { get; private set; }
    public string LobbyCode { get; private set; }
    public bool IsUsingSteam => SteamManager.Initialized;
    public bool CanInviteFriends => SteamManager.Lobby != null && SteamManager.Lobby.InLobby;

    private Dictionary<ulong, int> clientSlots = new Dictionary<ulong, int>();

    private bool isLeaving = false;

    // The scene's UnityTransport, remembered so we can switch back to it after a Steam session.
    private static UnityTransport unityTransport;

    private const string LobbyCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int LobbyCodeLength = 6;

    // A friend's "Join Game" can launch us straight into a lobby; honor it only once per run.
    private static bool launchLobbyHandled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
        }

        if (SteamManager.Lobby != null)
        {
            SteamManager.Lobby.OnJoinedLobbyAsClient += HandleSteamLobbyJoined;
            SteamManager.Lobby.OnJoinRequested += HandleSteamJoinRequested;
            SteamManager.Lobby.OnFailed += HandleSteamLobbyFailed;
        }
    }

    private void Start()
    {
        if (!launchLobbyHandled && SteamManager.Lobby != null)
        {
            launchLobbyHandled = true;

            if (SteamLobby.TryGetLaunchLobby(out CSteamID lobby))
            {
                HandleSteamJoinRequested(lobby);
            }
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            // ConnectionApprovalCallback is a property, not an event, so we can't -= in the same way.
            // But we can set it to null if needed, or leave it if the NetworkManager handles it.
        }

        if (SteamManager.Lobby != null)
        {
            SteamManager.Lobby.OnJoinedLobbyAsClient -= HandleSteamLobbyJoined;
            SteamManager.Lobby.OnJoinRequested -= HandleSteamJoinRequested;
            SteamManager.Lobby.OnFailed -= HandleSteamLobbyFailed;
        }
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        string receivedCode = Encoding.UTF8.GetString(request.Payload);
        bool codeMatches = !string.IsNullOrEmpty(LobbyCode) && string.Equals(receivedCode, LobbyCode, StringComparison.OrdinalIgnoreCase);
        // Joining by typed IP address can't know the code; reaching the host's IP is the "secret" there.
        bool directIpJoin = !IsUsingSteam && string.IsNullOrEmpty(receivedCode);
        bool hasFreeSlot = NetworkManager.Singleton.ConnectedClientsIds.Count < maxPlayers;
        bool isLocalHost = request.ClientNetworkId == NetworkManager.ServerClientId;
        bool approved = isLocalHost || ((codeMatches || directIpJoin) && hasFreeSlot);

        response.Reason = approved ? null : !hasFreeSlot ? "Lobby is full." : "Wrong lobby code.";

        response.Approved = approved;
        response.CreatePlayerObject = true;
        response.Pending = false;

        Debug.Log($"Lobby code validation for client {request.ClientNetworkId}: {(approved ? "APPROVED" : "REJECTED")}");
    }

    public void StartHostLobby()
    {
        isLeaving = false;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            IsLobbyActive = true;
            LobbyCode = GenerateLobbyCode();
            SelectTransport(useSteam: IsUsingSteam);

            if (!NetworkManager.Singleton.StartHost())
            {
                Debug.LogError("Failed to start host.");
                LeaveLobby();
                return;
            }

            SteamManager.Lobby?.Create(LobbyCode, maxPlayers);
            OnLobbyStateChanged?.Invoke();
            OnLobbyStarted?.Invoke();
        }
    }

    private string GenerateLobbyCode()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < LobbyCodeLength; i++)
        {
            sb.Append(LobbyCodeChars[UnityEngine.Random.Range(0, LobbyCodeChars.Length)]);
        }
        return sb.ToString();
    }

    public void StartClientLobby(string lobbyCode)
    {
        isLeaving = false;
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (System.Net.IPAddress.TryParse(lobbyCode, out _))
        {
            SelectTransport(useSteam: false);
            UnityTransport ipTransport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            ipTransport.SetConnectionData(lobbyCode, ipTransport.ConnectionData.Port);
            StartClient(string.Empty);
        }
        else if (IsUsingSteam)
        {
            // The Steam lobby tells us who the host is; we connect once we're inside it.
            IsLobbyActive = true;
            SteamManager.Lobby.JoinByCode(lobbyCode);
        }
        else
        {
            SelectTransport(useSteam: false);
            StartClient(lobbyCode);
        }
    }

    private void StartClient(string lobbyCode)
    {
        PendingLobbyCode = lobbyCode;

        // Must match the host: the flag is part of NGO's config hash, and without it the code isn't sent at all.
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;

        // Store the lobby code in connection data so the server can validate it
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(lobbyCode);
        IsLobbyActive = true;

        if (!NetworkManager.Singleton.StartClient())
        {
            Debug.LogError("Failed to start client.");
            LeaveLobby();
        }
    }

    private void HandleSteamLobbyJoined(ulong hostSteamId, string lobbyCode)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            return;
        }

        SelectTransport(useSteam: true).ConnectToSteamId = hostSteamId;
        StartClient(lobbyCode ?? string.Empty);
    }

    private void HandleSteamJoinRequested(CSteamID lobby)
    {
        if (IsConnected || IsLobbyActive)
        {
            LeaveLobby();
        }

        isLeaving = false;
        IsLobbyActive = true;
        SteamManager.Lobby.Join(lobby);
    }

    private void HandleSteamLobbyFailed(string message)
    {
        Debug.LogWarning("[Steam] " + message);

        // Hosting still works for direct invites by IP if lobby creation failed; only a failed join resets.
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
        {
            IsLobbyActive = false;
            OnLobbyStateChanged?.Invoke();
        }
    }

    public void InviteFriends()
    {
        SteamManager.Lobby?.OpenInviteOverlay();
    }

    // Steam is used when available; UnityTransport (direct IP) stays on the NetworkManager as fallback.
    private SteamNetworkingSocketsTransport SelectTransport(bool useSteam)
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (unityTransport == null)
        {
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        }

        SteamNetworkingSocketsTransport steamTransport = networkManager.GetComponent<SteamNetworkingSocketsTransport>();

        if (useSteam)
        {
            if (steamTransport == null)
            {
                steamTransport = networkManager.gameObject.AddComponent<SteamNetworkingSocketsTransport>();
            }

            networkManager.NetworkConfig.NetworkTransport = steamTransport;
        }
        else if (unityTransport != null)
        {
            networkManager.NetworkConfig.NetworkTransport = unityTransport;
        }

        return steamTransport;
    }

    public string PendingLobbyCode { get; private set; }

    public bool TryValidateLobbyCode(string clientCode)
    {
        if (string.IsNullOrEmpty(PendingLobbyCode))
            return false;

        // Codes must exactly match
        return string.Equals(clientCode, PendingLobbyCode, StringComparison.OrdinalIgnoreCase);
    }

    public void LeaveLobby()
    {
        isLeaving = true;
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        SteamManager.Lobby?.Leave();
        clientSlots.Clear();
        IsLobbyActive = false;
        LobbyCode = null;
        PendingLobbyCode = null;

        if (SceneManager.GetActiveScene().name != "MainMenu")
        {
            SceneManager.LoadScene("MainMenu");
        }

        OnLobbyStateChanged?.Invoke();
        OnLobbyLeft?.Invoke();
    }

    public void StartGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            IsLobbyActive = false;
            SteamManager.Lobby?.SetJoinable(false);
            OnGameStarted?.Invoke();
            NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        int assignedSlot = (clientId == 0) ? 0 : 1; // Server/Host is Slot 0 (Right), Client is Slot 1 (Left)

        clientSlots[clientId] = assignedSlot;
        Debug.Log($"Client connected: {clientId}, assigned to slot {assignedSlot}");
        OnPlayerJoinedSlot?.Invoke(clientId);
        OnLobbyStateChanged?.Invoke();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && clientId == NetworkManager.Singleton.LocalClientId)
        {
            OnLobbyStarted?.Invoke();
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            isLeaving = true;
            IsLobbyActive = false;
            LobbyCode = null;
            PendingLobbyCode = null;
            SteamManager.Lobby?.Leave();

            if (!string.IsNullOrEmpty(NetworkManager.Singleton.DisconnectReason))
            {
                Debug.LogWarning("Disconnected: " + NetworkManager.Singleton.DisconnectReason);
            }

            if (SceneManager.GetActiveScene().name != "MainMenu")
            {
                SceneManager.LoadScene("MainMenu");
            }

            OnLobbyStateChanged?.Invoke();
            OnLobbyLeft?.Invoke();
        }

        if (clientSlots.ContainsKey(clientId))
        {
            clientSlots.Remove(clientId);
            Debug.Log($"Client disconnected: {clientId}");
            OnPlayerLeftSlot?.Invoke(clientId);
            OnLobbyStateChanged?.Invoke();
        }
    }

    public int GetClientSlot(ulong clientId)
    {
        return clientSlots.TryGetValue(clientId, out int slot) ? slot : -1;
    }

    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    public bool IsConnected => !isLeaving && NetworkManager.Singleton != null &&
        (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient);
}
