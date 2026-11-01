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
    private void SetPlayerUiActive(bool isActive)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument != null)
        {
            playerUiDocument.gameObject.SetActive(isActive);
        }

        if (isActive)
        {
            ResolveHealthUi();
            UpdateHealthBar(currentHealth.Value);
        }
        else
        {
            healthBarProgressElement = null;
        }
    }

    private void SetCardsUiActive(bool isActive, bool isInteractable = false)
    {
        ResolveMatchUiDocuments();

        if (cardsUiDocument == null)
        {
            return;
        }

        cardsUiDocument.gameObject.SetActive(isActive);

        if (cardsUiDocument.rootVisualElement == null)
        {
            cardsUiWasActive = isActive;
            return;
        }

        if (isActive && !cardsUiWasActive)
        {
            ResetCardsUiRound();
        }

        cardsUiDocument.rootVisualElement.pickingMode = PickingMode.Position;
        cardsUiDocument.rootVisualElement.SetEnabled(true);
        SetCardsUiInputBlocked(isActive && !isInteractable);
        UpdateCardsUiPlayerColorLabel(isActive);
        PopulateCardsUiFromRoundCards();
        cardsUiWasActive = isActive;
    }

    // Names the player who is choosing a card, colored to match their side.
    // Player index 0 is RED and index 1 is BLUE, matching the scoreboard in PlayerUI.uxml.
    private void UpdateCardsUiPlayerColorLabel(bool isActive)
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        Label colorLabel = cardsUiDocument.rootVisualElement.Q<Label>(CardsUiPlayerColorLabelName);

        if (colorLabel == null)
        {
            return;
        }

        if (!isActive)
        {
            colorLabel.text = CardsUiPlayerRedText;
            colorLabel.style.color = CardsUiPlayerRedColor;
            return;
        }

        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        int chooserIndex = GetScoreboardPlayerIndex(controllers, cardsUiLosingClientId);
        bool isRed = chooserIndex != 1;

        colorLabel.text = isRed ? CardsUiPlayerRedText : CardsUiPlayerBlueText;
        colorLabel.style.color = isRed ? CardsUiPlayerRedColor : CardsUiPlayerBlueColor;
    }

    private void PopulateCardsUiFromRoundCards()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();

        for (int i = 0; i < cards.Count; i++)
        {
            VisualElement cardElement = cards[i];
            bool hasRoundCard = i < CardsUiCardsPerRound && currentRoundCards[i] != null;

            if (cardElement == null)
            {
                continue;
            }

            cardElement.transform.position = Vector3.zero;
            SetCardsUiCardScale(cardElement, 1f);
            cardElement.style.display = hasRoundCard ? DisplayStyle.Flex : DisplayStyle.None;
            cardElement.SetEnabled(hasRoundCard);
            RestoreCardsUiCardBackSprite(cardElement);

            if (!hasRoundCard)
            {
                continue;
            }

            PopulateCardsUiCardInfo(cardElement, currentRoundCards[i]);
        }
    }

    private void PopulateCardsUiCardInfo(VisualElement cardElement, Card cardData)
    {
        VisualElement infoElement = cardElement.Q<VisualElement>(CardsUiInfoElementName);

        if (infoElement == null)
        {
            Debug.LogWarning($"CardsUI card '{cardElement.name}' is missing an Info child VisualElement.", this);
            return;
        }

        infoElement.Clear();
        infoElement.AddToClassList(CardsUiInfoClassName);
        infoElement.style.display = DisplayStyle.None;

        if (!string.IsNullOrWhiteSpace(cardData.cardName))
        {
            Label titleLabel = new Label(cardData.cardName);
            titleLabel.AddToClassList(CardsUiTitleClassName);
            infoElement.Add(titleLabel);
        }

        if (cardData.abilities != null)
        {
            foreach (CardAbility ability in cardData.abilities)
            {
                AddCardsUiAbilityLabels(infoElement, ability);
            }
        }

        if (cardData.stats != null)
        {
            foreach (CardStat stat in cardData.stats)
            {
                AddCardsUiStatLabel(infoElement, stat);
            }
        }
    }

    private void AddCardsUiStatLabel(VisualElement infoElement, CardStat stat)
    {
        string description = GetCardStatDescription(stat);

        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        VisualElement statRow = new VisualElement();
        statRow.AddToClassList(CardsUiStatRowClassName);

        string keyword = stat.keyword;
        int keywordIndex = !string.IsNullOrEmpty(keyword) ? description.IndexOf(keyword, System.StringComparison.Ordinal) : -1;

        Label statLabel;

        if (keywordIndex < 0)
        {
            // No keyword - simple label
            statLabel = new Label(description);
            statLabel.AddToClassList(CardsUiStatTextClassName);
        }
        else
        {
            // Use rich text to format keyword within a single label
            string textBeforeKeyword = description.Substring(0, keywordIndex);
            string textAfterKeyword = description.Substring(keywordIndex + keyword.Length);

            // Determine color based on polarity (convert RGB to hex)
            string colorHex = stat.polarity == CardStatPolarity.Good ? "#229640" : "#be2a2a";

            // Build rich text string
            string richText = $"{textBeforeKeyword}<color={colorHex}><b>{keyword}</b></color>{textAfterKeyword}";

            statLabel = new Label(richText);
            statLabel.AddToClassList(CardsUiStatTextClassName);
        }

        statRow.Add(statLabel);
        infoElement.Add(statRow);
    }

    private string GetCardStatDescription(CardStat stat)
    {
        if (!string.IsNullOrWhiteSpace(stat.description))
        {
            return stat.description;
        }

        string sign = stat.amount > 0f ? "+" : string.Empty;
        string suffix = stat.modifierMode == CardStatModifierMode.Percent ? "%" : string.Empty;
        return $"{sign}{stat.amount}{suffix} {FormatCardStatTargetName(stat.target)}";
    }

    private string FormatCardStatTargetName(CardStatTarget target)
    {
        string targetName = target.ToString();
        string formattedName = string.Empty;

        for (int i = 0; i < targetName.Length; i++)
        {
            char character = targetName[i];

            if (i > 0 && char.IsUpper(character))
            {
                formattedName += " ";
            }

            formattedName += character;
        }

        return formattedName;
    }

    private void AddCardsUiInlineLabel(VisualElement parent, string text, string className)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Label label = new Label(text);
        label.AddToClassList(className);
        parent.Add(label);
    }

    private void AddCardsUiAbilityLabels(VisualElement infoElement, CardAbility ability)
    {
        if (!string.IsNullOrWhiteSpace(ability.description))
        {
            Label abilityDescriptionLabel = new Label(ability.description);
            abilityDescriptionLabel.AddToClassList(CardsUiAbilityDescriptionClassName);
            infoElement.Add(abilityDescriptionLabel);
        }
    }

    private void ProcessCardsUiKeyboardSelection()
    {
        if (!CanLocalClientInteractWithCardsUi()
            || cardsUiDocument == null
            || !cardsUiDocument.gameObject.activeInHierarchy
            || animatingCardsUiCards.Count > 0
            || isCardsUiPickAnimationPlaying)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            int cardIndexToPick = selectedCardsUiCardIndex >= 0 ? selectedCardsUiCardIndex : 0;
            isCardsUiPickAnimationPlaying = true;
            RequestCardsUiCardPickServerRpc(cardIndexToPick);
            return;
        }

        int direction = 0;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            direction = -1;
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            direction = 1;
        }

        if (direction == 0)
        {
            return;
        }

        int cardCount = GetCardsUiCardCount();

        if (cardCount <= 0)
        {
            return;
        }

        int currentIndex = selectedCardsUiCardIndex >= 0 ? selectedCardsUiCardIndex : 0;
        int nextIndex = Mathf.Clamp(currentIndex + direction, 0, cardCount - 1);

        if (nextIndex != selectedCardsUiCardIndex)
        {
            RequestCardsUiCardSelectionServerRpc(nextIndex);
        }
    }

    private void ResetCardsUiRound()
    {
        flippedCardsUiCards.Clear();
        flippedCardsUiCardIndexes.Clear();
        animatingCardsUiCards.Clear();
        selectedCardsUiCardIndex = -1;
        isCardsUiPickAnimationPlaying = false;

        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        int cardsToReset = Mathf.Min(CardsUiCardsPerRound, cards.Count);

        for (int i = 0; i < cardsToReset; i++)
        {
            VisualElement card = cards[i];

            if (card == null)
            {
                continue;
            }

            card.transform.position = Vector3.zero;
            SetCardsUiCardScale(card, 1f);
            RestoreCardsUiCardBackSprite(card);

            VisualElement infoElement = card.Q<VisualElement>(CardsUiInfoElementName);

            if (infoElement != null)
            {
                infoElement.style.display = DisplayStyle.None;
            }
        }
    }

    private bool CanLocalClientInteractWithCardsUi()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.LocalClientId == cardsUiLosingClientId
            && OwnerClientId == cardsUiWinningClientId;
    }

    private int GetCardsUiCardCount()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return 0;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        int cardsToCheck = Mathf.Min(CardsUiCardsPerRound, cards.Count);
        int cardCount = 0;

        for (int i = 0; i < cardsToCheck; i++)
        {
            if (currentRoundCards[i] != null)
            {
                cardCount++;
            }
        }

        return cardCount;
    }

    private VisualElement GetCardsUiCardByIndex(int cardIndex)
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return null;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();

        if (cardIndex < 0 || cardIndex >= Mathf.Min(CardsUiCardsPerRound, cards.Count) || currentRoundCards[cardIndex] == null)
        {
            return null;
        }

        return cards[cardIndex];
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCardsUiCardSelectionServerRpc(int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != cardsUiLosingClientId || OwnerClientId != cardsUiWinningClientId)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= CardsUiCardsPerRound || currentRoundCards[cardIndex] == null)
        {
            return;
        }

        BroadcastCardsUiCardSelection(cardIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCardsUiCardPickServerRpc(int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != cardsUiLosingClientId || OwnerClientId != cardsUiWinningClientId)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= currentRoundCards.Length || currentRoundCards[cardIndex] == null || appliedCardsUiCardIndexes.Contains(cardIndex))
        {
            return;
        }

        appliedCardsUiCardIndexes.Add(cardIndex);
        FirstPersonController losingController = FindPlayerByOwnerClientId(cardsUiLosingClientId);

        if (losingController != null)
        {
            losingController.ApplyPickedCard(currentRoundCards[cardIndex]);
        }

        BroadcastCardsUiCardPick(cardIndex);
    }

    private void ClearCardsUiRoundState()
    {
        cardsUiWinningClientId = ulong.MaxValue;
        cardsUiLosingClientId = ulong.MaxValue;
        selectedCardsUiCardIndex = -1;
        cardsUiWasActive = false;
        isCardsUiPickAnimationPlaying = false;
        flippedCardsUiCards.Clear();
        flippedCardsUiCardIndexes.Clear();
        appliedCardsUiCardIndexes.Clear();
        animatingCardsUiCards.Clear();

        for (int i = 0; i < currentRoundCards.Length; i++)
        {
            currentRoundCards[i] = null;
        }

        ResetCardsUiCardTransforms();
        SetCardsUiActive(false);
    }

    private void ResetCardsUiCardTransforms()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            VisualElement card = cards[i];
            if (card != null)
            {
                card.transform.position = Vector3.zero;
                SetCardsUiCardScale(card, 1f);
            }
        }
    }

    private void BroadcastCardsUiCardSelection(int cardIndex)
    {
        ApplyCardsUiCardSelection(cardIndex);

        if (IsSpawned)
        {
            ApplyCardsUiCardSelectionClientRpc(cardIndex);
        }
    }

    [ClientRpc]
    private void ApplyCardsUiCardSelectionClientRpc(int cardIndex)
    {
        ApplyCardsUiCardSelection(cardIndex);
    }

    private void BroadcastCardsUiCardPick(int cardIndex)
    {
        PlayCardsUiCardPickAnimation(cardIndex);

        if (IsSpawned)
        {
            PlayCardsUiCardPickAnimationClientRpc(cardIndex);
        }
    }

    [ClientRpc]
    private void PlayCardsUiCardPickAnimationClientRpc(int cardIndex)
    {
        if (IsServer)
        {
            return;
        }

        PlayCardsUiCardPickAnimation(cardIndex);
    }

    private void PlayCardsUiCardPickAnimation(int cardIndex)
    {
        isCardsUiPickAnimationPlaying = true;
        if (cardsUiDocument != null && cardsUiDocument.rootVisualElement != null)
        {
            SetCardsUiInputBlocked(true);
        }
        StartCoroutine(AnimateCardsUiPickSequence(cardIndex));
    }

    private IEnumerator AnimateCardsUiPickSequence(int chosenIndex)
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            isCardsUiPickAnimationPlaying = false;
            if (IsServer)
            {
                ResetLevelAfterCardPick();
            }
            yield break;
        }

        VisualElement chosenCard = GetCardsUiCardByIndex(chosenIndex);
        List<VisualElement> otherCards = new List<VisualElement>();
        int totalCards = GetCardsUiCardCount();

        for (int i = 0; i < totalCards; i++)
        {
            if (i != chosenIndex)
            {
                VisualElement otherCard = GetCardsUiCardByIndex(i);
                if (otherCard != null)
                {
                    otherCards.Add(otherCard);
                }
            }
        }

        // 1. Chosen Card Animation: Gets a little bigger and then scales down to nothing.
        if (chosenCard != null)
        {
            animatingCardsUiCards.Add(chosenCard);
            EnableCardsUiCardInfo(chosenCard);

            float startScale = chosenCard.transform.scale.x > 0.01f ? chosenCard.transform.scale.x : CardsUiSelectedScale;
            float popScale = CardsUiPickGrowScale;

            // Phase 1A: Grow slightly bigger
            float elapsedGrow = 0f;
            while (elapsedGrow < CardsUiPickGrowDuration)
            {
                float t = elapsedGrow / CardsUiPickGrowDuration;
                float currentScale = Mathf.SmoothStep(startScale, popScale, t);
                SetCardsUiCardScale(chosenCard, currentScale);
                elapsedGrow += Time.unscaledDeltaTime;
                yield return null;
            }
            SetCardsUiCardScale(chosenCard, popScale);

            // Phase 1B: Scale down to nothing
            float elapsedShrink = 0f;
            while (elapsedShrink < CardsUiPickShrinkDuration)
            {
                float t = elapsedShrink / CardsUiPickShrinkDuration;
                float currentScale = Mathf.Lerp(popScale, 0f, t * t);
                SetCardsUiCardScale(chosenCard, currentScale);
                elapsedShrink += Time.unscaledDeltaTime;
                yield return null;
            }
            SetCardsUiCardScale(chosenCard, 0f);
            chosenCard.style.display = DisplayStyle.None;
            animatingCardsUiCards.Remove(chosenCard);
        }

        // 2. Other Cards Move Down and Out of the Screen afterwards
        if (otherCards.Count > 0)
        {
            foreach (VisualElement card in otherCards)
            {
                animatingCardsUiCards.Add(card);
            }

            float rootHeight = cardsUiDocument.rootVisualElement.layout.height > 0
                ? cardsUiDocument.rootVisualElement.layout.height
                : Screen.height;
            float dropDistance = Mathf.Max(rootHeight, Screen.height) + 500f;

            float elapsedDrop = 0f;
            while (elapsedDrop < CardsUiDismissOthersDuration)
            {
                float t = elapsedDrop / CardsUiDismissOthersDuration;
                // Quadratic ease-in to simulate accelerating downwards off screen
                float currentY = Mathf.Lerp(0f, dropDistance, t * t);

                foreach (VisualElement card in otherCards)
                {
                    card.transform.position = new Vector3(0f, currentY, 0f);
                }

                elapsedDrop += Time.unscaledDeltaTime;
                yield return null;
            }

            foreach (VisualElement card in otherCards)
            {
                card.transform.position = new Vector3(0f, dropDistance, 0f);
                card.style.display = DisplayStyle.None;
                animatingCardsUiCards.Remove(card);
            }
        }

        if (CardsUiPostPickDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(CardsUiPostPickDelay);
        }

        isCardsUiPickAnimationPlaying = false;

        if (IsServer)
        {
            ResetLevelAfterCardPick();
        }
    }

    private void ApplyCardsUiCardSelection(int cardIndex)
    {
        VisualElement selectedCard = GetCardsUiCardByIndex(cardIndex);

        if (selectedCard == null)
        {
            selectedCardsUiCardIndex = cardIndex;
            return;
        }

        selectedCardsUiCardIndex = cardIndex;
        ApplyCardsUiCardScales();

        if (!flippedCardsUiCardIndexes.Contains(cardIndex) && !animatingCardsUiCards.Contains(selectedCard))
        {
            StartCoroutine(FlipCardsUiCard(selectedCard, cardIndex));
        }
    }

    private void ApplyCardsUiCardScales()
    {
        int cardCount = GetCardsUiCardCount();

        for (int i = 0; i < cardCount; i++)
        {
            VisualElement card = GetCardsUiCardByIndex(i);

            if (card == null || animatingCardsUiCards.Contains(card))
            {
                continue;
            }

            SetCardsUiCardScale(card, i == selectedCardsUiCardIndex ? CardsUiSelectedScale : 1f);
        }
    }

    private IEnumerator FlipCardsUiCard(VisualElement card, int cardIndex)
    {
        animatingCardsUiCards.Add(card);

        float halfDuration = Mathf.Max(.01f, CardsUiFlipDuration * .5f);
        float targetScale = cardIndex == selectedCardsUiCardIndex ? CardsUiSelectedScale : 1f;

        yield return LerpCardsUiCardXScale(card, targetScale, 0f, targetScale, halfDuration);
        EnableCardsUiCardInfo(card);
        yield return LerpCardsUiCardXScale(card, 0f, targetScale, targetScale, halfDuration);

        animatingCardsUiCards.Remove(card);
        flippedCardsUiCards.Add(card);
        flippedCardsUiCardIndexes.Add(cardIndex);
        ApplyCardsUiCardScales();
    }

    private IEnumerator LerpCardsUiCardXScale(VisualElement card, float fromScale, float toScale, float yScale, float duration)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            float normalizedTime = elapsedTime / duration;
            float xScale = Mathf.Lerp(fromScale, toScale, normalizedTime);
            card.transform.scale = new Vector3(xScale, yScale, 1f);
            elapsedTime += Time.unscaledDeltaTime;
            yield return null;
        }

        card.transform.scale = new Vector3(toScale, yScale, 1f);
    }

    private void SetCardsUiCardScale(VisualElement card, float scale)
    {
        card.transform.scale = new Vector3(scale, scale, 1f);
    }

    private void EnableCardsUiCardInfo(VisualElement card)
    {
        VisualElement infoElement = card.Q<VisualElement>(CardsUiInfoElementName);

        if (infoElement != null)
        {
            infoElement.style.display = DisplayStyle.Flex;
            infoElement.SetEnabled(true);
        }

        SetCardsUiCardFlippedSprite(card);
    }

    // Swaps the card artwork to the flipped sprite at the same moment the card info is revealed.
    private void SetCardsUiCardFlippedSprite(VisualElement card)
    {
        if (card == null || flippedCardSprite == null)
        {
            return;
        }

        CacheCardsUiCardBackBackground(card);
        card.style.backgroundImage = new StyleBackground(Background.FromSprite(flippedCardSprite));
    }

    // Restores the face-down artwork, used when the cards are repopulated for a new round.
    private void RestoreCardsUiCardBackSprite(VisualElement card)
    {
        if (card == null)
        {
            return;
        }

        if (!cardsUiCardBackBackgrounds.TryGetValue(card, out Background backBackground))
        {
            return;
        }

        card.style.backgroundImage = new StyleBackground(backBackground);
    }

    private void CacheCardsUiCardBackBackground(VisualElement card)
    {
        if (card == null || cardsUiCardBackBackgrounds.ContainsKey(card))
        {
            return;
        }

        // The face-down artwork is set as an inline background image in CardsUI.uxml.
        cardsUiCardBackBackgrounds.Add(card, card.style.backgroundImage.value);
    }

    private void SetCardsUiInputBlocked(bool isBlocked)
    {
        VisualElement root = cardsUiDocument.rootVisualElement;
        VisualElement blocker = root.Q<VisualElement>(CardsUiInputBlockerName);

        if (!isBlocked)
        {
            blocker?.RemoveFromHierarchy();
            return;
        }

        if (blocker == null)
        {
            blocker = new VisualElement
            {
                name = CardsUiInputBlockerName,
                pickingMode = PickingMode.Position
            };

            blocker.style.position = Position.Absolute;
            blocker.style.left = 0;
            blocker.style.right = 0;
            blocker.style.top = 0;
            blocker.style.bottom = 0;
            blocker.style.backgroundColor = Color.clear;

            blocker.RegisterCallback<PointerDownEvent>(eventBase => eventBase.StopImmediatePropagation());
            blocker.RegisterCallback<PointerUpEvent>(eventBase => eventBase.StopImmediatePropagation());
            blocker.RegisterCallback<ClickEvent>(eventBase => eventBase.StopImmediatePropagation());

            root.Add(blocker);
        }

        blocker.BringToFront();
    }
}
