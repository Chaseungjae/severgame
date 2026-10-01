using System.Collections.Generic;
using UnityEngine;

namespace MiniParty
{
    /// <summary>
    /// Procedural song + chart for the rhythm battle. The same seed gives the same chart and the same audio on both
    /// PCs, so no music assets are needed. Drums keep the beat, and every note in the chart also plays a short
    /// melodic pluck so the notes are audible.
    /// </summary>
    public static class RhythmSynth
    {
        public const int Rate = 44100;
        public const float Bpm = 132f;
        public const int Bars = 16;
        public const int LeadInBars = 1;
        public static float Sixteenth => 60f / Bpm / 4f;
        public static float BarLength => Sixteenth * 16f;

        public struct Note
        {
            public float Time;
            public int Lane;
        }

        /// <summary>Notes get denser as the song goes on: quarters → eighths → sixteenth bursts with chords.</summary>
        public static List<Note> MakeChart(int seed)
        {
            var rng = new System.Random(seed);
            var notes = new List<Note>();
            int lastLane = -1;
            for (int bar = 0; bar < Bars; bar++)
            {
                for (int step = 0; step < 16; step++)
                {
                    bool place;
                    if (bar < 4) place = step % 4 == 0;
                    else if (bar < 8) place = step % 2 == 0 && (step % 4 == 0 || rng.NextDouble() < 0.7);
                    else if (bar < 12) place = step % 2 == 0 || rng.NextDouble() < 0.3;
                    else place = step % 2 == 0 || rng.NextDouble() < 0.55;
                    if (!place) continue;

                    float time = (LeadInBars * 16 + bar * 16 + step) * Sixteenth;
                    int lane = rng.Next(4);
                    if (lane == lastLane) lane = (lane + 1 + rng.Next(3)) % 4; // avoid jacks
                    notes.Add(new Note { Time = time, Lane = lane });
                    lastLane = lane;

                    // chords on strong beats in the second half
                    if (bar >= 8 && step % 8 == 0 && rng.NextDouble() < 0.6)
                    {
                        int second = (lane + 2) % 4;
                        notes.Add(new Note { Time = time, Lane = second });
                    }
                }
            }
            return notes;
        }

        static readonly float[] LaneFreq = { 440f, 523.25f, 659.25f, 783.99f };   // A4 C5 E5 G5 (A minor pentatonic)
        static readonly float[] BassRoots = { 110f, 87.31f, 130.81f, 98f };        // Am F C G

        public static AudioClip MakeSong(int seed, List<Note> chart)
        {
            float length = (LeadInBars + Bars) * BarLength + 1.5f;
            var buf = new float[Mathf.CeilToInt(length * Rate)];
            var rng = new System.Random(seed ^ 0x5eed);

            // count-in: hats on the quarters
            for (int q = 0; q < 4; q++) Hat(buf, q * 4 * Sixteenth, rng, 0.35f);

            for (int bar = 0; bar < Bars; bar++)
            {
                float barStart = (LeadInBars + bar) * BarLength;
                for (int step = 0; step < 16; step++)
                {
                    float t = barStart + step * Sixteenth;
                    bool fourOnFloor = bar >= 4;
                    if (step == 0 || step == 8 || (fourOnFloor && step % 4 == 0)) Kick(buf, t);
                    if (bar >= 4 && (step == 4 || step == 12)) Snare(buf, t, rng);
                    if (bar >= 8 || step % 2 == 0) Hat(buf, t, rng, step % 4 == 2 ? 0.3f : 0.18f);
                    if (step % 4 == 0) Bass(buf, t, BassRoots[bar % 4] * (step == 8 ? 1.5f : 1f), Sixteenth * 3.6f);
                }
            }
            foreach (var n in chart) Pluck(buf, n.Time, LaneFreq[n.Lane]);

            // soft clip
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)System.Math.Tanh(buf[i] * 1.1f) * 0.8f;

            var clip = AudioClip.Create("RhythmSong", buf.Length, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        public static AudioClip MakeClick()
        {
            int n = Rate / 30;
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                buf[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * Mathf.Exp(-t * 90f) * 0.35f;
            }
            var clip = AudioClip.Create("Click", n, 1, Rate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static void Add(float[] buf, int i, float v)
        {
            if (i >= 0 && i < buf.Length) buf[i] += v;
        }

        static void Kick(float[] buf, float at)
        {
            int start = (int)(at * Rate), n = (int)(0.28f * Rate);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = 45f + 110f * Mathf.Exp(-t * 28f);
                phase += 2 * System.Math.PI * f / Rate;
                Add(buf, start + i, (float)System.Math.Sin(phase) * Mathf.Exp(-t * 9f) * 0.9f);
            }
        }

        static void Snare(float[] buf, float at, System.Random rng)
        {
            int start = (int)(at * Rate), n = (int)(0.2f * Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float tone = Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-t * 30f) * 0.3f;
                Add(buf, start + i, (noise * Mathf.Exp(-t * 22f) * 0.45f) + tone);
            }
        }

        static void Hat(float[] buf, float at, System.Random rng, float amp)
        {
            int start = (int)(at * Rate), n = (int)(0.05f * Rate);
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float high = noise - prev; // crude high-pass
                prev = noise;
                Add(buf, start + i, high * Mathf.Exp(-t * 70f) * amp * 0.5f);
            }
        }

        static void Bass(float[] buf, float at, float freq, float dur)
        {
            int start = (int)(at * Rate), n = (int)(dur * Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float tri = 2f * Mathf.Abs(2f * Mathf.Repeat(t * freq, 1f) - 1f) - 1f;
                float env = Mathf.Min(1f, t * 200f) * Mathf.Min(1f, (dur - t) * 30f);
                Add(buf, start + i, tri * env * 0.28f);
            }
        }

        static void Pluck(float[] buf, float at, float freq)
        {
            int start = (int)(at * Rate), n = (int)(0.2f * Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float sq = Mathf.Repeat(t * freq, 1f) < 0.5f ? 1f : -1f;
                float sine = Mathf.Sin(2f * Mathf.PI * freq * t);
                Add(buf, start + i, (sq * 0.35f + sine * 0.65f) * Mathf.Exp(-t * 14f) * 0.16f);
            }
        }
    }
}
