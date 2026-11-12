using UnityEngine;
using UnityEngine.EventSystems;

public abstract class BaseMouseInputHandler<TTile> : MonoBehaviour
    where TTile : BaseTileController
{
    protected Camera _cam;

    protected virtual void Start() => _cam = Camera.main;

    void Update()
    {
        if (!CanProcessInput()) return;

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
                var tile = hit.collider.GetComponent<TTile>();
                if (tile != null) OnTilePressed(tile);
            }
        }

        if (Input.GetMouseButtonUp(0))
            OnMouseReleased();
    }

    protected abstract bool CanProcessInput();
    public abstract void OnTilePressed(TTile tile);
    protected virtual void OnMouseReleased() { }
    public virtual void OnTileReleased(TTile tile) { }
}
