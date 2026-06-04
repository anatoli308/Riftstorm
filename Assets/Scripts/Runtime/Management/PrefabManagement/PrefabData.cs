

using System;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Riftstorm.Management.PrefabManagement
{
    /// <summary>
    /// Datenstruktur für gespeicherte Prefab-Informationen.
    /// </summary>
    public class PrefabData<T> where T : UnityEngine.Object
    {
        public string Id { get; set; }
        public T Prefab { get; set; }
        public PrefabSource Source { get; set; }
        public DateTime LoadedAt { get; set; }

        /// <summary>
        /// Addressables Handle für Memory Management.
        /// Non-generic AsyncOperationHandle kann alle Asset-Typen halten.
        /// </summary>
        public AsyncOperationHandle Handle { get; set; }
    }
}