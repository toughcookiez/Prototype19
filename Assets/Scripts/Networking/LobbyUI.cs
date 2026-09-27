using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LobbyUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject waitingRoomPanel;

    [Header("Main Menu Controls")]
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button joinLobbyButton;
    [SerializeField] private TMP_InputField ipAddressInputField;

    [Header("Waiting Room Controls")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private TMP_Text statusText;

    private void Start()
    {
        if (createLobbyButton != null)
            createLobbyButton.onClick.AddListener(OnCreateLobbyClicked);

        if (joinLobbyButton != null)
            joinLobbyButton.onClick.AddListener(OnJoinLobbyClicked);

        if (startGameButton != null)
            startGameButton.onClick.AddListener(OnStartGameClicked);

        if (leaveLobbyButton != null)
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyClicked);

        if (ipAddressInputField != null && string.IsNullOrEmpty(ipAddressInputField.text))
            ipAddressInputField.text = "127.0.0.1";

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStateChanged += UpdateUIState;
        }

        UpdateUIState();
    }

    private void OnDestroy()
    {
        if (createLobbyButton != null) createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);
        if (joinLobbyButton != null) joinLobbyButton.onClick.RemoveListener(OnJoinLobbyClicked);
        if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGameClicked);
        if (leaveLobbyButton != null) leaveLobbyButton.onClick.RemoveListener(OnLeaveLobbyClicked);

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStateChanged -= UpdateUIState;
        }
    }

    private void OnCreateLobbyClicked()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.StartHostLobby();
        }
    }

    private void OnJoinLobbyClicked()
    {
        if (LobbyManager.Instance != null)
        {
            string ip = ipAddressInputField != null ? ipAddressInputField.text : "127.0.0.1";
            LobbyManager.Instance.StartClientLobby(ip);
        }
    }

    private void OnStartGameClicked()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.StartGame();
        }
    }

    private void OnLeaveLobbyClicked()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.LeaveLobby();
        }
    }

    private void UpdateUIState()
    {
        bool isConnected = LobbyManager.Instance != null && LobbyManager.Instance.IsConnected;
        bool isHost = LobbyManager.Instance != null && LobbyManager.Instance.IsHost;

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(!isConnected);

        if (waitingRoomPanel != null)
            waitingRoomPanel.SetActive(isConnected);

        if (startGameButton != null)
            startGameButton.gameObject.SetActive(isHost);

        if (statusText != null)
        {
            if (isConnected)
            {
                statusText.text = isHost ? "Hosting Lobby... Waiting for players." : "Joined Lobby! Waiting for host to start.";
            }
            else
            {
                statusText.text = "Disconnected.";
            }
        }
    }
}
