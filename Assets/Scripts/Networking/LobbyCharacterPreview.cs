using UnityEngine;
using Unity.Netcode;

public class LobbyCharacterPreview : MonoBehaviour
{
    [Header("Preview Slots")]
    [SerializeField] private Transform hostSlot;   // Right side (Slot 0)
    [SerializeField] private Transform clientSlot; // Left side (Slot 1)

    [Header("Preview Prefabs / Models (Optional, defaults to procedural dummy if null)")]
    [SerializeField] private GameObject hostPreviewPrefab;
    [SerializeField] private GameObject clientPreviewPrefab;

    private GameObject activeHostPreview;
    private GameObject activeClientPreview;

    private void Start()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerJoinedSlot += HandlePlayerJoined;
            LobbyManager.Instance.OnPlayerLeftSlot += HandlePlayerLeft;
            LobbyManager.Instance.OnLobbyStateChanged += HandleLobbyStateChanged;
        }
        UpdatePreviews();
    }

    private void Update()
    {
        UpdatePreviews();
    }

    private void OnDestroy()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerJoinedSlot -= HandlePlayerJoined;
            LobbyManager.Instance.OnPlayerLeftSlot -= HandlePlayerLeft;
            LobbyManager.Instance.OnLobbyStateChanged -= HandleLobbyStateChanged;
        }
    }

    private void HandlePlayerJoined(ulong clientId) => UpdatePreviews();
    private void HandlePlayerLeft(ulong clientId) => UpdatePreviews();
    private void HandleLobbyStateChanged() => UpdatePreviews();

    private void UpdatePreviews()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            ClearPreviews();
            return;
        }

        bool isHost = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer;
        bool isClient = NetworkManager.Singleton.IsClient && !isHost;

        // 1. Host Preview (Right side - Slot 0)
        if (hostSlot != null)
        {
            if (activeHostPreview == null && (isHost || isClient))
            {
                if (hostPreviewPrefab != null)
                {
                    activeHostPreview = Instantiate(hostPreviewPrefab, hostSlot.position, hostSlot.rotation, hostSlot);
                }
                else
                {
                    activeHostPreview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    activeHostPreview.transform.SetPositionAndRotation(hostSlot.position, hostSlot.rotation);
                    activeHostPreview.transform.SetParent(hostSlot);
                    var renderer = activeHostPreview.GetComponent<Renderer>();
                    if (renderer != null) renderer.material.color = Color.blue;
                    Collider col = activeHostPreview.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }
            }
        }

        // 2. Client Preview (Left side - Slot 1)
        bool clientConnected = false;
        if (NetworkManager.Singleton.ConnectedClientsList != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId != 0)
                {
                    clientConnected = true;
                    break;
                }
            }
        }

        if (isClient || clientConnected)
        {
            if (activeClientPreview == null && clientSlot != null)
            {
                if (clientPreviewPrefab != null)
                {
                    activeClientPreview = Instantiate(clientPreviewPrefab, clientSlot.position, clientSlot.rotation, clientSlot);
                }
                else
                {
                    activeClientPreview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    activeClientPreview.transform.SetPositionAndRotation(clientSlot.position, clientSlot.rotation);
                    activeClientPreview.transform.SetParent(clientSlot);
                    var renderer = activeClientPreview.GetComponent<Renderer>();
                    if (renderer != null) renderer.material.color = Color.red;
                    Collider col = activeClientPreview.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }
            }
        }
        else
        {
            if (activeClientPreview != null)
            {
                Destroy(activeClientPreview);
                activeClientPreview = null;
            }
        }
    }

    private void ClearPreviews()
    {
        if (activeHostPreview != null)
        {
            Destroy(activeHostPreview);
            activeHostPreview = null;
        }
        if (activeClientPreview != null)
        {
            Destroy(activeClientPreview);
            activeClientPreview = null;
        }
    }
}
