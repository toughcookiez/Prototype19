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

public class FirstPersonController : NetworkBehaviour
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
    private static readonly Dictionary<ulong, WeaponSettings> savedWeaponSettingsByPlayer = new Dictionary<ulong, WeaponSettings>();
    private static readonly Dictionary<ulong, PlayerSettings> savedPlayerSettingsByPlayer = new Dictionary<ulong, PlayerSettings>();
    private static readonly Dictionary<ulong, int> roundWinsByPlayer = new Dictionary<ulong, int>();
    private static int scoreboardPlayer1Score;
    private static int scoreboardPlayer2Score;

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
    private float lookValue = 0.0f;
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
    public float holdToFireRateThreshold = 5f;
    public int bulletsPerShot = 1;
    public float bulletBurstInterval = .03f;
    public int magazineSize = 12;
    public int startingBullets = 12;
    public float reloadTime = 1f;
    public Image progressRing;

    // Internal Variables
    private int bulletsInMagazine;
    private bool isReloading;
    private CanvasGroup progressRingCG;
    private float nextShootTime;
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

    // Internal Variables
    private readonly HashSet<string> animatorParameters = new HashSet<string>();
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
        currentHealth.OnValueChanged += OnCurrentHealthChanged;

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
        SaveCurrentWeaponSettings();
        SaveCurrentPlayerSettings();
        currentHealth.OnValueChanged -= OnCurrentHealthChanged;
        healthBarProgressElement = null;
    }

    float camRotation;

    private void Update()
    {
        ProcessCardsUiKeyboardSelection();

        if (!HasInputAuthority())
        {
            return;
        }

        moveXInput = Input.GetAxis("Horizontal");
        moveYInput = Input.GetAxis("Vertical");

        #region Camera

        // Control camera movement
        if(cameraCanMove)
        {
            yaw = transform.localEulerAngles.y + Input.GetAxis("Mouse X") * mouseSensitivity;

            float lookInput = mouseSensitivity * Input.GetAxis("Mouse Y") / maxLookAngle;
            lookValue = Mathf.Clamp(lookValue + (invertCamera ? lookInput : -lookInput), -1f, 1f);

            transform.localEulerAngles = new Vector3(0, yaw, 0);
        }

        #region Camera Zoom

        if (enableZoom)
        {
            if(Input.GetKeyDown(zoomKey) && !holdToZoom && !isSprinting)
            {
                isZoomed = !isZoomed;
            }

            if(holdToZoom && !isSprinting)
            {
                if(Input.GetKeyDown(zoomKey))
                {
                    isZoomed = true;
                }
                else if(Input.GetKeyUp(zoomKey))
                {
                    isZoomed = false;
                }
            }

            if(isZoomed)
            {
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, zoomFOV, zoomStepTime * Time.deltaTime);
            }
            else if(!isZoomed && !isSprinting)
            {
                playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, fov, zoomStepTime * Time.deltaTime);
            }
        }

        #endregion
        #endregion

        #region Shooting

        if (enableShooting)
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
        SaveCurrentWeaponSettingsIfChanged();
    }

    private void TryShoot()
    {
        if (!HasInputAuthority())
        {
            return;
        }

        if (isReloading)
        {
            return;
        }

        if (Time.time < nextShootTime)
        {
            return;
        }

        if (bulletsInMagazine <= 0)
        {
            TryStartReload();
            return;
        }

        if (bulletPrefab == null || shootPoint == null)
        {
            return;
        }

        Vector3 crosshairPoint = GetCrosshairPoint();
        Vector3 shootDirection = GetShootDirection(crosshairPoint);
        float ignoreGravityDistance = Vector3.Distance(shootPoint.position, crosshairPoint);
        nextShootTime = Time.time + (1f / Mathf.Max(.1f, fireRate));

        int bulletsToFire = Mathf.Min(Mathf.Max(1, bulletsPerShot), bulletsInMagazine);

        if (ShouldUseNetworkSpawning())
        {
            ShootServerRpc(shootDirection, ignoreGravityDistance, bulletsToFire);
            bulletsInMagazine -= bulletsToFire;
            return;
        }

        StartCoroutine(FireBulletBurst(shootDirection, ignoreGravityDistance, IsSpawned ? OwnerClientId : ulong.MaxValue, bulletsToFire));
        bulletsInMagazine -= bulletsToFire;
    }

    private IEnumerator FireBulletBurst(Vector3 shootDirection, float ignoreGravityDistance, ulong shooterClientId, int bulletsToFire)
    {
        int clampedBulletsToFire = Mathf.Max(1, bulletsToFire);

        for (int i = 0; i < clampedBulletsToFire; i++)
        {
            if (bulletPrefab == null || shootPoint == null)
            {
                yield break;
            }

            SpawnBullet(shootPoint.position, shootDirection, ignoreGravityDistance, shooterClientId);

            if (i < clampedBulletsToFire - 1)
            {
                if (bulletBurstInterval > 0f)
                {
                    yield return new WaitForSeconds(bulletBurstInterval);
                }
                else
                {
                    yield return null;
                }
            }
        }
    }

    private void SpawnBullet(Vector3 spawnPosition, Vector3 shootDirection, float ignoreGravityDistance, ulong shooterClientId)
    {
        Quaternion shootRotation = Quaternion.LookRotation(shootDirection, Vector3.up);
        GameObject spawnedBullet = Instantiate(bulletPrefab, spawnPosition, shootRotation);

        if (spawnedBullet.TryGetComponent<Bullet>(out Bullet bullet))
        {
            bullet.damage = bulletDamage;
            bullet.SetBounces(bulletBounces);
            bullet.SetShooter(shooterClientId, this);
        }

        if (NetworkManager.Singleton != null && IsServer && spawnedBullet.TryGetComponent<NetworkObject>(out NetworkObject networkObject))
        {
            networkObject.Spawn();
        }

        if (spawnedBullet.TryGetComponent<Bullet>(out Bullet launchedBullet))
        {
            launchedBullet.Launch(shootDirection, bulletSpeed, bulletGravity, ignoreGravityDistance);
        }
        else if (spawnedBullet.TryGetComponent<Rigidbody>(out Rigidbody bulletRb))
        {
            bulletRb.linearVelocity = shootDirection * bulletSpeed;

            if (spawnedBullet.TryGetComponent<BulletGravity>(out BulletGravity bulletGravityComponent))
            {
                bulletGravityComponent.SetGravity(bulletGravity, ignoreGravityDistance);
            }
            else
            {
                spawnedBullet.AddComponent<BulletGravity>().SetGravity(bulletGravity, ignoreGravityDistance);
            }
        }
    }

    [ServerRpc]
    private void ShootServerRpc(Vector3 shootDirection, float ignoreGravityDistance, int bulletsToFire)
    {
        if (bulletPrefab == null || shootPoint == null)
        {
            return;
        }

        StartCoroutine(FireBulletBurst(shootDirection.normalized, Mathf.Max(0f, ignoreGravityDistance), OwnerClientId, bulletsToFire));
    }

    public WeaponSettings GetWeaponSettings()
    {
        return new WeaponSettings
        {
            bulletDamage = bulletDamage,
            crosshairPointDistance = crosshairPointDistance,
            bulletSpeed = bulletSpeed,
            bulletGravity = bulletGravity,
            bulletBounces = bulletBounces,
            fireRate = fireRate,
            holdToFireRateThreshold = holdToFireRateThreshold,
            bulletsPerShot = bulletsPerShot,
            magazineSize = magazineSize,
            startingBullets = startingBullets,
            reloadTime = reloadTime
        };
    }

    public void ApplyWeaponSettings(WeaponSettings weaponSettings, bool refillMagazine = false)
    {
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
        SyncWeaponSettings(weaponSettings, refillMagazine);
    }

    public PlayerSettings GetPlayerSettings()
    {
        return new PlayerSettings
        {
            maxHealth = maxHealth,
            walkSpeed = walkSpeed,
            sprintSpeed = sprintSpeed,
            sprintDuration = sprintDuration,
            jumpPower = jumpPower
        };
    }

    public void ApplyPlayerSettings(PlayerSettings playerSettings)
    {
        ApplyPlayerSettingsLocally(playerSettings);
        SaveCurrentPlayerSettings();
        SyncPlayerSettings(playerSettings);
    }

    [ServerRpc]
    private void ApplyWeaponSettingsServerRpc(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue, bool refillMagazine)
    {
        WeaponSettings weaponSettings = CreateWeaponSettings(bulletDamageValue, crosshairPointDistanceValue, bulletSpeedValue, bulletGravityValue, bulletBouncesValue, fireRateValue, holdToFireRateThresholdValue, bulletsPerShotValue, magazineSizeValue, startingBulletsValue, reloadTimeValue);
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
    }

    [ClientRpc]
    private void ApplyWeaponSettingsOwnerClientRpc(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue, bool refillMagazine, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        WeaponSettings weaponSettings = CreateWeaponSettings(bulletDamageValue, crosshairPointDistanceValue, bulletSpeedValue, bulletGravityValue, bulletBouncesValue, fireRateValue, holdToFireRateThresholdValue, bulletsPerShotValue, magazineSizeValue, startingBulletsValue, reloadTimeValue);
        ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
        SaveCurrentWeaponSettings();
    }

    private static WeaponSettings CreateWeaponSettings(float bulletDamageValue, float crosshairPointDistanceValue, float bulletSpeedValue, float bulletGravityValue, int bulletBouncesValue, float fireRateValue, float holdToFireRateThresholdValue, int bulletsPerShotValue, int magazineSizeValue, int startingBulletsValue, float reloadTimeValue)
    {
        return new WeaponSettings
        {
            bulletDamage = bulletDamageValue,
            crosshairPointDistance = crosshairPointDistanceValue,
            bulletSpeed = bulletSpeedValue,
            bulletGravity = bulletGravityValue,
            bulletBounces = bulletBouncesValue,
            fireRate = fireRateValue,
            holdToFireRateThreshold = holdToFireRateThresholdValue,
            bulletsPerShot = bulletsPerShotValue,
            magazineSize = magazineSizeValue,
            startingBullets = startingBulletsValue,
            reloadTime = reloadTimeValue
        };
    }

    private void ApplySavedWeaponSettings(bool refillMagazine)
    {
        if (savedWeaponSettingsByPlayer.TryGetValue(GetWeaponSettingsKey(), out WeaponSettings weaponSettings))
        {
            ApplyWeaponSettingsLocally(weaponSettings, refillMagazine);
            SaveCurrentWeaponSettings();
            return;
        }

        SaveCurrentWeaponSettings();
    }

    private void ApplySavedPlayerSettings()
    {
        if (savedPlayerSettingsByPlayer.TryGetValue(GetPlayerSettingsKey(), out PlayerSettings playerSettings))
        {
            ApplyPlayerSettingsLocally(playerSettings);
            SaveCurrentPlayerSettings();
            return;
        }

        NormalizePlayerSettings();
        SaveCurrentPlayerSettings();
    }

    private void ApplyPlayerSettingsLocally(PlayerSettings playerSettings)
    {
        maxHealth = playerSettings.maxHealth;
        walkSpeed = playerSettings.walkSpeed;
        sprintSpeed = playerSettings.sprintSpeed;
        sprintDuration = playerSettings.sprintDuration;
        jumpPower = playerSettings.jumpPower;
        NormalizePlayerSettings();
        UpdateHealthBar(currentHealth.Value);
    }

    private void ApplyWeaponSettingsLocally(WeaponSettings weaponSettings, bool refillMagazine)
    {
        bulletDamage = weaponSettings.bulletDamage;
        crosshairPointDistance = weaponSettings.crosshairPointDistance;
        bulletSpeed = weaponSettings.bulletSpeed;
        bulletGravity = weaponSettings.bulletGravity;
        bulletBounces = weaponSettings.bulletBounces;
        fireRate = weaponSettings.fireRate;
        holdToFireRateThreshold = weaponSettings.holdToFireRateThreshold;
        bulletsPerShot = weaponSettings.bulletsPerShot;
        magazineSize = weaponSettings.magazineSize;
        startingBullets = weaponSettings.startingBullets;
        reloadTime = weaponSettings.reloadTime;

        NormalizeWeaponSettings();
        bulletsInMagazine = refillMagazine ? Mathf.Clamp(startingBullets, 0, magazineSize) : Mathf.Clamp(bulletsInMagazine, 0, magazineSize);
    }

    private void NormalizeWeaponSettings()
    {
        bulletDamage = Mathf.Max(0.1f, bulletDamage);
        bulletSpeed = Mathf.Max(0.1f, bulletSpeed);
        bulletsPerShot = Mathf.Max(1, bulletsPerShot);
        bulletBounces = Mathf.Max(0, bulletBounces);
        magazineSize = Mathf.Max(1, magazineSize);
        crosshairPointDistance = Mathf.Max(0.1f, crosshairPointDistance);
        fireRate = Mathf.Max(.1f, fireRate);
        holdToFireRateThreshold = Mathf.Max(.1f, holdToFireRateThreshold);
        startingBullets = Mathf.Clamp(startingBullets, 1, magazineSize);
        reloadTime = Mathf.Max(.1f, reloadTime);
    }

    private void SaveCurrentWeaponSettingsIfChanged()
    {
        WeaponSettings weaponSettings = GetWeaponSettings();

        if (hasSavedWeaponSettings && WeaponSettingsMatch(lastSavedWeaponSettings, weaponSettings))
        {
            return;
        }

        ApplyWeaponSettingsLocally(weaponSettings, false);
        SaveWeaponSettings(weaponSettings);
        SyncWeaponSettings(weaponSettings, false);
    }

    private void SaveCurrentWeaponSettings()
    {
        SaveWeaponSettings(GetWeaponSettings());
    }

    private void SaveCurrentPlayerSettings()
    {
        SavePlayerSettings(GetPlayerSettings());
    }

    private void SaveWeaponSettings(WeaponSettings weaponSettings)
    {
        NormalizeWeaponSettings(ref weaponSettings);
        savedWeaponSettingsByPlayer[GetWeaponSettingsKey()] = weaponSettings;
        lastSavedWeaponSettings = weaponSettings;
        hasSavedWeaponSettings = true;
    }

    private void SavePlayerSettings(PlayerSettings playerSettings)
    {
        NormalizePlayerSettings(ref playerSettings);
        savedPlayerSettingsByPlayer[GetPlayerSettingsKey()] = playerSettings;
        lastSavedPlayerSettings = playerSettings;
        hasSavedPlayerSettings = true;
    }

    private ulong GetWeaponSettingsKey()
    {
        return IsSpawned ? OwnerClientId : LocalWeaponSettingsKey;
    }

    private ulong GetPlayerSettingsKey()
    {
        return IsSpawned ? OwnerClientId : LocalWeaponSettingsKey;
    }

    private void SyncWeaponSettings(WeaponSettings weaponSettings, bool refillMagazine)
    {
        if (!IsSpawned || NetworkManager.Singleton == null)
        {
            return;
        }

        if (IsServer)
        {
            ApplyWeaponSettingsOwnerClientRpc(weaponSettings.bulletDamage, weaponSettings.crosshairPointDistance, weaponSettings.bulletSpeed, weaponSettings.bulletGravity, weaponSettings.bulletBounces, weaponSettings.fireRate, weaponSettings.holdToFireRateThreshold, weaponSettings.bulletsPerShot, weaponSettings.magazineSize, weaponSettings.startingBullets, weaponSettings.reloadTime, refillMagazine, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            });
        }
        else if (IsOwner)
        {
            ApplyWeaponSettingsServerRpc(weaponSettings.bulletDamage, weaponSettings.crosshairPointDistance, weaponSettings.bulletSpeed, weaponSettings.bulletGravity, weaponSettings.bulletBounces, weaponSettings.fireRate, weaponSettings.holdToFireRateThreshold, weaponSettings.bulletsPerShot, weaponSettings.magazineSize, weaponSettings.startingBullets, weaponSettings.reloadTime, refillMagazine);
        }
    }

    private void SyncPlayerSettings(PlayerSettings playerSettings)
    {
        if (!IsSpawned || NetworkManager.Singleton == null || !IsServer)
        {
            return;
        }

        ApplyPlayerSettingsOwnerClientRpc(playerSettings.maxHealth, playerSettings.walkSpeed, playerSettings.sprintSpeed, playerSettings.sprintDuration, playerSettings.jumpPower, new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        });
    }

    [ClientRpc]
    private void ApplyPlayerSettingsOwnerClientRpc(float maxHealthValue, float walkSpeedValue, float sprintSpeedValue, float sprintDurationValue, float jumpPowerValue, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        PlayerSettings playerSettings = new PlayerSettings
        {
            maxHealth = maxHealthValue,
            walkSpeed = walkSpeedValue,
            sprintSpeed = sprintSpeedValue,
            sprintDuration = sprintDurationValue,
            jumpPower = jumpPowerValue
        };

        ApplyPlayerSettingsLocally(playerSettings);
        SaveCurrentPlayerSettings();
    }

    private static void NormalizeWeaponSettings(ref WeaponSettings weaponSettings)
    {
        weaponSettings.bulletDamage = Mathf.Max(0.1f, weaponSettings.bulletDamage);
        weaponSettings.bulletSpeed = Mathf.Max(0.1f, weaponSettings.bulletSpeed);
        weaponSettings.bulletsPerShot = Mathf.Max(1, weaponSettings.bulletsPerShot);
        weaponSettings.bulletBounces = Mathf.Max(0, weaponSettings.bulletBounces);
        weaponSettings.magazineSize = Mathf.Max(1, weaponSettings.magazineSize);
        weaponSettings.crosshairPointDistance = Mathf.Max(0.1f, weaponSettings.crosshairPointDistance);
        weaponSettings.fireRate = Mathf.Max(.1f, weaponSettings.fireRate);
        weaponSettings.holdToFireRateThreshold = Mathf.Max(.1f, weaponSettings.holdToFireRateThreshold);
        weaponSettings.startingBullets = Mathf.Clamp(weaponSettings.startingBullets, 1, weaponSettings.magazineSize);
        weaponSettings.reloadTime = Mathf.Max(.1f, weaponSettings.reloadTime);
    }

    private static void NormalizePlayerSettings(ref PlayerSettings playerSettings)
    {
        playerSettings.maxHealth = Mathf.Max(1f, playerSettings.maxHealth);
        playerSettings.walkSpeed = Mathf.Max(0.1f, playerSettings.walkSpeed);
        playerSettings.sprintSpeed = Mathf.Max(0.1f, playerSettings.sprintSpeed);
        playerSettings.sprintDuration = Mathf.Max(0.1f, playerSettings.sprintDuration);
        playerSettings.jumpPower = Mathf.Max(0.1f, playerSettings.jumpPower);
    }

    private static bool WeaponSettingsMatch(WeaponSettings first, WeaponSettings second)
    {
        return Mathf.Approximately(first.bulletDamage, second.bulletDamage)
            && Mathf.Approximately(first.crosshairPointDistance, second.crosshairPointDistance)
            && Mathf.Approximately(first.bulletSpeed, second.bulletSpeed)
            && Mathf.Approximately(first.bulletGravity, second.bulletGravity)
            && first.bulletBounces == second.bulletBounces
            && Mathf.Approximately(first.fireRate, second.fireRate)
            && Mathf.Approximately(first.holdToFireRateThreshold, second.holdToFireRateThreshold)
            && first.bulletsPerShot == second.bulletsPerShot
            && first.magazineSize == second.magazineSize
            && first.startingBullets == second.startingBullets
            && Mathf.Approximately(first.reloadTime, second.reloadTime);
    }

    private bool ShouldUseNetworkSpawning()
    {
        return enableMultiplayerAuthority && NetworkManager.Singleton != null && IsSpawned && IsOwner && !IsServer;
    }

    public bool TryApplyDamage(float damageAmount, ulong shooterClientId)
    {
        if (!IsServer || !enablePlayerDamage)
        {
            return false;
        }

        if (!IsAlive())
        {
            return false;
        }

        currentHealth.Value = Mathf.Max(0f, currentHealth.Value - Mathf.Max(0f, damageAmount));

        Debug.Log($"Player {OwnerClientId} hit by player {shooterClientId} for {damageAmount}. Health: {currentHealth.Value}/{maxHealth}", this);

        if (currentHealth.Value <= 0f)
        {
            Debug.Log($"Player {OwnerClientId} died. Killer: {shooterClientId}", this);
            SetRagdollState(true);
            HandlePlayerDefeatServer(shooterClientId);
        }

        return true;
    }

    private void HandlePlayerDefeatServer(ulong winningClientId)
    {
        if (!IsServer)
        {
            return;
        }

        playerCanMove = false;
        StopRigidbodyMotion();

        if (!IsSpawned)
        {
            return;
        }

        DisableAllPlayerControlsAfterDeath();
        DisableAllPlayerControlsAfterDeathClientRpc();
        int winningScore = AddRoundWin(winningClientId);
        SetCardsUiRoundForAll(winningClientId, OwnerClientId);

        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);
        int[] roundCardPoolIndexes = winnerController != null ? winnerController.DrawCardsUiRoundIndexes() : new int[0];

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiRoundCards(roundCardPoolIndexes);
        }

        UpdatePostMatchUiClientRpc(winningClientId, roundCardPoolIndexes, winningScore);

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

    private void DisableAllPlayerControlsAfterDeath()
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        ClearActiveBullets();

        foreach (FirstPersonController controller in controllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.DisableControlsAfterDeath();
        }
    }

    private static int AddRoundWin(ulong winningClientId)
    {
        int winningScore = 0;
        roundWinsByPlayer.TryGetValue(winningClientId, out winningScore);
        winningScore++;
        roundWinsByPlayer[winningClientId] = winningScore;
        return winningScore;
    }

    private void ActivateScoreboardPoint(ulong winningClientId, int winningScore)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument == null || playerUiDocument.rootVisualElement == null)
        {
            return;
        }

        VisualElement pointsElement = playerUiDocument.rootVisualElement.Q<VisualElement>(PlayerUiPointsName);
        VisualElement playerElement = GetScoreboardPlayerElement(pointsElement, winningClientId);

        if (playerElement == null)
        {
            return;
        }

        List<VisualElement> pointBackgrounds = playerElement.Query<VisualElement>(name: PlayerUiPointBackgroundName).ToList();
        int activatedPoints = 0;

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point == null)
            {
                continue;
            }

            if (point.style.display == DisplayStyle.Flex)
            {
                activatedPoints++;
            }
        }

        if (activatedPoints >= winningScore)
        {
            return;
        }

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point != null && point.style.display != DisplayStyle.Flex)
            {
                point.style.display = DisplayStyle.Flex;
                break;
            }
        }
    }

    private void ActivateScoreboardPointForPlayerIndex(int playerIndex, int winningScore)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument == null || playerUiDocument.rootVisualElement == null || winningScore <= 0)
        {
            return;
        }

        VisualElement pointsElement = playerUiDocument.rootVisualElement.Q<VisualElement>(PlayerUiPointsName);
        VisualElement playerElement = pointsElement != null
            ? pointsElement.Q<VisualElement>(playerIndex == 0 ? PlayerUiPlayer1Name : PlayerUiPlayer2Name)
            : null;

        if (playerElement == null)
        {
            return;
        }

        List<VisualElement> pointBackgrounds = playerElement.Query<VisualElement>(name: PlayerUiPointBackgroundName).ToList();
        int activatedPoints = 0;

        foreach (VisualElement pointBackground in pointBackgrounds)
        {
            VisualElement point = pointBackground.Q<VisualElement>(PlayerUiPointName);

            if (point != null && point.style.display == DisplayStyle.Flex)
            {
                activatedPoints++;
            }
        }

        int pointsToActivate = Mathf.Min(winningScore, pointBackgrounds.Count);

        for (int i = activatedPoints; i < pointsToActivate; i++)
        {
            VisualElement point = pointBackgrounds[i].Q<VisualElement>(PlayerUiPointName);

            if (point != null)
            {
                point.style.display = DisplayStyle.Flex;
            }
        }
    }

    private void RefreshScoreboard()
    {
        ActivateScoreboardPointForPlayerIndex(0, scoreboardPlayer1Score);
        ActivateScoreboardPointForPlayerIndex(1, scoreboardPlayer2Score);
    }

    private VisualElement GetScoreboardPlayerElement(VisualElement pointsElement, ulong winningClientId)
    {
        if (pointsElement == null)
        {
            return null;
        }

        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        int playerIndex = GetScoreboardPlayerIndex(controllers, winningClientId);

        return pointsElement.Q<VisualElement>(playerIndex == 0 ? PlayerUiPlayer1Name : PlayerUiPlayer2Name);
    }

    private int GetScoreboardPlayerIndex(FirstPersonController[] controllers, ulong playerClientId)
    {
        List<FirstPersonController> spawnedControllers = new List<FirstPersonController>();

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null && controller.IsSpawned)
            {
                spawnedControllers.Add(controller);
            }
        }

        spawnedControllers.Sort((first, second) => first.OwnerClientId.CompareTo(second.OwnerClientId));
        return spawnedControllers.FindIndex(controller => controller.OwnerClientId == playerClientId);
    }

    private void ClearActiveBullets()
    {
        Bullet[] bullets = FindObjectsOfType<Bullet>(true);

        foreach (Bullet bullet in bullets)
        {
            if (bullet == null)
            {
                continue;
            }

            if (bullet.TryGetComponent<NetworkObject>(out NetworkObject networkObject) && networkObject.IsSpawned)
            {
                networkObject.Despawn();
            }
            else
            {
                Destroy(bullet.gameObject);
            }
        }
    }

    [ClientRpc]
    private void DisableAllPlayerControlsAfterDeathClientRpc()
    {
        DisableAllPlayerControlsAfterDeath();
    }

    private void DisableControlsAfterDeath()
    {
        controlsDisabledAfterDeath = true;
        moveXInput = 0f;
        moveYInput = 0f;
        isWalking = false;
        isSprinting = false;
        isReloading = false;
        StopRigidbodyMotion();
    }

    [ClientRpc]
    private void UpdatePostMatchUiClientRpc(ulong winningClientId, int[] roundCardPoolIndexes, int winningScore)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        bool isLosingClient = NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == OwnerClientId;
        int winningPlayerIndex = GetScoreboardPlayerIndex(controllers, winningClientId);

        if (winningPlayerIndex == 0)
        {
            scoreboardPlayer1Score = winningScore;
        }
        else if (winningPlayerIndex == 1)
        {
            scoreboardPlayer2Score = winningScore;
        }

        SetCardsUiRoundForAll(winningClientId, OwnerClientId);
        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiRoundCards(roundCardPoolIndexes);
        }

        if (isLosingClient)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        foreach (FirstPersonController controller in controllers)
        {
            if (controller == null)
            {
                continue;
            }

            controller.SetPlayerUiActive(false);

            bool isWinner = controller.IsSpawned && controller.OwnerClientId == winningClientId;
            controller.SetCardsUiActive(isWinner, isWinner && isLosingClient);
        }

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.RefreshScoreboard();
            }
        }

        if (winnerController != null)
        {
            winnerController.ApplyCardsUiCardSelection(0);
        }
    }

    private void SetCardsUiRoundForAll(ulong winningClientId, ulong losingClientId)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.SetCardsUiRound(winningClientId, losingClientId);
            }
        }
    }

    private void SetCardsUiRound(ulong winningClientId, ulong losingClientId)
    {
        bool isNewRound = cardsUiWinningClientId != winningClientId || cardsUiLosingClientId != losingClientId;

        cardsUiWinningClientId = winningClientId;
        cardsUiLosingClientId = losingClientId;

        if (isNewRound)
        {
            selectedCardsUiCardIndex = -1;
            flippedCardsUiCardIndexes.Clear();
            appliedCardsUiCardIndexes.Clear();
        }
    }

    private int[] DrawCardsUiRoundIndexes()
    {
        List<int> availableIndexes = new List<int>();

        if (cardPool != null)
        {
            for (int i = 0; i < cardPool.Length; i++)
            {
                if (cardPool[i] != null)
                {
                    availableIndexes.Add(i);
                }
            }
        }

        if (availableIndexes.Count == 0)
        {
            Debug.LogWarning("CardsUI card pool is empty. No cards will be shown this round.", this);
            return new int[0];
        }

        int cardCount = Mathf.Min(CardsUiCardsPerRound, availableIndexes.Count);
        int[] roundIndexes = new int[cardCount];

        for (int i = 0; i < cardCount; i++)
        {
            int availableIndex = Random.Range(0, availableIndexes.Count);
            roundIndexes[i] = availableIndexes[availableIndex];
            availableIndexes.RemoveAt(availableIndex);
        }

        return roundIndexes;
    }

    private void ApplyCardsUiRoundCards(int[] cardPoolIndexes)
    {
        for (int i = 0; i < currentRoundCards.Length; i++)
        {
            currentRoundCards[i] = null;
        }

        if (cardPoolIndexes != null)
        {
            int cardsToApply = Mathf.Min(CardsUiCardsPerRound, cardPoolIndexes.Length);

            for (int i = 0; i < cardsToApply; i++)
            {
                int cardPoolIndex = cardPoolIndexes[i];

                if (cardPool != null && cardPoolIndex >= 0 && cardPoolIndex < cardPool.Length)
                {
                    currentRoundCards[i] = cardPool[cardPoolIndex];
                }
                else
                {
                    Debug.LogWarning($"CardsUI received invalid card pool index {cardPoolIndex}.", this);
                }
            }
        }

        PopulateCardsUiFromRoundCards();
    }

    [ClientRpc]
    private void SwitchToWinnerCameraOwnerClientRpc(ulong winningClientId, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        ConfigureLocalView();

        FirstPersonController winnerController = FindPlayerByOwnerClientId(winningClientId);

        if (winnerController == null || winnerController == this)
        {
            return;
        }

        winnerController.SetSpectatorCameraActive(true);
    }

    private FirstPersonController FindPlayerByOwnerClientId(ulong ownerClientId)
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null && controller.IsSpawned && controller.OwnerClientId == ownerClientId)
            {
                return controller;
            }
        }

        return null;
    }

    private void SetSpectatorCameraActive(bool isActive)
    {
        if (playerCamera == null)
        {
            return;
        }

        playerCamera.gameObject.SetActive(isActive);

        if (isActive)
        {
            SetExclusiveAudioListeners(playerCamera);
        }
        else
        {
            SetAudioListenersEnabled(playerCamera, false);
        }
    }

    private static void SetExclusiveAudioListeners(Camera activeCamera)
    {
        if (activeCamera == null)
        {
            return;
        }

        AudioListener[] allListeners = FindObjectsOfType<AudioListener>(true);

        foreach (AudioListener audioListener in allListeners)
        {
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }
        }

        SetAudioListenersEnabled(activeCamera, true);
    }

    private static void SetAudioListenersEnabled(Camera cameraSource, bool isEnabled)
    {
        if (cameraSource == null)
        {
            return;
        }

        foreach (AudioListener audioListener in cameraSource.GetComponentsInChildren<AudioListener>(true))
        {
            audioListener.enabled = isEnabled;
        }
    }

    private void ResolveMatchUiDocuments()
    {
        if (playerUiDocument != null && cardsUiDocument != null)
        {
            if (healthUiDocument == null)
            {
                healthUiDocument = playerUiDocument;
            }

            return;
        }

        UIDocument[] uiDocuments = GetComponentsInChildren<UIDocument>(true);

        foreach (UIDocument uiDocument in uiDocuments)
        {
            if (uiDocument == null)
            {
                continue;
            }

            if (playerUiDocument == null && uiDocument.gameObject.name.Equals("PlayerUI", System.StringComparison.OrdinalIgnoreCase))
            {
                playerUiDocument = uiDocument;
            }

            if (cardsUiDocument == null && uiDocument.gameObject.name.Equals("CardsUI", System.StringComparison.OrdinalIgnoreCase))
            {
                cardsUiDocument = uiDocument;
            }
        }

        if (healthUiDocument == null)
        {
            healthUiDocument = playerUiDocument;
        }
    }

    private void SetPlayerUiActive(bool isActive)
    {
        ResolveMatchUiDocuments();

        if (playerUiDocument != null)
        {
            playerUiDocument.gameObject.SetActive(isActive);
        }

        if (isActive)
        {
            ResolveHealthUi();
            UpdateHealthBar(currentHealth.Value);
        }
        else
        {
            healthBarProgressElement = null;
        }
    }

    private void SetCardsUiActive(bool isActive, bool isInteractable = false)
    {
        ResolveMatchUiDocuments();

        if (cardsUiDocument == null)
        {
            return;
        }

        cardsUiDocument.gameObject.SetActive(isActive);

        if (cardsUiDocument.rootVisualElement == null)
        {
            cardsUiWasActive = isActive;
            return;
        }

        if (isActive && !cardsUiWasActive)
        {
            ResetCardsUiRound();
        }

        cardsUiDocument.rootVisualElement.pickingMode = PickingMode.Position;
        cardsUiDocument.rootVisualElement.SetEnabled(true);
        SetCardsUiInputBlocked(isActive && !isInteractable);
        PopulateCardsUiFromRoundCards();
        cardsUiWasActive = isActive;
    }

    private void PopulateCardsUiFromRoundCards()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();

        for (int i = 0; i < cards.Count; i++)
        {
            VisualElement cardElement = cards[i];
            bool hasRoundCard = i < CardsUiCardsPerRound && currentRoundCards[i] != null;

            if (cardElement == null)
            {
                continue;
            }

            cardElement.transform.position = Vector3.zero;
            SetCardsUiCardScale(cardElement, 1f);
            cardElement.style.display = hasRoundCard ? DisplayStyle.Flex : DisplayStyle.None;
            cardElement.SetEnabled(hasRoundCard);

            if (!hasRoundCard)
            {
                continue;
            }

            PopulateCardsUiCardInfo(cardElement, currentRoundCards[i]);
        }
    }

    private void PopulateCardsUiCardInfo(VisualElement cardElement, Card cardData)
    {
        VisualElement infoElement = cardElement.Q<VisualElement>(CardsUiInfoElementName);

        if (infoElement == null)
        {
            Debug.LogWarning($"CardsUI card '{cardElement.name}' is missing an Info child VisualElement.", this);
            return;
        }

        infoElement.Clear();
        infoElement.AddToClassList(CardsUiInfoClassName);
        infoElement.style.display = DisplayStyle.None;

        if (!string.IsNullOrWhiteSpace(cardData.cardName))
        {
            Label titleLabel = new Label(cardData.cardName);
            titleLabel.AddToClassList(CardsUiTitleClassName);
            infoElement.Add(titleLabel);
        }

        if (cardData.abilities != null)
        {
            foreach (CardAbility ability in cardData.abilities)
            {
                AddCardsUiAbilityLabels(infoElement, ability);
            }
        }

        if (cardData.stats != null)
        {
            foreach (CardStat stat in cardData.stats)
            {
                AddCardsUiStatLabel(infoElement, stat);
            }
        }
    }

    private void AddCardsUiStatLabel(VisualElement infoElement, CardStat stat)
    {
        string description = GetCardStatDescription(stat);

        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        VisualElement statRow = new VisualElement();
        statRow.AddToClassList(CardsUiStatRowClassName);

        string keyword = stat.keyword;
        int keywordIndex = !string.IsNullOrEmpty(keyword) ? description.IndexOf(keyword, System.StringComparison.Ordinal) : -1;

        if (keywordIndex < 0)
        {
            Label statLabel = new Label(description);
            statLabel.AddToClassList(CardsUiStatTextClassName);
            statRow.Add(statLabel);
        }
        else
        {
            string textBeforeKeyword = description.Substring(0, keywordIndex);
            string textAfterKeyword = description.Substring(keywordIndex + keyword.Length).TrimStart();

            AddCardsUiInlineLabel(statRow, textBeforeKeyword, CardsUiStatTextClassName);

            Label keywordLabel = new Label(keyword);
            keywordLabel.AddToClassList(CardsUiStatTextClassName);
            keywordLabel.AddToClassList(stat.polarity == CardStatPolarity.Good ? CardsUiStatKeywordGoodClassName : CardsUiStatKeywordBadClassName);
            statRow.Add(keywordLabel);

            AddCardsUiInlineLabel(statRow, textAfterKeyword, CardsUiStatTextClassName);
        }

        infoElement.Add(statRow);
    }

    private string GetCardStatDescription(CardStat stat)
    {
        if (!string.IsNullOrWhiteSpace(stat.description))
        {
            return stat.description;
        }

        string sign = stat.amount > 0f ? "+" : string.Empty;
        string suffix = stat.modifierMode == CardStatModifierMode.Percent ? "%" : string.Empty;
        return $"{sign}{stat.amount}{suffix} {FormatCardStatTargetName(stat.target)}";
    }

    private string FormatCardStatTargetName(CardStatTarget target)
    {
        string targetName = target.ToString();
        string formattedName = string.Empty;

        for (int i = 0; i < targetName.Length; i++)
        {
            char character = targetName[i];

            if (i > 0 && char.IsUpper(character))
            {
                formattedName += " ";
            }

            formattedName += character;
        }

        return formattedName;
    }

    private void AddCardsUiInlineLabel(VisualElement parent, string text, string className)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Label label = new Label(text);
        label.AddToClassList(className);
        parent.Add(label);
    }

    private void AddCardsUiAbilityLabels(VisualElement infoElement, CardAbility ability)
    {
        if (!string.IsNullOrWhiteSpace(ability.description))
        {
            Label abilityDescriptionLabel = new Label(ability.description);
            abilityDescriptionLabel.AddToClassList(CardsUiAbilityDescriptionClassName);
            infoElement.Add(abilityDescriptionLabel);
        }
    }

    private void ProcessCardsUiKeyboardSelection()
    {
        if (!CanLocalClientInteractWithCardsUi()
            || cardsUiDocument == null
            || !cardsUiDocument.gameObject.activeInHierarchy
            || animatingCardsUiCards.Count > 0
            || isCardsUiPickAnimationPlaying)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            int cardIndexToPick = selectedCardsUiCardIndex >= 0 ? selectedCardsUiCardIndex : 0;
            isCardsUiPickAnimationPlaying = true;
            RequestCardsUiCardPickServerRpc(cardIndexToPick);
            return;
        }

        int direction = 0;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            direction = -1;
        }
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            direction = 1;
        }

        if (direction == 0)
        {
            return;
        }

        int cardCount = GetCardsUiCardCount();

        if (cardCount <= 0)
        {
            return;
        }

        int currentIndex = selectedCardsUiCardIndex >= 0 ? selectedCardsUiCardIndex : 0;
        int nextIndex = Mathf.Clamp(currentIndex + direction, 0, cardCount - 1);

        if (nextIndex != selectedCardsUiCardIndex)
        {
            RequestCardsUiCardSelectionServerRpc(nextIndex);
        }
    }

    private void ResetCardsUiRound()
    {
        flippedCardsUiCards.Clear();
        flippedCardsUiCardIndexes.Clear();
        animatingCardsUiCards.Clear();
        selectedCardsUiCardIndex = -1;
        isCardsUiPickAnimationPlaying = false;

        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        int cardsToReset = Mathf.Min(CardsUiCardsPerRound, cards.Count);

        for (int i = 0; i < cardsToReset; i++)
        {
            VisualElement card = cards[i];

            if (card == null)
            {
                continue;
            }

            card.transform.position = Vector3.zero;
            SetCardsUiCardScale(card, 1f);

            VisualElement infoElement = card.Q<VisualElement>(CardsUiInfoElementName);

            if (infoElement != null)
            {
                infoElement.style.display = DisplayStyle.None;
            }
        }
    }

    private bool CanLocalClientInteractWithCardsUi()
    {
        return NetworkManager.Singleton != null
            && NetworkManager.Singleton.LocalClientId == cardsUiLosingClientId
            && OwnerClientId == cardsUiWinningClientId;
    }

    private int GetCardsUiCardCount()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return 0;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        int cardsToCheck = Mathf.Min(CardsUiCardsPerRound, cards.Count);
        int cardCount = 0;

        for (int i = 0; i < cardsToCheck; i++)
        {
            if (currentRoundCards[i] != null)
            {
                cardCount++;
            }
        }

        return cardCount;
    }

    private VisualElement GetCardsUiCardByIndex(int cardIndex)
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return null;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();

        if (cardIndex < 0 || cardIndex >= Mathf.Min(CardsUiCardsPerRound, cards.Count) || currentRoundCards[cardIndex] == null)
        {
            return null;
        }

        return cards[cardIndex];
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCardsUiCardSelectionServerRpc(int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != cardsUiLosingClientId || OwnerClientId != cardsUiWinningClientId)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= CardsUiCardsPerRound || currentRoundCards[cardIndex] == null)
        {
            return;
        }

        BroadcastCardsUiCardSelection(cardIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCardsUiCardPickServerRpc(int cardIndex, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != cardsUiLosingClientId || OwnerClientId != cardsUiWinningClientId)
        {
            return;
        }

        if (cardIndex < 0 || cardIndex >= currentRoundCards.Length || currentRoundCards[cardIndex] == null || appliedCardsUiCardIndexes.Contains(cardIndex))
        {
            return;
        }

        appliedCardsUiCardIndexes.Add(cardIndex);
        FirstPersonController losingController = FindPlayerByOwnerClientId(cardsUiLosingClientId);

        if (losingController != null)
        {
            losingController.ApplyPickedCard(currentRoundCards[cardIndex]);
        }

        BroadcastCardsUiCardPick(cardIndex);
    }

    private void ApplyPickedCard(Card cardData)
    {
        if (cardData == null)
        {
            return;
        }

        ApplyCardStatModifiers(cardData);
        SyncWeaponSettings(GetWeaponSettings(), false);
        SyncPlayerSettings(GetPlayerSettings());
    }

    private void ResetLevelAfterCardPick()
    {
        if (!IsServer)
        {
            return;
        }

        // Check if we should switch scenes
        bool shouldSwitchScene = mapPoolSceneIndices != null && mapPoolSceneIndices.Length > 0
            && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null;

        if (shouldSwitchScene)
        {
            int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
            List<int> availableScenes = new List<int>();

            for (int i = 0; i < mapPoolSceneIndices.Length; i++)
            {
                int sceneIdx = mapPoolSceneIndices[i];
                if (sceneIdx >= 0 && sceneIdx < SceneManager.sceneCountInBuildSettings
                    && sceneIdx != currentSceneIndex)
                {
                    availableScenes.Add(sceneIdx);
                }
            }

            if (availableScenes.Count > 0)
            {
                // Player objects persist across a single-mode scene load (NGO moves them to DontDestroyOnLoad),
                // so OnNetworkSpawn does not run again. Players are reset once every client finished loading.
                int chosen = availableScenes[UnityEngine.Random.Range(0, availableScenes.Count)];
                StartCoroutine(LoadSceneDelayed(chosen));
                return;
            }
        }

        ResetAllPlayersForNextRound();
    }

    private static void ResetAllPlayersForNextRound()
    {
        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>(true);
        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null)
            {
                controller.ResetPlayerForNextRound();
            }
        }
    }

    private IEnumerator LoadSceneDelayed(int sceneIndex)
    {
        // Wait a frame to ensure respawn state is synced
        yield return null;

        Debug.Log($"[MapPool] Switching to scene index {sceneIndex}.", this);
        ClearActiveBullets();

        string scenePath = SceneUtility.GetScenePathByBuildIndex(sceneIndex);
        string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);

        if (!string.IsNullOrEmpty(sceneName))
        {
            Debug.Log($"[MapPool] Loading scene '{sceneName}' for all clients.", this);
            NetworkSceneManager networkSceneManager = NetworkManager.Singleton.SceneManager;
            networkSceneManager.OnLoadEventCompleted += OnMapPoolSceneLoadEventCompleted;
            SceneEventProgressStatus loadStatus = networkSceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            Debug.Log($"[MapPool] Scene load status: {loadStatus}", this);

            if (loadStatus != SceneEventProgressStatus.Started)
            {
                networkSceneManager.OnLoadEventCompleted -= OnMapPoolSceneLoadEventCompleted;
                ResetAllPlayersForNextRound();
            }
        }
        else
        {
            Debug.LogError($"[MapPool] Failed to get scene name for index {sceneIndex}", this);
            ResetAllPlayersForNextRound();
        }
    }

    private static void OnMapPoolSceneLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnMapPoolSceneLoadEventCompleted;
        }

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            Debug.LogWarning($"[MapPool] Clients timed out loading '{sceneName}': {string.Join(", ", clientsTimedOut)}");
        }

        // All clients are now in the new scene, so reset RPCs reach every player.
        ResetAllPlayersForNextRound();
    }

    private void ResetPlayerForNextRound()
    {
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
        currentHealth.Value = Mathf.Max(1f, maxHealth);
        SetRagdollState(false);

        if (SpawnPoint.TryGetSpawnPoint((int)OwnerClientId, out Vector3 pos, out Quaternion rot))
        {
            spawnPosition = pos;
            transform.position = pos;
            transform.rotation = rot;
        }

        ApplyRespawnPosition(spawnPosition);
        ClearCardsUiRoundState();

        if (IsSpawned)
        {
            ResetPlayerForNextRoundClientRpc(spawnPosition, currentHealth.Value);
        }
    }

    [ClientRpc]
    private void ResetPlayerForNextRoundClientRpc(Vector3 resetPosition, float resetHealth)
    {
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
        ApplyRespawnPosition(resetPosition);
        ApplyRagdollState(false);
        ClearCardsUiRoundState();
        ConfigureLocalView();

        if (!enableMultiplayerAuthority || !IsSpawned || IsOwner)
        {
            ForceLocalRoundView();
        }

        ResolveHealthUi();
        UpdateHealthBar(resetHealth);
    }

    private void ForceLocalRoundView()
    {
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(true);
            SetExclusiveAudioListeners(playerCamera);
        }

        SetPlayerUiActive(true);
        SetCardsUiActive(false);
        RefreshScoreboard();

        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            canvas.gameObject.SetActive(true);
        }

        foreach (UIDocument uiDocument in GetComponentsInChildren<UIDocument>(true))
        {
            if (uiDocument == playerUiDocument || uiDocument == cardsUiDocument)
            {
                continue;
            }

            uiDocument.gameObject.SetActive(true);
        }

        ApplyBodyShadowMode(true);

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ApplyRespawnPosition(Vector3 respawnPosition)
    {
        transform.position = respawnPosition;
        StopRigidbodyMotion();
    }

    private void ClearCardsUiRoundState()
    {
        cardsUiWinningClientId = ulong.MaxValue;
        cardsUiLosingClientId = ulong.MaxValue;
        selectedCardsUiCardIndex = -1;
        cardsUiWasActive = false;
        isCardsUiPickAnimationPlaying = false;
        flippedCardsUiCards.Clear();
        flippedCardsUiCardIndexes.Clear();
        appliedCardsUiCardIndexes.Clear();
        animatingCardsUiCards.Clear();

        for (int i = 0; i < currentRoundCards.Length; i++)
        {
            currentRoundCards[i] = null;
        }

        ResetCardsUiCardTransforms();
        SetCardsUiActive(false);
    }

    private void ResetCardsUiCardTransforms()
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            return;
        }

        List<VisualElement> cards = cardsUiDocument.rootVisualElement.Query<VisualElement>(className: CardsUiCardClassName).ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            VisualElement card = cards[i];
            if (card != null)
            {
                card.transform.position = Vector3.zero;
                SetCardsUiCardScale(card, 1f);
            }
        }
    }

    private void ApplyCardStatModifiers(Card cardData)
    {
        if (cardData.stats == null)
        {
            return;
        }

        foreach (CardStat stat in cardData.stats)
        {
            ApplyCardStatModifier(stat);
        }

        NormalizeWeaponSettings();
        NormalizePlayerSettings();
        bulletsInMagazine = Mathf.Clamp(bulletsInMagazine, 0, magazineSize);
        SaveCurrentWeaponSettings();
        SaveCurrentPlayerSettings();
        UpdateHealthBar(currentHealth.Value);
    }

    private void ApplyCardStatModifier(CardStat stat)
    {
        switch (stat.target)
        {
            case CardStatTarget.MaxHealth:
                maxHealth = ApplyCardStatValue(maxHealth, stat);
                break;
            case CardStatTarget.WalkSpeed:
                walkSpeed = ApplyCardStatValue(walkSpeed, stat);
                break;
            case CardStatTarget.SprintSpeed:
                sprintSpeed = ApplyCardStatValue(sprintSpeed, stat);
                break;
            case CardStatTarget.SprintDuration:
                sprintDuration = ApplyCardStatValue(sprintDuration, stat);
                sprintRemaining = Mathf.Min(sprintRemaining, sprintDuration);
                break;
            case CardStatTarget.JumpPower:
                jumpPower = ApplyCardStatValue(jumpPower, stat);
                break;
            case CardStatTarget.BulletDamage:
                bulletDamage = ApplyCardStatValue(bulletDamage, stat);
                break;
            case CardStatTarget.BulletSpeed:
                bulletSpeed = ApplyCardStatValue(bulletSpeed, stat);
                break;
            case CardStatTarget.BulletGravity:
                bulletGravity = ApplyCardStatValue(bulletGravity, stat);
                break;
            case CardStatTarget.BulletBounces:
                bulletBounces = Mathf.RoundToInt(ApplyCardStatValue(bulletBounces, stat));
                break;
            case CardStatTarget.FireRate:
                fireRate = ApplyCardStatValue(fireRate, stat);
                break;
            case CardStatTarget.BulletsPerShot:
                bulletsPerShot = Mathf.RoundToInt(ApplyCardStatValue(bulletsPerShot, stat));
                break;
            case CardStatTarget.MagazineSize:
                magazineSize = Mathf.RoundToInt(ApplyCardStatValue(magazineSize, stat));
                break;
            case CardStatTarget.StartingBullets:
                startingBullets = Mathf.RoundToInt(ApplyCardStatValue(startingBullets, stat));
                break;
            case CardStatTarget.ReloadTime:
                reloadTime = ApplyCardStatValue(reloadTime, stat);
                break;
            case CardStatTarget.CrosshairPointDistance:
                crosshairPointDistance = ApplyCardStatValue(crosshairPointDistance, stat);
                break;
        }
    }

    private float ApplyCardStatValue(float currentValue, CardStat stat)
    {
        if (stat.modifierMode == CardStatModifierMode.Percent)
        {
            return currentValue * (1f + stat.amount * .01f);
        }

        return currentValue + stat.amount;
    }

    private void NormalizePlayerSettings()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        walkSpeed = Mathf.Max(0.1f, walkSpeed);
        sprintSpeed = Mathf.Max(0.1f, sprintSpeed);
        sprintDuration = Mathf.Max(0.1f, sprintDuration);
        sprintRemaining = Mathf.Clamp(sprintRemaining, 0f, sprintDuration);
        jumpPower = Mathf.Max(0.1f, jumpPower);
    }

    private void BroadcastCardsUiCardSelection(int cardIndex)
    {
        ApplyCardsUiCardSelection(cardIndex);

        if (IsSpawned)
        {
            ApplyCardsUiCardSelectionClientRpc(cardIndex);
        }
    }

    [ClientRpc]
    private void ApplyCardsUiCardSelectionClientRpc(int cardIndex)
    {
        ApplyCardsUiCardSelection(cardIndex);
    }

    private void BroadcastCardsUiCardPick(int cardIndex)
    {
        PlayCardsUiCardPickAnimation(cardIndex);

        if (IsSpawned)
        {
            PlayCardsUiCardPickAnimationClientRpc(cardIndex);
        }
    }

    [ClientRpc]
    private void PlayCardsUiCardPickAnimationClientRpc(int cardIndex)
    {
        if (IsServer)
        {
            return;
        }

        PlayCardsUiCardPickAnimation(cardIndex);
    }

    private void PlayCardsUiCardPickAnimation(int cardIndex)
    {
        isCardsUiPickAnimationPlaying = true;
        if (cardsUiDocument != null && cardsUiDocument.rootVisualElement != null)
        {
            SetCardsUiInputBlocked(true);
        }
        StartCoroutine(AnimateCardsUiPickSequence(cardIndex));
    }

    private IEnumerator AnimateCardsUiPickSequence(int chosenIndex)
    {
        if (cardsUiDocument == null || cardsUiDocument.rootVisualElement == null)
        {
            isCardsUiPickAnimationPlaying = false;
            if (IsServer)
            {
                ResetLevelAfterCardPick();
            }
            yield break;
        }

        VisualElement chosenCard = GetCardsUiCardByIndex(chosenIndex);
        List<VisualElement> otherCards = new List<VisualElement>();
        int totalCards = GetCardsUiCardCount();

        for (int i = 0; i < totalCards; i++)
        {
            if (i != chosenIndex)
            {
                VisualElement otherCard = GetCardsUiCardByIndex(i);
                if (otherCard != null)
                {
                    otherCards.Add(otherCard);
                }
            }
        }

        // 1. Chosen Card Animation: Gets a little bigger and then scales down to nothing.
        if (chosenCard != null)
        {
            animatingCardsUiCards.Add(chosenCard);
            EnableCardsUiCardInfo(chosenCard);

            float startScale = chosenCard.transform.scale.x > 0.01f ? chosenCard.transform.scale.x : CardsUiSelectedScale;
            float popScale = CardsUiPickGrowScale;

            // Phase 1A: Grow slightly bigger
            float elapsedGrow = 0f;
            while (elapsedGrow < CardsUiPickGrowDuration)
            {
                float t = elapsedGrow / CardsUiPickGrowDuration;
                float currentScale = Mathf.SmoothStep(startScale, popScale, t);
                SetCardsUiCardScale(chosenCard, currentScale);
                elapsedGrow += Time.unscaledDeltaTime;
                yield return null;
            }
            SetCardsUiCardScale(chosenCard, popScale);

            // Phase 1B: Scale down to nothing
            float elapsedShrink = 0f;
            while (elapsedShrink < CardsUiPickShrinkDuration)
            {
                float t = elapsedShrink / CardsUiPickShrinkDuration;
                float currentScale = Mathf.Lerp(popScale, 0f, t * t);
                SetCardsUiCardScale(chosenCard, currentScale);
                elapsedShrink += Time.unscaledDeltaTime;
                yield return null;
            }
            SetCardsUiCardScale(chosenCard, 0f);
            chosenCard.style.display = DisplayStyle.None;
            animatingCardsUiCards.Remove(chosenCard);
        }

        // 2. Other Cards Move Down and Out of the Screen afterwards
        if (otherCards.Count > 0)
        {
            foreach (VisualElement card in otherCards)
            {
                animatingCardsUiCards.Add(card);
            }

            float rootHeight = cardsUiDocument.rootVisualElement.layout.height > 0
                ? cardsUiDocument.rootVisualElement.layout.height
                : Screen.height;
            float dropDistance = Mathf.Max(rootHeight, Screen.height) + 500f;

            float elapsedDrop = 0f;
            while (elapsedDrop < CardsUiDismissOthersDuration)
            {
                float t = elapsedDrop / CardsUiDismissOthersDuration;
                // Quadratic ease-in to simulate accelerating downwards off screen
                float currentY = Mathf.Lerp(0f, dropDistance, t * t);

                foreach (VisualElement card in otherCards)
                {
                    card.transform.position = new Vector3(0f, currentY, 0f);
                }

                elapsedDrop += Time.unscaledDeltaTime;
                yield return null;
            }

            foreach (VisualElement card in otherCards)
            {
                card.transform.position = new Vector3(0f, dropDistance, 0f);
                card.style.display = DisplayStyle.None;
                animatingCardsUiCards.Remove(card);
            }
        }

        if (CardsUiPostPickDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(CardsUiPostPickDelay);
        }

        isCardsUiPickAnimationPlaying = false;

        if (IsServer)
        {
            ResetLevelAfterCardPick();
        }
    }

    private void ApplyCardsUiCardSelection(int cardIndex)
    {
        VisualElement selectedCard = GetCardsUiCardByIndex(cardIndex);

        if (selectedCard == null)
        {
            selectedCardsUiCardIndex = cardIndex;
            return;
        }

        selectedCardsUiCardIndex = cardIndex;
        ApplyCardsUiCardScales();

        if (!flippedCardsUiCardIndexes.Contains(cardIndex) && !animatingCardsUiCards.Contains(selectedCard))
        {
            StartCoroutine(FlipCardsUiCard(selectedCard, cardIndex));
        }
    }

    private void ApplyCardsUiCardScales()
    {
        int cardCount = GetCardsUiCardCount();

        for (int i = 0; i < cardCount; i++)
        {
            VisualElement card = GetCardsUiCardByIndex(i);

            if (card == null || animatingCardsUiCards.Contains(card))
            {
                continue;
            }

            SetCardsUiCardScale(card, i == selectedCardsUiCardIndex ? CardsUiSelectedScale : 1f);
        }
    }

    private IEnumerator FlipCardsUiCard(VisualElement card, int cardIndex)
    {
        animatingCardsUiCards.Add(card);

        float halfDuration = Mathf.Max(.01f, CardsUiFlipDuration * .5f);
        float targetScale = cardIndex == selectedCardsUiCardIndex ? CardsUiSelectedScale : 1f;

        yield return LerpCardsUiCardXScale(card, targetScale, 0f, targetScale, halfDuration);
        EnableCardsUiCardInfo(card);
        yield return LerpCardsUiCardXScale(card, 0f, targetScale, targetScale, halfDuration);

        animatingCardsUiCards.Remove(card);
        flippedCardsUiCards.Add(card);
        flippedCardsUiCardIndexes.Add(cardIndex);
        ApplyCardsUiCardScales();
    }

    private IEnumerator LerpCardsUiCardXScale(VisualElement card, float fromScale, float toScale, float yScale, float duration)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            float normalizedTime = elapsedTime / duration;
            float xScale = Mathf.Lerp(fromScale, toScale, normalizedTime);
            card.transform.scale = new Vector3(xScale, yScale, 1f);
            elapsedTime += Time.unscaledDeltaTime;
            yield return null;
        }

        card.transform.scale = new Vector3(toScale, yScale, 1f);
    }

    private void SetCardsUiCardScale(VisualElement card, float scale)
    {
        card.transform.scale = new Vector3(scale, scale, 1f);
    }

    private void EnableCardsUiCardInfo(VisualElement card)
    {
        VisualElement infoElement = card.Q<VisualElement>(CardsUiInfoElementName);

        if (infoElement != null)
        {
            infoElement.style.display = DisplayStyle.Flex;
            infoElement.SetEnabled(true);
        }
    }

    private void SetCardsUiInputBlocked(bool isBlocked)
    {
        VisualElement root = cardsUiDocument.rootVisualElement;
        VisualElement blocker = root.Q<VisualElement>(CardsUiInputBlockerName);

        if (!isBlocked)
        {
            blocker?.RemoveFromHierarchy();
            return;
        }

        if (blocker == null)
        {
            blocker = new VisualElement
            {
                name = CardsUiInputBlockerName,
                pickingMode = PickingMode.Position
            };

            blocker.style.position = Position.Absolute;
            blocker.style.left = 0;
            blocker.style.right = 0;
            blocker.style.top = 0;
            blocker.style.bottom = 0;
            blocker.style.backgroundColor = Color.clear;

            blocker.RegisterCallback<PointerDownEvent>(eventBase => eventBase.StopImmediatePropagation());
            blocker.RegisterCallback<PointerUpEvent>(eventBase => eventBase.StopImmediatePropagation());
            blocker.RegisterCallback<ClickEvent>(eventBase => eventBase.StopImmediatePropagation());

            root.Add(blocker);
        }

        blocker.BringToFront();
    }

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

    private Vector3 GetCrosshairPoint()
    {
        if (playerCamera == null)
        {
            return shootPoint.position + shootPoint.forward * Mathf.Max(0.1f, crosshairPointDistance);
        }

        Ray centerRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        return centerRay.GetPoint(Mathf.Max(0.1f, crosshairPointDistance));
    }

    private Vector3 GetShootDirection(Vector3 targetPoint)
    {
        Vector3 shootDirection = targetPoint - shootPoint.position;

        if (shootDirection.sqrMagnitude <= 0.0001f)
        {
            return shootPoint.forward;
        }

        return shootDirection.normalized;
    }

    private void TryStartReload()
    {
        if (isReloading)
        {
            return;
        }

        if (bulletsInMagazine >= magazineSize)
        {
            return;
        }

        StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;
        float elapsed = 0f;

        if (progressRing != null)
        {
            progressRing.fillAmount = 0f;
        }

        if (progressRingCG != null)
        {
            progressRingCG.alpha = 0f;
        }

        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / reloadTime);

            if (progressRing != null)
            {
                progressRing.fillAmount = progress;
            }

            if (progressRingCG != null)
            {
                progressRingCG.alpha = Mathf.Min(progress * 10f, (1f - progress) * 10f, 1f);
            }

            yield return null;
        }

        bulletsInMagazine = magazineSize;
        isReloading = false;

        if (progressRing != null)
        {
            progressRing.fillAmount = 1f;
        }

        if (progressRingCG != null)
        {
            progressRingCG.alpha = 0f;
        }
    }

    private void CheckGround()
    {
        Vector3 origin = new Vector3(transform.position.x, transform.position.y + (transform.localScale.y * .5f), transform.position.z);
        Vector3 direction = transform.TransformDirection(Vector3.down);
        float distance = .75f;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, distance))
        {
            Debug.DrawRay(origin, direction * distance, Color.red);
            isGrounded = true;
        }
        else
        {
            isGrounded = false;
        }
    }

    private void Jump()
    {
        if (isGrounded)
        {
            rb.AddForce(0f, jumpPower, 0f, ForceMode.Impulse);
            isGrounded = false;

            SetAnimatorTrigger(jumpTriggerParameter);
        }

        if(isCrouched && !holdToCrouch)
        {
            Crouch();
        }
    }

    private void Crouch()
    {
        if(isCrouched)
        {
            transform.localScale = new Vector3(originalScale.x, originalScale.y, originalScale.z);
            walkSpeed /= speedReduction;

            isCrouched = false;
        }
        else
        {
            transform.localScale = new Vector3(originalScale.x, crouchHeight, originalScale.z);
            walkSpeed *= speedReduction;

            isCrouched = true;
        }
    }

    private void HeadBob()
    {
        if(isWalking)
        {
            if(isSprinting)
            {
                timer += Time.deltaTime * (bobSpeed + sprintSpeed);
            }
            else if (isCrouched)
            {
                timer += Time.deltaTime * (bobSpeed * speedReduction);
            }
            else
            {
                timer += Time.deltaTime * bobSpeed;
            }

            joint.localPosition = new Vector3(
                jointOriginalPos.x + Mathf.Sin(timer) * bobAmount.x,
                jointOriginalPos.y + Mathf.Sin(timer) * bobAmount.y,
                jointOriginalPos.z + Mathf.Sin(timer) * bobAmount.z);
        }
        else
        {
            timer = 0;
            joint.localPosition = new Vector3(
                Mathf.Lerp(joint.localPosition.x, jointOriginalPos.x, Time.deltaTime * bobSpeed),
                Mathf.Lerp(joint.localPosition.y, jointOriginalPos.y, Time.deltaTime * bobSpeed),
                Mathf.Lerp(joint.localPosition.z, jointOriginalPos.z, Time.deltaTime * bobSpeed));
        }
    }

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
    }

    private bool HasAnimatorParameter(string parameterName)
    {
        return !string.IsNullOrEmpty(parameterName) && characterAnimator != null && animatorParameters.Contains(parameterName);
    }

    private void SetAnimatorFloat(string parameterName, float value)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetFloat(parameterName, value, animationDampTime, Time.deltaTime);
        }
    }

    private void SetAnimatorFloat(string parameterName, float value, float dampTime)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetFloat(parameterName, value, dampTime, Time.deltaTime);
        }
    }

    private void SetAnimatorBool(string parameterName, bool value)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetBool(parameterName, value);
        }
    }

    private void SetAnimatorTrigger(string parameterName)
    {
        if (HasAnimatorParameter(parameterName))
        {
            characterAnimator.SetTrigger(parameterName);
        }
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
        SetAnimatorFloat(lookValueParameter, lookValue, lookAnimationDampTime);
        SetAnimatorBool(isMovingParameter, isMovingNow);
        SetAnimatorBool(isSprintingParameter, isSprinting);
        SetAnimatorBool(isGroundedParameter, isGrounded);
        SetAnimatorBool(isCrouchedParameter, isCrouched);
        SetAnimatorBool(isReloadingParameter, isReloading);
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
        ApplySecondPlayerBodyMaterial();

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

        ApplyBodyShadowMode(isLocalPlayer);
    }

    private void CacheBodyRenderers()
    {
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
        if (!IsSpawned || NetworkManager.Singleton == null || secondPlayerBodyMaterial == null)
        {
            return false;
        }

        FirstPersonController[] controllers = FindObjectsOfType<FirstPersonController>();
        List<FirstPersonController> spawnedControllers = new List<FirstPersonController>();

        foreach (FirstPersonController controller in controllers)
        {
            if (controller != null && controller.IsSpawned)
            {
                spawnedControllers.Add(controller);
            }
        }

        spawnedControllers.Sort((first, second) => first.OwnerClientId.CompareTo(second.OwnerClientId));
        return spawnedControllers.Count >= 2 && spawnedControllers[1] == this;
    }

    private void ApplySecondPlayerBodyMaterial()
    {
        if (playerBodyRenderers == null)
        {
            return;
        }

        bool isSecondPlayer = IsSecondPlayerController();

        for (int i = 0; i < playerBodyRenderers.Length; i++)
        {
            Renderer bodyRenderer = playerBodyRenderers[i];

            if (bodyRenderer == null)
            {
                continue;
            }

            if (isSecondPlayer)
            {
                if (secondPlayerBodyMaterial == null)
                {
                    continue;
                }

                Material[] materials = bodyRenderer.materials;

                if (materials != null && materials.Length > 0)
                {
                    materials[0] = secondPlayerBodyMaterial;
                    bodyRenderer.materials = materials;
                }
            }
            else if (originalBodyMaterials != null && i < originalBodyMaterials.Length && originalBodyMaterials[i] != null)
            {
                bodyRenderer.materials = originalBodyMaterials[i];
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

    private void ApplyBodyShadowMode(bool isLocalPlayer)
    {
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

            bodyRenderer.shadowCastingMode = isLocalPlayer && originalBodyShadowModes != null && i < originalBodyShadowModes.Length
                ? originalBodyShadowModes[i]
                : ShadowCastingMode.On;
        }
    }

    private void ResolveHealthUi()
    {
        ResolveMatchUiDocuments();

        if (healthUiDocument == null)
        {
            healthUiDocument = GetComponentInChildren<UIDocument>(true);
        }

        if (healthUiDocument == null || !healthUiDocument.gameObject.activeInHierarchy || healthUiDocument.rootVisualElement == null)
        {
            healthBarProgressElement = null;
            return;
        }

        healthBarProgressElement = healthUiDocument.rootVisualElement.Q<VisualElement>("BarProgress");
    }

    private void OnCurrentHealthChanged(float previousValue, float newValue)
    {
        UpdateHealthBar(newValue);
    }

    private void UpdateHealthBar(float healthValue)
    {
        if (healthBarProgressElement == null || healthBarProgressElement.panel == null ||
            (healthUiDocument != null && (healthUiDocument.rootVisualElement == null || healthBarProgressElement.panel != healthUiDocument.rootVisualElement.panel)))
        {
            ResolveHealthUi();
        }

        if (healthBarProgressElement == null)
        {
            return;
        }

        float maxHealthValue = Mathf.Max(1f, maxHealth);
        float normalizedHealth = Mathf.Clamp01(healthValue / maxHealthValue);
        healthBarProgressElement.style.width = Length.Percent(normalizedHealth * 100f);
    }
}



// Custom Editor
#if UNITY_EDITOR
    [CustomEditor(typeof(FirstPersonController)), InitializeOnLoadAttribute]
    public class FirstPersonControllerEditor : Editor
    {
    FirstPersonController fpc;
    SerializedObject SerFPC;

    private void OnEnable()
    {
        fpc = (FirstPersonController)target;
        SerFPC = new SerializedObject(fpc);
    }

    public override void OnInspectorGUI()
    {
        SerFPC.Update();

        EditorGUILayout.Space();
        GUILayout.Label("Modular First Person Controller", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 });
        GUILayout.Label("By Jess Case", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal, fontSize = 12 });
        GUILayout.Label("version 1.0.1", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal, fontSize = 12 });
        EditorGUILayout.Space();

        #region Multiplayer Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Multiplayer Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.enableMultiplayerAuthority = EditorGUILayout.ToggleLeft(new GUIContent("Enable Multiplayer Authority", "Only the owning network player reads input and uses local camera/UI when this object is spawned by Netcode."), fpc.enableMultiplayerAuthority);
        fpc.bodyRoot = (Transform)EditorGUILayout.ObjectField(new GUIContent("Body Root", "Optional root used to find the renderers that should stay Shadows Only locally and become visible for remote players."), fpc.bodyRoot, typeof(Transform), true);
        fpc.autoFindBodyRenderers = EditorGUILayout.ToggleLeft(new GUIContent("Auto Find Body Renderers", "Automatically finds body renderers under Body Root or this player object, excluding camera children."), fpc.autoFindBodyRenderers);
        fpc.secondPlayerBodyMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Second Player Material", "Material assigned to the second spawned player on their main body renderer(s)."), fpc.secondPlayerBodyMaterial, typeof(Material), false);

        SerializedProperty bodyRenderersProperty = SerFPC.FindProperty("playerBodyRenderers");
        EditorGUILayout.PropertyField(bodyRenderersProperty, new GUIContent("Body Renderers"), true);

        EditorGUILayout.Space();

        #endregion

        #region Combat Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Combat Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.enablePlayerDamage = EditorGUILayout.ToggleLeft(new GUIContent("Enable Player Damage", "Allows bullets to damage other players on the server."), fpc.enablePlayerDamage);

        GUI.enabled = fpc.enablePlayerDamage;
        fpc.maxHealth = EditorGUILayout.Slider(new GUIContent("Max Health", "Health restored on spawn and respawn."), fpc.maxHealth, 1f, 500f);
        fpc.respawnDelay = EditorGUILayout.Slider(new GUIContent("Respawn Delay", "Delay in seconds before the player respawns after death."), fpc.respawnDelay, 0f, 15f);
        fpc.respawnRadius = EditorGUILayout.Slider(new GUIContent("Respawn Radius", "Random respawn offset around the original spawn point."), fpc.respawnRadius, 0f, 10f);
        fpc.playerUiDocument = (UIDocument)EditorGUILayout.ObjectField(new GUIContent("Player UI Document", "Primary in-game PlayerUI document to disable after a death."), fpc.playerUiDocument, typeof(UIDocument), true);
        fpc.cardsUiDocument = (UIDocument)EditorGUILayout.ObjectField(new GUIContent("Cards UI Document", "CardsUI document to enable for the winning player after a death."), fpc.cardsUiDocument, typeof(UIDocument), true);
        fpc.healthUiDocument = (UIDocument)EditorGUILayout.ObjectField(new GUIContent("Health UI Document", "UI document containing the BarProgress health element."), fpc.healthUiDocument, typeof(UIDocument), true);

        SerializedProperty cardPoolProperty = SerFPC.FindProperty("cardPool");
        EditorGUILayout.PropertyField(cardPoolProperty, new GUIContent("Card Pool", "Cards that can be randomly drawn for the post-death card round."), true);

        fpc.enableRagdollOnDeath = EditorGUILayout.ToggleLeft(new GUIContent("Enable Ragdoll On Death", "Turns bone rigidbodies non-kinematic, makes ragdoll colliders solid, and disables the normal player collider on death."), fpc.enableRagdollOnDeath);

        GUI.enabled = fpc.enablePlayerDamage && fpc.enableRagdollOnDeath;
        fpc.normalCollider = (Collider)EditorGUILayout.ObjectField(new GUIContent("Normal Collider", "The main movement collider to disable while ragdoll is active."), fpc.normalCollider, typeof(Collider), true);
        fpc.ragdollRoot = (Transform)EditorGUILayout.ObjectField(new GUIContent("Ragdoll Root", "Optional root used to auto-find ragdoll bone rigidbodies and colliders."), fpc.ragdollRoot, typeof(Transform), true);
        fpc.autoFindRagdollParts = EditorGUILayout.ToggleLeft(new GUIContent("Auto Find Ragdoll Parts", "Automatically fills empty ragdoll arrays from Ragdoll Root, Body Root, or this player object."), fpc.autoFindRagdollParts);
        fpc.disableAnimatorOnRagdoll = EditorGUILayout.ToggleLeft(new GUIContent("Disable Animator On Ragdoll", "Disables the character animator while ragdoll physics are active."), fpc.disableAnimatorOnRagdoll);

        SerializedProperty ragdollRigidbodiesProperty = SerFPC.FindProperty("ragdollRigidbodies");
        EditorGUILayout.PropertyField(ragdollRigidbodiesProperty, new GUIContent("Ragdoll Rigidbodies"), true);

        SerializedProperty ragdollCollidersProperty = SerFPC.FindProperty("ragdollColliders");
        EditorGUILayout.PropertyField(ragdollCollidersProperty, new GUIContent("Ragdoll Colliders"), true);
        GUI.enabled = true;

        EditorGUILayout.Space();

        SerializedProperty mapPoolProperty = SerFPC.FindProperty("mapPoolSceneIndices");
        EditorGUILayout.PropertyField(mapPoolProperty, new GUIContent("Map Pool Scene Indices", "Scene indices from Build Settings to use as the map pool. Leave empty to stay in current scene."), true);

        EditorGUILayout.Space();

        #endregion

        #region Camera Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Camera Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.playerCamera = (Camera)EditorGUILayout.ObjectField(new GUIContent("Camera", "Camera attached to the controller."), fpc.playerCamera, typeof(Camera), true);
        fpc.fov = EditorGUILayout.Slider(new GUIContent("Field of View", "The camera’s view angle. Changes the player camera directly."), fpc.fov, fpc.zoomFOV, 179f);
        fpc.cameraCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Look Control", "Determines if mouse look is allowed to rotate the player and drive vertical look animation."), fpc.cameraCanMove);

        GUI.enabled = fpc.cameraCanMove;
        fpc.invertCamera = EditorGUILayout.ToggleLeft(new GUIContent("Invert Vertical Look", "Inverts the up and down movement sent to the look animation value."), fpc.invertCamera);
        fpc.mouseSensitivity = EditorGUILayout.Slider(new GUIContent("Look Sensitivity", "Determines how sensitive the mouse movement is."), fpc.mouseSensitivity, .1f, 10f);
        fpc.maxLookAngle = EditorGUILayout.Slider(new GUIContent("Look Value Scale", "Mouse Y movement is divided by this value before being added to LookValue."), fpc.maxLookAngle, 40, 90);
        GUI.enabled = true;

        fpc.lockCursor = EditorGUILayout.ToggleLeft(new GUIContent("Lock and Hide Cursor", "Turns off the cursor visibility and locks it to the middle of the screen."), fpc.lockCursor);

        fpc.crosshair = EditorGUILayout.ToggleLeft(new GUIContent("Auto Crosshair", "Determines if the basic crosshair will be turned on, and sets is to the center of the screen."), fpc.crosshair);

        if(fpc.crosshair) 
        { 
            EditorGUI.indentLevel++; 
            EditorGUILayout.BeginHorizontal(); 
            EditorGUILayout.PrefixLabel(new GUIContent("Crosshair Image", "Sprite to use as the crosshair.")); 
            fpc.crosshairImage = (Sprite)EditorGUILayout.ObjectField(fpc.crosshairImage, typeof(Sprite), false);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            fpc.crosshairColor = EditorGUILayout.ColorField(new GUIContent("Crosshair Color", "Determines the color of the crosshair."), fpc.crosshairColor);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--; 
        }

        EditorGUILayout.Space();

        #region Camera Zoom Setup

        GUILayout.Label("Zoom", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableZoom = EditorGUILayout.ToggleLeft(new GUIContent("Enable Zoom", "Determines if the player is able to zoom in while playing."), fpc.enableZoom);

        GUI.enabled = fpc.enableZoom;
        fpc.holdToZoom = EditorGUILayout.ToggleLeft(new GUIContent("Hold to Zoom", "Requires the player to hold the zoom key instead if pressing to zoom and unzoom."), fpc.holdToZoom);
        fpc.zoomKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Zoom Key", "Determines what key is used to zoom."), fpc.zoomKey);
        fpc.zoomFOV = EditorGUILayout.Slider(new GUIContent("Zoom FOV", "Determines the field of view the camera zooms to."), fpc.zoomFOV, .1f, fpc.fov);
        fpc.zoomStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while zooming in."), fpc.zoomStepTime, .1f, 10f);
        GUI.enabled = true;

        #endregion

        #endregion

        #region Movement Setup

        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Movement Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.playerCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Player Movement", "Determines if the player is allowed to move."), fpc.playerCanMove);

        GUI.enabled = fpc.playerCanMove;
        fpc.walkSpeed = EditorGUILayout.Slider(new GUIContent("Walk Speed", "Determines how fast the player will move while walking."), fpc.walkSpeed, .1f, fpc.sprintSpeed);
        GUI.enabled = true;

        EditorGUILayout.Space();

        #region Sprint

        GUILayout.Label("Sprint", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableSprint = EditorGUILayout.ToggleLeft(new GUIContent("Enable Sprint", "Determines if the player is allowed to sprint."), fpc.enableSprint);

        GUI.enabled = fpc.enableSprint;
        fpc.unlimitedSprint = EditorGUILayout.ToggleLeft(new GUIContent("Unlimited Sprint", "Determines if 'Sprint Duration' is enabled. Turning this on will allow for unlimited sprint."), fpc.unlimitedSprint);
        fpc.sprintKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Sprint Key", "Determines what key is used to sprint."), fpc.sprintKey);
        fpc.sprintSpeed = EditorGUILayout.Slider(new GUIContent("Sprint Speed", "Determines how fast the player will move while sprinting."), fpc.sprintSpeed, fpc.walkSpeed, 20f);

        fpc.sprintDuration = EditorGUILayout.Slider(new GUIContent("Sprint Duration", "Determines how long the player can sprint while unlimited sprint is disabled."), fpc.sprintDuration, 1f, 20f);
        fpc.sprintCooldown = EditorGUILayout.Slider(new GUIContent("Sprint Cooldown", "Determines how long the recovery time is when the player runs out of sprint."), fpc.sprintCooldown, .1f, fpc.sprintDuration);

        fpc.sprintFOV = EditorGUILayout.Slider(new GUIContent("Sprint FOV", "Determines the field of view the camera changes to while sprinting."), fpc.sprintFOV, fpc.fov, 179f);
        fpc.sprintFOVStepTime = EditorGUILayout.Slider(new GUIContent("Step Time", "Determines how fast the FOV transitions while sprinting."), fpc.sprintFOVStepTime, .1f, 20f);

        fpc.useSprintBar = EditorGUILayout.ToggleLeft(new GUIContent("Use Sprint Bar", "Determines if the default sprint bar will appear on screen."), fpc.useSprintBar);

        if(fpc.useSprintBar)
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            fpc.hideBarWhenFull = EditorGUILayout.ToggleLeft(new GUIContent("Hide Full Bar", "Hides the sprint bar when sprint duration is full, and fades the bar in when sprinting. Disabling this will leave the bar on screen at all times when the sprint bar is enabled."), fpc.hideBarWhenFull);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Bar BG", "Object to be used as sprint bar background."));
            fpc.sprintBarBG = (Image)EditorGUILayout.ObjectField(fpc.sprintBarBG, typeof(Image), true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Bar", "Object to be used as sprint bar foreground."));
            fpc.sprintBar = (Image)EditorGUILayout.ObjectField(fpc.sprintBar, typeof(Image), true);
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.BeginHorizontal();
            fpc.sprintBarWidthPercent = EditorGUILayout.Slider(new GUIContent("Bar Width", "Determines the width of the sprint bar."), fpc.sprintBarWidthPercent, .1f, .5f);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            fpc.sprintBarHeightPercent = EditorGUILayout.Slider(new GUIContent("Bar Height", "Determines the height of the sprint bar."), fpc.sprintBarHeightPercent, .001f, .025f);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }
        GUI.enabled = true;

        EditorGUILayout.Space();

        #endregion

        #region Jump

        GUILayout.Label("Jump", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableJump = EditorGUILayout.ToggleLeft(new GUIContent("Enable Jump", "Determines if the player is allowed to jump."), fpc.enableJump);

        GUI.enabled = fpc.enableJump;
        fpc.jumpKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Jump Key", "Determines what key is used to jump."), fpc.jumpKey);
        fpc.jumpPower = EditorGUILayout.Slider(new GUIContent("Jump Power", "Determines how high the player will jump."), fpc.jumpPower, .1f, 20f);
        GUI.enabled = true;

        EditorGUILayout.Space();

        #endregion

        #region Shooting

        GUILayout.Label("Shooting", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableShooting = EditorGUILayout.ToggleLeft(new GUIContent("Enable Shooting", "Determines if the player can fire and reload."), fpc.enableShooting);

        GUI.enabled = fpc.enableShooting;
        fpc.shootKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Shoot Key", "Determines what key is used to shoot."), fpc.shootKey);
        fpc.reloadKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Reload Key", "Determines what key is used to reload."), fpc.reloadKey);
        fpc.shootPoint = (Transform)EditorGUILayout.ObjectField(new GUIContent("Shoot Point", "Transform used as spawn point and direction for bullets."), fpc.shootPoint, typeof(Transform), true);
        fpc.bulletPrefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Bullet Prefab", "Prefab spawned when shooting. Should contain a Rigidbody."), fpc.bulletPrefab, typeof(GameObject), false);
        fpc.bulletDamage = EditorGUILayout.Slider(new GUIContent("Bullet Damage", "Damage assigned to each spawned bullet."), fpc.bulletDamage, .1f, 500f);
        fpc.crosshairPointDistance = EditorGUILayout.Slider(new GUIContent("Crosshair Point Distance", "Distance from the camera where the screen-center crosshair point is placed. Bullet gravity is ignored until the projectile reaches this distance from the muzzle."), fpc.crosshairPointDistance, 0.1f, 500f);
        fpc.bulletSpeed = EditorGUILayout.Slider(new GUIContent("Bullet Speed", "Speed applied to each spawned bullet."), fpc.bulletSpeed, 1f, 200f);
        fpc.bulletGravity = EditorGUILayout.Slider(new GUIContent("Bullet Gravity", "Vertical acceleration applied to each spawned bullet. Positive values pull down, negative values pull up, and 0 means no bullet gravity."), fpc.bulletGravity, -50f, 50f);
        fpc.bulletBounces = EditorGUILayout.IntSlider(new GUIContent("Bullet Bounces", "Number of times each bullet can bounce off non-player surfaces before despawning."), fpc.bulletBounces, 0, 20);
        fpc.fireRate = EditorGUILayout.Slider(new GUIContent("Fire Rate", "Shots allowed per second."), fpc.fireRate, .1f, 30f);
        fpc.holdToFireRateThreshold = EditorGUILayout.Slider(new GUIContent("Hold To Fire Rate Threshold", "Fire Rate required before holding Shoot Key continuously fires."), fpc.holdToFireRateThreshold, .1f, 30f);
        fpc.bulletsPerShot = EditorGUILayout.IntSlider(new GUIContent("Bullets Per Shot", "Bullets fired per trigger pull. Multiple bullets are fired one after another very quickly."), fpc.bulletsPerShot, 1, 20);
        fpc.bulletBurstInterval = EditorGUILayout.Slider(new GUIContent("Bullet Burst Interval", "Delay in seconds between bullets when Bullets Per Shot is greater than 1."), fpc.bulletBurstInterval, 0f, .2f);
        fpc.magazineSize = EditorGUILayout.IntSlider(new GUIContent("Magazine Size", "Maximum bullets before reloading."), fpc.magazineSize, 1, 100);
        fpc.startingBullets = EditorGUILayout.IntSlider(new GUIContent("Starting Bullets", "Bullets loaded in the magazine at start."), fpc.startingBullets, 1, fpc.magazineSize);
        fpc.reloadTime = EditorGUILayout.Slider(new GUIContent("Reload Time", "Time in seconds to refill the magazine."), fpc.reloadTime, .1f, 5f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("Progress Ring", "Image used to show reload progress."));
        fpc.progressRing = (Image)EditorGUILayout.ObjectField(fpc.progressRing, typeof(Image), true);
        EditorGUILayout.EndHorizontal();
        GUI.enabled = true;

        EditorGUILayout.Space();

        #endregion

        #region Crouch

        GUILayout.Label("Crouch", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));

        fpc.enableCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Enable Crouch", "Determines if the player is allowed to crouch."), fpc.enableCrouch);

        GUI.enabled = fpc.enableCrouch;
        fpc.holdToCrouch = EditorGUILayout.ToggleLeft(new GUIContent("Hold To Crouch", "Requires the player to hold the crouch key instead if pressing to crouch and uncrouch."), fpc.holdToCrouch);
        fpc.crouchKey = (KeyCode)EditorGUILayout.EnumPopup(new GUIContent("Crouch Key", "Determines what key is used to crouch."), fpc.crouchKey);
        fpc.crouchHeight = EditorGUILayout.Slider(new GUIContent("Crouch Height", "Determines the y scale of the player object when crouched."), fpc.crouchHeight, .1f, 1);
        fpc.speedReduction = EditorGUILayout.Slider(new GUIContent("Speed Reduction", "Determines the percent 'Walk Speed' is reduced by. 1 being no reduction, and .5 being half."), fpc.speedReduction, .1f, 1);
        GUI.enabled = true;

        #endregion

        #endregion

        #region Animation Setup

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Animation Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.enableAnimations = EditorGUILayout.ToggleLeft(new GUIContent("Enable Animations", "Drives animator parameters from player movement state."), fpc.enableAnimations);

        GUI.enabled = fpc.enableAnimations;
        fpc.autoFindAnimator = EditorGUILayout.ToggleLeft(new GUIContent("Auto Find Animator", "Searches for an Animator in this object and children at runtime if one is not assigned."), fpc.autoFindAnimator);
        fpc.characterAnimator = (Animator)EditorGUILayout.ObjectField(new GUIContent("Animator", "Animator to drive. Leave empty to use Auto Find Animator."), fpc.characterAnimator, typeof(Animator), true);
        fpc.animationDampTime = EditorGUILayout.Slider(new GUIContent("Float Damping", "Smoothing time for float animation parameters."), fpc.animationDampTime, 0f, .5f);
        fpc.lookAnimationDampTime = EditorGUILayout.Slider(new GUIContent("Look Damping", "Smoothing time for LookValue. Set to 0 for immediate vertical look response."), fpc.lookAnimationDampTime, 0f, .5f);

        EditorGUILayout.Space();
        GUILayout.Label("Parameter Names", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 12 }, GUILayout.ExpandWidth(true));
        fpc.moveXParameter = EditorGUILayout.TextField(new GUIContent("Move X", "Animator float parameter for horizontal input."), fpc.moveXParameter);
        fpc.moveYParameter = EditorGUILayout.TextField(new GUIContent("Move Y", "Animator float parameter for forward input."), fpc.moveYParameter);
        fpc.speedParameter = EditorGUILayout.TextField(new GUIContent("Speed", "Animator float parameter for horizontal movement speed."), fpc.speedParameter);
        fpc.isMovingParameter = EditorGUILayout.TextField(new GUIContent("Is Moving", "Animator bool parameter for locomotion state."), fpc.isMovingParameter);
        fpc.isSprintingParameter = EditorGUILayout.TextField(new GUIContent("Is Sprinting", "Animator bool parameter for sprint state."), fpc.isSprintingParameter);
        fpc.isGroundedParameter = EditorGUILayout.TextField(new GUIContent("Is Grounded", "Animator bool parameter for grounded state."), fpc.isGroundedParameter);
        fpc.isCrouchedParameter = EditorGUILayout.TextField(new GUIContent("Is Crouched", "Animator bool parameter for crouch state."), fpc.isCrouchedParameter);
        fpc.isReloadingParameter = EditorGUILayout.TextField(new GUIContent("Is Reloading", "Animator bool parameter for reload state."), fpc.isReloadingParameter);
        fpc.lookValueParameter = EditorGUILayout.TextField(new GUIContent("Look Value", "Animator float parameter for normalized vertical look direction."), fpc.lookValueParameter);
        fpc.jumpTriggerParameter = EditorGUILayout.TextField(new GUIContent("Jump Trigger", "Animator trigger parameter fired when the player jumps."), fpc.jumpTriggerParameter);
        GUI.enabled = true;

        #endregion

        #region Head Bob

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("Head Bob Setup", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 }, GUILayout.ExpandWidth(true));
        EditorGUILayout.Space();

        fpc.enableHeadBob = EditorGUILayout.ToggleLeft(new GUIContent("Enable Head Bob", "Determines if the camera will bob while the player is walking."), fpc.enableHeadBob);

        GUI.enabled = fpc.enableHeadBob;
        fpc.joint = (Transform)EditorGUILayout.ObjectField(new GUIContent("Camera Joint", "Joint object position is moved while head bob is active."), fpc.joint, typeof(Transform), true);
        fpc.bobSpeed = EditorGUILayout.Slider(new GUIContent("Speed", "Determines how often a bob rotation is completed."), fpc.bobSpeed, 1, 20);
        fpc.bobAmount = EditorGUILayout.Vector3Field(new GUIContent("Bob Amount", "Determines the amount the joint moves in both directions on every axes."), fpc.bobAmount);
        GUI.enabled = true;

        #endregion

        if(GUI.changed)
        {
            EditorUtility.SetDirty(fpc);
            Undo.RecordObject(fpc, "FPC Change");
            SerFPC.ApplyModifiedProperties();
        }
    }

}

#endif
