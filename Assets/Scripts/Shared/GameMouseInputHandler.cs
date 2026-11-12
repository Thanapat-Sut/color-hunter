using UnityEngine;
using UnityEngine.EventSystems;

// universal mouse/touch input — auto-finds any IMouseGameReceiver in scene
public class GameMouseInputHandler : MonoBehaviour
{
    public static GameMouseInputHandler Instance { get; private set; }

    IMouseGameReceiver _receiver;
    Camera _cam;
    BaseTileController _lastTile;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start() => _cam = Camera.main;

    bool EnsureReceiver()
    {
        if (_receiver != null) return true;
        foreach (var mb in FindObjectsOfType<MonoBehaviour>()) {
            if (mb is IMouseGameReceiver r) { _receiver = r; return true; }
        }
        return false;
    }

    void Update()
    {
        if (!EnsureReceiver()) return;
        if (!_receiver.CanProcessMouseInput()) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 mousePos = Input.mousePosition;
            mousePos.z = Mathf.Abs(_cam.transform.position.z);
            Vector2 worldPos = _cam.ScreenToWorldPoint(mousePos);

            var hit = Physics2D.Raycast(worldPos, Vector2.zero);
            if (hit.collider != null) {
                var tile = hit.collider.GetComponent<BaseTileController>();
                if (tile != null) {
                    _lastTile = tile;
                    _receiver.OnTileDown(tile);
                }
            }
        }

        if (Input.GetMouseButtonUp(0)) {
            if (_lastTile != null) {
                _receiver.OnTileUp(_lastTile);
                _lastTile = null;
            }
            _receiver.OnInputReleased();
        }
    }

    public void OnFloorTouchDown(int physX, int physY)
    {
        if (!EnsureReceiver()) return;
        _receiver.OnFloorTouchDown(physX, physY);
    }

    public void OnFloorTouchUp(int physX, int physY)
    {
        if (!EnsureReceiver()) return;
        _receiver.OnFloorTouchUp(physX, physY);
    }
}
