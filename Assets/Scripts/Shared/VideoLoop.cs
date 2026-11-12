using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// looping video on a RawImage — plays on enable, pauses on disable
public class VideoLoop : MonoBehaviour
{
    [SerializeField] private RawImage display;
    [SerializeField] private VideoClip clip;
    // A keyed clip's RenderTexture is black until the first frame, which shows as a black box
    [SerializeField] private bool hideUntilFirstFrame;

    VideoPlayer _vp;
    RenderTexture _rt;
    bool _initialized;
    VideoClip _pendingLoop;
    Action _pendingLoopCallback;
    bool _watchingLoopPoint;
    bool _firstFrameShown;

    void Initialize()
    {
        if (_initialized || display == null || clip == null) return;

        _vp = gameObject.AddComponent<VideoPlayer>();
        _vp.playOnAwake = false;
        _vp.source = VideoSource.VideoClip;
        _vp.renderMode = VideoRenderMode.RenderTexture;
        _vp.isLooping = true;
        _vp.clip = clip;
        // Kill the audio path. Default audioOutputMode=Direct silent-fails on a UI
        // GameObject (no AudioListener) and pushes time into a race with DSP clock.
        _vp.audioOutputMode = VideoAudioOutputMode.None;
        _vp.controlledAudioTrackCount = 0;
        // GameTime locks playback to Time.deltaTime so this loop matches editor preview pacing
        // even if another VideoPlayer's Prepare hogs the decoder briefly.
        _vp.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        _vp.skipOnDrop = false;

        _rt = new RenderTexture((int)clip.width, (int)clip.height, 0);
        _vp.targetTexture = _rt;
        display.texture = _rt;
        _initialized = true;
    }

    void Awake()
    {
        // Pre-prepare at scene load so BG isn't fighting the intro VideoPlayer
        // for decoder capacity when both spin up in the same frame.
        Initialize();
        if (_vp != null) _vp.Prepare();
    }

    void OnEnable()
    {
        Initialize();
        if (_vp == null) return;
        if (!_vp.isPrepared) _vp.Prepare();
        if (hideUntilFirstFrame && !_firstFrameShown)
        {
            display.enabled = false;
            _vp.sendFrameReadyEvents = true;
            _vp.frameReady -= OnFirstFrame;
            _vp.frameReady += OnFirstFrame;
        }
        else display.enabled = true;
        _vp.Play();
    }

    void OnFirstFrame(VideoPlayer vp, long frame)
    {
        _firstFrameShown = true;
        vp.frameReady -= OnFirstFrame;
        vp.sendFrameReadyEvents = false;
        if (display != null) display.enabled = true;
    }

    void OnDisable()
    {
        if (_vp != null) _vp.Pause();
    }

    void OnDestroy()
    {
        if (_vp != null) Destroy(_vp);
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
    }

    // Swap to a different looping clip at runtime. Safe to call repeatedly with the same clip.
    public void SwapClip(VideoClip newClip)
    {
        if (newClip == null) return;
        Initialize();
        if (_vp == null) return;

        DetachLoopPointWatcher();
        _pendingLoop = null;
        _pendingLoopCallback = null;

        if (_vp.clip == newClip)
        {
            _vp.isLooping = true;
            if (gameObject.activeInHierarchy && display != null && display.enabled && !_vp.isPlaying)
                _vp.Play();
            return;
        }

        _vp.Stop();
        _vp.isLooping = true;
        _vp.clip = newClip;
        if (!_vp.isPrepared) _vp.Prepare();
        if (gameObject.activeInHierarchy && display != null && display.enabled)
            _vp.Play();
    }

    // Play a transition clip once, then automatically swap to `loop` and loop it.
    // If either clip is null this falls back to SwapClip semantics on the non-null one.
    // Optional `onLoopStarted` fires the moment the loop clip begins playing — use it
    // to defer UI (e.g. game-over text reveal) until the intro-to-loop handoff lands.
    public void PlayThenLoop(VideoClip transition, VideoClip loop, Action onLoopStarted = null)
    {
        if (transition == null) { SwapClip(loop); onLoopStarted?.Invoke(); return; }
        if (loop == null) { SwapClip(transition); return; }
        Initialize();
        if (_vp == null) return;

        DetachLoopPointWatcher();
        _pendingLoop = loop;
        _pendingLoopCallback = onLoopStarted;

        _vp.Stop();
        _vp.isLooping = false;
        _vp.clip = transition;
        _vp.loopPointReached += OnTransitionFinished;
        _watchingLoopPoint = true;
        if (!_vp.isPrepared) _vp.Prepare();
        if (gameObject.activeInHierarchy && display != null && display.enabled)
            _vp.Play();
    }

    void OnTransitionFinished(VideoPlayer vp)
    {
        DetachLoopPointWatcher();
        var next = _pendingLoop;
        var callback = _pendingLoopCallback;
        _pendingLoop = null;
        _pendingLoopCallback = null;
        if (next == null) return;
        // Swap directly without re-invoking DetachLoopPointWatcher paths — we've
        // already cleared the callback above.
        _vp.Stop();
        _vp.isLooping = true;
        _vp.clip = next;
        if (!_vp.isPrepared) _vp.Prepare();
        if (gameObject.activeInHierarchy && display != null && display.enabled)
            _vp.Play();
        callback?.Invoke();
    }

    void DetachLoopPointWatcher()
    {
        if (_watchingLoopPoint && _vp != null)
            _vp.loopPointReached -= OnTransitionFinished;
        _watchingLoopPoint = false;
    }
}
