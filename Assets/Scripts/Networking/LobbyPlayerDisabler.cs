using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class LobbyPlayerDisabler : NetworkBehaviour
{
    private FirstPersonController fpController;
    private Camera playerCamera;
    private Rigidbody rb;
    private Collider playerCollider;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStarted += DisablePlayerControls;
            LobbyManager.Instance.OnLobbyEnded += EnablePlayerControls;
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStarted -= DisablePlayerControls;
            LobbyManager.Instance.OnLobbyEnded -= EnablePlayerControls;
        }
    }

    private void Start()
    {
        fpController = GetComponent<FirstPersonController>();
        playerCamera = GetComponentInChildren<Camera>(true);
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();

        if (LobbyManager.Instance != null && LobbyManager.Instance.IsLobbyActive)
        {
            DisablePlayerControls();
        }
        else
        {
            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.name != "MainMenu" && currentScene.name != "MainMenuScene")
            {
                EnablePlayerControls();
                SpawnAtSpawnPoint();
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When entering a gameplay scene (not the main menu)
        if (scene.name != "MainMenu" && scene.name != "MainMenuScene")
        {
            EnablePlayerControls();
            SpawnAtSpawnPoint();
        }
        else if (LobbyManager.Instance != null && LobbyManager.Instance.IsLobbyActive)
        {
            DisablePlayerControls();
        }
    }

    public void SpawnAtSpawnPoint()
    {
        if (IsOwner)
        {
            if (SpawnPoint.TryGetSpawnPoint((int)OwnerClientId, out Vector3 spawnPos, out Quaternion spawnRot))
            {
                if (rb != null)
                {
                    rb.isKinematic = true;
                    transform.position = spawnPos;
                    transform.rotation = spawnRot;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = false;
                }
                else
                {
                    transform.position = spawnPos;
                    transform.rotation = spawnRot;
                }
            }
        }
    }

    private void DisablePlayerControls()
    {
        if (!IsOwner) return;

        // Unlock cursor so player can interact with UI
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Disable FirstPersonController movement and camera look
        if (fpController != null)
        {
            fpController.playerCanMove = false;
            fpController.cameraCanMove = false;
        }

        // Disable camera
        if (playerCamera != null)
        {
            playerCamera.enabled = false;
        }

        // Disable rigidbody physics/movement
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Disable collider
        if (playerCollider != null)
        {
            playerCollider.enabled = false;
        }

        // Move off camera during lobby
        transform.position = new Vector3(0f, -100f, 0f);
    }

    private void EnablePlayerControls()
    {
        if (!IsOwner) return;

        // Re-enable collider
        if (playerCollider != null)
        {
            playerCollider.enabled = true;
        }

        // Re-enable rigidbody
        if (rb != null)
        {
            rb.isKinematic = false;
        }

        // Re-enable camera
        if (playerCamera != null)
        {
            playerCamera.enabled = true;
        }

        // Re-enable FirstPersonController movement and camera look
        if (fpController != null)
        {
            fpController.playerCanMove = true;
            fpController.cameraCanMove = true;
        }

        // Lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
