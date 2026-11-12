public interface IMouseGameReceiver
{
    bool CanProcessMouseInput();
    void OnTileDown(BaseTileController tile);
    void OnTileUp(BaseTileController tile);
    void OnInputReleased();
    void OnFloorTouchDown(int physX, int physY);
    void OnFloorTouchUp(int physX, int physY);
}
