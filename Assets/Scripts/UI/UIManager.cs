using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("HUD Elements")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI coinsText;
    [SerializeField] private TextMeshProUGUI livesText;

    [Header("Panels")]
    [SerializeField] private GameObject hudPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private GameObject mainMenuPanel;

    [Header("Game Over UI")]
    [SerializeField] private TextMeshProUGUI finalScoreText;
    [SerializeField] private TextMeshProUGUI highScoreText;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button quitButton;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        SetupButtons();
        SubscribeToEvents();
        InitializeUI();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void SetupButtons()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumeClicked);

        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);

        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    private void SubscribeToEvents()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += UpdateScoreUI;
            ScoreManager.Instance.OnCoinsChanged += UpdateCoinsUI;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLivesChanged += UpdateLivesUI;
            GameManager.Instance.OnGameStateChanged += OnGameStateChanged;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= UpdateScoreUI;
            ScoreManager.Instance.OnCoinsChanged -= UpdateCoinsUI;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLivesChanged -= UpdateLivesUI;
            GameManager.Instance.OnGameStateChanged -= OnGameStateChanged;
        }
    }

    private void InitializeUI()
    {
        HideAllPanels();
        ShowHUD();
    }

    private void OnGameStateChanged(GameState newState)
    {
        switch (newState)
        {
            case GameState.Menu:
                ShowMainMenu();
                break;
            case GameState.Playing:
                HideAllPanels();
                ShowHUD();
                break;
            case GameState.Paused:
                ShowPauseMenu();
                break;
            case GameState.GameOver:
                ShowGameOverScreen();
                break;
            case GameState.Victory:
                ShowVictoryScreen();
                break;
        }
    }

    public void HideAllPanels()
    {
        if (hudPanel != null) hudPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    }

    public void ShowHUD()
    {
        if (hudPanel != null)
            hudPanel.SetActive(true);
    }

    public void HideHUD()
    {
        if (hudPanel != null)
            hudPanel.SetActive(false);
    }

    public void ShowPauseMenu()
    {
        if (pausePanel != null)
            pausePanel.SetActive(true);
    }

    public void HidePauseMenu()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    public void ShowGameOverScreen()
    {
        HideHUD();
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);

            if (finalScoreText != null && ScoreManager.Instance != null)
                finalScoreText.text = $"Score: {ScoreManager.Instance.CurrentScore}";

            if (highScoreText != null && ScoreManager.Instance != null)
                highScoreText.text = $"High Score: {ScoreManager.Instance.HighScore}";
        }
    }

    public void ShowVictoryScreen()
    {
        HideHUD();
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(true);

            if (finalScoreText != null && ScoreManager.Instance != null)
                finalScoreText.text = $"Score: {ScoreManager.Instance.CurrentScore}";
        }
    }

    public void ShowMainMenu()
    {
        HideAllPanels();
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
    }

    private void UpdateScoreUI(int score)
    {
        if (scoreText != null)
            scoreText.text = $"Score: {score}";
    }

    private void UpdateCoinsUI(int coins)
    {
        if (coinsText != null)
            coinsText.text = $"x {coins}";
    }

    private void UpdateLivesUI(int lives)
    {
        if (livesText != null)
            livesText.text = $"x {lives}";
    }

    private void OnResumeClicked()
    {
        GameManager.Instance?.TogglePause();
        AudioManager.Instance?.PlaySFX("ButtonClick");
    }

    private void OnRestartClicked()
    {
        GameManager.Instance?.RestartLevel();
        AudioManager.Instance?.PlaySFX("ButtonClick");
    }

    private void OnMainMenuClicked()
    {
        GameManager.Instance?.LoadMainMenu();
        AudioManager.Instance?.PlaySFX("ButtonClick");
    }

    private void OnQuitClicked()
    {
        GameManager.Instance?.QuitGame();
        AudioManager.Instance?.PlaySFX("ButtonClick");
    }
}
