using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private string gameplaySceneName = "ThatLevel";

    public event Action<ulong> OnPlayerJoinedSlot;
    public event Action<ulong> OnPlayerLeftSlot;
    public event Action OnLobbyStateChanged;
    public event Action OnLobbyStarted;
    public event Action OnLobbyEnded;

    public bool IsLobbyActive { get; private set; }
    public string LobbyCode { get; private set; }

    private Dictionary<ulong, int> clientSlots = new Dictionary<ulong, int>();

    private const string LobbyCodeChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int LobbyCodeLength = 6;

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
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        string receivedCode = Encoding.UTF8.GetString(request.Payload);
        bool approved = !string.IsNullOrEmpty(LobbyCode) && string.Equals(receivedCode, LobbyCode, StringComparison.OrdinalIgnoreCase);

        response.Approved = approved;
        response.CreatePlayerObject = true;
        response.Pending = false;

        Debug.Log($"Lobby code validation for client {request.ClientNetworkId}: {(approved ? "APPROVED" : "REJECTED")}");
    }

    public void StartHostLobby()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
            IsLobbyActive = true;
            LobbyCode = GenerateLobbyCode();
            NetworkManager.Singleton.StartHost();
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
        if (NetworkManager.Singleton != null)
        {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            PendingLobbyCode = lobbyCode;

            // Store the lobby code in connection data so the server can validate it
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(lobbyCode);
            IsLobbyActive = true;
            NetworkManager.Singleton.StartClient();
        }
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
        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }
        clientSlots.Clear();
        IsLobbyActive = false;
        OnLobbyStateChanged?.Invoke();
        OnLobbyEnded?.Invoke();
    }

    public void StartGame()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            IsLobbyActive = false;
            OnLobbyEnded?.Invoke();
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
            IsLobbyActive = false;
            PendingLobbyCode = null;
            OnLobbyStateChanged?.Invoke();
            OnLobbyEnded?.Invoke();
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
    public bool IsConnected => NetworkManager.Singleton != null &&
        (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient);
}
