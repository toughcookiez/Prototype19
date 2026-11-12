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
    public bool TryApplyDamage(float damageAmount, ulong shooterClientId)
    {
        if (!IsServer || !enablePlayerDamage)
        {
            return false;
        }

        if (!IsAlive())
        {
            return false;
        }

        if (isShieldActive.Value)
        {
            return false;
        }

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - Mathf.Max(0f, damageAmount));

        Debug.Log($"Player {OwnerClientId} hit by player {shooterClientId} for {damageAmount}. Health: {currentHealth.Value}/{maxHealth}", this);

        if (currentHealth.Value <= 0f)
        {
            Debug.Log($"Player {OwnerClientId} died. Killer: {shooterClientId}", this);
            SetRagdollState(true);
            HandlePlayerDefeatServer(shooterClientId);
        }

        return true;
    }

    private void ResolveHealthUi()
    {
        ResolveMatchUiDocuments();

        if (healthUiDocument == null)
        {
            healthUiDocument = GetComponentInChildren<UIDocument>(true);
        }

        if (healthUiDocument == null || !healthUiDocument.gameObject.activeInHierarchy || healthUiDocument.rootVisualElement == null)
        {
            healthBarProgressElement = null;
            return;
        }

        healthBarProgressElement = healthUiDocument.rootVisualElement.Q<VisualElement>("BarProgress");

        if (healthBarProgressElement != null)
        {
            // Animate width changes so the bar glides to its new value instead of snapping.
            healthBarProgressElement.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("width") };
            healthBarProgressElement.style.transitionDuration = new List<TimeValue> { new TimeValue(HealthBarTransitionSeconds, TimeUnit.Second) };
            healthBarProgressElement.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
        }
    }

    private const float HealthBarTransitionSeconds = 0.3f;

    private void OnCurrentHealthChanged(float previousValue, float newValue)
    {
        UpdateHealthBar(newValue);

        if (IsOwner && newValue < previousValue && newValue > 0f)
        {
            FlashDamageOverlay();
        }
    }

    private const float DamageFlashPeakOpacity = 0.4f;
    private const float DamageFlashFadeSeconds = 0.35f;
    private VisualElement damageFlashElement;

    // Blinks a full-screen red overlay (added to the PlayerUI root) once: instant on, then fades out.
    private void FlashDamageOverlay()
    {
        if (healthUiDocument == null || healthUiDocument.rootVisualElement == null)
        {
            ResolveHealthUi();
        }

        VisualElement root = healthUiDocument != null ? healthUiDocument.rootVisualElement : null;
        if (root == null || root.panel == null)
        {
            return;
        }

        if (damageFlashElement == null || damageFlashElement.panel != root.panel)
        {
            damageFlashElement = new VisualElement { name = "DamageFlash", pickingMode = PickingMode.Ignore };
            damageFlashElement.style.position = Position.Absolute;
            damageFlashElement.style.left = 0;
            damageFlashElement.style.top = 0;
            damageFlashElement.style.right = 0;
            damageFlashElement.style.bottom = 0;
            damageFlashElement.style.backgroundColor = new Color(1f, 0f, 0f, 1f);
            damageFlashElement.style.opacity = 0f;
            root.Add(damageFlashElement);
        }

        VisualElement flash = damageFlashElement;
        flash.BringToFront();

        // Snap on with no transition, then enable the fade on the next frame so it animates back to 0.
        flash.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
        flash.style.opacity = DamageFlashPeakOpacity;
        flash.schedule.Execute(() =>
        {
            flash.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
            flash.style.transitionDuration = new List<TimeValue> { new TimeValue(DamageFlashFadeSeconds, TimeUnit.Second) };
            flash.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
            flash.style.opacity = 0f;
        }).ExecuteLater(50);
    }

    private const float HealFlashPeakOpacity = 0.3f;
    private const float HealFlashFadeSeconds = 0.6f;
    private VisualElement healFlashElement;

    // Quick Snack: a green full-screen pulse, like the damage flash but slower.
    private void FlashHealOverlay()
    {
        if (healthUiDocument == null || healthUiDocument.rootVisualElement == null)
        {
            ResolveHealthUi();
        }

        VisualElement root = healthUiDocument != null ? healthUiDocument.rootVisualElement : null;
        if (root == null || root.panel == null)
        {
            return;
        }

        if (healFlashElement == null || healFlashElement.panel != root.panel)
        {
            healFlashElement = new VisualElement { name = "HealFlash", pickingMode = PickingMode.Ignore };
            healFlashElement.style.position = Position.Absolute;
            healFlashElement.style.left = 0;
            healFlashElement.style.top = 0;
            healFlashElement.style.right = 0;
            healFlashElement.style.bottom = 0;
            healFlashElement.style.backgroundColor = new Color(0.2f, 1f, 0.3f, 1f);
            healFlashElement.style.opacity = 0f;
            root.Add(healFlashElement);
        }

        VisualElement flash = healFlashElement;
        flash.BringToFront();

        flash.style.transitionDuration = new List<TimeValue> { new TimeValue(0f, TimeUnit.Second) };
        flash.style.opacity = HealFlashPeakOpacity;
        flash.schedule.Execute(() =>
        {
            flash.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
            flash.style.transitionDuration = new List<TimeValue> { new TimeValue(HealFlashFadeSeconds, TimeUnit.Second) };
            flash.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
            flash.style.opacity = 0f;
        }).ExecuteLater(50);

        SpawnHealPluses(root);
    }

    private const int HealPlusCount = 20;
    private const float HealPlusRiseSeconds = 0.9f;
    private static readonly Color HealPlusColor = new Color(0.65f, 1f, 0.7f, 1f);

    // Light green "+" symbols that drift upward from around the middle of the screen and fade out.
    private void SpawnHealPluses(VisualElement root)
    {
        for (int i = 0; i < HealPlusCount; i++)
        {
            Label plus = new Label("+") { pickingMode = PickingMode.Ignore };
            plus.style.position = Position.Absolute;
            // Alternate between the left and right screen edges.
            plus.style.left = Length.Percent(i % 2 == 0 ? Random.Range(2f, 12f) : Random.Range(88f, 96f));
            // Each side gets evenly spaced vertical slots (with jitter) so the effect covers top to bottom.
            int slot = i / 2;
            plus.style.top = Length.Percent((slot + Random.value) / (HealPlusCount / 2f) * 92f);
            plus.style.fontSize = Random.Range(28f, 56f);
            plus.style.unityFontStyleAndWeight = FontStyle.Bold;
            plus.style.color = HealPlusColor;
            plus.style.opacity = 0f;
            plus.style.translate = new Translate(0f, 0f);
            root.Add(plus);

            long delayMs = (long)(Random.Range(0f, 250f));
            float rise = Random.Range(90f, 170f);

            plus.schedule.Execute(() =>
            {
                plus.style.opacity = 1f;
                plus.schedule.Execute(() =>
                {
                    plus.style.transitionProperty = new List<StylePropertyName>
                    {
                        new StylePropertyName("translate"),
                        new StylePropertyName("opacity")
                    };
                    plus.style.transitionDuration = new List<TimeValue> { new TimeValue(HealPlusRiseSeconds, TimeUnit.Second) };
                    plus.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
                    plus.style.translate = new Translate(0f, -rise);
                    plus.style.opacity = 0f;
                }).ExecuteLater(30);
            }).ExecuteLater(delayMs + 50);

            plus.schedule.Execute(() => plus.RemoveFromHierarchy()).ExecuteLater(delayMs + 50 + (long)(HealPlusRiseSeconds * 1000f) + 200);
        }
    }

    private void UpdateHealthBar(float healthValue)
    {
        if (healthBarProgressElement == null || healthBarProgressElement.panel == null ||
            (healthUiDocument != null && (healthUiDocument.rootVisualElement == null || healthBarProgressElement.panel != healthUiDocument.rootVisualElement.panel)))
        {
            ResolveHealthUi();
        }

        if (healthBarProgressElement == null)
        {
            return;
        }

        float maxHealthValue = Mathf.Max(1f, maxHealth);
        float normalizedHealth = Mathf.Clamp01(healthValue / maxHealthValue);
        healthBarProgressElement.style.width = Length.Percent(normalizedHealth * 100f);
    }
}
