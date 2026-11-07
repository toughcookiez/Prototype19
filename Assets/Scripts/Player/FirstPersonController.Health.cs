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
