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
    private void CacheAnimatorParameters()
    {
        animatorParameters.Clear();

        if (!enableAnimations)
        {
            return;
        }

        if (characterAnimator == null && autoFindAnimator)
        {
            characterAnimator = GetComponentInChildren<Animator>(true);
        }

        if (characterAnimator == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in characterAnimator.parameters)
        {
            animatorParameters.Add(parameter.name);
        }

        CacheOuterAnimator();
    }

    private void CacheOuterAnimator()
    {
        outerAnimatorParameters.Clear();
        FindGooberReferences();

        if (outerGoober == null)
        {
            return;
        }

        outerAnimator = outerGoober.GetComponentInChildren<Animator>(true);
        outerNetworkAnimator = outerGoober.GetComponentInChildren<Unity.Netcode.Components.NetworkAnimator>(true);

        if (outerAnimator == characterAnimator)
        {
            outerAnimator = null;
            outerNetworkAnimator = null;
        }

        if (outerAnimator != null)
        {
            foreach (AnimatorControllerParameter parameter in outerAnimator.parameters)
            {
                outerAnimatorParameters.Add(parameter.name);
            }
        }

        if (outerShootPoint == null && shootPoint != null)
        {
            foreach (Transform child in outerGoober.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == shootPoint.name)
                {
                    outerShootPoint = child;
                    break;
                }
            }
        }
    }

    private bool HasAnimatorParameter(string parameterName)
    {
        return !string.IsNullOrEmpty(parameterName) && characterAnimator != null && animatorParameters.Contains(parameterName);
    }

    private bool HasOuterAnimatorParameter(string parameterName)
    {
        return !string.IsNullOrEmpty(parameterName) && outerAnimator != null && outerAnimator.isActiveAndEnabled && outerAnimatorParameters.Contains(parameterName);
    }

    private void SetAnimatorFloat(string parameterName, float value)
    {
        SetAnimatorFloat(parameterName, value, animationDampTime);
    }

    private void SetAnimatorFloat(string parameterName, float value, float dampTime)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetFloat(parameterName, value, dampTime, Time.deltaTime);
        }

        SetOuterAnimatorFloat(parameterName, value, dampTime);
    }

    private void SetOuterAnimatorFloat(string parameterName, float value, float dampTime)
    {
        if (HasOuterAnimatorParameter(parameterName))
        {
            outerAnimator.SetFloat(parameterName, value, dampTime, Time.deltaTime);
        }
    }

    private void SetAnimatorBool(string parameterName, bool value)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetBool(parameterName, value);
        }

        if (HasOuterAnimatorParameter(parameterName))
        {
            outerAnimator.SetBool(parameterName, value);
        }
    }

    private void SetAnimatorTrigger(string parameterName)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetTrigger(parameterName);
        }

        if (HasOuterAnimatorParameter(parameterName))
        {
            // Triggers only replicate through NetworkAnimator.SetTrigger.
            if (outerNetworkAnimator != null && IsSpawned && IsOwner)
            {
                outerNetworkAnimator.SetTrigger(parameterName);
            }
            else
            {
                outerAnimator.SetTrigger(parameterName);
            }
        }
    }

    // LookLayer sits above UpperBody and its LookUp/LookDown clips key the whole right arm (shoulder to hand),
    // so it overwrites the right arm of the reload clip while the left arm (only LeftArm keyed) still plays it.
    // Fade the layer out while reloading so UpperBody's reload pose reaches both arms.
    private void UpdateLookLayerWeight(Animator animator)
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        int layer = animator.GetLayerIndex("LookLayer");

        if (layer < 0)
        {
            return;
        }

        float target = isReloading ? 0f : 1f;
        float current = animator.GetLayerWeight(layer);
        animator.SetLayerWeight(layer, Mathf.MoveTowards(current, target, Time.deltaTime / .15f));
    }

    private void UpdateAnimator()
    {
        if (!enableAnimations)
        {
            return;
        }

        if (characterAnimator == null)
        {
            CacheAnimatorParameters();
        }

        if (characterAnimator == null)
        {
            return;
        }

        float horizontalSpeed = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;
        bool isMovingNow = horizontalSpeed > .1f && isGrounded;

        SetAnimatorFloat(moveXParameter, moveXInput);
        SetAnimatorFloat(moveYParameter, moveYInput);
        SetAnimatorFloat(speedParameter, horizontalSpeed);
        SetAnimatorBool(isMovingParameter, isMovingNow);
        SetAnimatorBool(isSprintingParameter, isSprinting);
        SetAnimatorBool(isGroundedParameter, isGrounded);
        SetAnimatorBool(isCrouchedParameter, isCrouched);
        SetAnimatorBool(isReloadingParameter, isReloading);

        UpdateLookLayerWeight(characterAnimator);
        UpdateLookLayerWeight(outerAnimator);

        // Only the outer body needs LookValue: the inner body is parented to the camera and already pitches with it.
        SetOuterAnimatorFloat(lookValueParameter, Mathf.InverseLerp(-maxLookAngle, maxLookAngle, pitch), 0f);
    }
}
