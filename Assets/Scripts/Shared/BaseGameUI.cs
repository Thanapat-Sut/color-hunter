using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System;
using System.Collections;

public abstract class BaseGameUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] protected GameObject setupPanel;
    [SerializeField] protected GameObject gameplayPanel;
    [SerializeField] protected GameObject gameOverPanel;

    [Header("Countdown")]
    [SerializeField] protected GameObject countdownPanel;
    [SerializeField] protected TextMeshProUGUI countdownText;

    [Header("Setup Video Sequence")]
    [Tooltip("RawImage that displays the setup intro + loop video.")]
    [SerializeField] protected RawImage setupVideoDisplay;
    [Tooltip("Plays once when entering Setup.")]
    [SerializeField] protected VideoClip setupStartClip;
    [Tooltip("Loops after the start clip finishes; how-to-play pops in once this begins.")]
    [SerializeField] protected VideoClip setupLoopClip;
    [Tooltip("Optional layer over the setup video, hidden during the start clip and shown once the loop begins (TGS 2026: portal + new character over the old one).")]
    [SerializeField] protected GameObject setupLoopOverlay;
    [Tooltip("How-To-Play panel shown between loop start and Start button.")]
    [SerializeField] protected GameObject howToPlayPanel;
    [Tooltip("Start-button GameObject (popped in after delays). Use the root that should pop — usually the image-bar parent of the Button. The actual Button is auto-discovered from its children.")]
    [SerializeField] protected GameObject setupStartButton;
    [Tooltip("Delay after loop starts before the How-To-Play panel pops in.")]
    [SerializeField] protected float setupHowToPlayDelay = 3f;
    [Tooltip("Delay after How-To-Play panel pops in before the Start button pops in.")]
    [SerializeField] protected float setupRevealDelay = 3f;
    [Tooltip("Delay after the loop starts before PregameVoice fires. 0 = play immediately when loop begins.")]
    [SerializeField] protected float pregameVoiceDelay = 0f;
    [Tooltip("Voice clip name played during setup. Leave blank to disable.")]
    [SerializeField] protected string pregameVoiceName = "PregameVoice";
    [Tooltip("Duration of the start-button pop-in animation.")]
    [SerializeField] protected float setupRevealPopDuration = 0.45f;
    [Tooltip("Duration of the How-To-Play panel pop-in animation.")]
    [SerializeField] protected float setupHowToPlayPopDuration = 0.45f;

    private VideoPlayer _vpSetup;
    bool _setupStartClipPlaying;

    /// <summary>Seconds into the Setup start clip by the frame reached; 0 while it loads; -1 when it isn't playing.</summary>
    public double SetupStartClipTime
    {
        get
        {
            if (!_setupStartClipPlaying || _vpSetup == null) return -1;
            if (!_vpSetup.isPrepared || _vpSetup.frame < 0 || _vpSetup.frameRate <= 0f) return 0;
            return _vpSetup.frame / (double)_vpSetup.frameRate;
        }
    }
    private RenderTexture _rtSetup;
    private Coroutine _setupSeqCo;
    private Coroutine _pregameVoiceCo;
    private bool _setupSequenceComplete;
    private Vector3 _startButtonOrigScale;
    private bool _startButtonOrigCaptured;
    private Vector2 _howToPlayOrigAnchoredPos;
    private bool _howToPlayOrigCaptured;

    protected bool SetupSequenceComplete => _setupSequenceComplete;
    protected GameObject SetupStartButton => setupStartButton;
    // Auto-discovers the Button component anywhere inside setupStartButton — handles the case where
    // the popped-in root is an image-bar parent and the actual Button sits underneath.
    protected Button SetupStartButtonComponent =>
        setupStartButton != null ? setupStartButton.GetComponentInChildren<Button>(true) : null;

    // UI and LED routes keep their own state/authority gates. This only restores
    // pointer delivery to an explicitly bound control, never to the entire HUD.
    protected static void BindTouchButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null || action == null) return;
        var graphic = button.targetGraphic;
        if (graphic == null)
        {
            graphic = button.GetComponent<Graphic>();
            if (graphic == null) graphic = button.GetComponentInChildren<Graphic>(true);
            if (graphic != null) button.targetGraphic = graphic;
        }
        if (graphic != null) graphic.raycastTarget = true;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    protected static Button EnsureTouchButton(GameObject target,
        UnityEngine.Events.UnityAction action, bool allowThroughHud = false)
    {
        if (target == null || action == null) return null;
        var button = target.GetComponent<Button>();
        if (button == null)
        {
            button = target.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
        }
        if (button.targetGraphic == null)
        {
            var graphic = target.GetComponent<Graphic>();
            if (graphic == null) graphic = target.GetComponentInChildren<Graphic>(true);
            if (graphic == null)
            {
                var hitArea = target.AddComponent<Image>();
                hitArea.color = Color.clear;
                graphic = hitArea;
            }
            button.targetGraphic = graphic;
        }
        if (allowThroughHud)
        {
            var group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.AddComponent<CanvasGroup>();
            group.blocksRaycasts = true;
            group.interactable = true;
            group.ignoreParentGroups = true;
        }
        BindTouchButton(button, action);
        return button;
    }

    protected bool IsAppStateTransitioning => _transitionCo != null;

    // Tablet RESTART (Pilot 2026-11-04): play again straight into Ready / gameplay. Under one fade the
    // game resets through its Setup state and starts at once; the Start page (intro video, pregame
    // voice, START reveal) is skipped while _quickRestart is set.
    bool _quickRestart;
    protected bool IsQuickRestarting => _quickRestart;

    protected void BeginQuickRestart(Action resetToSetup, Action startGame)
    {
        BeginAppStateTransition(() =>
        {
            _quickRestart = true;
            try
            {
                resetToSetup?.Invoke();
                startGame?.Invoke();
            }
            finally { _quickRestart = false; }
        });
    }

    // Override per-game to call Manager.RevealStartButton() — paints the LED start tile when the UI button pops in.
    protected virtual void OnSetupRevealStartButton() { }

    [Header("Transition Fade")]
    [Tooltip("Full-screen Image used as the white cover for screen transitions. Keep it on top of all panels; leave inactive by default.")]
    [SerializeField] protected Image transitionFadeOverlay;
    [Tooltip("Color the cover fades to during a transition (white = blank flash).")]
    [SerializeField] protected Color transitionFadeColor = Color.white;
    [Tooltip("Seconds to fade the cover IN over the current screen.")]
    [SerializeField] protected float transitionFadeOutDuration = 0.35f;
    [Tooltip("Seconds to fade the cover OUT, revealing the new screen.")]
    [SerializeField] protected float transitionFadeInDuration = 0.35f;
    [Tooltip("Beat to hold on the full cover while panels swap underneath.")]
    [SerializeField] protected float transitionHoldDuration = 0.05f;

    protected Coroutine _transitionCo;


    const float ScalePopDuration = 0.25f;
    const float ScalePopStart = 1.3f;
    const float StatusHoldTime = 1.5f;
    const float StatusFadeDuration = 0.5f;
    const float PulseSpeed = 2f;
    const float PulseScaleMax = 1.15f;
    const float TitlePopDuration = 0.4f;
    const float TitlePopStart = 1.5f;
    const float CountdownInterval = 1f;
    const float GoDisplayTime = 0.5f;

    const float CdPunchScale = 2.5f;
    const float CdPunchDuration = 0.3f;
    const float CdBounceOvershoot = 0.15f;
    const float CdShrinkStart = 0.75f;
    const float CdRotationShake = 8f;
    const float CdGoPunchScale = 3f;

    protected Coroutine _statusPopCoroutine;
    protected Coroutine _countdownAnimCoroutine;
    protected bool _timerPulsing;

    // Auto-init the shared transition overlay so every subclass can call
    // BeginAppStateTransition / BeginGameOverFade without needing its own Start-time
    // ResetTransitionOverlay call. Subclasses that add Awake must invoke base.Awake().
    protected virtual void Awake()
    {
        ResetTransitionOverlay();
    }

    protected virtual void HideAllPanels()
    {
        if (setupPanel != null) setupPanel.SetActive(false);
        if (gameplayPanel != null) gameplayPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    protected void ShowPanel(GameObject panel)
    {
        if (panel == null) return;
        panel.SetActive(true);
        if (panel == gameplayPanel) EnsureNoBlockRaycasts(panel);
    }

    protected void DisableHUDRaycast() => EnsureNoBlockRaycasts(gameplayPanel);

    // make sure grid touches pass through the UI overlay
    void EnsureNoBlockRaycasts(GameObject panel)
    {
        if (panel == null) return;
        var cg = panel.GetComponent<CanvasGroup>();
        if (cg == null) cg = panel.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
    }

    protected void ShowStatusPop(TextMeshProUGUI statusText, string text, Color color)
    {
        if (statusText == null) return;
        statusText.text = text;
        statusText.color = color;
        statusText.transform.localScale = Vector3.one;

        if (_statusPopCoroutine != null) StopCoroutine(_statusPopCoroutine);
        _statusPopCoroutine = StartCoroutine(StatusPopRoutine(statusText));
    }

    IEnumerator StatusPopRoutine(TextMeshProUGUI txt)
    {
        if (txt == null) yield break;
        var t = txt.transform;
        var c = txt.color;

        // scale down pop
        float elapsed = 0f;
        while (elapsed < ScalePopDuration) {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(ScalePopStart, 1f, elapsed / ScalePopDuration);
            t.localScale = Vector3.one * s;
            yield return null;
        }
        t.localScale = Vector3.one;

        yield return new WaitForSeconds(StatusHoldTime);

        // fade out
        elapsed = 0f;
        while (elapsed < StatusFadeDuration) {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, elapsed / StatusFadeDuration);
            txt.color = new Color(c.r, c.g, c.b, a);
            yield return null;
        }
        txt.color = new Color(c.r, c.g, c.b, 0f);
        _statusPopCoroutine = null;
    }

    protected IEnumerator TimerPulseRoutine(TextMeshProUGUI timerText)
    {
        while (_timerPulsing && timerText != null)
        {
            float t = Mathf.PingPong(Time.time * PulseSpeed, 1f);
            float scale = Mathf.Lerp(1f, PulseScaleMax, t);
            timerText.transform.localScale = Vector3.one * scale;
            yield return null;
        }
        if (timerText != null) timerText.transform.localScale = Vector3.one;
    }

    protected void UpdateTimerColor(TextMeshProUGUI timerText, float timeLeft, float redAt = 10f, float yellowAt = 30f)
    {
        if (timerText == null) return;
        if (timeLeft <= redAt) timerText.color = Color.red;
        else if (timeLeft <= yellowAt) timerText.color = new Color(1f, 0.5f, 0f, 1f);
        else timerText.color = Color.black;
    }

    // White → Yellow → Red palette. Use when the HUD sits on a dark background where black is invisible.
    protected void UpdateTimerColorWYR(TextMeshProUGUI timerText, float timeLeft, float redAt = 10f, float yellowAt = 30f)
    {
        if (timerText == null) return;
        if (timeLeft <= redAt) timerText.color = Color.red;
        else if (timeLeft <= yellowAt) timerText.color = Color.yellow;
        else timerText.color = Color.white;
    }

    // count up score from 0 to final
    protected IEnumerator ScoreCountUpRoutine(TextMeshProUGUI tmp, string prefix, int finalValue, float duration = 1.5f, string suffix = "", string format = "")
    {
        if (tmp == null) yield break;
        tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, 1f);
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            int shown = Mathf.RoundToInt(Mathf.Lerp(0, finalValue, t));
            tmp.text = $"{prefix}{shown.ToString(format)}{suffix}";
            yield return null;
        }
        tmp.text = $"{prefix}{finalValue.ToString(format)}{suffix}";
    }

    // fade in a TMP line
    protected IEnumerator FadeInLineRoutine(TextMeshProUGUI tmp, string text, Color targetColor, float duration = 0.3f)
    {
        if (tmp == null) yield break;
        tmp.text = text;
        tmp.color = new Color(targetColor.r, targetColor.g, targetColor.b, 0f);

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float a = Mathf.Clamp01(elapsed / duration);
            tmp.color = new Color(targetColor.r, targetColor.g, targetColor.b, a);
            yield return null;
        }
        tmp.color = targetColor;
    }

    protected IEnumerator FadeInImageRoutine(Image img, Color targetColor, float duration = 0.3f)
    {
        if (img == null) yield break;
        img.color = new Color(targetColor.r, targetColor.g, targetColor.b, 0f);

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float a = Mathf.Clamp01(elapsed / duration);
            img.color = new Color(targetColor.r, targetColor.g, targetColor.b, a);
            yield return null;
        }
        img.color = targetColor;
    }

    // title pop: big -> normal scale
    protected IEnumerator TitlePopRoutine(TextMeshProUGUI tmp)
    {
        if (tmp == null) yield break;
        tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, 1f);
        float elapsed = 0f;
        while (elapsed < TitlePopDuration) {
            elapsed += Time.deltaTime;
            float s = Mathf.Lerp(TitlePopStart, 1f, elapsed / TitlePopDuration);
            tmp.transform.localScale = Vector3.one * s;
            yield return null;
        }
        tmp.transform.localScale = Vector3.one;
    }

    protected void HideTMP(TextMeshProUGUI tmp)
    {
        if (tmp != null) tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, 0f);
    }

    protected void HideImage(Image img)
    {
        if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, 0f);
    }

    // countdown with TMP text
    protected IEnumerator CountdownRoutine(TextMeshProUGUI text)
    {
        if (text == null) yield break;
        if (setupPanel != null) setupPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        ShowPanel(gameplayPanel);
        ShowCountdownPanel(true);

        for (int i = 3; i > 0; i--) {
            text.text = i.ToString();
            text.color = Color.white;
            PlayCountdownDigitAnim(i.ToString(), Color.white);
            yield return new WaitForSeconds(CountdownInterval);
        }
        text.text = "GO!";
        text.color = Color.green;
        PlayCountdownGoAnim();
        yield return new WaitForSeconds(GoDisplayTime);
        ShowCountdownPanel(false);
    }

    // countdown with grid digits only (no TMP)
    protected IEnumerator CountdownRoutine(
        System.Action<int, Color> onRenderDigit, System.Action onClearGrid, Color digitColor)
    {
        yield return CountdownRoutine(onRenderDigit, null, onClearGrid, digitColor);
    }

    // countdown with grid digits + GO
    protected IEnumerator CountdownRoutine(
        System.Action<int, Color> onRenderDigit, System.Action<Color> onRenderGo,
        System.Action onClearGrid, Color digitColor)
    {
        if (setupPanel != null) setupPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        ShowPanel(gameplayPanel);
        ShowCountdownPanel(true);

        AudioManager.Instance?.PlaySFX("CountdownSFX");
        for (int i = 3; i > 0; i--) {
            onRenderDigit?.Invoke(i, digitColor);
            PlayCountdownDigitAnim(i.ToString(), Color.white);
            yield return new WaitForSeconds(CountdownInterval);
        }

        PlayCountdownGoAnim();

        if (onRenderGo != null) {
            onRenderGo.Invoke(Color.green);   // LED palette green #00FF00
            yield return new WaitForSeconds(GoDisplayTime);
            onClearGrid?.Invoke();
        } else {
            onClearGrid?.Invoke();
            yield return new WaitForSeconds(GoDisplayTime);
        }

        ShowCountdownPanel(false);
    }

    // --- countdown animation helpers ---

    protected void ShowCountdownPanel(bool show)
    {
        if (countdownPanel != null)
        {
            countdownPanel.SetActive(show);
            if (show) countdownPanel.transform.SetAsLastSibling();
        }
        if (show && countdownText != null)
        {

            countdownText.fontSize = 348;
            countdownText.transform.localScale = Vector3.one;
            countdownText.transform.localRotation = Quaternion.identity;
        }
        if (!show) StopCountdownAnim();
    }

    protected void PlayCountdownDigitAnim(string text, Color color)
    {
        if (countdownText == null) return;
        StopCountdownAnim();
        countdownText.text = text;
        countdownText.fontSize = 348;
        _countdownAnimCoroutine = StartCoroutine(CountdownDigitAnimRoutine(color));
    }

    protected void PlayCountdownGoAnim()
    {
        if (countdownText == null) return;
        StopCountdownAnim();
        countdownText.text = "GO!";
        countdownText.fontSize = 348;
        _countdownAnimCoroutine = StartCoroutine(CountdownGoAnimRoutine());
    }

    void StopCountdownAnim()
    {
        if (_countdownAnimCoroutine != null)
        {
            StopCoroutine(_countdownAnimCoroutine);
            _countdownAnimCoroutine = null;
        }
        if (countdownText != null)
        {
            countdownText.transform.localScale = Vector3.one;
            countdownText.transform.localRotation = Quaternion.identity;
        }
    }

    // digit: punch in big → elastic bounce to 1x → hold → shrink + fade out
    IEnumerator CountdownDigitAnimRoutine(Color color)
    {
        var t = countdownText.transform;
        countdownText.color = color;

        // phase 1: punch in with elastic bounce (0 → CdPunchDuration)
        float elapsed = 0f;
        while (elapsed < CdPunchDuration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / CdPunchDuration);
            float scale = Mathf.Lerp(CdPunchScale, 1f, ElasticOut(p));
            float rot = Mathf.Sin(p * Mathf.PI * 3f) * CdRotationShake * (1f - p);
            t.localScale = Vector3.one * scale;
            t.localRotation = Quaternion.Euler(0f, 0f, rot);
            yield return null;
        }
        t.localScale = Vector3.one;
        t.localRotation = Quaternion.identity;

        // phase 2: hold with subtle pulse
        float holdEnd = CdShrinkStart - CdPunchDuration;
        elapsed = 0f;
        while (elapsed < holdEnd)
        {
            elapsed += Time.deltaTime;
            float pulse = 1f + Mathf.Sin(elapsed * Mathf.PI * 4f) * 0.05f;
            t.localScale = Vector3.one * pulse;
            yield return null;
        }

        // phase 3: shrink + fade out (CdShrinkStart → 1.0)
        float shrinkDuration = CountdownInterval - CdShrinkStart;
        elapsed = 0f;
        while (elapsed < shrinkDuration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / shrinkDuration);
            float scale = Mathf.Lerp(1f, 0.4f, p * p);
            float alpha = Mathf.Lerp(1f, 0f, p * p);
            t.localScale = Vector3.one * scale;
            countdownText.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }
        _countdownAnimCoroutine = null;
    }

    // GO!: big punch in → color flash yellow/green → hold at scale
    IEnumerator CountdownGoAnimRoutine()
    {
        var t = countdownText.transform;
        var green = Color.green;
        var yellow = Color.yellow;

        // punch in
        float elapsed = 0f;
        float punchDur = CdPunchDuration * 0.8f;
        while (elapsed < punchDur)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / punchDur);
            float scale = Mathf.Lerp(CdGoPunchScale, 1f, ElasticOut(p));
            float rot = Mathf.Sin(p * Mathf.PI * 4f) * CdRotationShake * 1.5f * (1f - p);
            t.localScale = Vector3.one * scale;
            t.localRotation = Quaternion.Euler(0f, 0f, rot);
            countdownText.color = Color.Lerp(yellow, green, p);
            yield return null;
        }
        t.localScale = Vector3.one;
        t.localRotation = Quaternion.identity;
        countdownText.color = green;

        // energetic pulse until panel hides
        elapsed = 0f;
        while (elapsed < GoDisplayTime)
        {
            elapsed += Time.deltaTime;
            float pulse = 1f + Mathf.Sin(elapsed * Mathf.PI * 8f) * 0.08f;
            t.localScale = Vector3.one * pulse;
            yield return null;
        }
        _countdownAnimCoroutine = null;
    }

    // elastic ease-out: overshoot then settle
    static float ElasticOut(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        float p = 0.3f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t - p / 4f) * (2f * Mathf.PI) / p) + 1f;
    }

    // --- White-fade screen transitions (shared across all games) ---

    // Call from Start() to make sure the overlay is hidden + non-blocking on scene load.
    protected void ResetTransitionOverlay()
    {
        if (transitionFadeOverlay == null) return;
        var c = transitionFadeColor;
        transitionFadeOverlay.color = new Color(c.r, c.g, c.b, 0f);
        transitionFadeOverlay.raycastTarget = false;
        transitionFadeOverlay.gameObject.SetActive(false);
    }

    // Wrap any state change (Setup→Selection, →Playing, →Setup, etc.) in a
    // fade: fade-in white → call atMidpoint → hold → fade-out. Guarded so
    // concurrent triggers no-op rather than overlap.
    protected void BeginAppStateTransition(Action atMidpoint)
    {
        if (_transitionCo != null) return;
        AudioManager.Instance?.PlaySFX("ClickSFX");
        _transitionCo = StartCoroutine(AppStateTransitionRoutine(atMidpoint));
    }

    IEnumerator AppStateTransitionRoutine(Action atMidpoint)
    {
        yield return FadeOverlayRoutine(0f, 1f, transitionFadeOutDuration);
        atMidpoint?.Invoke();
        if (transitionHoldDuration > 0f) yield return new WaitForSeconds(transitionHoldDuration);
        yield return FadeOverlayRoutine(1f, 0f, transitionFadeInDuration);
        _transitionCo = null;
    }

    protected IEnumerator FadeOverlayRoutine(float fromA, float toA, float duration)
    {
        if (transitionFadeOverlay == null) yield break;

        var c = transitionFadeColor;
        transitionFadeOverlay.gameObject.SetActive(true);
        transitionFadeOverlay.transform.SetAsLastSibling();
        transitionFadeOverlay.raycastTarget = true;
        transitionFadeOverlay.color = new Color(c.r, c.g, c.b, fromA);

        if (duration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float a = Mathf.Lerp(fromA, toA, Mathf.Clamp01(elapsed / duration));
                transitionFadeOverlay.color = new Color(c.r, c.g, c.b, a);
                yield return null;
            }
        }
        transitionFadeOverlay.color = new Color(c.r, c.g, c.b, toA);

        if (toA <= 0f)
        {
            transitionFadeOverlay.raycastTarget = false;
            transitionFadeOverlay.gameObject.SetActive(false);
        }
    }

    // GameOver-entry helper for the common case where the game auto-fires
    // SetAppState(GameOver). Subclasses call this from their HandleStateChanged
    // GameOver branch, passing the panel-swap action that should run mid-cover.
    protected void BeginGameOverFade(Action onPanelSwap)
    {
        if (_transitionCo != null) { StopCoroutine(_transitionCo); _transitionCo = null; }
        _transitionCo = StartCoroutine(GameOverFadeRoutine(onPanelSwap));
    }

    IEnumerator GameOverFadeRoutine(Action onPanelSwap)
    {
        yield return FadeOverlayRoutine(0f, 1f, transitionFadeOutDuration);
        onPanelSwap?.Invoke();
        if (transitionHoldDuration > 0f) yield return new WaitForSeconds(transitionHoldDuration);
        yield return FadeOverlayRoutine(1f, 0f, transitionFadeInDuration);
        _transitionCo = null;
    }

    // --- Setup video sequence (shared) ---

    protected void SetupVideoPlayers()
    {
        // Setup-only channel — uses setupStartClip as the sizing reference (loop clip usually matches).
        var setupSizingClip = setupStartClip != null ? setupStartClip : setupLoopClip;
        _vpSetup = MakeChannel(setupVideoDisplay, setupSizingClip, ref _rtSetup);

        // Don't let Unity skip frames to keep up with wall-clock — if the
        // Editor can't render at 60fps the clip would otherwise advance time
        // beyond what's actually drawn, then the wait exits early.
        if (_vpSetup != null) _vpSetup.skipOnDrop = false;
    }

    VideoPlayer MakeChannel(RawImage display, VideoClip refClip, ref RenderTexture rt)
    {
        if (display == null || refClip == null) return null;

        var vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.source = VideoSource.VideoClip;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.isLooping = false;
        // Kill the audio path. Default audioOutputMode=Direct silent-fails on
        // a UI GameObject (no AudioListener) and pushes the video's time
        // reference into a race with DSP clock — video plays several times
        // its native rate. None + 0 tracks pins time to the internal clock.
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.controlledAudioTrackCount = 0;
        // DSPTime (default) advances by audio engine sample-rate and drifts
        // when no audio source anchors it. GameTime locks playback to
        // Time.deltaTime so the clip matches Editor preview pacing.
        vp.timeUpdateMode = VideoTimeUpdateMode.GameTime;

        rt = new RenderTexture((int)refClip.width, (int)refClip.height, 0);
        vp.targetTexture = rt;
        display.texture = rt;
        display.enabled = false;
        return vp;
    }

    void WireEnd(VideoPlayer vp, RawImage display)
    {
        if (vp == null) return;
        vp.loopPointReached += _ =>
        {
            if (vp.isLooping) return;
            if (display != null) display.enabled = false;
        };
    }

    void PlayOn(VideoPlayer vp, RawImage display, VideoClip clip, bool loop = false)
    {
        if (vp == null || display == null || clip == null) return;
        vp.Stop();
        vp.clip = clip;
        vp.time = 0;
        vp.isLooping = loop;
        display.enabled = true;
        vp.Play();
    }

    void StopChannel(VideoPlayer vp, RawImage display)
    {
        if (vp) vp.Stop();
        if (display) display.enabled = false;
    }

    protected void CleanupVideo()
    {
        if (_vpSetup != null) Destroy(_vpSetup);
        if (_rtSetup != null) { _rtSetup.Release(); Destroy(_rtSetup); }
        _vpSetup = null; _rtSetup = null;
    }

    protected void BeginSetupSequence()
    {
        _setupSequenceComplete = false;
        if (_quickRestart)
        {
            // passing through Setup on the way back into the game: no intro, no voice
            StopSetupSequence();
            _setupSequenceComplete = true;
            return;
        }

        if (setupStartButton != null && !_startButtonOrigCaptured)
        {
            _startButtonOrigScale = setupStartButton.transform.localScale;
            if (_startButtonOrigScale == Vector3.zero) _startButtonOrigScale = Vector3.one;
            _startButtonOrigCaptured = true;
        }
        if (setupStartButton != null) setupStartButton.SetActive(false);

        if (howToPlayPanel != null && !_howToPlayOrigCaptured)
        {
            var rt = howToPlayPanel.transform as RectTransform;
            if (rt != null)
            {
                _howToPlayOrigAnchoredPos = rt.anchoredPosition;
                _howToPlayOrigCaptured = true;
            }
        }
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);

        StopSetupSequence();

        // PregameVoice fires on setup entry regardless of whether the setup video is wired —
        // the fast-path (no video) skipped SetupSequenceRoutine entirely and dropped the voice.
        if (!string.IsNullOrEmpty(pregameVoiceName))
        {
            if (_pregameVoiceCo != null) StopCoroutine(_pregameVoiceCo);
            _pregameVoiceCo = StartCoroutine(PregameVoiceRoutine());
        }

        // Only fall to instant-reveal when there's literally nothing to time in.
        // If the how-to-play panel or start button is wired, keep the timed sequence
        // so the slide-in and pop still fire — SetupSequenceRoutine gates every
        // video-play block on non-null clips so it's safe to run without video.
        // (Pilot 2026-07-10: DungeonRace has no intro/loop clips but needs popups.)
        bool hasSetupVisuals = setupVideoDisplay != null && (setupStartClip != null || setupLoopClip != null);
        bool hasPopups = howToPlayPanel != null || setupStartButton != null;
        if (!hasSetupVisuals && !hasPopups)
        {
            RevealStartButtonInstant();
            return;
        }
        _setupSeqCo = StartCoroutine(SetupSequenceRoutine());
    }

    protected void StopSetupSequence()
    {
        if (_setupSeqCo != null) { StopCoroutine(_setupSeqCo); _setupSeqCo = null; }
        if (_pregameVoiceCo != null) { StopCoroutine(_pregameVoiceCo); _pregameVoiceCo = null; }
        _setupStartClipPlaying = false;
        StopChannel(_vpSetup, setupVideoDisplay);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        if (setupLoopOverlay != null) setupLoopOverlay.SetActive(false);
    }

    IEnumerator SetupSequenceRoutine()
    {
        // PregameVoice is kicked off by BeginSetupSequence (fires regardless of video wiring)
        // so it runs even when the fast-path skips this routine.

        // 1) Start clip (once) — wait via frame-progress, not isPlaying (cold load can race).
        if (setupStartClip != null && _vpSetup != null)
        {
            _setupStartClipPlaying = true;
            PlayOn(_vpSetup, setupVideoDisplay, setupStartClip, loop: false);
            yield return WaitForVideoEnd(_vpSetup);
            _setupStartClipPlaying = false;
        }

        // 2) Loop clip (kept playing forever on this state)
        if (setupLoopClip != null && _vpSetup != null)
            PlayOn(_vpSetup, setupVideoDisplay, setupLoopClip, loop: true);
        if (setupLoopOverlay != null) setupLoopOverlay.SetActive(true);

        // 3) Beat → slide the How-To-Play panel in from the left
        if (setupHowToPlayDelay > 0f) yield return new WaitForSeconds(setupHowToPlayDelay);
        RevealHowToPlaySlideIn();

        // 4) Beat → pop the Start button (UI + tile)
        if (setupRevealDelay > 0f) yield return new WaitForSeconds(setupRevealDelay);
        RevealStartButtonWithPop();
    }

    IEnumerator PregameVoiceRoutine()
    {
        if (pregameVoiceDelay > 0f) yield return new WaitForSeconds(pregameVoiceDelay);
        PlayPregameVoiceClip(pregameVoiceName);
        _pregameVoiceCo = null;
    }

    // Virtual hook so dual-lane MP subclasses can route the pregame narrator
    // through the correct voice source per PC (Source A for LEFT, Source B for
    // RIGHT) instead of always firing Source A. Default matches the legacy behavior.
    protected virtual void PlayPregameVoiceClip(string name)
    {
        var am = AudioManager.Instance;
        if (am != null) am.PlayVoice(name);
    }

    IEnumerator WaitForVideoEnd(VideoPlayer vp)
    {
        if (vp == null) yield break;

        // Wait for async Prepare to land. Polling isPlaying short-timeouts was
        // racing — cold load can take 6+s and the wait bailed before play started.
        while (vp != null && !vp.isPrepared) yield return null;
        if (vp == null) yield break;

        // Frame-based wait. vp.time advances on wall-clock even when the GPU
        // can't paint frames at clip rate — relying on it caused the wait to
        // exit at "done" while the user had only seen half the clip.
        double clipLength = vp.clip != null ? vp.clip.length : (double)vp.length;
        if (clipLength <= 0.0 || vp.frameCount == 0) yield break;
        long lastTargetFrame = (long)vp.frameCount - 1;
        float deadline = Time.unscaledTime + (float)(clipLength * 5.0 + 5.0);
        bool playbackBegan = false;
        while (vp != null && vp.isPrepared)
        {
            if (vp.isPlaying) playbackBegan = true;
            if (vp.frame >= lastTargetFrame) break;
            if (playbackBegan && !vp.isPlaying) break;
            if (Time.unscaledTime > deadline) break;
            yield return null;
        }
    }

    void RevealStartButtonInstant()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(true);
            var rt = howToPlayPanel.transform as RectTransform;
            if (rt != null && _howToPlayOrigCaptured) rt.anchoredPosition = _howToPlayOrigAnchoredPos;
        }
        if (setupStartButton != null)
        {
            setupStartButton.SetActive(true);
            setupStartButton.transform.localScale = _startButtonOrigScale == Vector3.zero ? Vector3.one : _startButtonOrigScale;
        }
        OnSetupRevealStartButton();
        _setupSequenceComplete = true;
    }

    void RevealHowToPlaySlideIn()
    {
        if (howToPlayPanel == null) return;
        var rt = howToPlayPanel.transform as RectTransform;
        if (rt == null) return;
        if (!_howToPlayOrigCaptured)
        {
            _howToPlayOrigAnchoredPos = rt.anchoredPosition;
            _howToPlayOrigCaptured = true;
        }
        howToPlayPanel.SetActive(true);
        StartCoroutine(SlideInFromLeftRoutine(rt, _howToPlayOrigAnchoredPos, setupHowToPlayPopDuration));
    }

    IEnumerator SlideInFromLeftRoutine(RectTransform rt, Vector2 target, float duration)
    {
        if (rt == null) yield break;
        if (duration <= 0f) { rt.anchoredPosition = target; yield break; }

        // Push fully off the left edge — use the parent canvas width as the safe offset.
        float canvasWidth = 1920f;
        var canvas = rt.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var canvasRT = canvas.transform as RectTransform;
            if (canvasRT != null) canvasWidth = canvasRT.rect.width;
        }
        Vector2 start = new Vector2(target.x - canvasWidth, target.y);
        rt.anchoredPosition = start;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            // Ease-out cubic — fast in, soft landing.
            float k = 1f - Mathf.Pow(1f - u, 3f);
            rt.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
            yield return null;
        }
        rt.anchoredPosition = target;
    }

    void RevealStartButtonWithPop()
    {
        OnSetupRevealStartButton();
        _setupSequenceComplete = true;
        if (setupStartButton == null) return;
        setupStartButton.SetActive(true);
        StartCoroutine(PopRoutine(setupStartButton.transform, _startButtonOrigScale, setupRevealPopDuration));
    }

    IEnumerator PopRoutine(Transform t, Vector3 target, float duration)
    {
        if (t == null || duration <= 0f) { if (t != null) t.localScale = target; yield break; }
        Vector3 start = Vector3.zero;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            // Elastic-out so the button has a little overshoot.
            float k = 1f - Mathf.Pow(2f, -10f * u) * Mathf.Cos((u * 10f - 0.75f) * (2f * Mathf.PI / 3f));
            t.localScale = Vector3.LerpUnclamped(start, target, k);
            yield return null;
        }
        t.localScale = target;
    }
}
