public interface IKeyboardGameReceiver
{
    AppState GetAppState();
    void RegisterPlayerReady(int playerIndex);
    void MovePlayerByDirection(int playerIndex, int dx, int dy);
}
