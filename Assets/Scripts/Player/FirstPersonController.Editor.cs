#if UNITY_EDITOR
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
using UnityEditor;


// Custom Editor
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
        fpc.innerGoober = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Inner Goober", "The inner first-person body used for casting shadows for the local player. Automatically resolved from Body Root or found by name if left empty."), fpc.innerGoober, typeof(GameObject), true);
        fpc.outerGoober = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Outer Goober", "The outer body visible to other players. Automatically resolved from Body Root or found by name if left empty."), fpc.outerGoober, typeof(GameObject), true);
        fpc.syncOuterArmsToInner = EditorGUILayout.ToggleLeft(new GUIContent("Sync Outer Arms To Inner", "Bend the outer (third-person) arms each frame so its hands match the inner (first-person) hands the owner sees. Other players then see the gun where the shooter does."), fpc.syncOuterArmsToInner);
        GUI.enabled = fpc.syncOuterArmsToInner;
        fpc.syncOuterLeftArm = EditorGUILayout.ToggleLeft(new GUIContent("Sync Outer Left Arm", "Also match the left hand. The right (gun) hand is always matched when syncing is on."), fpc.syncOuterLeftArm);
        fpc.outerArmSyncSharpness = EditorGUILayout.Slider(new GUIContent("Outer Arm Sync Sharpness", "How fast remote copies blend toward the latest hand pose received from the owner (higher = snappier, lower = smoother between network ticks)."), fpc.outerArmSyncSharpness, 1f, 100f);
        GUI.enabled = true;

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

        fpc.flippedCardSprite = (Sprite)EditorGUILayout.ObjectField(new GUIContent("Flipped Card Sprite", "Sprite shown on a card once it has been flipped and its info revealed."), fpc.flippedCardSprite, typeof(Sprite), false);

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
        fpc.cameraCanMove = EditorGUILayout.ToggleLeft(new GUIContent("Enable Look Control", "Determines if mouse look is allowed to rotate the camera."), fpc.cameraCanMove);

        GUI.enabled = fpc.cameraCanMove;
        fpc.invertCamera = EditorGUILayout.ToggleLeft(new GUIContent("Invert Vertical Look", "Inverts the up and down camera look direction."), fpc.invertCamera);
        fpc.mouseSensitivity = EditorGUILayout.Slider(new GUIContent("Look Sensitivity", "Determines how sensitive the mouse movement is."), fpc.mouseSensitivity, .1f, 10f);
        fpc.maxLookAngle = EditorGUILayout.Slider(new GUIContent("Look Value Scale", "Mouse Y movement is divided by this value before being applied as vertical camera rotation."), fpc.maxLookAngle, 40, 90);
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
        fpc.outerShootPoint = (Transform)EditorGUILayout.ObjectField(new GUIContent("Outer Shoot Point", "Muzzle on the outer (third-person) gun. Other players see bullets start here. Auto-found by the Shoot Point's name if empty."), fpc.outerShootPoint, typeof(Transform), true);
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
        fpc.ammoDotsPerRow = EditorGUILayout.IntSlider(new GUIContent("Ammo Dots Per Row", "Ammo bar dots per row. Extra dots stack in new rows above."), fpc.ammoDotsPerRow, 1, 30);
        fpc.ammoDotSize = EditorGUILayout.Slider(new GUIContent("Ammo Dot Size", "Size in pixels of each ammo bar dot. Spacing scales with it."), fpc.ammoDotSize, 6f, 60f);
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
        fpc.lookAnimationDampTime = EditorGUILayout.Slider(new GUIContent("Look Damping", "Smoothing time for vertical camera look. Set to 0 for immediate vertical look response."), fpc.lookAnimationDampTime, 0f, .5f);

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
        fpc.shootTriggerParameter = EditorGUILayout.TextField(new GUIContent("Shoot Trigger", "Animator trigger parameter fired when the player shoots."), fpc.shootTriggerParameter);
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
