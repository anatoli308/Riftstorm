using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Riftstorm.Game.Npc
{
    /// <summary>
    /// Aus MUGEN <c>constants.json</c> destillierte, Riftstorm-freundliche
    /// Charakter-Statwerte. Wird per Python-Konverter (<c>mugen_to_flare.py</c>)
    /// als <c>&lt;AtlasName&gt;.stats.json</c> neben dem FLARE-Manifest abgelegt.
    /// </summary>
    /// <remarks>
    /// Pixel-Werte sind bereits in Meter umgerechnet, Velocities in m/s.
    /// Die Datei ist optional — fehlt sie, fällt der Spawner auf die im
    /// Inspector konfigurierten Defaults zurück.
    /// </remarks>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenCharacterStats
    {
        /// <summary>JSON-Sub-DTO für den Skalierungs-Vektor.</summary>
        [JsonObject(MemberSerialization.OptIn)]
        public sealed class ScaleData
        {
            /// <summary>Horizontale Skala (entspricht MUGEN <c>size.xscale</c>).</summary>
            [JsonProperty("x")] public float X { get; set; } = 1f;
            /// <summary>Vertikale Skala (entspricht MUGEN <c>size.yscale</c>).</summary>
            [JsonProperty("y")] public float Y { get; set; } = 1f;
        }

        // -------------------------------------------------------------------------
        // Identität
        // -------------------------------------------------------------------------

        /// <summary>
        /// Anzeigename des Charakters (aus MUGEN <c>.def</c> <c>info.displayname</c>,
        /// per <c>char.json</c> in den Sidecar gespiegelt). Wird in
        /// <see cref="Combat.UnitStats.DisplayName"/> übernommen
        /// und dient dort als Quelle für Name-Tags und Debug-Labels.
        /// </summary>
        [JsonProperty("displayName")] public string DisplayName { get; set; } = "";

        // -------------------------------------------------------------------------
        // Combat-Kern → UnitStats
        // -------------------------------------------------------------------------

        /// <summary>Maximale HP (MUGEN <c>data.life</c>).</summary>
        [JsonProperty("maxHp")] public int MaxHp { get; set; } = 100;
        /// <summary>Maximale Mana (MUGEN <c>data.power</c>). 0 = ressourcenlos.</summary>
        [JsonProperty("maxMana")] public int MaxMana { get; set; }
        /// <summary>Angriffsstärke (MUGEN <c>data.attack</c>).</summary>
        [JsonProperty("strength")] public int Strength { get; set; }
        /// <summary>Rüstung (MUGEN <c>data.defence</c>).</summary>
        [JsonProperty("armor")] public int Armor { get; set; }

        // -------------------------------------------------------------------------
        // Körper / Visual
        // -------------------------------------------------------------------------

        /// <summary>Hit-Radius in Metern, abgeleitet aus <c>size.ground_front/back</c> × Scale.</summary>
        [JsonProperty("hitRadius")] public float HitRadius { get; set; } = 0.2f;
        /// <summary>Visuelle Sprite-Skalierung am Visuals-Child (MUGEN <c>size.xscale/yscale</c>).</summary>
        [JsonProperty("visualScale")] public ScaleData VisualScale { get; set; } = new();
        /// <summary>Geschätzte Modellhöhe in Metern (nur informativ, z. B. für Floating-Text-Offset).</summary>
        [JsonProperty("height")] public float Height { get; set; }

        // -------------------------------------------------------------------------
        // Movement (m/s)
        // -------------------------------------------------------------------------

        /// <summary>Geh-Geschwindigkeit in m/s (MUGEN <c>velocity.walk_fwd</c> × 60 / PPM).</summary>
        [JsonProperty("walkSpeed")] public float WalkSpeed { get; set; }
        /// <summary>Lauf-Geschwindigkeit in m/s (MUGEN <c>velocity.run_fwd[0]</c> × 60 / PPM).</summary>
        [JsonProperty("runSpeed")] public float RunSpeed { get; set; }

        // -------------------------------------------------------------------------
        // NPC-intrinsische Waffen-Reichweiten
        // -------------------------------------------------------------------------

        /// <summary>Melee-Reichweite in Metern (MUGEN <c>size.attack_dist</c>).</summary>
        [JsonProperty("attackRange")] public float AttackRange { get; set; }
        /// <summary>Projektil-Reichweite in Metern (MUGEN <c>size.proj_attack_dist</c>).</summary>
        [JsonProperty("projectileRange")] public float ProjectileRange { get; set; }

        /// <summary>Provenance: mit welcher Pixel-per-Meter-Konstante konvertiert wurde.</summary>
        [JsonProperty("pixelsPerMeter")] public float PixelsPerMeter { get; set; } = 100f;

        /// <summary>
        /// Aus <c>states.json</c> extrahierte HitDef-Skills, eine Entry pro
        /// <c>HitDef</c>-State-Controller. Leeres Array, wenn keine Skills
        /// gefunden wurden. Reihenfolge entspricht dem MUGEN-Source.
        /// </summary>
        [JsonProperty("skills")] public MugenSkillData[] Skills { get; set; } = System.Array.Empty<MugenSkillData>();

        /// <summary>VisualScale als Unity <see cref="Vector2"/>.</summary>
        public Vector2 VisualScaleVector => new(VisualScale?.X ?? 1f, VisualScale?.Y ?? 1f);

        /// <summary>Lazy-Cache fuer <see cref="GetBasicAttackPool"/>. Wird nach dem JSON-Load gebaut.</summary>
        [JsonIgnore] private MugenSkillData[] m_BasicAttackPoolCache;

        /// <summary>
        /// Liefert die Untermenge der <see cref="Skills"/>, die als Stand-Normal-Attack
        /// (MUGEN <c>attr = "S, NA"</c>) gespielt werden koennen — also der
        /// Auto-Attack-Pool fuer die NPC-AI in Phase B.5.0. Phantom-Eintraege ohne
        /// gueltige Animation (<c>AnimActionId &lt; 0</c> oder leerer
        /// <see cref="MugenSkillData.AnimAlias"/>, typisch z. B. die <c>6061</c>-
        /// Hit-Reaction-Holds) werden herausgefiltert.
        /// </summary>
        /// <remarks>
        /// Das Ergebnis wird einmalig gecached. Der Filter ist absichtlich eng
        /// (nur <c>S, NA</c>), damit Air-Normals, Crouch-Variants, Throws und
        /// Hyper-Specials fuer spaetere Phasen reserviert bleiben.
        /// </remarks>
        public MugenSkillData[] GetBasicAttackPool()
        {
            if (m_BasicAttackPoolCache != null)
            {
                return m_BasicAttackPoolCache;
            }
            if (Skills == null || Skills.Length == 0)
            {
                m_BasicAttackPoolCache = System.Array.Empty<MugenSkillData>();
                return m_BasicAttackPoolCache;
            }
            var list = new System.Collections.Generic.List<MugenSkillData>(Skills.Length);
            for (int i = 0; i < Skills.Length; i++)
            {
                MugenSkillData s = Skills[i];
                if (s == null)
                {
                    continue;
                }
                if (s.AnimActionId < 0)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(s.AnimAlias))
                {
                    continue;
                }
                if (!IsStandNormalAttack(s.Attr))
                {
                    continue;
                }
                list.Add(s);
            }
            m_BasicAttackPoolCache = list.ToArray();
            return m_BasicAttackPoolCache;
        }

        /// <summary>
        /// Pruft, ob ein MUGEN <c>attr</c>-String "S, NA" entspricht (Stand-Normal-Attack),
        /// tolerant gegenueber Whitespace und Case ("s,na", "S , NA", "S, NA").
        /// </summary>
        private static bool IsStandNormalAttack(string attr)
        {
            if (string.IsNullOrEmpty(attr))
            {
                return false;
            }
            // Manuelles Parsen statt Replace/ToUpper: vermeidet zwei String-Allocs
            // pro Skill auch wenn der Pfad nur einmal pro Charakter durchlaeuft.
            int i = 0;
            int len = attr.Length;
            while (i < len && (attr[i] == ' ' || attr[i] == '\t')) i++;
            if (i >= len) return false;
            char pos = attr[i];
            if (pos != 'S' && pos != 's') return false;
            i++;
            while (i < len && (attr[i] == ' ' || attr[i] == '\t')) i++;
            if (i >= len || attr[i] != ',') return false;
            i++;
            while (i < len && (attr[i] == ' ' || attr[i] == '\t')) i++;
            if (i + 1 >= len) return false;
            char a = attr[i];
            char b = attr[i + 1];
            return (a == 'N' || a == 'n') && (b == 'A' || b == 'a');
        }
    }

    /// <summary>
    /// Ein aus einem MUGEN <c>HitDef</c>-State-Controller extrahierter Skill.
    /// Felder spiegeln die Original-Parameter wider — die Riftstorm-Combat-
    /// Schicht entscheidet eigenständig, welche davon sie auf ihre eigene
    /// Ability-Pipeline mappt.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MugenSkillData
    {
        /// <summary>MUGEN-State-Nummer, in dem der HitDef lebt (z. B. 200 = Stand Light Punch).</summary>
        [JsonProperty("stateNo")] public int StateNo { get; set; }
        /// <summary>MUGEN-Action-ID aus dem State-Header (<c>anim</c>). -1, wenn nicht gesetzt.</summary>
        [JsonProperty("animActionId")] public int AnimActionId { get; set; } = -1;
        /// <summary>FLARE-Alias der Action (z. B. "swing"), falls in <c>_ACTION_ALIASES</c> bekannt.</summary>
        [JsonProperty("animAlias")] public string AnimAlias { get; set; } = "";
        /// <summary>1-basierter MUGEN-Frame-Index, ab dem der Hit scharf wird (aus <c>AnimElem</c>-Trigger). 0 = unbekannt.</summary>
        [JsonProperty("hitOnFrame")] public int HitOnFrame { get; set; }

        /// <summary>Schaden bei Treffer (MUGEN <c>damage</c> [0]).</summary>
        [JsonProperty("damage")] public int Damage { get; set; }
        /// <summary>Schaden bei Block (MUGEN <c>damage</c> [1]).</summary>
        [JsonProperty("guardDamage")] public int GuardDamage { get; set; }

        /// <summary>MUGEN <c>attr</c> ("S, NA" = Stand/Normal Attack).</summary>
        [JsonProperty("attr")] public string Attr { get; set; } = "";
        /// <summary>MUGEN <c>hitflag</c> (z. B. "MAF" = hits Mid/Air/Fallen).</summary>
        [JsonProperty("hitFlag")] public string HitFlag { get; set; } = "";
        /// <summary>MUGEN <c>guardflag</c> (z. B. "MA" = blockable Mid/Air).</summary>
        [JsonProperty("guardFlag")] public string GuardFlag { get; set; } = "";
        /// <summary>MUGEN <c>animtype</c> (Light/Medium/Hard/...).</summary>
        [JsonProperty("animType")] public string AnimType { get; set; } = "";
        /// <summary>MUGEN <c>priority</c> roh ("3, Hit").</summary>
        [JsonProperty("priority")] public string Priority { get; set; } = "";

        /// <summary>Pause-Frames bei Treffer (MUGEN <c>pausetime</c> [0]).</summary>
        [JsonProperty("pauseTimeHit")] public int PauseTimeHit { get; set; }
        /// <summary>Pause-Frames bei Block (MUGEN <c>pausetime</c> [1]).</summary>
        [JsonProperty("pauseTimeGuard")] public int PauseTimeGuard { get; set; }

        /// <summary>MUGEN <c>ground.type</c> (High/Low/Trip/...).</summary>
        [JsonProperty("groundType")] public string GroundType { get; set; } = "";
        /// <summary>MUGEN <c>air.type</c>.</summary>
        [JsonProperty("airType")] public string AirType { get; set; } = "";
        /// <summary>MUGEN <c>ground.slidetime</c> (Frames).</summary>
        [JsonProperty("groundSlideTime")] public int GroundSlideTime { get; set; }
        /// <summary>MUGEN <c>ground.hittime</c> (Frames).</summary>
        [JsonProperty("groundHitTime")] public int GroundHitTime { get; set; }
        /// <summary>MUGEN <c>air.hittime</c> (Frames).</summary>
        [JsonProperty("airHitTime")] public int AirHitTime { get; set; }

        /// <summary>Knockback X bei Bodentreffer (MUGEN-Pixel/Tick).</summary>
        [JsonProperty("groundVelocityX")] public float GroundVelocityX { get; set; }
        /// <summary>Knockback Y bei Bodentreffer.</summary>
        [JsonProperty("groundVelocityY")] public float GroundVelocityY { get; set; }
        /// <summary>Knockback X bei Lufttreffer.</summary>
        [JsonProperty("airVelocityX")] public float AirVelocityX { get; set; }
        /// <summary>Knockback Y bei Lufttreffer.</summary>
        [JsonProperty("airVelocityY")] public float AirVelocityY { get; set; }
        /// <summary>Knockback X bei Luft-Block.</summary>
        [JsonProperty("airGuardVelocityX")] public float AirGuardVelocityX { get; set; }
        /// <summary>Knockback Y bei Luft-Block.</summary>
        [JsonProperty("airGuardVelocityY")] public float AirGuardVelocityY { get; set; }

        /// <summary>MUGEN <c>sparkno</c> (Treffer-FX-ID).</summary>
        [JsonProperty("sparkNo")] public string SparkNo { get; set; } = "";
        /// <summary>MUGEN <c>hitsound</c> ("5, 0").</summary>
        [JsonProperty("hitSound")] public string HitSound { get; set; } = "";
        /// <summary>MUGEN <c>guardsound</c>.</summary>
        [JsonProperty("guardSound")] public string GuardSound { get; set; } = "";
        /// <summary>MUGEN <c>p2stateno</c> — Target-Forced-State bei Treffer. 0 = unset.</summary>
        [JsonProperty("p2stateno")] public int P2StateNo { get; set; }
        /// <summary>MUGEN <c>fall</c>-Flag — Treffer wirft das Ziel um.</summary>
        [JsonProperty("fall")] public bool Fall { get; set; }
    }

    /// <summary>
    /// Synchrones Lade-Utility für <see cref="MugenCharacterStats"/> aus
    /// <c>StreamingAssets/&lt;subFolder&gt;/&lt;AtlasName&gt;.stats.json</c>.
    /// </summary>
    /// <remarks>
    /// Pure Service ohne MonoBehaviour-Abhängigkeit. Bewusst kein Cache — pro
    /// NPC-Spawn wird die Datei einmal gelesen (kleines JSON, ~400 Bytes).
    /// Falls später viele identische NPCs gespawnt werden, kann hier ein
    /// Per-Path-Cache nachgerüstet werden.
    /// </remarks>
    public static class MugenCharacterStatsLoader
    {
        /// <summary>
        /// Lädt die Stats-Sidecar-Datei. Liefert <c>null</c>, wenn die Datei
        /// nicht existiert oder das JSON kaputt ist — der Aufrufer behält
        /// dann seine Inspector-Defaults.
        /// </summary>
        /// <param name="streamingSubFolder">Pfad relativ zu <see cref="Application.streamingAssetsPath"/>.</param>
        /// <param name="atlasName">Atlas-/Charakter-Name ohne Extension.</param>
        public static MugenCharacterStats LoadOrNull(string streamingSubFolder, string atlasName)
        {
            if (string.IsNullOrEmpty(streamingSubFolder) || string.IsNullOrEmpty(atlasName))
            {
                return null;
            }

            string path = Path.Combine(
                Application.streamingAssetsPath,
                streamingSubFolder,
                atlasName + ".stats.json");

            if (!File.Exists(path))
            {
                Debug.Log($"[MugenCharacterStatsLoader] Keine Stats-Sidecar gefunden ({path}) - Inspector-Defaults aktiv.");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                MugenCharacterStats stats = JsonConvert.DeserializeObject<MugenCharacterStats>(json);
                if (stats == null)
                {
                    Debug.LogWarning($"[MugenCharacterStatsLoader] Leeres oder ungültiges Stats-JSON: {path}");
                    return null;
                }
                Debug.Log($"[MugenCharacterStatsLoader] Stats geladen: {path} " +
                          $"(hp={stats.MaxHp} mp={stats.MaxMana} str={stats.Strength} arm={stats.Armor} " +
                          $"radius={stats.HitRadius}m walk={stats.WalkSpeed}m/s run={stats.RunSpeed}m/s " +
                          $"skills={(stats.Skills != null ? stats.Skills.Length : 0)})");
                return stats;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MugenCharacterStatsLoader] Fehler beim Laden von {path}: {ex.Message}");
                return null;
            }
        }
    }
}
