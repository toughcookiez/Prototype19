using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class FirstPersonController
{
    private const string AmmoBarElementName = "AmmoBar";
    // Spacing scales with ammoDotSize so the bar keeps its proportions at any size.
    private const float AmmoDotSpacingRatio = 0.3f;
    private const float AmmoRowSpacingRatio = 0.65f;
    private static readonly Color AmmoDotLitColor = new Color(1f, 0.55f, 0.08f, 1f);
    private static readonly Color AmmoDotSpentColor = new Color(0.08f, 0.063f, 0.047f, 1f);

    private VisualElement ammoBarElement;
    private readonly List<VisualElement> ammoDots = new List<VisualElement>();
    private bool ammoBarWasReloading;
    private float ammoReloadStartTime;
    private int ammoReloadStartBullets;
    private int displayedAmmoCount = -1;
    private int displayedMagazineSize = -1;
    private int displayedDotsPerRow = -1;
    private float displayedDotSize = -1f;

    // Polled every frame by the owner. Only touches the UI when the magazine size or bullet count changed,
    // so shooting, reloading, round resets and card-driven magazine changes are all covered in one place.
    private void UpdateAmmoBar()
    {
        if (ammoBarElement == null || ammoBarElement.panel == null
            || healthUiDocument == null || healthUiDocument.rootVisualElement == null
            || ammoBarElement.panel != healthUiDocument.rootVisualElement.panel)
        {
            ResolveAmmoBar();
        }

        if (ammoBarElement == null)
        {
            return;
        }

        int magazine = Mathf.Max(1, magazineSize);
        int bullets = Mathf.Clamp(bulletsInMagazine, 0, magazine);

        if (isReloading)
        {
            if (!ammoBarWasReloading)
            {
                ammoBarWasReloading = true;
                ammoReloadStartTime = Time.time;
                ammoReloadStartBullets = bullets;
            }

            // Light the missing dots one by one, evenly spaced across the reload time.
            int missing = Mathf.Max(0, magazine - ammoReloadStartBullets);
            float progress = Mathf.Clamp01((Time.time - ammoReloadStartTime) / Mathf.Max(.01f, reloadTime));
            bullets = Mathf.Min(magazine, ammoReloadStartBullets + Mathf.Min(missing, Mathf.FloorToInt(progress * (missing + 1))));
        }
        else
        {
            ammoBarWasReloading = false;
        }

        float dotSize = Mathf.Max(1f, ammoDotSize);

        if (magazine == displayedMagazineSize && bullets == displayedAmmoCount
            && Mathf.Max(1, ammoDotsPerRow) == displayedDotsPerRow && Mathf.Approximately(dotSize, displayedDotSize))
        {
            return;
        }

        int dotsPerRow = Mathf.Max(1, ammoDotsPerRow);

        if (magazine != displayedMagazineSize || dotsPerRow != displayedDotsPerRow || !Mathf.Approximately(dotSize, displayedDotSize))
        {
            displayedDotsPerRow = dotsPerRow;
            displayedDotSize = dotSize;
            RebuildAmmoDots(magazine);
        }

        for (int i = 0; i < ammoDots.Count; i++)
        {
            // Dots fill left to right, so the rightmost lit dot is the first to go dark.
            ammoDots[i].style.backgroundColor = i < bullets ? AmmoDotLitColor : AmmoDotSpentColor;
        }

        displayedMagazineSize = magazine;
        displayedAmmoCount = bullets;
    }

    private void ResolveAmmoBar()
    {
        ammoBarElement = null;
        ammoDots.Clear();
        displayedMagazineSize = -1;
        displayedAmmoCount = -1;
        displayedDotsPerRow = -1;
        displayedDotSize = -1f;

        if (healthUiDocument == null)
        {
            ResolveHealthUi();
        }

        if (healthUiDocument == null || !healthUiDocument.gameObject.activeInHierarchy || healthUiDocument.rootVisualElement == null)
        {
            return;
        }

        ammoBarElement = healthUiDocument.rootVisualElement.Q<VisualElement>(AmmoBarElementName);
    }

    private void RebuildAmmoDots(int count)
    {
        ammoBarElement.Clear();
        ammoDots.Clear();

        // Column-reverse puts the first row at the bottom, so extra rows stack on top of it.
        ammoBarElement.style.flexDirection = FlexDirection.ColumnReverse;
        ammoBarElement.style.alignItems = Align.FlexStart;

        int dotsPerRow = Mathf.Max(1, ammoDotsPerRow);
        float dotSize = Mathf.Max(1f, ammoDotSize);
        float dotSpacing = dotSize * AmmoDotSpacingRatio;
        float rowSpacing = dotSize * AmmoRowSpacingRatio;
        VisualElement row = null;

        for (int i = 0; i < count; i++)
        {
            if (i % dotsPerRow == 0)
            {
                row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.style.flexDirection = FlexDirection.Row;
                // Column-reverse stacks upward, so the gap to the row below is this row's bottom margin.
                row.style.marginBottom = i == 0 ? 0f : rowSpacing;
                ammoBarElement.Add(row);
            }

            VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.width = dotSize;
            dot.style.height = dotSize;
            dot.style.flexShrink = 0f;
            dot.style.marginRight = (i % dotsPerRow < dotsPerRow - 1 && i < count - 1) ? dotSpacing : 0f;
            dot.style.borderTopLeftRadius = dotSize * 0.5f;
            dot.style.borderTopRightRadius = dotSize * 0.5f;
            dot.style.borderBottomLeftRadius = dotSize * 0.5f;
            dot.style.borderBottomRightRadius = dotSize * 0.5f;
            // Black outline to match the menu button style.
            float outline = Mathf.Max(2f, dotSize * 0.15f);
            dot.style.borderTopWidth = outline;
            dot.style.borderRightWidth = outline;
            dot.style.borderLeftWidth = outline;
            dot.style.borderBottomWidth = outline * 1.6f;
            dot.style.borderTopColor = Color.black;
            dot.style.borderRightColor = Color.black;
            dot.style.borderBottomColor = Color.black;
            dot.style.borderLeftColor = Color.black;
            row.Add(dot);
            ammoDots.Add(dot);
        }
    }
}
