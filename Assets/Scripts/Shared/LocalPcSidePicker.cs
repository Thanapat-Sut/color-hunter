using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Drop-in Left/Right PC-side picker. Attach to the "Left/Right Row" GameObject (or a
/// sibling) in any setup UI, wire the two buttons in Inspector, and this component:
///   1. Hides <see cref="pickerRoot"/> when <see cref="LocalPcSide.IsSelected"/> is true.
///   2. Shows it when the side is still Unset, so the player can register this PC once.
///   3. Persists the pick via <see cref="LocalPcSide.Set(int)"/> — shared across every
///      casual-game .exe on this machine (see LocalPcSide storage notes).
///
/// Games that want per-game visuals (grayed-out already-picked button, hero portraits,
/// etc.) can subscribe to <see cref="onSidePicked"/> or keep their own picker UI — this
/// component is just the reusable path for the shared "Left/Right Row" pattern.
/// </summary>
public class LocalPcSidePicker : MonoBehaviour
{
    [Tooltip("Root GameObject that holds the Left + Right buttons. Toggled active/inactive based on LocalPcSide.IsSelected. Leave null to toggle this GameObject.")]
    [SerializeField] private GameObject pickerRoot;
    [Tooltip("Button that registers this PC as LEFT (slot 0).")]
    [SerializeField] private Button leftButton;
    [Tooltip("Button that registers this PC as RIGHT (slot 1).")]
    [SerializeField] private Button rightButton;
    [Tooltip("Fires after the player picks a side. Use for follow-up UI transitions (e.g. reveal Start button).")]
    public UnityEvent onSidePicked;
    [Tooltip("When true, force-show the picker even if a side was previously saved. Useful for a re-register admin flow.")]
    [SerializeField] private bool forceShow;

    void Awake()
    {
        // Stay subscribed while the picker hides itself, so reset/rejected side
        // assignments can reveal it again without a game-specific UI listener.
        LocalPcSide.OnChanged += HandleSideChanged;
        if (leftButton != null)
        {
            leftButton.onClick.RemoveListener(HandleLeft);
            leftButton.onClick.AddListener(HandleLeft);
        }
        if (rightButton != null)
        {
            rightButton.onClick.RemoveListener(HandleRight);
            rightButton.onClick.AddListener(HandleRight);
        }
    }

    void OnEnable() => RefreshVisibility();

    void OnDestroy()
    {
        LocalPcSide.OnChanged -= HandleSideChanged;
        if (leftButton != null) leftButton.onClick.RemoveListener(HandleLeft);
        if (rightButton != null) rightButton.onClick.RemoveListener(HandleRight);
    }

    void HandleSideChanged(int side) => RefreshVisibility();

    void HandleLeft()  => Pick(LocalPcSide.Left);
    void HandleRight() => Pick(LocalPcSide.Right);

    void Pick(int side)
    {
        LocalPcSide.Set(side);
        RefreshVisibility();
        onSidePicked?.Invoke();
    }

    /// <summary>Inspector helper: clear this PC's saved side and show the picker again.</summary>
    [ContextMenu("Reset PC Side")]
    public void ResetPcSide()
    {
        LocalPcSide.Clear();
        RefreshVisibility();
    }

    /// <summary>Manually re-evaluate visibility — call after clearing the saved side externally.</summary>
    public void RefreshVisibility()
    {
        int side = LocalPcSide.Value;
        if (leftButton != null) leftButton.interactable = side != LocalPcSide.Left;
        if (rightButton != null) rightButton.interactable = side != LocalPcSide.Right;
        var target = pickerRoot != null ? pickerRoot : gameObject;
        bool shouldShow = forceShow || !LocalPcSide.IsSelected;
        if (target.activeSelf != shouldShow) target.SetActive(shouldShow);
    }
}
