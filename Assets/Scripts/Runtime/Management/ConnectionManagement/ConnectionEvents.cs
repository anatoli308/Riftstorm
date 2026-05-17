namespace Tolik.Riftstorm.Runtime.ConnectionManagement
{
    /// <summary>
    /// Reasons / outcomes of a connection transition.
    /// </summary>
    public enum ConnectStatus
    {
        Undefined,
        Success,
        ServerFull,
        LoggedInAgain,
        UserRequestedDisconnect,
        GenericDisconnect,
        ServerEndedSession,
        StartServerFailed,
        StartClientFailed,
        Reconnecting,
    }

    /// <summary>
    /// Broadcast on the ConnectionManager.EventManager whenever a connection state transitions
    /// to a notable outcome (server up, client connected, disconnected, ...).
    /// </summary>
    public class ConnectionEvent : AppEvent
    {
        public ConnectStatus status;

        public ConnectionEvent(ConnectStatus status)
        {
            this.status = status;
        }
    }
}
