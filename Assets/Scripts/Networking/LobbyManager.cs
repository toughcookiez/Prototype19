using System;
using System.Collections.Generic;
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

    private Dictionary<ulong, int> clientSlots = new Dictionary<ulong, int>();

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
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    public void StartHostLobby()
    {
        if (NetworkManager.Singleton != null)
        {
            IsLobbyActive = true;
            NetworkManager.Singleton.StartHost();
            OnLobbyStateChanged?.Invoke();
            OnLobbyStarted?.Invoke();
        }
    }

    public void StartClientLobby(string ipAddress)
    {
        if (NetworkManager.Singleton != null)
        {
            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null && !string.IsNullOrEmpty(ipAddress))
            {
                transport.ConnectionData.Address = ipAddress;
            }
            IsLobbyActive = true;
            NetworkManager.Singleton.StartClient();
            OnLobbyStateChanged?.Invoke();
            OnLobbyStarted?.Invoke();
        }
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
    }

    private void HandleClientDisconnected(ulong clientId)
    {
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
    public bool IsConnected => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
}
