using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Bullet : NetworkBehaviour
{
    public float lifeTime = 5f;
    public float damage = 25f;
    public float selfCollisionGraceTime = .25f;
    public int maxBounces;
    public bool hasPayload;
    public GameObject explosionPrefab;
    public float explosionRadius = 5f;
    public float explosionForce = 10f;

    public GameObject impactEffectPrefab;

    public float bounceBounciness = 1.0f;
    public float minNormalBounce = 3.0f;

    private Rigidbody rb;
    private float gravityForce = 9.81f;
    private float gravityDelayDistance;
    private Vector3 launchPosition;
    private bool isGravityDelayed;
    private ulong shooterClientId = ulong.MaxValue;
    private bool isIgnoringShooter;
    private int bouncesRemaining;
    private Vector3 lastVelocity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (bounceBounciness <= 0f) bounceBounciness = 1.2f;
        if (minNormalBounce <= 0f) minNormalBounce = 3.0f;
        IgnoreOtherBullets();
    }

    private void IgnoreOtherBullets()
    {
        Collider[] myColliders = GetComponentsInChildren<Collider>();
        Bullet[] allBullets = FindObjectsByType<Bullet>(FindObjectsSortMode.None);

        foreach (Bullet otherBullet in allBullets)
        {
            if (otherBullet == this || otherBullet == null)
            {
                continue;
            }

            Collider[] otherColliders = otherBullet.GetComponentsInChildren<Collider>();

            foreach (Collider myCollider in myColliders)
            {
                if (myCollider == null)
                {
                    continue;
                }

                foreach (Collider otherCollider in otherColliders)
                {
                    if (otherCollider != null)
                    {
                        Physics.IgnoreCollision(myCollider, otherCollider, true);
                    }
                }
            }
        }
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null || !IsSpawned)
        {
            // Local fallback bullets are not network-authoritative, so disable NetworkRigidbody.
            if (TryGetComponent<NetworkRigidbody>(out NetworkRigidbody networkRigidbody))
            {
                networkRigidbody.enabled = false;
            }

            rb.isKinematic = false;
            rb.WakeUp();
            Destroy(gameObject, lifeTime);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartCoroutine(DespawnAfterLifetime());
        }
    }

    private IEnumerator DespawnAfterLifetime()
    {
        yield return new WaitForSeconds(lifeTime);
        DespawnOrDestroy();
    }

    public void Launch(Vector3 direction, float speed)
    {
        Launch(direction, speed, gravityForce, 0f);
    }

    public void Launch(Vector3 direction, float speed, float bulletGravity)
    {
        Launch(direction, speed, bulletGravity, 0f);
    }

    public void Launch(Vector3 direction, float speed, float bulletGravity, float ignoreGravityDistance)
    {
        if (rb.isKinematic)
        {
            rb.isKinematic = false;
            rb.WakeUp();
        }

        gravityForce = bulletGravity;
        gravityDelayDistance = Mathf.Max(0f, ignoreGravityDistance);
        launchPosition = rb.position;
        isGravityDelayed = !Mathf.Approximately(gravityForce, 0f) && gravityDelayDistance > 0f;
        rb.useGravity = false;
        rb.linearVelocity = direction.normalized * speed;
    }

    public void SetBounces(int bounceCount)
    {
        maxBounces = Mathf.Max(0, bounceCount);
        bouncesRemaining = maxBounces;
    }

    private void FixedUpdate()
    {
        if (rb != null && !rb.isKinematic)
        {
            lastVelocity = rb.linearVelocity;
        }

        if (!Mathf.Approximately(gravityForce, 0f) && rb != null && !rb.isKinematic)
        {
            if (isGravityDelayed)
            {
                if (Vector3.Distance(launchPosition, rb.position) < gravityDelayDistance)
                {
                    return;
                }

                isGravityDelayed = false;
            }

            rb.AddForce(Vector3.down * gravityForce, ForceMode.Acceleration);
        }
    }

    public void SetShooter(ulong shooterId, FirstPersonController shooter)
    {
        shooterClientId = shooterId;

        if (shooter == null || selfCollisionGraceTime <= 0f)
        {
            return;
        }

        Collider[] bulletColliders = GetComponentsInChildren<Collider>();
        Collider[] shooterColliders = shooter.GetComponentsInChildren<Collider>();

        SetShooterCollisionIgnored(bulletColliders, shooterColliders, true);
        StartCoroutine(RestoreShooterCollision(bulletColliders, shooterColliders));
    }

    private IEnumerator RestoreShooterCollision(Collider[] bulletColliders, Collider[] shooterColliders)
    {
        yield return new WaitForSeconds(selfCollisionGraceTime);
        SetShooterCollisionIgnored(bulletColliders, shooterColliders, false);
    }

    private void SetShooterCollisionIgnored(Collider[] bulletColliders, Collider[] shooterColliders, bool ignored)
    {
        isIgnoringShooter = ignored;

        foreach (Collider bulletCollider in bulletColliders)
        {
            if (bulletCollider == null)
            {
                continue;
            }

            foreach (Collider shooterCollider in shooterColliders)
            {
                if (shooterCollider != null)
                {
                    Physics.IgnoreCollision(bulletCollider, shooterCollider, ignored);
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (NetworkManager.Singleton != null && IsSpawned && !IsServer)
        {
            return;
        }

        if (collision.collider.GetComponentInParent<Bullet>() != null)
        {
            return;
        }

        FirstPersonController hitPlayer = collision.collider.GetComponentInParent<FirstPersonController>();

        if (isIgnoringShooter && hitPlayer != null && hitPlayer.IsSpawned && hitPlayer.OwnerClientId == shooterClientId)
        {
            return;
        }

        if (hitPlayer != null)
        {
            if (hitPlayer.IsBlocking() && hitPlayer.IsShieldCollider(collision.collider))
            {
                if (TryReflectAcrossShield(collision))
                {
                    return;
                }
            }

            bool damageApplied = hitPlayer.TryApplyDamage(damage, shooterClientId);

            if (damageApplied)
            {
                Debug.Log($"Bullet from player {shooterClientId} hit {hitPlayer.name} for {damage} damage.", this);
            }
            else
            {
                Debug.Log($"Bullet hit {hitPlayer.name}, but no damage was applied.", this);
            }

            DespawnOrDestroy();
            return;
        }

        if (TryBounce(collision))
        {
            return;
        }

        DespawnOrDestroy();
    }

    private bool TryReflectAcrossShield(Collision collision)
    {
        if (rb == null || collision.contactCount <= 0)
        {
            return false;
        }

        Vector3 incomingVelocity = lastVelocity.sqrMagnitude > 0.0001f ? lastVelocity : rb.linearVelocity;

        if (incomingVelocity.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 normal = contact.normal;

        Vector3 reflectedVelocity = Vector3.Reflect(incomingVelocity, normal);

        float normalComponent = Vector3.Dot(reflectedVelocity, normal);
        if (normalComponent < minNormalBounce)
        {
            Vector3 tangentialVelocity = reflectedVelocity - (normal * normalComponent);
            reflectedVelocity = tangentialVelocity + (normal * Mathf.Max(normalComponent, minNormalBounce));
        }

        float incomingSpeed = incomingVelocity.magnitude;
        float outgoingSpeed = Mathf.Max(incomingSpeed * bounceBounciness, incomingSpeed);
        reflectedVelocity = reflectedVelocity.normalized * outgoingSpeed;

        bouncesRemaining = Mathf.Max(bouncesRemaining, 1);

        rb.position += normal * 0.05f;
        rb.linearVelocity = reflectedVelocity;

        if (reflectedVelocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(reflectedVelocity);
        }

        if (rb != null)
        {
            rb.angularVelocity = Random.insideUnitSphere * 15f;
        }

        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, contact.point, Quaternion.LookRotation(normal));
        }

        launchPosition = rb.position;
        isGravityDelayed = false;
        lastVelocity = reflectedVelocity;
        return true;
    }

    private bool TryBounce(Collision collision)
    {
        if (bouncesRemaining <= 0 || rb == null || collision.contactCount <= 0)
        {
            return false;
        }

        Vector3 incomingVelocity = lastVelocity.sqrMagnitude > 0.0001f ? lastVelocity : rb.linearVelocity;

        if (incomingVelocity.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 normal = contact.normal;

        // Reflect incoming velocity across the normal
        Vector3 reflectedVelocity = Vector3.Reflect(incomingVelocity, normal);

        // Ensure minimum normal bounce so shallow angles bounce out forcefully
        float normalComponent = Vector3.Dot(reflectedVelocity, normal);
        if (normalComponent < minNormalBounce)
        {
            Vector3 tangentialVelocity = reflectedVelocity - (normal * normalComponent);
            reflectedVelocity = tangentialVelocity + (normal * Mathf.Max(normalComponent, minNormalBounce));
        }

        // Apply bounciness multiplier to speed magnitude
        float incomingSpeed = incomingVelocity.magnitude;
        float outgoingSpeed = Mathf.Max(incomingSpeed * bounceBounciness, incomingSpeed);
        reflectedVelocity = reflectedVelocity.normalized * outgoingSpeed;

        bouncesRemaining--;
        rb.position += normal * 0.05f;
        rb.linearVelocity = reflectedVelocity;

        if (reflectedVelocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(reflectedVelocity);
        }

        if (rb != null)
        {
            rb.angularVelocity = Random.insideUnitSphere * 15f;
        }

        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, contact.point, Quaternion.LookRotation(normal));
        }

        launchPosition = rb.position;
        isGravityDelayed = false;
        lastVelocity = reflectedVelocity;
        return true;
    }

    private void DespawnOrDestroy()
    {
        if (hasPayload)
        {
            ApplyExplosion();
        }

        GameObject effectToSpawn = hasPayload && explosionPrefab != null ? explosionPrefab : impactEffectPrefab;
        Vector3 effectPosition = transform.position;

        if (NetworkManager.Singleton != null && IsSpawned && TryGetComponent<NetworkObject>(out NetworkObject networkObject))
        {
            if (effectToSpawn != null)
            {
                SpawnEffectClientRpc(effectPosition, hasPayload);
            }

            networkObject.Despawn();
            return;
        }

        if (effectToSpawn != null)
        {
            Instantiate(effectToSpawn, effectPosition, Quaternion.identity);
        }

        Destroy(gameObject);
    }

    [ClientRpc]
    private void SpawnEffectClientRpc(Vector3 position, bool isExplosion)
    {
        GameObject effectToSpawn = isExplosion && explosionPrefab != null ? explosionPrefab : impactEffectPrefab;

        if (effectToSpawn != null)
        {
            Instantiate(effectToSpawn, position, Quaternion.identity);
        }
    }

    private void ApplyExplosion()
    {
        if (NetworkManager.Singleton != null && IsSpawned && !IsServer)
        {
            return;
        }

        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        float explosionDamage = damage * 0.5f;

        foreach (Collider hitCollider in colliders)
        {
            if (hitCollider == null)
            {
                continue;
            }

            FirstPersonController hitPlayer = hitCollider.GetComponentInParent<FirstPersonController>();
            if (hitPlayer != null)
            {
                if (isIgnoringShooter && hitPlayer.IsSpawned && hitPlayer.OwnerClientId == shooterClientId)
                {
                    continue;
                }

                hitPlayer.TryApplyDamage(explosionDamage, shooterClientId);
            }

            Rigidbody hitRb = hitCollider.GetComponentInParent<Rigidbody>();
            if (hitRb != null && hitRb != rb)
            {
                hitRb.AddExplosionForce(explosionForce, transform.position, explosionRadius, 1f, ForceMode.Impulse);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (hasPayload)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}

class BulletGravity : MonoBehaviour
{
    private Rigidbody rb;
    private float gravityForce;
    private float gravityDelayDistance;
    private Vector3 launchPosition;
    private bool isGravityDelayed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        launchPosition = rb != null ? rb.position : transform.position;
    }

    private void FixedUpdate()
    {
        if (!Mathf.Approximately(gravityForce, 0f) && rb != null && !rb.isKinematic)
        {
            if (isGravityDelayed)
            {
                if (Vector3.Distance(launchPosition, rb.position) < gravityDelayDistance)
                {
                    return;
                }

                isGravityDelayed = false;
            }

            rb.AddForce(Vector3.down * gravityForce, ForceMode.Acceleration);
        }
    }

    public void SetGravity(float bulletGravity)
    {
        SetGravity(bulletGravity, 0f);
    }

    public void SetGravity(float bulletGravity, float ignoreGravityDistance)
    {
        gravityForce = bulletGravity;
        gravityDelayDistance = Mathf.Max(0f, ignoreGravityDistance);

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (rb != null)
        {
            launchPosition = rb.position;
            isGravityDelayed = !Mathf.Approximately(gravityForce, 0f) && gravityDelayDistance > 0f;
            rb.useGravity = false;
        }
    }
}
