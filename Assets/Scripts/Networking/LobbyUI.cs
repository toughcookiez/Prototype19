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
    [SerializeField] private float rotationAndPositionDuration = 0.5f;

    [Header("UI Toolkit Root (UIDocument assigned by user)")]
    [SerializeField] private UIDocument uiDocument;

    private UITKButton toolkitCreateButton;
    private UITKButton toolkitJoinButton;
    private UITKButton toolkitStartGameButton;
    private UITKButton toolkitLeaveLobbyButton;
    private Label toolkitLobbyCodeLabel;
    private UITKButton toolkitCopyCodeButton;
    private UITKButton toolkitInviteFriendsButton;
    private Label toolkitStatusText;
    private VisualElement toolkitMainMenuContainer;
    private VisualElement toolkitWaitingRoomContainer;
    private TextField toolkitLobbyCodeInput;
    private bool hasRotatedCamera = false;
    private Quaternion initialCameraRot;
    private Vector3 initialCameraPos;
    private bool hasInitialRot = false;
    private bool hasInitialPos = false;

    private void Start()
    {
        Camera targetCam = lobbyCamera != null ? lobbyCamera : Camera.main;
        if (targetCam != null)
        {
            initialCameraRot = targetCam.transform.rotation;
            hasInitialRot = true;
            initialCameraPos = targetCam.transform.position;
            hasInitialPos = true;
        }

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
            LobbyManager.Instance.StartHostLobby();
        }
    }

    private bool IsValidLobbyCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != 6)
            return false;

        foreach (char c in code)
        {
            if (!char.IsLetterOrDigit(c))
                return false;
        }
        return true;
    }

    private void OnJoinLobbyClicked()
    {
        string codeOrIp = string.Empty;
        if (ipAddressInputField != null && !string.IsNullOrEmpty(ipAddressInputField.text) && ipAddressInputField.text != "127.0.0.1")
        {
            codeOrIp = ipAddressInputField.text.Trim();
        }
        else if (toolkitLobbyCodeInput != null && !string.IsNullOrEmpty(toolkitLobbyCodeInput.value))
        {
            codeOrIp = toolkitLobbyCodeInput.value.Trim().ToUpper();
        }

        bool isValidIp = !string.IsNullOrEmpty(codeOrIp) && System.Net.IPAddress.TryParse(codeOrIp, out _);
        bool isValidCode = IsValidLobbyCode(codeOrIp);

        if (!isValidIp && !isValidCode)
        {
            Debug.LogWarning("Cannot join lobby: Please enter a valid 6-character lobby code or IP address.");
            return;
        }

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.StartClientLobby(codeOrIp);
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

    private void RotateCamera(bool forward)
    {
        Camera targetCam = lobbyCamera != null ? lobbyCamera : Camera.main;
        if (targetCam != null)
        {
            //disable the rotation script if it exists
            MainMenuCameraBehaviour cameraBehaviour = targetCam.GetComponentInParent<MainMenuCameraBehaviour>();
            if (cameraBehaviour != null)
            {
                cameraBehaviour.shouldRotate = false;
            }
            
            Quaternion targetRot;
            Vector3 targetPos;
            if (forward)
            {
                targetRot = Quaternion.Euler(targetCam.transform.rotation.eulerAngles.x, targetCam.transform.rotation.eulerAngles.y + 180f, 
                    targetCam.transform.rotation.eulerAngles.z);
                targetPos = new Vector3(targetCam.transform.position.x, targetCam.transform.position.y, targetCam.transform.position.z);
            }
            else
            {
                targetRot = hasInitialRot ? initialCameraRot : Quaternion.Euler(targetCam.transform.rotation.eulerAngles.x, 
                    targetCam.transform.rotation.eulerAngles.y - 180f, targetCam.transform.rotation.eulerAngles.z);
                
                targetPos = hasInitialPos ? initialCameraPos : new Vector3(targetCam.transform.position.x,
                    targetCam.transform.position.y, targetCam.transform.position.z);
            }
            StartCoroutine(LerpCameraRotationAndPosition(targetCam, targetRot, targetPos));
        }
    }

    private System.Collections.IEnumerator LerpCameraRotationAndPosition(Camera targetCam, Quaternion targetRot, Vector3 targetpos)
    {
        Quaternion startRot = targetCam.transform.rotation;
        
        Vector3 startPos = targetCam.transform.position;
        float elapsed = 0f;

        while (elapsed < rotationAndPositionDuration)
        {
            targetCam.transform.rotation = Quaternion.Slerp(startRot, targetRot, elapsed / rotationAndPositionDuration);
            targetCam.transform.position = Vector3.Lerp(startPos, targetpos, elapsed / rotationAndPositionDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        targetCam.transform.rotation = targetRot;
        targetCam.transform.position = targetpos;
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

        if (isConnected && !hasRotatedCamera)
        {
            hasRotatedCamera = true;
            RotateCamera(true);
        }
        else if (!isConnected && hasRotatedCamera)
        {
            hasRotatedCamera = false;
            RotateCamera(false);
        }

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(!isConnected);

        if (waitingRoomPanel != null)
            waitingRoomPanel.SetActive(isConnected);

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);
            startGameButton.interactable = LobbyManager.Instance != null && LobbyManager.Instance.HasEnoughPlayersToStart;
        }

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
        toolkitInviteFriendsButton = root.Query<UITKButton>("InviteFriends");
        toolkitStatusText = root.Query<Label>("Status");
        toolkitLobbyCodeInput = root.Query<TextField>("LobbyCodeInput");
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
        if (toolkitInviteFriendsButton != null)
            toolkitInviteFriendsButton.clicked += () => LobbyManager.Instance?.InviteFriends();

        UpdateUIToolkitState();
    }

    private void UpdateUIToolkitState()
    {
        bool isConnected = LobbyManager.Instance != null && LobbyManager.Instance.IsConnected;
        bool isHost = LobbyManager.Instance != null && LobbyManager.Instance.IsHost;
        bool canStart = LobbyManager.Instance != null && LobbyManager.Instance.HasEnoughPlayersToStart;

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
            if (show) toolkitLobbyCodeLabel.text = "<size=20><color=#EE831A>LOBBY CODE</color></size>\n<size=64>" + LobbyManager.Instance.LobbyCode + "</size>";
        }

        if (toolkitCopyCodeButton != null)
        {
            toolkitCopyCodeButton.style.display = (isConnected && isHost) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (toolkitStartGameButton != null)
        {
            toolkitStartGameButton.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            toolkitStartGameButton.SetEnabled(canStart);
            toolkitStartGameButton.style.opacity = canStart ? 1f : 0.5f;
        }

        if (toolkitInviteFriendsButton != null)
        {
            bool canInvite = isConnected && LobbyManager.Instance != null && LobbyManager.Instance.IsUsingSteam;
            toolkitInviteFriendsButton.style.display = canInvite ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

}
