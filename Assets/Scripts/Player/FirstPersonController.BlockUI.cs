using UnityEngine;
using UnityEngine.UIElements;

// Block cooldown indicator, shown next to the ammo dots in the bottom-left HUD corner.
public partial class FirstPersonController
{
    private static readonly Color BlockReadyColor = new Color(0.47f, 0.78f, 1f, 1f);
    private static readonly Color BlockActiveColor = Color.white;
    private static readonly Color BlockChargingColor = new Color(0.27f, 0.43f, 0.55f, 1f);

    private VisualElement blockIndicatorElement;
    private VisualElement blockFillElement;
    private Label blockKeyLabel;
    private int displayedBlockPercent = -1;
    private int displayedBlockState = -1;

    // Polled every frame by the owner; only touches styles when the percentage or state changes.
    private void UpdateBlockUi()
    {
        if (blockIndicatorElement == null || blockIndicatorElement.panel == null
            || healthUiDocument == null || healthUiDocument.rootVisualElement == null
            || blockIndicatorElement.panel != healthUiDocument.rootVisualElement.panel)
        {
            ResolveBlockUi();
        }

        if (blockIndicatorElement == null)
        {
            return;
        }

        bool visible = enableBlock;
        blockIndicatorElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (!visible)
        {
            return;
        }

        int state;
        float fill;

        if (Time.time < blockStartTime + blockDuration)
        {
            state = 2;
            fill = 1f;
        }
        else if (Time.time < nextBlockTime)
        {
            state = 1;
            float remaining = nextBlockTime - Time.time;
            fill = 1f - Mathf.Clamp01(remaining / Mathf.Max(0.05f, blockCooldown));
        }
        else
        {
            state = 0;
            fill = 1f;
        }

        int percent = Mathf.RoundToInt(fill * 100f);
        if (state == displayedBlockState && percent == displayedBlockPercent)
        {
            return;
        }

        displayedBlockState = state;
        displayedBlockPercent = percent;
        blockFillElement.style.height = Length.Percent(percent);
        blockFillElement.style.backgroundColor = state == 2 ? BlockActiveColor : state == 1 ? BlockChargingColor : BlockReadyColor;
    }

    private void ResolveBlockUi()
    {
        blockIndicatorElement = null;
        blockFillElement = null;
        blockKeyLabel = null;
        displayedBlockPercent = -1;
        displayedBlockState = -1;

        if (healthUiDocument == null)
        {
            ResolveHealthUi();
        }

        if (healthUiDocument == null || !healthUiDocument.gameObject.activeInHierarchy || healthUiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement root = healthUiDocument.rootVisualElement;
        blockIndicatorElement = root.Q<VisualElement>("BlockIndicator");
        blockFillElement = root.Q<VisualElement>("BlockFill");
        blockKeyLabel = root.Q<Label>("BlockKey");

        if (blockIndicatorElement == null || blockFillElement == null)
        {
            blockIndicatorElement = null;
            return;
        }

        if (blockKeyLabel != null)
        {
            blockKeyLabel.text = GetBlockKeyLabel();
        }
    }

    private string GetBlockKeyLabel()
    {
        switch (blockKey)
        {
            case KeyCode.Mouse0: return "LMB";
            case KeyCode.Mouse1: return "RMB";
            case KeyCode.Mouse2: return "MMB";
            case KeyCode.LeftShift: return "SHIFT";
            case KeyCode.LeftControl: return "CTRL";
            default: return blockKey.ToString().ToUpperInvariant();
        }
    }
}
