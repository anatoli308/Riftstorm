using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Riftstorm.Game.Sprites
{
    /// <summary>
    /// Lädt die aus einer MUGEN-<c>.snd</c>-Bank extrahierten WAV-Samples eines
    /// Charakters und cached sie. Reiner Service (kein MonoBehaviour) — Owner ruft
    /// <see cref="ClearCache"/>.
    /// </summary>
    /// <remarks>
    /// Der Importer legt jedes Sample unter <c>&lt;group&gt;_&lt;index&gt;.wav</c> ab;
    /// die Adressierung entspricht damit direkt MUGENs <c>PlaySnd = Gruppe, Index</c>.
    /// </remarks>
    public sealed class MugenSoundBank
    {
        private readonly string m_SoundFolder;
        private readonly Dictionary<(int Group, int Index), AudioClip> m_Clips = new();

        /// <summary>Erzeugt eine Bank über einem Ordner mit <c>&lt;group&gt;_&lt;index&gt;.wav</c>-Dateien.</summary>
        public MugenSoundBank(string soundFolder)
        {
            m_SoundFolder = soundFolder;
        }

        /// <summary>
        /// Lädt ein Sample und legt es im Cache ab. Wiederholte Aufrufe liefern
        /// dieselbe Instanz. <c>null</c>, wenn die Datei fehlt oder nicht dekodierbar ist.
        /// </summary>
        public async Task<AudioClip> LoadAsync(int group, int index)
        {
            (int, int) key = (group, index);
            if (m_Clips.TryGetValue(key, out AudioClip cached))
            {
                return cached;
            }

            string path = Path.Combine(m_SoundFolder, $"{group}_{index}.wav");
            if (!File.Exists(path))
            {
                // Negativ-Eintrag, damit ein fehlendes Sample nicht bei jedem Move erneut sucht.
                m_Clips[key] = null;
                return null;
            }

            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                new System.Uri(path).AbsoluteUri, AudioType.WAV);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MugenSoundBank] {group},{index} nicht ladbar: {request.error}");
                m_Clips[key] = null;
                return null;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip != null)
            {
                clip.name = $"snd_{group}_{index}";
            }
            m_Clips[key] = clip;
            return clip;
        }

        /// <summary>Liefert ein bereits geladenes Sample ohne Ladeversuch.</summary>
        public bool TryGet(int group, int index, out AudioClip clip)
        {
            return m_Clips.TryGetValue((group, index), out clip) && clip != null;
        }

        /// <summary>Zerstört alle gecachten Clips und leert den Cache.</summary>
        public void ClearCache()
        {
            foreach (AudioClip clip in m_Clips.Values)
            {
                if (clip != null)
                {
                    Object.Destroy(clip);
                }
            }
            m_Clips.Clear();
        }
    }
}
