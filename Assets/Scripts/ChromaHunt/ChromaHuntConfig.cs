using UnityEngine;

[CreateAssetMenu(fileName = "ChromaHuntConfig", menuName = "CasualGame/ChromaHuntConfig")]
public class ChromaHuntConfig : BaseGameConfig
{
    [Header("Timing")]
    public int gameDuration = 90;

    [Header("Scoring")]
    public int correctScore = 10;
    public int wrongPenalty = 5;
    public int bonusEvery = 10;
    public int bonusScore = 10;

    [Header("Result Tier SFX Thresholds")]
    [Tooltip("Score >= this plays HighSFX on game over. Below midScoreThreshold plays LowSFX.")]
    public int highScoreThreshold = 200;
    public int midScoreThreshold = 100;

    [Header("SFX Timing")]
    [Tooltip("Extra gap after lead-in (Startgame/Next) before color SFX. 0 = back-to-back.")]
    public float promptSfxGap = 0f;
    [Tooltip("Extra gap after 30SFX before StroopSFX. 0 = back-to-back.")]
    public float stroopSfxGap = 0f;

    [Header("Game Colors")]
    public ChromaHuntColorEntry[] gameColors = new ChromaHuntColorEntry[]
    {
        new ChromaHuntColorEntry { colorName = "RED",    thaiName = "สีแดง",   color = new Color(1f, 0.2f, 0.2f) },
        new ChromaHuntColorEntry { colorName = "GREEN",  thaiName = "สีเขียว", color = new Color(0.2f, 1f, 0.2f) },
        new ChromaHuntColorEntry { colorName = "BLUE",   thaiName = "สีฟ้า",   color = new Color(0.3f, 0.5f, 1f) },
        new ChromaHuntColorEntry { colorName = "YELLOW", thaiName = "สีเหลือง", color = new Color(1f, 1f, 0.2f) },
        new ChromaHuntColorEntry { colorName = "PURPLE", thaiName = "สีม่วง",  color = new Color(0.8f, 0.2f, 1f) },
        new ChromaHuntColorEntry { colorName = "ORANGE", thaiName = "สีส้ม",   color = new Color(1f, 0.6f, 0.1f) },
    };

    // phase 1 (0-30s) starter — limited color pool
    [Header("Phase 1")]
    public float phase1End = 30f;
    public float phase1TargetDuration = 10f;
    public float phase1ShuffleInterval = 0f;
    [Tooltip("Colors visible during Phase 1: target + (N-1) distractors. Clamped to gameColors length.")]
    [Range(2, 8)]
    public int phase1ColorCount = 2;

    // phase 2 (30-120s) ramp colors up to full pool + faster shuffle
    [Header("Phase 2")]
    public float phase2End = 120f;
    public float phase2TargetDuration = 6f;
    public float phase2ShuffleInterval = 3f;
    [Tooltip("Seconds between adding one new color to the pool during Phase 2. 0 = jump straight to full pool at Phase 2 start.")]
    public float phase2ColorAddInterval = 20f;

    // phase 3 (last 60s, 120-180s) stroop + frequent shuffle
    [Header("Phase 3")]
    public float phase3TargetDuration = 4f;
    public float phase3ShuffleInterval = 2f;
    [Tooltip("Phase 3 has no color voice, so steps are ignored for this fixed window after each target change instead of until the voice ends (CH-01).")]
    public float phase3PromptIframe = 1f;

    [Header("Voice Cues")]
    [Tooltip("One-shot 30sVoice trigger. Fires when timeLeft first drops to this value.")]
    public float warnVoiceTimeLeft = 30f;

    [Header("Grid")]
    [Range(0.1f, 1f)]
    public float activeTileRatio = 0.6f;
}

[System.Serializable]
public class ChromaHuntColorEntry
{
    public string colorName;
    public string thaiName;
    public Color color;
    [Tooltip("Colour of the name text on the colour cloud. Alpha 0 = use Color (the floor tile colour).")]
    public Color textColor;

    [Header("Sprites")]
    public Sprite characterSprite;

    [Header("Monster Video")]
    [Tooltip("Looping monster shown as the target. Leave empty to show characterSprite instead.")]
    public UnityEngine.Video.VideoClip monsterClip;
    [Tooltip("Chroma-key material matching this clip's background colour.")]
    public Material monsterMaterial;

    [Header("Voice")]
    [Tooltip("AudioManager Voice key. Leave empty to auto-derive from colorName ('RED' → 'RedVoice').")]
    public string sfxName;
}
