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
    private float nextBlockTime;

    public bool IsBlocking()
    {
        return isBlocking || (IsSpawned && isShieldActive.Value);
    }

    private void OnShieldActiveChanged(bool previousValue, bool newValue)
    {
        isBlocking = newValue;
        if (shieldObject != null)
        {
            shieldObject.SetActive(newValue);
            if (newValue)
            {
                if (shieldOpacityCoroutine != null)
                {
                    StopCoroutine(shieldOpacityCoroutine);
                }
                shieldOpacityCoroutine = StartCoroutine(ShieldOpacityRoutine());
            }
        }
    }

    public bool IsShieldCollider(Collider col)
    {
        if (shieldObject == null || col == null)
        {
            return false;
        }

        return col.transform == shieldObject.transform || col.transform.IsChildOf(shieldObject.transform);
    }

    private void TryBlock()
    {
        if (isBlocking || isBlockCooldown || Time.time < nextBlockTime)
        {
            return;
        }

        nextBlockTime = Time.time + blockCooldown;

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

    [ServerRpc(RequireOwnership = false)]
    private void RequestBlockServerRpc(ServerRpcParams serverRpcParams = default)
    {
        StartCoroutine(BlockRoutineServer());
    }

    private IEnumerator BlockRoutineServer()
    {
        isShieldActive.Value = true;
        yield return new WaitForSeconds(blockDuration);
        isShieldActive.Value = false;
    }

    private IEnumerator BlockRoutineLocal()
    {
        isBlocking = true;
        if (shieldObject != null)
        {
            shieldObject.SetActive(true);
            if (shieldOpacityCoroutine != null)
            {
                StopCoroutine(shieldOpacityCoroutine);
            }
            shieldOpacityCoroutine = StartCoroutine(ShieldOpacityRoutine());
        }

        yield return new WaitForSeconds(blockDuration);

        if (shieldObject != null)
        {
            shieldObject.SetActive(false);
        }
        isBlocking = false;
        blockCoroutine = null;
    }

    private IEnumerator ShieldOpacityRoutine()
    {
        if (shieldRenderers == null || shieldRenderers.Length == 0)
        {
            if (shieldObject != null)
            {
                shieldRenderers = shieldObject.GetComponentsInChildren<Renderer>(true);
            }
        }

        string[] candidateNames = new string[] {
            "opacity (Total)",
            "_OpacityTotal",
            "_Opacity_Total",
            "_opacity_Total",
            "Opacity_Total",
            "OpacityTotal",
            "_Opacity"
        };

        float elapsed = 0f;
        while (elapsed < blockDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / blockDuration);
            float opacity = Mathf.Sin(t * Mathf.PI);

            if (shieldRenderers != null)
            {
                foreach (Renderer rend in shieldRenderers)
                {
                    if (rend != null && rend.material != null)
                    {
                        Material mat = rend.material;
                        foreach (string name in candidateNames)
                        {
                            if (mat.HasProperty(name))
                            {
                                mat.SetFloat(name, opacity);
                            }
                        }

                        Shader shader = mat.shader;
                        if (shader != null)
                        {
                            int propCount = shader.GetPropertyCount();
                            for (int i = 0; i < propCount; i++)
                            {
                                string propName = shader.GetPropertyName(i);
                                if (!string.IsNullOrEmpty(propName))
                                {
                                    string lower = propName.ToLower();
                                    if (lower.Contains("opacity") || lower.Contains("total"))
                                    {
                                        mat.SetFloat(propName, opacity);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            yield return null;
        }

        if (shieldRenderers != null)
        {
            foreach (Renderer rend in shieldRenderers)
            {
                if (rend != null && rend.material != null)
                {
                    Material mat = rend.material;
                    foreach (string name in candidateNames)
                    {
                        if (mat.HasProperty(name))
                        {
                            mat.SetFloat(name, 0f);
                        }
                    }

                    Shader shader = mat.shader;
                    if (shader != null)
                    {
                        int propCount = shader.GetPropertyCount();
                        for (int i = 0; i < propCount; i++)
                        {
                            string propName = shader.GetPropertyName(i);
                            if (!string.IsNullOrEmpty(propName))
                            {
                                string lower = propName.ToLower();
                                if (lower.Contains("opacity") || lower.Contains("total"))
                                {
                                    mat.SetFloat(propName, 0f);
                                }
                            }
                        }
                    }
                }
            }
        }

        shieldOpacityCoroutine = null;
    }
}
