using UnityEngine;

public class ChromaHuntFloorBridge : MonoBehaviour
{
    [SerializeField] private bool showDebugLog = true;

    public void OnFloorTouchDown(int physX, int physY)
    {
        var grid = ChromaHuntGridManager.Instance;
        if (grid == null) return;

        if (physX < 0 || physX >= grid.GridWidth || physY < 0 || physY >= grid.GridHeight) {
            if (showDebugLog)
                Debug.Log($"[ChromaHuntFloor] ({physX},{physY}) out of bounds");
            return;
        }

        if (showDebugLog)
            Debug.Log($"[ChromaHuntFloor] touch ({physX},{physY})");

        grid.OnDebugTouchDown(physX, physY);
    }
}
