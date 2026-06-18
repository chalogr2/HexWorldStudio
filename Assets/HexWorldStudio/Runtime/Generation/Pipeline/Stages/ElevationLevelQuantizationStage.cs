using System;
using System.Collections.Generic;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Quantizes the continuous <see cref="HexTile.Elevation"/> field
    /// into the discrete <see cref="HexTile.ElevationLevel"/> with a
    /// single median-smoothing pass over the 6-neighborhood to collapse
    /// isolated single-tile outliers into their plateaus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default level count is 6 (levels <c>0..5</c>). Hardcoded for v1;
    /// moves to <c>WorldGenerationSettings</c> or <c>WorldPreset</c>
    /// content later.
    /// </para>
    /// <para>
    /// Quantization: <c>level = clamp(floor(Elevation * LevelCount), 0, LevelCount-1)</c>
    /// for land tiles. Water tiles (<see cref="TileLandState.Water"/>)
    /// are forced to <c>ElevationLevel = 0</c> regardless of the
    /// continuous elevation noise, so oceans and coasts never render
    /// above the surrounding land.
    /// </para>
    /// <para>
    /// Smoothing: a single non-iterative pass that, for each land tile,
    /// computes the median of <c>{self} ∪ wrap-aware-neighbors</c> and
    /// snaps the tile one step toward the median if the gap is two or
    /// more. Preserves legitimate sharp features (cliffs of 1 level
    /// stay; isolated 2-step outliers collapse). Water tiles are
    /// skipped in this pass and contribute their pinned level 0 to
    /// neighboring land tiles' medians, so coastal land doesn't get
    /// smoothed up by stray water elevation noise.
    /// </para>
    /// <para>
    /// Honors cylindrical wrap via <see cref="WorldMap.TryGetTile"/>
    /// for neighbor lookups, so seam tiles smooth correctly across the
    /// horizontal wrap.
    /// </para>
    /// </remarks>
    internal sealed class ElevationLevelQuantizationStage : IGenerationStage
    {
        /// <summary>
        /// Default number of discrete elevation levels.
        /// Levels are <c>[0, LevelCount-1]</c>.
        /// </summary>
        public const int DefaultLevelCount = 6;

        private readonly int _levelCount;

        /// <summary>
        /// Constructs the stage with the default level count of
        /// <see cref="DefaultLevelCount"/>.
        /// </summary>
        public ElevationLevelQuantizationStage() : this(DefaultLevelCount) { }

        /// <summary>
        /// Constructs the stage with an explicit level count. Used by
        /// tests; the default pipeline uses
        /// <see cref="DefaultLevelCount"/> per ADR-0011.
        /// </summary>
        /// <param name="levelCount">Number of discrete levels. Must be at least 2.</param>
        public ElevationLevelQuantizationStage(int levelCount)
        {
            if (levelCount < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(levelCount), levelCount,
                    "Elevation level count must be at least 2.");
            }
            _levelCount = levelCount;
        }

        /// <inheritdoc/>
        public string Name => "ElevationLevelQuantization";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            int maxLevel = _levelCount - 1;

            // Pass 1: floor-bucket continuous elevation into discrete levels.
            // Water tiles pin to level 0 so oceans and coasts never render above the surrounding land.
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState == TileLandState.Water)
                {
                    tile.ElevationLevel = 0;
                    continue;
                }
                int level = (int)Math.Floor(tile.Elevation * _levelCount);
                if (level < 0) level = 0;
                if (level > maxLevel) level = maxLevel;
                tile.ElevationLevel = level;
            }

            // Pass 2: single median-smoothing pass.
            // Read levels into a snapshot so the pass is order-
            // independent (every smoothed value derives from pre-pass
            // neighbor values, not from earlier writes in the same pass).
            int[] snapshot = new int[count];
            for (int i = 0; i < count; i++) snapshot[i] = map.Tiles[i].ElevationLevel;

            var neighborhood = new List<int>(7);
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];

                // Water tiles stay pinned to 0 — skip smoothing entirely.
                // They still appear in neighboring land tiles' medians via
                // the snapshot (with value 0), so a single-tile land peak
                // surrounded by water collapses correctly.
                if (tile.LandState == TileLandState.Water) continue;

                neighborhood.Clear();
                neighborhood.Add(snapshot[i]);
                foreach (HexCoord neighbor in tile.Coord.Neighbors())
                {
                    if (map.TryGetIndex(neighbor, out int neighborIndex))
                    {
                        neighborhood.Add(snapshot[neighborIndex]);
                    }
                }

                neighborhood.Sort();
                int median = neighborhood[neighborhood.Count / 2];
                int self = snapshot[i];
                int gap = median - self;

                if (gap >= 2)
                {
                    tile.ElevationLevel = self + 1;
                }
                else if (gap <= -2)
                {
                    tile.ElevationLevel = self - 1;
                }
                // |gap| < 2: legitimate cliff or already aligned — leave alone.

                int v = tile.ElevationLevel;
                if (v < 0) tile.ElevationLevel = 0;
                else if (v > maxLevel) tile.ElevationLevel = maxLevel;
            }
        }
    }
}
