using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Persistent full-screen scene transition: a tilted orange bar with a black outline sweeps across the
/// screen to hide a scene change, then sweeps out again. After a networked load of a gameplay map it also
/// slams in a "ROUND N" banner.
///
/// Usage:
///   TransitionManager.LoadScene("MainMenu");              // local (non-networked) scene load
///   TransitionManager.CoverThen(() => ...LoadScene(...)); // cover first, run the load once fully covered
///
/// Networked loads (NetworkSceneManager) are picked up automatically: the cover starts on the Load scene
/// event and the reveal happens on LoadEventCompleted, i.e. when every client has finished loading.
/// To keep clients in step with the host, CoverThenNetworked also sends a tiny named message (on the persistent
/// NetworkManager, not on any scene object) so clients start their cover at the same time as the host, instead of
/// only when the Load event arrives. No RPCs are involved, so it is safe right before a scene load.
/// </summary>
[DefaultExecutionOrder(-9000)]
public class TransitionManager : MonoBehaviour
{
    private enum State { Hidden, Covering, Covered, Revealing }

    private const float CoverDuration = 0.35f;
    private const float RevealDuration = 0.45f;
    private const float StartupHold = 0.25f;
    private const float WatchdogSeconds = 20f;
    private const float BarWidth = 4000f;
    private const float BarHeight = 3200f;
    private const float BarTilt = -10f;
    private const float OffscreenX = 3300f;
    private const string MainMenuSceneName = "MainMenu";
    private const string CoverMessageName = "TransitionCover";

    private static readonly Color OrangeColor = new Color32(255, 140, 20, 255);
    private static readonly Color OrangeLightColor = new Color32(255, 180, 40, 255);

    public static TransitionManager Instance { get; private set; }

    private State state = State.Hidden;
    private float currentX;
    private float coveredAt;
    private Coroutine sweepRoutine;
    private Coroutine bannerRoutine;
    private readonly List<Action> onCoveredCallbacks = new List<Action>();

    private RectTransform accentBar;
    private RectTransform mainBar;
    private Text bannerText;
    private RectTransform bannerRect;
    private CanvasGroup bannerGroup;

    private bool localLoadPending;
    private bool networkLoadPending;
    private NetworkSceneManager subscribedSceneManager;
    private CustomMessagingManager subscribedMessaging;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }

        new GameObject("TransitionManager").AddComponent<TransitionManager>();
    }

    // ---------------------------------------------------------------- public API

    /// <summary>Loads a scene (non-networked) behind the transition.</summary>
    public static void LoadScene(string sceneName)
    {
        if (Instance == null)
        {
            SceneManager.LoadScene(sceneName);
            return;
        }

        Instance.localLoadPending = true;
        Instance.CoverThenInternal(() => SceneManager.LoadScene(sceneName));
    }

    /// <summary>Plays the cover and invokes <paramref name="onCovered"/> once the screen is fully covered.</summary>
    public static void CoverThen(Action onCovered)
    {
        if (Instance == null)
        {
            onCovered?.Invoke();
            return;
        }

        Instance.CoverThenInternal(onCovered);
    }

    /// <summary>
    /// Server only: tells every client to start covering now, covers locally, and invokes
    /// <paramref name="onCovered"/> once the host's screen is fully covered (use it to start the networked load).
    /// </summary>
    public static void CoverThenNetworked(Action onCovered)
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager != null && manager.IsServer && manager.CustomMessagingManager != null)
        {
            using (FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp))
            {
                manager.CustomMessagingManager.SendNamedMessageToAll(CoverMessageName, writer);
            }
        }

        if (Instance != null)
        {
            Instance.networkLoadPending = true;
        }

        CoverThen(onCovered);
    }

    /// <summary>Starts the reveal if the screen is covered. Safe to call at any time.</summary>
    public static void Reveal()
    {
        if (Instance != null)
        {
            Instance.RevealInternal(false);
        }
    }

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildUi();

        // Start covered so the very first scene (main menu) also reveals with the sweep.
        SetBarX(0f);
        state = State.Covered;
        coveredAt = Time.unscaledTime;
        StartCoroutine(RevealAfterDelay(StartupHold));
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnUnitySceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnUnitySceneLoaded;
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkScenes();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        TrackNetworkSceneManager();

        // Never leave the player staring at the cover if a load event gets lost.
        if (state == State.Covered && onCoveredCallbacks.Count == 0
            && Time.unscaledTime - coveredAt > WatchdogSeconds)
        {
            Debug.LogWarning("[Transition] Cover held too long without a load completing; revealing.", this);
            RevealInternal(false);
        }
    }

    // ---------------------------------------------------------------- scene hooks

    private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
        {
            return;
        }

        // Networked loads reveal on LoadEventCompleted instead (the server's sceneLoaded fires before clients are done).
        if (localLoadPending && !networkLoadPending)
        {
            localLoadPending = false;
            StartCoroutine(RevealAfterDelay(0.05f));
        }
    }

    private void TrackNetworkSceneManager()
    {
        NetworkManager manager = NetworkManager.Singleton;
        NetworkSceneManager current = manager != null ? manager.SceneManager : null;
        CustomMessagingManager messaging = manager != null ? manager.CustomMessagingManager : null;

        if (current == subscribedSceneManager && messaging == subscribedMessaging)
        {
            return;
        }

        UnsubscribeFromNetworkScenes();

        if (current != null)
        {
            current.OnSceneEvent += OnNetworkSceneEvent;
            subscribedSceneManager = current;
        }

        if (messaging != null)
        {
            messaging.RegisterNamedMessageHandler(CoverMessageName, OnCoverMessage);
            subscribedMessaging = messaging;
        }
    }

    private void OnCoverMessage(ulong senderClientId, FastBufferReader reader)
    {
        networkLoadPending = true;
        CoverThenInternal(null);
    }

    private void UnsubscribeFromNetworkScenes()
    {
        if (subscribedSceneManager != null)
        {
            subscribedSceneManager.OnSceneEvent -= OnNetworkSceneEvent;
        }

        subscribedSceneManager = null;

        if (subscribedMessaging != null)
        {
            try
            {
                subscribedMessaging.UnregisterNamedMessageHandler(CoverMessageName);
            }
            catch (Exception)
            {
                // The messaging manager may already be torn down during shutdown.
            }
        }

        subscribedMessaging = null;
    }

    private void OnNetworkSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                if (sceneEvent.LoadSceneMode == LoadSceneMode.Single)
                {
                    networkLoadPending = true;
                    CoverThenInternal(null);
                }
                break;

            case SceneEventType.LoadEventCompleted:
                if (sceneEvent.LoadSceneMode == LoadSceneMode.Single && networkLoadPending)
                {
                    networkLoadPending = false;
                    localLoadPending = false;
                    RevealInternal(true);
                }
                break;
        }
    }

    // ---------------------------------------------------------------- cover / reveal

    private void CoverThenInternal(Action onCovered)
    {
        if (state == State.Covered)
        {
            onCovered?.Invoke();
            return;
        }

        if (onCovered != null)
        {
            onCoveredCallbacks.Add(onCovered);
        }

        if (state == State.Covering)
        {
            return;
        }

        float fromX = state == State.Hidden ? -OffscreenX : currentX;
        state = State.Covering;
        StartSweep(fromX, 0f, CoverDuration, true);
    }

    private void RevealInternal(bool showRoundBanner)
    {
        if (state == State.Hidden || state == State.Revealing)
        {
            return;
        }

        // Callbacks that never ran (cover interrupted) are dropped; the caller's load will not happen.
        if (state == State.Covering)
        {
            onCoveredCallbacks.Clear();
        }

        state = State.Revealing;
        StartSweep(currentX, OffscreenX, RevealDuration, false);

        if (showRoundBanner && SceneManager.GetActiveScene().name != MainMenuSceneName
            && FindFirstObjectByType<FirstPersonController>() != null)
        {
            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
            }

            bannerRoutine = StartCoroutine(PlayRoundBanner(FirstPersonController.CurrentRoundNumber));
        }
    }

    private IEnumerator RevealAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (state == State.Covered)
        {
            RevealInternal(false);
        }
    }

    private void StartSweep(float fromX, float toX, float duration, bool covering)
    {
        if (sweepRoutine != null)
        {
            StopCoroutine(sweepRoutine);
        }

        mainBar.gameObject.SetActive(true);
        accentBar.gameObject.SetActive(true);
        sweepRoutine = StartCoroutine(SweepRoutine(fromX, toX, duration, covering));
    }

    private IEnumerator SweepRoutine(float fromX, float toX, float duration, bool covering)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // The light-orange accent leads the main bar when covering and trails it when revealing.
            float mainT = EaseInOut(t);
            float accentT = covering
                ? EaseInOut(Mathf.Clamp01(t * 1.18f))
                : EaseInOut(Mathf.Clamp01((t - 0.14f) / 0.86f));

            currentX = Mathf.LerpUnclamped(fromX, toX, mainT);
            mainBar.anchoredPosition = new Vector2(currentX, 0f);
            accentBar.anchoredPosition = new Vector2(Mathf.LerpUnclamped(fromX, toX, accentT), 0f);
            yield return null;
        }

        sweepRoutine = null;

        if (covering)
        {
            SetBarX(0f);
            state = State.Covered;
            coveredAt = Time.unscaledTime;

            // Callbacks may start another cover/reveal, so run a snapshot.
            Action[] callbacks = onCoveredCallbacks.ToArray();
            onCoveredCallbacks.Clear();

            foreach (Action callback in callbacks)
            {
                callback?.Invoke();
            }
        }
        else
        {
            state = State.Hidden;
            mainBar.gameObject.SetActive(false);
            accentBar.gameObject.SetActive(false);
        }
    }

    private void SetBarX(float x)
    {
        currentX = x;
        mainBar.gameObject.SetActive(true);
        accentBar.gameObject.SetActive(true);
        mainBar.anchoredPosition = new Vector2(x, 0f);
        accentBar.anchoredPosition = new Vector2(x, 0f);
    }

    private static float EaseInOut(float t)
    {
        return t * t * (3f - 2f * t);
    }

    // ---------------------------------------------------------------- round banner

    private IEnumerator PlayRoundBanner(int roundNumber)
    {
        // Let the bar clear the middle of the screen first.
        yield return new WaitForSecondsRealtime(RevealDuration * 0.35f);

        bannerText.text = "ROUND " + Mathf.Max(1, roundNumber);
        bannerGroup.alpha = 1f;

        const float slamTime = 0.16f;
        const float holdTime = 0.9f;
        const float fadeTime = 0.3f;

        for (float e = 0f; e < slamTime; e += Time.unscaledDeltaTime)
        {
            float t = e / slamTime;
            float scale = Mathf.Lerp(2.6f, 1f, 1f - (1f - t) * (1f - t));
            bannerRect.localScale = new Vector3(scale, scale, 1f);
            bannerGroup.alpha = Mathf.Clamp01(t * 3f);
            yield return null;
        }

        bannerRect.localScale = Vector3.one;
        bannerGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(holdTime);

        for (float e = 0f; e < fadeTime; e += Time.unscaledDeltaTime)
        {
            bannerGroup.alpha = 1f - e / fadeTime;
            yield return null;
        }

        bannerGroup.alpha = 0f;
        bannerRoutine = null;
    }

    // ---------------------------------------------------------------- ui construction

    private void BuildUi()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Accent first so the main bar draws over it.
        accentBar = CreateBar("AccentBar", OrangeLightColor, Color.black, 0f);
        mainBar = CreateBar("MainBar", OrangeColor, Color.black, 28f);

        BuildBanner();
    }

    private RectTransform CreateBar(string objectName, Color fill, Color outline, float outlineThickness)
    {
        RectTransform bar = CreateRect(objectName, transform);
        bar.sizeDelta = new Vector2(BarWidth, BarHeight);
        bar.localRotation = Quaternion.Euler(0f, 0f, BarTilt);

        if (outlineThickness > 0f)
        {
            // Black border drawn as a full-size black rect with the orange inset on the vertical edges.
            Image border = bar.gameObject.AddComponent<Image>();
            border.color = outline;
            border.raycastTarget = false;

            RectTransform inner = CreateRect("Fill", bar);
            inner.anchorMin = Vector2.zero;
            inner.anchorMax = Vector2.one;
            inner.offsetMin = new Vector2(outlineThickness, 0f);
            inner.offsetMax = new Vector2(-outlineThickness, 0f);
            Image fillImage = inner.gameObject.AddComponent<Image>();
            fillImage.color = fill;
            fillImage.raycastTarget = false;
        }
        else
        {
            Image image = bar.gameObject.AddComponent<Image>();
            image.color = fill;
            image.raycastTarget = false;
        }

        return bar;
    }

    private void BuildBanner()
    {
        bannerRect = CreateRect("RoundBanner", transform);
        bannerRect.sizeDelta = new Vector2(1400f, 300f);
        bannerRect.localRotation = Quaternion.Euler(0f, 0f, -3f);
        bannerGroup = bannerRect.gameObject.AddComponent<CanvasGroup>();
        bannerGroup.alpha = 0f;
        bannerGroup.interactable = false;
        bannerGroup.blocksRaycasts = false;

        Font font = Resources.Load<Font>("Transitions/TransitionFont");

        if (font == null)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        bannerText = bannerRect.gameObject.AddComponent<Text>();
        bannerText.font = font;
        bannerText.fontSize = 190;
        bannerText.alignment = TextAnchor.MiddleCenter;
        bannerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        bannerText.verticalOverflow = VerticalWrapMode.Overflow;
        bannerText.color = OrangeColor;
        bannerText.raycastTarget = false;

        UnityEngine.UI.Outline outline = bannerRect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(8f, -8f);
        outline.useGraphicAlpha = true;

        Shadow shadow = bannerRect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(0f, -16f);
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        return rect;
    }
}
