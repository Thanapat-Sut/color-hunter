using UnityEngine;
using System;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Score Settings")]
    [SerializeField] private int scorePerCoin = 10;
    [SerializeField] private int scorePerEnemy = 50;

    private int currentScore;
    private int highScore;
    private int coinsCollected;

    private const string HIGH_SCORE_KEY = "HighScore";

    public int CurrentScore => currentScore;
    public int HighScore => highScore;
    public int CoinsCollected => coinsCollected;

    public event Action<int> OnScoreChanged;
    public event Action<int> OnCoinsChanged;
    public event Action<int> OnHighScoreChanged;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        LoadHighScore();
    }

    public void AddScore(int amount)
    {
        currentScore += amount;
        OnScoreChanged?.Invoke(currentScore);

        CheckHighScore();
    }

    public void CollectCoin()
    {
        coinsCollected++;
        AddScore(scorePerCoin);
        OnCoinsChanged?.Invoke(coinsCollected);
        AudioManager.Instance?.PlaySFX("Coin");
    }

    public void DefeatEnemy()
    {
        AddScore(scorePerEnemy);
        AudioManager.Instance?.PlaySFX("EnemyDefeat");
    }

    private void CheckHighScore()
    {
        if (currentScore > highScore)
        {
            highScore = currentScore;
            SaveHighScore();
            OnHighScoreChanged?.Invoke(highScore);
        }
    }

    private void SaveHighScore()
    {
        PlayerPrefs.SetInt(HIGH_SCORE_KEY, highScore);
        PlayerPrefs.Save();
    }

    private void LoadHighScore()
    {
        highScore = PlayerPrefs.GetInt(HIGH_SCORE_KEY, 0);
    }

    public void ResetScore()
    {
        currentScore = 0;
        coinsCollected = 0;
        OnScoreChanged?.Invoke(currentScore);
        OnCoinsChanged?.Invoke(coinsCollected);
    }

    public void ResetHighScore()
    {
        highScore = 0;
        PlayerPrefs.DeleteKey(HIGH_SCORE_KEY);
        OnHighScoreChanged?.Invoke(highScore);
    }
}
