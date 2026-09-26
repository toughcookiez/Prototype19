using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs before NetworkManager so a duplicate scene copy can be destroyed
// before NGO initializes its transport and grabs a UDP socket.
[DefaultExecutionOrder(-10000)]
public class PersistentNetworkManager : MonoBehaviour
{
    private void Awake()
    {
        NetworkManager localNetworkManager = GetComponent<NetworkManager>();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton != localNetworkManager)
        {
            // The duplicate's transport may live on a separate GameObject; remove it as well.
            NetworkTransport duplicateTransport = GetTransport(localNetworkManager);

            if (duplicateTransport != null && duplicateTransport.gameObject != gameObject
                && duplicateTransport != GetTransport(NetworkManager.Singleton))
            {
                DestroyImmediate(duplicateTransport.gameObject);
            }

            // DestroyImmediate prevents the sibling NetworkManager.Awake from running
            // on this duplicate GameObject, which would otherwise clobber the live UDP socket.
            DestroyImmediate(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);

        // The transport owns the UDP socket. If it sits on its own root GameObject it would be
        // destroyed with the scene on a single-mode scene switch, killing the socket.
        NetworkTransport transport = GetTransport(localNetworkManager);

        if (transport != null && transport.gameObject != gameObject)
        {
            if (transport.transform.parent != null)
            {
                transport.transform.SetParent(null);
            }

            DontDestroyOnLoad(transport.gameObject);
        }
    }

    private static NetworkTransport GetTransport(NetworkManager networkManager)
    {
        return networkManager != null && networkManager.NetworkConfig != null
            ? networkManager.NetworkConfig.NetworkTransport
            : null;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
        TrySubscribeToNetworkSceneEvents();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
        }
    }

    private void TrySubscribeToNetworkSceneEvents()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetworkSceneEvent;
        }
    }

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Belt-and-suspenders: kill any duplicate that slipped past the Awake guard.
        CleanupDuplicateNetworkManagers();
        // NGO's SceneManager instance may only be available after the network session is up.
        TrySubscribeToNetworkSceneEvents();
    }

    private void OnNetworkSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete
            || sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted)
        {
            CleanupDuplicateNetworkManagers();
        }
    }

    private static void CleanupDuplicateNetworkManagers()
    {
        NetworkManager[] managers = FindObjectsOfType<NetworkManager>(true);

        foreach (NetworkManager networkManager in managers)
        {
            if (networkManager != null && networkManager != NetworkManager.Singleton)
            {
                NetworkTransport duplicateTransport = GetTransport(networkManager);

                if (duplicateTransport != null && duplicateTransport.gameObject != networkManager.gameObject
                    && duplicateTransport != GetTransport(NetworkManager.Singleton))
                {
                    Destroy(duplicateTransport.gameObject);
                }

                Destroy(networkManager.gameObject);
            }
        }
    }
}
