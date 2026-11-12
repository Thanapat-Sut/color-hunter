using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool isActivated;
    [SerializeField] private Color activatedColor = Color.green;
    [SerializeField] private Color deactivatedColor = Color.gray;

    [Header("Effects")]
    [SerializeField] private GameObject activateEffect;

    private SpriteRenderer spriteRenderer;

    public bool IsActivated => isActivated;

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        UpdateVisual();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player") && !isActivated)
        {
            Activate();
        }
    }

    public void Activate()
    {
        isActivated = true;
        UpdateVisual();

        GameManager.Instance?.SetCheckpoint(transform.position);

        if (activateEffect != null)
        {
            Instantiate(activateEffect, transform.position, Quaternion.identity);
        }

        // Deactivate other checkpoints
        Checkpoint[] allCheckpoints = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None);
        foreach (Checkpoint cp in allCheckpoints)
        {
            if (cp != this && cp.isActivated)
            {
                cp.Deactivate();
            }
        }
    }

    public void Deactivate()
    {
        isActivated = false;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = isActivated ? activatedColor : deactivatedColor;
        }
    }
}
