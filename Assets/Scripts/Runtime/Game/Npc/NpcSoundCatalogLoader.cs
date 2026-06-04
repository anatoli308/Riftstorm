using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Synchroner Lade-Utility fuer NPC-Combat-Sounds aus
    /// <c>StreamingAssets/npc/_sounds.json</c>. Baut beim ersten Zugriff einen
    /// Lookup <c>Model-Name -&gt; Event -&gt; Liste von Sound-Dateinamen</c> auf
    /// und cached ihn prozessweit (Lazy-Static-Cache), damit jeder NPC ohne IO
    /// auf seine Sounds zugreift.
    /// </summary>
    /// <remarks>
    /// Bewusst static und ohne ServiceLocator-Registrierung (KISS), analog zu
    /// <see cref="NpcCatalogLoader"/>: die Daten sind unveraenderlich pro Prozess
    /// und werden ausschliesslich per Model-Name / Event gelesen. Die Datei ist
    /// ein JSON-Array von <c>{ "model", "event", "sound" }</c>-Objekten; pro
    /// (Model, Event) koennen mehrere Eintraege existieren, aus denen
    /// <see cref="TryGetRandomSound"/> zufaellig waehlt. Model- und Event-Keys
    /// werden case-insensitive verglichen. Bei fehlender Datei bleibt der Cache
    /// leer und alle Lookups liefern <c>false</c>.
    /// </remarks>
    public static class NpcSoundCatalogLoader
    {
        /// <summary>Unterordner unter <c>Application.streamingAssetsPath</c>.</summary>
        public const string SubFolder = "npc";

        /// <summary>Dateiname der Sound-Tabelle.</summary>
        public const string SoundsFileName = "_sounds.json";

        private static Dictionary<string, Dictionary<string, List<string>>> s_ByModel;
        private static bool s_LoadAttempted;

        /// <summary>
        /// Liefert einen zufaelligen Sound-Dateinamen fuer
        /// <paramref name="model"/> und <paramref name="evt"/> (z. B.
        /// <c>"attack"</c>, <c>"damage"</c>, <c>"die"</c>, <c>"aggro"</c>).
        /// Gibt <c>false</c> zurueck, wenn keine Datei, kein Model oder kein
        /// passendes Event existiert.
        /// </summary>
        /// <param name="model">FLARE-Model-Name (entspricht <see cref="NpcModel.Name"/>).</param>
        /// <param name="evt">Event-Schluessel aus <c>_sounds.json</c>.</param>
        /// <param name="soundFile">Ausgewaehlter Dateiname inkl. Endung (z. B. <c>"goblin_hit_1.ogg"</c>).</param>
        public static bool TryGetRandomSound(string model, string evt, out string soundFile)
        {
            soundFile = null;
            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(evt))
            {
                return false;
            }

            EnsureLoaded();

            if (!s_ByModel.TryGetValue(model, out Dictionary<string, List<string>> byEvent))
            {
                return false;
            }
            if (!byEvent.TryGetValue(evt, out List<string> sounds) || sounds.Count == 0)
            {
                return false;
            }

            soundFile = sounds.Count == 1 ? sounds[0] : sounds[Random.Range(0, sounds.Count)];
            return !string.IsNullOrEmpty(soundFile);
        }

        /// <summary>Setzt den Cache zurueck. Fuer Tests oder Editor-Reload.</summary>
        public static void ResetCacheForTesting()
        {
            s_ByModel = null;
            s_LoadAttempted = false;
        }

        private static void EnsureLoaded()
        {
            if (s_LoadAttempted)
            {
                return;
            }
            s_LoadAttempted = true;
            s_ByModel = new Dictionary<string, Dictionary<string, List<string>>>(System.StringComparer.OrdinalIgnoreCase);

            string path = Path.Combine(Application.streamingAssetsPath, SubFolder, SoundsFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[NpcSoundCatalogLoader] Datei fehlt: {path}");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                List<NpcSoundEntry> entries = JsonConvert.DeserializeObject<List<NpcSoundEntry>>(json);
                if (entries == null)
                {
                    return;
                }

                int counted = 0;
                foreach (NpcSoundEntry entry in entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Model) ||
                        string.IsNullOrEmpty(entry.Event) || string.IsNullOrEmpty(entry.Sound))
                    {
                        continue;
                    }

                    if (!s_ByModel.TryGetValue(entry.Model, out Dictionary<string, List<string>> byEvent))
                    {
                        byEvent = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
                        s_ByModel[entry.Model] = byEvent;
                    }
                    if (!byEvent.TryGetValue(entry.Event, out List<string> sounds))
                    {
                        sounds = new List<string>();
                        byEvent[entry.Event] = sounds;
                    }
                    sounds.Add(entry.Sound);
                    counted++;
                }

                Debug.Log($"[NpcSoundCatalogLoader] {counted} Sound-Eintraege fuer {s_ByModel.Count} Models geladen aus {path}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[NpcSoundCatalogLoader] Fehler beim Laden von {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// DTO fuer einen Eintrag aus <c>_sounds.json</c>
        /// (<c>{ "model", "event", "sound" }</c>).
        /// </summary>
        private sealed class NpcSoundEntry
        {
            /// <summary>FLARE-Model-Name (Schluessel).</summary>
            [JsonProperty("model")] public string Model { get; set; }

            /// <summary>Event-Schluessel (attack/damage/die/aggro).</summary>
            [JsonProperty("event")] public string Event { get; set; }

            /// <summary>Sound-Dateiname inkl. Endung.</summary>
            [JsonProperty("sound")] public string Sound { get; set; }
        }
    }
}
