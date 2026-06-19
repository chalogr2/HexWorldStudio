using System.Collections.Generic;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// Pure analysis helper for a set of <see cref="BiomeDefinition"/>
    /// climate envelopes: reports the fraction of <c>(T, M)</c> space
    /// uncovered by any land biome's envelope, plus the pairs of land
    /// biomes whose envelopes overlap. Used by the editor inspector's
    /// envelope-chart diagnostics; safe for buyer authoring tools too.
    /// </summary>
    /// <remarks>
    /// Gap detection samples the unit square on a fixed grid (so the
    /// result is deterministic and testable). Overlap detection uses
    /// analytic rectangle intersection. Water biomes are ignored — their
    /// envelopes are unused at runtime (water classifies by topology,
    /// not climate).
    /// </remarks>
    public static class BiomeEnvelopeCoverage
    {
        /// <summary>Sample grid resolution for gap detection. Fixed for deterministic test output.</summary>
        public const int SampleResolution = 21;

        /// <summary>Result of a <see cref="Compute"/> call.</summary>
        public struct Result
        {
            /// <summary>Fraction of the <c>(T, M)</c> unit square not covered by any land biome envelope. <c>0</c> means full coverage; <c>1</c> means no land biomes (or no coverage).</summary>
            public float GapFraction;

            /// <summary>Pairs of land biomes whose envelopes overlap. First-match-wins resolves the overlap at runtime; this list lets editors flag the overlap so authors know.</summary>
            public List<(BiomeDefinition A, BiomeDefinition B)> OverlapPairs;
        }

        /// <summary>
        /// Computes the gap fraction and overlap pairs for the given
        /// biome set. Null entries and water biomes are skipped.
        /// </summary>
        public static Result Compute(IReadOnlyList<BiomeDefinition> biomes)
        {
            var result = new Result
            {
                OverlapPairs = new List<(BiomeDefinition, BiomeDefinition)>(),
            };

            if (biomes == null || biomes.Count == 0)
            {
                result.GapFraction = 1f;
                return result;
            }

            // Gap detection: sample the unit square.
            int total = SampleResolution * SampleResolution;
            int uncovered = 0;
            for (int i = 0; i < SampleResolution; i++)
            {
                float m = (SampleResolution == 1) ? 0.5f : i / (float)(SampleResolution - 1);
                for (int j = 0; j < SampleResolution; j++)
                {
                    float t = (SampleResolution == 1) ? 0.5f : j / (float)(SampleResolution - 1);
                    bool covered = false;
                    for (int k = 0; k < biomes.Count; k++)
                    {
                        BiomeDefinition b = biomes[k];
                        if (b == null || b.IsWater) continue;
                        if (t >= b.MinTemperature && t <= b.MaxTemperature
                            && m >= b.MinMoisture && m <= b.MaxMoisture)
                        {
                            covered = true;
                            break;
                        }
                    }
                    if (!covered) uncovered++;
                }
            }
            result.GapFraction = (float)uncovered / total;

            // Overlap detection: pairwise rectangle intersection.
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeDefinition a = biomes[i];
                if (a == null || a.IsWater) continue;
                for (int j = i + 1; j < biomes.Count; j++)
                {
                    BiomeDefinition b = biomes[j];
                    if (b == null || b.IsWater) continue;

                    bool tOverlap = a.MaxTemperature > b.MinTemperature && b.MaxTemperature > a.MinTemperature;
                    bool mOverlap = a.MaxMoisture > b.MinMoisture && b.MaxMoisture > a.MinMoisture;
                    if (tOverlap && mOverlap)
                    {
                        result.OverlapPairs.Add((a, b));
                    }
                }
            }

            return result;
        }
    }
}
