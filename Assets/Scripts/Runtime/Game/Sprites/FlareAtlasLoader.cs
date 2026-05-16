using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Lädt FLARE-Atlanten (JSON + PNG) aus <c>StreamingAssets</c> und cached sie.
    /// Reiner Service (kein MonoBehaviour) — wird über den ServiceLocator registriert
    /// und in <c>ClearCache</c> sauber aufgeräumt.
    /// </summary>
    public sealed class FlareAtlasLoader
    {
        private const float PixelsPerUnit = 64f;

        private readonly string m_JsonFolder;
        private readonly string m_TextureFolder;
        private readonly Dictionary<string, FlareAtlas> m_Cache = new();

        /// <summary>
        /// Erzeugt einen Loader, der JSON-Atlanten aus <c>StreamingAssets/{subFolder}</c>
        /// und Texturen aus demselben Ordner lädt.
        /// </summary>
        public FlareAtlasLoader(string subFolder)
            : this(
                Path.Combine(Application.streamingAssetsPath, subFolder),
                Path.Combine(Application.streamingAssetsPath, subFolder))
        {
        }

        /// <summary>
        /// Erzeugt einen Loader mit getrennten Pfaden für JSON-Atlanten und PNG-Texturen.
        /// Beide Pfade sind absolut (z. B. <c>Application.streamingAssetsPath</c>-
        /// oder <c>Application.dataPath</c>-relativ). Nützlich, wenn FLARE-Definitionen
        /// in <c>StreamingAssets</c> liegen, die Bilder aber unter <c>Assets/Art</c>.
        /// </summary>
        public FlareAtlasLoader(string jsonAbsoluteFolder, string textureAbsoluteFolder)
        {
            m_JsonFolder = jsonAbsoluteFolder;
            m_TextureFolder = textureAbsoluteFolder;
        }

        /// <summary>
        /// Lädt einen Atlas (z. B. "default_chest"). Cache-first; wiederholte Aufrufe
        /// liefern dieselbe Instanz. Liefert <c>null</c>, wenn die JSON-Datei fehlt.
        /// </summary>
        public async Task<FlareAtlas> LoadAsync(string atlasName)
        {
            if (string.IsNullOrEmpty(atlasName))
            {
                return null;
            }
            if (m_Cache.TryGetValue(atlasName, out FlareAtlas cached))
            {
                return cached;
            }

            string jsonPath = Path.Combine(m_JsonFolder, atlasName + ".json");
            string jsonText = await ReadTextAsync(jsonPath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[FlareAtlasLoader] JSON nicht gefunden: {jsonPath}");
                return null;
            }

            FlareAtlasDef def;
            try
            {
                def = JsonConvert.DeserializeObject<FlareAtlasDef>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[FlareAtlasLoader] JSON-Parse-Fehler in {jsonPath}: {ex.Message}");
                return null;
            }
            if (def == null || def.Animations == null)
            {
                Debug.LogError($"[FlareAtlasLoader] Leere Atlas-Definition in {jsonPath}");
                return null;
            }

            Texture2D texture = await LoadTextureAsync(Path.Combine(m_TextureFolder, def.Image));
            FlareAtlas atlas = BuildAtlas(atlasName, texture, def);
            m_Cache[atlasName] = atlas;
            return atlas;
        }

        /// <summary>
        /// Räumt den internen Cache auf und zerstört alle erzeugten Texturen/Sprites.
        /// Wird vom ServiceLocator beim Teardown via Reflection aufgerufen.
        /// </summary>
        public void ClearCache()
        {
            foreach (FlareAtlas atlas in m_Cache.Values)
            {
                if (atlas == null)
                {
                    continue;
                }
                if (atlas.Animations != null)
                {
                    foreach (FlareAnimation anim in atlas.Animations.Values)
                    {
                        if (anim?.Sprites == null)
                        {
                            continue;
                        }
                        for (int f = 0; f < anim.Sprites.Length; f++)
                        {
                            Sprite[] row = anim.Sprites[f];
                            if (row == null)
                            {
                                continue;
                            }
                            for (int d = 0; d < row.Length; d++)
                            {
                                if (row[d] != null)
                                {
                                    Object.Destroy(row[d]);
                                }
                            }
                        }
                    }
                }
                if (atlas.Texture != null)
                {
                    Object.Destroy(atlas.Texture);
                }
            }
            m_Cache.Clear();
        }

        private static FlareAtlas BuildAtlas(string name, Texture2D texture, FlareAtlasDef def)
        {
            Dictionary<string, FlareAnimation> animations = new(def.Animations.Count);
            int textureHeight = texture != null ? texture.height : 0;

            foreach (KeyValuePair<string, FlareAnimationDef> kvp in def.Animations)
            {
                FlareAnimationDef ad = kvp.Value;
                if (ad?.Frames == null || ad.FramesCount <= 0)
                {
                    continue;
                }
                Sprite[][] sprites = new Sprite[ad.FramesCount][];
                for (int f = 0; f < ad.FramesCount && f < ad.Frames.Count; f++)
                {
                    List<FlareCell> row = ad.Frames[f];
                    Sprite[] dirs = new Sprite[FlareDirection.Count];
                    if (row != null && texture != null)
                    {
                        int n = Mathf.Min(FlareDirection.Count, row.Count);
                        for (int d = 0; d < n; d++)
                        {
                            dirs[d] = BuildSprite(texture, textureHeight, row[d]);
                        }
                    }
                    sprites[f] = dirs;
                }
                float durationSeconds = Mathf.Max(0.001f, ad.DurationMs / 1000f);
                animations[kvp.Key] = new FlareAnimation(kvp.Key, ad.Type, durationSeconds, sprites);
            }

            return new FlareAtlas(name, texture, animations);
        }

        private static Sprite BuildSprite(Texture2D texture, int textureHeight, FlareCell cell)
        {
            if (cell == null || cell.W <= 0 || cell.H <= 0)
            {
                return null;
            }
            // FLARE benutzt Top-Left als Origin; Unity-Sprite-Rect ist Bottom-Left.
            float rectY = textureHeight - cell.Y - cell.H;
            Rect rect = new(cell.X, rectY, cell.W, cell.H);
            // FLARE-Anker (ox, oy) ist vom Top-Left; Unity-Pivot ist normalisiert vom Bottom-Left.
            Vector2 pivot = new(
                cell.W > 0 ? (float)cell.Ox / cell.W : 0.5f,
                cell.H > 0 ? 1f - (float)cell.Oy / cell.H : 0f);
            Sprite sprite = Sprite.Create(texture, rect, pivot, PixelsPerUnit, 0u, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            // StreamingAssets liegt unter Android in einem Archiv → UnityWebRequest;
            // auf Desktop genügt direktes File-IO. Hier einheitlich via UWR.
            using UnityWebRequest req = UnityWebRequest.Get(FileUri(path));
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                return null;
            }
            return req.downloadHandler.text;
        }

        private static async Task<Texture2D> LoadTextureAsync(string path)
        {
            using UnityWebRequest req = UnityWebRequestTexture.GetTexture(FileUri(path), true);
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[FlareAtlasLoader] PNG nicht gefunden / unlesbar: {path} ({req.error}). Spieler bleibt unsichtbar bis das Asset eingespielt ist.");
                return null;
            }
            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static string FileUri(string absolutePath)
        {
            // UnityWebRequest akzeptiert nackten Pfad auf Desktop und braucht file:// auf manchen Plattformen.
            if (absolutePath.Contains("://"))
            {
                return absolutePath;
            }
            return "file:///" + absolutePath.Replace('\\', '/');
        }
    }
}
