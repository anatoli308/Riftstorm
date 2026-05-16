namespace Tolik.Riftstorm.Runtime.Game
{
    /// <summary>
    /// Root component for the Game scene. The server loads this scene via NGO once
    /// ConnectionManager broadcasts <c>Success</c>, and connected clients sync into it
    /// automatically through NGO's scene management.
    /// </summary>
    public class GameApplication : BaseApplication<GameModel, GameView, GameController>
    {
    }
}
