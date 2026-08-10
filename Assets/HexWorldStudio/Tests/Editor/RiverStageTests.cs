using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Generation.Pipeline.Stages;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for the corner-network <c>RiverStage</c>
    /// (ADR-0025). These run the whole generator rather than a
    /// hand-seeded fixture, because the properties that matter are
    /// about real terrain: that river chains are corner-connected, that
    /// no edge lies on a shoreline, that channels stay on one terrace
    /// while waterfalls cross between them, and that rivers meet the
    /// sea head-on instead of running along it.
    /// </summary>
    public class RiverStageTests
    {
        private const int Width = 60;
        private const int Height = 40;

        private readonly List<BiomeDefinition> _created = new List<BiomeDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (BiomeDefinition b in _created)
            {
                if (b != null) Object.DestroyImmediate(b);
            }
            _created.Clear();
        }

        private BiomeDefinition NewBiome(string name, bool isWater, bool isCoastal)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastal;
            if (!isWater)
            {
                b.MinTemperature = 0f;
                b.MaxTemperature = 1f;
                b.MinMoisture = 0f;
                b.MaxMoisture = 1f;
            }
            _created.Add(b);
            return b;
        }

        private WorldGenerationSettings NewSettings(
            ulong seed,
            float riverDensity = WorldGenerationSettings.DefaultRiverDensity,
            int minSourceElevation = WorldGenerationSettings.DefaultRiverSourceMinElevation,
            int minRiverLength = WorldGenerationSettings.DefaultMinRiverLength,
            int maxWaterfallDrop = WorldGenerationSettings.DefaultMaxWaterfallDrop,
            float lakeDepth = WorldGenerationSettings.DefaultLakeDepthThreshold)
        {
            var biomes = new[]
            {
                NewBiome("Ocean", isWater: true, isCoastal: false),
                NewBiome("Coast", isWater: true, isCoastal: true),
                NewBiome("Land", isWater: false, isCoastal: false),
            };

            return new WorldGenerationSettings(
                Width, Height, seed, WorldGenerationSettings.DefaultLandRatio, biomes,
                WorldGenerationSettings.DefaultBiomeCohesion,
                WorldGenerationSettings.DefaultFeatureCohesion,
                ContinentShapeAlgorithm.Continents,
                resources: null, resourceDensity: 0f,
                riverDensity: riverDensity,
                riverSourceMinElevation: minSourceElevation,
                maxRiverLength: WorldGenerationSettings.DefaultMaxRiverLength,
                minRiverLength: minRiverLength,
                maxWaterfallDrop: maxWaterfallDrop,
                lakeDepthThreshold: lakeDepth);
        }

        private WorldMap Generate(ulong seed) =>
            new HexWorldGenerator().Generate(NewSettings(seed));

        private static bool TouchesWater(HexCorner corner, WorldMap map)
        {
            corner.GetIncidentTiles(map.Width, out HexCoord a, out HexCoord b, out HexCoord c);
            return IsWater(map, a) || IsWater(map, b) || IsWater(map, c);
        }

        private static bool IsWater(WorldMap map, HexCoord coord) =>
            map.TryGetTile(coord, out HexTile t) && t.LandState == TileLandState.Water;

        // Seeds chosen only for coverage breadth; the assertions are
        // properties, not golden values, so any seed set works.
        private static readonly ulong[] Seeds =
        {
            0xDEADBEEFuL, 0x1234uL, 0xABCDEFuL, 0x99uL,
        };

        // ---- opt-out and guards ----------------------------------

        [Test]
        public void Execute_ZeroDensity_ProducesNoRiversLakesOrPaths()
        {
            WorldMap map = new HexWorldGenerator().Generate(NewSettings(0xFEEDuL, riverDensity: 0f));

            Assert.That(map.RiverEdges.Count, Is.EqualTo(0), "RiverDensity 0 must produce no edges.");
            Assert.That(map.RiverPaths.Count, Is.EqualTo(0), "RiverDensity 0 must produce no paths.");
            foreach (HexTile t in map.Tiles)
            {
                Assert.That(t.IsLake, Is.False,
                    "RiverDensity 0 must skip the hydrology pass entirely, lakes included.");
            }
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new RiverStage().Execute(null));
        }

        [Test]
        public void Execute_ProducesRiversOnDefaultSettings()
        {
            // Guards against the whole subsystem silently producing
            // nothing — the failure mode that a pile of "no invalid
            // rivers exist" tests would happily pass.
            int total = 0;
            foreach (ulong seed in Seeds) total += Generate(seed).RiverPaths.Count;

            Assert.That(total, Is.GreaterThan(0),
                "Default settings must produce rivers on ordinary terrain.");
        }

        // ---- the structural invariants ---------------------------

        [Test]
        public void Execute_ConsecutiveStepsShareACorner()
        {
            // The whole point of the corner model: chains are connected
            // by construction. This is the property the ADR-0024
            // connector pass tried and failed to guarantee.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    for (int i = 1; i < path.Steps.Count; i++)
                    {
                        Assert.That(path.Steps[i].From, Is.EqualTo(path.Steps[i - 1].To),
                            $"seed {seed:X}: step {i} does not continue from step {i - 1}.");
                    }
                }
            }
        }

        [Test]
        public void Execute_EachStepsEdgeIsTheEdgeBetweenItsCorners()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    foreach (RiverStep step in path.Steps)
                    {
                        Assert.That(step.From.TryGetSharedEdge(step.To, map.Width, out RiverEdge edge),
                            Is.True, $"seed {seed:X}: {step} corners are not adjacent.");
                        Assert.That(edge, Is.EqualTo(step.Edge),
                            $"seed {seed:X}: {step} carries an edge that is not its corners' shared edge.");
                    }
                }
            }
        }

        [Test]
        public void Execute_NoRiverEdgeTouchesWater()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverEdge e in map.RiverEdges)
                {
                    Assert.That(map.TryGetTile(e.Coord, out HexTile a), Is.True);
                    Assert.That(map.TryGetTile(e.Coord.Neighbor(e.Direction), out HexTile b), Is.True);
                    Assert.That(a.LandState, Is.EqualTo(TileLandState.Land),
                        $"seed {seed:X}: river edge {e} lies on a shoreline.");
                    Assert.That(b.LandState, Is.EqualTo(TileLandState.Land),
                        $"seed {seed:X}: river edge {e} lies on a shoreline.");
                }
            }
        }

        [Test]
        public void Execute_ChannelStepsRunBetweenTilesOfEqualElevationLevel()
        {
            // The terrace rule: a river never runs *along* a boundary
            // between a higher and a lower tile.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    foreach (RiverStep step in path.Steps)
                    {
                        if (step.IsWaterfall) continue;
                        map.TryGetTile(step.Edge.Coord, out HexTile a);
                        map.TryGetTile(step.Edge.Coord.Neighbor(step.Edge.Direction), out HexTile b);
                        Assert.That(a.ElevationLevel, Is.EqualTo(b.ElevationLevel),
                            $"seed {seed:X}: channel {step} runs along a terrace boundary "
                            + $"({a.ElevationLevel} vs {b.ElevationLevel}).");
                    }
                }
            }
        }

        [Test]
        public void Execute_WaterfallStepsMatchTheirTilesAndRespectMaxDrop()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    foreach (RiverStep step in path.Steps)
                    {
                        if (!step.IsWaterfall) continue;
                        map.TryGetTile(step.Edge.Coord, out HexTile a);
                        map.TryGetTile(step.Edge.Coord.Neighbor(step.Edge.Direction), out HexTile b);

                        int high = Mathf.Max(a.ElevationLevel, b.ElevationLevel);
                        int low = Mathf.Min(a.ElevationLevel, b.ElevationLevel);

                        Assert.That(step.FromLevel, Is.EqualTo(high), $"seed {seed:X}: {step} FromLevel wrong.");
                        Assert.That(step.ToLevel, Is.EqualTo(low), $"seed {seed:X}: {step} ToLevel wrong.");
                        Assert.That(step.DropLevels,
                            Is.LessThanOrEqualTo(WorldGenerationSettings.DefaultMaxWaterfallDrop),
                            $"seed {seed:X}: {step} drops further than MaxWaterfallDrop.");
                    }
                }
            }
        }

        [Test]
        public void Execute_NeverPlacesTwoWaterfallsInARow()
        {
            // "They can't be in a line between a higher and a lower
            // tile" — one crossing, then the river must resume as a
            // channel on the lower terrace. No staircases down a cliff.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    for (int i = 1; i < path.Steps.Count; i++)
                    {
                        Assert.That(path.Steps[i - 1].IsWaterfall && path.Steps[i].IsWaterfall, Is.False,
                            $"seed {seed:X}: consecutive waterfalls at step {i}.");
                    }
                }
            }
        }

        [Test]
        public void Execute_TerraceLevelsAreContinuousAlongAPath()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    for (int i = 1; i < path.Steps.Count; i++)
                    {
                        Assert.That(path.Steps[i].FromLevel, Is.EqualTo(path.Steps[i - 1].ToLevel),
                            $"seed {seed:X}: water surface jumps terraces at step {i}.");
                    }
                }
            }
        }

        [Test]
        public void Execute_RiversMeetWaterHeadOnRatherThanRunningAlongIt()
        {
            // Only the final corner of a path may touch water. If an
            // interior corner did, the river would be flowing parallel
            // to a shoreline instead of into it.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    Assert.That(TouchesWater(path.Source, map), Is.False,
                        $"seed {seed:X}: river source already touches water.");

                    for (int i = 0; i < path.Steps.Count - 1; i++)
                    {
                        Assert.That(TouchesWater(path.Steps[i].To, map), Is.False,
                            $"seed {seed:X}: interior corner at step {i} touches water — "
                            + "the river is running along the shore.");
                    }
                }
            }
        }

        [Test]
        public void Execute_EveryRiverDrainsToWater()
        {
            // The symptom that drove the ADR-0025 backward-tree
            // revision: under the old forward walk, 43% of rivers ran
            // out of legal downhill steps and simply stopped in the
            // middle of the land. The descent tree is grown outward
            // from water, so a corner is only reachable when a legal
            // route back down to water exists — a river physically
            // cannot strand.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    if (TouchesWater(path.Mouth, map)) continue;

                    // Otherwise it must have joined another river,
                    // which itself drains.
                    bool joinsAnother = false;
                    foreach (RiverPath other in map.RiverPaths)
                    {
                        if (ReferenceEquals(other, path)) continue;
                        foreach (RiverStep step in other.Steps)
                        {
                            if (step.From.Equals(path.Mouth) || step.To.Equals(path.Mouth))
                            {
                                joinsAnother = true;
                                break;
                            }
                        }
                        if (joinsAnother) break;
                    }

                    Assert.That(joinsAnother, Is.True,
                        $"seed {seed:X}: {path} ends inland without joining another river.");
                }
            }
        }

        [Test]
        public void Execute_RiverMouthsAreTheOnlyCornersAgainstWater()
        {
            // Companion to the drain test: a river must meet water head
            // on. Every corner except the mouth is inland, so a river
            // cannot run parallel to a shoreline.
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    for (int i = 0; i < path.Steps.Count; i++)
                    {
                        Assert.That(TouchesWater(path.Steps[i].From, map), Is.False,
                            $"seed {seed:X}: corner entering step {i} touches water.");
                    }
                }
            }
        }

        [Test]
        public void Execute_DiscardsRiversShorterThanMinLength()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    Assert.That(path.Length,
                        Is.GreaterThanOrEqualTo(WorldGenerationSettings.DefaultMinRiverLength),
                        $"seed {seed:X}: {path} is shorter than MinRiverLength.");
                }
            }
        }

        // ---- edge set / path agreement ---------------------------

        [Test]
        public void Execute_RiverEdgesIsExactlyTheUnionOfPathEdges()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);

                var union = new HashSet<RiverEdge>();
                foreach (RiverPath path in map.RiverPaths)
                {
                    foreach (RiverStep step in path.Steps) union.Add(step.Edge);
                }

                Assert.That(map.RiverEdges.Count, Is.EqualTo(union.Count),
                    $"seed {seed:X}: RiverEdges and RiverPaths disagree on edge count.");

                foreach (RiverEdge e in union)
                {
                    Assert.That(map.HasRiverAt(e.Coord, e.Direction), Is.True,
                        $"seed {seed:X}: path edge {e} is missing from RiverEdges.");
                }
            }
        }

        [Test]
        public void Execute_RiverEdgesContainsNoDuplicates()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                var seen = new HashSet<RiverEdge>();
                foreach (RiverEdge e in map.RiverEdges)
                {
                    Assert.That(seen.Add(e), Is.True, $"seed {seed:X}: duplicate edge {e}.");
                }
            }
        }

        // ---- lakes ------------------------------------------------

        [Test]
        public void Execute_LakesAreWaterAndFlaggedAsLakes()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (HexTile t in map.Tiles)
                {
                    if (!t.IsLake) continue;
                    Assert.That(t.LandState, Is.EqualTo(TileLandState.Water),
                        $"seed {seed:X}: lake tile {t.Coord} is not water.");
                    Assert.That(t.Biome, Is.Not.Null);
                    Assert.That(t.Biome.IsWater, Is.True,
                        $"seed {seed:X}: lake tile {t.Coord} carries a non-water biome.");
                    Assert.That(t.Feature, Is.Null,
                        $"seed {seed:X}: lake tile {t.Coord} kept a land feature.");
                }
            }
        }

        [Test]
        public void Execute_RaisingLakeThreshold_ProducesFewerLakes()
        {
            int Count(float threshold)
            {
                WorldMap map = new HexWorldGenerator().Generate(
                    NewSettings(0xDEADBEEFuL, lakeDepth: threshold));
                int n = 0;
                foreach (HexTile t in map.Tiles) if (t.IsLake) n++;
                return n;
            }

            Assert.That(Count(0.2f), Is.LessThanOrEqualTo(Count(0.02f)),
                "A deeper lake threshold must not produce more lakes.");
        }

        // ---- Strahler --------------------------------------------

        [Test]
        public void Execute_StrahlerOrderIsAtLeastOneOnEveryPath()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (RiverPath path in map.RiverPaths)
                {
                    Assert.That(path.StrahlerOrder, Is.GreaterThanOrEqualTo(1),
                        $"seed {seed:X}: {path} has an invalid Strahler order.");
                }
            }
        }

        // ---- determinism -----------------------------------------

        [Test]
        public void Execute_Deterministic_SameSeedSameRiversAndLakes()
        {
            WorldMap a = Generate(0xC0FFEEuL);
            WorldMap b = Generate(0xC0FFEEuL);

            Assert.That(a.RiverEdges.Count, Is.EqualTo(b.RiverEdges.Count));
            for (int i = 0; i < a.RiverEdges.Count; i++)
            {
                Assert.That(a.RiverEdges[i], Is.EqualTo(b.RiverEdges[i]),
                    $"River edge {i} diverged — determinism contract broken.");
            }

            Assert.That(a.RiverPaths.Count, Is.EqualTo(b.RiverPaths.Count));
            for (int p = 0; p < a.RiverPaths.Count; p++)
            {
                Assert.That(a.RiverPaths[p].Length, Is.EqualTo(b.RiverPaths[p].Length),
                    $"Path {p} length diverged.");
                Assert.That(a.RiverPaths[p].StrahlerOrder, Is.EqualTo(b.RiverPaths[p].StrahlerOrder),
                    $"Path {p} Strahler order diverged.");
                for (int s = 0; s < a.RiverPaths[p].Length; s++)
                {
                    Assert.That(a.RiverPaths[p].Steps[s].Edge,
                        Is.EqualTo(b.RiverPaths[p].Steps[s].Edge),
                        $"Path {p} step {s} diverged.");
                }
            }

            for (int i = 0; i < a.Tiles.Count; i++)
            {
                Assert.That(a.Tiles[i].IsLake, Is.EqualTo(b.Tiles[i].IsLake),
                    $"Lake flag diverged at tile {i}.");
            }
        }
    }
}
