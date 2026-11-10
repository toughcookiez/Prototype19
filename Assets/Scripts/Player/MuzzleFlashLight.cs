using UnityEngine;

// Fades out and disables the flash's point light quickly so it doesn't outlive the particle burst.
[RequireComponent(typeof(Light))]
public class MuzzleFlashLight : MonoBehaviour
{
    public float duration = 0.06f;

    private Light flashLight;
    private float startIntensity;
    private float elapsed;

    private void Awake()
    {
        flashLight = GetComponent<Light>();
        startIntensity = flashLight.intensity;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
        flashLight.intensity = startIntensity * (1f - t);

        if (t >= 1f)
        {
            flashLight.enabled = false;
            enabled = false;
        }
    }
}
