using System;

namespace Riftstorm.Management.PrefabManagement
{
    /// <summary>
    /// Factory für PrefabData-Erstellung.
    /// </summary>
    public static class PrefabDataFactory
    {
        public static PrefabData<T> Create<T>(string key, T prefab, PrefabSource source = PrefabSource.System) where T : UnityEngine.Object
        {
            return new PrefabData<T>
            {
                Id = key,
                Prefab = prefab,
                Source = source,
                LoadedAt = DateTime.Now
            };
        }
    }
}