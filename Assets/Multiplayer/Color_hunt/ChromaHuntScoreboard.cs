using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit overlay scoreboard for Color Hunt multiplayer. Reads score
/// state from ChromaHuntNetwork each frame, sorts entries by score
/// descending, and tags the local player's row with "(me)".
///
/// Visible only while a multiplayer session is in the Playing state.
/// In solo (no ChromaHuntNetwork in scene or no Mirror connection active)
/// the scoreboard stays hidden so it doesn't clutter single-player UI.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ChromaHuntScoreboard : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [Tooltip("UXML asset for one row. Must contain Labels named 'rank', 'name', 'score' inside an element with class 'score-row'.")]
    [SerializeField] private VisualTreeAsset rowTemplate;

    [Header("Display names (per slot — overridden by launcher invite)")]
    [SerializeField] private string playerAName = "Player 1";
    [SerializeField] private string playerBName = "Player 2";

    /// <summary>
    /// Set the PC names shown next to each slot. Called by ChromaHuntNetworkManager
    /// from the launcher invite payload (from_pc → A, to_pc → B). Empty/null
    /// inputs are ignored so a partial payload doesn't blank out a name.
    /// </summary>
    public void SetPlayerNames(string a, string b)
    {
        if (!string.IsNullOrEmpty(a)) playerAName = a;
        if (!string.IsNullOrEmpty(b)) playerBName = b;
        // Force the next Update to rebuild rows with the new names.
        lastScoreA = int.MinValue;
    }

    private VisualElement root;
    private VisualElement rowsContainer;

    private int lastScoreA = int.MinValue;
    private int lastScoreB = int.MinValue;
    private bool lastLocalIsA;
    private bool lastVisible;

    private void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        BindRoot();
        SetVisible(false);
        lastScoreA = int.MinValue;
        lastScoreB = int.MinValue;
    }

    private void BindRoot()
    {
        if (document == null || document.rootVisualElement == null) return;
        root = document.rootVisualElement.Q<VisualElement>("scoreboard-root") ?? document.rootVisualElement;
        rowsContainer = document.rootVisualElement.Q<VisualElement>("rows");
    }

    private void Update()
    {
        if (rowsContainer == null) BindRoot();

        bool networked = ChromaHuntNetwork.IsNetworked && ChromaHuntNetwork.Instance != null;
        bool playing = ChromaHuntManager.Instance != null
                       && ChromaHuntManager.Instance.state.appState == AppState.Playing;
        bool shouldShow = networked && playing;

        if (shouldShow != lastVisible)
        {
            SetVisible(shouldShow);
            lastVisible = shouldShow;
        }
        if (!shouldShow) return;

        var net = ChromaHuntNetwork.Instance;
        bool localIsA = net.LocalIsPlayerA;
        int sa = localIsA ? net.LocalScore : net.OpponentScore;
        int sb = localIsA ? net.OpponentScore : net.LocalScore;

        if (sa != lastScoreA || sb != lastScoreB || localIsA != lastLocalIsA)
        {
            RebuildRows(sa, sb, localIsA);
            lastScoreA = sa;
            lastScoreB = sb;
            lastLocalIsA = localIsA;
        }
    }

    private void SetVisible(bool show)
    {
        if (root != null)
            root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private struct Entry
    {
        public string name;
        public int score;
        public bool isMe;
    }

    private void RebuildRows(int scoreA, int scoreB, bool localIsA)
    {
        if (rowsContainer == null || rowTemplate == null) return;
        rowsContainer.Clear();

        var entries = new List<Entry>(2)
        {
            new Entry { name = playerAName, score = scoreA, isMe = localIsA },
            new Entry { name = playerBName, score = scoreB, isMe = !localIsA }
        };
        // Sort by score descending. Ties keep their original order (stable sort).
        entries.Sort((a, b) => b.score.CompareTo(a.score));

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var row = rowTemplate.Instantiate();

            var rank = row.Q<Label>("rank");
            var name = row.Q<Label>("name");
            var score = row.Q<Label>("score");
            if (rank != null) rank.text = (i + 1).ToString();
            if (name != null) name.text = e.isMe ? $"{e.name} (me)" : e.name;
            if (score != null) score.text = e.score.ToString("N0");

            // Highlight the local player's row via the .me class on the score-row.
            var scoreRow = row.Q(className: "score-row");
            if (scoreRow != null && e.isMe) scoreRow.AddToClassList("me");

            rowsContainer.Add(row);
        }
    }
}
