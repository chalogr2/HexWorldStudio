using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Hydrology;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="DepressionFill"/> (ADR-0025). The
    /// property that matters to the river walk is the last one: after
    /// filling, no land tile is a strict local minimum, so a downhill
    /// walk always has somewhere to go and terminates at an outlet
    /// rather than stranding and punching a spurious lake.
    /// </summary>
    public class DepressionFillTests
    {
        private static WorldMap NewLandMap(int width, int height)
        {
            var map = new WorldMap(width, height, 1234uL);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Land;
                t.Elevation = 0.5f;
            }
            return map;
        }

        private static HexTile At(WorldMap map, int col, int row)
        {
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(col, row), out HexTile t);
            return t;
        }

        private static float FilledAt(WorldMap map, float[] filled, int col, int row)
        {
            map.TryGetIndex(HexCoordExtensions.FromOffsetOddR(col, row), out int i);
            return filled[i];
        }

        [Test]
        public void Fill_NullMap_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => DepressionFill.Fill(null));
        }

        [Test]
        public void Fill_SinglePit_RaisesItToTheSpillLevel()
        {
            WorldMap map = NewLandMap(9, 9);
            At(map, 4, 4).Elevation = 0.1f;

            float[] filled = DepressionFill.Fill(map);

            // Filling raises to just above the spill level (see
            // DepressionFill.Epsilon), so this is "0.5 plus a hair".
            Assert.That(FilledAt(map, filled, 4, 4), Is.EqualTo(0.5f).Within(1e-4f),
                "An isolated pit must fill to the level of the terrain around it.");
        }

        [Test]
        public void Fill_MonotoneRamp_IsLeftUntouched()
        {
            WorldMap map = NewLandMap(9, 9);
            foreach (HexTile t in map.Tiles)
            {
                (int _, int row) = t.Coord.ToOffsetOddR();
                t.Elevation = row * 0.1f;
            }

            float[] filled = DepressionFill.Fill(map);

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                Assert.That(filled[i], Is.EqualTo(map.Tiles[i].Elevation).Within(1e-6f),
                    $"A field with no depressions must come back unchanged (tile {i}).");
            }
        }

        [Test]
        public void Fill_NestedBasin_FillsFlatToTheOuterRim()
        {
            // Deep core inside a shallow bowl inside a high rim. The
            // whole basin must come out flat at the rim height — filling
            // only to the bowl height would leave the core a pit.
            WorldMap map = NewLandMap(11, 11);
            foreach (HexTile t in map.Tiles) t.Elevation = 0.9f;
            for (int c = 3; c <= 7; c++)
                for (int r = 3; r <= 7; r++) At(map, c, r).Elevation = 0.4f;
            for (int c = 4; c <= 6; c++)
                for (int r = 4; r <= 6; r++) At(map, c, r).Elevation = 0.15f;

            float[] filled = DepressionFill.Fill(map);

            for (int c = 3; c <= 7; c++)
            {
                for (int r = 3; r <= 7; r++)
                {
                    // Within a hair of the rim: the epsilon gradient
                    // accumulates along the fill chain, so the filled
                    // basin slopes imperceptibly toward its outlet
                    // rather than being exactly flat.
                    Assert.That(FilledAt(map, filled, c, r), Is.EqualTo(0.9f).Within(1e-3f),
                        $"Basin tile ({c},{r}) did not fill to the rim.");
                }
            }
        }

        [Test]
        public void Fill_NeverLowersTerrain()
        {
            WorldMap map = NewLandMap(20, 14);
            var rng = new Random(11);
            foreach (HexTile t in map.Tiles) t.Elevation = (float)rng.NextDouble();

            float[] filled = DepressionFill.Fill(map);

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                Assert.That(filled[i], Is.GreaterThanOrEqualTo(map.Tiles[i].Elevation - 1e-6f),
                    $"Fill lowered tile {i}; it must be monotone non-decreasing.");
            }
        }

        [Test]
        public void Fill_LeavesEveryTileAStrictlyLowerNeighbour()
        {
            // The defining property, on a pure-noise field: every
            // interior land tile must have a STRICTLY lower neighbour.
            // Filling to exactly the spill level would only guarantee
            // "lower or equal", and a walk requiring strict descent
            // cannot cross a flat filled basin at all — it would strand
            // in the middle of the very depressions the fill exists to
            // remove. The epsilon gradient is what upgrades this to
            // strict, and it is the property the river walk relies on.
            WorldMap map = NewLandMap(24, 16);
            var rng = new Random(7);
            foreach (HexTile t in map.Tiles)
            {
                (int col, int _) = t.Coord.ToOffsetOddR();
                t.Elevation = (float)rng.NextDouble();
                if (col >= 21)
                {
                    t.LandState = TileLandState.Water;
                    t.Elevation = 0.01f;
                }
            }

            float[] filled = DepressionFill.Fill(map);

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState != TileLandState.Land) continue;
                (int _, int row) = tile.Coord.ToOffsetOddR();
                if (row == 0 || row == map.Height - 1) continue; // outlets by definition

                bool hasStrictlyLower = false;
                for (int d = 0; d < 6 && !hasStrictlyLower; d++)
                {
                    if (map.TryGetIndex(tile.Coord.Neighbor((HexDirection)d), out int ni)
                        && filled[ni] < filled[i])
                    {
                        hasStrictlyLower = true;
                    }
                }

                Assert.That(hasStrictlyLower, Is.True,
                    $"Tile {tile.Coord} has no strictly lower neighbour after filling — "
                    + "a strict-descent walk would strand here.");
            }
        }

        [Test]
        public void Fill_IsInvariantUnderHorizontalRotation()
        {
            // Rotating the whole field K columns east must rotate the
            // filled result identically. Catches any neighbour lookup
            // that fails to cross the cylindrical wrap seam.
            const int W = 18, H = 12, K = 5;
            var rng = new Random(23);
            var source = new float[W * H];
            for (int i = 0; i < source.Length; i++) source[i] = (float)rng.NextDouble();

            WorldMap a = NewLandMap(W, H);
            foreach (HexTile t in a.Tiles)
            {
                (int c, int r) = t.Coord.ToOffsetOddR();
                t.Elevation = source[r * W + c];
            }
            float[] fa = DepressionFill.Fill(a);

            WorldMap b = NewLandMap(W, H);
            foreach (HexTile t in b.Tiles)
            {
                (int c, int r) = t.Coord.ToOffsetOddR();
                t.Elevation = source[r * W + ((c - K + W) % W)];
            }
            float[] fb = DepressionFill.Fill(b);

            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    Assert.That(FilledAt(b, fb, (c + K) % W, r),
                        Is.EqualTo(FilledAt(a, fa, c, r)).Within(1e-6f),
                        $"Rotation invariance broken at ({c},{r}) — wrap seam bug.");
                }
            }
        }

        [Test]
        public void Fill_IsDeterministic()
        {
            var rng = new Random(31);
            var source = new float[30 * 20];
            for (int i = 0; i < source.Length; i++) source[i] = (float)rng.NextDouble();

            float[] Run()
            {
                WorldMap map = NewLandMap(30, 20);
                foreach (HexTile t in map.Tiles)
                {
                    (int c, int r) = t.Coord.ToOffsetOddR();
                    t.Elevation = source[r * 30 + c];
                }
                return DepressionFill.Fill(map);
            }

            float[] first = Run();
            float[] second = Run();

            for (int i = 0; i < first.Length; i++)
            {
                Assert.That(second[i], Is.EqualTo(first[i]),
                    $"Fill diverged at index {i} — determinism contract broken.");
            }
        }
    }
}
