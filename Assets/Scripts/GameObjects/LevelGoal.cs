using UnityEngine;

public class LevelGoal : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool loadNextLevel = true;
    [SerializeField] private string specificLevelName;

    [Header("Effects")]
    [SerializeField] private GameObject goalEffect;

    private bool hasTriggered;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered) return;

        if (other.CompareTag("Player"))
        {
            hasTriggered = true;
            ReachGoal();
        }
    }

    private void ReachGoal()
    {
        if (goalEffect != null)
        {
            Instantiate(goalEffect, transform.position, Quaternion.identity);
        }

        if (loadNextLevel)
        {
            GameManager.Instance?.Victory();
        }
        else if (!string.IsNullOrEmpty(specificLevelName))
        {
            GameManager.Instance?.LoadLevel(specificLevelName);
        }
    }
}
