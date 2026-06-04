using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Riftstorm.Management.PrefabManagement
{
    /// <summary>
    /// Zentrale Verwaltung für Prefab-Assets mit Caching über Addressables.
    /// Cached geladene Prefabs und managed Addressables Handles.
    /// </summary>
    public class PrefabRegistry
    {
        private readonly Dictionary<string, object> m_PrefabCache = new(StringComparer.OrdinalIgnoreCase);

        #region Loading & Caching

        /// <summary>
        /// Registriert bereits erstellte PrefabData.
        /// </summary>
        public void RegisterPrefabData<T>(PrefabData<T> data) where T : UnityEngine.Object
        {
            if (data == null || string.IsNullOrEmpty(data.Id) || data.Prefab == null)
            {
                Debug.LogWarning("[PrefabRegistry] Cannot register invalid PrefabData");
                return;
            }

            m_PrefabCache[data.Id] = data;
        }

        /// <summary>
        /// Gibt PrefabData nach Key zurück. Nutzt Cache oder lädt über Addressables.
        /// </summary>
        public PrefabData<T> GetOrLoadPrefabData<T>(string key) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key))
                return null;

            // 1. Cache prüfen
            if (m_PrefabCache.TryGetValue(key, out var cached) && cached is PrefabData<T> castedData)
            {
                //Debug.Log($"[PrefabRegistry] Returning cached prefab: {key}");
                return castedData;
            }

            // 2. Addressables laden
            if (TryLoadFromAddressables<T>(key, out var addressablesData))
            {
                m_PrefabCache[key] = (object)addressablesData;
                return addressablesData;
            }

            Debug.LogWarning($"[PrefabRegistry] Prefab not found: {key}");
            return null;
        }

        /// <summary>
        /// Entfernt ein Prefab aus dem Cache und released das Addressables-Handle.
        /// </summary>
        public void UnregisterPrefab(string key)
        {
            if (!m_PrefabCache.TryGetValue(key, out var data))
                return;

            m_PrefabCache.Remove(key);
            ReleasePrefabDataHandle(data);
        }

        /// <summary>
        /// Löscht alle gecachten Prefab-Referenzen und released Addressables-Handles.
        /// </summary>
        public void ClearCache()
        {
            int releasedCount = 0;
            foreach (var data in m_PrefabCache.Values)
            {
                if (ReleasePrefabDataHandle(data))
                    releasedCount++;
            }

            m_PrefabCache.Clear();
            Debug.Log($"[PrefabRegistry] Prefab Cache cleared - released {releasedCount} Addressables handles");
        }
        #endregion

        #region Internal Methods

        /// <summary>
        /// Hilfsmethode zum Release des Addressables-Handles aus PrefabData.
        /// Nutzt Reflection statt dynamic.
        /// </summary>
        private bool ReleasePrefabDataHandle(object data)
        {
            try
            {
                var handleProp = data.GetType().GetProperty("Handle");
                if (handleProp != null)
                {
                    var handle = (AsyncOperationHandle)handleProp.GetValue(data);
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PrefabRegistry] Error releasing handle: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Versucht, ein Prefab über Addressables zu laden.
        /// </summary>
        private bool TryLoadFromAddressables<T>(string key, out PrefabData<T> prefabData) where T : UnityEngine.Object
        {
            prefabData = null;

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                var asset = handle.WaitForCompletion();

                if (asset != null)
                {
                    prefabData = PrefabDataFactory.Create(key, asset, PrefabSource.System);
                    prefabData.Handle = handle;
                    //Debug.Log($"[PrefabRegistry] Loaded prefab from Addressables: {key}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PrefabRegistry] Failed to load from Addressables: {key}, Error: {ex.Message}");
            }

            return false;
        }

        #endregion

        #region Statistics & Debug

        /// <summary>
        /// Anzahl der gecachten Prefabs.
        /// </summary>
        public int CachedPrefabCount => m_PrefabCache.Count;

        /// <summary>
        /// Gibt Statistiken über die PrefabRegistry aus.
        /// </summary>
        public void LogStatistics()
        {
            Debug.Log($"[PrefabRegistry] Statistics:\n" +
                      $"  Cached Prefabs: {CachedPrefabCount}\n" +
                      $"  Cached Keys: {string.Join(", ", m_PrefabCache.Keys)}");
        }

        #endregion
    }
}
