using System;
using UnityEngine;

[Serializable]
public class ChromaHuntState
{
    public AppState appState = AppState.Setup;
    public int score;
    public float timeLeft;
    public int gameDuration = 90;
    public GamePhase currentPhase = GamePhase.Phase1;

    // target
    public int targetColorIndex = -1;
    public float targetTimeLeft;

    // scoring
    public int totalCorrect;
    public int totalWrong;
    public int correctStreak;
    public int bestStreak;
    [Space]
    public float correctPercent;

    // stroop (phase 3)
    public bool isStroopRound;
    public int stroopDisplayColorIndex;

    // color pool ramp — shuffledColorPool is a random permutation of pool indices,
    // revealed one at a time as activeColorCount grows
    public int activeColorCount;
    public float nextColorAddTime;
    public bool warnVoiceFired;
    public int[] shuffledColorPool;

    public int[] GetActiveColorIndices()
    {
        if (shuffledColorPool == null || shuffledColorPool.Length == 0 || activeColorCount <= 0)
            return null;
        int n = Mathf.Clamp(activeColorCount, 1, shuffledColorPool.Length);
        var result = new int[n];
        for (int i = 0; i < n; i++) result[i] = shuffledColorPool[i];
        return result;
    }

    public float ElapsedTime => gameDuration - timeLeft;

    public float CorrectPercent
    {
        get
        {
            int total = totalCorrect + totalWrong;
            correctPercent = total > 0 ? (float)totalCorrect / total * 100f : 0f;
            return correctPercent;
        }
    }

    public void ResetScores()
    {
        score = 0;
        totalCorrect = 0;
        totalWrong = 0;
        correctStreak = 0;
        bestStreak = 0;
        correctPercent = 0f;
    }

    public void ResetTurnState()
    {
        currentPhase = GamePhase.Phase1;
        targetColorIndex = -1;
        targetTimeLeft = 0f;
        isStroopRound = false;
        stroopDisplayColorIndex = -1;
        activeColorCount = 0;
        nextColorAddTime = 0f;
        warnVoiceFired = false;
        shuffledColorPool = null;
    }
}
