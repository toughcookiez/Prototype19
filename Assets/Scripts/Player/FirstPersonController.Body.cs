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
    private void SetRagdollState(bool isRagdollActive)
    {
        ApplyRagdollState(isRagdollActive);

        if (IsSpawned)
        {
            SetRagdollStateClientRpc(isRagdollActive);
        }
    }

    [ClientRpc]
    private void SetRagdollStateClientRpc(bool isRagdollActive)
    {
        ApplyRagdollState(isRagdollActive);
    }

    private void ApplyRagdollState(bool isRagdollActive)
    {
        if (!enableRagdollOnDeath)
        {
            isRagdollActive = false;
        }

        if (rb != null)
        {
            if (isRagdollActive)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.useGravity = false;
                rb.detectCollisions = false;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
            else
            {
                rb.isKinematic = rootRbWasKinematic;
                rb.useGravity = rootRbUseGravity;
                rb.detectCollisions = rootRbDetectCollisions;
                rb.interpolation = rootRbInterpolation;
                rb.collisionDetectionMode = rootRbCollisionDetectionMode;
            }
        }

        if (normalCollider != null)
        {
            normalCollider.enabled = !isRagdollActive;
        }

        if (isRagdollActive && disableAnimatorOnRagdoll && characterAnimator != null)
        {
            characterAnimator.enabled = false;
        }

        if (ragdollRigidbodies != null)
        {
            foreach (Rigidbody ragdollRigidbody in ragdollRigidbodies)
            {
                if (ragdollRigidbody == null)
                {
                    continue;
                }

                if (isRagdollActive)
                {
                    ragdollRigidbody.isKinematic = false;
                    ragdollRigidbody.detectCollisions = true;
                    ragdollRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    ragdollRigidbody.WakeUp();
                }
                else
                {
                    if (!ragdollRigidbody.isKinematic)
                    {
                        ragdollRigidbody.linearVelocity = Vector3.zero;
                        ragdollRigidbody.angularVelocity = Vector3.zero;
                    }

                    ragdollRigidbody.isKinematic = true;
                    ragdollRigidbody.detectCollisions = false;
                    ragdollRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
                }
            }
        }

        if (ragdollColliders != null)
        {
            foreach (Collider ragdollCollider in ragdollColliders)
            {
                if (ragdollCollider == null)
                {
                    continue;
                }

                ragdollCollider.enabled = true;
                ragdollCollider.isTrigger = !isRagdollActive;
            }
        }

        if (!isRagdollActive)
        {
            RestoreRagdollPose();

            if (disableAnimatorOnRagdoll && characterAnimator != null)
            {
                characterAnimator.enabled = animatorWasEnabled;
            }
        }
    }

    private void StopRigidbodyMotion()
    {
        if (rb == null || rb.isKinematic)
        {
            return;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private bool IsAlive()
    {
        return !enablePlayerDamage || !IsSpawned || currentHealth.Value > 0f;
    }

    private bool HasInputAuthority()
    {
        bool hasAuthority = !enableMultiplayerAuthority || !IsSpawned || IsOwner;
        return hasAuthority && IsAlive() && !controlsDisabledAfterDeath;
    }

    private void ConfigureLocalView()
    {
        bool isLocalPlayer = HasInputAuthority();
        ResolveMatchUiDocuments();

        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(isLocalPlayer);

            if (isLocalPlayer)
            {
                SetExclusiveAudioListeners(playerCamera);
            }
            else
            {
                SetAudioListenersEnabled(playerCamera, false);
            }
        }

        SetPlayerUiActive(isLocalPlayer);
        SetCardsUiActive(false);

        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            canvas.gameObject.SetActive(isLocalPlayer);
        }

        foreach (UIDocument uiDocument in GetComponentsInChildren<UIDocument>(true))
        {
            if (uiDocument == playerUiDocument || uiDocument == cardsUiDocument)
            {
                continue;
            }

            uiDocument.gameObject.SetActive(isLocalPlayer);
        }

        ApplySecondPlayerBodyMaterial();
        ApplyBodyShadowMode(isLocalPlayer);
    }

    private void FindGooberReferences()
    {
        Transform searchRoot = bodyRoot != null ? bodyRoot : transform;

        if (innerGoober == null)
        {
            foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Trim().Equals("InnerGoober", System.StringComparison.OrdinalIgnoreCase) ||
                    child.name.Contains("InnerGoober"))
                {
                    innerGoober = child.gameObject;
                    break;
                }
            }
        }

        if (outerGoober == null)
        {
            foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Trim().Equals("OuterGoober", System.StringComparison.OrdinalIgnoreCase) ||
                    child.name.Contains("OuterGoober"))
                {
                    outerGoober = child.gameObject;
                    break;
                }
            }
        }
    }

    private void CacheBodyRenderers()
    {
        FindGooberReferences();

        if ((playerBodyRenderers == null || playerBodyRenderers.Length == 0) && autoFindBodyRenderers)
        {
            Transform searchRoot = bodyRoot != null ? bodyRoot : transform;
            Renderer[] renderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            List<Renderer> filteredRenderers = new List<Renderer>();

            foreach (Renderer bodyRenderer in renderers)
            {
                if (bodyRenderer == null || (playerCamera != null && bodyRenderer.transform.IsChildOf(playerCamera.transform)))
                {
                    continue;
                }

                filteredRenderers.Add(bodyRenderer);
            }

            playerBodyRenderers = filteredRenderers.ToArray();
        }

        originalBodyShadowModes = new ShadowCastingMode[playerBodyRenderers != null ? playerBodyRenderers.Length : 0];

        for (int i = 0; i < originalBodyShadowModes.Length; i++)
        {
            originalBodyShadowModes[i] = playerBodyRenderers[i] != null ? playerBodyRenderers[i].shadowCastingMode : ShadowCastingMode.On;
        }
    }

    private void CacheOriginalBodyMaterials()
    {
        if (playerBodyRenderers == null)
        {
            originalBodyMaterials = null;
            return;
        }

        originalBodyMaterials = new Material[playerBodyRenderers.Length][];

        for (int i = 0; i < playerBodyRenderers.Length; i++)
        {
            Renderer bodyRenderer = playerBodyRenderers[i];

            if (bodyRenderer == null)
            {
                continue;
            }

            originalBodyMaterials[i] = bodyRenderer.materials;
        }
    }

    private bool IsSecondPlayerController()
    {
        // The host (server) is always player 1; any other owner is the second player.
        // This is known on every machine as soon as the object spawns, so it doesn't
        // depend on the other player's object having spawned yet.
        if (!IsSpawned || NetworkManager == null || secondPlayerBodyMaterial == null)
        {
            return false;
        }

        return OwnerClientId != NetworkManager.ServerClientId;
    }

    private Material[] GetOriginalMaterials(Renderer renderer)
    {
        if (renderer == null)
        {
            return null;
        }

        if (playerBodyRenderers != null && originalBodyMaterials != null)
        {
            int index = System.Array.IndexOf(playerBodyRenderers, renderer);
            if (index >= 0 && index < originalBodyMaterials.Length)
            {
                return originalBodyMaterials[index];
            }
        }

        return null;
    }

    private void ApplySecondPlayerBodyMaterial()
    {
        FindGooberReferences();
        bool isSecondPlayer = IsSecondPlayerController();

        ApplyGooberBodyMaterial(outerGoober, isSecondPlayer);
        ApplyGooberBodyMaterial(innerGoober, isSecondPlayer);
    }

    // Only the skinned meshes (Body/Arms) are recoloured. The goober also contains the gun's
    // MeshRenderers, which must keep their own material. Slot 0 is the body colour.
    private void ApplyGooberBodyMaterial(GameObject goober, bool isSecondPlayer)
    {
        if (goober == null)
        {
            return;
        }

        foreach (SkinnedMeshRenderer skinned in goober.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (isSecondPlayer && secondPlayerBodyMaterial != null)
            {
                Material[] materials = skinned.sharedMaterials;
                if (materials != null && materials.Length > 0)
                {
                    materials[0] = secondPlayerBodyMaterial;
                    skinned.sharedMaterials = materials;
                }
            }
            else
            {
                Material[] original = GetOriginalMaterials(skinned);
                if (original != null)
                {
                    skinned.sharedMaterials = original;
                }
            }
        }
    }

    private void CacheRagdollParts()
    {
        if (normalCollider == null)
        {
            normalCollider = GetComponent<Collider>();
        }

        if (!autoFindRagdollParts)
        {
            return;
        }

        Transform searchRoot = ragdollRoot != null ? ragdollRoot : bodyRoot != null ? bodyRoot : transform;

        if (ragdollRigidbodies == null || ragdollRigidbodies.Length == 0)
        {
            Rigidbody[] foundRigidbodies = searchRoot.GetComponentsInChildren<Rigidbody>(true);
            List<Rigidbody> filteredRigidbodies = new List<Rigidbody>();

            foreach (Rigidbody ragdollRigidbody in foundRigidbodies)
            {
                if (ragdollRigidbody == null || ragdollRigidbody == rb)
                {
                    continue;
                }

                filteredRigidbodies.Add(ragdollRigidbody);
            }

            ragdollRigidbodies = filteredRigidbodies.ToArray();
        }

        if (ragdollColliders == null || ragdollColliders.Length == 0)
        {
            Collider[] foundColliders = searchRoot.GetComponentsInChildren<Collider>(true);
            List<Collider> filteredColliders = new List<Collider>();

            foreach (Collider ragdollCollider in foundColliders)
            {
                if (ragdollCollider == null || ragdollCollider == normalCollider)
                {
                    continue;
                }

                filteredColliders.Add(ragdollCollider);
            }

            ragdollColliders = filteredColliders.ToArray();
        }
    }

    private void CacheRagdollPose()
    {
        if (ragdollRigidbodies == null || ragdollRigidbodies.Length == 0)
        {
            ragdollTransforms = new Transform[0];
            ragdollLocalPositions = new Vector3[0];
            ragdollLocalRotations = new Quaternion[0];
            ragdollLocalScales = new Vector3[0];
            return;
        }

        List<Transform> cachedTransforms = new List<Transform>();

        foreach (Rigidbody ragdollRigidbody in ragdollRigidbodies)
        {
            if (ragdollRigidbody == null || ragdollRigidbody.transform == null)
            {
                continue;
            }

            cachedTransforms.Add(ragdollRigidbody.transform);
        }

        ragdollTransforms = cachedTransforms.ToArray();
        ragdollLocalPositions = new Vector3[ragdollTransforms.Length];
        ragdollLocalRotations = new Quaternion[ragdollTransforms.Length];
        ragdollLocalScales = new Vector3[ragdollTransforms.Length];

        for (int i = 0; i < ragdollTransforms.Length; i++)
        {
            ragdollLocalPositions[i] = ragdollTransforms[i].localPosition;
            ragdollLocalRotations[i] = ragdollTransforms[i].localRotation;
            ragdollLocalScales[i] = ragdollTransforms[i].localScale;
        }
    }

    private void RestoreRagdollPose()
    {
        if (ragdollTransforms == null || ragdollLocalPositions == null || ragdollLocalRotations == null || ragdollLocalScales == null)
        {
            return;
        }

        int count = Mathf.Min(ragdollTransforms.Length, ragdollLocalPositions.Length, ragdollLocalRotations.Length, ragdollLocalScales.Length);

        for (int i = 0; i < count; i++)
        {
            Transform ragdollTransform = ragdollTransforms[i];

            if (ragdollTransform == null)
            {
                continue;
            }

            ragdollTransform.localPosition = ragdollLocalPositions[i];
            ragdollTransform.localRotation = ragdollLocalRotations[i];
            ragdollTransform.localScale = ragdollLocalScales[i];
        }
    }

    private void SetOuterGooberPresence(bool isPresent)
    {
        if (outerGoober == null)
        {
            return;
        }

        foreach (Renderer outerRenderer in outerGoober.GetComponentsInChildren<Renderer>(true))
        {
            if (outerRenderer != null)
            {
                outerRenderer.enabled = isPresent;
            }
        }

        foreach (Collider outerCollider in outerGoober.GetComponentsInChildren<Collider>(true))
        {
            if (outerCollider != null)
            {
                outerCollider.enabled = isPresent;
            }
        }
    }

    private void ApplyBodyShadowMode(bool isLocalPlayer)
    {
        FindGooberReferences();

        if (innerGoober != null && outerGoober != null)
        {
            if (isLocalPlayer)
            {
                // Local player view:
                // InnerGoober visible with ShadowCastingMode.On so the local player sees their own body.
                // OuterGoober stays active but hidden (no renderers/colliders) so its Animator keeps running
                // and its OwnerNetworkAnimator can sync the pose other players see.
                innerGoober.SetActive(true);
                outerGoober.SetActive(true);
                SetOuterGooberPresence(false);

                Renderer[] innerRenderers = innerGoober.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in innerRenderers)
                {
                    if (r != null)
                    {
                        r.shadowCastingMode = ShadowCastingMode.On;
                    }
                }
            }
            else
            {
                // Remote player view:
                // InnerGoober inactive (remote players don't need inner first-person body).
                // OuterGoober active with ShadowCastingMode.On so other players see the outer body.
                innerGoober.SetActive(false);
                outerGoober.SetActive(true);
                SetOuterGooberPresence(true);

                Renderer[] outerRenderers = outerGoober.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in outerRenderers)
                {
                    if (r != null)
                    {
                        r.shadowCastingMode = ShadowCastingMode.On;
                    }
                }
            }
            return;
        }

        // Fallback if innerGoober or outerGoober is not assigned
        if (playerBodyRenderers == null)
        {
            return;
        }

        for (int i = 0; i < playerBodyRenderers.Length; i++)
        {
            Renderer bodyRenderer = playerBodyRenderers[i];

            if (bodyRenderer == null)
            {
                continue;
            }

            bool isOuterGoober = i == 1;
            GameObject gooberObj = bodyRenderer.gameObject;

            if (isLocalPlayer)
            {
                if (isOuterGoober)
                {
                    gooberObj.SetActive(false);
                }
                else
                {
                    gooberObj.SetActive(true);
                    bodyRenderer.shadowCastingMode = ShadowCastingMode.On;
                }
            }
            else
            {
                if (isOuterGoober)
                {
                    gooberObj.SetActive(true);
                    bodyRenderer.shadowCastingMode = ShadowCastingMode.On;
                }
                else
                {
                    gooberObj.SetActive(false);
                }
            }
        }
    }
}
