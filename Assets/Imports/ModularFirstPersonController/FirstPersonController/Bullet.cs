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

    public GameObject impactEffectPrefab;
    
    private Rigidbody rb;
    private float gravityForce = 9.81f;
    private float gravityDelayDistance;
    private Vector3 launchPosition;
    private bool isGravityDelayed;
    private ulong shooterClientId = ulong.MaxValue;
    private bool isIgnoringShooter;
    private int bouncesRemaining;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
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

        FirstPersonController hitPlayer = collision.collider.GetComponentInParent<FirstPersonController>();

        if (isIgnoringShooter && hitPlayer != null && hitPlayer.IsSpawned && hitPlayer.OwnerClientId == shooterClientId)
        {
            return;
        }

        if (hitPlayer != null)
        {
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

    private bool TryBounce(Collision collision)
    {
        if (bouncesRemaining <= 0 || rb == null || collision.contactCount <= 0)
        {
            return false;
        }

        Vector3 currentVelocity = rb.linearVelocity;

        if (currentVelocity.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        ContactPoint contact = collision.GetContact(0);
        Vector3 reflectedVelocity = Vector3.Reflect(currentVelocity, contact.normal);
        bouncesRemaining--;
        rb.position += contact.normal * 0.02f;
        rb.linearVelocity = reflectedVelocity;
        launchPosition = rb.position;
        isGravityDelayed = false;
        return true;
    }

    private void DespawnOrDestroy()
    {
        if (NetworkManager.Singleton != null && IsSpawned && TryGetComponent<NetworkObject>(out NetworkObject networkObject))
        {
            if (impactEffectPrefab != null)
            {
                GameObject impactEffect = Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
                if (impactEffect.TryGetComponent<NetworkObject>(out NetworkObject impactNetworkObject))
                {
                    impactNetworkObject.Spawn();
                }
            }
            
            networkObject.Despawn();
            return;
        }

        
        if (impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
        }
        
        Destroy(gameObject);
        
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
