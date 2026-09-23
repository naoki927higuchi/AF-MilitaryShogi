using System;
using System.Collections.Generic;

namespace MilitaryShogi.Rules
{
    /// <summary>
    /// SplitMix64. Used instead of System.Random so a seed produces the same sequence
    /// on Unity's Mono/IL2CPP and on the .NET test runner.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong state;

        public DeterministicRandom(ulong seed) { state = seed; }
        public DeterministicRandom(int seed) : this(unchecked((ulong)(uint)seed) * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL) { }

        /// <summary>Stable seed derivation, e.g. (decisionSeed, ply) -> per-decision stream.</summary>
        public static DeterministicRandom Derive(int seed, params int[] salt)
        {
            ulong s = unchecked((ulong)(uint)seed);
            foreach (int v in salt) s = Mix(s ^ Mix(unchecked((ulong)(uint)v) + 0x9E3779B97F4A7C15UL));
            return new DeterministicRandom(s);
        }

        private static ulong Mix(ulong z)
        {
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            return Mix(state);
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() { return (NextULong() >> 11) * (1.0 / (1UL << 53)); }

        /// <summary>Uniform int in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        /// <summary>Standard normal via Box-Muller.</summary>
        public double NextGaussian()
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        /// <summary>Fresh random seed in the positive int range, for "random seed" buttons.</summary>
        public static int NewSeed(DeterministicRandom source) { return 1 + source.Next(int.MaxValue - 1); }
    }
}
