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

        /// <summary>
        /// Obergrenze (ms) für eine einzelne Frame-Anzeigedauer. Kappt defekte AIR-Ticks,
        /// die sonst als absurde Werte in der JSON stehen und den Frame endlos einfrieren.
        /// </summary>
        private const long MaxFrameMs = 10_000;

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

            Texture2D[] textures = await LoadTexturesAsync(def);
            FlareAtlas atlas = BuildAtlas(atlasName, textures, def);
            m_Cache[atlasName] = atlas;
            return atlas;
        }

        /// <summary>
        /// Lädt alle Atlas-Seiten. Nutzt <see cref="FlareAtlasDef.Images"/> (mehrseitig)
        /// und fällt auf <see cref="FlareAtlasDef.Image"/> zurück (einseitige Alt-Atlanten).
        /// Fehlende Seiten bleiben als <c>null</c> im Array, damit Seitenindizes stimmen.
        /// </summary>
        private async Task<Texture2D[]> LoadTexturesAsync(FlareAtlasDef def)
        {
            string[] files = def.Images != null && def.Images.Length > 0
                ? def.Images
                : new[] { def.Image };
            Texture2D[] textures = new Texture2D[files.Length];
            for (int i = 0; i < files.Length; i++)
            {
                if (string.IsNullOrEmpty(files[i]))
                {
                    continue;
                }
                textures[i] = await LoadTextureAsync(Path.Combine(m_TextureFolder, files[i]));
            }
            return textures;
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
                if (atlas.Textures != null)
                {
                    foreach (Texture2D tex in atlas.Textures)
                    {
                        if (tex != null)
                        {
                            Object.Destroy(tex);
                        }
                    }
                }
            }
            m_Cache.Clear();
        }

        private static FlareAtlas BuildAtlas(string name, Texture2D[] textures, FlareAtlasDef def)
        {
            Dictionary<string, FlareAnimation> animations = new(def.Animations.Count);
            int pageCount = textures != null ? textures.Length : 0;
            bool anyTexture = false;
            for (int i = 0; i < pageCount; i++)
            {
                if (textures[i] != null)
                {
                    anyTexture = true;
                    break;
                }
            }

            foreach (KeyValuePair<string, FlareAnimationDef> kvp in def.Animations)
            {
                FlareAnimationDef ad = kvp.Value;
                if (ad?.Frames == null || ad.FramesCount <= 0)
                {
                    continue;
                }
                Sprite[][] sprites = new Sprite[ad.FramesCount][];
                FlareCell[][] cells = new FlareCell[ad.FramesCount][];
                bool[][] flipH = null;
                bool[][] flipV = null;
                bool anyFlipH = false;
                bool anyFlipV = false;
                for (int f = 0; f < ad.FramesCount && f < ad.Frames.Count; f++)
                {
                    List<FlareCell> row = ad.Frames[f];
                    Sprite[] dirs = new Sprite[FlareDirection.Count];
                    FlareCell[] cellRow = new FlareCell[FlareDirection.Count];
                    bool[] rowFlipH = null;
                    bool[] rowFlipV = null;
                    if (row != null && anyTexture)
                    {
                        int n = Mathf.Min(FlareDirection.Count, row.Count);
                        for (int d = 0; d < n; d++)
                        {
                            FlareCell cell = row[d];
                            cellRow[d] = cell;
                            Texture2D pageTexture = ResolvePageTexture(textures, cell);
                            dirs[d] = BuildSprite(pageTexture, cell);
                            if (cell != null && cell.FlipH)
                            {
                                rowFlipH ??= new bool[FlareDirection.Count];
                                rowFlipH[d] = true;
                                anyFlipH = true;
                            }
                            if (cell != null && cell.FlipV)
                            {
                                rowFlipV ??= new bool[FlareDirection.Count];
                                rowFlipV[d] = true;
                                anyFlipV = true;
                            }
                        }
                    }
                    sprites[f] = dirs;
                    cells[f] = cellRow;
                    if (rowFlipH != null)
                    {
                        flipH ??= new bool[ad.FramesCount][];
                        flipH[f] = rowFlipH;
                    }
                    if (rowFlipV != null)
                    {
                        flipV ??= new bool[ad.FramesCount][];
                        flipV[f] = rowFlipV;
                    }
                }
                float[] frameDurations = null;
                if (ad.FrameDurationsMs != null && ad.FrameDurationsMs.Length == ad.FramesCount)
                {
                    frameDurations = new float[ad.FramesCount];
                    for (int f = 0; f < ad.FramesCount; f++)
                    {
                        // Klammern: defekte AIR-Ticks (Int32-Overflow im JSON) auf einen
                        // spielbaren Wert kappen, sonst friert ein Frame quasi endlos ein.
                        long ms = System.Math.Min(ad.FrameDurationsMs[f], MaxFrameMs);
                        // Defensive: ein Tick als Minimum, damit Frame nicht "verschluckt" wird.
                        frameDurations[f] = Mathf.Max(1f / 60f, ms / 1000f);
                    }
                }
                float durationSeconds;
                if (frameDurations != null)
                {
                    // Per-Frame-Dauer ist Wahrheitsquelle; DurationSeconds = Summe.
                    float sum = 0f;
                    for (int f = 0; f < frameDurations.Length; f++)
                    {
                        sum += frameDurations[f];
                    }
                    durationSeconds = Mathf.Max(0.001f, sum);
                }
                else
                {
                    long totalMs = System.Math.Min(ad.DurationMs, (long)MaxFrameMs * System.Math.Max(1, ad.FramesCount));
                    durationSeconds = Mathf.Max(0.001f, totalMs / 1000f);
                }
                animations[kvp.Key] = new FlareAnimation(
                    kvp.Key,
                    ad.Label,
                    ad.Type,
                    durationSeconds,
                    sprites,
                    cells,
                    anyFlipH ? flipH : null,
                    anyFlipV ? flipV : null,
                    frameDurations,
                    BuildFrameBlends(ad.FrameBlends),
                    ad.LoopStart);
            }

            return new FlareAtlas(name, textures, animations, BuildSequences(def.Sequences, animations))
            {
                Gravity = def.Gravity,
                StandFriction = def.StandFriction,
                CrouchFriction = def.CrouchFriction,
            };
        }

        /// <summary>
        /// Übersetzt die AIR-Blend-Flags in Blend-Zustände. <c>null</c>, wenn die
        /// Animation durchgehend normal gezeichnet wird — dann muss der Animator
        /// pro Frame nichts umschalten.
        /// </summary>
        private static FlareEffectBlend[] BuildFrameBlends(int[][] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return null;
            }
            FlareEffectBlend[] blends = new FlareEffectBlend[defs.Length];
            bool any = false;
            for (int i = 0; i < defs.Length; i++)
            {
                int[] entry = defs[i];
                int op = entry != null && entry.Length > 0 ? entry[0] : 0;
                int src = entry != null && entry.Length > 1 ? entry[1] : 255;
                int dst = entry != null && entry.Length > 2 ? entry[2] : 0;
                blends[i] = new FlareEffectBlend(op switch { 1 => "add", 2 => "sub", _ => "none" }, src, dst);
                any |= op != 0;
            }
            return any ? blends : null;
        }

        /// <summary>
        /// Übersetzt die aus <c>states.json</c> abgeleiteten Sequenz-Definitionen in
        /// Laufzeit-Objekte. Es werden nur Animationsnamen behalten, die tatsächlich
        /// im Atlas gebaut wurden; Sequenzen ohne verbleibende Body-Animation werden
        /// verworfen, damit die Anzeige nie auf fehlende Clips zeigt.
        /// </summary>
        private static List<FlareSequence> BuildSequences(
            FlareSequenceDef[] defs,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            List<FlareSequence> sequences = new(defs != null ? defs.Length : 0);
            if (defs == null)
            {
                return sequences;
            }
            foreach (FlareSequenceDef sd in defs)
            {
                if (sd == null)
                {
                    continue;
                }
                FlareSequenceAnim[] body = BuildBody(sd.Anims, animations);
                FlareSequenceEffect[] effects = BuildEffects(sd.Effects, animations);
                FlareSequenceAnim[] victim = BuildBody(sd.Victim, animations);
                if (body.Length == 0 && effects.Length == 0 && victim.Length == 0)
                {
                    continue;
                }
                sequences.Add(new FlareSequence
                {
                    State = sd.State,
                    Label = sd.Label,
                    Group = sd.Group,
                    Category = string.IsNullOrEmpty(sd.Category) ? "Custom" : sd.Category,
                    Anims = body,
                    Effects = effects,
                    Sounds = BuildSounds(sd.Sounds),
                    PalFx = BuildPalFx(sd.PalFx),
                    Pauses = BuildPauses(sd.Pauses),
                    Moves = BuildMoves(sd.Moves),
                    Hits = BuildHits(sd.Hits, animations),
                    AfterImage = BuildAfterImage(sd.AfterImage),
                    Modifies = BuildModifies(sd.Modifies),
                    BodyTrans = BuildBodyTrans(sd.BodyTrans),
                    Victim = victim,
                    SprPriority = BuildSprPriority(sd.SprPriority),
                    BodyAngle = BuildBodyAngle(sd.BodyAngle),
                    Helpers = BuildHelpers(sd.Helpers, animations),
                    Airborne = sd.Airborne != 0,
                    Physics = ParsePhysics(sd.Physics),
                    PhysicsChanges = BuildPhysicsChanges(sd.PhysicsChanges),
                    Turns = BuildTimeMarkers(sd.Turns),
                    Invisible = BuildTimeMarkers(sd.Invisible),
                });
            }
            return sequences;
        }

        /// <summary>Übersetzt eine reine Zeitpunkt-Spur in Ticks.</summary>
        private static int[] BuildTimeMarkers(FlareTimeMarkerDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<int>();
            }
            int[] result = new int[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                result[i] = defs[i].Time;
            }
            return result;
        }

        /// <summary>Übersetzt die Helfer-Bindungen an den Charakter.</summary>
        private static FlareSequenceBind[] BuildBinds(FlareBindDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceBind>();
            }
            FlareSequenceBind[] result = new FlareSequenceBind[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                int[] pos = defs[i].Pos;
                result[i] = new FlareSequenceBind(
                    defs[i].Time,
                    new Vector2(
                        pos != null && pos.Length > 0 ? pos[0] : 0f,
                        pos != null && pos.Length > 1 ? pos[1] : 0f),
                    defs[i].Duration);
            }
            return result;
        }

        /// <summary>Übersetzt die <c>StateTypeSet</c>-Physikwechsel.</summary>
        private static FlareSequencePhysics[] BuildPhysicsChanges(FlarePhysicsChangeDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequencePhysics>();
            }
            FlareSequencePhysics[] result = new FlareSequencePhysics[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                result[i] = new FlareSequencePhysics(defs[i].Time, ParsePhysics(defs[i].Value));
            }
            return result;
        }

        /// <summary>Übersetzt <c>physics</c> aus dem Statedef-Kopf.</summary>
        private static FlarePhysics ParsePhysics(string physics)
        {
            if (string.IsNullOrEmpty(physics))
            {
                return FlarePhysics.None;
            }
            switch (char.ToUpperInvariant(physics[0]))
            {
                case 'A': return FlarePhysics.Air;
                case 'S': return FlarePhysics.Stand;
                case 'C': return FlarePhysics.Crouch;
                default: return FlarePhysics.None;
            }
        }

        /// <summary>Übersetzt die Helfer-Akteure in Laufzeit-Objekte.</summary>
        private static FlareSequenceHelper[] BuildHelpers(
            FlareHelperDef[] defs,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceHelper>();
            }
            List<FlareSequenceHelper> kept = new(defs.Length);
            foreach (FlareHelperDef hd in defs)
            {
                FlareSequenceHelper helper = new()
                {
                    Time = hd.Time,
                    PosX = hd.Pos != null && hd.Pos.Length > 0 ? hd.Pos[0] : 0,
                    PosY = hd.Pos != null && hd.Pos.Length > 1 ? hd.Pos[1] : 0,
                    Anims = BuildBody(hd.Anims, animations),
                    Effects = BuildEffects(hd.Effects, animations),
                    Sounds = BuildSounds(hd.Sounds),
                    Hits = BuildHits(hd.Hits, animations),
                    Binds = BuildBinds(hd.Binds),
                    SprPriority = hd.SprPriority,
                    Motion = new FlareEffectMotion(
                        ToVector(hd.Velocity, Vector2.zero),
                        ToVector(hd.Acceleration, Vector2.zero),
                        Vector2.one,
                        bindTicks: 0,
                        angle: 0f),
                };
                if (helper.Anims.Length > 0 || helper.Effects.Length > 0
                    || helper.Sounds.Length > 0 || helper.Hits.Length > 0)
                {
                    kept.Add(helper);
                }
            }
            return kept.ToArray();
        }

        /// <summary>
        /// Übersetzt die Prioritätsspur. Ohne Eintrag bei Tick 0 gilt bis zum
        /// ersten Wechsel MUGENs Vorgabe 0.
        /// </summary>
        private static FlareSequenceSprPriority[] BuildSprPriority(FlareSprPriorityDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceSprPriority>();
            }
            FlareSequenceSprPriority[] result = new FlareSequenceSprPriority[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                result[i] = new FlareSequenceSprPriority(defs[i].Time, defs[i].Value);
            }
            return result;
        }

        /// <summary>Übersetzt die Winkelspur des Charakters.</summary>
        private static FlareSequenceBodyAngle[] BuildBodyAngle(FlareBodyAngleDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceBodyAngle>();
            }
            FlareSequenceBodyAngle[] result = new FlareSequenceBodyAngle[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlareBodyAngleDef ad = defs[i];
                float sx = ad.Scale != null && ad.Scale.Length > 0 ? ad.Scale[0] : 1f;
                float sy = ad.Scale != null && ad.Scale.Length > 1 ? ad.Scale[1] : 1f;
                result[i] = new FlareSequenceBodyAngle(ad.Time, ad.Angle, new Vector2(sx, sy));
            }
            return result;
        }

        /// <summary>Übersetzt die Transparenzwechsel des Charakters.</summary>
        private static FlareSequenceBodyTrans[] BuildBodyTrans(FlareBodyTransDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceBodyTrans>();
            }
            FlareSequenceBodyTrans[] result = new FlareSequenceBodyTrans[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlareBodyTransDef td = defs[i];
                int aSrc = td.Alpha != null && td.Alpha.Length > 0 ? td.Alpha[0] : 255;
                int aDst = td.Alpha != null && td.Alpha.Length > 1 ? td.Alpha[1] : 0;
                result[i] = new FlareSequenceBodyTrans(
                    td.Time, new FlareEffectBlend(td.Trans, aSrc, aDst));
            }
            return result;
        }

        /// <summary>Übersetzt die Nachzieh-Definitionen in Laufzeit-Objekte.</summary>
        private static FlareSequenceAfterImage[] BuildAfterImage(FlareAfterImageDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceAfterImage>();
            }
            FlareSequenceAfterImage[] result = new FlareSequenceAfterImage[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlareAfterImageDef ad = defs[i];
                int aSrc = ad.Alpha != null && ad.Alpha.Length > 0 ? ad.Alpha[0] : 255;
                int aDst = ad.Alpha != null && ad.Alpha.Length > 1 ? ad.Alpha[1] : 128;
                result[i] = new FlareSequenceAfterImage(
                    ad.Time, ad.Duration, ad.Retime != 0, ad.Length, ad.TimeGap, ad.FrameGap,
                    ToVector3(ad.PalBright, new Vector3(30f, 30f, 30f)),
                    ToVector3(ad.PalContrast, new Vector3(120f, 120f, 220f)),
                    ToVector3(ad.PalPostBright, Vector3.zero),
                    ad.PalColor, ad.PalInvertAll != 0,
                    ToVector3(ad.PalAdd, new Vector3(10f, 10f, 25f)),
                    ToVector3f(ad.PalMul, new Vector3(0.65f, 0.65f, 0.75f)),
                    new FlareEffectBlend(ad.Trans, aSrc, aDst));
            }
            return result;
        }

        /// <summary>Übersetzt die Effekt-Änderungen in Laufzeit-Objekte.</summary>
        private static FlareSequenceModify[] BuildModifies(FlareModifyDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceModify>();
            }
            FlareSequenceModify[] result = new FlareSequenceModify[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlareModifyDef md = defs[i];
                result[i] = new FlareSequenceModify(
                    md.Time, md.Id,
                    md.Scale != null ? ToVector(md.Scale, Vector2.one) : null,
                    md.SprPriority, md.RemoveTime, md.Angle,
                    md.Vel != null ? ToVector(md.Vel, Vector2.zero) : null,
                    md.Accel != null ? ToVector(md.Accel, Vector2.zero) : null);
            }
            return result;
        }

        /// <summary>Liest ein Float-Tripel aus der JSON; fehlende Kanäle behalten den Default.</summary>
        private static Vector3 ToVector3f(float[] values, Vector3 fallback)
        {
            if (values == null)
            {
                return fallback;
            }
            return new Vector3(
                values.Length > 0 ? values[0] : fallback.x,
                values.Length > 1 ? values[1] : fallback.y,
                values.Length > 2 ? values[2] : fallback.z);
        }

        /// <summary>
        /// Übersetzt die Treffer-Definitionen. Funken ohne Animation im Atlas
        /// werden fallengelassen, der Treffer bleibt aber wegen seines Tons erhalten.
        /// </summary>
        private static FlareSequenceHit[] BuildHits(
            FlareHitDef[] defs,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceHit>();
            }
            FlareSequenceHit[] result = new FlareSequenceHit[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                result[i] = BuildHit(defs[i], animations);
            }
            return result;
        }

        /// <summary>Übersetzt einen einzelnen Treffer; ein Funke ohne Animation entfällt.</summary>
        private static FlareSequenceHit BuildHit(
            FlareHitDef hd,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            string spark = hd.Spark != null && animations.ContainsKey(hd.Spark) ? hd.Spark : null;
            int px = hd.SparkPos != null && hd.SparkPos.Length > 0 ? hd.SparkPos[0] : 0;
            int py = hd.SparkPos != null && hd.SparkPos.Length > 1 ? hd.SparkPos[1] : 0;
            return new FlareSequenceHit(
                hd.Time, spark, px, py,
                hd.Sound != null ? hd.Sound.Group : -1,
                hd.Sound != null ? hd.Sound.Index : 0,
                hd.PauseTime,
                new FlareVictimReaction(
                    hd.AnimType, hd.GroundType,
                    ToVector2(hd.GroundVel), ToVector2(hd.AirVel),
                    hd.Fall != 0, hd.HitTime));
        }

        /// <summary>Liest ein [x, y]-Paar; fehlende Achsen bleiben 0.</summary>
        private static Vector2 ToVector2(float[] pair)
        {
            if (pair == null)
            {
                return Vector2.zero;
            }
            return new Vector2(
                pair.Length > 0 ? pair[0] : 0f,
                pair.Length > 1 ? pair[1] : 0f);
        }

        /// <summary>Übersetzt die Pausen-Definitionen in Laufzeit-Objekte.</summary>
        private static FlareSequencePause[] BuildPauses(FlarePauseDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequencePause>();
            }
            FlareSequencePause[] result = new FlareSequencePause[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                result[i] = new FlareSequencePause(defs[i].Time, defs[i].Duration);
            }
            return result;
        }

        /// <summary>Übersetzt die Bewegungs-Controller; unbekannte Typen entfallen.</summary>
        private static FlareSequenceMove[] BuildMoves(FlareMoveDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceMove>();
            }
            List<FlareSequenceMove> kept = new(defs.Length);
            foreach (FlareMoveDef md in defs)
            {
                if (TryParseMoveKind(md.Type, out FlareMoveKind kind))
                {
                    kept.Add(new FlareSequenceMove(md.Time, kind, md.X, md.Y));
                }
            }
            return kept.ToArray();
        }

        private static bool TryParseMoveKind(string type, out FlareMoveKind kind)
        {
            switch (type)
            {
                case "velset": kind = FlareMoveKind.VelSet; return true;
                case "veladd": kind = FlareMoveKind.VelAdd; return true;
                case "velmul": kind = FlareMoveKind.VelMul; return true;
                case "posset": kind = FlareMoveKind.PosSet; return true;
                case "posadd": kind = FlareMoveKind.PosAdd; return true;
                default: kind = default; return false;
            }
        }

        /// <summary>Übersetzt die Farbfilter-Definitionen in Laufzeit-Objekte.</summary>
        private static FlareSequencePalFx[] BuildPalFx(FlarePalFxDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequencePalFx>();
            }
            FlareSequencePalFx[] result = new FlareSequencePalFx[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlarePalFxDef pd = defs[i];
                result[i] = new FlareSequencePalFx(
                    pd.Time, pd.Duration, pd.Scope == "all",
                    ToVector3(pd.Add, Vector3.zero),
                    ToVector3(pd.Mul, new Vector3(256f, 256f, 256f)),
                    pd.Color, pd.InvertAll != 0,
                    ToVector3(pd.SinAdd, Vector3.zero), pd.SinPeriod);
            }
            return result;
        }

        /// <summary>Liest ein RGB-Tripel aus der JSON; fehlende Kanäle behalten den Default.</summary>
        private static Vector3 ToVector3(int[] values, Vector3 fallback)
        {
            if (values == null)
            {
                return fallback;
            }
            return new Vector3(
                values.Length > 0 ? values[0] : fallback.x,
                values.Length > 1 ? values[1] : fallback.y,
                values.Length > 2 ? values[2] : fallback.z);
        }

        /// <summary>Übersetzt die <c>PlaySnd</c>-Definitionen in Laufzeit-Objekte.</summary>
        private static FlareSequenceSound[] BuildSounds(FlareSoundDef[] defs)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceSound>();
            }
            FlareSequenceSound[] sounds = new FlareSequenceSound[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                FlareSoundDef sd = defs[i];
                sounds[i] = new FlareSequenceSound(
                    sd.Group, sd.Index, sd.Time, sd.Period, sd.Volume, sd.Loop != 0);
            }
            return sounds;
        }

        /// <summary>
        /// Übersetzt die Effekt-Definitionen in Laufzeit-Objekte und behält nur
        /// jene, deren Animation tatsächlich im Atlas gebaut wurde.
        /// </summary>
        private static FlareSequenceEffect[] BuildEffects(
            FlareEffectDef[] defs,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceEffect>();
            }
            List<FlareSequenceEffect> kept = new(defs.Length);
            foreach (FlareEffectDef ed in defs)
            {
                if (ed == null || ed.Anim == null || !animations.ContainsKey(ed.Anim))
                {
                    continue;
                }
                int px = ed.Pos != null && ed.Pos.Length > 0 ? ed.Pos[0] : 0;
                int py = ed.Pos != null && ed.Pos.Length > 1 ? ed.Pos[1] : 0;
                float sx = ed.Scale != null && ed.Scale.Length > 0 ? ed.Scale[0] : 1f;
                float sy = ed.Scale != null && ed.Scale.Length > 1 ? ed.Scale[1] : 1f;
                int aSrc = ed.Alpha != null && ed.Alpha.Length > 0 ? ed.Alpha[0] : 255;
                int aDst = ed.Alpha != null && ed.Alpha.Length > 1 ? ed.Alpha[1] : 0;
                FlareEffectMotion motion = new(
                    ToVector(ed.Vel, Vector2.zero),
                    ToVector(ed.Accel, Vector2.zero),
                    ToVector(ed.Friction, Vector2.one),
                    ed.BindTime,
                    ed.Angle);
                kept.Add(new FlareSequenceEffect(
                    ed.Anim, ed.Id, ed.Time, ed.Period, px, py, ed.PosType, ed.SprPriority, ed.Layer,
                    sx, sy, ed.Facing, ed.VFacing, ed.RemoveTime, ed.RemoveAt ?? -1, motion,
                    new FlareEffectBlend(ed.Trans, aSrc, aDst),
                    ed.Hit != null ? BuildHit(ed.Hit, animations) : (FlareSequenceHit?)null,
                    ed.HitAnim != null && animations.ContainsKey(ed.HitAnim) ? ed.HitAnim : null));
            }
            return kept.ToArray();
        }

        /// <summary>Liest ein Zahlenpaar aus der JSON; fehlende Achsen behalten den Default.</summary>
        private static Vector2 ToVector(float[] values, Vector2 fallback)
        {
            if (values == null)
            {
                return fallback;
            }
            return new Vector2(
                values.Length > 0 ? values[0] : fallback.x,
                values.Length > 1 ? values[1] : fallback.y);
        }

        /// <summary>Behält nur die Namen, für die eine geladene Animation existiert.</summary>
        private static FlareSequenceAnim[] BuildBody(
            FlareSequenceAnimDef[] defs,
            IReadOnlyDictionary<string, FlareAnimation> animations)
        {
            if (defs == null || defs.Length == 0)
            {
                return System.Array.Empty<FlareSequenceAnim>();
            }
            List<FlareSequenceAnim> kept = new(defs.Length);
            foreach (FlareSequenceAnimDef ad in defs)
            {
                if (ad != null && ad.Anim != null && animations.ContainsKey(ad.Anim))
                {
                    kept.Add(new FlareSequenceAnim(ad.Anim, ad.Time));
                }
            }
            return kept.ToArray();
        }

        /// <summary>
        /// Löst die Textur der Atlas-Seite auf, auf der die Zelle liegt. Fällt bei
        /// ungültigem/fehlendem Index defensiv auf Seite 0 zurück.
        /// </summary>
        private static Texture2D ResolvePageTexture(Texture2D[] textures, FlareCell cell)
        {
            if (textures == null || textures.Length == 0)
            {
                return null;
            }
            int page = cell != null ? cell.AtlasPage : 0;
            if (page < 0 || page >= textures.Length)
            {
                page = 0;
            }
            return textures[page];
        }

        private static Sprite BuildSprite(Texture2D texture, FlareCell cell)
        {
            if (texture == null || cell == null || cell.W <= 0 || cell.H <= 0)
            {
                return null;
            }
            // FLARE benutzt Top-Left als Origin; Unity-Sprite-Rect ist Bottom-Left.
            float rectY = texture.height - cell.Y - cell.H;
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
