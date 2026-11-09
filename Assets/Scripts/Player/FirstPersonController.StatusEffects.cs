using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Debuffs applied by enemy bullets (Chilly Shells stun, Dimsighted nearsight). The server decides on a hit
// and tells the victim's owner, who is the only machine that needs the effect (movement is owner-authoritative).
public partial class FirstPersonController
{
    private static readonly Color StunTintColor = new Color(0.55f, 0.85f, 1f, 1f);
    private const float StunTintOpacity = 0.28f;
    private const float NearsightFadeIn = 0.2f;
    private const float NearsightFadeOut = 0.5f;
    private const float NearsightViewDistance = 7f;
    private static readonly Color NearsightFogColor = new Color(0.05f, 0.05f, 0.07f, 1f);

    private float stunnedUntil;
    private float nearsightStart;
    private float nearsightUntil;
    private VisualElement stunTintElement;

    public bool IsStunned => Time.time < stunnedUntil;

    // Server only. Call after a bullet actually damaged this player.
    public void ApplyStunServer(float duration)
    {
        if (!IsServer || !IsSpawned || duration <= 0f)
        {
            return;
        }

        // Sent to everyone: the owner is frozen, every other machine shows the stunned body.
        ApplyStunClientRpc(duration);
    }

    public void ApplyNearsightServer(float duration)
    {
        if (!IsServer || !IsSpawned || duration <= 0f)
        {
            return;
        }

        ApplyNearsightClientRpc(duration, OwnerOnlyRpcParams());
    }

    private ClientRpcParams OwnerOnlyRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
    }

    [ClientRpc]
    private void ApplyStunClientRpc(float duration)
    {
        stunnedUntil = Time.time + duration;

        if (!HasInputAuthority() && !stunBodyTintActive)
        {
            StartCoroutine(StunBodyTintRoutine());
        }
    }

    private static Material stunIceMaterial;
    private bool stunBodyTintActive;

    // Turns the stunned player's outer body icy blue for as long as the stun lasts (extended if re-stunned),
    // then restores the exact previous materials. Mirrors the white block flash.
    private System.Collections.IEnumerator StunBodyTintRoutine()
    {
        if (stunIceMaterial == null)
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

            Color ice = new Color(0.35f, 0.75f, 1f, 1f);
            stunIceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            stunIceMaterial.color = ice;
            if (stunIceMaterial.HasProperty("_BaseColor"))
            {
                stunIceMaterial.SetColor("_BaseColor", ice);
            }
        }

        GameObject root = outerGoober != null ? outerGoober : gameObject;
        var renderers = new System.Collections.Generic.List<Renderer>();
        var originals = new System.Collections.Generic.List<Material[]>();

        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer))
            {
                continue;
            }

            Material[] mats = r.sharedMaterials;
            Material[] ice = new Material[mats.Length];
            for (int i = 0; i < ice.Length; i++)
            {
                ice[i] = stunIceMaterial;
            }

            renderers.Add(r);
            originals.Add(mats);
            r.sharedMaterials = ice;
        }

        stunBodyTintActive = true;
        while (Time.time < stunnedUntil)
        {
            yield return null;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].sharedMaterials = originals[i];
            }
        }
        stunBodyTintActive = false;
    }

    [ClientRpc]
    private void ApplyNearsightClientRpc(float duration, ClientRpcParams clientRpcParams = default)
    {
        nearsightStart = Time.time;
        nearsightUntil = Time.time + duration;
    }

    private void ClearStatusEffects()
    {
        stunnedUntil = 0f;
        nearsightUntil = 0f;
        UpdateStatusEffects();
    }

    // Polled every frame by the owner.
    private void UpdateStatusEffects()
    {
        UpdateStunTint();
        UpdateNearsight();
    }

    private void UpdateStunTint()
    {
        bool stunned = IsStunned;

        if (stunTintElement == null || stunTintElement.panel == null
            || healthUiDocument == null || healthUiDocument.rootVisualElement == null
            || stunTintElement.panel != healthUiDocument.rootVisualElement.panel)
        {
            stunTintElement = null;

            if (!stunned || healthUiDocument == null || healthUiDocument.rootVisualElement == null
                || healthUiDocument.rootVisualElement.panel == null)
            {
                return;
            }

            stunTintElement = new VisualElement { name = "StunTint", pickingMode = PickingMode.Ignore };
            stunTintElement.style.position = Position.Absolute;
            stunTintElement.style.left = 0;
            stunTintElement.style.top = 0;
            stunTintElement.style.right = 0;
            stunTintElement.style.bottom = 0;
            stunTintElement.style.backgroundColor = StunTintColor;
            stunTintElement.style.opacity = 0f;
            healthUiDocument.rootVisualElement.Add(stunTintElement);
        }

        float wantedOpacity = stunned ? StunTintOpacity : 0f;
        if (!Mathf.Approximately(stunTintElement.resolvedStyle.opacity, wantedOpacity))
        {
            stunTintElement.style.opacity = wantedOpacity;
        }
    }

    // Nearsighted = the view stays sharp but only reaches a short, perfectly round distance around the player.
    // Dark spheres centered on the camera hide everything beyond their radius (anything closer is in front of
    // them and so stays visible); the inner shells are translucent so the edge fades in softly.
    private static readonly float[] NearsightShellScales = { 0.6f, 0.8f, 1f };
    private static readonly float[] NearsightShellAlphas = { 0.3f, 0.5f, 1f };
    private const float NearsightStartRadius = 40f;
    private GameObject[] nearsightShells;
    private Material nearsightMaterial;

    private void UpdateNearsight()
    {
        bool active = Time.time < nearsightUntil && playerCamera != null;

        if (!active)
        {
            RestoreNearsight();
            return;
        }

        if (nearsightShells == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }

            nearsightMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            nearsightShells = new GameObject[NearsightShellScales.Length];

            for (int i = 0; i < nearsightShells.Length; i++)
            {
                GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shell.name = "NearsightShell";
                shell.hideFlags = HideFlags.HideAndDontSave;
                Destroy(shell.GetComponent<Collider>());

                Renderer shellRenderer = shell.GetComponent<Renderer>();
                shellRenderer.sharedMaterial = nearsightMaterial;
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                Color color = NearsightFogColor;
                color.a = NearsightShellAlphas[i];
                block.SetColor("_Color", color);
                block.SetColor("_RendererColor", color);
                shellRenderer.SetPropertyBlock(block);

                nearsightShells[i] = shell;
            }
        }

        float weight = Mathf.Min(Mathf.Clamp01((Time.time - nearsightStart) / NearsightFadeIn),
            Mathf.Clamp01((nearsightUntil - Time.time) / NearsightFadeOut));
        float radius = Mathf.Lerp(NearsightStartRadius, NearsightViewDistance, weight);
        Vector3 center = playerCamera.transform.position;

        for (int i = 0; i < nearsightShells.Length; i++)
        {
            if (nearsightShells[i] == null)
            {
                continue;
            }

            nearsightShells[i].transform.position = center;
            nearsightShells[i].transform.localScale = Vector3.one * (radius * NearsightShellScales[i] * 2f);
        }
    }

    private void RestoreNearsight()
    {
        if (nearsightShells == null)
        {
            return;
        }

        foreach (GameObject shell in nearsightShells)
        {
            if (shell != null)
            {
                Destroy(shell);
            }
        }

        nearsightShells = null;

        if (nearsightMaterial != null)
        {
            Destroy(nearsightMaterial);
            nearsightMaterial = null;
        }
    }

    private void OnDisable()
    {
        RestoreNearsight();
    }
}
