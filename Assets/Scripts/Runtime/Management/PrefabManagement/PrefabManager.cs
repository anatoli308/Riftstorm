using System.Threading.Tasks;

namespace Riftstorm.Management.PrefabManagement
{
    /// <summary>
    /// Schlanker Addressables-Facade-Service: lädt Prefabs, nutzt PrefabRegistry für Cache/Handles,
    /// stellt synchrone und einfache async-Zugriffe bereit und kann aus jedem Kontext per DI verwendet werden.
    /// </summary>
    public class PrefabManager
    {
        private readonly PrefabRegistry m_Registry;

        public PrefabManager()
        {
            m_Registry = new PrefabRegistry();
        }

        #region Loading
        /// <summary>
        /// Lädt ein Prefab synchron über Addressables.
        /// Nutzt Cache falls vorhanden.
        /// WARNUNG: Blockiert den Main Thread - verwende LoadPrefabAsync wenn möglich!
        /// </summary>
        public T LoadPrefab<T>(string key) where T : UnityEngine.Object
        {
            var data = m_Registry.GetOrLoadPrefabData<T>(key);
            return data?.Prefab;
        }

        /// <summary>
        /// Lädt ein Prefab asynchron über Addressables.
        /// </summary>
        public async Task<T> LoadPrefabAsync<T>(string key) where T : UnityEngine.Object
        {
            return await Task.FromResult(LoadPrefab<T>(key));
        }

        #endregion

        #region Public API

        /// <summary>
        /// Löscht den Cache und released alle Addressables Handles.
        /// </summary>
        public void ClearCache()
        {
            m_Registry?.ClearCache();
        }

        #endregion

        #region Debug

        /// <summary>
        /// Gibt Statistiken über die PrefabRegistry aus.
        /// </summary>
        public void LogStatistics()
        {
            m_Registry?.LogStatistics();
        }

        #endregion
    }
}
