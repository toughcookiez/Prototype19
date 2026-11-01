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
    public WeaponSettings GetWeaponSettings()
    {
        return new WeaponSettings
        {
            bulletDamage = bulletDamage,
            crosshairPointDistance = crosshairPointDistance,
            bulletSpeed = bulletSpeed,
            bulletGravity = bulletGravity,
            bulletBounces = bulletBounces,
            fireRate = fireRate,
            holdToFireRateThreshold = holdToFireRateThreshold,
            bulletsPerShot = bulletsPerShot,
            magazineSize = magazineSize,
            startingBullets = startingBullets,
            reloadTime = reloadTime
        };
    }

    public void ApplyWeaponSettings(WeaponSettings weaponSettings, bool refillMagazine = false)
    {
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
        SyncWeaponSettings(weaponSettings, refillMagazine);
    }

    public PlayerSettings GetPlayerSettings()
    {
        return new PlayerSettings
        {
            maxHealth = maxHealth,
            walkSpeed = walkSpeed,
            sprintSpeed = sprintSpeed,
            sprintDuration = sprintDuration,
            jumpPower = jumpPower,
            blockCooldown = blockCooldown
        };
    }

    public void ApplyPlayerSettings(PlayerSettings playerSettings)
    {
        ApplyPlayerSettingsLocally(playerSettings);
        SaveCurrentPlayerSettings();
        SyncPlayerSettings(playerSettings);
    }

    [ServerRpc]
    private void ApplyWeaponSettingsServerRpc(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue, bool refillMagazine)
    {
        WeaponSettings weaponSettings = CreateWeaponSettings(bulletDamageValue, crosshairPointDistanceValue, bulletSpeedValue, bulletGravityValue, bulletBouncesValue, fireRateValue, holdToFireRateThresholdValue, bulletsPerShotValue, magazineSizeValue, startingBulletsValue, reloadTimeValue);
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
    }

    [ClientRpc]
    private void ApplyWeaponSettingsOwnerClientRpc(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue, bool refillMagazine, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        WeaponSettings weaponSettings = CreateWeaponSettings(bulletDamageValue, crosshairPointDistanceValue, bulletSpeedValue, bulletGravityValue, bulletBouncesValue, fireRateValue, holdToFireRateThresholdValue, bulletsPerShotValue, magazineSizeValue, startingBulletsValue, reloadTimeValue);
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
    }

    private static WeaponSettings CreateWeaponSettings(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue)
    {
        return new WeaponSettings
        {
            bulletDamage = bulletDamageValue,
            crosshairPointDistance = crosshairPointDistanceValue,
            bulletSpeed = bulletSpeedValue,
            bulletGravity = bulletGravityValue,
            bulletBounces = bulletBouncesValue,
            fireRate = fireRateValue,
            holdToFireRateThreshold = holdToFireRateThresholdValue,
            bulletsPerShot = bulletsPerShotValue,
            magazineSize = magazineSizeValue,
            startingBullets = startingBulletsValue,
            reloadTime = reloadTimeValue
        };
    }

    private void ApplySavedWeaponSettings(bool refillMagazine)
    {
        if (savedWeaponSettingsByPlayer.TryGetValue(GetWeaponSettingsKey(), out WeaponSettings weaponSettings))
        {
            ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
            SaveCurrentWeaponSettings();
            return;
        }

        SaveCurrentWeaponSettings();
    }

    private void ApplySavedPlayerSettings()
    {
        if (savedPlayerSettingsByPlayer.TryGetValue(GetPlayerSettingsKey(), out PlayerSettings playerSettings))
        {
            ApplyPlayerSettingsLocally(playerSettings);
            SaveCurrentPlayerSettings();
            return;
        }

        NormalizePlayerSettings();
        SaveCurrentPlayerSettings();
    }

    private void ApplyPlayerSettingsLocally(PlayerSettings playerSettings)
    {
        maxHealth = playerSettings.maxHealth;
        walkSpeed = playerSettings.walkSpeed;
        sprintSpeed = playerSettings.sprintSpeed;
        sprintDuration = playerSettings.sprintDuration;
        jumpPower = playerSettings.jumpPower;
        blockCooldown = playerSettings.blockCooldown;
        NormalizePlayerSettings();
        UpdateHealthBar(currentHealth.Value);
    }

    private void ApplyWeaponSettingsLocally(WeaponSettings weaponSettings, bool refillMagazine)
    {
        bulletDamage = weaponSettings.bulletDamage;
        crosshairPointDistance = weaponSettings.crosshairPointDistance;
        bulletSpeed = weaponSettings.bulletSpeed;
        bulletGravity = weaponSettings.bulletGravity;
        bulletBounces = weaponSettings.bulletBounces;
        fireRate = weaponSettings.fireRate;
        holdToFireRateThreshold = weaponSettings.holdToFireRateThreshold;
        bulletsPerShot = weaponSettings.bulletsPerShot;
        magazineSize = weaponSettings.magazineSize;
        startingBullets = weaponSettings.startingBullets;
        reloadTime = weaponSettings.reloadTime;

        NormalizeWeaponSettings();
        bulletsInMagazine = refillMagazine ? Mathf.Clamp(startingBullets, 0, magazineSize) : Mathf.Clamp(bulletsInMagazine, 0, magazineSize);
    }

    private void NormalizeWeaponSettings()
    {
        bulletDamage = Mathf.Max(0.1f, bulletDamage);
        bulletSpeed = Mathf.Max(0.1f, bulletSpeed);
        bulletsPerShot = Mathf.Max(1, bulletsPerShot);
        bulletBounces = Mathf.Max(0, bulletBounces);
        magazineSize = Mathf.Max(1, magazineSize);
        crosshairPointDistance = Mathf.Max(0.1f, crosshairPointDistance);
        fireRate = Mathf.Max(.1f, fireRate);
        holdToFireRateThreshold = Mathf.Max(.1f, holdToFireRateThreshold);
        startingBullets = Mathf.Clamp(startingBullets, 1, magazineSize);
        reloadTime = Mathf.Max(.1f, reloadTime);
    }

    private void SaveCurrentWeaponSettingsIfChanged()
    {
        WeaponSettings weaponSettings = GetWeaponSettings();

        if (hasSavedWeaponSettings && WeaponSettingsMatch(lastSavedWeaponSettings, weaponSettings))
        {
            return;
        }

        ApplyWeaponSettingsLocally(weaponSettings, false);
        SaveWeaponSettings(weaponSettings);
        SyncWeaponSettings(weaponSettings, false);
    }

    private void SaveCurrentWeaponSettings()
    {
        SaveWeaponSettings(GetWeaponSettings());
    }

    private void SaveCurrentPlayerSettings()
    {
        SavePlayerSettings(GetPlayerSettings());
    }

    private void SaveWeaponSettings(WeaponSettings weaponSettings)
    {
        NormalizeWeaponSettings(ref weaponSettings);
        savedWeaponSettingsByPlayer[GetWeaponSettingsKey()] = weaponSettings;
        lastSavedWeaponSettings = weaponSettings;
        hasSavedWeaponSettings = true;
    }

    private void SavePlayerSettings(PlayerSettings playerSettings)
    {
        NormalizePlayerSettings(ref playerSettings);
        savedPlayerSettingsByPlayer[GetPlayerSettingsKey()] = playerSettings;
        lastSavedPlayerSettings = playerSettings;
        hasSavedPlayerSettings = true;
    }

    private ulong GetWeaponSettingsKey()
    {
        return IsSpawned ? OwnerClientId : LocalWeaponSettingsKey;
    }

    private ulong GetPlayerSettingsKey()
    {
        return IsSpawned ? OwnerClientId : LocalWeaponSettingsKey;
    }

    private void SyncWeaponSettings(WeaponSettings weaponSettings, bool refillMagazine)
    {
        if (!IsSpawned || NetworkManager.Singleton == null)
        {
            return;
        }

        if (IsServer)
        {
            ApplyWeaponSettingsOwnerClientRpc(weaponSettings.bulletDamage, weaponSettings.crosshairPointDistance, weaponSettings.bulletSpeed, weaponSettings.bulletGravity, weaponSettings.bulletBounces, weaponSettings.fireRate, weaponSettings.holdToFireRateThreshold, weaponSettings.bulletsPerShot, weaponSettings.magazineSize, weaponSettings.startingBullets, weaponSettings.reloadTime, refillMagazine, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            });
        }
        else if (IsOwner)
        {
            ApplyWeaponSettingsServerRpc(weaponSettings.bulletDamage, weaponSettings.crosshairPointDistance, weaponSettings.bulletSpeed, weaponSettings.bulletGravity, weaponSettings.bulletBounces, weaponSettings.fireRate, weaponSettings.holdToFireRateThreshold, weaponSettings.bulletsPerShot, weaponSettings.magazineSize, weaponSettings.startingBullets, weaponSettings.reloadTime, refillMagazine);
        }
    }

    private void SyncPlayerSettings(PlayerSettings playerSettings)
    {
        if (!IsSpawned || NetworkManager.Singleton == null || !IsServer)
        {
            return;
        }

        ApplyPlayerSettingsOwnerClientRpc(playerSettings.maxHealth, playerSettings.walkSpeed, playerSettings.sprintSpeed, playerSettings.sprintDuration, playerSettings.jumpPower, playerSettings.blockCooldown, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        });
    }

    [ClientRpc]
    private void ApplyPlayerSettingsOwnerClientRpc(float maxHealthValue, float walkSpeedValue, float sprintSpeedValue, float sprintDurationValue, float jumpPowerValue, float blockCooldownValue, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        PlayerSettings playerSettings = new PlayerSettings
        {
            maxHealth = maxHealthValue,
            walkSpeed = walkSpeedValue,
            sprintSpeed = sprintSpeedValue,
            sprintDuration = sprintDurationValue,
            jumpPower = jumpPowerValue,
            blockCooldown = blockCooldownValue
        };

        ApplyPlayerSettingsLocally(playerSettings);
        SaveCurrentPlayerSettings();
    }

    private static void NormalizeWeaponSettings(ref WeaponSettings weaponSettings)
    {
        weaponSettings.bulletDamage = Mathf.Max(0.1f, weaponSettings.bulletDamage);
        weaponSettings.bulletSpeed = Mathf.Max(0.1f, weaponSettings.bulletSpeed);
        weaponSettings.bulletsPerShot = Mathf.Max(1, weaponSettings.bulletsPerShot);
        weaponSettings.bulletBounces = Mathf.Max(0, weaponSettings.bulletBounces);
        weaponSettings.magazineSize = Mathf.Max(1, weaponSettings.magazineSize);
        weaponSettings.crosshairPointDistance = Mathf.Max(0.1f, weaponSettings.crosshairPointDistance);
        weaponSettings.fireRate = Mathf.Max(.1f, weaponSettings.fireRate);
        weaponSettings.holdToFireRateThreshold = Mathf.Max(.1f, weaponSettings.holdToFireRateThreshold);
        weaponSettings.startingBullets = Mathf.Clamp(weaponSettings.startingBullets, 1, weaponSettings.magazineSize);
        weaponSettings.reloadTime = Mathf.Max(.1f, weaponSettings.reloadTime);
    }

    private static void NormalizePlayerSettings(ref PlayerSettings playerSettings)
    {
        playerSettings.maxHealth = Mathf.Max(1f, playerSettings.maxHealth);
        playerSettings.walkSpeed = Mathf.Max(0.1f, playerSettings.walkSpeed);
        playerSettings.sprintSpeed = Mathf.Max(0.1f, playerSettings.sprintSpeed);
        playerSettings.sprintDuration = Mathf.Max(0.1f, playerSettings.sprintDuration);
        playerSettings.jumpPower = Mathf.Max(0.1f, playerSettings.jumpPower);
        playerSettings.blockCooldown = Mathf.Max(0.05f, playerSettings.blockCooldown);
    }

    private static bool WeaponSettingsMatch(WeaponSettings first, WeaponSettings second)
    {
        return Mathf.Approximately(first.bulletDamage, second.bulletDamage)
            && Mathf.Approximately(first.crosshairPointDistance, second.crosshairPointDistance)
            && Mathf.Approximately(first.bulletSpeed, second.bulletSpeed)
            && Mathf.Approximately(first.bulletGravity, second.bulletGravity)
            && first.bulletBounces == second.bulletBounces
            && Mathf.Approximately(first.fireRate, second.fireRate)
            && Mathf.Approximately(first.holdToFireRateThreshold, second.holdToFireRateThreshold)
            && first.bulletsPerShot == second.bulletsPerShot
            && first.magazineSize == second.magazineSize
            && first.startingBullets == second.startingBullets
            && Mathf.Approximately(first.reloadTime, second.reloadTime);
    }
}
