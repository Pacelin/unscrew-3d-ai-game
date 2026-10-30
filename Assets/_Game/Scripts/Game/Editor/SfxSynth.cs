using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Procedural sound set (no external assets, deterministic): short SFX and one light music loop written as
    /// 16-bit PCM WAV into Assets/_Game/Audio. Menu "Unscrew/Build Audio" (also called by GameSceneBuilder).
    /// </summary>
    public static class SfxSynth
    {
        public const string Dir = "Assets/_Game/Audio";
        private const int Rate = 44100;
        private const float TwoPi = Mathf.PI * 2f;

        [MenuItem("Unscrew/Build Audio")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        /// <summary>Writes every clip (index = Sfx) and the music loop; returns a summary.</summary>
        public static string BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game", "Audio");
            var rnd = new System.Random(1234);
            Write("sfx_unscrew", Unscrew(rnd));
            Write("sfx_land_box", LandBox());
            Write("sfx_land_hole", LandHole());
            Write("sfx_box_complete", BoxComplete());
            Write("sfx_part_fall", PartFall(rnd));
            Write("sfx_win", Win());
            Write("sfx_lose", Lose());
            Write("sfx_click", Click());
            Write("sfx_coin", Coin());
            Write("sfx_blocked", Blocked());
            Write("sfx_warning", Warning());
            Write("sfx_pop", Pop());
            Write("sfx_star", Star(rnd));
            Write("music_loop", Music(rnd), 22050);
            AssetDatabase.Refresh();
            foreach (var name in Names) Import(name, false);
            Import("music_loop", true);
            return $"SfxSynth: {Names.Length} sfx + music_loop in {Dir}";
        }

        /// <summary>File names in Sfx enum order.</summary>
        public static readonly string[] Names =
        {
            "sfx_unscrew", "sfx_land_box", "sfx_land_hole", "sfx_box_complete", "sfx_part_fall", "sfx_win", "sfx_lose",
            "sfx_click", "sfx_coin", "sfx_blocked", "sfx_warning", "sfx_pop", "sfx_star",
        };

        public static AudioClip Load(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Dir}/{name}.wav");

        // ---------- instruments ----------

        private static float[] Buf(float seconds, int rate = Rate) => new float[Mathf.CeilToInt(seconds * rate)];

        private static float Env(float t, float attack, float decay) =>
            t < 0f ? 0f : t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        /// <summary>Bell / marimba-like tone: sine + inharmonic partial, exponential decay.</summary>
        private static void Bell(float[] b, float start, float freq, float decay, float amp, float partial = 2.76f, float partialAmp = 0.35f, int rate = Rate)
        {
            int s0 = Mathf.Max(0, (int)(start * rate));
            int len = (int)((decay * 6f) * rate);
            for (int i = 0; i < len; i++)
            {
                int k = s0 + i;
                // SFX are cut at the end; the music (other sample rate) wraps tails around for a seamless loop.
                if (k >= b.Length && rate == Rate) break;
                float t = i / (float)rate;
                float e = Env(t, 0.004f, decay);
                float v = Mathf.Sin(TwoPi * freq * t) + partialAmp * Mathf.Sin(TwoPi * freq * partial * t) * Mathf.Exp(-t / (decay * 0.35f));
                b[k % b.Length] += v * e * amp;
            }
        }

        /// <summary>Soft lead: sine + a little 3rd harmonic, given length with attack/release.</summary>
        private static void Tone(float[] b, float start, float freq, float length, float amp, float vibrato = 0f, int rate = Rate)
        {
            int s0 = (int)(start * rate);
            int len = (int)((length + 0.12f) * rate);
            float phase = 0f;
            for (int i = 0; i < len && s0 + i < b.Length; i++)
            {
                float t = i / (float)rate;
                float f = freq * (1f + vibrato * Mathf.Sin(TwoPi * 5.5f * t));
                phase += TwoPi * f / rate;
                float e = Mathf.Clamp01(t / 0.01f) * (t > length ? Mathf.Exp(-(t - length) / 0.04f) : 1f) * Mathf.Exp(-t * 1.2f);
                b[s0 + i] += (Mathf.Sin(phase) + 0.18f * Mathf.Sin(3f * phase)) * e * amp;
            }
        }

        private static void Normalize(float[] b, float peak = 0.89f)
        {
            float m = 1e-6f;
            foreach (var v in b) m = Mathf.Max(m, Mathf.Abs(v));
            float k = peak / m;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
            // 2 ms fade at both ends (no clicks).
            int f = Mathf.Min(b.Length / 4, Rate / 500);
            for (int i = 0; i < f; i++)
            {
                b[i] *= i / (float)f;
                b[b.Length - 1 - i] *= i / (float)f;
            }
        }

        // ---------- sfx ----------

        private static float[] Unscrew(System.Random rnd)
        {
            // Ratchet clicks speeding up + a rising "zip".
            var b = Buf(0.22f);
            float t = 0f, gap = 0.032f;
            while (t < 0.16f)
            {
                int s0 = (int)(t * Rate);
                for (int i = 0; i < 260 && s0 + i < b.Length; i++)
                {
                    float tt = i / (float)Rate;
                    b[s0 + i] += (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-tt / 0.0015f) * 0.6f
                                 + Mathf.Sin(TwoPi * 3200f * tt) * Mathf.Exp(-tt / 0.002f) * 0.4f;
                }
                t += gap;
                gap *= 0.8f;
            }
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float tt = i / (float)Rate;
                float f = Mathf.Lerp(500f, 1500f, tt / 0.22f);
                phase += TwoPi * f / Rate;
                b[i] += (2f * Mathf.Abs(2f * (phase / TwoPi % 1f) - 1f) - 1f) * 0.22f * Env(tt, 0.02f, 0.08f);
            }
            Normalize(b, 0.8f);
            return b;
        }

        private static float[] LandBox()
        {
            var b = Buf(0.16f);
            Bell(b, 0f, 660f, 0.045f, 1f, 2.0f, 0.5f);
            Bell(b, 0f, 1320f, 0.02f, 0.3f);
            Normalize(b, 0.8f);
            return b;
        }

        private static float[] LandHole()
        {
            var b = Buf(0.18f);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float f = 300f * Mathf.Exp(-t * 6f) + 140f;
                phase += TwoPi * f / Rate;
                b[i] = Mathf.Sin(phase) * Env(t, 0.002f, 0.05f);
            }
            Normalize(b, 0.75f);
            return b;
        }

        private static float[] BoxComplete()
        {
            var b = Buf(0.75f);
            float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
            for (int i = 0; i < notes.Length; i++) Bell(b, i * 0.055f, notes[i], 0.16f, 0.8f - i * 0.1f);
            Normalize(b, 0.85f);
            return b;
        }

        private static float[] PartFall(System.Random rnd)
        {
            // Band-passed noise sweeping down (two one-pole filters).
            var b = Buf(0.42f);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float cutoff = Mathf.Lerp(2400f, 300f, t / 0.42f);
                float a = 1f - Mathf.Exp(-TwoPi * cutoff / Rate);
                float n = (float)(rnd.NextDouble() * 2 - 1);
                lp += a * (n - lp);
                lp2 += a * 0.25f * (lp - lp2);
                float band = lp - lp2;
                b[i] = band * Mathf.Sin(Mathf.Clamp01(t / 0.42f) * Mathf.PI);
            }
            Normalize(b, 0.6f);
            return b;
        }

        private static float[] Win()
        {
            var b = Buf(1.6f);
            float[] run = { 523.3f, 659.3f, 784f, 1046.5f };
            for (int i = 0; i < run.Length; i++) Tone(b, i * 0.1f, run[i], 0.09f, 0.6f);
            float c = 0.42f;
            foreach (var f in new[] { 523.3f, 659.3f, 784f, 1046.5f }) Tone(b, c, f, 0.75f, 0.32f, 0.004f);
            for (int i = 0; i < 4; i++) Bell(b, c + i * 0.08f, 2093f * (1f + i * 0.25f), 0.12f, 0.15f);
            Normalize(b, 0.88f);
            return b;
        }

        private static float[] Lose()
        {
            var b = Buf(1.2f);
            float[] notes = { 392f, 329.6f, 261.6f, 207.7f };
            for (int i = 0; i < notes.Length; i++) Tone(b, i * 0.2f, notes[i], i == 3 ? 0.45f : 0.17f, 0.6f, i == 3 ? 0.012f : 0f);
            Normalize(b, 0.75f);
            return b;
        }

        private static float[] Click()
        {
            var b = Buf(0.06f);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += TwoPi * Mathf.Lerp(1100f, 650f, t / 0.06f) / Rate;
                b[i] = Mathf.Sin(phase) * Env(t, 0.001f, 0.012f);
            }
            Normalize(b, 0.7f);
            return b;
        }

        private static float[] Coin()
        {
            var b = Buf(0.45f);
            Bell(b, 0f, 987.8f, 0.05f, 0.7f, 3f, 0.2f);
            Bell(b, 0.07f, 1318.5f, 0.12f, 0.8f, 3f, 0.2f);
            Normalize(b, 0.75f);
            return b;
        }

        private static float[] Blocked()
        {
            var b = Buf(0.16f);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += TwoPi * (170f - 60f * t / 0.16f) / Rate;
                float sq = Mathf.Sin(phase) + 0.3f * Mathf.Sin(3f * phase) + 0.15f * Mathf.Sin(5f * phase);
                b[i] = sq * Env(t, 0.002f, 0.045f);
            }
            Normalize(b, 0.65f);
            return b;
        }

        private static float[] Warning()
        {
            var b = Buf(0.42f);
            Tone(b, 0f, 466f, 0.1f, 0.6f);
            Tone(b, 0.17f, 349f, 0.14f, 0.6f);
            Normalize(b, 0.6f);
            return b;
        }

        private static float[] Pop()
        {
            var b = Buf(0.12f);
            float phase = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                phase += TwoPi * Mathf.Lerp(280f, 980f, Mathf.Sqrt(t / 0.12f)) / Rate;
                b[i] = Mathf.Sin(phase) * Env(t, 0.003f, 0.03f);
            }
            Normalize(b, 0.7f);
            return b;
        }

        private static float[] Star(System.Random rnd)
        {
            var b = Buf(0.6f);
            Bell(b, 0f, 1760f, 0.14f, 0.6f, 2.0f, 0.3f);
            Bell(b, 0.03f, 2637f, 0.12f, 0.35f);
            for (int i = 0; i < 6; i++) Bell(b, 0.05f + i * 0.04f, 3000f + (float)rnd.NextDouble() * 2500f, 0.03f, 0.12f);
            Normalize(b, 0.75f);
            return b;
        }

        // ---------- music ----------

        /// <summary>
        /// 8 bars at 100 bpm (19.2 s), C - Am - F - G, soft bass + marimba arpeggio + quiet shaker.
        /// Note tails wrap to the beginning, so the loop is seamless.
        /// </summary>
        private static float[] Music(System.Random rnd)
        {
            const int rate = 22050;
            const float beat = 0.6f;
            var b = new float[(int)(8 * 4 * beat * rate)];
            int[][] chords =
            {
                new[] { 60, 64, 67, 72 }, new[] { 57, 60, 64, 69 }, new[] { 53, 57, 60, 65 }, new[] { 55, 59, 62, 67 },
            };
            int[] arp = { 0, 1, 2, 3, 2, 1, 2, 3 };
            for (int bar = 0; bar < 8; bar++)
            {
                var ch = chords[(bar / 2) % 4];
                float bt = bar * 4 * beat;
                // Bass: root on beats 1 and 3, fifth on the "and" of 4.
                Bell(b, bt, Midi(ch[0] - 24), 0.35f, 0.5f, 2f, 0.15f, rate);
                Bell(b, bt + 2 * beat, Midi(ch[0] - 24), 0.35f, 0.42f, 2f, 0.15f, rate);
                Bell(b, bt + 3.5f * beat, Midi(ch[2] - 24), 0.2f, 0.3f, 2f, 0.15f, rate);
                for (int e = 0; e < 8; e++)
                {
                    float vel = e % 2 == 0 ? 0.26f : 0.18f;
                    Bell(b, bt + e * beat * 0.5f, Midi(ch[arp[e]] + 12), 0.16f, vel, 4f, 0.25f, rate);
                }
                // A simple melody every other bar.
                if (bar % 2 == 1) Bell(b, bt + 2.5f * beat, Midi(ch[3] + 12), 0.3f, 0.16f, 3f, 0.2f, rate);
            }
            // Shaker on the off-beats.
            for (int i = 0; i < 64; i++)
            {
                int s0 = (int)((i * 0.5f + 0.25f) * beat * rate);
                for (int k = 0; k < rate / 25; k++)
                {
                    float t = k / (float)rate;
                    b[(s0 + k) % b.Length] += (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t / 0.012f) * 0.05f;
                }
            }
            float m = 1e-6f;
            foreach (var v in b) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < b.Length; i++) b[i] *= 0.8f / m;
            return b;
        }

        private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        // ---------- io ----------

        private static void Write(string name, float[] samples, int rate = Rate)
        {
            var path = Path.Combine(Dir, name + ".wav");
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (var s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
        }

        private static void Import(string name, bool music)
        {
            var path = $"{Dir}/{name}.wav";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) throw new InvalidOperationException("SfxSynth: not imported " + path);
            imp.forceToMono = true;
            imp.loadInBackground = music;
            var s = imp.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            s.quality = 0.5f;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }
    }
}
