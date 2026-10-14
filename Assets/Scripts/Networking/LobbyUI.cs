using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using TMPro;

using UGUIButton = UnityEngine.UI.Button;
using UITKButton = UnityEngine.UIElements.Button;

public class LobbyUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject waitingRoomPanel;

    [Header("Main Menu Controls")]
    [SerializeField] private UGUIButton createLobbyButton;
    [SerializeField] private UGUIButton joinLobbyButton;
    [SerializeField] private TMP_InputField ipAddressInputField;

    [Header("Waiting Room Controls")]
    [SerializeField] private UGUIButton startGameButton;
    [SerializeField] private UGUIButton leaveLobbyButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text lobbyCodeText;
    [SerializeField] private UGUIButton copyCodeButton;

    [Header("Camera Animation")]
    [SerializeField] private Camera lobbyCamera;
    [SerializeField] private float rotationDuration = 0.5f;

    [Header("UI Toolkit Root (UIDocument assigned by user)")]
    [SerializeField] private UIDocument uiDocument;

    private UITKButton toolkitCreateButton;
    private UITKButton toolkitJoinButton;
    private UITKButton toolkitStartGameButton;
    private UITKButton toolkitLeaveLobbyButton;
    private Label toolkitLobbyCodeLabel;
    private UITKButton toolkitCopyCodeButton;
    private Label toolkitStatusText;
    private VisualElement toolkitMainMenuContainer;
    private VisualElement toolkitWaitingRoomContainer;

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

        if (copyCodeButton != null)
            copyCodeButton.onClick.AddListener(OnCopyCodeClicked);

        if (ipAddressInputField != null && string.IsNullOrEmpty(ipAddressInputField.text))
            ipAddressInputField.text = "127.0.0.1";

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStateChanged += UpdateUIState;
        }

        if (uiDocument != null)
        {
            SetupUIToolkit(uiDocument.rootVisualElement);
        }

        UpdateUIState();
    }

    private void OnDestroy()
    {
        if (createLobbyButton != null) createLobbyButton.onClick.RemoveListener(OnCreateLobbyClicked);
        if (joinLobbyButton != null) joinLobbyButton.onClick.RemoveListener(OnJoinLobbyClicked);
        if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGameClicked);
        if (leaveLobbyButton != null) leaveLobbyButton.onClick.RemoveListener(OnLeaveLobbyClicked);
        if (copyCodeButton != null) copyCodeButton.onClick.RemoveListener(OnCopyCodeClicked);

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyStateChanged -= UpdateUIState;
        }
    }

    private void OnCreateLobbyClicked()
    {
        if (LobbyManager.Instance != null)
        {
            RotateCamera();
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (waitingRoomPanel != null) waitingRoomPanel.SetActive(true);

            // UI Toolkit visuals
            if (toolkitMainMenuContainer != null) toolkitMainMenuContainer.style.display = DisplayStyle.None;
            if (toolkitWaitingRoomContainer != null) toolkitWaitingRoomContainer.style.display = DisplayStyle.Flex;

            LobbyManager.Instance.StartHostLobby();
        }
    }

    private void OnJoinLobbyClicked()
    {
        if (LobbyManager.Instance != null)
        {
            RotateCamera();
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (waitingRoomPanel != null) waitingRoomPanel.SetActive(true);
            string ip = ipAddressInputField != null ? ipAddressInputField.text : "127.0.0.1";
            LobbyManager.Instance.StartClientLobby(ip);
        }
    }

    private void OnCopyCodeClicked()
    {
        if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.LobbyCode))
        {
            GUIUtility.systemCopyBuffer = LobbyManager.Instance.LobbyCode;
            Debug.Log("Lobby code copied to clipboard: " + LobbyManager.Instance.LobbyCode);
        }
    }

    private void RotateCamera()
    {
        Camera targetCam = lobbyCamera != null ? lobbyCamera : Camera.main;
        if (targetCam != null)
        {
            StartCoroutine(LerpCameraRotation(targetCam));
        }
    }

    private System.Collections.IEnumerator LerpCameraRotation(Camera targetCam)
    {
        Quaternion startRot = targetCam.transform.rotation;
        Quaternion targetRot = Quaternion.Euler(startRot.eulerAngles.x, startRot.eulerAngles.y + 180f, startRot.eulerAngles.z);
        float elapsed = 0f;

        while (elapsed < rotationDuration)
        {
            targetCam.transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / rotationDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        targetCam.transform.rotation = targetRot;
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

        if (lobbyCodeText != null)
        {
            if (isConnected && isHost && LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.LobbyCode))
            {
                lobbyCodeText.gameObject.SetActive(true);
                lobbyCodeText.text = "Lobby Code: " + LobbyManager.Instance.LobbyCode;
            }
            else
            {
                lobbyCodeText.gameObject.SetActive(false);
            }
        }

        if (copyCodeButton != null)
        {
            copyCodeButton.gameObject.SetActive(isConnected && isHost);
        }
        UpdateUIToolkitState();
    }

    public void SetupUIToolkit(VisualElement root)
    {
        if (root == null) return;

        toolkitCreateButton = root.Query<UITKButton>("CreateLobby");
        toolkitJoinButton = root.Query<UITKButton>("JoinLobby");
        toolkitStartGameButton = root.Query<UITKButton>("StartGame");
        toolkitLeaveLobbyButton = root.Query<UITKButton>("LeaveLobby");
        toolkitLobbyCodeLabel = root.Query<Label>("LobbyCode");
        toolkitCopyCodeButton = root.Query<UITKButton>("CopyLobbyCode");
        toolkitStatusText = root.Query<Label>("Status");
        toolkitMainMenuContainer = root.Query<VisualElement>("MainMenu");
        if (toolkitMainMenuContainer == null) toolkitMainMenuContainer = root.Query<VisualElement>("MainMenuPanel");
        toolkitWaitingRoomContainer = root.Query<VisualElement>("WaitingRoom");
        if (toolkitWaitingRoomContainer == null) toolkitWaitingRoomContainer = root.Query<VisualElement>("WaitingRoomPanel");

        if (toolkitCreateButton != null)
            toolkitCreateButton.clicked += () => OnCreateLobbyClicked();
        if (toolkitJoinButton != null)
            toolkitJoinButton.clicked += () => OnJoinLobbyClicked();
        if (toolkitStartGameButton != null)
            toolkitStartGameButton.clicked += () => OnStartGameClicked();
        if (toolkitLeaveLobbyButton != null)
            toolkitLeaveLobbyButton.clicked += () => OnLeaveLobbyClicked();
        if (toolkitCopyCodeButton != null)
            toolkitCopyCodeButton.clicked += () => OnCopyCodeClicked();

        UpdateUIToolkitState();
    }

    private void UpdateUIToolkitState()
    {
        bool isConnected = LobbyManager.Instance != null && LobbyManager.Instance.IsConnected;
        bool isHost = LobbyManager.Instance != null && LobbyManager.Instance.IsHost;

        if (toolkitMainMenuContainer != null)
            toolkitMainMenuContainer.style.display = !isConnected ? DisplayStyle.Flex : DisplayStyle.None;

        if (toolkitWaitingRoomContainer != null)
            toolkitWaitingRoomContainer.style.display = isConnected ? DisplayStyle.Flex : DisplayStyle.None;

        if (toolkitStatusText != null)
        {
            toolkitStatusText.text = isConnected ? (isHost ? "Hosting Lobby... Waiting for players." : "Joined Lobby! Waiting for host to start.") : "Disconnected.";
        }

        if (toolkitLobbyCodeLabel != null)
        {
            bool show = isConnected && isHost && LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.LobbyCode);
            toolkitLobbyCodeLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) toolkitLobbyCodeLabel.text = "Lobby Code: " + LobbyManager.Instance.LobbyCode;
        }

        if (toolkitCopyCodeButton != null)
        {
            toolkitCopyCodeButton.style.display = (isConnected && isHost) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (toolkitStartGameButton != null)
        {
            toolkitStartGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

}
