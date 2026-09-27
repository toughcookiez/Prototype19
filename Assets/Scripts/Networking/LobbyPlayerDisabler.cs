using UnityEngine;
using Unity.Netcode;

public class LobbyPlayerDisabler : NetworkBehaviour
{
    private FirstPersonController fpController;
    private Camera playerCamera;
    private Rigidbody rb;
    private Collider playerCollider;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        fpController = GetComponent<FirstPersonController>();
        playerCamera = GetComponentInChildren<Camera>(true);
        rb = GetComponent<Rigidbody>();
        playerCollider = GetComponent<Collider>();

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStarted += DisablePlayerControls;
            LobbyManager.Instance.OnLobbyEnded += EnablePlayerControls;

            if (LobbyManager.Instance.IsLobbyActive)
            {
                DisablePlayerControls();
            }
        }

        // Ensure player prefab spawns off camera in the lobby
        if (IsOwner && LobbyManager.Instance != null && LobbyManager.Instance.IsLobbyActive)
        {
            transform.position = new Vector3(0f, -100f, 0f);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStarted -= DisablePlayerControls;
            LobbyManager.Instance.OnLobbyEnded -= EnablePlayerControls;
        }
    }

    private void OnDestroy()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStarted -= DisablePlayerControls;
            LobbyManager.Instance.OnLobbyEnded -= EnablePlayerControls;
        }
    }

    private void DisablePlayerControls()
    {
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

        // Move off camera
        transform.position = new Vector3(0f, -100f, 0f);
    }

    private void EnablePlayerControls()
    {
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
