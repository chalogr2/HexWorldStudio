using System;

namespace HexWorldStudio.Generation.Rng
{
    /// <summary>
    /// Deterministic, splittable, seeded pseudo-random source. The single
    /// source of randomness inside the generation pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements PCG XSH-RR with a 64-bit state and 32-bit output (the
    /// canonical <c>pcg32</c>). Statistically excellent (passes BigCrush),
    /// platform-independent, and trivially reproducible: the same seed
    /// always produces the same sequence on every supported platform,
    /// across process restarts, and across .NET runtime versions.
    /// </para>
    /// <para>
    /// Splitting is implemented by deriving a child seed from the parent's
    /// current state via SplitMix64. <see cref="Fork()"/> does <b>not</b>
    /// advance the parent's state; calling it twice in a row therefore
    /// produces two children with identical sequences. If a stage needs
    /// multiple distinct children, use <see cref="Fork(string)"/> with
    /// stage-specific labels — labeled forks make stage reorderings safe
    /// because each child is keyed by label rather than by call order.
    /// </para>
    /// <para>
    /// <see cref="NextInt(int, int)"/> uses simple modulo reduction over
    /// the bounded range; the resulting bias is on the order of
    /// <c>range / 2^32</c> and is negligible for the integer ranges
    /// typical of map generation (well under one part per million for
    /// ranges &lt; 4000). Rejection sampling is intentionally omitted to
    /// keep underlying-draw counts predictable.
    /// </para>
    /// <para>
    /// This type is <c>internal</c> for v1; buyers seed generation through
    /// <c>WorldGenerationSettings</c> and never construct an
    /// <c>RngService</c> directly. The visibility can be widened later
    /// without breaking compatibility if buyer demand surfaces.
    /// </para>
    /// </remarks>
    internal sealed class RngService
    {
        // Canonical PCG-XSH-RR-64-32 constants.
        private const ulong Multiplier = 6364136223846793005UL;
        private const ulong DefaultIncrement = 1442695040888963407UL;

        private ulong _state;
        private readonly ulong _increment;

        /// <summary>The seed this stream was constructed with.</summary>
        public ulong Seed { get; }

        /// <summary>
        /// The current internal 64-bit state. Exposed for diagnostics and
        /// snapshot tests; advances on every draw.
        /// </summary>
        public ulong State => _state;

        /// <summary>
        /// Constructs a new RNG seeded from <paramref name="seed"/>.
        /// </summary>
        /// <param name="seed">
        /// The 64-bit seed. Two instances constructed with the same seed
        /// produce identical sequences for the lifetime of the instance.
        /// </param>
        public RngService(ulong seed)
        {
            Seed = seed;
            _increment = DefaultIncrement;
            _state = seed;
            Step(); // burn one to mix seed into state
        }

        /// <summary>Returns the next uniform 32-bit unsigned integer.</summary>
        /// <returns>A value in <c>[0, uint.MaxValue]</c>.</returns>
        public uint NextUInt() => Step();

        /// <summary>Returns the next uniform non-negative 32-bit integer.</summary>
        /// <returns>A value in <c>[0, int.MaxValue]</c>.</returns>
        public int NextInt() => (int)(NextUInt() & 0x7FFFFFFFu);

        /// <summary>
        /// Returns a uniform integer in <c>[minInclusive, maxExclusive)</c>.
        /// </summary>
        /// <param name="minInclusive">The inclusive lower bound.</param>
        /// <param name="maxExclusive">The exclusive upper bound.</param>
        /// <returns>An integer in the requested range.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="maxExclusive"/> is not strictly greater
        /// than <paramref name="minInclusive"/>.
        /// </exception>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentException(
                    $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).",
                    nameof(maxExclusive));
            }

            long range = (long)maxExclusive - minInclusive;
            uint sample = NextUInt();
            return (int)(minInclusive + (sample % range));
        }

        /// <summary>Returns a uniform float in <c>[0, 1)</c>.</summary>
        public float NextFloat()
        {
            // Use the top 24 bits to fill a float's mantissa.
            return (NextUInt() >> 8) * (1.0f / (1 << 24));
        }

        /// <summary>Returns a uniform float in <c>[min, max)</c>.</summary>
        /// <param name="min">The inclusive lower bound.</param>
        /// <param name="max">The exclusive upper bound.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="max"/> is not strictly greater than
        /// <paramref name="min"/>, or when either bound is NaN.
        /// </exception>
        public float NextFloat(float min, float max)
        {
            if (float.IsNaN(min) || float.IsNaN(max) || !(max > min))
            {
                throw new ArgumentException(
                    $"NextFloat requires finite bounds with max > min (got min={min}, max={max}).",
                    nameof(max));
            }

            return min + NextFloat() * (max - min);
        }

        /// <summary>Returns a uniform double in <c>[0, 1)</c>.</summary>
        public double NextDouble()
        {
            // Combine two 32-bit draws into 53 bits of mantissa entropy.
            ulong upper = (ulong)NextUInt() << 32;
            ulong lower = NextUInt();
            ulong combined = (upper | lower) >> 11;
            return combined * (1.0 / (1UL << 53));
        }

        /// <summary>Returns <c>true</c> or <c>false</c> with equal probability.</summary>
        public bool NextBool() => (NextUInt() & 1u) == 1u;

        /// <summary>
        /// Returns <c>true</c> with the given probability.
        /// </summary>
        /// <param name="trueProbability">
        /// The probability of returning <c>true</c>. Must be a finite value
        /// in <c>[0, 1]</c>.
        /// </param>
        /// <returns>A weighted random boolean.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="trueProbability"/> is NaN or outside
        /// <c>[0, 1]</c>.
        /// </exception>
        public bool NextBool(float trueProbability)
        {
            if (float.IsNaN(trueProbability) || trueProbability < 0f || trueProbability > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(trueProbability),
                    trueProbability,
                    "trueProbability must be a finite value in [0, 1].");
            }

            if (trueProbability == 0f) return false;
            if (trueProbability == 1f) return true;
            return NextFloat() < trueProbability;
        }

        /// <summary>
        /// Forks an independent child stream from this RNG's current state.
        /// </summary>
        /// <remarks>
        /// Does not advance this stream's state. Two consecutive calls to
        /// <see cref="Fork()"/> therefore return two RNGs that produce
        /// identical sequences. If a stage needs two distinct children,
        /// use <see cref="Fork(string)"/> with different labels.
        /// </remarks>
        /// <returns>An independent child <see cref="RngService"/>.</returns>
        public RngService Fork()
        {
            ulong childSeed = SplitMix64(_state);
            return new RngService(childSeed);
        }

        /// <summary>
        /// Forks an independent child stream keyed by a string label.
        /// </summary>
        /// <param name="label">
        /// A stage- or purpose-specific label (e.g. <c>"elevation"</c>,
        /// <c>"rivers"</c>). Same parent state plus same label produces an
        /// identical child sequence on every run and every platform; case
        /// and character content matter (<c>"elevation"</c> differs from
        /// <c>"Elevation"</c>). Empty string is permitted.
        /// </param>
        /// <returns>An independent child <see cref="RngService"/>.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="label"/> is null.
        /// </exception>
        /// <remarks>
        /// Two different labels could in principle FNV-1a-hash to the same
        /// 64-bit value, producing the same child stream. The probability
        /// is astronomically small for the label counts a generation
        /// pipeline produces (under <c>2^-32</c> for thousands of labels)
        /// and is not engineered around.
        /// </remarks>
        public RngService Fork(string label)
        {
            if (label == null)
            {
                throw new ArgumentNullException(nameof(label));
            }

            ulong labelHash = Fnv1a64(label);
            ulong childSeed = SplitMix64(_state ^ labelHash);
            return new RngService(childSeed);
        }

        // Canonical PCG-XSH-RR step: advance state, output a permuted
        // function of the previous state.
        private uint Step()
        {
            ulong oldState = _state;
            _state = oldState * Multiplier + _increment;
            uint xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
            int rot = (int)(oldState >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        // Standard SplitMix64 finalizer used for child-seed derivation.
        private static ulong SplitMix64(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // FNV-1a 64-bit applied to the little-endian byte sequence of the
        // string's UTF-16 chars. Platform-independent and allocation-free.
        private static ulong Fnv1a64(string s)
        {
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;

            ulong hash = OffsetBasis;
            for (int i = 0; i < s.Length; i++)
            {
                int c = s[i];
                hash ^= (byte)(c & 0xFF);
                hash *= Prime;
                hash ^= (byte)((c >> 8) & 0xFF);
                hash *= Prime;
            }
            return hash;
        }
    }
}
