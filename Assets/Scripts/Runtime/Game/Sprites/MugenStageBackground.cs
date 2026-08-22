using System.Collections.Generic;
using UnityEngine;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Zeichnet die Hintergrundebenen einer MUGEN-Stage hinter (und bei
    /// <c>layerno = 1</c> vor) dem Charakter und treibt ihre Animationen.
    /// </summary>
    /// <remarks>
    /// Die Kamera der Showcase steht still, deshalb entfällt die Parallaxe: bei
    /// Kameraposition 0 ist die <c>start</c>-Angabe einer Ebene bereits ihre
    /// Bildschirmposition. Der Charakter steht nicht in der Bildmitte, sondern
    /// auf <c>p1startx</c> — dieser Versatz wird beim Platzieren herausgerechnet,
    /// damit Stage und bildschirmverankerte Effekte denselben Ursprung teilen.
    /// </remarks>
    public sealed class MugenStageBackground : System.IDisposable
    {
        /// <summary>Umrechnung MUGEN-Pixel → Unity-Einheiten (muss zum Atlas passen).</summary>
        private const float PixelsPerUnit = 64f;

        /// <summary>Sortier-Order der hintersten Ebene; Effekte mit <c>under</c> liegen darüber.</summary>
        private const int BackgroundOrder = -30000;

        /// <summary>Sortier-Order der vordersten Ebene (<c>layerno = 1</c>).</summary>
        private const int ForegroundOrder = 30000;

        /// <summary>Obergrenze für Kacheln je Achse, damit ein winziges Sprite die Szene nicht flutet.</summary>
        private const int MaxTiles = 64;

        /// <summary>Eine instanziierte Ebene samt ihrer Kacheln.</summary>
        private sealed class LayerInstance
        {
            public MugenStageLayer Spec;
            public Transform Root;
            public SpriteRenderer[] Tiles;
            public int FrameIndex;
            public float NextFrameAt;
        }

        private readonly GameObject m_Root;
        private readonly List<LayerInstance> m_Layers = new();
        private readonly MugenStage m_Stage;
        private float m_Elapsed;

        /// <summary>
        /// Baut die Stage unter <paramref name="parent"/> auf. Der Ursprung von
        /// <paramref name="parent"/> muss der Bodenpunkt des Charakters sein und
        /// darf nicht skaliert oder gedreht sein — eine Stage kennt keine
        /// Charakter-Skalierung.
        /// </summary>
        public MugenStageBackground(MugenStage stage, Transform parent)
        {
            m_Stage = stage;
            m_Root = new GameObject("MugenStage");
            m_Root.transform.SetParent(parent, false);
            m_Root.transform.localPosition = Vector3.zero;

            if (stage?.Layers == null)
            {
                return;
            }
            for (int i = 0; i < stage.Layers.Length; i++)
            {
                LayerInstance instance = BuildLayer(stage.Layers[i], i);
                if (instance != null)
                {
                    m_Layers.Add(instance);
                }
            }
            ApplyFrame(0f);
        }

        /// <summary>Blendet die gesamte Stage ein oder aus.</summary>
        public void SetVisible(bool visible)
        {
            if (m_Root != null)
            {
                m_Root.SetActive(visible);
            }
        }

        /// <summary>Treibt die Hintergrundanimationen weiter.</summary>
        public void Tick(float deltaTime)
        {
            if (m_Layers.Count == 0)
            {
                return;
            }
            m_Elapsed += deltaTime;
            ApplyFrame(m_Elapsed);
        }

        /// <summary>Zerstört alle erzeugten Objekte. Texturen gehören dem Loader.</summary>
        public void Dispose()
        {
            m_Layers.Clear();
            if (m_Root != null)
            {
                Object.Destroy(m_Root);
            }
        }

        private LayerInstance BuildLayer(MugenStageLayer layer, int index)
        {
            if (layer?.Frames == null || layer.Frames.Length == 0)
            {
                return null;
            }

            GameObject go = new($"BG_{index}_{layer.Name}");
            go.transform.SetParent(m_Root.transform, false);
            go.transform.localScale = new Vector3(layer.Scale.x, layer.Scale.y, 1f);

            int order = (layer.Layer > 0 ? ForegroundOrder : BackgroundOrder) + index;
            Sprite first = layer.Frames[0].Sprite;
            SpriteRenderer[] tiles = BuildTiles(go.transform, first, layer.Tile, order);

            return new LayerInstance
            {
                Spec = layer,
                Root = go.transform,
                Tiles = tiles,
                FrameIndex = -1,
                NextFrameAt = 0f,
            };
        }

        /// <summary>
        /// Erzeugt die Kachel-Renderer. <c>tile = 0</c> ergibt eine einzelne Kachel,
        /// <c>1</c> füllt die Kamerabreite, jeder größere Wert gibt die Anzahl vor.
        /// </summary>
        private SpriteRenderer[] BuildTiles(
            Transform parent, Sprite sprite, Vector2Int tile, int order)
        {
            int countX = TileCount(tile.x, sprite != null ? sprite.rect.width : 0f, m_Stage.ScreenWidth);
            List<SpriteRenderer> renderers = new(countX);
            int half = countX / 2;
            float step = sprite != null ? sprite.rect.width / PixelsPerUnit : 0f;

            for (int i = 0; i < countX; i++)
            {
                GameObject go = countX == 1 ? parent.gameObject : new GameObject($"Tile_{i}");
                if (countX > 1)
                {
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = new Vector3((i - half) * step, 0f, 0f);
                }
                SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = order;
                renderers.Add(renderer);
            }
            return renderers.ToArray();
        }

        private static int TileCount(int tile, float spriteWidth, float screenWidth)
        {
            if (tile == 0 || spriteWidth <= 0f)
            {
                return 1;
            }
            if (tile > 1)
            {
                return Mathf.Min(tile, MaxTiles);
            }
            // tile = 1: so viele Kacheln, dass der volle Kameraausschnitt gedeckt ist.
            return Mathf.Clamp(Mathf.CeilToInt(screenWidth * 2f / spriteWidth) + 1, 1, MaxTiles);
        }

        private void ApplyFrame(float elapsed)
        {
            foreach (LayerInstance instance in m_Layers)
            {
                int index = FrameIndexAt(instance.Spec.Frames, elapsed);
                if (index == instance.FrameIndex)
                {
                    continue;
                }
                instance.FrameIndex = index;
                MugenStageFrame frame = instance.Spec.Frames[index];
                foreach (SpriteRenderer renderer in instance.Tiles)
                {
                    renderer.sprite = frame.Sprite;
                }
                instance.Root.localPosition = LocalPosition(instance.Spec, frame);
            }
        }

        /// <summary>
        /// Frame-Index zur Zeit <paramref name="elapsed"/>. Ein Frame mit
        /// <c>Seconds &lt;= 0</c> hält für immer und beendet die Schleife.
        /// </summary>
        private static int FrameIndexAt(MugenStageFrame[] frames, float elapsed)
        {
            float total = 0f;
            foreach (MugenStageFrame frame in frames)
            {
                if (frame.Seconds <= 0f)
                {
                    total = 0f;
                    break;
                }
                total += frame.Seconds;
            }
            float time = total > 0f ? Mathf.Repeat(elapsed, total) : elapsed;

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i].Seconds <= 0f)
                {
                    return i;
                }
                time -= frames[i].Seconds;
                if (time < 0f)
                {
                    return i;
                }
            }
            return frames.Length - 1;
        }

        /// <summary>
        /// MUGEN-Bildschirmkoordinate → lokale Position. x rechnet den Standversatz
        /// des Charakters heraus, y zählt vom Boden (<c>zoffset</c>) nach oben.
        /// </summary>
        private Vector3 LocalPosition(MugenStageLayer layer, MugenStageFrame frame)
        {
            float x = layer.Start.x + frame.Offset.x - m_Stage.P1Start.x;
            float y = layer.Start.y + frame.Offset.y;
            return new Vector3(x / PixelsPerUnit, (m_Stage.GroundLevel - y) / PixelsPerUnit, 0f);
        }
    }
}
