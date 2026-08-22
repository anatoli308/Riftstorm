using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Lädt eine aus MUGEN konvertierte Stage: Definition aus
    /// <c>StreamingAssets/Custom_Stages/&lt;Name&gt;/</c>, Atlas-Seiten aus
    /// <c>Assets/Art/Custom_Stages/&lt;Name&gt;/</c>.
    /// </summary>
    public sealed class MugenStageLoader
    {
        private const float PixelsPerUnit = 64f;

        private readonly string m_JsonFolder;
        private readonly string m_TextureFolder;

        /// <summary>Erzeugt einen Loader für die Standard-Projektpfade.</summary>
        /// <param name="stageName">Ordnername der Stage, z. B. <c>"Agony"</c>.</param>
        public MugenStageLoader(string stageName)
        {
            m_JsonFolder = Path.Combine(
                Application.streamingAssetsPath, "Custom_Stages", stageName);
            m_TextureFolder = Path.Combine(
                Application.dataPath, "Art", "Custom_Stages", stageName);
            StageName = stageName;
        }

        /// <summary>Ordnername der zu ladenden Stage.</summary>
        public string StageName { get; }

        /// <summary>
        /// Lädt die Stage. Liefert <c>null</c>, wenn Definition oder Atlas fehlen —
        /// die Showcase fällt dann auf ihre Inspector-Werte zurück.
        /// </summary>
        public async Task<MugenStage> LoadAsync()
        {
            string jsonPath = Path.Combine(m_JsonFolder, StageName + ".json");
            string jsonText = await ReadTextAsync(jsonPath);
            if (jsonText == null)
            {
                Debug.LogWarning($"[MugenStageLoader] Stage-JSON nicht gefunden: {jsonPath}");
                return null;
            }

            MugenStageDef def;
            try
            {
                def = JsonConvert.DeserializeObject<MugenStageDef>(jsonText);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[MugenStageLoader] JSON-Parse-Fehler in {jsonPath}: {ex.Message}");
                return null;
            }
            if (def == null)
            {
                return null;
            }

            Texture2D[] textures = await LoadTexturesAsync(def);
            Dictionary<string, Sprite> sprites = BuildSprites(def, textures);
            return new MugenStage
            {
                Name = string.IsNullOrEmpty(def.Name) ? StageName : def.Name,
                ScreenWidth = Component(def.LocalCoord, 0, 320f),
                ScreenHeight = Component(def.LocalCoord, 1, 240f),
                GroundLevel = def.ZOffset,
                P1Start = new Vector2(Component(def.P1Start, 0, 0f), Component(def.P1Start, 1, 0f)),
                PlayerDistance = def.PlayerDistance,
                Layers = BuildLayers(def, sprites),
                Textures = textures,
            };
        }

        private async Task<Texture2D[]> LoadTexturesAsync(MugenStageDef def)
        {
            string[] files = def.Images != null && def.Images.Length > 0
                ? def.Images
                : new[] { def.Image };
            Texture2D[] textures = new Texture2D[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                if (!string.IsNullOrEmpty(files[i]))
                {
                    textures[i] = await LoadTextureAsync(Path.Combine(m_TextureFolder, files[i]));
                }
            }
            return textures;
        }

        /// <summary>Baut je Atlas-Rechteck ein Sprite, dessen Pivot auf der MUGEN-Achse sitzt.</summary>
        private static Dictionary<string, Sprite> BuildSprites(MugenStageDef def, Texture2D[] textures)
        {
            Dictionary<string, Sprite> sprites = new();
            if (def.Sprites == null)
            {
                return sprites;
            }
            foreach (MugenStageSpriteDef cell in def.Sprites)
            {
                if (cell == null || cell.W <= 0 || cell.H <= 0)
                {
                    continue;
                }
                Texture2D texture = cell.Page >= 0 && cell.Page < textures.Length
                    ? textures[cell.Page]
                    : null;
                if (texture == null)
                {
                    continue;
                }
                Rect rect = new(cell.X, texture.height - cell.Y - cell.H, cell.W, cell.H);
                Vector2 pivot = new(
                    (float)cell.PivotX / cell.W,
                    1f - (float)cell.PivotY / cell.H);
                Sprite sprite = Sprite.Create(
                    texture, rect, pivot, PixelsPerUnit, 0u, SpriteMeshType.FullRect);
                sprite.hideFlags = HideFlags.HideAndDontSave;
                sprites[cell.Key] = sprite;
            }
            return sprites;
        }

        private static MugenStageLayer[] BuildLayers(
            MugenStageDef def, Dictionary<string, Sprite> sprites)
        {
            if (def.Layers == null)
            {
                return System.Array.Empty<MugenStageLayer>();
            }
            List<MugenStageLayer> layers = new(def.Layers.Length);
            foreach (MugenStageLayerDef layerDef in def.Layers)
            {
                MugenStageFrame[] frames = BuildFrames(layerDef, sprites);
                if (frames.Length == 0)
                {
                    continue;
                }
                layers.Add(new MugenStageLayer
                {
                    Name = layerDef.Name,
                    Layer = layerDef.Layer,
                    Start = new Vector2(
                        Component(layerDef.Start, 0, 0f), Component(layerDef.Start, 1, 0f)),
                    Scale = new Vector2(
                        Component(layerDef.Scale, 0, 1f), Component(layerDef.Scale, 1, 1f)),
                    Tile = new Vector2Int(
                        (int)Component(layerDef.Tile, 0, 0f), (int)Component(layerDef.Tile, 1, 0f)),
                    Frames = frames,
                });
            }
            return layers.ToArray();
        }

        private static MugenStageFrame[] BuildFrames(
            MugenStageLayerDef layerDef, Dictionary<string, Sprite> sprites)
        {
            if (layerDef?.Frames == null)
            {
                return System.Array.Empty<MugenStageFrame>();
            }
            List<MugenStageFrame> frames = new(layerDef.Frames.Length);
            foreach (MugenStageFrameDef frameDef in layerDef.Frames)
            {
                if (frameDef == null
                    || string.IsNullOrEmpty(frameDef.Sprite)
                    || !sprites.TryGetValue(frameDef.Sprite, out Sprite sprite))
                {
                    continue;
                }
                frames.Add(new MugenStageFrame
                {
                    Sprite = sprite,
                    Seconds = frameDef.Ms / 1000f,
                    Offset = new Vector2(frameDef.X, frameDef.Y),
                });
            }
            return frames.ToArray();
        }

        private static float Component(float[] values, int index, float fallback)
        {
            return values != null && index < values.Length ? values[index] : fallback;
        }

        private static float Component(int[] values, int index, float fallback)
        {
            return values != null && index < values.Length ? values[index] : fallback;
        }

        private static async Task<string> ReadTextAsync(string path)
        {
            using UnityWebRequest req = UnityWebRequest.Get(FileUri(path));
            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            while (!op.isDone)
            {
                await Task.Yield();
            }
            return req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null;
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
                Debug.LogWarning($"[MugenStageLoader] Stage-PNG unlesbar: {path} ({req.error})");
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
            return absolutePath.Contains("://")
                ? absolutePath
                : "file:///" + absolutePath.Replace('\\', '/');
        }
    }
}
