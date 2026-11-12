using TMPro;
using UnityEngine;

/// <summary>
/// Thai/English label that follows <see cref="GameLanguage"/>. Attach to a GameObject with a
/// <see cref="TMP_Text"/> and author both strings in the Inspector. An empty string for the
/// current language leaves the TMP text untouched.
/// (For the timed TH⇄EN alternation use <see cref="BilingualText"/> instead.)
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Optional override. Leave empty to use the TMP_Text on this GameObject.")]
    [SerializeField] private TMP_Text targetText;

    [TextArea(2, 6)]
    [SerializeField] private string thai;

    [TextArea(2, 6)]
    [SerializeField] private string english;

    void Awake()
    {
        if (targetText == null) targetText = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        GameLanguage.OnChanged += Apply;
        Apply(GameLanguage.Current);
    }

    void OnDisable() => GameLanguage.OnChanged -= Apply;

    void Apply(Language lang)
    {
        if (targetText == null) return;
        string s = lang == Language.EN ? english : thai;
        if (!string.IsNullOrEmpty(s)) targetText.text = s;
    }
}
