using UnityEngine;

public abstract class BaseKeyboardInputHandler : MonoBehaviour
{
    public int playerIndex;

    protected static readonly Vector2Int[] Directions = new Vector2Int[]
    {
        new Vector2Int( 0, -1),  // Up
        new Vector2Int( 0,  1),  // Down
        new Vector2Int(-1,  0),  // Left
        new Vector2Int( 1,  0)   // Right
    };

    protected abstract KeyCode[][] KeyBindings { get; }
    protected abstract bool IsManagerReady();
    protected abstract AppState GetAppState();
    protected abstract void RegisterReady();
    protected abstract void MoveByDirection(int dx, int dy);

    void Update()
    {
        if (!IsManagerReady()) return;
        if (playerIndex < 0 || playerIndex >= KeyBindings.Length) return;

        var keys = KeyBindings[playerIndex];
        var state = GetAppState();

        if (state == AppState.Waiting || state == AppState.Playing)
        {
            for (int i = 0; i < 4; i++) {
                if (Input.GetKeyDown(keys[i])) {
                    MoveByDirection(Directions[i].x, Directions[i].y);
                    break;
                }
            }
        }
    }
}
