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
            // DestroyImmediate prevents the sibling NetworkManager.Awake from running
            // on this duplicate GameObject, which would otherwise clobber the live UDP socket.
            DestroyImmediate(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
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
                Destroy(networkManager.gameObject);
            }
        }
    }
}
