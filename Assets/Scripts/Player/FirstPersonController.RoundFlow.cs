using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;
using Image = UnityEngine.UI.Image;

public partial class FirstPersonController
{
    private void HandlePlayerDefeatServer(ulong winningClientId)
    {
        if (!IsServer)
        {
            return;
        }

        playerCanMove = false;
        StopRigidbodyMotion();

        if (!IsSpawned)
        {
            return;
        }

        DisableAllPlayerControlsAfterDeath();
        DisableAllPlayerControlsAfterDeathClientRpc();
        int winningScore = AddRoundWin(winningClientId);
        SetCardsUiRoundForAll(winningClientId, OwnerClientId);

        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);
        int[] roundCardPoolIndexes = winnerController != null ? winnerController.DrawCardsUiRoundIndexes() : new int[0];

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiRoundCards(roundCardPoolIndexes);
        }

        UpdatePostMatchUiClientRpc(winningClientId, roundCardPoolIndexes, winningScore);

        if (winnerController != null)
        {
            winnerController.BroadcastCardsUiCardSelection(0);
        }

        SwitchToWinnerCameraOwnerClientRpc(winningClientId, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        });
    }

    private void DisableAllPlayerControlsAfterDeath()
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        ClearActiveBullets();

        foreach (FirstPersonController controller in controllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.DisableControlsAfterDeath();
        }
    }

    private static int AddRoundWin(ulong winningClientId)
    {
        int winningScore = 0;
        roundWinsByPlayer.TryGetValue(winningClientId, out winningScore);
        winningScore++;
        roundWinsByPlayer[winningClientId] = winningScore;
        return winningScore;
    }

    private void ActivateScoreboardPoint(ulong winningClientId, int winningScore)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument == null || playerUiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement pointsElement = playerUiDocument.rootVisualElement.Q<VisualElement>(PlayerUiPointsName);
        VisualElement playerElement = GetScoreboardPlayerElement(pointsElement, winningClientId);

        if (playerElement == null)
        {
            return;
        }

        List<VisualElement> pointBackgrounds = playerElement.Query<VisualElement>(name: PlayerUiPointBackgroundName).ToList();
        int activatedPoints = 0;

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point == null)
            {
                continue;
            }

            if (point.style.display == DisplayStyle.Flex)
            {
                activatedPoints++;
            }
        }

        if (activatedPoints >= winningScore)
        {
            return;
        }

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point != null && point.style.display != DisplayStyle.Flex)
            {
                point.style.display = DisplayStyle.Flex;
                break;
            }
        }
    }

    private void ActivateScoreboardPointForPlayerIndex(int playerIndex, int winningScore)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument == null || playerUiDocument.rootVisualElement == null || winningScore <= 0)
        {
            return;
        }

        VisualElement pointsElement = playerUiDocument.rootVisualElement.Q<VisualElement>(PlayerUiPointsName);
        VisualElement playerElement = pointsElement != null
            ? pointsElement.Q<VisualElement>(playerIndex == 0 ? PlayerUiPlayer1Name : PlayerUiPlayer2Name)
            : null;

        if (playerElement == null)
        {
            return;
        }

        List<VisualElement> pointBackgrounds = playerElement.Query<VisualElement>(name: PlayerUiPointBackgroundName).ToList();
        int activatedPoints = 0;

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point != null && point.style.display == DisplayStyle.Flex)
            {
                activatedPoints++;
            }
        }

        int pointsToActivate = Mathf.Min(winningScore, pointBackgrounds.Count);

        for (int i = activatedPoints; i < pointsToActivate; i++)
        {
            VisualElement point = pointBackgrounds[i].Q<VisualElement>(PlayerUiPointName);

            if (point != null)
            {
                point.style.display = DisplayStyle.Flex;
            }
        }
    }

    private void RefreshScoreboard()
    {
        ActivateScoreboardPointForPlayerIndex(0, scoreboardPlayer1Score);
        ActivateScoreboardPointForPlayerIndex(1, scoreboardPlayer2Score);
    }

    private VisualElement GetScoreboardPlayerElement(VisualElement pointsElement, ulong winningClientId)
    {
        if (pointsElement == null)
        {
            return null;
        }

        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        int playerIndex = GetScoreboardPlayerIndex(controllers, winningClientId);

        return pointsElement.Q<VisualElement>(playerIndex == 0 ? PlayerUiPlayer1Name : PlayerUiPlayer2Name);
    }

    private int GetScoreboardPlayerIndex(FirstPersonController[] controllers, ulong playerClientId)
    {
        List<FirstPersonController> spawnedControllers = new List<FirstPersonController>();

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null && controller.IsSpawned)
            {
                spawnedControllers.Add(controller);
            }
        }

        spawnedControllers.Sort((first, second) => first.OwnerClientId.CompareTo(second.OwnerClientId));
        return spawnedControllers.FindIndex(controller => controller.OwnerClientId == playerClientId);
    }

    private void ClearActiveBullets()
    {
        Bullet[] bullets = FindObjectsOfType<Bullet>(true);

        foreach (Bullet bullet in bullets)
        {
            if (bullet == null)
            {
                continue;
            }

            if (bullet.TryGetComponent<NetworkObject>(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                networkObject.Despawn();
            }
            else
            {
                Destroy(bullet.gameObject);
            }
        }
    }

    [ClientRpc]
    private void DisableAllPlayerControlsAfterDeathClientRpc()
    {
        DisableAllPlayerControlsAfterDeath();
    }

    private void DisableControlsAfterDeath()
    {
        controlsDisabledAfterDeath = true;
        moveXInput = 0f;
        moveYInput = 0f;
        isWalking = false;
        isSprinting = false;
        isReloading = false;
        StopRigidbodyMotion();
    }

    [ClientRpc]
    private void UpdatePostMatchUiClientRpc(ulong winningClientId, int[] roundCardPoolIndexes, int winningScore)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        bool isLosingClient = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == OwnerClientId;
        int winningPlayerIndex = GetScoreboardPlayerIndex(controllers, winningClientId);

        if (winningPlayerIndex == 0)
        {
            scoreboardPlayer1Score = winningScore;
        }
        else if (winningPlayerIndex == 1)
        {
            scoreboardPlayer2Score = winningScore;
        }

        SetCardsUiRoundForAll(winningClientId, OwnerClientId);
        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiRoundCards(roundCardPoolIndexes);
        }

        if (isLosingClient)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        foreach (FirstPersonController controller in controllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.SetPlayerUiActive(false);

            bool isWinner = controller.IsSpawned && controller.OwnerClientId == winningClientId;
            controller.SetCardsUiActive(isWinner, isWinner && isLosingClient);
        }

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.RefreshScoreboard();
            }
        }

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiCardSelection(0);
        }
    }

    private void SetCardsUiRoundForAll(ulong winningClientId, ulong losingClientId)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.SetCardsUiRound(winningClientId, losingClientId);
            }
        }
    }

    private void SetCardsUiRound(ulong winningClientId, ulong losingClientId)
    {
        bool isNewRound = cardsUiWinningClientId != winningClientId || cardsUiLosingClientId != losingClientId;

        cardsUiWinningClientId = winningClientId;
        cardsUiLosingClientId = losingClientId;

        if (isNewRound)
        {
            selectedCardsUiCardIndex = -1;
            flippedCardsUiCardIndexes.Clear();
            appliedCardsUiCardIndexes.Clear();
        }
    }

    private int[] DrawCardsUiRoundIndexes()
    {
        List<int> availableIndexes = new List<int>();

        if (cardPool != null)
        {
            for (int i = 0; i < cardPool.Length; i++)
            {
                if (cardPool[i] != null)
                {
                    availableIndexes.Add(i);
                }
            }
        }

        if (availableIndexes.Count == 0)
        {
            Debug.LogWarning("CardsUI card pool is empty. No cards will be shown this round.", this);
            return new int[0];
        }

        int cardCount = Mathf.Min(CardsUiCardsPerRound, availableIndexes.Count);
        int[] roundIndexes = new int[cardCount];

        for (int i = 0; i < cardCount; i++)
        {
            int availableIndex = Random.Range(0, availableIndexes.Count);
            roundIndexes[i] = availableIndexes[availableIndex];
            availableIndexes.RemoveAt(availableIndex);
        }

        return roundIndexes;
    }

    private void ApplyCardsUiRoundCards(int[] cardPoolIndexes)
    {
        for (int i = 0; i < currentRoundCards.Length; i++)
        {
            currentRoundCards[i] = null;
        }

        if (cardPoolIndexes != null)
        {
            int cardsToApply = Mathf.Min(CardsUiCardsPerRound, cardPoolIndexes.Length);

            for (int i = 0; i < cardsToApply; i++)
            {
                int cardPoolIndex = cardPoolIndexes[i];

                if (cardPool != null && cardPoolIndex >= 0 && cardPoolIndex < cardPool.Length)
                {
                    currentRoundCards[i] = cardPool[cardPoolIndex];
                }
                else
                {
                    Debug.LogWarning($"CardsUI received invalid card pool index {cardPoolIndex}.", this);
                }
            }
        }

        PopulateCardsUiFromRoundCards();
    }

    [ClientRpc]
    private void SwitchToWinnerCameraOwnerClientRpc(ulong winningClientId, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        ConfigureLocalView();

        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);

        if (winnerController == null || winnerController == this)
        {
            return;
        }

        winnerController.SetSpectatorCameraActive(true);
    }

    private FirstPersonController FindPlayerByOwnerClientId(ulong ownerClientId)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null && controller.IsSpawned && controller.OwnerClientId == ownerClientId)
            {
                return controller;
            }
        }

        return null;
    }

    private void SetSpectatorCameraActive(bool isActive)
    {
        if (playerCamera == null)
        {
            return;
        }

        playerCamera.gameObject.SetActive(isActive);

        if (isActive)
        {
            SetExclusiveAudioListeners(playerCamera);
        }
        else
        {
            SetAudioListenersEnabled(playerCamera, false);
        }
    }

    private static void SetExclusiveAudioListeners(Camera activeCamera)
    {
        if (activeCamera == null)
        {
            return;
        }

        AudioListener[] allListeners = FindObjectsOfType<AudioListener>(true);

        foreach (AudioListener audioListener in allListeners)
        {
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }
        }

        SetAudioListenersEnabled(activeCamera, true);
    }

    private static void SetAudioListenersEnabled(Camera cameraSource, bool isEnabled)
    {
        if (cameraSource == null)
        {
            return;
        }

        foreach (AudioListener audioListener in cameraSource.GetComponentsInChildren<AudioListener>(true))
        {
            audioListener.enabled = isEnabled;
        }
    }

    private void ResolveMatchUiDocuments()
    {
        if (playerUiDocument != null && cardsUiDocument != null)
        {
            if (healthUiDocument == null)
            {
                healthUiDocument = playerUiDocument;
            }

            return;
        }

        UIDocument[] uiDocuments = GetComponentsInChildren<UIDocument>(true);

        foreach (UIDocument uiDocument in uiDocuments)
        {
            if (uiDocument == null)
            {
                continue;
            }

            if (playerUiDocument == null && uiDocument.gameObject.name.Equals("PlayerUI", System.StringComparison.OrdinalIgnoreCase))
            {
                playerUiDocument = uiDocument;
            }

            if (cardsUiDocument == null && uiDocument.gameObject.name.Equals("CardsUI", System.StringComparison.OrdinalIgnoreCase))
            {
                cardsUiDocument = uiDocument;
            }
        }

        if (healthUiDocument == null)
        {
            healthUiDocument = playerUiDocument;
        }
    }

    private void ResetLevelAfterCardPick()
    {
        if (!IsServer)
        {
            return;
        }

        // Check if we should switch scenes
        bool shouldSwitchScene = mapPoolSceneIndices != null && mapPoolSceneIndices.Length > 0
            && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null;

        if (shouldSwitchScene)
        {
            int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
            List<int> availableScenes = new List<int>();

            for (int i = 0; i < mapPoolSceneIndices.Length; i++)
            {
                int sceneIdx = mapPoolSceneIndices[i];
                if (sceneIdx >= 0 && sceneIdx < SceneManager.sceneCountInBuildSettings
                    && sceneIdx != currentSceneIndex)
                {
                    availableScenes.Add(sceneIdx);
                }
            }

            if (availableScenes.Count > 0)
            {
                // Player objects persist across a single-mode scene load (NGO moves them to DontDestroyOnLoad),
                // so OnNetworkSpawn does not run again. Players are reset once every client finished loading.
                int chosen = availableScenes[UnityEngine.Random.Range(0, availableScenes.Count)];
                StartCoroutine(LoadSceneDelayed(chosen));
                return;
            }
        }

        ResetAllPlayersForNextRound();
    }

    private static void ResetAllPlayersForNextRound()
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.ResetPlayerForNextRound();
            }
        }
    }

    private IEnumerator LoadSceneDelayed(int sceneIndex)
    {
        // Wait a frame to ensure respawn state is synced
        yield return null;

        // Sweep the transition over the screen first; clients cover when the networked Load event arrives.
        bool covered = false;
        TransitionManager.CoverThenNetworked(() => covered = true);
        yield return new WaitUntil(() => covered);

        Debug.Log($"[MapPool] Switching to scene index {sceneIndex}.", this);
        ClearActiveBullets();

        string scenePath = SceneUtility.GetScenePathByBuildIndex(sceneIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"[MapPool] Loading scene '{sceneName}' for all clients.", this);
            NetworkSceneManager networkSceneManager = NetworkManager.Singleton.SceneManager;
            networkSceneManager.OnLoadEventCompleted += OnMapPoolSceneLoadEventCompleted;
            SceneEventProgressStatus loadStatus = networkSceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            Debug.Log($"[MapPool] Scene load status: {loadStatus}", this);

            if (loadStatus != SceneEventProgressStatus.Started)
            {
                networkSceneManager.OnLoadEventCompleted -= OnMapPoolSceneLoadEventCompleted;
                TransitionManager.Reveal();
                ResetAllPlayersForNextRound();
            }
        }
        else
        {
            Debug.LogError($"[MapPool] Failed to get scene name for index {sceneIndex}", this);
            TransitionManager.Reveal();
            ResetAllPlayersForNextRound();
        }
    }

    private static void OnMapPoolSceneLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnMapPoolSceneLoadEventCompleted;
        }

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            Debug.LogWarning($"[MapPool] Clients timed out loading '{sceneName}': {string.Join(", ", clientsTimedOut)}");
        }

        // All clients are now in the new scene, so reset RPCs reach every player.
        ResetAllPlayersForNextRound();
    }

    private void ResetPlayerForNextRound()
    {
        controlsDisabledAfterDeath = false;
        playerCanMove = true;
        isWalking = false;
        isSprinting = false;
        isReloading = false;
        isSprintCooldown = false;
        moveXInput = 0f;
        moveYInput = 0f;
        sprintRemaining = sprintDuration;
        bulletsInMagazine = magazineSize;
        ResetBlockState();
        ClearStatusEffects();
        currentHealth.Value = Mathf.Max(1f, maxHealth);
        SetRagdollState(false);

        if (SpawnPoint.TryGetSpawnPoint((int)OwnerClientId, out Vector3 pos, out Quaternion rot))
        {
            spawnPosition = pos;
            transform.position = pos;
            transform.rotation = rot;
        }

        ApplyRespawnPosition(spawnPosition);
        ClearCardsUiRoundState();
        ResetAnimatorPlaybackSpeed();

        if (IsSpawned)
        {
            ResetPlayerForNextRoundClientRpc(spawnPosition, currentHealth.Value);
        }
    }

    [ClientRpc]
    private void ResetPlayerForNextRoundClientRpc(Vector3 resetPosition, float resetHealth)
    {
        controlsDisabledAfterDeath = false;
        playerCanMove = true;
        isWalking = false;
        isSprinting = false;
        isReloading = false;
        isSprintCooldown = false;
        moveXInput = 0f;
        moveYInput = 0f;
        sprintRemaining = sprintDuration;
        bulletsInMagazine = magazineSize;
        ResetBlockState();
        ClearStatusEffects();
        ApplyRespawnPosition(resetPosition);
        ApplyRagdollState(false);
        ClearCardsUiRoundState();
        ResetAnimatorPlaybackSpeed();
        ConfigureLocalView();

        if (!enableMultiplayerAuthority || !IsSpawned || IsOwner)
        {
            ForceLocalRoundView();
        }

        ResolveHealthUi();
        UpdateHealthBar(resetHealth);
    }

    private void ForceLocalRoundView()
    {
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
            SetExclusiveAudioListeners(playerCamera);
        }

        SetPlayerUiActive(true);
        SetCardsUiActive(false);
        RefreshScoreboard();

        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            canvas.gameObject.SetActive(true);
        }

        foreach (UIDocument uiDocument in GetComponentsInChildren<UIDocument>(true))
        {
            if (uiDocument == playerUiDocument || uiDocument == cardsUiDocument)
            {
                continue;
            }

            uiDocument.gameObject.SetActive(true);
        }

        ApplySecondPlayerBodyMaterial();
        ApplyBodyShadowMode(true);

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ApplyRespawnPosition(Vector3 respawnPosition)
    {
        transform.position = respawnPosition;
        StopRigidbodyMotion();
    }
}
