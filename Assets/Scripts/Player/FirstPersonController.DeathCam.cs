using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Slow-motion overhead "kill cam" that every player sees for a moment after a round-ending death,
// before the card UI appears. The camera is purely local (created per machine); only the start is an RPC.
public partial class FirstPersonController
{
    // Real-time seconds the server waits before showing the card UI.
    private const float DeathCamDuration = 2.75f;
    // Local time scale while the kill cam is running.
    private const float DeathCamTimeScale = 0.2f;
    // Extra real seconds a client keeps the cam if the post-match RPC never arrives.
    private const float DeathCamFailsafeSeconds = 1.5f;
    private const float DeathCamFieldOfView = 55f;
    private const float DeathCamStartHeight = 6.5f;
    private const float DeathCamEndHeight = 4.5f;
    private const float DeathCamStartRadius = 2.5f;
    private const float DeathCamEndRadius = 1.5f;
    private const float DeathCamOrbitDegrees = 40f;

    private static Camera deathCamera;
    private static FirstPersonController deathCamOwner;
    private static Coroutine deathCamRoutine;
    private static float deathCamBaseFixedDeltaTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDeathCamStatics()
    {
        deathCamera = null;
        deathCamOwner = null;
        deathCamRoutine = null;
    }

    [ClientRpc]
    private void StartDeathCamClientRpc(float duration)
    {
        BeginDeathCam(duration + DeathCamFailsafeSeconds);
    }

    private void BeginDeathCam(float duration)
    {
        EndDeathCam();

        // The local loser normally sees only the first-person inner body; show the outer (ragdolling) body instead.
        // Not HasInputAuthority(): that is false once the player is dead.
        if (IsSpawned && IsOwner && outerGoober != null)
        {
            if (innerGoober != null)
            {
                innerGoober.SetActive(false);
            }

            outerGoober.SetActive(true);
            SetOuterGooberPresence(true);
        }

        foreach (FirstPersonController controller in FindObjectsOfType<FirstPersonController>(true))
        {
            if (controller != null)
            {
                controller.SetPlayerUiActive(false);
            }
        }

        GameObject cameraObject = new GameObject("DeathCam");
        deathCamera = cameraObject.AddComponent<Camera>();
        deathCamera.fieldOfView = DeathCamFieldOfView;
        deathCamera.nearClipPlane = 0.1f;
        deathCamera.farClipPlane = 500f;
        deathCamera.depth = 100f; // draws over every player camera

        deathCamBaseFixedDeltaTime = Time.fixedDeltaTime;
        Time.timeScale = DeathCamTimeScale;
        Time.fixedDeltaTime = deathCamBaseFixedDeltaTime * DeathCamTimeScale;

        deathCamOwner = this;
        deathCamRoutine = StartCoroutine(DeathCamRoutine(duration));
    }

    private IEnumerator DeathCamRoutine(float duration)
    {
        Vector3 focus = GetDeathCamFocus();
        float startAngle = transform.eulerAngles.y + 180f;
        float elapsed = 0f;
        bool first = true;

        while (elapsed < duration && deathCamera != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / DeathCamDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            focus = Vector3.Lerp(focus, GetDeathCamFocus(), 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));

            float angle = startAngle + eased * DeathCamOrbitDegrees;
            float radius = Mathf.Lerp(DeathCamStartRadius, DeathCamEndRadius, eased);
            float height = Mathf.Lerp(DeathCamStartHeight, DeathCamEndHeight, eased);

            Vector3 targetPosition = ResolveUnblockedDeathCamPosition(focus, angle, radius, height);
            Transform cam = deathCamera.transform;

            // Smooth so switching between candidate positions (when something gets in the way) glides instead of snapping.
            cam.position = first ? targetPosition : Vector3.Lerp(cam.position, targetPosition, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            first = false;
            cam.rotation = Quaternion.LookRotation(focus - cam.position, Vector3.up);

            yield return null;
        }

        EndDeathCam();
    }

    private static readonly RaycastHit[] deathCamHits = new RaycastHit[24];
    private static readonly float[] deathCamAzimuthOffsets = { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 180f };
    private static readonly float[] deathCamPitchFactors = { 1f, 0.6f, 0.3f };
    private const float DeathCamCollisionRadius = 0.35f;
    private const float DeathCamMinDistance = 1.2f;

    // Picks the camera position nearest to the ideal orbit that has a clear line to the focus.
    // Tries the ideal spot first, then other azimuths and lower pitches; if everything is blocked it
    // moves the camera in front of the nearest obstacle on the best candidate.
    private Vector3 ResolveUnblockedDeathCamPosition(Vector3 focus, float azimuthDegrees, float radius, float height)
    {
        float idealDistance = new Vector2(radius, height).magnitude;
        Vector3 bestPosition = focus + Vector3.up * DeathCamMinDistance;
        float bestDistance = -1f;

        foreach (float pitchFactor in deathCamPitchFactors)
        {
            // Lower the camera but keep roughly the same distance from the focus.
            float candidateHeight = height * pitchFactor;
            float candidateRadius = Mathf.Sqrt(Mathf.Max(0f, idealDistance * idealDistance - candidateHeight * candidateHeight));

            foreach (float azimuthOffset in deathCamAzimuthOffsets)
            {
                float angle = (azimuthDegrees + azimuthOffset) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle) * candidateRadius, candidateHeight, Mathf.Cos(angle) * candidateRadius).normalized;
                float clearDistance = GetClearDistance(focus, direction, idealDistance);

                if (clearDistance >= idealDistance - 0.01f)
                {
                    return focus + direction * idealDistance;
                }

                if (clearDistance > bestDistance)
                {
                    bestDistance = clearDistance;
                    bestPosition = focus + direction * Mathf.Max(clearDistance, DeathCamMinDistance);
                }
            }
        }

        return bestPosition;
    }

    // Distance from origin along direction (up to maxDistance) before world geometry blocks the camera.
    // Player bodies (including both ragdolls) are ignored.
    private float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        int hitCount = Physics.SphereCastNonAlloc(origin, DeathCamCollisionRadius, direction, deathCamHits, maxDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = maxDistance;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = deathCamHits[i];

            if (hit.collider == null || hit.collider.GetComponentInParent<FirstPersonController>() != null)
            {
                continue;
            }

            // Spherecasts that start inside a collider report distance 0 with a zero point; treat as unblocked there.
            if (hit.distance <= 0f && hit.point == Vector3.zero)
            {
                continue;
            }

            nearest = Mathf.Min(nearest, hit.distance);
        }

        return nearest;
    }

    private Vector3 GetDeathCamFocus()
    {
        if (ragdollRigidbodies != null && ragdollRigidbodies.Length > 0)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (Rigidbody ragdollRigidbody in ragdollRigidbodies)
            {
                if (ragdollRigidbody != null)
                {
                    sum += ragdollRigidbody.position;
                    count++;
                }
            }

            if (count > 0)
            {
                return sum / count;
            }
        }

        return transform.position + Vector3.up;
    }

    // Safe to call any time (also when no kill cam is running): restores time scale and removes the camera.
    private static void EndDeathCam()
    {
        if (deathCamOwner != null && deathCamRoutine != null)
        {
            deathCamOwner.StopCoroutine(deathCamRoutine);
        }

        deathCamRoutine = null;
        deathCamOwner = null;

        if (deathCamera != null)
        {
            Destroy(deathCamera.gameObject);
            deathCamera = null;

            Time.timeScale = 1f;
            Time.fixedDeltaTime = deathCamBaseFixedDeltaTime > 0f ? deathCamBaseFixedDeltaTime : 0.02f;
        }
    }

    // Server only: waits out the kill cam, then shows the card UI to everyone.
    private IEnumerator ShowPostMatchAfterDeathCam(ulong winningClientId, int[] roundCardPoolIndexes, int winningScore)
    {
        yield return new WaitForSecondsRealtime(DeathCamDuration);

        if (!IsSpawned)
        {
            yield break;
        }

        UpdatePostMatchUiClientRpc(winningClientId, roundCardPoolIndexes, winningScore);

        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);

        if (winnerController != null)
        {
            winnerController.BroadcastCardsUiCardSelection(0);
        }

        SwitchToWinnerCameraOwnerClientRpc(winningClientId, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        });
    }
}
