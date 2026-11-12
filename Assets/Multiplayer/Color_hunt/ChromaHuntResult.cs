using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// UI Toolkit overlay shown at the end of a multiplayer round. Displays
/// "YOU WIN" / "YOU LOSE" / "DRAW" plus both final scores ordered by
/// score, with the local player's row highlighted.
///
/// Visible only while a multiplayer session is in the GameOver state.
/// In solo, the existing single-player game-over UI runs as-is.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ChromaHuntResult : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [Tooltip("Same row template as the scoreboard (ScoreRow.uxml).")]
    [SerializeField] private VisualTreeAsset rowTemplate;

    [Header("Display names (per slot — overridden by launcher invite)")]
    [SerializeField] private string playerAName = "Player 1";
    [SerializeField] private string playerBName = "Player 2";

    private VisualElement root;
    private VisualElement rowsContainer;
    private Label winnerTag;

    private bool lastVisible;
    private bool needsRender = true;

    /// <summary>Set the PC names for the result panel. Called from the launcher invite.</summary>
    public void SetPlayerNames(string a, string b)
    {
        if (!string.IsNullOrEmpty(a)) playerAName = a;
        if (!string.IsNullOrEmpty(b)) playerBName = b;
        needsRender = true;
    }

    private void Awake()
    {
        if (document == null) document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        BindRoot();
        SetVisible(false);
        needsRender = true;
    }

    private void BindRoot()
    {
        if (document == null || document.rootVisualElement == null) return;
        root = document.rootVisualElement.Q<VisualElement>("result-root") ?? document.rootVisualElement;
        rowsContainer = document.rootVisualElement.Q<VisualElement>("rows");
        winnerTag = document.rootVisualElement.Q<Label>("winner-tag");
    }

    private void Update()
    {
        if (rowsContainer == null) BindRoot();

        bool networked = ChromaHuntNetwork.IsNetworked && ChromaHuntNetwork.Instance != null;
        bool gameOver = ChromaHuntManager.Instance != null
                        && ChromaHuntManager.Instance.state.appState == AppState.GameOver;
        bool shouldShow = networked && gameOver;

        if (shouldShow != lastVisible)
        {
            // Re-render when transitioning into visible so we capture the
            // final scores at the moment GameOver fires.
            if (shouldShow) needsRender = true;
            SetVisible(shouldShow);
            lastVisible = shouldShow;
        }

        if (shouldShow && needsRender)
        {
            Render();
            needsRender = false;
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

    private void Render()
    {
        var net = ChromaHuntNetwork.Instance;
        if (net == null) return;

        bool localIsA = net.LocalIsPlayerA;
        int sa = localIsA ? net.LocalScore : net.OpponentScore;
        int sb = localIsA ? net.OpponentScore : net.LocalScore;
        int my = localIsA ? sa : sb;
        int opp = localIsA ? sb : sa;

        // Winner tag
        if (winnerTag != null)
        {
            winnerTag.RemoveFromClassList("win");
            winnerTag.RemoveFromClassList("lose");
            winnerTag.RemoveFromClassList("draw");
            if (my > opp) { winnerTag.text = "YOU WIN"; winnerTag.AddToClassList("win"); }
            else if (my < opp) { winnerTag.text = "YOU LOSE"; winnerTag.AddToClassList("lose"); }
            else { winnerTag.text = "DRAW"; winnerTag.AddToClassList("draw"); }
        }

        // Rows
        if (rowsContainer == null || rowTemplate == null) return;
        rowsContainer.Clear();

        var entries = new List<Entry>(2)
        {
            new Entry { name = playerAName, score = sa, isMe = localIsA },
            new Entry { name = playerBName, score = sb, isMe = !localIsA }
        };
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

            var scoreRow = row.Q(className: "score-row");
            if (scoreRow != null && e.isMe) scoreRow.AddToClassList("me");

            rowsContainer.Add(row);
        }
    }
}
