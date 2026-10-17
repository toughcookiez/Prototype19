using System;
using UnityEngine;

public class DummyBehaviour : MonoBehaviour
{
    [SerializeField] private Vector2 cooldownRange = new Vector2(5f, 10f);

    [SerializeField] private float startDelay = 0f;
    
    [SerializeField] private GameObject projectilePrefab;
    
    [SerializeField] private Transform projectileSpawnPoint;
    
    private Animator _animator;

    private float _timer;
    
    
    
    
    private void Start()
    {
        cooldownRange = new Vector2(Mathf.Max(0f, cooldownRange.x), Mathf.Max(0f, cooldownRange.y));
        
        _timer = UnityEngine.Random.Range(cooldownRange.x, cooldownRange.y);
        
        _animator = GetComponent<Animator>();
        if (_animator == null)
        {
            Debug.LogError("Animator component not found on the GameObject.");
        }
        
        if (startDelay > 0f)
        {
            _timer += startDelay;
        }
    }

    private void Update()
    {
        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            _animator.SetTrigger("Shoot");
            _timer = UnityEngine.Random.Range(cooldownRange.x, cooldownRange.y);
        }
    }
    
    public void SpawnProjectile()
    {
        GameObject bullet = new GameObject();
        
        if (projectilePrefab != null && projectileSpawnPoint != null)
        {
            bullet = Instantiate(projectilePrefab, projectileSpawnPoint.position, projectileSpawnPoint.rotation);
        }
        else
        {
            Debug.LogError("Projectile prefab or spawn point is not assigned.");
        }
        
        if (bullet != null)
        {
            Rigidbody rb = bullet.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.LogError("Rigidbody component not found on the projectile prefab.");
                return;
            }
            
            if (rb.isKinematic)
            {
                rb.isKinematic = false;
                rb.WakeUp();
            }
                
                
            float randomForce = UnityEngine.Random.Range(10f, 15f); // Adjust the range as needed
            
            rb.linearVelocity = bullet.transform.forward * randomForce; // Adjust the speed as needed
            
        }
    }
}
