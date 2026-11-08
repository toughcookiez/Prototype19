// CHANGE LOG
// 
// CHANGES || version VERSION
//
// "Enable/Disable Headbob, Changed look rotations - should result in reduced camera jitters" || version 1.0.1

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

#if UNITY_EDITOR
    using UnityEditor;
    using System.Net;
#endif

public partial class FirstPersonController : NetworkBehaviour
{
    [System.Serializable]
    public struct WeaponSettings
    {
        public float bulletDamage;
        public float crosshairPointDistance;
        public float bulletSpeed;
        public float bulletGravity;
        public int bulletBounces;
        public float fireRate;
        public float holdToFireRateThreshold;
        public int bulletsPerShot;
        public int magazineSize;
        public int startingBullets;
        public float reloadTime;
    }

    [System.Serializable]
    public struct PlayerSettings
    {
        public float maxHealth;
        public float walkSpeed;
        public float sprintSpeed;
        public float sprintDuration;
        public float jumpPower;
        public float blockCooldown;
    }

    private const ulong LocalWeaponSettingsKey = ulong.MaxValue;
    private const string CardsUiInputBlockerName = "CardsUIInputBlocker";
    private const string CardsUiCardClassName = "CardButton";
    private const string CardsUiInfoElementName = "Info";
    private const string CardsUiTitleClassName = "card-title";
    private const string CardsUiInfoClassName = "card-info";
    private const string CardsUiStatRowClassName = "card-stat-row";
    private const string CardsUiStatTextClassName = "card-stat-text";
    private const string CardsUiStatKeywordGoodClassName = "card-stat-keyword-good";
    private const string CardsUiStatKeywordBadClassName = "card-stat-keyword-bad";
    private const string CardsUiAbilityDescriptionClassName = "card-ability-description";
    private const string CardsUiPlayerColorLabelName = "PlayerColorLabel";
    private const string CardsUiPlayerRedText = "RED";
    private const string CardsUiPlayerBlueText = "BLUE";
    private const string PlayerUiPointsName = "Points";
    private const string PlayerUiPlayer1Name = "Player1";
    private const string PlayerUiPlayer2Name = "Player2";
    private const string PlayerUiPointBackgroundName = "PointBackground";
    private const string PlayerUiPointName = "Point";
    private const int CardsUiCardsPerRound = 5;
    private const float CardsUiFlipDuration = .35f;
    private const float CardsUiSelectedScale = 1.1f;
    private const float CardsUiPickGrowDuration = .15f;
    private const float CardsUiPickShrinkDuration = .25f;
    private const float CardsUiPickGrowScale = 1.3f;
    private const float CardsUiDismissOthersDuration = .35f;
    private const float CardsUiPostPickDelay = .1f;
    private static readonly Color CardsUiPlayerRedColor = new Color(1f, 32f / 255f, 0f);
    private static readonly Color CardsUiPlayerBlueColor = new Color(0f, 150f / 255f, 1f);
    private static readonly Dictionary<ulong, WeaponSettings> savedWeaponSettingsByPlayer = new Dictionary<ulong, WeaponSettings>();
    private static readonly Dictionary<ulong, PlayerSettings> savedPlayerSettingsByPlayer = new Dictionary<ulong, PlayerSettings>();
    private static readonly Dictionary<ulong, int> roundWinsByPlayer = new Dictionary<ulong, int>();
    private static int scoreboardPlayer1Score;
    private static int scoreboardPlayer2Score;

    /// <summary>1-based number of the round about to be played, derived from the synced scoreboard.</summary>
    public static int CurrentRoundNumber => scoreboardPlayer1Score + scoreboardPlayer2Score + 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSavedSettingsForPlayMode()
    {
        savedWeaponSettingsByPlayer.Clear();
        savedPlayerSettingsByPlayer.Clear();
        roundWinsByPlayer.Clear();
        scoreboardPlayer1Score = 0;
        scoreboardPlayer2Score = 0;
    }

    private Rigidbody rb;

    #region Multiplayer Variables

    public bool enableMultiplayerAuthority = true;
    public Transform bodyRoot;
    public bool autoFindBodyRenderers = true;
    public Renderer[] playerBodyRenderers;
    public Material secondPlayerBodyMaterial;

    [Header("Goober Visuals")]
    [Tooltip("The inner first-person body used for casting shadows for the local player.")]
    public GameObject innerGoober;
    [Tooltip("The outer body visible to other players.")]
    public GameObject outerGoober;
    [Tooltip("Bend the outer (third-person) arms each frame so its hands match the inner (first-person) hands the owner sees. Other players then see the gun where the shooter does.")]
    public bool syncOuterArmsToInner = true;
    [Tooltip("Also match the left hand (the right hand holds the gun and is always matched when syncing is on).")]
    public bool syncOuterLeftArm = true;
    [Tooltip("How fast remote copies blend toward the latest hand pose received from the owner (higher = snappier, lower = smoother between network ticks).")]
    public float outerArmSyncSharpness = 30f;

    private ShadowCastingMode[] originalBodyShadowModes;
    private Material[][] originalBodyMaterials;

    #endregion

    #region Combat Variables

    public bool enablePlayerDamage = true;
    public float maxHealth = 100f;
    public float respawnDelay = 3f;
    public float respawnRadius = 2f;
    public UIDocument playerUiDocument;
    public UIDocument cardsUiDocument;
    public UIDocument healthUiDocument;
    public Card[] cardPool;
    [Tooltip("Sprite shown on a card once it has been flipped and its info revealed.")]
    public Sprite flippedCardSprite;
    public bool enableRagdollOnDeath = true;
    public Collider normalCollider;
    public Transform ragdollRoot;
    public bool autoFindRagdollParts = true;
    public Rigidbody[] ragdollRigidbodies;
    public Collider[] ragdollColliders;
    public bool disableAnimatorOnRagdoll = true;
    [Tooltip("Scene indices from Build Settings to use as the map pool. Leave empty to stay in current scene.")]
    public int[] mapPoolSceneIndices = new int[0];

    private Vector3 spawnPosition;
    private readonly NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    // Inner (first-person) hand poses written by the owner; other machines bend the outer arms onto them.
    private readonly NetworkVariable<InnerHandPose> innerHandPose = new NetworkVariable<InnerHandPose>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> isShieldActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private VisualElement healthBarProgressElement;
    private bool animatorWasEnabled = true;
    private Transform[] ragdollTransforms;
    private Vector3[] ragdollLocalPositions;
    private Quaternion[] ragdollLocalRotations;
    private Vector3[] ragdollLocalScales;
    private bool rootRbWasKinematic;
    private bool rootRbUseGravity;
    private bool rootRbDetectCollisions;
    private RigidbodyInterpolation rootRbInterpolation;
    private CollisionDetectionMode rootRbCollisionDetectionMode;
    private bool controlsDisabledAfterDeath;
    private bool cardsUiWasActive;
    private ulong cardsUiWinningClientId = ulong.MaxValue;
    private ulong cardsUiLosingClientId = ulong.MaxValue;
    private int selectedCardsUiCardIndex = -1;
    private readonly Card[] currentRoundCards = new Card[CardsUiCardsPerRound];
    private readonly HashSet<VisualElement> flippedCardsUiCards = new HashSet<VisualElement>();
    private readonly HashSet<int> flippedCardsUiCardIndexes = new HashSet<int>();
    private readonly Dictionary<VisualElement, Background> cardsUiCardBackBackgrounds = new Dictionary<VisualElement, Background>();
    private readonly HashSet<int> appliedCardsUiCardIndexes = new HashSet<int>();
    private readonly HashSet<VisualElement> animatingCardsUiCards = new HashSet<VisualElement>();
    private bool isCardsUiPickAnimationPlaying;

    #endregion

    #region Camera Movement Variables

    public Camera playerCamera;

    public float fov = 60f;
    public bool invertCamera = false;
    public bool cameraCanMove = true;
    public float mouseSensitivity = 2f;
    public float maxLookAngle = 50f;

    // Crosshair
    public bool lockCursor = true;
    public bool crosshair = true;
    public Sprite crosshairImage;
    public Color crosshairColor = Color.white;

    // Internal Variables
    private float yaw = 0.0f;
    private float pitch = 0.0f;
    private Image crosshairObject;

    #region Camera Zoom Variables

    public bool enableZoom = true;
    public bool holdToZoom = false;
    public KeyCode zoomKey = KeyCode.Mouse1;
    public float zoomFOV = 30f;
    public float zoomStepTime = 5f;

    // Internal Variables
    private bool isZoomed = false;

    #endregion
    #endregion

    #region Movement Variables

    public bool playerCanMove = true;
    public float walkSpeed = 5f;
    public float maxVelocityChange = 10f;

    // Internal Variables
    private bool isWalking = false;

    #region Sprint

    public bool enableSprint = true;
    public bool unlimitedSprint = false;
    public KeyCode sprintKey = KeyCode.LeftShift;
    public float sprintSpeed = 7f;
    public float sprintDuration = 5f;
    public float sprintCooldown = .5f;
    public float sprintFOV = 80f;
    public float sprintFOVStepTime = 10f;

    // Sprint Bar
    public bool useSprintBar = true;
    public bool hideBarWhenFull = true;
    public Image sprintBarBG;
    public Image sprintBar;
    public float sprintBarWidthPercent = .3f;
    public float sprintBarHeightPercent = .015f;

    // Internal Variables
    private CanvasGroup sprintBarCG;
    private bool isSprinting = false;
    private float sprintRemaining;
    private float sprintBarWidth;
    private float sprintBarHeight;
    private bool isSprintCooldown = false;
    private float sprintCooldownReset;

    #endregion

    #region Jump

    public bool enableJump = true;
    public KeyCode jumpKey = KeyCode.Space;
    public float jumpPower = 5f;

    // Internal Variables
    private bool isGrounded = false;

    #endregion

    #region Crouch

    public bool enableCrouch = true;
    public bool holdToCrouch = true;
    public KeyCode crouchKey = KeyCode.LeftControl;
    public float crouchHeight = .75f;
    public float speedReduction = .5f;

    // Internal Variables
    private bool isCrouched = false;
    private Vector3 originalScale;

    #endregion
    #endregion

    #region Block Variables

    public bool enableBlock = true;
    public KeyCode blockKey = KeyCode.Mouse1;
    [Tooltip("Seconds the block window lasts (invulnerable, bullets are destroyed).")]
    public float blockDuration = 0.35f;
    [Tooltip("Seconds to recharge after the block window ends.")]
    public float blockCooldown = 3f;

    // Internal Variables
    private bool isBlocking = false;
    private bool isBlockCooldown = false;
    private Coroutine blockCoroutine;

    #endregion

    #region Shooting Variables

    public bool enableShooting = true;
    public KeyCode shootKey = KeyCode.Mouse0;
    public KeyCode reloadKey = KeyCode.R;
    public Transform shootPoint;
    public GameObject bulletPrefab;
    public float bulletDamage = 25f;
    public float crosshairPointDistance = 50f;
    public float bulletSpeed = 30f;
    public float bulletGravity = 9.81f;
    public int bulletBounces;
    public float fireRate = 5f;
    public bool hasFlintknock;
    public float flintknockForce = 15f;
    public bool hasPayload;
    public bool hasBeacon;
    private Outline playerOutline;
    private Coroutine beaconCoroutine;
    public GameObject explosionPrefab;
    public float explosionRadius = 5f;
    public float explosionForce = 10f;
    public float holdToFireRateThreshold = 5f;
    public int bulletsPerShot = 1;
    public float bulletBurstInterval = .03f;
    public int magazineSize = 12;
    public int startingBullets = 12;
    public int ammoDotsPerRow = 6;
    public float ammoDotSize = 22f;
    public float reloadTime = 1f;
    public Image progressRing;

    // Internal Variables
    private int bulletsInMagazine;
    private bool isReloading;
    private CanvasGroup progressRingCG;
    private float nextShootTime;
    private int localShotCounter;
    [Tooltip("Muzzle on the outer (third-person) body's gun. Auto-found by the Shoot Point's name if left empty.")]
    public Transform outerShootPoint;
    private WeaponSettings lastSavedWeaponSettings;
    private bool hasSavedWeaponSettings;
    private PlayerSettings lastSavedPlayerSettings;
    private bool hasSavedPlayerSettings;

    #endregion

    #region Animation Variables

    public bool enableAnimations = true;
    public bool autoFindAnimator = true;
    public Animator characterAnimator;
    public float animationDampTime = .1f;
    public float lookAnimationDampTime = 0f;
    [Tooltip("Horizontal speed at which animations play at normal (1x) speed.")]
    public float animationReferenceSpeed = 5f;
    public Vector2 animationSpeedRange = new Vector2(.5f, 2.5f);

    public string moveXParameter = "MoveX";
    public string moveYParameter = "MoveY";
    public string speedParameter = "Speed";
    public string isMovingParameter = "IsMoving";
    public string isSprintingParameter = "IsSprinting";
    public string isGroundedParameter = "IsGrounded";
    public string isCrouchedParameter = "IsCrouched";
    public string isReloadingParameter = "IsReloading";
    public string lookValueParameter = "LookValue";
    public string jumpTriggerParameter = "Jump";
    public string shootTriggerParameter = "Shoot";

    // Internal Variables
    private readonly HashSet<string> animatorParameters = new HashSet<string>();
    // The outer body is what other players see. It is driven with the same parameters as the
    // inner body (plus LookValue so its gun aims with the camera) and synced by its OwnerNetworkAnimator.
    private Animator outerAnimator;
    private Unity.Netcode.Components.NetworkAnimator outerNetworkAnimator;
    private readonly HashSet<string> outerAnimatorParameters = new HashSet<string>();
    private static readonly List<FirstPersonController> spawnedPlayers = new List<FirstPersonController>();
    private float moveXInput;
    private float moveYInput;

    #endregion

    #region Head Bob

    public bool enableHeadBob = true;
    public Transform joint;
    public float bobSpeed = 10f;
    public Vector3 bobAmount = new Vector3(.15f, .05f, 0f);

    // Internal Variables
    private Vector3 jointOriginalPos;
    private float timer = 0;

    #endregion

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rootRbWasKinematic = rb.isKinematic;
            rootRbUseGravity = rb.useGravity;
            rootRbDetectCollisions = rb.detectCollisions;
            rootRbInterpolation = rb.interpolation;
            rootRbCollisionDetectionMode = rb.collisionDetectionMode;
        }

        crosshairObject = GetComponentInChildren<Image>(true);

        // Set internal variables
        if (playerCamera != null)
        {
            playerCamera.fieldOfView = fov;
        }

        originalScale = transform.localScale;

        if (joint != null)
        {
            jointOriginalPos = joint.localPosition;
        }

        CacheBodyRenderers();
        CacheOriginalBodyMaterials();
        CacheAnimatorParameters();
        CacheRagdollParts();
        CacheRagdollPose();
        ResolveMatchUiDocuments();
        animatorWasEnabled = characterAnimator == null || characterAnimator.enabled;
        ApplyRagdollState(false);

        NormalizeWeaponSettings();
        bulletsInMagazine = Mathf.Clamp(startingBullets, 0, magazineSize);
        SaveCurrentWeaponSettings();

        if (!unlimitedSprint)
        {
            sprintRemaining = sprintDuration;
            sprintCooldownReset = sprintCooldown;
        }

        // The old shield mesh is no longer used (block is a ring effect now); keep it hidden if still in the prefab.
        Transform legacyShield = transform.Find("ShieldObject");
        if (legacyShield != null)
        {
            legacyShield.gameObject.SetActive(false);
        }

        playerOutline = GetComponent<Outline>();
        if (playerOutline == null)
        {
            playerOutline = gameObject.AddComponent<Outline>();
        }
        playerOutline.enabled = false;
    }

    void Start()
    {
        ConfigureLocalView();
        ResolveHealthUi();
        UpdateHealthBar(currentHealth.Value);

        if (!HasInputAuthority())
        {
            return;
        }

        if(lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        if(crosshair && crosshairObject != null)
        {
            crosshairObject.sprite = crosshairImage;
            crosshairObject.color = crosshairColor;
        }
        else if (crosshairObject != null)
        {
            crosshairObject.gameObject.SetActive(false);
        }

        #region Sprint Bar

        sprintBarCG = sprintBar != null && sprintBar.transform.parent != null
            ? sprintBar.transform.parent.GetComponent<CanvasGroup>()
            : null;

        if (progressRing != null)
        {
            progressRingCG = progressRing.transform.parent != null ? progressRing.transform.parent.GetComponent<CanvasGroup>() : null;
            progressRing.fillAmount = 0f;
            // Replaced by the UI Toolkit reload ring (UpdateReloadRing in FirstPersonController.AmmoUI.cs).
            // The image is only disabled (not deactivated) because it still marks where the new ring is placed.
            progressRing.enabled = false;

            if (progressRingCG != null)
            {
                progressRingCG.alpha = 0f;
            }
        }

        if(useSprintBar && sprintBarBG != null && sprintBar != null)
        {
            sprintBarBG.gameObject.SetActive(true);
            sprintBar.gameObject.SetActive(true);

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            sprintBarWidth = screenWidth * sprintBarWidthPercent;
            sprintBarHeight = screenHeight * sprintBarHeightPercent;

            sprintBarBG.rectTransform.sizeDelta = new Vector3(sprintBarWidth, sprintBarHeight, 0f);
            sprintBar.rectTransform.sizeDelta = new Vector3(sprintBarWidth - 2, sprintBarHeight - 2, 0f);

            if(hideBarWhenFull)
            {
                sprintBarCG.alpha = 0;
            }
        }
        else
        {
            if (sprintBarBG != null)
            {
                sprintBarBG.gameObject.SetActive(false);
            }

            if (sprintBar != null)
            {
                sprintBar.gameObject.SetActive(false);
            }
        }

        #endregion
    }

    public override void OnNetworkSpawn()
    {
        if (!spawnedPlayers.Contains(this))
        {
            spawnedPlayers.Add(this);
        }

        currentHealth.OnValueChanged += OnCurrentHealthChanged;
        isShieldActive.OnValueChanged += OnShieldActiveChanged;

        ApplySavedWeaponSettings(true);
        ApplySavedPlayerSettings();

        if (IsServer)
        {
            spawnPosition = transform.position;
            currentHealth.Value = Mathf.Max(1f, maxHealth);
        }

        if (SpawnPoint.TryGetSpawnPoint((int)OwnerClientId, out Vector3 spawnPos, out Quaternion spawnRot))
        {
            spawnPosition = spawnPos;
            transform.position = spawnPos;
            transform.rotation = spawnRot;
        }

        // Reset player state on spawn (handles scene changes after death)
        controlsDisabledAfterDeath = false;
        playerCanMove = true;
        isWalking = false;
        isSprinting = false;
        isReloading = false;
        isSprintCooldown = false;
        moveXInput = 0f;
        moveYInput = 0f;
        sprintRemaining = sprintDuration;
        bulletsInMagazine = magazineSize;
        ClearCardsUiRoundState();

        ResolveMatchUiDocuments();
        ResolveHealthUi();
        UpdateHealthBar(currentHealth.Value);
        ConfigureLocalView();
        ApplySecondPlayerBodyMaterial();
        ApplyRagdollState(!IsAlive());

        // Ensure local camera is active and UI is visible after scene change
        if (HasInputAuthority())
        {
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(true);
                SetExclusiveAudioListeners(playerCamera);
            }
            SetPlayerUiActive(true);
            SetCardsUiActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        spawnedPlayers.Remove(this);
        SaveCurrentWeaponSettings();
        SaveCurrentPlayerSettings();
        currentHealth.OnValueChanged -= OnCurrentHealthChanged;
        isShieldActive.OnValueChanged -= OnShieldActiveChanged;
        healthBarProgressElement = null;
    }

    float camRotation;

    private void Update()
    {
        ProcessCardsUiKeyboardSelection();

        if (!HasInputAuthority())
        {
            UpdateRemoteAnimatorPlaybackSpeed();
            return;
        }

        UpdateAmmoBar();
        UpdateReloadRing();
        UpdateBlockUi();

        moveXInput = Input.GetAxis("Horizontal");
        moveYInput = Input.GetAxis("Vertical");

        #region Camera

        // Control camera movement
        if(cameraCanMove)
        {
            yaw = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * mouseSensitivity;
            transform.localEulerAngles = new Vector3(0, yaw, 0);

            float verticalLookInput = 100 * -Input.GetAxis("Mouse Y") / maxLookAngle;
            float targetPitch = pitch + (invertCamera ? -verticalLookInput : verticalLookInput);
            float pitchClamp = maxLookAngle;

            if (pitchClamp > 0f)
            {
                targetPitch = Mathf.Clamp(targetPitch, -pitchClamp, pitchClamp);
            }

            float pitchSmoothTime = lookAnimationDampTime;
            float smoothedPitch = pitchSmoothTime > 0f
                ? Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchSmoothTime * Time.deltaTime))
                : targetPitch;
            pitch = smoothedPitch;

            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        #region Block

        if (enableBlock && Input.GetKeyDown(blockKey) && Time.time >= nextBlockTime && !isSprinting)
        {
            if (!isBlocking)
            {
                TryBlock();
            }
        }

        #endregion
        #endregion

        #region Shooting

        if (enableShooting && !IsInMainMenuScene())
        {
            bool canHoldToShoot = fireRate >= holdToFireRateThreshold;
            bool isTryingToShoot = canHoldToShoot ? Input.GetKey(shootKey) : Input.GetKeyDown(shootKey);

            if (isTryingToShoot)
            {
                TryShoot();
            }

            if (Input.GetKeyDown(reloadKey))
            {
                TryStartReload();
            }
        }

        #endregion

        #region Sprint

        if(enableSprint)
        {
            if(isSprinting)
            {
                isZoomed = false;
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, sprintFOV, sprintFOVStepTime * Time.deltaTime);

                if(!unlimitedSprint)
                {
                    sprintRemaining -= Time.deltaTime;
                    if (sprintRemaining <= 0)
                    {
                        isSprinting = false;
                        isSprintCooldown = true;
                    }
                }
            }
            else
            {
                sprintRemaining = Mathf.Clamp(sprintRemaining += Time.deltaTime, 0, sprintDuration);
            }

            if(isSprintCooldown)
            {
                sprintCooldown -= Time.deltaTime;
                if (sprintCooldown <= 0)
                {
                    isSprintCooldown = false;
                }
            }
            else
            {
                sprintCooldown = sprintCooldownReset;
            }

            if(useSprintBar && !unlimitedSprint && sprintBar != null)
            {
                float sprintRemainingPercent = sprintRemaining / sprintDuration;
                sprintBar.transform.localScale = new Vector3(sprintRemainingPercent, 1f, 1f);
            }
        }

        #endregion

        #region Jump

        if(enableJump && Input.GetKeyDown(jumpKey) && isGrounded)
        {
            Jump();
        }

        #endregion

        #region Crouch

        if (enableCrouch)
        {
            if(Input.GetKeyDown(crouchKey) && !holdToCrouch)
            {
                Crouch();
            }
            
            if(Input.GetKeyDown(crouchKey) && holdToCrouch)
            {
                isCrouched = false;
                Crouch();
            }
            else if(Input.GetKeyUp(crouchKey) && holdToCrouch)
            {
                isCrouched = true;
                Crouch();
            }
        }

        #endregion

        if(enableHeadBob)
        {
            HeadBob();
        }

        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (!HasInputAuthority())
        {
            return;
        }

        CheckGround();

        #region Movement

        if (playerCanMove)
        {
            Vector3 targetVelocity = new Vector3(moveXInput, 0, moveYInput);

            if ((targetVelocity.x != 0 || targetVelocity.z != 0) && isGrounded)
            {
                isWalking = true;
            }
            else
            {
                isWalking = false;
            }

            if (enableSprint && Input.GetKey(sprintKey) && sprintRemaining > 0f && !isSprintCooldown)
            {
                targetVelocity = transform.TransformDirection(targetVelocity) * sprintSpeed;

                Vector3 velocity = rb.linearVelocity;
                Vector3 velocityChange = (targetVelocity - velocity);
                velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                velocityChange.y = 0;

                if (velocityChange.x != 0 || velocityChange.z != 0)
                {
                    isSprinting = true;

                    if (isCrouched)
                    {
                        Crouch();
                    }

                    if (hideBarWhenFull && !unlimitedSprint && sprintBarCG != null)
                    {
                        sprintBarCG.alpha += 5 * Time.deltaTime;
                    }
                }

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
            else
            {
                isSprinting = false;

                if (hideBarWhenFull && sprintRemaining == sprintDuration && sprintBarCG != null)
                {
                    sprintBarCG.alpha -= 3 * Time.deltaTime;
                }

                targetVelocity = transform.TransformDirection(targetVelocity) * walkSpeed;

                Vector3 velocity = rb.linearVelocity;
                Vector3 velocityChange = (targetVelocity - velocity);
                velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
                velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
                velocityChange.y = 0;

                rb.AddForce(velocityChange, ForceMode.VelocityChange);
            }
        }

        #endregion
    }

    private void LateUpdate()
    {
        // Runs after both Animators have evaluated this frame.
        SyncOuterArmsToInner();
        SaveCurrentWeaponSettingsIfChanged();
    }
}
