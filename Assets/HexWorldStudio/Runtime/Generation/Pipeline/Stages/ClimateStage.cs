using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Default climate algorithm: latitude-driven temperature with an
    /// elevation lapse rate and Perlin perturbation; moisture from a
    /// prevailing-wind transport simulation (east-to-west) with
    /// orographic precipitation, rain shadow, and Perlin perturbation
    /// (per ADR-0012).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Temperature: <c>1 - latitude</c> (so equator is hot, poles
    /// cold), minus a lapse <c>k * Elevation</c>, plus a small
    /// cylindrical Perlin noise term. Clamped to <c>[0, 1]</c>.
    /// </para>
    /// <para>
    /// Moisture: per row, an east-to-west wind simulation. The wind
    /// picks up moisture over water tiles, deposits it as orographic
    /// precipitation on land (with rate scaling on elevation), and
    /// gets a rain-shadow penalty as it crosses high-elevation tiles.
    /// Two passes around the cylindrical row let the initial carry
    /// state wash out before the recorded pass. Land moisture is
    /// normalized to <c>[0, 1]</c> by the maximum recorded
    /// precipitation, then perturbed by Perlin noise and clamped.
    /// Water tiles always get <c>Moisture = 1.0</c>.
    /// </para>
    /// <para>
    /// Algorithm constants are hardcoded for v1 and move to
    /// <c>WorldPreset</c> SO content later. Alternate climate
    /// algorithms (latitude-banded wind, more sophisticated lapse
    /// rates) land as additional <see cref="IGenerationStage"/>
    /// implementations per ADR-0010.
    /// </para>
    /// </remarks>
    internal sealed class ClimateStage : IGenerationStage
    {
        // Temperature parameters.
        private const float ElevationLapseRate       = 0.45f;
        private const float TemperatureNoiseAmpl     = 0.10f;
        private const float TemperatureNoiseCellsX   = 6f;
        private const float TemperatureNoiseCellsY   = 4f;

        // Moisture parameters (prevailing-wind simulation).
        private const int   MoisturePasses           = 2;     // 1 warmup + 1 record
        private const float NeutralCarry             = 0.5f;
        private const float WaterUptake              = 0.25f;
        private const float BasePrecipitation        = 0.15f;
        private const float ElevationPrecipitation   = 0.25f;
        private const float RainShadowFactor         = 0.10f;
        private const float MoistureNoiseAmpl        = 0.10f;
        private const float MoistureNoiseCellsX      = 8f;
        private const float MoistureNoiseCellsY      = 5f;

        /// <inheritdoc/>
        public string Name => "Climate";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;

            // ---- Temperature ----
            RngService tempRng = ctx.Rng.Fork("noise-temperature");
            var tempPerlin = new Perlin(tempRng);

            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                (int col, int row) = tile.Coord.ToOffsetOddR();

                float latitude = ComputeLatitudeFactor(row, map.Height);
                float fromLat  = 1f - latitude;
                float lapse    = ElevationLapseRate * tile.Elevation;
                float colNorm  = (float)col / map.Width;
                float rowNorm  = (float)row / Math.Max(1, map.Height - 1);
                float noise    = tempPerlin.CylindricalSample(
                                     colNorm, rowNorm,
                                     TemperatureNoiseCellsX, TemperatureNoiseCellsY)
                                 * TemperatureNoiseAmpl;

                float t = fromLat - lapse + noise;
                tile.Temperature = Clamp01(t);
            }

            // ---- Moisture (prevailing-wind east-to-west) ----
            ComputeMoisture(map);

            RngService moistRng = ctx.Rng.Fork("noise-moisture");
            var moistPerlin = new Perlin(moistRng);

            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState == TileLandState.Water) continue;
                (int col, int row) = tile.Coord.ToOffsetOddR();
                float colNorm = (float)col / map.Width;
                float rowNorm = (float)row / Math.Max(1, map.Height - 1);
                float noise   = moistPerlin.CylindricalSample(
                                    colNorm, rowNorm,
                                    MoistureNoiseCellsX, MoistureNoiseCellsY)
                                * MoistureNoiseAmpl;
                tile.Moisture = Clamp01(tile.Moisture + noise);
            }
        }

        // 0 at the equator row, 1 at the polar rows.
        private static float ComputeLatitudeFactor(int row, int height)
        {
            float center  = (height - 1) * 0.5f;
            float dist    = Math.Abs(row - center);
            float maxDist = Math.Max(center, 1f);
            return dist / maxDist;
        }

        private static void ComputeMoisture(WorldMap map)
        {
            int count = map.Tiles.Count;
            float[] rawLandMoisture = new float[count];
            float maxLandMoisture = 0f;

            // Per row: east-to-west wind. Two cylindrical passes per row
            // so the arbitrary initial carry value washes out before the
            // recorded second pass.
            for (int row = 0; row < map.Height; row++)
            {
                float carry = NeutralCarry;

                for (int pass = 0; pass < MoisturePasses; pass++)
                {
                    bool record = (pass == MoisturePasses - 1);

                    for (int col = map.Width - 1; col >= 0; col--)
                    {
                        HexCoord coord = HexCoordExtensions.FromOffsetOddR(col, row);
                        map.TryGetTile(coord, out HexTile tile);

                        if (tile.LandState == TileLandState.Water)
                        {
                            carry = Math.Min(1f, carry + WaterUptake);
                            if (record) tile.Moisture = 1f;
                        }
                        else
                        {
                            float precip = (BasePrecipitation
                                            + ElevationPrecipitation * tile.Elevation)
                                           * carry;
                            if (precip > carry) precip = carry;
                            carry -= precip;
                            carry = Math.Max(0f, carry - tile.Elevation * RainShadowFactor);

                            if (record)
                            {
                                rawLandMoisture[tile.TileIndex] = precip;
                                if (precip > maxLandMoisture) maxLandMoisture = precip;
                            }
                        }
                    }
                }
            }

            // Normalize land moisture to [0, 1] by the realized max.
            // Defensive: if nothing precipitated (e.g., all-water map),
            // every land tile would have Moisture 0 — fine.
            if (maxLandMoisture <= 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    if (map.Tiles[i].LandState != TileLandState.Water)
                        map.Tiles[i].Moisture = 0f;
                }
                return;
            }

            float inv = 1f / maxLandMoisture;
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState == TileLandState.Water) continue;
                tile.Moisture = rawLandMoisture[i] * inv;
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
