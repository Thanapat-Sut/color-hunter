using System;
using System.Collections;
using UnityEngine;

public class ChromaHuntManager : MonoBehaviour
{
    public static ChromaHuntManager Instance { get; private set; }

    [Header("References")]
    public ChromaHuntConfig config;
    public ChromaHuntGridManager gridManager;
    public ChromaHuntUI ui;

    [Header("CCTV Replay")]
    [Tooltip("Email receiving the replay clip. Leave empty for local JSON capture only.")]
    public string replayEmail = "";
    public string replayPlayerName = "";

    ReplaySubmit _roundReplay;
    string _roundReplayEmail;
    string _roundReplayPlayerName;
    float _replayClock;
    public float ReplayElapsed => Time.unscaledTime - _replayClock;

    [Header("Runtime")]
    public ChromaHuntState state = new ChromaHuntState();

    public event Action<AppState> OnStateChanged;
    public event Action<int> OnScoreChanged;
    public event Action<float> OnTimeChanged;
    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int, bool, int> OnTargetColorChanged;
    public event Action<float> OnTargetTimeChanged;
    public event Action<int, bool> OnTileStepResult;
    public event Action<int> OnBonusAwarded;
    public event Action<int> OnStreakChanged;
    public event Action<float> OnAccuracyChanged;

    Coroutine _timerCo;
    Coroutine _targetCo;
    Coroutine _shuffleCo;
    Coroutine _menuMusicCo;
    bool _countingDown;
    bool _gridReady;
    float _startReadyTime;
    float _gameOverReadyTime;

    public bool IsCountingDown => _countingDown;
    public float StartReadyTime => _startReadyTime;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        state.gameDuration = config.gameDuration;
        Application.runInBackground = true;
    }

    // LED floor show on the idle solo Setup and on Game Over from the moment its panel comes up (FloorShowRunner,
    // Pilot 2026-11-04 pick: key 7)
    bool FloorShowIdle => !ChromaHuntNetwork.IsNetworked && ((state.appState == AppState.Setup && !_countingDown) || state.appState == AppState.GameOver);

    void Start()
    {
        // Start page: gather, spiral and blast timed to the intro clip, then Color Rain (Pilot 2026-11-06)
        FloorShowRunner.Attach(this, gridManager, "Color_hunt", 0, () => FloorShowIdle, FloorShowPatterns.ColorHunterIntro,
            () => state.appState == AppState.Setup && ui != null && ui.FloorIntro ? ui.SetupStartClipTime : -1);
        SetState(AppState.Setup);
        InitFloorForSetup();
        AudioManager.Instance?.PlayMusic("ColorMenu");
    }

    void InitFloorForSetup()
    {
        if (!_gridReady) {
            gridManager.Initialize();
            _gridReady = true;
        }
        // start tile held until setup video reaches loop — UI calls RevealStartButton()
        gridManager.SetAllTilesBlack();
        _startReadyTime = float.PositiveInfinity;
    }

    public void RevealStartButton()
    {
        // solo: START comes from the tablet (TabletStart), the floor stays dark
        if (!TabletStart.FloorHidden(ChromaHuntNetwork.IsNetworked))
            gridManager.RenderStartButton(config.startButtonConfirmColor);
        _startReadyTime = Time.time + TabletStart.StartDelay(ChromaHuntNetwork.IsNetworked, config.startButtonDelay);
    }

    // Solo: tablet START / RESTART do what the floor buttons do in multiplayer.
    void LateUpdate()
    {
        if (ChromaHuntNetwork.IsNetworked) return;
        bool startReady = state.appState == AppState.Setup && !_countingDown && Time.time >= _startReadyTime;
        bool restartReady = state.appState == AppState.GameOver && Time.time >= _gameOverReadyTime;
        if (startReady && TabletStart.TakeGo())
        {
            if (ui != null) ui.BeginStartGame();
            else { AudioManager.Instance?.PlaySFX("ClickSFX"); StartGame(); }
        }
        else if (restartReady && TabletStart.TakeRestart()) TabletQuickRestart();
        else if (restartReady && TabletStart.TakeHome()) HandleGameOverButton(0);
        TabletStart.Report(TabletStart.ScreenFor(state.appState, startReady, restartReady));
    }

    public void SetState(AppState s)
    {
        // Client PCs reach GameOver via Net_SetAppState, never EndGame — finish here so both paths submit.
        if (s == AppState.GameOver) FinishReplay();
        else if (s != AppState.Playing) _roundReplay = null;
        state.appState = s;
        OnStateChanged?.Invoke(s);
    }

    // --- start & countdown ---

    public void StartGame()
    {
        StopAllCo();
        state.gameDuration = config.gameDuration;
        state.timeLeft = state.gameDuration;
        state.ResetTurnState();
        state.ResetScores();
        _countingDown = false;

        gridManager.Initialize();
        StartCoroutine(CountdownThenPlay());
    }

    IEnumerator CountdownThenPlay()
    {
        _countingDown = true;
        AudioManager.Instance?.FadeOutMusic(0.5f);
        if (ui != null)
            yield return StartCoroutine(ui.StartCountdown());
        _countingDown = false;
        BeginPlaying();
    }

    void BeginPlaying()
    {
        _replayClock = Time.unscaledTime;
        BeginReplay();
        SetState(AppState.Playing);
        RecordReplaySnapshot("game_start");
        if (ui != null) ui.RefreshHUD();
        AudioManager.Instance?.FadeInMusic("ColorBGM", 1f);

        // welcome plays in parallel — kicked off after RefreshHUD's StopAllVideo sweep
        if (ui != null) ui.PlayWelcomeParallel();

        // pure client = server pushes state, skip local loops
        if (ChromaHuntNetwork.IsNetClientOnly)
        {
            // first target can sync in during our countdown, before replay started — open it now
            if (state.targetColorIndex >= 0 && !_tActive) BeginReplayTarget();
            _clientStroopIntroPlayed = false;
            OnPhaseChanged?.Invoke(state.currentPhase);
            return;
        }

        _isFirstColor = true;
        _promptIframe = false;
        _stroopIntroPending = _stroopIntroActive = _timerPaused = false;
        // shuffle pool order once per game so Phase 1's 2 colors + Phase 2 reveal sequence
        // vary run to run instead of always drawing indices 0,1,2,...
        state.shuffledColorPool = BuildShuffledPool(config.gameColors.Length);
        state.activeColorCount = Mathf.Clamp(config.phase1ColorCount, 1, config.gameColors.Length);
        // first ramp lands right at Phase 2 entry (elapsed = phase1End); subsequent adds every phase2ColorAddInterval
        state.nextColorAddTime = config.phase1End;
        state.warnVoiceFired = false;
        PickNewTargetColor();
        gridManager.DistributeColors(state.targetColorIndex, state.GetActiveColorIndices());

        Kill(ref _timerCo);
        _timerCo = StartCoroutine(GameTimerRoutine());
        Kill(ref _targetCo);
        _targetCo = StartCoroutine(TargetColorLoop());

        OnPhaseChanged?.Invoke(state.currentPhase);
    }

    bool _isFirstColor;

    // --- CCTV replay ---

    void BeginReplay()
    {
        if (_roundReplay != null) return;
        var replay = ReplaySubmit.I;
        if (replay == null || !replay.isActiveAndEnabled)
        {
            Debug.LogWarning("[ChromaReplay] Add an active ReplaySubmit to the scene to enable replay.");
            return;
        }
        _roundReplayEmail = replayEmail?.Trim() ?? "";
        _roundReplayPlayerName = replayPlayerName;
        replay.BeginRound();
        _roundReplay = replay;
        _tActive = false;
        _tIndex = 0;
    }

    void FinishReplay()
    {
        var replay = _roundReplay;
        if (replay == null) return;
        EndReplayTarget("game_end");
        string tier = state.score >= config.highScoreThreshold ? "high"
            : state.score >= config.midScoreThreshold ? "mid" : "low";
        RecordReplaySnapshot("game_end", FormattableString.Invariant(
            $"tier={tier};highThreshold={config.highScoreThreshold};midThreshold={config.midScoreThreshold};accuracy={state.CorrectPercent:0.#}"));
        _roundReplay = null; // Clear before submit so one match submits only once per PC.
        if (replay.isActiveAndEnabled)
            replay.EndRound(_roundReplayEmail, _roundReplayPlayerName, state.score, "timeout");
    }

    public void RecordReplayLog(string message)
    {
        if (_roundReplay == null) return;
        Debug.Log(message);
        _roundReplay.RecordLog(message, ReplayElapsed);
    }

    // Per-PC snapshot: slot 0 = host/solo, slot 1 = MP client. Score/streak are this PC's player only.
    void RecordReplaySnapshot(string kind, string extra = null)
    {
        if (_roundReplay == null) return;
        int slot = ChromaHuntNetwork.IsNetClientOnly ? 1 : 0;
        string msg = FormattableString.Invariant(
            $"[ChromaReplay] type={kind};slot={slot};phase={state.currentPhase};timeLeft={state.timeLeft:R};duration={state.gameDuration};target={ColorName(state.targetColorIndex)};stroop={state.isStroopRound};stroopDisplay={ColorName(state.stroopDisplayColorIndex)};activeColors={state.activeColorCount};score={state.score};streak={state.correctStreak};bestStreak={state.bestStreak};correct={state.totalCorrect};wrong={state.totalWrong}");
        if (!string.IsNullOrEmpty(extra)) msg += ";" + extra;
        RecordReplayLog(msg);
    }

    // MP steps are adjudicated on the server; each PC logs its own player's result from RpcOnStepResult.
    public void RecordReplayStep(int tileIndex, int tileColor, bool correct, int delta, bool bonus)
    {
        if (_tActive)
        {
            float t = ReplayElapsed - _tStart;
            if (correct)
            {
                _tHits++;
                if (_tFirstHit < 0f) _tFirstHit = t;
                _tLastHit = t;
                if (bonus) _tBonuses++;
            }
            else
            {
                _tMisses++;
                if (state.isStroopRound && tileColor == state.stroopDisplayColorIndex) _tStroopFooled++;
            }
        }
        RecordReplaySnapshot("step", FormattableString.Invariant(
            $"tile={tileIndex};tileColor={ColorName(tileColor)};hit={correct};delta={delta};bonus={bonus}"));
    }

    // Per-target summary: one line per prompt so the server can digest without regrouping steps.
    bool _tActive;
    float _tStart, _tFirstHit, _tLastHit;
    int _tHits, _tMisses, _tBonuses, _tStroopFooled, _tScoreStart, _tIndex;

    void BeginReplayTarget()
    {
        if (_roundReplay == null) return;
        _tActive = true;
        _tIndex++;
        _tStart = ReplayElapsed;
        _tFirstHit = _tLastHit = -1f;
        _tHits = _tMisses = _tBonuses = _tStroopFooled = 0;
        _tScoreStart = state.score;
        RecordReplaySnapshot("target_changed", FormattableString.Invariant($"targetIndex={_tIndex}"));
    }

    // Call BEFORE the target state is overwritten so the snapshot still shows the finished target.
    void EndReplayTarget(string endedBy)
    {
        if (!_tActive || _roundReplay == null) return;
        _tActive = false;
        string Sec(float v) => v < 0f ? "null" : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        RecordReplaySnapshot("target_end", FormattableString.Invariant(
            $"targetIndex={_tIndex};endedBy={endedBy};targetDuration={Sec(ReplayElapsed - _tStart)};hits={_tHits};misses={_tMisses};firstHitAfter={Sec(_tFirstHit)};lastHitAfter={Sec(_tLastHit)};bonuses={_tBonuses};stroopFooled={_tStroopFooled};scoreDelta={state.score - _tScoreStart};streakAtEnd={state.correctStreak}"));
    }

    string ColorName(int idx)
    {
        if (config.gameColors == null || idx < 0 || idx >= config.gameColors.Length) return "none";
        return config.gameColors[idx].colorName;
    }

    IEnumerator GameTimerRoutine()
    {
        while (state.timeLeft > 0)
        {
            yield return new WaitForSeconds(1f);
            if (_timerPaused) continue; // Phase 3 intro
            state.timeLeft--;
            OnTimeChanged?.Invoke(state.timeLeft);
            CheckPhase();
            CheckColorRamp();
            CheckWarnVoice();
        }
        EndGame();
    }

    void CheckColorRamp()
    {
        // Phase 2 gradually adds one color at a time from the pool.
        // Phase 3 keeps the full pool it inherited.
        if (state.currentPhase != GamePhase.Phase2) return;
        int poolCount = config.gameColors.Length;
        if (state.activeColorCount >= poolCount) return;
        float interval = config.phase2ColorAddInterval;
        if (interval <= 0f)
        {
            // interval=0 → jump straight to full pool at Phase 2 entry
            state.activeColorCount = poolCount;
            gridManager.DistributeColors(state.targetColorIndex, state.GetActiveColorIndices());
            RecordReplaySnapshot("colors_added");
            return;
        }
        if (state.ElapsedTime + 0.001f < state.nextColorAddTime) return;
        state.activeColorCount = Mathf.Min(state.activeColorCount + 1, poolCount);
        state.nextColorAddTime += interval;
        gridManager.DistributeColors(state.targetColorIndex, state.GetActiveColorIndices());
        RecordReplaySnapshot("colors_added");
    }

    void CheckWarnVoice()
    {
        if (state.warnVoiceFired) return;
        if (state.timeLeft > config.warnVoiceTimeLeft) return;
        state.warnVoiceFired = true;
        StartCoroutine(PlayWarnCue());
    }

    IEnumerator PlayWarnCue()
    {
        var am = AudioManager.Instance;
        if (am == null) yield break;
        while (am.IsVoicePlaying()) yield return null;
        float len = am.GetVoiceClipLength("30sVoice");
        am.DuckMusicFor(len);
        am.LockVoiceFor(len);
        am.PlayVoice("30sVoice");
    }

    void CheckPhase()
    {
        float elapsed = state.ElapsedTime;
        GamePhase p;
        if (elapsed < config.phase1End) p = GamePhase.Phase1;
        else if (elapsed < config.phase2End) p = GamePhase.Phase2;
        else p = GamePhase.Phase3;

        if (p != state.currentPhase) {
            state.currentPhase = p;
            RecordReplaySnapshot("phase_changed");
            OnPhaseChanged?.Invoke(p);
            UpdateShuffleCo();

            // CH-02 (2026-10-16): the in-flight target keeps its full old-phase duration; only
            // the next target uses the new phase's values (TargetColorLoop reads them per target).
            // Entering Phase 3: the stroop rule is explained at the first Phase 3 target, with the
            // game clock paused (StroopIntroRoutine).
            if (p == GamePhase.Phase3) _stroopIntroPending = true;
        }
    }

    // Phase 3 intro: the first stroop target is already on screen as the example while
    // StroopVoice explains the rule; game clock paused and steps ignored until it ends.
    bool _stroopIntroPending, _stroopIntroActive, _timerPaused;

    /// <summary>Steps are ignored (no score, no penalty) — prompt i-frame or the Phase 3 intro.</summary>
    public bool StepsIgnored => _promptIframe || _stroopIntroActive;

    IEnumerator StroopIntroRoutine()
    {
        _timerPaused = true;
        _stroopIntroActive = true;
        // target bar shows full while waiting (its countdown starts after the intro)
        state.targetTimeLeft = GetTargetDuration();
        OnTargetTimeChanged?.Invoke(state.targetTimeLeft);
        RecordReplaySnapshot("stroop_intro");
        yield return PlayStroopCue();
        _stroopIntroActive = false;
        _timerPaused = false;
    }

    bool _pendingStroopAnnouncement;
    IEnumerator PlayStroopCue()
    {
        var am = AudioManager.Instance;
        if (am == null) yield break;
        while (am.IsVoicePlaying()) yield return null;
        float stroopLen = am.GetVoiceClipLength("StroopVoice");
        am.DuckMusicFor(stroopLen);
        am.LockVoiceFor(stroopLen);
        am.PlayVoice("StroopVoice");
        if (stroopLen > 0f) yield return new WaitForSeconds(stroopLen);
    }

    // --- target color loop ---

    IEnumerator TargetColorLoop()
    {
        while (state.appState == AppState.Playing)
        {
            float dur = GetTargetDuration();
            state.targetTimeLeft = dur;
            OnTargetTimeChanged?.Invoke(dur);

            // target countdown starts only after the prompt i-frame (voice) ends
            while (_promptIframe && state.appState == AppState.Playing)
                yield return null;

            while (state.targetTimeLeft > 0 && state.appState == AppState.Playing)
            {
                yield return null;
                state.targetTimeLeft -= Time.deltaTime;
                OnTargetTimeChanged?.Invoke(state.targetTimeLeft);
            }
            if (state.appState != AppState.Playing) break;

            // don't spawn a new target if the game will end before it completes —
            // avoids the hanging half-cycle prompt at time-up. timer keeps running via GameTimerRoutine.
            if (state.timeLeft < GetTargetDuration()) break;

            // first Phase 3 target: StroopVoice replaces the NextVoice lead-in
            bool stroopIntro = _stroopIntroPending && state.currentPhase == GamePhase.Phase3;
            if (stroopIntro) { _stroopIntroPending = false; _pendingStroopAnnouncement = true; }

            PickNewTargetColor();
            gridManager.DistributeColors(state.targetColorIndex, state.GetActiveColorIndices());

            if (stroopIntro) yield return StroopIntroRoutine();
        }
    }

    static int[] BuildShuffledPool(int poolCount)
    {
        var pool = new int[poolCount];
        for (int i = 0; i < poolCount; i++) pool[i] = i;
        for (int i = poolCount - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool;
    }

    void PickNewTargetColor()
    {
        int poolCount = config.gameColors.Length;
        if (poolCount == 0) return;
        EndReplayTarget("next_target");
        int[] activePool = state.GetActiveColorIndices();
        int count = activePool != null ? activePool.Length : poolCount;

        // pick slot in active subset; translate to actual pool index via activePool[slot]
        int idx;
        do
        {
            int slot = UnityEngine.Random.Range(0, count);
            idx = activePool != null ? activePool[slot] : slot;
        }
        while (idx == state.targetColorIndex && count > 1);

        state.targetColorIndex = idx;

        // phase 3 = stroop (show wrong color name)
        if (state.currentPhase == GamePhase.Phase3)
        {
            state.isStroopRound = true;
            int displayIdx;
            do
            {
                int slot = UnityEngine.Random.Range(0, count);
                displayIdx = activePool != null ? activePool[slot] : slot;
            }
            while (displayIdx == idx && count > 1);
            state.stroopDisplayColorIndex = displayIdx;
        }
        else {
            state.isStroopRound = false;
            state.stroopDisplayColorIndex = -1;
        }

        BeginReplayTarget();
        OnTargetColorChanged?.Invoke(state.targetColorIndex, state.isStroopRound, state.stroopDisplayColorIndex);
        // Phase3: "Next" only, no color SFX (stroop = don't reveal the target name).
        bool playColor = state.currentPhase != GamePhase.Phase3;
        bool suppressLead = _pendingStroopAnnouncement;
        if (suppressLead) _pendingStroopAnnouncement = false;
        StartPrompt(state.targetColorIndex, _isFirstColor, playColor, !suppressLead, state.currentPhase == GamePhase.Phase3);
        _isFirstColor = false;
    }

    Coroutine _promptCo;

    // i-frame: floor steps are ignored (no score, no penalty) from a target change until its
    // prompt voice (lead-in + color name) has finished, so players hear the color before stepping.
    // Phase 3 has no color voice: the i-frame is a fixed config.phase3PromptIframe window (CH-01).
    bool _promptIframe;

    void StartPrompt(int colorIdx, bool isFirst, bool playColorSfx, bool playVoice, bool phase3)
    {
        if (_promptCo != null) StopCoroutine(_promptCo);
        _promptCo = StartCoroutine(PlayPromptSfx(colorIdx, isFirst, playColorSfx, playVoice,
            phase3 ? Mathf.Max(0f, config.phase3PromptIframe) : -1f));
    }

    // fixedIframe < 0: i-frame lasts until the prompt voice ends; >= 0: fixed seconds, voice runs on its own
    IEnumerator PlayPromptSfx(int colorIdx, bool isFirst, bool playColorSfx, bool playVoice, float fixedIframe)
    {
        _promptIframe = true;
        if (fixedIframe >= 0f)
        {
            if (playVoice) StartCoroutine(PlayPromptVoices(colorIdx, isFirst, playColorSfx));
            if (fixedIframe > 0f) yield return new WaitForSeconds(fixedIframe);
        }
        else if (playVoice)
            yield return PlayPromptVoices(colorIdx, isFirst, playColorSfx);
        _promptIframe = false;
    }

    IEnumerator PlayPromptVoices(int colorIdx, bool isFirst, bool playColorSfx)
    {
        // duck BGM → lead-in → color voice. duck window covers both up front so music
        // doesn't peek back up in the gap.
        string leadKey = isFirst ? "StartVoice" : "NextVoice";
        string colorKey = null;
        if (playColorSfx && config.gameColors != null && colorIdx >= 0 && colorIdx < config.gameColors.Length) {
            colorKey = config.gameColors[colorIdx].sfxName;
            // fallback when sfxName isn't set: "RED" → "RedVoice"
            if (string.IsNullOrEmpty(colorKey))
                colorKey = TitleCaseVoiceName(config.gameColors[colorIdx].colorName);
        }

        var am = AudioManager.Instance;
        // hold off lead-in until any in-flight voice (e.g. CorrectVoice/WrongVoice reaction) finishes
        // — PlayVoice is exclusive, so jumping in now would cut the reaction off
        while (am != null && am.IsVoicePlaying()) yield return null;

        float leadLen = am != null ? am.GetVoiceClipLength(leadKey) : 0f;
        float colorLen = (am != null && !string.IsNullOrEmpty(colorKey)) ? am.GetVoiceClipLength(colorKey) : 0f;
        float gap = Mathf.Max(0f, config.promptSfxGap);
        float total = leadLen + (colorLen > 0f ? gap + colorLen : 0f);
        am?.DuckMusicFor(total);
        am?.LockVoiceFor(total);

        am?.PlayVoice(leadKey);
        if (string.IsNullOrEmpty(colorKey)) {
            if (leadLen > 0f) yield return new WaitForSeconds(leadLen);
            yield break;
        }
        float wait = leadLen + gap;
        if (wait > 0f) yield return new WaitForSeconds(wait);
        am?.PlayVoice(colorKey);
        if (colorLen > 0f) yield return new WaitForSeconds(colorLen);
    }

    void PlayCorrectOrWrong(bool correct)
    {
        AudioManager.Instance?.PlaySFX(correct ? "CorrectSFX" : "WrongSFX");
    }

    static string TitleCaseVoiceName(string c)
    {
        if (string.IsNullOrEmpty(c)) return null;
        return char.ToUpper(c[0]) + c.Substring(1).ToLowerInvariant() + "Voice";
    }

    float GetTargetDuration()
    {
        switch (state.currentPhase) {
            case GamePhase.Phase2: return config.phase2TargetDuration;
            case GamePhase.Phase3: return config.phase3TargetDuration;
            default: return config.phase1TargetDuration;
        }
    }

    // --- shuffle ---

    void UpdateShuffleCo()
    {
        Kill(ref _shuffleCo);
        float interval = GetShuffleInterval();
        if (interval > 0f)
            _shuffleCo = StartCoroutine(ShuffleLoop(interval));
    }

    IEnumerator ShuffleLoop(float interval)
    {
        while (state.appState == AppState.Playing)
        {
            yield return new WaitForSeconds(interval);
            if (state.appState == AppState.Playing)
            {
                gridManager.ShuffleTilePositions();
                RecordReplaySnapshot("tiles_shuffled");
            }
        }
    }

    float GetShuffleInterval()
    {
        switch (state.currentPhase) {
            case GamePhase.Phase1: return config.phase1ShuffleInterval;
            case GamePhase.Phase2: return config.phase2ShuffleInterval;
            case GamePhase.Phase3: return config.phase3ShuffleInterval;
            default: return 0f;
        }
    }

    // --- tile step ---

    public void OnTileStepped(ChromaHuntTile tile)
    {
        if (state.appState != AppState.Playing) return;
        if (tile == null || tile.ColorIndex == -1) return;
        if (StepsIgnored) return;

        bool correct = tile.ColorIndex == state.targetColorIndex;

        if (correct)
        {
            state.score += config.correctScore;
            state.totalCorrect++;
            state.correctStreak++;
            if (state.correctStreak > state.bestStreak)
                state.bestStreak = state.correctStreak;

            PlayCorrectOrWrong(true);
            gridManager.FlashTile(tile.TileIndex);
            gridManager.RespawnTile(tile, state.targetColorIndex);
            OnStreakChanged?.Invoke(state.correctStreak);

            bool bonus = state.correctStreak % config.bonusEvery == 0;
            int pts = config.correctScore;
            if (bonus) {
                state.score += config.bonusScore;
                pts += config.bonusScore;
            }

            OnScoreChanged?.Invoke(state.score);
            RecordReplayStep(tile.TileIndex, state.targetColorIndex, true, pts, bonus);
            OnTileStepResult?.Invoke(pts, true);
            if (bonus) OnBonusAwarded?.Invoke(pts);
            OnAccuracyChanged?.Invoke(state.CorrectPercent);
        }
        else
        {
            state.score = Mathf.Max(0, state.score - config.wrongPenalty);
            state.totalWrong++;
            state.correctStreak = 0;

            PlayCorrectOrWrong(false);

            int wrongColor = tile.ColorIndex;
            gridManager.FlashTile(tile.TileIndex);
            gridManager.RespawnTile(tile, wrongColor);

            OnStreakChanged?.Invoke(0);
            OnScoreChanged?.Invoke(state.score);
            RecordReplayStep(tile.TileIndex, wrongColor, false, -config.wrongPenalty, false);
            OnTileStepResult?.Invoke(-config.wrongPenalty, false);
            OnAccuracyChanged?.Invoke(state.CorrectPercent);
        }
    }

    // --- end / restart ---

    void EndGame()
    {
        StopAllCo();
        // Block all game-over tile taps until UI paints buttons and arms via ResetGameOverDelay.
        // Prevents mid-sequence taps from firing during the result-video / tier-voice window.
        _gameOverReadyTime = float.MaxValue;

        int high = PlayerPrefs.GetInt("ChromaHunt_HighScore", 0);
        if (state.score > high) { PlayerPrefs.SetInt("ChromaHunt_HighScore", state.score); PlayerPrefs.Save(); }

        SetState(AppState.GameOver);
        // tier voice fires from UI side, paired with the result video so EndVoice owns time-up alone
    }

    public int GetHighScore() => PlayerPrefs.GetInt("ChromaHunt_HighScore", 0);

    // exposed so UI can fire tier voice alongside the result video
    public string GetTierVoiceKey()
    {
        if (state.score >= config.highScoreThreshold) return "HighVoice";
        if (state.score >= config.midScoreThreshold) return "MidVoice";
        return "LowVoice";
    }

    public void RestartGame()
    {
        StopAllCo();
        state.ResetScores();
        SetState(AppState.Setup);
        Kill(ref _menuMusicCo);
        _menuMusicCo = StartCoroutine(FadeToMenuMusic(0.5f, 1f));
    }

    // Tablet RESTART (Pilot 2026-11-04): play again straight into Ready / gameplay, no Start page.
    // The tablet's START PAGE button (TakeHome) does what the floor Restart did.
    // On-screen Restart on the Game Over panel = the tablet RESTART in solo (Pilot 2026-11-04)
    public void RestartFromScreen()
    {
        if (ChromaHuntNetwork.IsNetworked) { HandleGameOverButton(0); return; }   // multiplayer keeps its own restart
        if (state.appState == AppState.GameOver && Time.time >= _gameOverReadyTime) TabletQuickRestart();
    }

    void TabletQuickRestart()
    {
        if (ui != null) ui.BeginQuickRestart();
        else { ExitToSetupForReplay(); StartGame(); }
    }

    // ExitToSetup for a replay: the countdown follows straight away
    public void ExitToSetupForReplay()
    {
        ExitToSetup();
        if (_menuMusicCo != null) { StopCoroutine(_menuMusicCo); _menuMusicCo = null; }
    }

    public void ExitToSetup()
    {
        StopAllCo();
        state.ResetScores();
        SetState(AppState.Setup);
        InitFloorForSetup();
        Kill(ref _menuMusicCo);
        _menuMusicCo = StartCoroutine(FadeToMenuMusic(0.5f, 1f));
    }

    IEnumerator FadeToMenuMusic(float fadeOut, float fadeIn)
    {
        AudioManager.Instance?.FadeOutMusic(fadeOut);
        yield return new WaitForSeconds(fadeOut + 0.05f);
        AudioManager.Instance?.FadeInMusic("ColorMenu", fadeIn);
        _menuMusicCo = null;
    }

    public void ResetGameOverDelay()
    {
        _gameOverReadyTime = Time.time + config.gameOverButtonDelay;
    }

    public void HandleGameOverButton(int btn)
    {
        if (state.appState != AppState.GameOver
            || Time.time < _gameOverReadyTime || btn != 0) return;
        // Route through the UI fade so the floor tile uses the same transition as the on-screen button.
        if (ui != null) ui.BeginExitToSetup();
        else ExitToSetup();
    }

    void StopAllCo()
    {
        Kill(ref _timerCo);
        Kill(ref _targetCo);
        Kill(ref _shuffleCo);
    }

    void Kill(ref Coroutine co)
    {
        if (co != null) { StopCoroutine(co); co = null; }
    }

    public static string FormatTime(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60:D2}:{s % 60:D2}";
    }

    // --- net setters (ChromaHuntNetwork only) ---

    public void Net_SetAppState(AppState s)
    {
        if (state.appState == s) return;
        SetState(s);
    }

    public void Net_SetPhase(GamePhase p)
    {
        if (state.currentPhase == p) return;
        state.currentPhase = p;
        RecordReplaySnapshot("phase_changed");
        OnPhaseChanged?.Invoke(p);
    }

    bool _clientStroopIntroPlayed;

    public void Net_SetTimer(float t)
    {
        state.timeLeft = t;
        OnTimeChanged?.Invoke(t);
    }

    public void Net_SetTargetTime(float t)
    {
        state.targetTimeLeft = t;
        OnTargetTimeChanged?.Invoke(t);
    }

    public void Net_SetTargetColor(int idx, bool isStroop, int stroopIdx)
    {
        EndReplayTarget("next_target");
        state.targetColorIndex = idx;
        state.isStroopRound = isStroop;
        state.stroopDisplayColorIndex = stroopIdx;
        BeginReplayTarget();
        OnTargetColorChanged?.Invoke(idx, isStroop, stroopIdx);
        // mirror the host's Phase 3 intro: first stroop target plays StroopVoice instead of
        // NextVoice (the host pauses the clock; the client just follows the synced timer)
        if (isStroop && !_clientStroopIntroPlayed)
        {
            _clientStroopIntroPlayed = true;
            _pendingStroopAnnouncement = true;
            StartCoroutine(PlayStroopCue());
        }
        // mirror local path: stroop = lead-in only, first stroop round skips lead-in entirely
        bool suppressLead = _pendingStroopAnnouncement;
        if (suppressLead) _pendingStroopAnnouncement = false;
        StartPrompt(idx, false, !isStroop, !suppressLead, phase3: isStroop);
    }

    public void Net_SetScore(int score)
    {
        state.score = score;
        OnScoreChanged?.Invoke(score);
        OnAccuracyChanged?.Invoke(state.CorrectPercent);
    }

    public void Net_SetStreak(int streak)
    {
        state.correctStreak = streak;
        if (streak > state.bestStreak) state.bestStreak = streak;
        OnStreakChanged?.Invoke(streak);
    }

    public void Net_FireTileStepResult(int delta, bool correct)
    {
        if (correct) state.totalCorrect++; else state.totalWrong++;
        OnTileStepResult?.Invoke(delta, correct);
        OnAccuracyChanged?.Invoke(state.CorrectPercent);
    }

    public void Net_FireBonusAwarded(int totalDelta)
    {
        OnBonusAwarded?.Invoke(totalDelta);
    }

    // connection dropped mid-round — keep score, finish solo
    public void Net_ResumeAsSolo(int carryScore)
    {
        if (carryScore >= 0)
        {
            state.score = carryScore;
            OnScoreChanged?.Invoke(state.score);
        }
        // pure client had no loops running — spin them up now
        if (state.appState == AppState.Playing && _timerCo == null)
        {
            _timerCo = StartCoroutine(GameTimerRoutine());
            _targetCo = StartCoroutine(TargetColorLoop());
        }
    }
}
