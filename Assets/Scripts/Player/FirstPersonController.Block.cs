using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

// ROUNDS-style block: pressing the block key gives a very short window (blockDuration) in which every
// bullet that touches the player is destroyed and no damage is taken. A white shockwave ring plays on
// every machine. After the window the block recharges for blockCooldown seconds.
public partial class FirstPersonController
{
    private const float BlockRingLifetime = 0.45f;
    private const float BlockRingTextureSize = 128;

    private static Texture2D blockRingTexture;
    private static Material blockRingMaterialTemplate;

    private float nextBlockTime;
    // Counter Slug: set when a block starts, consumed by the next bullet fired.
    private bool counterSlugArmed;
    private float blockStartTime = -999f;
    // Server-side guard so a client cannot spam RequestBlockServerRpc. Slightly lenient to absorb latency.
    private float serverNextBlockTime;

    public bool IsBlocking()
    {
        return isBlocking || (IsSpawned && isShieldActive.Value);
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        isBlocking = newValue;
        if (newValue)
        {
            PlayBlockEffect();
        }
    }

    private void TryBlock()
    {
        if (isBlocking || isBlockCooldown || IsStunned || Time.time < nextBlockTime)
        {
            return;
        }

        blockStartTime = Time.time;
        counterSlugArmed = hasCounterSlug;
        // The cooldown starts counting once the block window has ended.
        nextBlockTime = Time.time + blockDuration + blockCooldown;

        if (IsServer)
        {
            StartCoroutine(BlockRoutineServer());
        }
        else if (IsSpawned)
        {
            RequestBlockServerRpc();
        }
        else
        {
            if (blockCoroutine != null)
            {
                StopCoroutine(blockCoroutine);
            }
            blockCoroutine = StartCoroutine(BlockRoutineLocal());
        }
    }

    // Called on round reset so a fresh round always starts with the block ready.
    private void ResetBlockState()
    {
        nextBlockTime = 0f;
        serverNextBlockTime = 0f;
        blockStartTime = -999f;
        counterSlugArmed = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBlockServerRpc(ServerRpcParams serverRpcParams = default)
    {
        StartCoroutine(BlockRoutineServer());
    }

    private IEnumerator BlockRoutineServer()
    {
        if (isShieldActive.Value || Time.time < serverNextBlockTime)
        {
            yield break;
        }

        serverNextBlockTime = Time.time + blockDuration + Mathf.Max(0f, blockCooldown - 0.15f);
        isShieldActive.Value = true;
        if (hasQuickSnack && IsAlive())
        {
            float healed = Mathf.Min(quickSnackHeal, maxHealth - currentHealth.Value);
            if (healed > 0f)
            {
                currentHealth.Value += healed;
                QuickSnackHealClientRpc(healed, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                });
            }
        }
        yield return new WaitForSeconds(blockDuration);
        isShieldActive.Value = false;
    }

    [ClientRpc]
    private void QuickSnackHealClientRpc(float amount, ClientRpcParams clientRpcParams = default)
    {
        FlashHealOverlay();
    }

    private IEnumerator BlockRoutineLocal()
    {
        isBlocking = true;
        PlayBlockEffect();
        yield return new WaitForSeconds(blockDuration);
        isBlocking = false;
        blockCoroutine = null;
    }

    #region Block Effect

    private void PlayBlockEffect()
    {
        // The owner sees the ring expand in front of their camera (their body is the camera);
        // everyone else sees it burst out of the blocking player's chest.
        bool firstPerson = HasInputAuthority() && playerCamera != null;
        StartCoroutine(BlockRingRoutine(firstPerson));

        if (!firstPerson && !HasInputAuthority() && !blockFlashActive)
        {
            StartCoroutine(BlockBodyFlashRoutine());
        }
    }

    private static Material blockWhiteMaterial;
    private bool blockFlashActive;

    // Turns the remote player's whole outer body solid white for the block window by swapping in an
    // unlit white material, then restores the exact previous materials.
    private IEnumerator BlockBodyFlashRoutine()
    {
        if (blockWhiteMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                yield break;
            }

            blockWhiteMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            blockWhiteMaterial.color = Color.white;
            if (blockWhiteMaterial.HasProperty("_BaseColor"))
            {
                blockWhiteMaterial.SetColor("_BaseColor", Color.white);
            }
        }

        GameObject root = outerGoober != null ? outerGoober : gameObject;
        System.Collections.Generic.List<Renderer> renderers = new System.Collections.Generic.List<Renderer>();
        System.Collections.Generic.List<Material[]> originals = new System.Collections.Generic.List<Material[]>();

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            Material[] mats = r.sharedMaterials;
            Material[] white = new Material[mats.Length];
            for (int i = 0; i < white.Length; i++)
            {
                white[i] = blockWhiteMaterial;
            }

            renderers.Add(r);
            originals.Add(mats);
            r.sharedMaterials = white;
        }

        blockFlashActive = true;
        yield return new WaitForSeconds(Mathf.Max(0.1f, blockDuration));

        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].sharedMaterials = originals[i];
            }
        }
        blockFlashActive = false;
    }

    private static void EnsureBlockRingAssets()
    {
        if (blockRingTexture == null)
        {
            int size = (int)BlockRingTextureSize;
            blockRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float ring = 1f - Mathf.SmoothStep(0f, 0.09f, Mathf.Abs(r - 0.88f));
                    float fill = r < 0.88f ? 0.14f * r * r : 0f;
                    float alpha = r > 1f ? 0f : Mathf.Clamp01(Mathf.Max(ring, fill));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            blockRingTexture.SetPixels32(pixels);
            blockRingTexture.Apply(false, true);
        }

        if (blockRingMaterialTemplate == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                blockRingMaterialTemplate = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                blockRingMaterialTemplate.mainTexture = blockRingTexture;
            }
        }
    }

    private IEnumerator BlockRingRoutine(bool firstPerson)
    {
        EnsureBlockRingAssets();
        if (blockRingMaterialTemplate == null)
        {
            yield break;
        }

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ring.name = "BlockRing";
        Collider ringCollider = ring.GetComponent<Collider>();
        if (ringCollider != null)
        {
            Destroy(ringCollider);
        }

        Renderer ringRenderer = ring.GetComponent<Renderer>();
        Material ringMaterial = new Material(blockRingMaterialTemplate);
        ringRenderer.sharedMaterial = ringMaterial;
        ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ringRenderer.receiveShadows = false;

        Destroy(ring, BlockRingLifetime + 0.25f);

        float startSize = firstPerson ? 0.6f : 0.3f;
        float endSize = firstPerson ? 3.2f : 2.2f;
        float elapsed = 0f;

        while (elapsed < BlockRingLifetime && ring != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / BlockRingLifetime);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);

            Camera viewCamera = firstPerson ? playerCamera : Camera.main;
            Vector3 position;
            if (firstPerson)
            {
                position = viewCamera.transform.position + viewCamera.transform.forward * 1.2f;
            }
            else
            {
                position = transform.position + Vector3.up * 1.0f;
            }

            ring.transform.position = position;
            ring.transform.localScale = Vector3.one * Mathf.Lerp(startSize, endSize, eased);

            if (viewCamera != null)
            {
                Vector3 away = position - viewCamera.transform.position;
                if (away.sqrMagnitude > 0.0001f)
                {
                    ring.transform.rotation = Quaternion.LookRotation(away, viewCamera.transform.up);
                }
            }

            float alpha = (1f - t) * (1f - t);
            ringMaterial.color = new Color(1f, 1f, 1f, alpha);
            yield return null;
        }

        if (ring != null)
        {
            Destroy(ring);
        }
        Destroy(ringMaterial);
    }

    #endregion
}
