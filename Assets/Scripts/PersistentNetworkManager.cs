using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures NetworkManager persists across scene loads and prevents duplicate NetworkManagers in loaded scenes.
/// </summary>
public class PersistentNetworkManager : MonoBehaviour
{
    private void Awake()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton != GetComponent<NetworkManager>())
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
        }
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        // Clean up duplicate NetworkManagers after scene loads
        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete)
        {
            foreach (NetworkManager networkManager in FindObjectsOfType<NetworkManager>(true))
            {
                if (networkManager != NetworkManager.Singleton)
                {
                    Destroy(networkManager.gameObject);
                }
            }
        }
    }
}
