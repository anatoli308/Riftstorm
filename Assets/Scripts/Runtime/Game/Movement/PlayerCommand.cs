using Unity.Netcode;
using UnityEngine;

namespace Riftstorm.Game.Movement
{
    /// <summary>
    /// Owner-Client → Server: ein Bewegungs-Frame (Input + DeltaTime + Sequenznummer).
    /// Server re-simuliert den Command mit identischer Formel; die Sequenznummer
    /// erlaubt dem Owner spaeter eine Reconciliation gegen den authoritativen Zustand.
    /// </summary>
    public struct PlayerCommand : INetworkSerializable
    {
        /// <summary>WASD-Input (x = Strafe, y = Forward/Back), bereits magnitude-geclamped.</summary>
        public Vector2 MoveInput;

        /// <summary>Lokales DeltaTime dieses Frames (Sekunden).</summary>
        public float DeltaTime;

        /// <summary>Streng monoton steigende Sequenznummer fuer Reconciliation-Replay.</summary>
        public uint SequenceNumber;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref MoveInput);
            serializer.SerializeValue(ref DeltaTime);
            serializer.SerializeValue(ref SequenceNumber);
        }
    }

    /// <summary>
    /// Server → Owner-Client Acknowledgement: autoritativer Zustand nach Verarbeitung
    /// eines Commands. Der Client vergleicht die gespeicherte Vorhersage fuer
    /// <see cref="LastProcessedSequence"/> und spielt alle spaeteren Commands nach,
    /// wenn die Abweichung den Reconciliation-Threshold ueberschreitet.
    /// </summary>
    public struct ServerMovementAck : INetworkSerializable
    {
        /// <summary>Sequenznummer des zuletzt verarbeiteten Commands.</summary>
        public uint LastProcessedSequence;

        /// <summary>Autoritative Position vom Server nach diesem Command.</summary>
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LastProcessedSequence);
            serializer.SerializeValue(ref Position);
        }
    }
}
