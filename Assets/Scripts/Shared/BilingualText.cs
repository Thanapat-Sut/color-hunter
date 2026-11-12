using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Per-label Thai/English swapper. Attach to any GameObject with a
/// <see cref="TMP_Text"/>. Strings are authored in the Inspector; swap timing
/// is driven globally by <see cref="BilingualTextSync"/> so every bilingual
/// label flips together.
///
/// Swap animation pops the old text out (scale → 0, fast ease-in) then pops
/// the new text in with an elastic overshoot. Text content swaps at the
/// midpoint while the label is at scale 0. Toggle off via
/// <see cref="animate"/> for an instant cut.
/// </summary>
[DisallowMultipleComponent]
public class BilingualText : MonoBehaviour
{
    [Tooltip("Optional override. Leave empty to use the TMP_Text on this GameObject.")]
    [SerializeField] private TMP_Text targetText;

    [TextArea(1, 4)]
    [SerializeField] private string thai;

    [TextArea(1, 4)]
    [SerializeField] private string english;

    [Header("Pop Animation")]
    [SerializeField] private bool animate = true;

    [Tooltip("Time to pop the old text out before the swap.")]
    [SerializeField, Min(0f)] private float popOutDuration = 0.12f;

    [Tooltip("Time for the new text's elastic pop-in after the swap.")]
    [SerializeField, Min(0f)] private float popInDuration = 0.35f;

    Coroutine _animCo;
    Vector3 _baseScale;
    bool _baseCaptured;

    void Awake()
    {
        if (targetText == null) targetText = GetComponent<TMP_Text>();
        CaptureBase();
    }

    void OnEnable()
    {
        BilingualTextSync.OnChanged += OnSyncChanged;
        // Apply immediately without animation — we may be enabling mid-scene.
        ApplyInstant(BilingualTextSync.ShowEnglish);
    }

    void OnDisable()
    {
        BilingualTextSync.OnChanged -= OnSyncChanged;
        if (_animCo != null) { StopCoroutine(_animCo); _animCo = null; }
        RestoreScale();
    }

    void CaptureBase()
    {
        if (_baseCaptured) return;
        _baseScale = transform.localScale;
        if (_baseScale == Vector3.zero) _baseScale = Vector3.one;
        _baseCaptured = true;
    }

    void RestoreScale()
    {
        if (_baseCaptured) transform.localScale = _baseScale;
    }

    void OnSyncChanged(bool showEnglish)
    {
        if (!animate || (popOutDuration <= 0f && popInDuration <= 0f))
        {
            ApplyInstant(showEnglish);
            return;
        }
        if (_animCo != null) StopCoroutine(_animCo);
        _animCo = StartCoroutine(PopSwap(showEnglish));
    }

    void ApplyInstant(bool showEnglish)
    {
        if (targetText == null) return;
        targetText.text = showEnglish ? english : thai;
        RestoreScale();
    }

    IEnumerator PopSwap(bool showEnglish)
    {
        if (targetText == null) yield break;
        CaptureBase();

        // --- pop out: scale → 0, ease-in cubic (snappy disappear) ---
        if (popOutDuration > 0f)
        {
            float t = 0f;
            while (t < popOutDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / popOutDuration);
                float k = u * u * u; // ease-in cubic
                transform.localScale = _baseScale * (1f - k);
                yield return null;
            }
            transform.localScale = Vector3.zero;
        }

        // --- swap text at the bottom of the dip ---
        targetText.text = showEnglish ? english : thai;

        // --- pop in: scale 0 → 1, elastic-out (overshoot bounce) ---
        if (popInDuration > 0f)
        {
            float t = 0f;
            while (t < popInDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / popInDuration);
                float k = ElasticOut(u);
                transform.localScale = _baseScale * k;
                yield return null;
            }
        }
        transform.localScale = _baseScale;
        _animCo = null;
    }

    static float ElasticOut(float t)
    {
        // Standard elastic-out with overshoot. Lands at exactly 1 when t=1.
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float c4 = (2f * Mathf.PI) / 3f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
    }

    /// <summary>Replace the two strings at runtime (e.g. dynamic score labels).</summary>
    public void SetTexts(string thaiText, string englishText)
    {
        thai = thaiText;
        english = englishText;
        ApplyInstant(BilingualTextSync.ShowEnglish);
    }
}
