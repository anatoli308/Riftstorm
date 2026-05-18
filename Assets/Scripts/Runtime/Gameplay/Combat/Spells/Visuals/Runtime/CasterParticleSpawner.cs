using UnityEngine;

namespace Riftstorm.Gameplay.Combat.Spells.Visuals.Runtime
{
    /// <summary>
    /// Spawnt einen Unity-<see cref="ParticleSystem"/>-GameObject aus einer
    /// <see cref="ParticleSystemDefinition"/> (Source-<c>.psi</c>-Mirror).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mapped Source-Felder auf Unity-Module:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>emission</c> → <see cref="ParticleSystem.EmissionModule.rateOverTime"/></description></item>
    /// <item><description><c>particleLifeMin/Max</c> → <see cref="ParticleSystem.MainModule.startLifetime"/> (Two Constants)</description></item>
    /// <item><description><c>direction</c>+<c>spread</c> → <see cref="ParticleSystem.ShapeModule"/> Cone</description></item>
    /// <item><description><c>speedMin/Max</c> → <see cref="ParticleSystem.MainModule.startSpeed"/></description></item>
    /// <item><description><c>gravityMin/Max</c> → <see cref="ParticleSystem.ForceOverLifetimeModule"/> (Y)</description></item>
    /// <item><description><c>sizeStart/End</c> → <see cref="ParticleSystem.MainModule.startSize"/> + <see cref="ParticleSystem.SizeOverLifetimeModule"/></description></item>
    /// <item><description><c>spinStart/End</c> → <see cref="ParticleSystem.MainModule.startRotation"/> + <see cref="ParticleSystem.RotationOverLifetimeModule"/></description></item>
    /// <item><description><c>colorStart/End</c> → <see cref="ParticleSystem.ColorOverLifetimeModule"/></description></item>
    /// <item><description><c>addBlend</c> → additives Particle-Material</description></item>
    /// <item><description><c>relative != 0</c> → GO als Child des Casters (sonst World-Space)</description></item>
    /// </list>
    /// <para>
    /// Lebensdauer: <see cref="ParticleSystemDefinition.Lifetime"/> &gt; 0 → System
    /// hat <see cref="ParticleSystem.MainModule.duration"/> und zerstört sich selbst,
    /// sobald keine Partikel mehr leben. Andernfalls läuft das System bis es vom
    /// Aufrufer explizit beendet wird (typisch via spell-finished-Hook in einer
    /// späteren Phase) — als Sicherheitsnetz limitiert auf <see cref="MaxOpenEndedSeconds"/>.
    /// </para>
    /// </remarks>
    public static class CasterParticleSpawner
    {
        /// <summary>
        /// Native Tile-Größe des Source-Atlas in Pixeln. Wird ausschließlich für
        /// die Tile-Koordinaten-Mathematik genutzt (<c>def.TileX / AtlasTilePixels = col</c>);
        /// die tatsächliche Textur-Auflösung pro Tile steuert <see cref="ProceduralTileTexPixels"/>.
        /// </summary>
        public const int AtlasTilePixels = 32;

        /// <summary>Atlas-Spaltenanzahl im Source-Layout (real: 4 Spalten × 32px = 128px breit).</summary>
        public const int AtlasColumns = 4;

        /// <summary>
        /// Atlas-Reihenanzahl im Source-Layout (real: 4 Reihen × 32px = 128px hoch).
        /// Zur Laufzeit wird die Reihenzahl aus der gebundenen Textur abgeleitet,
        /// diese Konstante dient nur dem prozeduralen Fallback-Atlas.
        /// </summary>
        public const int AtlasRows = 4;

        /// <summary>
        /// Render-Auflösung pro Tile für den prozeduralen Fallback-Atlas.
        /// Höher als <see cref="AtlasTilePixels"/>, damit die Soft-Blobs beim
        /// Hochskalieren auf Welt-Größe nicht blockig wirken.
        /// </summary>
        public const int ProceduralTileTexPixels = 128;

        /// <summary>Sicherheits-Cap für endlose Systeme (Source-<c>lifetime &lt;= 0</c>).</summary>
        public const float MaxOpenEndedSeconds = 8f;

        /// <summary>Source-Pixel → Unity-Welt-Einheiten Skalierung.</summary>
        public const float PixelsPerUnit = 32f;

        // Source: nativer Partikel-Quad = 32px. Unity startSize ist in Welt-Units.
        private const float SizeUnits = AtlasTilePixels / PixelsPerUnit;

        // Zusätzlicher visueller Scale, da Unitys additive Billboards mit
        // überlappender Emission (casting_fire: 73/s × ~2.4s Lifetime ≈ 175
        // gleichzeitige Blobs) deutlich „heller/breiter“ wirken als das
        // 2D-Blitting im Source. Reduziert nur das visuelle Footprint, ohne
        // Gameplay-Werte (Speed, Gravity, Lifetime) zu verzerren.
        private const float VisualScale = 0.15f;

        private static Material s_AdditiveMaterial;
        private static Material s_AlphaMaterial;
        // Cache fuer den real geladenen Atlas (StreamingAssets/particles/particles.png)
        // ODER den prozeduralen Soft-Blob-Fallback, je nachdem was zuerst verfuegbar ist.
        private static Texture2D s_AtlasTexture;

        /// <summary>
        /// Spawnt das Partikelsystem an <paramref name="caster"/> (oder als Child,
        /// wenn <see cref="ParticleSystemDefinition.IsRelative"/>). Liefert das
        /// erzeugte GameObject (zum optionalen späteren Stop).
        /// </summary>
        /// <param name="def">Die geladene Definition.</param>
        /// <param name="caster">Caster-Transform; Pflichtparameter.</param>
        /// <param name="worldYOffset">Vertikaler Welt-Offset (z. B. Caster-Höhe für <c>"-height"</c>-Tag).</param>
        public static GameObject Spawn(ParticleSystemDefinition def, Transform caster, float worldYOffset)
        {
            if (def == null || caster == null) { return null; }

            GameObject go = new("CasterParticles_" + def.TileX + "_" + def.TileY);
            // Wichtig: ParticleSystem startet beim Aktivieren automatisch.
            // Wir deaktivieren das GO, bevor wir die Komponente hinzufügen,
            // konfigurieren alle Module (inkl. duration) und aktivieren erst
            // danach — sonst warnt Unity: „Setting the duration while system
            // is still playing is not supported.“
            go.SetActive(false);
            Vector3 worldPos = caster.position + new Vector3(0f, worldYOffset, 0f);
            if (def.IsRelative)
            {
                go.transform.SetParent(caster, worldPositionStays: false);
                go.transform.localPosition = new Vector3(0f, worldYOffset, 0f);
            }
            else
            {
                go.transform.position = worldPos;
            }

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ConfigureMain(ps, def);
            ConfigureEmission(ps, def);
            ConfigureShape(ps, def);
            ConfigureSize(ps, def);
            ConfigureRotation(ps, def);
            ConfigureColor(ps, def);
            ConfigureForce(ps, def);
            ConfigureRenderer(ps, def);

            go.SetActive(true);
            ps.Play();

            // Auto-Destroy. Bei endlichen Systemen wartet StopAction.Destroy auf
            // das Aussterben aller Partikel; bei endlosen kappen wir hart.
            float dieAfter = def.HasFiniteLifetime
                ? def.Lifetime + Mathf.Max(def.ParticleLifeMin, def.ParticleLifeMax) + 0.5f
                : MaxOpenEndedSeconds;
            Object.Destroy(go, dieAfter);
            return go;
        }

        /// <summary>
        /// Beendet ein zuvor von <see cref="Spawn"/> erzeugtes Partikelsystem
        /// kontrolliert: Emission stoppt sofort, bereits lebende Partikel klingen
        /// ueber ihre Restlebenszeit aus (kein Schnitt). Idempotent gegenueber
        /// <c>null</c> / bereits zerstoerten GameObjects. Wird vom Caller (z. B.
        /// <c>PlayerCombat</c>) aufgerufen, sobald die Cast-Phase endet, damit
        /// endlose PSystems (<c>lifetime = -1</c>) nicht bis zum
        /// <see cref="MaxOpenEndedSeconds"/>-Cap am Boden weiter glitzern.
        /// </summary>
        public static void Stop(GameObject go)
        {
            if (go == null) { return; }
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null) { return; }
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmitting);
        }

        private static void ConfigureMain(ParticleSystem ps, ParticleSystemDefinition def)
        {
            ParticleSystem.MainModule m = ps.main;

            // Source-Duration: endliche -> exakt; endlose -> Cap (Loop ist aus).
            m.duration = def.HasFiniteLifetime ? def.Lifetime : MaxOpenEndedSeconds;
            m.loop = !def.HasFiniteLifetime;
            m.playOnAwake = false;
            m.simulationSpace = def.IsRelative ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;

            float lifeMin = Mathf.Max(0.05f, Mathf.Min(def.ParticleLifeMin, def.ParticleLifeMax));
            float lifeMax = Mathf.Max(lifeMin, Mathf.Max(def.ParticleLifeMin, def.ParticleLifeMax));
            m.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);

            // Source-Speed ist in Pixel/Sek. -> Unity-Units / Sek.
            float speedMin = Mathf.Min(def.SpeedMin, def.SpeedMax) / PixelsPerUnit;
            float speedMax = Mathf.Max(def.SpeedMin, def.SpeedMax) / PixelsPerUnit;
            m.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);

            // Start-Größe in Welt-Einheiten = sizeStart * (32px / PPU) * VisualScale.
            m.startSize = SizeUnits * VisualScale * Mathf.Max(0.01f, def.SizeStart);

            // Start-Rotation aus spinStart (rad/sek wird hier als Initial-Winkel
            // verwendet; spin-über-Zeit folgt im RotationOverLifetime-Modul).
            m.startRotation = def.SpinStart;

            // Initial-Farbe = colorStart (RGBA aus JSON).
            m.startColor = MakeColor(def.ColorStart, fallbackA: 1f);

            m.maxParticles = 500; // Source: 500 fix
        }

        private static void ConfigureEmission(ParticleSystem ps, ParticleSystemDefinition def)
        {
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = def.Emission > 0;
            e.rateOverTime = Mathf.Max(0f, def.Emission);
        }

        private static void ConfigureShape(ParticleSystem ps, ParticleSystemDefinition def)
        {
            ParticleSystem.ShapeModule s = ps.shape;
            s.enabled = true;

            // Source 2D: direction=0 = +X (rechts), Y nach unten. In Unity 3D
            // bilden wir die Top-Down-Emission auf die XY-Ebene ab. ShapeModule
            // emittiert entlang +Z; wir rotieren um Z, sodass der Kegel in der
            // XY-Ebene öffnet, dann Y-Flip damit Source-Y (unten) = Unity-Y (unten).
            float spread = Mathf.Max(0f, def.Spread);
            float halfAngleDeg = Mathf.Clamp(spread * Mathf.Rad2Deg * 0.5f, 0f, 180f);
            s.shapeType = ParticleSystemShapeType.Cone;
            s.angle = halfAngleDeg;
            s.radius = 0.01f;
            s.radiusThickness = 1f;

            // Source: direction misst gegen +X. Unity-Cone schaut entlang +Z.
            // Wir wollen Emission in XY-Ebene → drehe -90° um X, sodass +Z auf +Y mappt,
            // dann drehe um Z um den Source-Winkel.
            float dirDeg = def.Direction * Mathf.Rad2Deg;
            s.rotation = new Vector3(-90f, 0f, dirDeg);
        }

        private static void ConfigureSize(ParticleSystem ps, ParticleSystemDefinition def)
        {
            float startSize = Mathf.Max(0.01f, def.SizeStart);
            float endSize = Mathf.Max(0f, def.SizeEnd);
            if (Mathf.Approximately(startSize, endSize)) { return; }

            ParticleSystem.SizeOverLifetimeModule sm = ps.sizeOverLifetime;
            sm.enabled = true;
            AnimationCurve curve = new(
                new Keyframe(0f, 1f),
                new Keyframe(1f, endSize / startSize));
            sm.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void ConfigureRotation(ParticleSystem ps, ParticleSystemDefinition def)
        {
            if (Mathf.Approximately(def.SpinEnd, 0f) && Mathf.Approximately(def.SpinStart, 0f))
            {
                return;
            }
            ParticleSystem.RotationOverLifetimeModule r = ps.rotationOverLifetime;
            r.enabled = true;
            float spin = (def.SpinStart + def.SpinEnd) * 0.5f;
            r.z = new ParticleSystem.MinMaxCurve(spin);
        }

        private static void ConfigureColor(ParticleSystem ps, ParticleSystemDefinition def)
        {
            ParticleSystem.ColorOverLifetimeModule c = ps.colorOverLifetime;
            c.enabled = true;
            Color a = MakeColor(def.ColorStart, fallbackA: 1f);
            Color b = MakeColor(def.ColorEnd, fallbackA: 0f);
            Gradient grad = new();
            grad.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(b.a, 1f) });
            c.color = new ParticleSystem.MinMaxGradient(grad);
        }

        private static void ConfigureForce(ParticleSystem ps, ParticleSystemDefinition def)
        {
            float gMin = def.GravityMin / PixelsPerUnit;
            float gMax = def.GravityMax / PixelsPerUnit;
            if (Mathf.Approximately(gMin, 0f) && Mathf.Approximately(gMax, 0f)) { return; }

            ParticleSystem.ForceOverLifetimeModule f = ps.forceOverLifetime;
            f.enabled = true;
            // Source-Y geht nach unten = Unity-Y nach unten = negativer Y-Force.
            float lo = -Mathf.Max(gMin, gMax);
            float hi = -Mathf.Min(gMin, gMax);
            f.y = new ParticleSystem.MinMaxCurve(lo, hi);
            f.space = ParticleSystemSimulationSpace.World;
        }

        private static void ConfigureRenderer(ParticleSystem ps, ParticleSystemDefinition def)
        {
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.material = def.AddBlend ? GetAdditiveMaterial() : GetAlphaMaterial();
            r.sortingOrder = 100;

            // Atlas-UV: Source-Tile (TileX, TileY) im 32px-Atlas. Wir kennen die
            // tatsächliche Atlas-Auflösung nicht zur Compile-Time → leiten sie
            // aus der gebundenen Textur ab (oder Fallback 4×8).
            Texture mainTex = r.material != null ? r.material.mainTexture : null;
            int atlasW = mainTex != null ? mainTex.width : AtlasTilePixels * AtlasColumns;
            int atlasH = mainTex != null ? mainTex.height : AtlasTilePixels * AtlasRows;

            ParticleSystem.TextureSheetAnimationModule t = ps.textureSheetAnimation;
            t.enabled = true;
            t.mode = ParticleSystemAnimationMode.Grid;
            t.numTilesX = Mathf.Max(1, atlasW / AtlasTilePixels);
            t.numTilesY = Mathf.Max(1, atlasH / AtlasTilePixels);
            int tileCol = Mathf.Clamp(def.TileX / AtlasTilePixels, 0, t.numTilesX - 1);
            int tileRowSrc = Mathf.Clamp(def.TileY / AtlasTilePixels, 0, t.numTilesY - 1);
            // Source-PNG zählt tile_y von OBEN (SFML-Pixel-Koords, top-left origin).
            // Unity ParticleSystem.TextureSheetAnimation zählt Frames von UNTEN-LINKS
            // (Frame 0 = bottom-left, rowIndex 0 = unterste Reihe). Ohne Flip samplen
            // wir die falsche Reihe — typischerweise ein leerer Alpha-Tile, was als
            // weicher tinted Quad ohne Spark-Form erscheint.
            int tileRowUnity = (t.numTilesY - 1) - tileRowSrc;
            t.animation = ParticleSystemAnimationType.SingleRow;
            t.rowMode = ParticleSystemAnimationRowMode.Custom;
            t.rowIndex = tileRowUnity;
            t.startFrame = new ParticleSystem.MinMaxCurve(tileCol);
            t.frameOverTime = new ParticleSystem.MinMaxCurve(tileCol); // hält das Frame
            t.cycleCount = 1;
        }

        private static Color MakeColor(float[] rgba, float fallbackA)
        {
            if (rgba == null || rgba.Length < 3)
            {
                return new Color(1f, 1f, 1f, fallbackA);
            }
            float r = Mathf.Clamp01(rgba[0]);
            float g = Mathf.Clamp01(rgba[1]);
            float b = Mathf.Clamp01(rgba[2]);
            float a = rgba.Length >= 4 ? Mathf.Clamp01(rgba[3]) : fallbackA;
            return new Color(r, g, b, a);
        }

        private static Material GetAdditiveMaterial()
        {
            if (s_AdditiveMaterial != null) { return s_AdditiveMaterial; }
            Shader sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) { sh = Shader.Find("Legacy Shaders/Particles/Additive"); }
            if (sh == null) { sh = Shader.Find("Sprites/Default"); }
            s_AdditiveMaterial = new Material(sh) { name = "Riftstorm_ParticlesAdditive" };
            if (s_AdditiveMaterial.HasProperty("_Mode")) { s_AdditiveMaterial.SetFloat("_Mode", 4f); }
            if (s_AdditiveMaterial.HasProperty("_BlendOp")) { s_AdditiveMaterial.SetFloat("_BlendOp", 0f); }
            if (s_AdditiveMaterial.HasProperty("_SrcBlend")) { s_AdditiveMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); }
            if (s_AdditiveMaterial.HasProperty("_DstBlend")) { s_AdditiveMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); }
            if (s_AdditiveMaterial.HasProperty("_ZWrite")) { s_AdditiveMaterial.SetFloat("_ZWrite", 0f); }
            s_AdditiveMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 100;
            s_AdditiveMaterial.mainTexture = GetAtlasTexture();
            return s_AdditiveMaterial;
        }

        private static Material GetAlphaMaterial()
        {
            if (s_AlphaMaterial != null) { return s_AlphaMaterial; }
            Shader sh = Shader.Find("Particles/Standard Unlit");
            if (sh == null) { sh = Shader.Find("Sprites/Default"); }
            s_AlphaMaterial = new Material(sh) { name = "Riftstorm_ParticlesAlpha" };
            if (s_AlphaMaterial.HasProperty("_Mode")) { s_AlphaMaterial.SetFloat("_Mode", 3f); } // Transparent
            if (s_AlphaMaterial.HasProperty("_SrcBlend")) { s_AlphaMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); }
            if (s_AlphaMaterial.HasProperty("_DstBlend")) { s_AlphaMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
            if (s_AlphaMaterial.HasProperty("_ZWrite")) { s_AlphaMaterial.SetFloat("_ZWrite", 0f); }
            s_AlphaMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            s_AlphaMaterial.mainTexture = GetAtlasTexture();
            return s_AlphaMaterial;
        }

        /// <summary>
        /// Liefert die Source-Partikel-Atlas-Textur. Sucht zuerst
        /// <c>StreamingAssets/particles/particles.png</c> (128×128, 4×4 32px-Tiles);
        /// fällt sonst auf einen prozedural erzeugten Soft-Blob-Atlas in gleicher
        /// Rasterung zurück, sodass die Pipeline auch ohne das Original-Asset
        /// visuell etwas anzeigt.
        /// </summary>
        private static Texture2D GetAtlasTexture()
        {
            if (s_AtlasTexture != null) { return s_AtlasTexture; }

            string atlasPath = System.IO.Path.Combine(
                Application.streamingAssetsPath,
                ParticleSystemCatalogLoader.DefaultSubFolder,
                "particles.png");

            if (System.IO.File.Exists(atlasPath))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(atlasPath);
                    Texture2D real = new(2, 2, TextureFormat.RGBA32, mipChain: false);
                    if (real.LoadImage(bytes))
                    {
                        real.name = "Riftstorm_ParticleAtlas";
                        // Point-Filtering, weil die Source-Tiles bereits weiche Kanten
                        // mit klar abgegrenzten Alpha-Falloffs liefern; Bilinear wuerde
                        // benachbarte Tiles an den Tile-Grenzen ineinander bluten.
                        real.filterMode = FilterMode.Point;
                        real.wrapMode = TextureWrapMode.Clamp;
                        s_AtlasTexture = real;
                        Debug.Log($"[CasterParticleSpawner] Real-Atlas geladen: {atlasPath} ({real.width}×{real.height}, {bytes.Length} bytes) — Tiles: {real.width / AtlasTilePixels}×{real.height / AtlasTilePixels}");
                        return s_AtlasTexture;
                    }
                    // LoadImage hat fehlgeschlagen — Textur wegwerfen, Fallback nehmen.
                    Object.Destroy(real);
                    Debug.LogWarning($"[CasterParticleSpawner] particles.png konnte nicht dekodiert werden, verwende prozeduralen Fallback.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CasterParticleSpawner] Atlas-Laden fehlgeschlagen: {ex.Message}");
                }
            }
            else
            {
                Debug.Log($"[CasterParticleSpawner] particles.png nicht gefunden unter {atlasPath} — prozeduraler Fallback aktiv.");
            }

            s_AtlasTexture = BuildProceduralAtlas();
            return s_AtlasTexture;
        }

        private static Texture2D BuildProceduralAtlas()
        {
            // Atlas wird intern in ProceduralTileTexPixels-Auflösung gerendert
            // (z. B. 128px/Tile), damit die Soft-Blobs beim Hochskalieren auf
            // Welt-Größe weiche Kanten behalten. Die Tile-Koordinaten-Mathematik
            // im Renderer (def.TileX / AtlasTilePixels) bleibt davon unberührt,
            // weil sie das Atlas-RASTER (4×8) zählt, nicht die Pixel-Auflösung.
            int tile = ProceduralTileTexPixels;
            int w = tile * AtlasColumns;
            int h = tile * AtlasRows;
            Texture2D tex = new(w, h, TextureFormat.RGBA32, mipChain: true)
            {
                name = "Riftstorm_ParticleAtlas_Fallback",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 1,
            };
            Color32[] pixels = new Color32[w * h];
            // Effektiver Radius bewusst kleiner als Tile/2, damit die Tile-Ecken
            // garantiert alpha=0 sind → kein quadratisches „Glow-Boxing“ beim
            // additiven Überlagern, runde Disc statt eckiger Halo.
            float radius = tile * 0.42f;
            for (int ty = 0; ty < AtlasRows; ++ty)
            {
                for (int tx = 0; tx < AtlasColumns; ++tx)
                {
                    int ox = tx * tile;
                    int oy = ty * tile;
                    for (int py = 0; py < tile; ++py)
                    {
                        for (int px = 0; px < tile; ++px)
                        {
                            float dx = px - tile * 0.5f + 0.5f;
                            float dy = py - tile * 0.5f + 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            // Smoothstep-Falloff (3t²−2t³) liefert eine
                            // C¹-stetige weiche Disc ohne harte Kern-Hotspots
                            // und ohne sichtbare Tile-Ecken.
                            float t = Mathf.Clamp01(1f - d / radius);
                            float a = t * t * (3f - 2f * t);
                            byte alpha = (byte)(a * 255f);
                            pixels[(oy + py) * w + (ox + px)] = new Color32(255, 255, 255, alpha);
                        }
                    }
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        /// <summary>Verwirft gecachte Materialien/Texturen (z. B. beim Service-Teardown).</summary>
        public static void ClearCache()
        {
            if (s_AdditiveMaterial != null) { Object.Destroy(s_AdditiveMaterial); s_AdditiveMaterial = null; }
            if (s_AlphaMaterial != null) { Object.Destroy(s_AlphaMaterial); s_AlphaMaterial = null; }
            if (s_AtlasTexture != null) { Object.Destroy(s_AtlasTexture); s_AtlasTexture = null; }
        }
    }
}
