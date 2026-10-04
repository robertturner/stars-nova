#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars-Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;

    /// <summary>
    /// Deterministic random sources for game simulation, so a whole game (generation plus any
    /// number of turns, AI included) is bit-for-bit repeatable from its seed.
    /// </summary>
    /// <remarks>
    /// <para>Every stream is a plain <see cref="Random"/> seeded from a stable hash of
    /// (game seed, turn year, stream name, sub key, use index) - see <see cref="DeriveSeed"/>.
    /// The hash is FNV-1a/SplitMix64 over the values themselves (never string.GetHashCode,
    /// which is randomized per process), and <c>new Random(int)</c> is .NET's documented
    /// stable seeded algorithm, so the same inputs give the same draws in every process. A
    /// stream's position is therefore derivable from the seed, the year and the stream key
    /// alone - nothing depends on process state - which is what lets a saved and reloaded game
    /// continue identically. Each turn step (and each fleet's movement, and each AI player)
    /// draws from its own stream, so adding a draw in one step never shifts the others.</para>
    /// <para>Code in Nova.Common that has no ServerData to ask (stochastic rounding, fleet
    /// merging, tech trading, the battle-board allocator) draws from <see cref="Current"/>: the
    /// ambient stream installed with <see cref="Use"/> by the server for the step that is
    /// running (and by the AI for its own turn). With no ambient stream installed - UI
    /// projections, ad-hoc tests - it falls back to an unseeded per-thread Random, exactly the
    /// old behaviour.</para>
    /// </remarks>
    public static class GameRandom
    {
        [ThreadStatic]
        private static Random ambient;

        [ThreadStatic]
        private static Random unseededFallback;

        /// <summary>
        /// The ambient random stream for code that is not handed one: the stream installed by
        /// <see cref="Use"/> on this thread, else an unseeded per-thread fallback.
        /// </summary>
        public static Random Current
        {
            get
            {
                if (ambient != null)
                {
                    return ambient;
                }

                if (unseededFallback == null)
                {
                    unseededFallback = new Random();
                }

                return unseededFallback;
            }
        }

        /// <summary>True when an ambient stream has been installed on this thread.</summary>
        public static bool HasAmbient
        {
            get { return ambient != null; }
        }

        /// <summary>
        /// Installs <paramref name="random"/> as this thread's ambient stream until the returned
        /// scope is disposed (scopes nest; disposing restores the previous stream). A null
        /// <paramref name="random"/> leaves the current ambient stream in place.
        /// </summary>
        public static IDisposable Use(Random random)
        {
            Random previous = ambient;
            if (random != null)
            {
                ambient = random;
            }

            return new Scope(previous);
        }

        /// <summary>
        /// A stable 31-bit seed for one stream: (game seed, year, stream name, sub key, use index)
        /// hashed with FNV-1a (ordinal, char by char) and finished with SplitMix64.
        /// </summary>
        public static int DeriveSeed(long seed, int year, string stream, long subKey = 0, int index = 0)
        {
            ulong hash = 14695981039346656037UL;
            hash = MixValue(hash, (ulong)seed);
            hash = MixValue(hash, (ulong)(uint)year);

            if (stream != null)
            {
                foreach (char c in stream)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
            }

            hash = MixValue(hash, (ulong)subKey);
            hash = MixValue(hash, (ulong)(uint)index);
            hash = SplitMix64(hash);

            return (int)(hash & 0x7FFFFFFFUL);
        }

        /// <summary>A new Random for one stream - see <see cref="DeriveSeed"/>.</summary>
        public static Random Create(long seed, int year, string stream, long subKey = 0, int index = 0)
        {
            return new Random(DeriveSeed(seed, year, stream, subKey, index));
        }

        private static ulong MixValue(ulong hash, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (value >> (8 * i)) & 0xFFUL;
                hash *= 1099511628211UL;
            }

            return hash;
        }

        private static ulong SplitMix64(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private sealed class Scope : IDisposable
        {
            private readonly Random previous;
            private bool disposed;

            public Scope(Random previous)
            {
                this.previous = previous;
            }

            public void Dispose()
            {
                if (!disposed)
                {
                    ambient = previous;
                    disposed = true;
                }
            }
        }
    }
}
