using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class Sound
{
    public string name;
    public AudioClip clip;
    [Range(0f, 1f)]
    public float volume = 1f;
    [Range(0.1f, 3f)]
    public float pitch = 1f;
    public bool loop;
}

// voice entry — adds the English clip (music/SFX stay language-neutral)
[System.Serializable]
public class VoiceSound : Sound
{
    [Tooltip("English version. Played when GameLanguage is EN; empty = falls back to clip.")]
    public AudioClip clipEN;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource voiceSource;
    [Tooltip("Secondary voice channel (Source B) — used by dual-lane MP games (e.g. TerritoryWars) to keep left/right PC voice lines independent. Auto-created if left blank.")]
    [SerializeField] private AudioSource voiceSourceB;

    [Header("Audio Clips")]
    [SerializeField] private Sound[] musicClips;
    [SerializeField] private Sound[] sfxClips;
    [SerializeField] private VoiceSound[] voiceClips;

    [Header("Settings")]
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private float musicVolume = 0.5f;
    [SerializeField] private float sfxVolume = 1f;
    [SerializeField] private float voiceVolume = 1f;

    [Header("SFX Ducking")]
    [Tooltip("Music volume multiplier while an SFX is playing (1.0 = no ducking, 0.0 = fully muted). Music returns to original after the SFX finishes.")]
    [Range(0f, 1f)]
    [SerializeField] private float duckVolumeFactor = 0.3f;
    [Tooltip("Time (sec) to fade music DOWN when an SFX starts.")]
    [SerializeField] private float duckAttackTime = 0.08f;
    [Tooltip("Time (sec) to fade music BACK UP after the last overlapping SFX ends.")]
    [SerializeField] private float duckReleaseTime = 0.3f;

    [Header("Voice → SFX Ducking")]
    [Tooltip("SFX channel volume multiplier while voice is speaking. 1.0 = no duck.")]
    [Range(0f, 1f)]
    [SerializeField] private float voiceDuckSfxFactor = 0.3f;

    private Dictionary<string, Sound> musicDictionary;
    private Dictionary<string, Sound> sfxDictionary;
    private Dictionary<string, VoiceSound> voiceDictionary;
    private Coroutine fadeCoroutine;
    private Coroutine _duckCo;
    private float _duckUntil;
    private Coroutine _duckSfxCo;
    private float _duckSfxUntil;
    // explicit "voice busy" reservation — covers gaps inside a voice sequence (e.g. NextVoice → gap → ColorVoice)
    private float _voiceLockUntil;
    // The "intended" music volume to release back to. Tracked separately from musicSource.volume
    // because a fade-in may still be ramping when ducking kicks in — snapshotting the live
    // musicSource.volume would catch a near-zero value and the release would leave the music silent.
    private float _musicTargetVolume;

    private const string MASTER_VOLUME_KEY = "MasterVolume";
    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string SFX_VOLUME_KEY = "SFXVolume";
    private const string VOICE_VOLUME_KEY = "VoiceVolume";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAudioSources();
            InitializeDictionaries();
            LoadVolumeSettings();
        }
        else
        {
            // merge clips from this scene into the persistent instance
            Instance.MergeClips(musicClips, sfxClips, voiceClips);
            Destroy(gameObject);
            return;
        }
    }

    private void MergeClips(Sound[] newMusic, Sound[] newSfx, VoiceSound[] newVoice)
    {
        if (newMusic != null)
            foreach (Sound s in newMusic)
                if (!musicDictionary.ContainsKey(s.name))
                    musicDictionary.Add(s.name, s);

        if (newSfx != null)
            foreach (Sound s in newSfx)
                if (!sfxDictionary.ContainsKey(s.name))
                    sfxDictionary.Add(s.name, s);

        if (newVoice != null)
            foreach (VoiceSound s in newVoice)
                if (!voiceDictionary.ContainsKey(s.name))
                    voiceDictionary.Add(s.name, s);
    }

    private void InitializeAudioSources()
    {
        if (musicSource == null)
        {
            GameObject musicObj = new GameObject("MusicSource");
            musicObj.transform.SetParent(transform);
            musicSource = musicObj.AddComponent<AudioSource>();
            musicSource.loop = true;
        }

        if (sfxSource == null)
        {
            GameObject sfxObj = new GameObject("SFXSource");
            sfxObj.transform.SetParent(transform);
            sfxSource = sfxObj.AddComponent<AudioSource>();
        }

        // Voice sources are opt-in per scene: if the Inspector field is left empty, don't
        // auto-create one — leaves voiceSource/voiceSourceB null so PlayVoice/PlayVoiceB
        // no-op silently. Games without voice work stay quiet without needing a dummy
        // GameObject on the AudioManager (Pilot 2026-07-06).
    }

    private void InitializeDictionaries()
    {
        musicDictionary = new Dictionary<string, Sound>();
        sfxDictionary = new Dictionary<string, Sound>();
        voiceDictionary = new Dictionary<string, VoiceSound>();

        if (musicClips != null)
            foreach (Sound s in musicClips)
                if (!musicDictionary.ContainsKey(s.name))
                    musicDictionary.Add(s.name, s);

        if (sfxClips != null)
            foreach (Sound s in sfxClips)
                if (!sfxDictionary.ContainsKey(s.name))
                    sfxDictionary.Add(s.name, s);

        if (voiceClips != null)
            foreach (VoiceSound s in voiceClips)
                if (!voiceDictionary.ContainsKey(s.name))
                    voiceDictionary.Add(s.name, s);
    }

    public void PlayMusic(string name)
    {
        if (musicDictionary.TryGetValue(name, out Sound sound))
        {
            musicSource.clip = sound.clip;
            _musicTargetVolume = sound.volume * musicVolume * masterVolume;
            musicSource.volume = _musicTargetVolume;
            musicSource.pitch = sound.pitch;
            musicSource.loop = sound.loop;
            musicSource.Play();
        }
        else
        {
            Debug.LogWarning($"Music '{name}' not found!");
        }
    }

    // Sample-accurate scheduled playback for rhythm-game timing. `dspStartTime` is an absolute
    // AudioSettings.dspTime value in the future — the audio card will start the clip exactly at
    // that DSP tick (unlike Play() which starts "soon" with unspecified latency). Volume is set
    // to the sound's normal target immediately (no fade) so the timing anchor lines up with the
    // audible attack, not a ramp. Returns the actual DSP start time (== dspStartTime), or -1 if
    // the sound name isn't in the music dictionary.
    public double PlayMusicScheduled(string name, double dspStartTime)
    {
        if (!musicDictionary.TryGetValue(name, out Sound sound))
        {
            Debug.LogWarning($"Music '{name}' not found!");
            return -1.0;
        }
        if (fadeCoroutine != null) { StopCoroutine(fadeCoroutine); fadeCoroutine = null; }
        _musicTargetVolume = sound.volume * musicVolume * masterVolume;
        musicSource.clip = sound.clip;
        musicSource.volume = _musicTargetVolume;
        // Force pitch to 1.0 for rhythm-game accuracy — Sound entries may carry a custom pitch
        // (musical detune, preview effect), but the chart's `t` values assume playback at 1.0x.
        // Any deviation makes gameTime (real seconds) diverge from audio position (song seconds).
        musicSource.pitch = 1f;
        // Force loop off — a rhythm song should end at its duration; loop wraps timeSamples back
        // to 0, which the soft-snap would then read as a giant negative diff and hard-reset the
        // anchor into the past.
        musicSource.loop = false;
        musicSource.Stop();
        musicSource.PlayScheduled(dspStartTime);
        return dspStartTime;
    }

    // Sample-accurate playback position for rhythm-game timing. `timeSamples / frequency` is
    // more precise than AudioSource.time (which rounds to the current audio buffer). Returns 0
    // if there's no clip loaded. Value freezes while paused, so callers must gate their soft-
    // snap on IsMusicPlaying().
    public double GetMusicSongPositionSec()
    {
        if (musicSource == null || musicSource.clip == null) return 0.0;
        return (double)musicSource.timeSamples / (double)musicSource.clip.frequency;
    }

    public bool IsMusicPlaying() => musicSource != null && musicSource.isPlaying;

    public void StopMusic()
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        musicSource.Stop();
    }

    public void FadeInMusic(string name, float fadeDuration = 1f)
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeInCoroutine(name, fadeDuration));
    }

    public void FadeOutMusic(float fadeDuration = 1f, bool stopAfterFade = true)
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(FadeOutCoroutine(fadeDuration, stopAfterFade));
    }

    private IEnumerator FadeInCoroutine(string name, float duration)
    {
        if (!musicDictionary.TryGetValue(name, out Sound sound))
        {
            Debug.LogWarning($"Music '{name}' not found!");
            yield break;
        }

        float targetVolume = sound.volume * musicVolume * masterVolume;
        _musicTargetVolume = targetVolume;

        musicSource.clip = sound.clip;
        musicSource.volume = 0f;
        musicSource.pitch = sound.pitch;
        musicSource.loop = sound.loop;
        musicSource.Play();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / duration);
            yield return null;
        }

        musicSource.volume = targetVolume;
        fadeCoroutine = null;
    }

    private IEnumerator FadeOutCoroutine(float duration, bool stopAfterFade)
    {
        float startVolume = musicSource.volume;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
            yield return null;
        }

        musicSource.volume = 0f;
        if (stopAfterFade)
            musicSource.Stop();
        fadeCoroutine = null;
    }

    public void PauseMusic()
    {
        musicSource.Pause();
    }

    public void ResumeMusic()
    {
        musicSource.UnPause();
    }

    // step playback position back N seconds (clamped at 0). Use while music is paused.
    public void RewindMusic(float seconds)
    {
        if (musicSource == null || musicSource.clip == null) return;
        musicSource.time = Mathf.Max(0f, musicSource.time - seconds);
    }

    public void PlaySFX(string name)
    {
        if (sfxDictionary.TryGetValue(name, out Sound sound))
        {
            sfxSource.pitch = sound.pitch;
            sfxSource.PlayOneShot(sound.clip, sound.volume * sfxVolume * masterVolume);
            DuckMusicFor(sound.clip != null ? sound.clip.length / Mathf.Max(0.01f, sound.pitch) : 0f);
        }
        else
        {
            Debug.LogWarning($"SFX '{name}' not found!");
        }
    }

    // Temporarily dim the music while an SFX plays. Overlapping SFX extend the duck window;
    // music releases back to its TARGET volume (set by PlayMusic/FadeInMusic) after the last SFX
    // finishes. We track the target separately from musicSource.volume so a fade-in mid-ramp
    // doesn't get snapshotted as 0 and leave the music silent on release.
    public void DuckMusicFor(float duration)
    {
        if (musicSource == null || duration <= 0f) return;
        if (!musicSource.isPlaying) return;
        if (_musicTargetVolume <= 0f) return;

        float endTime = Time.unscaledTime + duration;
        if (endTime > _duckUntil) _duckUntil = endTime;

        if (_duckCo == null)
            _duckCo = StartCoroutine(DuckRoutine());
    }

    private IEnumerator DuckRoutine()
    {
        // Cancel any in-flight FadeIn/FadeOut so it doesn't fight us setting musicSource.volume.
        if (fadeCoroutine != null) { StopCoroutine(fadeCoroutine); fadeCoroutine = null; }

        float toVol = _musicTargetVolume * Mathf.Clamp01(duckVolumeFactor);

        while (musicSource != null)
        {
            // attack — fade down to ducked level (relative to TARGET, not current live volume)
            float fromVol = musicSource.volume;
            float elapsed = 0f;
            float attack = Mathf.Max(0.001f, duckAttackTime);
            while (elapsed < attack && musicSource != null)
            {
                elapsed += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(fromVol, toVol, elapsed / attack);
                yield return null;
            }
            if (musicSource != null) musicSource.volume = toVol;

            // hold until the last overlapping SFX finishes
            while (Time.unscaledTime < _duckUntil)
                yield return null;

            // release — fade back up to the music's target volume. Abort & re-duck if a new SFX
            // extends _duckUntil while we're releasing (so back-to-back SFX hold the duck).
            float curVol = musicSource != null ? musicSource.volume : toVol;
            elapsed = 0f;
            float release = Mathf.Max(0.001f, duckReleaseTime);
            bool aborted = false;
            while (elapsed < release && musicSource != null)
            {
                if (Time.unscaledTime < _duckUntil) { aborted = true; break; }
                elapsed += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(curVol, _musicTargetVolume, elapsed / release);
                yield return null;
            }
            if (aborted) continue;

            if (musicSource != null) musicSource.volume = _musicTargetVolume;
            break;
        }
        _duckCo = null;
    }

    public void StopSFX()
    {
        sfxSource.Stop();
    }

    // Voice channel — exclusive playback (new voice cancels previous).
    // Use for character lines, announcements, anything you don't want stacking.
    // voice clip for the current GameLanguage — clipEN when English and assigned, else clip
    static AudioClip VoiceClip(VoiceSound s) =>
        GameLanguage.IsEnglish && s.clipEN != null ? s.clipEN : s.clip;

    public void PlayVoice(string name)
    {
        if (voiceSource == null) return;   // no voice source wired in scene — silent no-op
        if (voiceDictionary != null && voiceDictionary.TryGetValue(name, out VoiceSound sound))
        {
            voiceSource.Stop();
            voiceSource.clip = VoiceClip(sound);
            voiceSource.volume = sound.volume * voiceVolume * masterVolume;
            voiceSource.pitch = sound.pitch;
            voiceSource.loop = false;
            voiceSource.Play();
            AudioClip clip = VoiceClip(sound);
            float dur = clip != null ? clip.length / Mathf.Max(0.01f, sound.pitch) : 0f;
            DuckMusicFor(dur);
            DuckSfxFor(dur);
        }
        else
        {
            Debug.LogWarning($"Voice '{name}' not found!");
        }
    }

    // Secondary voice channel — used by dual-lane MP (e.g. TerritoryWars) to give
    // left and right PCs independent voice tracks. Same ducking + volume rules as Source A.
    public void PlayVoiceB(string name)
    {
        if (voiceSourceB == null) return;   // secondary voice source not wired — silent no-op
        if (voiceDictionary != null && voiceDictionary.TryGetValue(name, out VoiceSound sound))
        {
            voiceSourceB.Stop();
            voiceSourceB.clip = VoiceClip(sound);
            voiceSourceB.volume = sound.volume * voiceVolume * masterVolume;
            voiceSourceB.pitch = sound.pitch;
            voiceSourceB.loop = false;
            voiceSourceB.Play();
            AudioClip clip = VoiceClip(sound);
            float dur = clip != null ? clip.length / Mathf.Max(0.01f, sound.pitch) : 0f;
            DuckMusicFor(dur);
            DuckSfxFor(dur);
        }
        else
        {
            Debug.LogWarning($"VoiceB '{name}' not found!");
        }
    }

    // Dim the SFX channel while voice is speaking — mirrors DuckMusicFor.
    public void DuckSfxFor(float duration)
    {
        if (sfxSource == null || duration <= 0f) return;
        float endTime = Time.unscaledTime + duration;
        if (endTime > _duckSfxUntil) _duckSfxUntil = endTime;
        if (_duckSfxCo == null)
            _duckSfxCo = StartCoroutine(DuckSfxRoutine());
    }

    private IEnumerator DuckSfxRoutine()
    {
        float toVol = Mathf.Clamp01(voiceDuckSfxFactor);

        while (sfxSource != null)
        {
            float fromVol = sfxSource.volume;
            float elapsed = 0f;
            float attack = Mathf.Max(0.001f, duckAttackTime);
            while (elapsed < attack && sfxSource != null)
            {
                elapsed += Time.unscaledDeltaTime;
                sfxSource.volume = Mathf.Lerp(fromVol, toVol, elapsed / attack);
                yield return null;
            }
            if (sfxSource != null) sfxSource.volume = toVol;

            while (Time.unscaledTime < _duckSfxUntil)
                yield return null;

            float curVol = sfxSource != null ? sfxSource.volume : toVol;
            elapsed = 0f;
            float release = Mathf.Max(0.001f, duckReleaseTime);
            bool aborted = false;
            while (elapsed < release && sfxSource != null)
            {
                if (Time.unscaledTime < _duckSfxUntil) { aborted = true; break; }
                elapsed += Time.unscaledDeltaTime;
                sfxSource.volume = Mathf.Lerp(curVol, 1f, elapsed / release);
                yield return null;
            }
            if (aborted) continue;

            if (sfxSource != null) sfxSource.volume = 1f;
            break;
        }
        _duckSfxCo = null;
    }

    public void StopVoice()
    {
        if (voiceSource != null) voiceSource.Stop();
        if (voiceSourceB != null) voiceSourceB.Stop();
    }

    public bool IsVoicePlaying()
    {
        bool live = (voiceSource != null && voiceSource.isPlaying)
                 || (voiceSourceB != null && voiceSourceB.isPlaying);
        return live || Time.unscaledTime < _voiceLockUntil;
    }

    // Reserve the voice channel as "busy" for the given duration even when no clip is currently
    // playing. Use when a voice sequence has internal gaps (e.g. lead → gap → color) so callers
    // checking IsVoicePlaying don't slip into the silent gap.
    public void LockVoiceFor(float duration)
    {
        if (duration <= 0f) return;
        float endTime = Time.unscaledTime + duration;
        if (endTime > _voiceLockUntil) _voiceLockUntil = endTime;
    }

    public float GetVoiceClipLength(string name)
    {
        if (voiceDictionary != null && voiceDictionary.TryGetValue(name, out VoiceSound s) && VoiceClip(s) != null)
            return VoiceClip(s).length / Mathf.Max(0.01f, s.pitch);
        return 0f;
    }

    // raw voice source state, ignores the LockVoiceFor reservation. Use for tight clip-end transitions.
    public bool IsVoiceClipLive() =>
        (voiceSource != null && voiceSource.isPlaying)
        || (voiceSourceB != null && voiceSourceB.isPlaying);

    // True only when a LockVoiceFor reservation is currently active. Use to
    // gate high-priority queued voices: they should wait for a locked voice
    // (30sVoice/EndVoice/etc.) but are free to interrupt any unlocked live clip.
    public bool IsVoiceLocked() => Time.unscaledTime < _voiceLockUntil;

    public void PlaySFXAtPosition(string name, Vector3 position)
    {
        if (sfxDictionary.TryGetValue(name, out Sound sound))
        {
            AudioSource.PlayClipAtPoint(sound.clip, position, sound.volume * sfxVolume * masterVolume);
        }
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateMusicVolume();
        SaveVolumeSettings();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        UpdateMusicVolume();
        SaveVolumeSettings();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        SaveVolumeSettings();
    }

    public void SetVoiceVolume(float volume)
    {
        voiceVolume = Mathf.Clamp01(volume);
        SaveVolumeSettings();
    }

    private void UpdateMusicVolume()
    {
        if (musicSource.clip != null && musicDictionary.TryGetValue(musicSource.clip.name, out Sound sound))
        {
            musicSource.volume = sound.volume * musicVolume * masterVolume;
        }
    }

    public float GetMasterVolume() => masterVolume;
    public float GetMusicVolume() => musicVolume;
    public float GetSFXVolume() => sfxVolume;
    public float GetVoiceVolume() => voiceVolume;

    // Runtime, non-persisting override for the music-duck factor. Used by BeatStomp to
    // disable ducking so BGM stays above SFX. Manager captures original + restores in OnDestroy.
    public float GetDuckVolumeFactor() => duckVolumeFactor;
    public void SetDuckVolumeFactor(float factor) => duckVolumeFactor = Mathf.Clamp01(factor);
    public float GetMusicClipLength() => musicSource != null && musicSource.clip != null ? musicSource.clip.length : 0f;

    public float GetSFXClipLength(string name)
    {
        if (sfxDictionary != null && sfxDictionary.TryGetValue(name, out Sound s) && s.clip != null)
            return s.clip.length / Mathf.Max(0.01f, s.pitch);
        return 0f;
    }

    private void SaveVolumeSettings()
    {
        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, masterVolume);
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, musicVolume);
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, sfxVolume);
        PlayerPrefs.SetFloat(VOICE_VOLUME_KEY, voiceVolume);
        PlayerPrefs.Save();
    }

    private void LoadVolumeSettings()
    {
        // Honor Inspector values when no saved override exists.
        if (PlayerPrefs.HasKey(MASTER_VOLUME_KEY)) masterVolume = PlayerPrefs.GetFloat(MASTER_VOLUME_KEY);
        if (PlayerPrefs.HasKey(MUSIC_VOLUME_KEY))  musicVolume  = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY);
        if (PlayerPrefs.HasKey(SFX_VOLUME_KEY))    sfxVolume    = PlayerPrefs.GetFloat(SFX_VOLUME_KEY);
        if (PlayerPrefs.HasKey(VOICE_VOLUME_KEY))  voiceVolume  = PlayerPrefs.GetFloat(VOICE_VOLUME_KEY);
    }
}
