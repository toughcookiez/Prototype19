using Unity.Netcode;
using UnityEngine;

// Keeps the outer (third-person) arms on the inner (first-person) hands.
//
// The two bodies share a skeleton and Animator controller but are posed differently: the inner body is
// parented to the camera and pitches rigidly with it, while the outer body pitches through the LookLayer
// blend tree. Their hands drift apart whenever the player looks up or down. The inner body is what the
// shooter sees and where bullets spawn (shootPoint), so it is the reference and is never modified.
//
// The inner body only runs on the owner's machine (PlayerCamera is inactive for remote players), so the
// owner publishes its inner hand poses in innerHandPose. Wherever the outer body is shown, its arms are
// bent with a two-bone IK onto those hands (elbow kept where the outer animation put it) and the outer
// hand is set to the inner hand's exact pose. Because the gun is a child of the hand, the outer muzzle
// then lies on the shooter's shootPoint.
public partial class FirstPersonController
{
    // Hand poses as unscaled offsets from the player root (crouching scales the root), so they map to the
    // same world pose on every machine that has the synced root transform.
    public struct InnerHandPose : INetworkSerializable, System.IEquatable<InnerHandPose>
    {
        public bool isValid;
        public Vector3 rightPosition;
        public Quaternion rightRotation;
        public Vector3 leftPosition;
        public Quaternion leftRotation;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref isValid);
            serializer.SerializeValue(ref rightPosition);
            serializer.SerializeValue(ref rightRotation);
            serializer.SerializeValue(ref leftPosition);
            serializer.SerializeValue(ref leftRotation);
        }

        public bool Equals(InnerHandPose other)
        {
            return isValid == other.isValid &&
                   rightPosition == other.rightPosition && rightRotation == other.rightRotation &&
                   leftPosition == other.leftPosition && leftRotation == other.leftRotation;
        }

        public override bool Equals(object obj)
        {
            return obj is InnerHandPose other && Equals(other);
        }

        public override int GetHashCode()
        {
            return rightPosition.GetHashCode() ^ leftPosition.GetHashCode();
        }
    }

    private sealed class ArmSyncChain
    {
        public Transform outerUpper;
        public Transform outerLower;
        public Transform outerHand;
        public Transform innerHand;
        // The clips only animate rotations, so positions written here would stick between frames.
        public Vector3 outerLowerRestPosition;
        public Vector3 outerHandRestPosition;
    }

    // Beyond this IK miss the hand is left where the IK put it instead of being snapped onto the target,
    // which would visibly stretch the wrist.
    private const float ArmSyncMaxPinDistance = .05f;
    private const int ArmSyncIkIterations = 4;
    private const float ArmSyncIkTolerance = .001f;
    // Below these changes the owner does not rewrite innerHandPose, to save bandwidth while idle.
    private const float ArmSyncSendPositionThreshold = .001f;
    private const float ArmSyncSendAngleThreshold = .25f;

    private ArmSyncChain rightArmSync;
    private ArmSyncChain leftArmSync;
    private bool armSyncResolved;
    private InnerHandPose smoothedInnerHandPose;

    private void SyncOuterArmsToInner()
    {
        if (!syncOuterArmsToInner || innerGoober == null || outerGoober == null)
        {
            return;
        }

        if (!armSyncResolved)
        {
            ResolveArmSyncChains();
        }

        if (rightArmSync == null)
        {
            return;
        }

        bool hasLiveInner = innerGoober.activeInHierarchy && characterAnimator != null && characterAnimator.isActiveAndEnabled;
        InnerHandPose livePose = hasLiveInner ? CaptureInnerHandPose() : default;

        if (hasLiveInner && IsSpawned && IsOwner && IsAlive())
        {
            PublishInnerHandPose(livePose);
        }

        // Skip while ragdolled (bones are driven by physics) or when the outer body is not animating.
        if (!outerGoober.activeInHierarchy || outerAnimator == null || !outerAnimator.isActiveAndEnabled || !IsAlive())
        {
            smoothedInnerHandPose.isValid = false;
            return;
        }

        InnerHandPose targetPose;

        if (hasLiveInner)
        {
            // The inner body runs on this machine: match it exactly, no network delay.
            targetPose = livePose;
        }
        else if (IsSpawned)
        {
            targetPose = SmoothRemoteInnerHandPose(innerHandPose.Value);
        }
        else
        {
            return;
        }

        if (!targetPose.isValid)
        {
            return;
        }

        SolveArmSync(rightArmSync, RootToWorldPosition(targetPose.rightPosition), RootToWorldRotation(targetPose.rightRotation));

        if (syncOuterLeftArm && leftArmSync != null)
        {
            SolveArmSync(leftArmSync, RootToWorldPosition(targetPose.leftPosition), RootToWorldRotation(targetPose.leftRotation));
        }
    }

    private InnerHandPose CaptureInnerHandPose()
    {
        InnerHandPose pose = new InnerHandPose
        {
            isValid = true,
            rightPosition = WorldToRootPosition(rightArmSync.innerHand.position),
            rightRotation = WorldToRootRotation(rightArmSync.innerHand.rotation),
            leftRotation = Quaternion.identity,
        };

        if (leftArmSync != null)
        {
            pose.leftPosition = WorldToRootPosition(leftArmSync.innerHand.position);
            pose.leftRotation = WorldToRootRotation(leftArmSync.innerHand.rotation);
        }

        return pose;
    }

    private void PublishInnerHandPose(InnerHandPose pose)
    {
        InnerHandPose published = innerHandPose.Value;

        bool hasChanged = !published.isValid ||
                          (published.rightPosition - pose.rightPosition).sqrMagnitude > ArmSyncSendPositionThreshold * ArmSyncSendPositionThreshold ||
                          (published.leftPosition - pose.leftPosition).sqrMagnitude > ArmSyncSendPositionThreshold * ArmSyncSendPositionThreshold ||
                          Quaternion.Angle(published.rightRotation, pose.rightRotation) > ArmSyncSendAngleThreshold ||
                          Quaternion.Angle(published.leftRotation, pose.leftRotation) > ArmSyncSendAngleThreshold;

        if (hasChanged)
        {
            innerHandPose.Value = pose;
        }
    }

    // The NetworkVariable only changes on network ticks; blend toward it so the arm doesn't step.
    private InnerHandPose SmoothRemoteInnerHandPose(InnerHandPose received)
    {
        if (!received.isValid)
        {
            smoothedInnerHandPose.isValid = false;
            return received;
        }

        if (!smoothedInnerHandPose.isValid || outerArmSyncSharpness <= 0f)
        {
            smoothedInnerHandPose = received;
            return smoothedInnerHandPose;
        }

        float t = 1f - Mathf.Exp(-outerArmSyncSharpness * Time.deltaTime);
        smoothedInnerHandPose.rightPosition = Vector3.Lerp(smoothedInnerHandPose.rightPosition, received.rightPosition, t);
        smoothedInnerHandPose.rightRotation = Quaternion.Slerp(smoothedInnerHandPose.rightRotation, received.rightRotation, t);
        smoothedInnerHandPose.leftPosition = Vector3.Lerp(smoothedInnerHandPose.leftPosition, received.leftPosition, t);
        smoothedInnerHandPose.leftRotation = Quaternion.Slerp(smoothedInnerHandPose.leftRotation, received.leftRotation, t);
        return smoothedInnerHandPose;
    }

    private Vector3 WorldToRootPosition(Vector3 worldPosition)
    {
        return Quaternion.Inverse(transform.rotation) * (worldPosition - transform.position);
    }

    private Quaternion WorldToRootRotation(Quaternion worldRotation)
    {
        return Quaternion.Inverse(transform.rotation) * worldRotation;
    }

    private Vector3 RootToWorldPosition(Vector3 rootPosition)
    {
        return transform.position + transform.rotation * rootPosition;
    }

    private Quaternion RootToWorldRotation(Quaternion rootRotation)
    {
        return transform.rotation * rootRotation;
    }

    private void ResolveArmSyncChains()
    {
        armSyncResolved = true;

        if (outerAnimator == null)
        {
            CacheOuterAnimator();
        }

        rightArmSync = BuildArmSyncChain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "mixamorig:RightArm", "mixamorig:RightForeArm", "mixamorig:RightHand");
        leftArmSync = BuildArmSyncChain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand");

        if (rightArmSync == null)
        {
            Debug.LogWarning($"[ArmSync] {name}: could not find matching right-arm bones on InnerGoober and OuterGoober; the outer gun will not follow the inner gun.", this);
        }
    }

    private ArmSyncChain BuildArmSyncChain(HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone, string upperName, string lowerName, string handName)
    {
        ArmSyncChain chain = new ArmSyncChain
        {
            outerUpper = FindArmBone(outerAnimator, outerGoober.transform, upperBone, upperName),
            outerLower = FindArmBone(outerAnimator, outerGoober.transform, lowerBone, lowerName),
            outerHand = FindArmBone(outerAnimator, outerGoober.transform, handBone, handName),
            innerHand = FindArmBone(characterAnimator, innerGoober.transform, handBone, handName),
        };

        bool isComplete = chain.outerUpper != null && chain.outerLower != null && chain.outerHand != null && chain.innerHand != null;

        if (!isComplete)
        {
            return null;
        }

        chain.outerLowerRestPosition = chain.outerLower.localPosition;
        chain.outerHandRestPosition = chain.outerHand.localPosition;
        return chain;
    }

    private static Transform FindArmBone(Animator animator, Transform root, HumanBodyBones bone, string boneName)
    {
        if (animator != null && animator.isHuman && animator.transform.IsChildOf(root))
        {
            Transform humanBone = animator.GetBoneTransform(bone);

            if (humanBone != null)
            {
                return humanBone;
            }
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == boneName)
            {
                return child;
            }
        }

        return null;
    }

    private static void SolveArmSync(ArmSyncChain chain, Vector3 targetPosition, Quaternion targetRotation)
    {
        // Undo last frame's pin before solving so bone lengths never drift.
        chain.outerLower.localPosition = chain.outerLowerRestPosition;
        chain.outerHand.localPosition = chain.outerHandRestPosition;

        // Pole: where this frame's outer animation put the elbow, so the arm keeps its natural bend.
        // Crouching scales the root non-uniformly, which skews the arm so bone lengths change as it
        // rotates; a few passes converge where a single analytic pass would miss.
        Vector3 pole = chain.outerLower.position;

        for (int i = 0; i < ArmSyncIkIterations; i++)
        {
            SolveTwoBoneIk(chain.outerUpper, chain.outerLower, chain.outerHand, targetPosition, pole);

            if ((chain.outerHand.position - targetPosition).sqrMagnitude <= ArmSyncIkTolerance * ArmSyncIkTolerance)
            {
                break;
            }
        }

        // Pin the hand exactly. Within reach this only removes float error, so the gun (a child of the
        // hand) lands exactly on the inner muzzle. Far out of reach, only match the rotation.
        if ((chain.outerHand.position - targetPosition).sqrMagnitude <= ArmSyncMaxPinDistance * ArmSyncMaxPinDistance)
        {
            chain.outerHand.SetPositionAndRotation(targetPosition, targetRotation);
        }
        else
        {
            chain.outerHand.rotation = targetRotation;
        }
    }

    // Analytic two-bone IK: bends the elbow so the chain length matches the shoulder-to-target distance,
    // swings the chain onto the target, then twists it about that line so the elbow faces the pole.
    private static void SolveTwoBoneIk(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
    {
        const float epsilon = 0.0001f;

        Vector3 a = upper.position;
        Vector3 b = lower.position;
        Vector3 c = end.position;

        float upperLength = Vector3.Distance(a, b);
        float lowerLength = Vector3.Distance(b, c);

        if (upperLength < epsilon || lowerLength < epsilon)
        {
            return;
        }

        float targetDistance = Mathf.Clamp(Vector3.Distance(a, target), epsilon, upperLength + lowerLength - epsilon);

        // 1. Elbow angle.
        Vector3 bendAxis = Vector3.Cross(c - a, b - a);

        if (bendAxis.sqrMagnitude < epsilon * epsilon)
        {
            bendAxis = Vector3.Cross(c - a, pole - a);
        }

        if (bendAxis.sqrMagnitude < epsilon * epsilon)
        {
            bendAxis = upper.right;
        }

        bendAxis.Normalize();

        float currentShoulderAngle = AngleBetween(c - a, b - a);
        float currentElbowAngle = AngleBetween(a - b, c - b);
        float desiredShoulderAngle = Mathf.Acos(Mathf.Clamp((lowerLength * lowerLength - upperLength * upperLength - targetDistance * targetDistance) / (-2f * upperLength * targetDistance), -1f, 1f));
        float desiredElbowAngle = Mathf.Acos(Mathf.Clamp((targetDistance * targetDistance - upperLength * upperLength - lowerLength * lowerLength) / (-2f * upperLength * lowerLength), -1f, 1f));

        lower.rotation = Quaternion.AngleAxis((desiredElbowAngle - currentElbowAngle) * Mathf.Rad2Deg, bendAxis) * lower.rotation;
        upper.rotation = Quaternion.AngleAxis((desiredShoulderAngle - currentShoulderAngle) * Mathf.Rad2Deg, bendAxis) * upper.rotation;

        // 2. Point the shoulder-to-hand line at the target.
        upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;

        // 3. Twist about the shoulder-to-target line so the elbow faces the pole.
        Vector3 reachAxis = (target - a).normalized;
        Vector3 elbowDirection = Vector3.ProjectOnPlane(lower.position - a, reachAxis);
        Vector3 poleDirection = Vector3.ProjectOnPlane(pole - a, reachAxis);

        if (elbowDirection.sqrMagnitude > epsilon * epsilon && poleDirection.sqrMagnitude > epsilon * epsilon)
        {
            float twist = Vector3.SignedAngle(elbowDirection, poleDirection, reachAxis);
            upper.rotation = Quaternion.AngleAxis(twist, reachAxis) * upper.rotation;
        }
    }

    private static float AngleBetween(Vector3 from, Vector3 to)
    {
        if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f)
        {
            return 0f;
        }

        return Mathf.Acos(Mathf.Clamp(Vector3.Dot(from.normalized, to.normalized), -1f, 1f));
    }
}
