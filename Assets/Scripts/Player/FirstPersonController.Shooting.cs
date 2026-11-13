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
    private static bool IsInMainMenuScene()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";
    }

    private void TryShoot()
    {
        if (!HasInputAuthority() || IsInMainMenuScene())
        {
            return;
        }

        if (isReloading || IsStunned)
        {
            return;
        }

        if (Time.time < nextShootTime)
        {
            return;
        }

        if (bulletsInMagazine <= 0)
        {
            TryStartReload();
            return;
        }

        if (bulletPrefab == null || shootPoint == null)
        {
            return;
        }

        Vector3 shootDirection = GetShootDirection(GetShotOrigin(), GetCrosshairPoint());
        nextShootTime = Time.time + (1f / Mathf.Max(.1f, fireRate));

        int bulletsToFire = Mathf.Min(Mathf.Max(1, bulletsPerShot), bulletsInMagazine);

        StartCoroutine(FireBulletBurst(bulletsToFire));
        bulletsInMagazine -= bulletsToFire;
        ApplyFlintknock(shootDirection);
        SetAnimatorTrigger(shootTriggerParameter);
    }

    // Runs only on the shooter's own machine. Every bullet in a burst samples the live muzzle position
    // and aim, so bursts follow the gun while moving. Remote clients send one ServerRpc per bullet and
    // show a local cosmetic bullet right away so the shot leaves their gun without waiting a round trip.
    private IEnumerator FireBulletBurst(int bulletsToFire)
    {
        int clampedBulletsToFire = Mathf.Max(1, bulletsToFire);

        for (int i = 0; i < clampedBulletsToFire; i++)
        {
            if (bulletPrefab == null || shootPoint == null || !HasInputAuthority())
            {
                yield break;
            }

            Vector3 crosshairPoint = GetCrosshairPoint();
            Vector3 spawnPosition = GetShotOrigin();
            Vector3 shootDirection = GetShootDirection(spawnPosition, crosshairPoint);
            // The bullet flies straight at what the crosshair is over, and gravity only starts once it has
            // travelled crosshairPointDistance from the muzzle.
            float ignoreGravityDistance = Mathf.Max(0f, crosshairPointDistance);

            // Counter Slug: the first bullet fired after a block is the boosted one.
            bool counterSlugShot = counterSlugArmed;
            counterSlugArmed = false;
            float damageMultiplier = counterSlugShot && hasCounterSlug ? counterSlugDamageMultiplier : 1f;

            // The shooter sees the flash on their own gun right away. Everyone else gets it from
            // Bullet.ConfigureNetworkVisuals when the networked bullet spawns on their machine.
            PlayMuzzleFlash(shootPoint);

            if (ShouldUseNetworkSpawning())
            {
                int shotId = ++localShotCounter;
                ShootServerRpc(spawnPosition, shootDirection, ignoreGravityDistance, shotId, counterSlugShot);
                SpawnBullet(spawnPosition, shootDirection, ignoreGravityDistance, OwnerClientId, shotId, true, damageMultiplier);
            }
            else
            {
                SpawnBullet(spawnPosition, shootDirection, ignoreGravityDistance, IsSpawned ? OwnerClientId : ulong.MaxValue, 0, false, damageMultiplier);
            }

            if (i < clampedBulletsToFire - 1)
            {
                if (bulletBurstInterval > 0f)
                {
                    yield return new WaitForSeconds(bulletBurstInterval);
                }
                else
                {
                    yield return null;
                }
            }
        }
    }

    private void SpawnBullet(Vector3 spawnPosition, Vector3 shootDirection, float ignoreGravityDistance, ulong shooterClientId, int shotId, bool isCosmetic, float damageMultiplier = 1f)
    {
        Quaternion shootRotation = Quaternion.LookRotation(shootDirection, Vector3.up);
        GameObject spawnedBullet = Instantiate(bulletPrefab, spawnPosition, shootRotation);

        if (spawnedBullet.TryGetComponent<Bullet>(out Bullet bullet))
        {
            bullet.SetShotId(shotId);

            if (isCosmetic)
            {
                bullet.MarkAsCosmetic();
            }

            bullet.damage = bulletDamage * damageMultiplier;
            bullet.SetPierces(bulletPierceCount);
            bullet.SetSizeMultiplier(bulletSizeMultiplier);
            bullet.SetCounterSlug(damageMultiplier > 1f && hasCounterSlug);
            bullet.stunDuration = hasChillyShells ? chillyStunDuration : 0f;
            bullet.nearsightDuration = hasDimsighted ? dimsightedDuration : 0f;
            bullet.SetBounces(bulletBounces);
            bullet.SetShooter(shooterClientId, this);
            bullet.hasPayload = hasPayload;
            bullet.hasBeacon = hasBeacon;
            if (explosionPrefab != null)
            {
                bullet.explosionPrefab = explosionPrefab;
            }
            bullet.explosionRadius = explosionRadius;
            bullet.explosionForce = explosionForce;
        }

        if (!isCosmetic && NetworkManager.Singleton != null && IsServer && spawnedBullet.TryGetComponent<NetworkObject>(out NetworkObject networkObject))
        {
            networkObject.Spawn();
        }

        if (spawnedBullet.TryGetComponent<Bullet>(out Bullet launchedBullet))
        {
            launchedBullet.Launch(shootDirection, bulletSpeed, bulletGravity, ignoreGravityDistance);
        }
        else if (spawnedBullet.TryGetComponent<Rigidbody>(out Rigidbody bulletRb))
        {
            bulletRb.linearVelocity = shootDirection * bulletSpeed;

            if (spawnedBullet.TryGetComponent<BulletGravity>(out BulletGravity bulletGravityComponent))
            {
                bulletGravityComponent.SetGravity(bulletGravity, ignoreGravityDistance);
            }
            else
            {
                spawnedBullet.AddComponent<BulletGravity>().SetGravity(bulletGravity, ignoreGravityDistance);
            }
        }
    }

    // One call per bullet; the owning client paces bursts and sends its live muzzle position each time.
    [ServerRpc]
    private void ShootServerRpc(Vector3 shootPosition, Vector3 shootDirection, float ignoreGravityDistance, int shotId, bool counterSlugShot)
    {
        if (bulletPrefab == null || shootDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        // The server only honors the boost if this player really owns Counter Slug.
        float damageMultiplier = counterSlugShot && hasCounterSlug ? counterSlugDamageMultiplier : 1f;
        SpawnBullet(shootPosition, shootDirection.normalized, Mathf.Max(0f, ignoreGravityDistance), OwnerClientId, shotId, false, damageMultiplier);
    }

    private void ApplyFlintknock(Vector3 shootDirection)
    {
        if (!hasFlintknock || rb == null || flintknockForce <= 0f)
        {
            return;
        }

        rb.AddForce(-shootDirection * flintknockForce, ForceMode.Impulse);
    }

    private bool ShouldUseNetworkSpawning()
    {
        return enableMultiplayerAuthority && NetworkManager.Singleton != null && IsSpawned && IsOwner && !IsServer;
    }

    private const float AimRayMaxDistance = 500f;
    private const float MuzzleClearanceRadius = 0.05f;
    private static readonly RaycastHit[] aimHitBuffer = new RaycastHit[16];

    // True for colliders that belong to this player or to projectiles, which the aim ray must ignore.
    private bool IsAimIgnoredCollider(Collider hitCollider)
    {
        if (hitCollider == null || hitCollider.isTrigger)
        {
            return true;
        }

        Transform hitTransform = hitCollider.transform;
        return hitTransform.IsChildOf(transform) || hitCollider.GetComponentInParent<Bullet>() != null;
    }

    // Nearest solid hit along the ray, skipping this player's own colliders, triggers and bullets.
    private bool TryRaycastAim(Ray ray, float maxDistance, out RaycastHit nearestHit)
    {
        nearestHit = default;
        int hitCount = Physics.RaycastNonAlloc(ray, aimHitBuffer, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = aimHitBuffer[i];

            if (hit.distance < nearestDistance && !IsAimIgnoredCollider(hit.collider))
            {
                nearestDistance = hit.distance;
                nearestHit = hit;
                found = true;
            }
        }

        return found;
    }

    // What the crosshair is over: the first solid thing on the camera's center ray, or a far point on a miss.
    private Vector3 GetCrosshairPoint()
    {
        if (playerCamera == null)
        {
            return shootPoint.position + shootPoint.forward * AimRayMaxDistance;
        }

        Ray centerRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (TryRaycastAim(centerRay, AimRayMaxDistance, out RaycastHit hit))
        {
            return hit.point;
        }

        return centerRay.GetPoint(AimRayMaxDistance);
    }

    // The muzzle, unless geometry sits between the camera and the muzzle (gun pushed into a wall);
    // then the bullet starts at the camera so it can't spawn behind the wall.
    private Vector3 GetShotOrigin()
    {
        if (playerCamera == null)
        {
            return shootPoint.position;
        }

        Vector3 cameraPosition = playerCamera.transform.position;
        Vector3 toMuzzle = shootPoint.position - cameraPosition;
        float muzzleDistance = toMuzzle.magnitude;

        if (muzzleDistance <= 0.001f)
        {
            return shootPoint.position;
        }

        Ray muzzleRay = new Ray(cameraPosition, toMuzzle / muzzleDistance);
        int hitCount = Physics.SphereCastNonAlloc(muzzleRay, MuzzleClearanceRadius, aimHitBuffer, muzzleDistance, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (!IsAimIgnoredCollider(aimHitBuffer[i].collider))
            {
                return cameraPosition + playerCamera.transform.forward * 0.1f;
            }
        }

        return shootPoint.position;
    }

    private Vector3 GetShootDirection(Vector3 origin, Vector3 targetPoint)
    {
        Vector3 toTarget = targetPoint - origin;
        float distance = toTarget.magnitude;

        if (distance <= 0.01f)
        {
            return shootPoint.forward;
        }

        return toTarget / distance;
    }

    private void TryStartReload()
    {
        if (isReloading || IsStunned || IsInMainMenuScene())
        {
            return;
        }

        if (bulletsInMagazine >= magazineSize)
        {
            return;
        }

        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        float elapsed = 0f;

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
        }

        if (progressRingCG != null)
        {
            progressRingCG.alpha = 0f;
        }

        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / reloadTime);

            if (progressRing != null)
            {
                progressRing.fillAmount = progress;
            }

            if (progressRingCG != null)
            {
                progressRingCG.alpha = Mathf.Min(progress * 10f, (1f - progress) * 10f, 1f);
            }

            yield return null;
        }

        bulletsInMagazine = magazineSize;
        isReloading = false;

        if (progressRing != null)
        {
            progressRing.fillAmount = 1f;
        }

        if (progressRingCG != null)
        {
            progressRingCG.alpha = 0f;
        }
    }

    public static FirstPersonController FindSpawnedPlayer(ulong clientId)
    {
        foreach (FirstPersonController player in spawnedPlayers)
        {
            if (player != null && player.IsSpawned && player.OwnerClientId == clientId)
            {
                return player;
            }
        }

        return null;
    }

    // Flash at the muzzle this machine renders for this player (first-person gun for the owner, outer gun for others).
    public void PlayMuzzleFlash(Transform muzzle = null)
    {
        if (muzzleFlashPrefab == null)
        {
            return;
        }

        if (muzzle == null)
        {
            Renderer outerRenderer = outerGoober != null ? outerGoober.GetComponentInChildren<Renderer>() : null;
            bool useOuter = outerShootPoint != null && outerRenderer != null && outerRenderer.enabled && outerShootPoint.gameObject.activeInHierarchy;
            muzzle = useOuter ? outerShootPoint : shootPoint;
        }

        if (muzzle == null)
        {
            return;
        }

        GameObject flash = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle);
        Destroy(flash, Mathf.Max(0.05f, muzzleFlashLifetime));
    }

    // Muzzle of whichever gun this machine is actually rendering for this player.
    public bool TryGetVisibleMuzzlePosition(out Vector3 position)
    {
        Renderer outerRenderer = outerGoober != null ? outerGoober.GetComponentInChildren<Renderer>() : null;

        if (outerShootPoint != null && outerRenderer != null && outerRenderer.enabled && outerShootPoint.gameObject.activeInHierarchy)
        {
            position = outerShootPoint.position;
            return true;
        }

        if (shootPoint != null && shootPoint.gameObject.activeInHierarchy)
        {
            position = shootPoint.position;
            return true;
        }

        position = default;
        return false;
    }
}
