using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections;

public enum VideoState
{
    Welcome,
    Target,
    ReactGood,
    ReactNo,
    HypeCombo,
    WarningTime,
    ResultLess40,
    Result40_79,
    Result80_100
}

[System.Serializable]
public class VideoClipEntry
{
    public VideoState state;
    public VideoClip clip;
}

public class ChromaHuntUI : BaseGameUI
{
    [Header("HUD")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private TextMeshProUGUI streakText;
    [SerializeField] private TextMeshProUGUI feedbackText;

    [Header("Target Display")]
    [SerializeField] private Image characterImage;
    [Tooltip("Root GameObject containing one or more TMP labels — all are updated to show the same color name + color.")]
    [SerializeField] private GameObject colorNameTextRoot;
    [Tooltip("Font for the Thai color name (thaiName) when GameLanguage is TH — the labels' own font has no Thai glyphs.")]
    [SerializeField] private TMP_FontAsset thaiColorNameFont;
    private TextMeshProUGUI[] _colorNameTexts;
    private TMP_FontAsset[] _colorNameFonts;   // labels' original (English) fonts

    [Header("Target Bar")]
    [SerializeField] private Image targetChangeBar;
    [SerializeField] private Image targetChangeBarBg;

    [Header("Video — Left")]
    [SerializeField] private RawImage videoDisplayLeft;
    [SerializeField] private VideoClipEntry[] leftClips;

    [Header("Video — Center")]
    [SerializeField] private RawImage videoDisplayCenter;
    [SerializeField] private VideoClipEntry[] centerClips;

    [Header("Video — Right")]
    [SerializeField] private RawImage videoDisplayRight;
    [SerializeField] private VideoClipEntry[] rightClips;

    private VideoPlayer _vpLeft, _vpCenter, _vpRight;
    private RenderTexture _rtLeft, _rtCenter, _rtRight;

    [Header("Target Monster Video")]
    [Tooltip("Shows the target colour's monsterClip. Child of the target image so the pop animation scales it too.")]
    [SerializeField] private RawImage monsterVideoDisplay;

    // One prepared player per colour: targets change every few seconds, and swapping
    // the clip on a single player would re-prepare and freeze on each change.
    private VideoPlayer[] _monsterPlayers;
    private RenderTexture[] _monsterTextures;
    private int _monsterIdx = -1;
    private Coroutine _monsterSwapCo;
    private const int MonsterTextureWidth = 960;
    private const int MonsterTextureHeight = 540;

    [Header("Game Over")]
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [Tooltip("Highscore TMP shown after the score count-up on the gameover panel. Yellow + 'NEW HIGH SCORE' if beaten, otherwise white + 'HIGH SCORE: N'. Leave unwired to skip the reveal.")]
    [SerializeField] private TextMeshProUGUI highScoreText;
    [SerializeField] private Color highScoreColor = Color.white;
    [SerializeField] private Color newHighScoreColor = Color.yellow;

    // The old UI (kept switchable in the scene) turns these off
    [Header("UI Version")]
    [SerializeField] private bool useEntryTextColor = true;
    [Tooltip("Floor intro is timed to the new ColorHunt_Intro clip")]
    [SerializeField] private bool floorIntro = true;
    public bool FloorIntro => floorIntro;
    [SerializeField] private Button restartButton;

    [Header("Target Pulse")]
    [Tooltip("Half-amplitude of the target-image breathing scale, e.g. 0.08 = ±8%.")]
    [SerializeField] private float charPulseAmount = 0.08f;
    [Tooltip("Pulse cycles per second.")]
    [SerializeField] private float charPulseSpeed = 1.2f;

    private ChromaHuntManager _mgr;
    private Coroutine _targetChangeCo;
    private Coroutine _gameOverTransition;
    private Coroutine _gameOverCo;
    private Coroutine _charPulseCo;
    private Vector3 _charOrigScale;
    private float _curTargetDur;
    private int _roundCorrect, _roundWrong;
    private bool _hadSteps;
    private float _voiceCheckTimer;
    private bool _voiceCheckArmed;

    [Header("Voice Reaction Timing")]
    [Tooltip("CorrectVoice / WrongVoice cooldown (sec). After this elapses the next step fires a reaction.")]
    [SerializeField] private float voiceCheckInterval = 3f;
    [Tooltip("Skip firing if the current target has less than this much time left at either end, so NextVoice/ColorVoice can land clean on the swap.")]
    [SerializeField] private float voiceCheckTailGuard = 1f;
    private bool _warningPlayed;
    private int _lastHypeStreak;

    void Start()
    {
        SetupButtons();
        DisableHUDRaycast();
        SetupTargetBar();
        SetupVideoPlayers();
        SetupExtraVideoPlayers();
        ResetTransitionOverlay();

        if (colorNameTextRoot != null)
        {
            _colorNameTexts = colorNameTextRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
            _colorNameFonts = new TMP_FontAsset[_colorNameTexts.Length];
            for (int i = 0; i < _colorNameTexts.Length; i++) _colorNameFonts[i] = _colorNameTexts[i].font;
        }

        _mgr = ChromaHuntManager.Instance;
        if (_mgr != null) _mgr.OnStateChanged += HandleStateChanged;
        SetupMonsterVideos();
        ShowPanel(setupPanel);
        BeginSetupSequence();
    }

    void OnDestroy()
    {
        Unsub();
        if (_mgr != null) _mgr.OnStateChanged -= HandleStateChanged;
        CleanupExtraVideo();
        CleanupVideo();
        CleanupMonsterVideos();
    }

    void SetupButtons()
    {
        var startBtn = SetupStartButtonComponent;
        BindTouchButton(startBtn, BeginStartGame);
        BindTouchButton(restartButton, () => _mgr?.RestartFromScreen());
    }

    protected override void OnSetupRevealStartButton() => _mgr?.RevealStartButton();

    // Public wrappers — floor-tile paths (GridManager / Manager) reach the same fade as buttons.
    public void BeginStartGame()
    {
        if (_mgr == null || _mgr.state.appState != AppState.Setup || _mgr.IsCountingDown
            || Time.time < _mgr.StartReadyTime || ChromaHuntNetwork.IsNetClientOnly) return;
        BeginAppStateTransition(StartGameNow);
    }
    public void BeginRestartGame() => BeginAppStateTransition(() => _mgr?.RestartGame());
    // Tablet RESTART (Pilot 2026-11-04): play again straight away - no Start page
    public void BeginQuickRestart() => BeginQuickRestart(() => _mgr?.ExitToSetupForReplay(), StartGameNow);
    public void BeginExitToSetup() => BeginAppStateTransition(() => _mgr?.ExitToSetup());

    void StartGameNow()
    {
        // only host may start
        if (ChromaHuntNetwork.IsNetClientOnly) return;

        // net path runs countdown on every client via RpcStartGame
        if (ChromaHuntNetwork.IsNetworked && ChromaHuntNetwork.Instance != null)
            ChromaHuntNetwork.Instance.LocalRequestStartGame();
        else
            _mgr?.StartGame();
    }

    void SetupTargetBar()
    {
        if (targetChangeBar == null) return;
        var rt = targetChangeBar.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void HandleStateChanged(AppState st)
    {
        Unsub();

        switch (st)
        {
            case AppState.Setup:
                if (_gameOverTransition != null) StopCoroutine(_gameOverTransition);
                StopCharPulse();
                HideAllPanels();
                StopAllVideo();
                ShowPanel(setupPanel);
                BeginSetupSequence();
                break;
            case AppState.Playing:
                HideAllPanels();
                ShowPlayingHUD();
                break;
            case AppState.GameOver:
                if (_gameOverTransition != null) StopCoroutine(_gameOverTransition);
                StopCharPulse();
                _gameOverTransition = StartCoroutine(GameOverTransition());
                break;
        }
    }

    void ShowPlayingHUD()
    {
        ShowPanel(gameplayPanel);

        _mgr.OnTimeChanged += UpdateTimer;
        _mgr.OnScoreChanged += OnScore;
        _mgr.OnTargetColorChanged += OnTargetChanged;
        _mgr.OnTargetTimeChanged += OnTargetTime;
        _mgr.OnTileStepResult += OnStepResult;
        _mgr.OnBonusAwarded += OnBonus;
        _mgr.OnStreakChanged += OnStreak;
        _mgr.OnPhaseChanged += OnPhase;

        RefreshHUD();
        StartCharPulse();
    }

    public void RefreshHUD()
    {
        if (_mgr == null) return;

        if (_targetChangeCo != null) { StopCoroutine(_targetChangeCo); _targetChangeCo = null; }
        if (_gameOverCo != null) { StopCoroutine(_gameOverCo); _gameOverCo = null; }
        if (_gameOverTransition != null) { StopCoroutine(_gameOverTransition); _gameOverTransition = null; }
        StopAllVideo();

        _roundCorrect = 0;
        _roundWrong = 0;
        _hadSteps = false;
        _voiceCheckTimer = 0f;
        _voiceCheckArmed = false;
        _warningPlayed = false;
        _lastHypeStreak = 0;
        _curTargetDur = 0f;

        if (scoreText) scoreText.text = "0";
        if (streakText) { streakText.text = ""; streakText.color = Color.white; }

        if (timerText != null) {
            timerText.text = ChromaHuntManager.FormatTime(_mgr.state.timeLeft);
            timerText.color = Color.black;
            timerText.transform.localScale = Vector3.one;
        }

        if (feedbackText != null) {
            feedbackText.text = "";
            var c = feedbackText.color; c.a = 0f; feedbackText.color = c;
            feedbackText.transform.localScale = Vector3.one;
        }

        if (characterImage) {
            // with monster videos the still only shows as a fallback, never during the countdown
            characterImage.enabled = monsterVideoDisplay == null;
            characterImage.color = Color.white;
            if (_charOrigScale != Vector3.zero)
                characterImage.transform.localScale = _charOrigScale;
        }
        if (_colorNameTexts != null)
            foreach (var t in _colorNameTexts) if (t != null) t.text = "";

        SetBarProgress(1f);
    }

    void UpdateTimer(float timeLeft)
    {
        if (timerText == null) return;
        timerText.text = ChromaHuntManager.FormatTime(timeLeft);
        UpdateTimerColor(timerText, timeLeft);

        // warning vid when <10s
        if (!_warningPlayed && timeLeft <= 10f && timeLeft > 0f) {
            _warningPlayed = true;
            PlayRight(FindClip(rightClips, VideoState.WarningTime));
        }
    }

    void OnScore(int score)
    {
        if (scoreText) scoreText.text = score.ToString();
    }

    // --- target changed ---

    void OnTargetChanged(int colorIdx, bool isStroop, int stroopIdx)
    {
        if (_mgr.config.gameColors.Length == 0) return;

        _curTargetDur = GetTargetDur();
        var entry = _mgr.config.gameColors[colorIdx];

        // monster video, or its still if the colour has no clip — hidden in Phase 3 stroop
        bool showMonsterVideo = !isStroop && ShowMonster(colorIdx);
        if (isStroop) HideMonster();
        if (characterImage != null)
        {
            characterImage.enabled = !isStroop && !showMonsterVideo;
            if (!isStroop && !showMonsterVideo && entry.characterSprite != null)
            {
                characterImage.sprite = entry.characterSprite;
                characterImage.color = Color.white;
            }
        }

        // color name text — name always = target (matches TTS so player can
        // read or listen). Tint = target color in Phase 1/2, swapped to
        // stroopIdx's color in Phase 3 so the *visual* color misleads.
        if (_colorNameTexts != null && _colorNameTexts.Length > 0)
        {
            int tintIdx = (isStroop && stroopIdx >= 0 && stroopIdx < _mgr.config.gameColors.Length)
                ? stroopIdx : colorIdx;
            var tintEntry = _mgr.config.gameColors[tintIdx];
            // TH: thaiName ("สีแดง") in the Thai font; EN: colorName ("RED") in the label's own font
            bool thai = !GameLanguage.IsEnglish && !string.IsNullOrEmpty(entry.thaiName);
            for (int i = 0; i < _colorNameTexts.Length; i++)
            {
                var t = _colorNameTexts[i];
                if (t == null) continue;
                var font = thai && thaiColorNameFont != null ? thaiColorNameFont : _colorNameFonts[i];
                if (font != null && t.font != font) t.font = font;
                t.text = thai ? entry.thaiName : entry.colorName;
                t.color = useEntryTextColor && tintEntry.textColor.a > 0f ? tintEntry.textColor : tintEntry.color;
            }
        }

        // react video based on prev round performance — voice handled separately by interval tick
        if (_mgr.state.appState == AppState.Playing && _hadSteps
            && _mgr.state.currentPhase != GamePhase.Phase3)
        {
            int tot = _roundCorrect + _roundWrong;
            if (tot > 0)
            {
                float wrongRate = (float)_roundWrong / tot;
                if (wrongRate >= 0.5f)
                    PlayRight(FindClip(rightClips, VideoState.ReactNo));
                else
                    PlayRight(FindClip(rightClips, VideoState.ReactGood));
            }
        }

        _roundCorrect = 0;
        _roundWrong = 0;
        _hadSteps = false;

        // pop anim
        if (_targetChangeCo != null) StopCoroutine(_targetChangeCo);
        _targetChangeCo = StartCoroutine(AnimTargetChange());

        SetBarProgress(1f);
    }

    float GetTargetDur()
    {
        if (_mgr == null) return 10f;
        switch (_mgr.state.currentPhase)
        {
            case GamePhase.Phase2: return _mgr.config.phase2TargetDuration;
            case GamePhase.Phase3: return _mgr.config.phase3TargetDuration;
            default: return _mgr.config.phase1TargetDuration;
        }
    }

    IEnumerator AnimTargetChange()
    {
        if (characterImage == null) yield break;

        if (_charOrigScale == Vector3.zero)
            _charOrigScale = characterImage.transform.localScale;

        characterImage.transform.localScale = _charOrigScale * 1.2f;
        float t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            characterImage.transform.localScale = Vector3.Lerp(
                _charOrigScale * 1.2f, _charOrigScale, t / 0.2f);
            yield return null;
        }
        characterImage.transform.localScale = _charOrigScale;
        _targetChangeCo = null;
    }

    void StartCharPulse()
    {
        if (characterImage == null) return;
        if (_charPulseCo != null) StopCoroutine(_charPulseCo);
        _charPulseCo = StartCoroutine(CharPulseRoutine());
    }

    void StopCharPulse()
    {
        if (_charPulseCo != null) { StopCoroutine(_charPulseCo); _charPulseCo = null; }
        if (characterImage != null && _charOrigScale != Vector3.zero)
            characterImage.transform.localScale = _charOrigScale;
    }

    IEnumerator CharPulseRoutine()
    {
        if (characterImage == null) yield break;
        if (_charOrigScale == Vector3.zero)
            _charOrigScale = characterImage.transform.localScale;

        float t = 0f;
        while (true)
        {
            t += Time.deltaTime;
            // Skip while AnimTargetChange owns the scale, otherwise we'd fight it.
            if (_targetChangeCo == null)
            {
                // (sin+1)/2 → 0..1 so scale stays in [_charOrigScale, _charOrigScale*(1+amount)].
                float u = (Mathf.Sin(t * charPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
                characterImage.transform.localScale = _charOrigScale * (1f + charPulseAmount * u);
            }
            yield return null;
        }
    }

    // target countdown bar

    void OnTargetTime(float timeLeft)
    {
        TickReactionVoiceTimer();
        if (targetChangeBar == null || _curTargetDur <= 0f) return;
        SetBarProgress(Mathf.Clamp01(timeLeft / _curTargetDur));
    }

    void TickReactionVoiceTimer()
    {
        if (_mgr == null || _mgr.state.appState != AppState.Playing) return;
        if (_voiceCheckArmed) return;
        _voiceCheckTimer += Time.deltaTime;
        if (_voiceCheckTimer >= voiceCheckInterval) _voiceCheckArmed = true;
    }

    // Called from OnStepResult — if the cooldown elapsed and guards pass, fire reaction matching this step
    void TryFireStepReactionVoice(bool correct)
    {
        if (!_voiceCheckArmed) return;
        if (_mgr == null || _mgr.state.appState != AppState.Playing) return;

        float elapsed = _curTargetDur - _mgr.state.targetTimeLeft;
        if (_mgr.state.targetTimeLeft <= voiceCheckTailGuard) return;
        if (elapsed <= voiceCheckTailGuard) return;

        var am = AudioManager.Instance;
        if (am == null) return;
        if (am.IsVoicePlaying()) return;

        if (correct)
            am.PlayVoice(UnityEngine.Random.Range(0, 2) == 0 ? "CorrectVoice1" : "CorrectVoice2");
        else
            am.PlayVoice(UnityEngine.Random.Range(0, 2) == 0 ? "WrongVoice1" : "WrongVoice2");

        _voiceCheckArmed = false;
        _voiceCheckTimer = 0f;
    }

    void SetBarProgress(float progress)
    {
        if (targetChangeBar == null) return;
        var rt = targetChangeBar.rectTransform;
        rt.anchorMax = new Vector2(progress, 1f);
        rt.offsetMax = Vector2.zero;

        // green -> yellow -> red
        if (progress > 0.5f)
            targetChangeBar.color = Color.Lerp(Color.yellow, Color.green, (progress - 0.5f) * 2f);
        else
            targetChangeBar.color = Color.Lerp(Color.red, Color.yellow, progress * 2f);
    }

    // --- feedback ---

    void OnStepResult(int delta, bool correct)
    {
        if (feedbackText)
        {
            if (correct) ShowStatusPop(feedbackText, $"+{delta}", Color.green);
            else ShowStatusPop(feedbackText, $"{delta}", Color.red);
        }
        _hadSteps = true;
        if (correct) _roundCorrect++;
        else _roundWrong++;
        TryFireStepReactionVoice(correct);
    }

    void OnBonus(int bonus)
    {
        if (feedbackText)
            ShowStatusPop(feedbackText, $"+{bonus}\nBONUS!", Color.yellow);

        // hype combo vid every 20 streak
        if (_mgr.state.correctStreak > 0 && _mgr.state.correctStreak % 20 == 0)
            PlayLeft(FindClip(leftClips, VideoState.HypeCombo));
    }

    void OnStreak(int streak)
    {
        if (streakText == null) return;
        if (streak < 3) { streakText.text = ""; return; }

        streakText.text = $"x{streak}";
        if (streak >= 10) streakText.color = Color.red;
        else if (streak >= 6) streakText.color = Color.yellow;
        else streakText.color = Color.white;
    }

    void OnPhase(GamePhase phase)
    {
        PlayLeft(FindClip(leftClips, VideoState.Target));
        if (phase == GamePhase.Phase3)
            PlayRight(FindClip(rightClips, VideoState.WarningTime));
    }

    // --- game over ---

    IEnumerator GameOverTransition()
    {
        StopChannel(_vpLeft, videoDisplayLeft);
        StopChannel(_vpRight, videoDisplayRight);
        HideMonster();

        // EndVoice rides the time-up moment before switching to gameover panel
        var am = AudioManager.Instance;
        if (am != null) {
            float endLen = am.GetVoiceClipLength("EndVoice");
            if (endLen > 0f) am.DuckMusicFor(endLen);
            am.PlayVoice("EndVoice");
            if (endLen > 0f) yield return new WaitForSeconds(endLen);
        }

        // White fade covers the panel swap from gameplay → gameover
        yield return FadeOverlayRoutine(0f, 1f, transitionFadeOutDuration);

        HideAllPanels();
        ShowPanel(gameOverPanel);

        var grid = ChromaHuntGridManager.Instance;
        // solo: Restart comes from the tablet (TabletStart) — black floor, no button
        if (grid != null)
        {
            if (TabletStart.FloorHidden(ChromaHuntNetwork.IsNetworked)) grid.SetAllTilesBlack();
            else grid.RenderGameOverButtons();
        }
        _mgr?.ResetGameOverDelay();

        HideTMP(finalScoreText);
        if (highScoreText != null) highScoreText.gameObject.SetActive(false);

        // result video + tier voice on gameover panel — score animation waits for video to finish
        float dur = PlayResultVideo();
        if (_mgr != null) {
            string tier = _mgr.GetTierVoiceKey();
            float tierLen = am != null ? am.GetVoiceClipLength(tier) : 0f;
            if (tierLen > 0f) am?.DuckMusicFor(tierLen);
            am?.PlayVoice(tier);
        }

        if (transitionHoldDuration > 0f) yield return new WaitForSeconds(transitionHoldDuration);
        yield return FadeOverlayRoutine(1f, 0f, transitionFadeInDuration);

        if (dur > 0f) yield return new WaitForSeconds(dur);

        if (_gameOverCo != null) StopCoroutine(_gameOverCo);
        _gameOverCo = StartCoroutine(GameOverAnim());
    }

    float PlayResultVideo()
    {
        float rate = _mgr.state.CorrectPercent;

        // no steps at all, skip
        if (rate == 0f && _mgr.state.totalCorrect == 0 && _mgr.state.totalWrong == 0) {
            Debug.LogWarning("[ChromaHuntUI] no steps — skip result video");
            return 0f;
        }

        VideoClip clip;
        if (rate >= 80f) clip = FindClip(centerClips, VideoState.Result80_100);
        else if (rate >= 40f) clip = FindClip(centerClips, VideoState.Result40_79);
        else clip = FindClip(centerClips, VideoState.ResultLess40);

        // new Result page has no tier videos: score animation starts right away
        if (clip == null) return 0f;

        PlayCenter(clip);
        return (float)clip.length;
    }

    IEnumerator GameOverAnim()
    {
        yield return ScoreCountUpRoutine(finalScoreText, "", _mgr.state.score);

        if (highScoreText != null && _mgr != null)
        {
            yield return new WaitForSeconds(0.3f);
            int score = _mgr.state.score;
            int high = _mgr.GetHighScore();
            bool newRecord = score >= high;
            Color hsColor = newRecord ? newHighScoreColor : highScoreColor;
            string prefix = newRecord ? "NEW HIGH SCORE: " : "HIGH SCORE: ";
            int shownValue = newRecord ? score : high;
            highScoreText.gameObject.SetActive(true);
            highScoreText.color = hsColor;
            yield return ScoreCountUpRoutine(highScoreText, prefix, shownValue);
        }
    }

    // --- countdown ---

    public IEnumerator StartCountdown()
    {
        HideAllPanels();
        ShowPanel(gameplayPanel);
        RefreshHUD();

        var grid = ChromaHuntGridManager.Instance;
        yield return CountdownRoutine(
            (digit, color) => grid?.RenderDigitOnGrid(digit, color),
            (color) => grid?.RenderGoOnGrid(color),
            () => grid?.SetAllTilesBlack(),
            Color.white);
        grid?.SetAllFloor();
        // welcome is kicked off by Manager AFTER state change — RefreshHUD's StopAllVideo
        // would otherwise kill it if started here
    }

    public void PlayWelcomeParallel()
    {
        StartCoroutine(PlayWelcomeAndWait());
    }

    IEnumerator PlayWelcomeAndWait()
    {
        var lw = FindClip(leftClips, VideoState.Welcome);
        var cw = FindClip(centerClips, VideoState.Welcome);
        var rw = FindClip(rightClips, VideoState.Welcome);

        VideoClip longest = null;
        if (lw != null) { PlayLeft(lw); longest = lw; }
        if (cw != null) { PlayCenter(cw); if (longest == null || cw.length > longest.length) longest = cw; }
        if (rw != null) { PlayRight(rw); if (longest == null || rw.length > longest.length) longest = rw; }

        if (longest != null)
            yield return new WaitForSeconds((float)longest.length);
    }

    // --- target monster video ---

    void SetupMonsterVideos()
    {
        if (monsterVideoDisplay == null || _mgr == null || _mgr.config == null) return;

        var colors = _mgr.config.gameColors;
        _monsterPlayers = new VideoPlayer[colors.Length];
        _monsterTextures = new RenderTexture[colors.Length];
        monsterVideoDisplay.enabled = false;

        for (int i = 0; i < colors.Length; i++)
        {
            var clip = colors[i].monsterClip;
            if (clip == null) continue;

            var rt = new RenderTexture(MonsterTextureWidth, MonsterTextureHeight, 0);
            // Start on the key colour so a frame that isn't decoded yet draws nothing, not a black box.
            var key = colors[i].monsterMaterial != null ? colors[i].monsterMaterial.GetColor("_KeyColor") : Color.clear;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, key);
            RenderTexture.active = previous;

            var vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.source = VideoSource.VideoClip;
            vp.clip = clip;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.isLooping = true;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.controlledAudioTrackCount = 0;
            vp.timeUpdateMode = VideoTimeUpdateMode.GameTime;
            vp.Prepare();

            _monsterPlayers[i] = vp;
            _monsterTextures[i] = rt;
        }
    }

    bool ShowMonster(int colorIdx)
    {
        if (monsterVideoDisplay == null || _monsterPlayers == null
            || colorIdx < 0 || colorIdx >= _monsterPlayers.Length || _monsterPlayers[colorIdx] == null)
        {
            HideMonster();
            return false;
        }
        if (colorIdx == _monsterIdx) return true;

        _monsterIdx = colorIdx;
        if (_monsterSwapCo != null) StopCoroutine(_monsterSwapCo);
        _monsterSwapCo = StartCoroutine(SwapMonsterRoutine(colorIdx));
        return true;
    }

    IEnumerator SwapMonsterRoutine(int colorIdx)
    {
        var vp = _monsterPlayers[colorIdx];
        vp.Play();

        // Keep the previous monster on screen until this one has a frame (first use only;
        // after that the paused player resumes straight away).
        float waited = 0f;
        while (vp.frame < 0 && waited < 0.5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        monsterVideoDisplay.texture = _monsterTextures[colorIdx];
        monsterVideoDisplay.material = _mgr.config.gameColors[colorIdx].monsterMaterial;
        monsterVideoDisplay.enabled = true;

        for (int i = 0; i < _monsterPlayers.Length; i++)
            if (i != colorIdx && _monsterPlayers[i] != null && _monsterPlayers[i].isPlaying)
                _monsterPlayers[i].Pause();
        _monsterSwapCo = null;
    }

    void HideMonster()
    {
        if (_monsterSwapCo != null) { StopCoroutine(_monsterSwapCo); _monsterSwapCo = null; }
        _monsterIdx = -1;
        if (monsterVideoDisplay != null) monsterVideoDisplay.enabled = false;
        if (_monsterPlayers == null) return;
        foreach (var vp in _monsterPlayers)
            if (vp != null && vp.isPlaying) vp.Pause();
    }

    void CleanupMonsterVideos()
    {
        if (_monsterTextures == null) return;
        foreach (var rt in _monsterTextures)
            if (rt != null) rt.Release();
    }

    // --- video helpers ---

    void SetupExtraVideoPlayers()
    {
        _vpLeft = MakeChannel(videoDisplayLeft, GetFirstClip(leftClips), ref _rtLeft);
        _vpCenter = MakeChannel(videoDisplayCenter, GetFirstClip(centerClips), ref _rtCenter);
        _vpRight = MakeChannel(videoDisplayRight, GetFirstClip(rightClips), ref _rtRight);

        WireEnd(_vpLeft, videoDisplayLeft);
        WireEnd(_vpCenter, videoDisplayCenter);
        WireEnd(_vpRight, videoDisplayRight);
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
            // dont hide during game over, transition handles it
            if (_mgr != null && _mgr.state.appState == AppState.GameOver) return;
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
        //Debug.Log($"[CHVideo] clip={clip.name} clip.frameRate={clip.frameRate} frameCount={(long)clip.frameCount} length={clip.length:F2}s loop={loop} timeUpdateMode={vp.timeUpdateMode} audioMode={vp.audioOutputMode}");
        vp.Play();
    }

    void PlayLeft(VideoClip clip) => PlayOn(_vpLeft, videoDisplayLeft, clip);
    void PlayCenter(VideoClip clip) => PlayOn(_vpCenter, videoDisplayCenter, clip);
    void PlayRight(VideoClip clip) => PlayOn(_vpRight, videoDisplayRight, clip);

    VideoClip FindClip(VideoClipEntry[] entries, VideoState st)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i] != null && entries[i].state == st) return entries[i].clip;
        return null;
    }

    VideoClip GetFirstClip(VideoClipEntry[] entries)
    {
        if (entries == null) return null;
        foreach (var e in entries)
            if (e?.clip != null) return e.clip;
        return null;
    }

    void StopAllVideo()
    {
        StopChannel(_vpLeft, videoDisplayLeft);
        StopChannel(_vpCenter, videoDisplayCenter);
        StopChannel(_vpRight, videoDisplayRight);
        HideMonster();
        StopSetupSequence();
    }

    void StopChannel(VideoPlayer vp, RawImage display)
    {
        if (vp) vp.Stop();
        if (display) display.enabled = false;
    }

    void CleanupExtraVideo()
    {
        CleanupCh(ref _vpLeft, ref _rtLeft);
        CleanupCh(ref _vpCenter, ref _rtCenter);
        CleanupCh(ref _vpRight, ref _rtRight);
    }


    void CleanupCh(ref VideoPlayer vp, ref RenderTexture rt)
    {
        if (vp != null) Destroy(vp);
        if (rt != null) { rt.Release(); Destroy(rt); }
        vp = null; rt = null;
    }

    void Unsub()
    {
        if (_mgr == null) return;
        _mgr.OnTimeChanged -= UpdateTimer;
        _mgr.OnScoreChanged -= OnScore;
        _mgr.OnTargetColorChanged -= OnTargetChanged;
        _mgr.OnTargetTimeChanged -= OnTargetTime;
        _mgr.OnTileStepResult -= OnStepResult;
        _mgr.OnBonusAwarded -= OnBonus;
        _mgr.OnStreakChanged -= OnStreak;
        _mgr.OnPhaseChanged -= OnPhase;
    }
}
