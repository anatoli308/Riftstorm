namespace Tolik.Riftstorm.Runtime.Metagame
{
    /// <summary>
    /// Holds metagame-scoped state — typed server address/port the user has entered, profile
    /// data once authentication systems are added, etc. Extend per feature.
    /// </summary>
    public class MetagameModel : Model<MetagameApplication>
    {
        public string ServerAddress = "127.0.0.1";
        public ushort ServerPort = 7777;
        public string PlayerName = "Player";
    }
}
