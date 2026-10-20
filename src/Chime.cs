using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;

namespace MintPet
{
    internal enum HitSound
    {
        Classic,
        Coin,
        EightBit,
        Punch,
        Laser,
        Bubble,
        Pop
    }

    /// <summary>
    /// Runtime-generated sound effects, so the app ships without audio files.
    /// Each hit sound is a short "damage / pickup" blip used for one balance tick.
    /// </summary>
    internal static class Chime
    {
        private const int SampleRate = 22050;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, SoundPlayer> Players = new Dictionary<string, SoundPlayer>();

        public static void PlayHit(HitSound sound)
        {
            PlayHit(sound, 100);
        }

        public static void PlayHit(HitSound sound, int volume)
        {
            PlayCached("hit:" + sound + ":" + NormalizeVolume(volume), RenderHit(sound), volume);
        }

        /// <summary>Cute "pop" played when the pet itself is clicked.</summary>
        public static void PlayPop()
        {
            PlayPop(100);
        }

        public static void PlayPop(int volume)
        {
            PlayCached("pop:" + NormalizeVolume(volume), BuildPop(), volume);
        }

        private static int NormalizeVolume(int volume)
        {
            if (volume < 0) return 0;
            if (volume > 100) return 100;
            return volume;
        }

        private static void PlayCached(string key, byte[] baseWav, int volume)
        {
            int level = NormalizeVolume(volume);
            if (level <= 0) return;
            try
            {
                lock (Sync)
                {
                    SoundPlayer player;
                    if (!Players.TryGetValue(key, out player))
                    {
                        byte[] wav = ScaleWav(baseWav, level / 100.0);
                        player = new SoundPlayer(new MemoryStream(wav));
                        player.Load();
                        Players[key] = player;
                    }
                    player.Play();
                }
            }
            catch
            {
                // never let a missing sound device break the pet
            }
        }

        private static byte[] ScaleWav(byte[] source, double gain)
        {
            if (gain >= 0.999) return source;
            byte[] copy = (byte[])source.Clone();
            for (int i = 44; i + 1 < copy.Length; i += 2)
            {
                int value = (int)Math.Round(BitConverter.ToInt16(copy, i) * gain);
                if (value > short.MaxValue) value = short.MaxValue;
                if (value < short.MinValue) value = short.MinValue;
                copy[i] = (byte)(value & 0xFF);
                copy[i + 1] = (byte)((value >> 8) & 0xFF);
            }
            return copy;
        }

        /// <summary>Raw WAV bytes for a hit sound (also used by tools/make_sfx).</summary>
        public static byte[] RenderHit(HitSound sound)
        {
            switch (sound)
            {
                case HitSound.Coin: return BuildCoin();
                case HitSound.EightBit: return BuildEightBit();
                case HitSound.Punch: return BuildPunch();
                case HitSound.Laser: return BuildLaser();
                case HitSound.Bubble: return BuildBubble();
                case HitSound.Pop: return BuildPop();
                default: return BuildClassic();
            }
        }

        private static byte[] BuildClassic()
        {
            const double duration = 0.11;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            Random noise = new Random(20261019);
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 330.0 - 230.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double square = Math.Sin(phase) >= 0 ? 1.0 : -1.0;
                double crack = (noise.NextDouble() * 2.0 - 1.0) * Math.Exp(-40.0 * t);
                double envelope = Math.Min(1.0, t / 0.002) * Math.Exp(-9.0 * t);
                samples[i] = ToSample((square * 0.20 + crack * 0.22) * envelope);
            }
            return ToWav(samples);
        }

        private static byte[] BuildCoin()
        {
            const double duration = 0.16;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double frequency = t < 0.055 ? 1318.5 : 1760.0;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double square = Math.Sin(phase) >= 0 ? 1.0 : -1.0;
                double envelope = Math.Min(1.0, t / 0.002) * Math.Exp(-7.0 * t);
                samples[i] = ToSample(square * 0.16 * envelope);
            }
            return ToWav(samples);
        }

        private static byte[] BuildEightBit()
        {
            const double duration = 0.09;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 520.0 - 360.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double square = Math.Sin(phase) >= 0 ? 1.0 : -1.0;
                double envelope = Math.Min(1.0, t / 0.001) * Math.Exp(-8.0 * t);
                double value = square * 0.22 * envelope;

                // crude 4-bit crush for a chiptune edge
                value = Math.Round(value * 8.0) / 8.0;
                samples[i] = ToSample(value);
            }
            return ToWav(samples);
        }

        private static byte[] BuildPunch()
        {
            const double duration = 0.16;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            Random noise = new Random(77031);
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 95.0 - 50.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double body = Math.Sin(phase) * 0.42;
                double crack = (noise.NextDouble() * 2.0 - 1.0) * Math.Exp(-30.0 * t) * 0.32;
                double envelope = Math.Min(1.0, t / 0.003) * Math.Exp(-9.0 * t);
                samples[i] = ToSample((body + crack) * envelope);
            }
            return ToWav(samples);
        }

        private static byte[] BuildLaser()
        {
            const double duration = 0.1;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 1400.0 - 1200.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double saw = (phase / (2.0 * Math.PI)) % 1.0;
                saw = saw * 2.0 - 1.0;
                double envelope = Math.Min(1.0, t / 0.002) * Math.Exp(-13.0 * t);
                samples[i] = ToSample(saw * 0.18 * envelope);
            }
            return ToWav(samples);
        }

        private static byte[] BuildBubble()
        {
            const double duration = 0.12;
            int count = (int)(SampleRate * duration);
            short[] samples = new short[count];
            double phase = 0.0;

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 700.0 + 300.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double tone = Math.Sin(phase) * 0.24;
                double envelope = (1.0 - Math.Exp(-40.0 * t)) * Math.Exp(-16.0 * t);
                samples[i] = ToSample(tone * envelope);
            }
            return ToWav(samples);
        }

        private static byte[] BuildPop()
        {
            const double duration = 0.14;
            int count = (int)(SampleRate * duration);
            double[] raw = new double[count];
            double phase = 0.0;
            Random noise = new Random(20261020);

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / SampleRate;
                double progress = t / duration;
                double frequency = 480.0 + 1100.0 * progress;
                phase += 2.0 * Math.PI * frequency / SampleRate;

                double envelope = Math.Min(1.0, t / 0.004) * Math.Exp(-18.0 * t) * (1.0 - progress);
                double crack = (noise.NextDouble() * 2.0 - 1.0) * 0.04 * Math.Exp(-45.0 * t);
                raw[i] = (Math.Sin(phase) * 0.28 + crack) * envelope;
            }

            // soften the top end so it reads as a cute "pop", not a click
            double y = 0.0;
            double peak = 1e-9;
            for (int i = 0; i < count; i++)
            {
                y += 0.55 * (raw[i] - y);
                raw[i] = y;
                if (Math.Abs(y) > peak) peak = Math.Abs(y);
            }

            short[] samples = new short[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = ToSample(raw[i] * (0.65 / peak));
            }
            return ToWav(samples);
        }

        private static short ToSample(double value)
        {
            if (value > 1.0) value = 1.0;
            if (value < -1.0) value = -1.0;
            return (short)(value * short.MaxValue);
        }

        private static byte[] ToWav(short[] samples)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int dataSize = samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);              // PCM
                writer.Write((short)1);              // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);
                for (int i = 0; i < samples.Length; i++) writer.Write(samples[i]);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
