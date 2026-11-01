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
    private void ApplyPickedCard(Card cardData)
    {
        if (cardData == null)
        {
            return;
        }

        ApplyCardStatModifiers(cardData);

        if (cardData.abilities != null)
        {
            foreach (CardAbility ability in cardData.abilities)
            {
                if (ability.abilityName == "Flintknock")
                {
                    hasFlintknock = true;
                    if (IsSpawned && IsServer)
                    {
                        ApplyFlintknockAbilityClientRpc(new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new[] { OwnerClientId }
                            }
                        });
                    }
                }
                else if (ability.abilityName == "Payload")
                {
                    hasPayload = true;
                    if (IsSpawned && IsServer)
                    {
                        ApplyPayloadAbilityClientRpc(new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new[] { OwnerClientId }
                            }
                        });
                    }
                }
                else if (ability.abilityName == "Beacon")
                {
                    hasBeacon = true;
                    if (IsSpawned && IsServer)
                    {
                        ApplyBeaconAbilityClientRpc(new ClientRpcParams
                        {
                            Send = new ClientRpcSendParams
                            {
                                TargetClientIds = new[] { OwnerClientId }
                            }
                        });
                    }
                }
            }
        }

        SyncWeaponSettings(GetWeaponSettings(), false);
        SyncPlayerSettings(GetPlayerSettings());
    }

    [ClientRpc]
    private void ApplyFlintknockAbilityClientRpc(ClientRpcParams clientRpcParams = default)
    {
        hasFlintknock = true;
    }

    [ClientRpc]
    private void ApplyPayloadAbilityClientRpc(ClientRpcParams clientRpcParams = default)
    {
        hasPayload = true;
    }

    [ClientRpc]
    private void ApplyBeaconAbilityClientRpc(ClientRpcParams clientRpcParams = default)
    {
        hasBeacon = true;
    }

    public void TriggerBeacon()
    {
        if (!IsServer)
        {
            return;
        }

        TriggerBeaconClientRpc();
    }

    [ClientRpc]
    private void TriggerBeaconClientRpc()
    {
        ApplyOrResetBeacon();
    }

    private void ApplyOrResetBeacon()
    {
        if (playerOutline == null)
        {
            playerOutline = GetComponent<Outline>();
            if (playerOutline == null)
            {
                playerOutline = gameObject.AddComponent<Outline>();
            }
        }

        if (beaconCoroutine != null)
        {
            StopCoroutine(beaconCoroutine);
        }

        beaconCoroutine = StartCoroutine(BeaconRevealRoutine());
    }

    private IEnumerator BeaconRevealRoutine()
    {
        playerOutline.enabled = true;
        Color baseColor = playerOutline.OutlineColor;
        if (baseColor == default || baseColor.a == 0f)
        {
            baseColor = Color.white;
        }

        int totalFlashes = 3;
        float flashDuration = 1.0f; // 1 second per flash
        float pauseDuration = 0.5f; // 0.1 second pause between flashes

        for (int i = 0; i < totalFlashes; i++)
        {
            float elapsed = 0f;
            while (elapsed < flashDuration)
            {
                float t = elapsed / flashDuration; // 0 to 1
                float alpha;
                if (t <= 0.5f)
                {
                    alpha = t * 2f; // 0 to 1 (fade in)
                }
                else
                {
                    alpha = (1f - t) * 2f; // 1 to 0 (fade out)
                }

                playerOutline.OutlineColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

                elapsed += Time.deltaTime;
                yield return null;
            }

            playerOutline.OutlineColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);

            if (i < totalFlashes - 1)
            {
                yield return new WaitForSeconds(pauseDuration);
            }
        }

        playerOutline.OutlineColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        playerOutline.enabled = false;
        beaconCoroutine = null;
    }

    private void ApplyCardStatModifiers(Card cardData)
    {
        if (cardData.stats == null)
        {
            return;
        }

        foreach (CardStat stat in cardData.stats)
        {
            ApplyCardStatModifier(stat);
        }

        NormalizeWeaponSettings();
        NormalizePlayerSettings();
        bulletsInMagazine = Mathf.Clamp(bulletsInMagazine, 0, magazineSize);
        SaveCurrentWeaponSettings();
        SaveCurrentPlayerSettings();
        UpdateHealthBar(currentHealth.Value);
    }

    private void ApplyCardStatModifier(CardStat stat)
    {
        switch (stat.target)
        {
            case CardStatTarget.MaxHealth:
                maxHealth = ApplyCardStatValue(maxHealth, stat);
                break;
            case CardStatTarget.WalkSpeed:
                walkSpeed = ApplyCardStatValue(walkSpeed, stat);
                break;
            case CardStatTarget.SprintSpeed:
                sprintSpeed = ApplyCardStatValue(sprintSpeed, stat);
                break;
            case CardStatTarget.SprintDuration:
                sprintDuration = ApplyCardStatValue(sprintDuration, stat);
                sprintRemaining = Mathf.Min(sprintRemaining, sprintDuration);
                break;
            case CardStatTarget.JumpPower:
                jumpPower = ApplyCardStatValue(jumpPower, stat);
                break;
            case CardStatTarget.BulletDamage:
                bulletDamage = ApplyCardStatValue(bulletDamage, stat);
                break;
            case CardStatTarget.BulletSpeed:
                bulletSpeed = ApplyCardStatValue(bulletSpeed, stat);
                break;
            case CardStatTarget.BulletGravity:
                bulletGravity = ApplyCardStatValue(bulletGravity, stat);
                break;
            case CardStatTarget.BulletBounces:
                bulletBounces = Mathf.RoundToInt(ApplyCardStatValue(bulletBounces, stat));
                break;
            case CardStatTarget.FireRate:
                fireRate = ApplyCardStatValue(fireRate, stat);
                break;
            case CardStatTarget.BulletsPerShot:
                bulletsPerShot = Mathf.RoundToInt(ApplyCardStatValue(bulletsPerShot, stat));
                break;
            case CardStatTarget.MagazineSize:
                magazineSize = Mathf.RoundToInt(ApplyCardStatValue(magazineSize, stat));
                break;
            case CardStatTarget.StartingBullets:
                startingBullets = Mathf.RoundToInt(ApplyCardStatValue(startingBullets, stat));
                break;
            case CardStatTarget.ReloadTime:
                reloadTime = ApplyCardStatValue(reloadTime, stat);
                break;
            case CardStatTarget.CrosshairPointDistance:
                crosshairPointDistance = ApplyCardStatValue(crosshairPointDistance, stat);
                break;
            case CardStatTarget.BlockCooldown:
                blockCooldown = ApplyCardStatValue(blockCooldown, stat);
                break;
        }
    }

    private float ApplyCardStatValue(float currentValue, CardStat stat)
    {
        if (stat.modifierMode == CardStatModifierMode.Percent)
        {
            return currentValue * (1f + stat.amount * .01f);
        }

        return currentValue + stat.amount;
    }

    private void NormalizePlayerSettings()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        walkSpeed = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(0.1f, sprintSpeed);
        sprintDuration = Mathf.Max(0.1f, sprintDuration);
        sprintRemaining = Mathf.Clamp(sprintRemaining, 0f, sprintDuration);
        jumpPower = Mathf.Max(0.1f, jumpPower);
        blockCooldown = Mathf.Max(0.05f, blockCooldown);
    }
}
