using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(BoxCollider2D))]
public class ChromaHuntTile : BaseTileController
{
    private int _colorIndex = -1;
    public int ColorIndex => _colorIndex;

    public void Init(int index, Vector2Int gridPos)
    {
        InitBase(index, gridPos);
    }

    public void SetTileColor(int colorIndex, Color color)
    {
        _colorIndex = colorIndex;
        _sr.color = color;
    }

    public void SetFloorColor(Color color)
    {
        _colorIndex = -1;
        StopAllAnimations();
        _sr.color = color;
    }

    // forward tap to grid manager for display 2
    public override void OnPointerDown(PointerEventData eventData)
    {
        var grid = ChromaHuntGridManager.Instance;
        if (grid != null)
            grid.OnDebugTouchDown(GridPosition.x, GridPosition.y);
    }
}
