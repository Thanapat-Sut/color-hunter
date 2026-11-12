using UnityEngine;

// universal keyboard input — auto-finds any IKeyboardGameReceiver in scene
public class GameKeyboardInputHandler : BaseKeyboardInputHandler
{
    static readonly KeyCode[][] _keyBindings = new KeyCode[][]
    {
        new[] { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D },                                   // P1
        new[] { KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow },     // P2
        new[] { KeyCode.I, KeyCode.K, KeyCode.J, KeyCode.L },                                   // P3
        new[] { KeyCode.Keypad8, KeyCode.Keypad5, KeyCode.Keypad4, KeyCode.Keypad6 }            // P4
    };

    IKeyboardGameReceiver _receiver;

    protected override KeyCode[][] KeyBindings => _keyBindings;

    protected override bool IsManagerReady()
    {
        if (_receiver != null) return true;
        foreach (var mb in FindObjectsOfType<MonoBehaviour>()) {
            if (mb is IKeyboardGameReceiver r) { _receiver = r; return true; }
        }
        return false;
    }

    protected override AppState GetAppState() => _receiver.GetAppState();
    protected override void RegisterReady() => _receiver.RegisterPlayerReady(playerIndex);
    protected override void MoveByDirection(int dx, int dy) => _receiver.MovePlayerByDirection(playerIndex, dx, dy);
}
