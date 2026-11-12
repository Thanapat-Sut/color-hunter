using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public enum GameState
{
    Menu,
    Playing,
    Paused,
    GameOver,
    Victory
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    [SerializeField] private int totalLives = 3;
    [SerializeField] private Vector3 defaultSpawnPoint = Vector3.zero;

    private GameState currentState = GameState.Menu;
    private int currentLives;
    private Vector3 lastCheckpoint;

    public GameState CurrentState => currentState;
    public int CurrentLives => currentLives;
    public bool IsPaused => currentState == GameState.Paused;
    public bool IsPlaying => currentState == GameState.Playing;

    public event Action<GameState> OnGameStateChanged;
    public event Action<int> OnLivesChanged;

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
    }

    private void Start()
    {
        InitializeGame();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePause();
        }

        if (Input.GetKeyDown(KeyCode.R) && currentState == GameState.GameOver)
        {
            RestartLevel();
        }
    }

    public void InitializeGame()
    {
        currentLives = totalLives;
        lastCheckpoint = defaultSpawnPoint;
        SetGameState(GameState.Playing);
        OnLivesChanged?.Invoke(currentLives);
    }

    public void SetGameState(GameState newState)
    {
        if (currentState == newState) return;

        currentState = newState;
        OnGameStateChanged?.Invoke(currentState);

        switch (currentState)
        {
            case GameState.Paused:
                Time.timeScale = 0f;
                break;
            case GameState.Playing:
                Time.timeScale = 1f;
                break;
            case GameState.GameOver:
                Time.timeScale = 0f;
                break;
            case GameState.Victory:
                Time.timeScale = 0f;
                break;
        }
    }

    public void TogglePause()
    {
        if (currentState == GameState.Playing)
        {
            SetGameState(GameState.Paused);
            UIManager.Instance?.ShowPauseMenu();
        }
        else if (currentState == GameState.Paused)
        {
            SetGameState(GameState.Playing);
            UIManager.Instance?.HidePauseMenu();
        }
    }

    public void GameOver()
    {
        currentLives--;
        OnLivesChanged?.Invoke(currentLives);

        if (currentLives <= 0)
        {
            SetGameState(GameState.GameOver);
            UIManager.Instance?.ShowGameOverScreen();
        }
        else
        {
            Invoke(nameof(RespawnPlayer), 1.5f);
        }
    }

    public void Victory()
    {
        SetGameState(GameState.Victory);
        UIManager.Instance?.ShowVictoryScreen();
        AudioManager.Instance?.PlaySFX("Victory");
    }

    private void RespawnPlayer()
    {
        PlayerController.Instance?.Respawn(lastCheckpoint);
    }

    public void SetCheckpoint(Vector3 position)
    {
        lastCheckpoint = position;
        AudioManager.Instance?.PlaySFX("Checkpoint");
    }

    public void RestartLevel()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        InitializeGame();
    }

    public void LoadLevel(int levelIndex)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(levelIndex);
        InitializeGame();
    }

    public void LoadLevel(string levelName)
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(levelName);
        InitializeGame();
    }

    public void LoadNextLevel()
    {
        int nextLevel = SceneManager.GetActiveScene().buildIndex + 1;
        if (nextLevel < SceneManager.sceneCountInBuildSettings)
        {
            LoadLevel(nextLevel);
        }
        else
        {
            LoadMainMenu();
        }
    }

    public void LoadMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
        SetGameState(GameState.Menu);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
