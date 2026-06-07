using Riftstorm.Game.Combat;
using Riftstorm.Game.Input;
using Riftstorm.Game.Sprites;
using Riftstorm.Gameplay.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Movement
{
[RequireComponent(typeof(MobaCommandController))]
    /// <summary>
    /// Server-authoritative Topdown-Bewegung mit Client Prediction und Reconciliation.
    /// Drei Rollen teilen sich eine Komponente:
    ///
    /// - <b>Owner-Client</b>: liest LoL-Style RMB-Klick via <see cref="MobaCommandController"/>, simuliert lokal sofort (Prediction), legt
    ///   jeden Command in einen Ringbuffer und schickt ihn via
    ///   <see cref="SubmitCommandServerRpc"/> an den Server. Auf Ack vergleicht er die
    ///   gespeicherte Vorhersage mit der autoritativen Position und re-simuliert alle
    ///   noch nicht bestaetigten Commands, falls die Abweichung > 5 cm ist.
    /// - <b>Server</b>: simuliert mit identischer Formel, schreibt <c>transform.position</c>,
    ///   schickt <see cref="ServerMovementAck"/> per <see cref="ReceiveAckClientRpc"/>
    ///   zurueck an den Owner und pflegt <see cref="m_ServerPosition"/> fuer Remote-Clients.
    /// - <b>Remote-Client</b>: interpoliert <c>transform.position</c> sanft Richtung
    ///   <see cref="m_ServerPosition"/>. KEIN NetworkTransform noetig oder erwuenscht.
    ///
    /// FLARE-Animation/Richtung leitet jeder Peer aus der lokalen Transform-Bewegung ab.
    /// </summary>
    [RequireComponent(typeof(PlayerCombat))]
    [RequireComponent(typeof(PlayerCombatVisuals))]
    [RequireComponent(typeof(TargetSelection))]
    [RequireComponent(typeof(UnitStats))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------------

        [SerializeField] private MobaCommandController m_MoveSource;
        [SerializeField] private FlareCharacter m_Character;
        [SerializeField] private PlayerCombatVisuals m_CombatVisuals;
        [SerializeField] private PlayerCombat m_Combat;
        [SerializeField] private TargetSelection m_TargetSelection;
        [SerializeField] private UnitStats m_Stats;

        [Header("Bewegung")]
        [SerializeField] private float m_Speed = 4f;

        [Header("Animationen")]
        [SerializeField] private string m_IdleAnimation = "stance";
        [SerializeField] private string m_RunAnimation = "run";

        [Header("Netzwerk-Tuning")]
        [Tooltip("Abweichung Prediction vs. Server in Metern, ab der reconciled wird.")]
        [SerializeField] private float m_ReconciliationThreshold = 0.05f;

        [Tooltip("Lerp-Geschwindigkeit fuer Remote-Clients (m_ServerPosition → transform). LEGACY — wird nur noch als Fallback genutzt, falls SmoothDamp deaktiviert ist.")]
        [SerializeField] private float m_RemoteInterpolationSpeed = 15f;
        [Tooltip("Smooth-Time (Sekunden) fuer Vector3.SmoothDamp auf Remote-Clients. " +
                 "Sollte etwa dem Server-Tick-Intervall entsprechen (~0.08–0.12s bei 10–15 Hz NetworkVariable-Tick). " +
                 "Glaettet die Schritte zwischen sporadischen NetworkVariable-Updates zu echten Frames — " +
                 "behebt 'Stepping'-Ruckler von Remote-Spielern im Editor.")]
        [SerializeField] private float m_RemoteSmoothTime = 0.1f;

        // -------------------------------------------------------------------------
        // Konstanten
        // -------------------------------------------------------------------------

        private const int k_PredictionBufferSize = 64;

        // FLARE-Konvention: 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW.
        private const int k_DefaultDirection = 2;

        // -------------------------------------------------------------------------
        // Netzwerk-State
        // -------------------------------------------------------------------------

        /// <summary>
        /// Autoritative Position vom Server. Owner ignoriert das Feld (er hat den Ack);
        /// Remote-Clients interpolieren ihre lokale Transform dahin.
        /// </summary>
        private readonly NetworkVariable<Vector3> m_ServerPosition = new(
            writePerm: NetworkVariableWritePermission.Server);

        // -------------------------------------------------------------------------
        // Owner-State (Prediction-Ringbuffer)
        // -------------------------------------------------------------------------

        private readonly PlayerCommand[] m_PredictionCommands = new PlayerCommand[k_PredictionBufferSize];
        private readonly Vector3[] m_PredictedPositions = new Vector3[k_PredictionBufferSize];
        private uint m_NextSequenceNumber = 1;
        private uint m_LastAcknowledgedSequence;

        // -------------------------------------------------------------------------
        // Sonstiger State
        // -------------------------------------------------------------------------

        private int m_LastDirection = k_DefaultDirection;
        private Vector3 m_LastObservedPosition;
        // SmoothDamp-State fuer Remote-Interpolation. Wird auf 0 zurueckgesetzt nach
        // harten Teleports, damit kein "Schwung" aus dem alten Pfad uebrigbleibt.
        private Vector3 m_RemoteSmoothVelocity;

        // ----- Impulse-State (KnockBack/PullTo/Charge/SlideFrom) -----
        // Auf allen Peers gespiegelt: Server schreibt aus <see cref="ServerApplyImpulse"/>
        // und broadcastet via <see cref="ApplyImpulseClientRpc"/>. Solange
        // <see cref="m_ImpulseSecondsRemaining"/> &gt; 0 ist, ueberlagert die
        // Velocity die Eigen-Bewegung: Owner-Input wird verworfen, Server skippt
        // die Move-Sim in <see cref="SubmitCommandServerRpc"/>, alle Peers
        // advancen <c>transform.position</c> autonom mit derselben Formel
        // (deterministisch, kein Reconciliation-Snap noetig).
        private Vector3 m_ImpulseVelocity;
        private float m_ImpulseSecondsRemaining;

        /// <summary>
        /// Lesbar auf jedem Peer: wird waehrend eines aktiven Impulses true und
        /// vom <see cref="TickOwner"/>-Gate konsumiert, damit der Owner waehrend
        /// KnockBack/PullTo/Charge keine eigenen Move-Commands schickt.
        /// </summary>
        private bool IsImpulseActive => m_ImpulseSecondsRemaining > 0f;

        // -------------------------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            m_LastObservedPosition = transform.position;

            if (IsServer)
            {
                m_ServerPosition.Value = transform.position;
            }

            // Falls die Inspector-Referenz verloren ging, am gleichen GameObject nachschauen.
            if (m_MoveSource == null)
            {
                m_MoveSource = GetComponent<MobaCommandController>();
            }
            if (m_Combat == null)
            {
                m_Combat = GetComponent<PlayerCombat>();
            }
            if (m_CombatVisuals == null)
            {
                m_CombatVisuals = GetComponent<PlayerCombatVisuals>();
            }
            if (m_TargetSelection == null)
            {
                m_TargetSelection = GetComponent<TargetSelection>();
            }
            if (m_Stats == null)
            {
                m_Stats = GetComponent<UnitStats>();
            }

            // Input nur auf dem Owner-Client. Server-Build und Remote-Clients duerfen
            // nicht sampeln.
            if (m_MoveSource != null)
            {
                m_MoveSource.enabled = IsOwner;
            }
        }

        /// <summary>
        /// Wird vom <see cref="Bootstrap.GamePlayerBootstrap"/> aufgerufen, sobald die
        /// FLARE-Visuals als Child-Hierarchie unter dem NetworkObject erzeugt sind.
        /// </summary>
        public void BindVisuals(FlareCharacter character)
        {
            if (m_CombatVisuals == null)
            {
                m_CombatVisuals = GetComponent<PlayerCombatVisuals>();
            }
            if (m_CombatVisuals != null)
            {
                m_CombatVisuals.BindCharacter(character);
            }
            m_Character = character;
        }

        // -------------------------------------------------------------------------
        // Update — drei Pfade je nach Rolle
        // -------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;

            // Impulse-Vorstep auf allen Peers: deterministische lineare Bewegung
            // mit gespiegelter Velocity. Server schreibt zusaetzlich
            // <see cref="m_ServerPosition"/>, damit ein Late-Joiner oder ein Peer
            // mit verlorenem ClientRpc ueber den NetworkVariable-Snapshot
            // konvergiert.
            if (IsImpulseActive)
            {
                AdvanceImpulse(dt);
            }

            if (IsOwner)
            {
                TickOwner();
            }
            else if (!IsServer)
            {
                TickRemoteClient();
            }
            // Server simuliert ausschliesslich in der RPC, nicht in Update.

            UpdateVisuals();
        }

        /// <summary>
        /// Wendet die laufende Impulse-Velocity auf <c>transform.position</c> an.
        /// Decrementiert <see cref="m_ImpulseSecondsRemaining"/> und stoppt
        /// automatisch, sobald die Dauer abgelaufen ist. Auf dem Server wird
        /// zusaetzlich <see cref="m_ServerPosition"/> gepflegt, sodass Remote-
        /// Clients per NetworkVariable-Snapshot synchron bleiben, falls das
        /// einleitende ClientRpc verlorengeht.
        /// </summary>
        private void AdvanceImpulse(float dt)
        {
            float step = Mathf.Min(dt, m_ImpulseSecondsRemaining);
            Vector3 delta = m_ImpulseVelocity * step;
            transform.position += delta;
            m_LastObservedPosition = transform.position;
            m_ImpulseSecondsRemaining -= step;
            if (IsServer)
            {
                m_ServerPosition.Value = transform.position;
            }
            if (m_ImpulseSecondsRemaining <= 0f)
            {
                m_ImpulseSecondsRemaining = 0f;
                m_ImpulseVelocity = Vector3.zero;
                m_RemoteSmoothVelocity = Vector3.zero;
                if (IsOwner)
                {
                    // Alle waehrend des Impulses akkumulierten Prediction-Slots
                    // verwerfen &#8212; sonst kommt der naechste Ack mit einer
                    // veralteten Pre-Impulse-Position und reisst den Spieler zurueck.
                    m_LastAcknowledgedSequence = m_NextSequenceNumber;
                }
            }
        }

        /// <summary>
        /// Server-only: setzt eine forcierte Bewegung in Gang. Bewegt die Unit
        /// mit <c>direction.normalized * meters / durationSec</c> m/s ueber
        /// <paramref name="durationSec"/> Sekunden. Waehrend der Dauer wird
        /// Eigen-Input verworfen (sowohl in Owner-Prediction als auch im Server-
        /// Authority-Pfad), CC-Status wird ignoriert (Impulse ist externe Kraft).
        /// </summary>
        public void ServerApplyImpulse(Vector3 direction, float meters, float durationSec)
        {
            if (!IsServer)
            {
                return;
            }
            if (durationSec <= 0f || meters == 0f)
            {
                return;
            }
            // XZ-Projektion + Normalisierung &#8212; Y-Komponente verwerfen, damit
            // Knockbacks nicht in den Boden oder die Luft druecken.
            Vector3 dir = direction;
            dir.y = 0f;
            float sqr = dir.sqrMagnitude;
            if (sqr < 1e-6f)
            {
                return;
            }
            dir /= Mathf.Sqrt(sqr);
            Vector3 velocity = dir * (meters / durationSec);
            m_ImpulseVelocity = velocity;
            m_ImpulseSecondsRemaining = durationSec;
            ApplyImpulseClientRpc(velocity, durationSec);
        }

        [ClientRpc]
        private void ApplyImpulseClientRpc(Vector3 velocity, float durationSec, ClientRpcParams _ = default)
        {
            // Host-Server hat den State bereits gesetzt.
            if (IsServer)
            {
                return;
            }
            m_ImpulseVelocity = velocity;
            m_ImpulseSecondsRemaining = durationSec;
            m_RemoteSmoothVelocity = Vector3.zero;
            if (IsOwner)
            {
                // Owner: alle in-flight Predictions invalidieren, damit der
                // Ack-Receiver sie nicht gegen die jetzt veraltete Pre-Impulse-
                // Pose vergleicht.
                m_LastAcknowledgedSequence = m_NextSequenceNumber;
            }
        }

        /// <summary>
        /// Owner: Command bauen, lokal predicten, in Ringbuffer ablegen, an Server schicken.
        /// </summary>
        private void TickOwner()
        {
            // Impulse aktiv? Eigen-Input und Prediction komplett aussetzen &#8212;
            // <see cref="AdvanceImpulse"/> hat bereits die Transform bewegt, und der
            // Server skippt seinen Sim-Schritt symmetrisch in <see cref="SubmitCommandServerRpc"/>.
            if (IsImpulseActive)
            {
                return;
            }

            Vector2 rawInput = m_MoveSource != null ? m_MoveSource.MoveDirection : Vector2.zero;
            bool isMoving = m_MoveSource != null && m_MoveSource.IsMoving;
            Vector2 input = isMoving ? ClampInput(rawInput) : Vector2.zero;

            // Owner-seitiger Movement-Lock. Zwei Quellen:
            //  1) PlayerCombatVisuals.IsBusy fuer nicht-Cast-Visuals. Cast-Posen
            //     duerfen den Move-Input NICHT lokal wegfiltern, sonst erreicht
            //     das serverseitige Move-cancels-Cast-Gate nie ein non-zero Input
            //     und der Spieler fuehlt einen kuenstlichen Nachhaenger.
            //  2) PlayerCombat.IsOwnerPredictingAttack — gesetzt SOFORT beim lokalen
            //     Attack-Input, bevor das ClientRpc zurückkommt. Schließt das ~1 RTT
            //     große Fenster, in dem der Owner sonst weiterpredictet, während der
            //     Server bereits clampt → Reconciliation-Ruckler.
            // Server clampt zusätzlich autoritativ (siehe SubmitCommandServerRpc).
            bool visualLocksMovement = m_CombatVisuals != null
                && m_CombatVisuals.IsBusy
                && m_CombatVisuals.CurrentAnim != CombatAnim.Cast;
            bool busy = visualLocksMovement
                        || (m_Combat != null && m_Combat.IsOwnerPredictingAttack);
            if (busy)
            {
                input = Vector2.zero;
            }

            // CC-Gate: Stun/Root unterbinden jegliche Eigenbewegung. Wird hier auf dem
            // Owner ebenfalls geprueft, damit die Prediction dasselbe Ergebnis liefert
            // wie der autoritative Server-Pfad in <see cref="SubmitCommandServerRpc"/>
            // &#8212; sonst gaebe es bei jedem Stun-Tick einen Reconciliation-Snap.
            if (m_Stats != null && m_Stats.IsImmobilized)
            {
                input = Vector2.zero;
            }

            float dt = Time.deltaTime;

            PlayerCommand cmd = new()
            {
                MoveInput = input,
                DeltaTime = dt,
                SequenceNumber = m_NextSequenceNumber++,
            };

            // 1) Lokale Prediction mit geteilter Simulations-Formel.
            // Snare/Haste (ModifyMoveSpeedPct) wirkt multiplikativ auf m_Speed. Wert
            // kommt aus dem replizierten <see cref="UnitStats.MoveSpeedMultiplier"/> &#8212;
            // identisch mit dem, den der Server fuer denselben Tick benutzt.
            float effectiveSpeed = m_Speed * (m_Stats != null ? m_Stats.MoveSpeedMultiplier : 1f);
            Vector3 pos = transform.position;
            Simulate(ref pos, cmd, effectiveSpeed);
            transform.position = pos;

            // 2) In Ringbuffer ablegen fuer spaetere Reconciliation.
            int slot = (int)(cmd.SequenceNumber % k_PredictionBufferSize);
            m_PredictionCommands[slot] = cmd;
            m_PredictedPositions[slot] = pos;

            // 3) An Server schicken (NGO ServerRpc ist reliable+ordered per Default).
            SubmitCommandServerRpc(cmd);
        }

        /// <summary>
        /// Remote-Client: keine Simulation, nur sanfte Interpolation zur autoritativen Position.
        /// Verwendet <see cref="Vector3.SmoothDamp(Vector3, Vector3, ref Vector3, float)"/>:
        /// die Smooth-Time absorbiert die Luecke zwischen seltenen NetworkVariable-Ticks und
        /// echten Render-Frames, sodass Remote-Spieler nicht 'steppen', wenn die Render-Rate
        /// (z. B. 200 FPS im Editor) deutlich hoeher liegt als der Server-Tick.
        /// </summary>
        private void TickRemoteClient()
        {
            // Impulse-Gate: AdvanceImpulse hat die Transform diesen Frame schon
            // direkt geschrieben; SmoothDamp wuerde gegen die Server-Position
            // zurueck-lerpen und ein Ruckeln erzeugen.
            if (IsImpulseActive) { return; }

            Vector3 target = m_ServerPosition.Value;
            if (m_RemoteSmoothTime > 0f)
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    target,
                    ref m_RemoteSmoothVelocity,
                    m_RemoteSmoothTime,
                    maxSpeed: Mathf.Infinity,
                    deltaTime: Time.deltaTime);
            }
            else
            {
                // Fallback (Smooth-Time auf 0 gesetzt): altes exponentielles Lerp.
                transform.position = Vector3.Lerp(
                    transform.position,
                    target,
                    m_RemoteInterpolationSpeed * Time.deltaTime);
            }
        }

        // -------------------------------------------------------------------------
        // Server-Teleport (Respawn / Cinematic / Knockback)
        // -------------------------------------------------------------------------

        /// <summary>
        /// Server-only: harte Reposition des Spielers. Setzt sowohl die Transform-Position
        /// als auch <see cref="m_ServerPosition"/> und fan't einen <see cref="TeleportClientRpc"/>
        /// an alle Peers, damit der Owner seinen Prediction-Ringbuffer invalidiert und
        /// Remote-Clients nicht sanft interpolieren, sondern direkt snappen.
        /// </summary>
        public void ServerTeleportTo(Vector3 position)
        {
            if (!IsServer)
            {
                return;
            }
            transform.position = position;
            m_LastObservedPosition = position;
            m_ServerPosition.Value = position;
            TeleportClientRpc(position);
        }

        [ClientRpc]
        private void TeleportClientRpc(Vector3 position, ClientRpcParams _ = default)
        {
            // Host-Server hat die Position bereits direkt gesetzt.
            if (IsServer)
            {
                return;
            }
            transform.position = position;
            m_LastObservedPosition = position;
            // Remote-Interp-Schwung wegwerfen, sonst gleitet der Spieler aus alter
            // Richtung in die neue Teleport-Pose.
            m_RemoteSmoothVelocity = Vector3.zero;
            if (IsOwner)
            {
                // Reconciliation aus dem Prediction-Buffer würde sonst beim nächsten Ack
                // den Spieler zurück an die alte Position snappen. Wir markieren alle
                // bisherigen Sequenzen als acknowledged → spätere out-of-order Acks werden
                // ignoriert.
                m_LastAcknowledgedSequence = m_NextSequenceNumber;
            }
        }

        // -------------------------------------------------------------------------
        // Server-Seite: Command verarbeiten und Ack zurueckschicken
        // -------------------------------------------------------------------------

        [ServerRpc]
        private void SubmitCommandServerRpc(PlayerCommand cmd, ServerRpcParams rpcParams = default)
        {
            // Hardening: Input clampen, DeltaTime auf vernuenftiges Fenster.
            cmd.MoveInput = ClampInput(cmd.MoveInput);
            cmd.DeltaTime = Mathf.Clamp(cmd.DeltaTime, 0f, 0.1f);

            // Move-cancels-Cast (LoL/WoW-Style): wenn der Owner waehrend eines
            // Casts ein non-zero MoveInput schickt, bricht der Server den Cast
            // sofort ab. Muss VOR dem Movement-Lock-Check passieren, damit der
            // gleiche Tick noch durchs Movement geht (sonst eine Tick-Verzoegerung).
            // Idempotent: ausserhalb von CastingState ist ServerInterruptCast ein No-Op.
            // Ausnahme: Spells mit SpellAttributes.CanMoveWhileCasting (z.B.
            // WoW-Scorch-while-moving) bleiben aktiv.
            if (m_Combat != null
                && m_Combat.IsServerCasting
                && cmd.MoveInput.sqrMagnitude > 0f)
            {
                Spells.SpellTemplate castSpell = m_Combat.CurrentCastSpell;
                bool canMoveWhileCasting = castSpell != null
                    && (castSpell.Attributes & Riftstorm.Game.Spells.SpellAttributes.CanMoveWhileCasting) != 0;
                if (!canMoveWhileCasting)
                {
                    m_Combat.ServerInterruptCast();
                }
            }

            // Server-autoritativer Movement-Lock: während Attacking/Dead darf sich
            // der Spieler nicht bewegen (anti-cheat gegen Move-while-Attacking).
            if (m_Combat != null && m_Combat.IsServerMovementLocked)
            {
                cmd.MoveInput = Vector2.zero;
            }

            // CC-Gate (server-authoritativ): Stun/Root verwerfen den Move-Input.
            // Deckungsgleich mit dem Owner-Gate in <see cref="TickOwner"/> &#8212;
            // verhindert Move-while-Stunned-Cheats.
            if (m_Stats != null && m_Stats.IsImmobilized)
            {
                cmd.MoveInput = Vector2.zero;
            }

            // Impulse-Gate: waehrend einer externen Bewegung (KnockBack/PullTo/
            // Charge/SlideFrom) wird Eigen-Input ignoriert. Die Transform wurde in
            // <see cref="AdvanceImpulse"/> bereits diesen Frame fortgeschrieben &#8212;
            // der Sim-Step liefe sonst doppelt.
            if (IsImpulseActive)
            {
                cmd.MoveInput = Vector2.zero;
            }

            // Authoritative Simulation mit identischer Formel.
            // Snare/Haste-Multiplikator analog Owner-Pfad &#8212; der Server ist die
            // Quelle der Wahrheit, der Multiplier kommt direkt vom AuraManager.
            float effectiveSpeed = m_Speed * (m_Stats != null ? m_Stats.MoveSpeedMultiplier : 1f);
            Vector3 pos = transform.position;
            Simulate(ref pos, cmd, effectiveSpeed);
            transform.position = pos;
            m_ServerPosition.Value = pos;

            // Ack gezielt an den absendenden Owner zurueck.
            ServerMovementAck ack = new()
            {
                LastProcessedSequence = cmd.SequenceNumber,
                Position = pos,
            };

            ClientRpcParams target = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { rpcParams.Receive.SenderClientId },
                },
            };
            ReceiveAckClientRpc(ack, target);
        }

        // -------------------------------------------------------------------------
        // Owner-Seite: Ack empfangen und ggf. reconcilen
        // -------------------------------------------------------------------------

        [ClientRpc]
        private void ReceiveAckClientRpc(ServerMovementAck ack, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
            {
                return;
            }

            // Veraltete / out-of-order Acks ignorieren.
            if (ack.LastProcessedSequence <= m_LastAcknowledgedSequence)
            {
                return;
            }
            m_LastAcknowledgedSequence = ack.LastProcessedSequence;

            int slot = (int)(ack.LastProcessedSequence % k_PredictionBufferSize);
            Vector3 predicted = m_PredictedPositions[slot];
            float drift = Vector3.Distance(predicted, ack.Position);

            if (drift <= m_ReconciliationThreshold)
            {
                return; // Prediction war gut genug — kein Snap noetig.
            }

            // Reconciliation: auf autoritative Position snappen und alle Commands seit
            // ack.LastProcessedSequence noch einmal anwenden, damit der lokale Spieler
            // nicht zurueckruckt, obwohl er seither weitergelaufen ist.
            Vector3 pos = ack.Position;
            for (uint seq = ack.LastProcessedSequence + 1; seq < m_NextSequenceNumber; seq++)
            {
                int replaySlot = (int)(seq % k_PredictionBufferSize);
                PlayerCommand replayCmd = m_PredictionCommands[replaySlot];

                // Buffer-Wraparound abfangen: nur Commands replayen, deren Sequenznummer
                // tatsaechlich noch im Slot steht.
                if (replayCmd.SequenceNumber != seq)
                {
                    continue;
                }

                // Replay mit aktuellem Move-Speed-Multiplier. Wir kennen den
                // historischen Multiplier-Wert nicht, aber der Drift bleibt klein,
                // da Reconciliation nur bei > 5cm Abweichung greift und Snare-Wechsel
                // selten innerhalb des Replay-Fensters passieren.
                float replaySpeed = m_Speed * (m_Stats != null ? m_Stats.MoveSpeedMultiplier : 1f);
                Simulate(ref pos, replayCmd, replaySpeed);
                m_PredictedPositions[replaySlot] = pos;
            }

            transform.position = pos;
        }

        // -------------------------------------------------------------------------
        // Geteilte deterministische Simulation
        // -------------------------------------------------------------------------

        /// <summary>
        /// Identische Bewegungsformel auf Owner-Client (Prediction + Replay) und Server
        /// (Authority). Topdown auf der XZ-Ebene: x=Strafe, y=Forward.
        /// </summary>
        private static void Simulate(ref Vector3 position, PlayerCommand cmd, float speed)
        {
            if (cmd.MoveInput.sqrMagnitude < 1e-6f || cmd.DeltaTime <= 0f)
            {
                return;
            }

            Vector3 delta = new(cmd.MoveInput.x, 0f, cmd.MoveInput.y);
            position += delta * (speed * cmd.DeltaTime);
        }

        private static Vector2 ClampInput(Vector2 input)
        {
            float sqr = input.sqrMagnitude;
            if (sqr > 1f)
            {
                input /= Mathf.Sqrt(sqr);
            }
            return input;
        }

        // -------------------------------------------------------------------------
        // FLARE-Visuals (gleich fuer alle Peers, abgeleitet aus Transform-Diff)
        // -------------------------------------------------------------------------

        private void UpdateVisuals()
        {
            Vector3 currentPos = transform.position;
            Vector3 diff = currentPos - m_LastObservedPosition;
            m_LastObservedPosition = currentPos;

            // FLARE-Atlas: Nord/Süd sind gegenüber Unity-+Z gespiegelt -> diff.z invertieren,
            // damit W (Bewegung in +Z) als Nord-Sprite erscheint und S als Süd.
            Vector2 visualDir = new(diff.x, -diff.z);
            bool moving = visualDir.sqrMagnitude > 1e-6f;

            if (moving)
            {
                m_LastDirection = ComputeFlareDirection(visualDir.normalized);
            }

            if (m_Character == null)
            {
                return;
            }

            // Combat-Visuals haben Priorität: solange Swing/Shoot/Cast/Hit/Block/Die
            // läuft, darf die Bewegungs-Schicht keine Stance/Run-Anim erzwingen.
            // Richtung wird trotzdem aktualisiert, damit Combat-Anims in die richtige
            // Himmelsrichtung schauen.
            bool combatBusy = m_CombatVisuals != null && m_CombatVisuals.IsBusy;
            if (!combatBusy)
            {
                m_Character.Play(moving ? m_RunAnimation : m_IdleAnimation);
            }
            else if (m_TargetSelection != null
                && m_TargetSelection.TryGetCurrentTarget(out NetworkObject targetNo, out _))
            {
                // W&#228;hrend Combat (LoL-Style Auto-Attack): jeden Frame Richtung
                // gelocktem Ziel drehen, damit die FLARE-Sprite-Richtung dem Ziel
                // folgt, auch wenn das Ziel l&#228;uft. Replizierte CurrentTargetId →
                // jeder Peer berechnet die Drehung selbst aus seinen lokalen Positionen.
                Vector3 toTarget = targetNo.transform.position - currentPos;
                // FLARE-Spiegelung wie bei Bewegung: z invertieren.
                Vector2 targetDir = new(toTarget.x, -toTarget.z);
                if (targetDir.sqrMagnitude > 1e-6f)
                {
                    m_LastDirection = ComputeFlareDirection(targetDir.normalized);
                }
            }
            m_Character.SetDirection(m_LastDirection);
        }

        /// <summary>
        /// Mappt einen 2D-Bewegungsvektor (x=rechts, y=oben) auf den FLARE-Direction-Index.
        /// Reihenfolge: 0=W, 1=SW, 2=S, 3=SE, 4=E, 5=NE, 6=N, 7=NW.
        /// </summary>
        private static int ComputeFlareDirection(Vector2 dir)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // E=0°, N=+90°
            int octant = Mathf.RoundToInt(angle / 45f);
            octant = ((octant % 8) + 8) % 8; // 0..7 mit 0=E, 2=N, 4=W, 6=S
            return (octant + 4) & 7;          // shift auf 0=W, 2=S, 4=E, 6=N
        }
    }
}
