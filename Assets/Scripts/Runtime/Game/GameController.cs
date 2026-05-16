namespace Tolik.Riftstorm.Runtime.Game
{
    /// <summary>
    /// Bridges Game-scene UI / input to higher-level managers (ConnectionManager,
    /// future GametypeManager, ...). Subscribe to scene-scoped events via AddListener&lt;TEvent&gt;
    /// from inside derived controllers or in Awake of Game-scene components.
    /// </summary>
    public class GameController : Controller<GameApplication>
    {
    }
}
