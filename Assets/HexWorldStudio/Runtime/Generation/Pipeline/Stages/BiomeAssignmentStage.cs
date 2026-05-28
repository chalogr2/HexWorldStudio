using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Default biome-assignment algorithm: water tiles classify by
    /// neighbor topology (Coast if any land neighbor, otherwise
    /// Ocean); land tiles classify by the first
    /// <see cref="BiomeDefinition"/> in
    /// <c>WorldGenerationSettings.Biomes</c> whose climate envelope
    /// contains the tile's (Temperature, Moisture).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Buyers control the biome roster entirely through
    /// <c>WorldGenerationSettings.Biomes</c>. Authoring order is
    /// meaningful: the assignment iterates the list and picks the
    /// first match, so authors place more specific envelopes before
    /// more general ones.
    /// </para>
    /// <para>
    /// Fallback: if a land tile's (T, M) somehow fails to match any
    /// non-water biome in the list (envelopes can be authored with
    /// gaps), the stage assigns the first non-water biome in the list
    /// as a defensive default rather than leaving
    /// <c>HexTile.Biome</c> null.
    /// </para>
    /// </remarks>
    internal sealed class BiomeAssignmentStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "BiomeAssignment";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldMap map = ctx.Map;
            IReadOnlyList<BiomeDefinition> biomes = ctx.Settings.Biomes;

            BiomeDefinition coastBiome = FindFirst(biomes, b => b.IsWater && b.IsCoastalWater);
            BiomeDefinition oceanBiome = FindFirst(biomes, b => b.IsWater && !b.IsCoastalWater);
            BiomeDefinition fallbackLandBiome = FindFirst(biomes, b => !b.IsWater);
            // HexWorldGenerator.Generate already validated all three
            // role biomes are present, but we keep the lookups defensive
            // so direct stage tests with handcrafted contexts also work.

            int count = map.Tiles.Count;
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];

                if (tile.LandState == TileLandState.Water)
                {
                    tile.Biome = HasLandNeighbor(map, tile.Coord)
                        ? coastBiome
                        : oceanBiome;
                    continue;
                }

                tile.Biome = MatchLandBiome(biomes, tile.Temperature, tile.Moisture)
                             ?? fallbackLandBiome;
            }
        }

        private static BiomeDefinition MatchLandBiome(IReadOnlyList<BiomeDefinition> biomes, float t, float m)
        {
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeDefinition b = biomes[i];
                if (b == null) continue;
                if (b.IsWater) continue;
                if (b.Matches(t, m)) return b;
            }
            return null;
        }

        private static BiomeDefinition FindFirst(IReadOnlyList<BiomeDefinition> biomes, System.Func<BiomeDefinition, bool> predicate)
        {
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeDefinition b = biomes[i];
                if (b == null) continue;
                if (predicate(b)) return b;
            }
            return null;
        }

        private static bool HasLandNeighbor(WorldMap map, HexCoord coord)
        {
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                if (!map.TryGetTile(neighbor, out HexTile n)) continue;
                if (n.LandState == TileLandState.Land) return true;
            }
            return false;
        }
    }
}
