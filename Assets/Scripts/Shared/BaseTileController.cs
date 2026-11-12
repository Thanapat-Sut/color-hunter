using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public abstract class BaseTileController : MonoBehaviour, IPointerDownHandler
{
    public int TileIndex { get; protected set; }
    public Vector2Int GridPosition { get; protected set; }

    protected SpriteRenderer _sr;
    protected Coroutine _flashCoroutine;
    protected Coroutine _pulseCoroutine;

    static Sprite _sharedWhiteSprite;

    const int WhiteSpriteSize = 4;
    const float PixelsPerUnit = 4f;
    const float HitFlashDuration = 0.2f;
    const float PulseSpeed = 3f;
    const float PulseMin = 0.3f;

    protected virtual void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null && _sr.sprite == null)
            _sr.sprite = GetOrCreateWhiteSprite();
    }

    public static Sprite GetOrCreateWhiteSprite()
    {
        if (_sharedWhiteSprite != null) return _sharedWhiteSprite;

        var tex = new Texture2D(WhiteSpriteSize, WhiteSpriteSize);
        int count = WhiteSpriteSize * WhiteSpriteSize;
        var pixels = new Color[count];
        for (int i = 0; i < count; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Point;

        _sharedWhiteSprite = Sprite.Create(
            tex,
            new Rect(0, 0, WhiteSpriteSize, WhiteSpriteSize),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit
        );
        _sharedWhiteSprite.name = "WhiteSquare";
        return _sharedWhiteSprite;
    }

    protected void InitBase(int index, Vector2Int gridPos)
    {
        TileIndex = index;
        GridPosition = gridPos;
        _sr = GetComponent<SpriteRenderer>();
    }

    public void SetColor(Color color) => _sr.color = color;
    public Color GetColor() => _sr.color;

    protected void StopFlash()
    {
        if (_flashCoroutine != null) { StopCoroutine(_flashCoroutine); _flashCoroutine = null; }
    }

    protected void StopPulse()
    {
        if (_pulseCoroutine != null) { StopCoroutine(_pulseCoroutine); _pulseCoroutine = null; }
    }

    protected void StopAllAnimations()
    {
        StopFlash();
        StopPulse();
    }

    public virtual void StopAllTileAnimations() => StopAllAnimations();

    public virtual void PlayHitAnimation()
    {
        StopFlash();
        _flashCoroutine = StartCoroutine(HitFlashRoutine());
    }

    IEnumerator HitFlashRoutine()
    {
        Color orig = _sr.color;
        _sr.color = Color.white;

        float t = 0f;
        while (t < HitFlashDuration) {
            t += Time.deltaTime;
            _sr.color = Color.Lerp(Color.white, orig, t / HitFlashDuration);
            yield return null;
        }
        _sr.color = orig;
        _flashCoroutine = null;
    }

    public void Flash(Color flashColor, float duration)
    {
        StopFlash();
        _flashCoroutine = StartCoroutine(ParamFlashRoutine(flashColor, duration));
    }

    IEnumerator ParamFlashRoutine(Color flashColor, float duration)
    {
        Color orig = _sr.color;
        _sr.color = flashColor;

        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            _sr.color = Color.Lerp(flashColor, orig, elapsed / duration);
            yield return null;
        }
        _sr.color = orig;
        _flashCoroutine = null;
    }

    public virtual void SetWaitingPulse(Color pulseColor, Color baseColor)
    {
        StopPulse();
        _pulseCoroutine = StartCoroutine(WaitingPulseRoutine(pulseColor, baseColor));
    }

    public virtual void StopWaiting() => StopPulse();

    protected IEnumerator WaitingPulseRoutine(Color pulseColor, Color baseColor)
    {
        float t = 0f;
        while (true) {
            t += Time.deltaTime * PulseSpeed;
            float lerp = (Mathf.Sin(t) + 1f) / 2f;
            _sr.color = Color.Lerp(baseColor, pulseColor, Mathf.Lerp(PulseMin, 1f, lerp));
            yield return null;
        }
    }

    public abstract void OnPointerDown(PointerEventData eventData);
}
