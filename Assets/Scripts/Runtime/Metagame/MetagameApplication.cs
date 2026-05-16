namespace Tolik.Riftstorm.Runtime.Metagame
{
    /// <summary>
    /// Root component for the Metagame scene (lobby / connect screen). Hosts the MVC trio for
    /// the Metagame scene and forwards events through the EventManager.
    /// </summary>
    public class MetagameApplication : BaseApplication<MetagameModel, MetagameView, MetagameController>
    {
    }
}
