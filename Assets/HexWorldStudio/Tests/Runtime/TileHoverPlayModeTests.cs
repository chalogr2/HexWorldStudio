using System.Collections;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Examples.QuickStart;
using HexWorldStudio.Generation;
using HexWorldStudio.Input;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests.Runtime
{
    /// <summary>
    /// PlayMode tests for <see cref="TileHover"/> — hover resolution
    /// at a known cursor position, hover duration progression, hover
    /// change events, outline visibility.
    /// </summary>
    public class TileHoverPlayModeTests
    {
        private const int Width = 8;
        private const int Height = 6;
        private const ulong Seed = 0xC1C2C3C4C5C6C7C8UL;

        // ---- mock input ----

        private sealed class MockCameraInput : MonoBehaviour, ICameraInput
        {
            public Vector2 PanAxisValue;
            public float   ZoomDeltaValue;
            public float   RotateAxisValue;
            public float   TiltAxisValue;
            public bool    IsMouseInWindowValue = true;
            public Vector2 MousePositionValue;
            public bool    PrimaryClickPressedValue;
            public bool    SecondaryClickPressedValue;

            public Vector2 PanAxis => PanAxisValue;
            public float   ZoomDelta => ZoomDeltaValue;
            public float   RotateAxis => RotateAxisValue;
            public float   TiltAxis => TiltAxisValue;
            public bool    IsMouseInWindow => IsMouseInWindowValue;
            public Vector2 MousePosition => MousePositionValue;
            public bool    PrimaryClickPressed => PrimaryClickPressedValue;
            public bool    SecondaryClickPressed => SecondaryClickPressedValue;
        }

        // ---- biome helpers (shared with other Runtime tests' pattern) ----

        private static BiomeDefinition NewBiome(string name, Color color,
            bool isWater = false, bool isCoastalWater = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.Color = color;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.MinTemperature = 0f;
            b.MaxTemperature = 1f;
            b.MinMoisture = 0f;
            b.MaxMoisture = 1f;
            return b;
        }

        private static BiomeDefinition[] MinimalBiomeSet()
        {
            return new[]
            {
                NewBiome("HOcean", new Color(0.10f, 0.25f, 0.5f), isWater: true),
                NewBiome("HCoast", new Color(0.23f, 0.49f, 0.72f), isWater: true, isCoastalWater: true),
                NewBiome("HLand",  new Color(0.44f, 0.66f, 0.36f)),
            };
        }

        // ---- scene fixture ----

        private sealed class HoverFixture
        {
            public GameObject RendererHost;
            public GameObject CameraGO;
            public Camera     Camera;
            public WorldRenderer Renderer;
            public WorldWrapRenderer Wrap;
            public WorldGeneratorBootstrap Bootstrap;
            public WorldMap   Map;
            public BiomeDefinition[] Biomes;
            public MockCameraInput Input;
            public GameObject InteractionGO;
            public TileHover  Hover;
        }

        private static HoverFixture BuildFixture()
        {
            var f = new HoverFixture();

            // Renderer host with World + Wrap + Bootstrap.
            f.RendererHost = new GameObject("HoverHost");
            f.Renderer = f.RendererHost.AddComponent<WorldRenderer>();
            f.Wrap = f.RendererHost.AddComponent<WorldWrapRenderer>();
            f.Wrap.Renderer = f.Renderer;
            f.Wrap.WorldTileWidth = Width;
            f.Bootstrap = f.RendererHost.AddComponent<WorldGeneratorBootstrap>();

            f.Biomes = MinimalBiomeSet();
            var settings = new WorldGenerationSettings(Width, Height, Seed,
                WorldGenerationSettings.DefaultLandRatio, f.Biomes);
            f.Map = new HexWorldGenerator().Generate(settings);
            f.Renderer.Render(f.Map);
            f.Wrap.Refresh();
            SetBootstrapLatestMap(f.Bootstrap, f.Map);

            // Camera positioned above the world looking straight down.
            // Orthographic projection so screen-pixel ↔ world-position
            // mapping is linear and stable. Explicit pixelRect so
            // Camera.ScreenPointToRay / WorldToScreenPoint have a
            // defined pixel space even in -nographics batch mode
            // (no RenderTexture — that requires a GPU and fails to
            // create headless).
            f.CameraGO = new GameObject("HoverCamera");
            f.Camera = f.CameraGO.AddComponent<Camera>();
            f.Camera.orthographic = true;
            f.Camera.orthographicSize = 8f;
            f.Camera.aspect = 1f;
            f.Camera.nearClipPlane = 0.1f;
            f.Camera.farClipPlane = 200f;
            f.Camera.pixelRect = new Rect(0f, 0f, 512f, 512f);
            f.CameraGO.transform.position = new Vector3(7f, 30f, 4.5f);
            f.CameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // look straight down

            // TileInteraction host with mock input + TileHover.
            f.InteractionGO = new GameObject("Interaction");
            f.Input = f.InteractionGO.AddComponent<MockCameraInput>();
            f.Hover = f.InteractionGO.AddComponent<TileHover>();
            WireField(f.Hover, "_camera", f.Camera);
            WireField(f.Hover, "_wrapRenderer", f.Wrap);
            WireField(f.Hover, "_bootstrap", f.Bootstrap);
            WireField(f.Hover, "_inputSource", f.Input);
            return f;
        }

        private static void Teardown(HoverFixture f)
        {
            if (f.InteractionGO != null) Object.DestroyImmediate(f.InteractionGO);
            if (f.CameraGO != null) Object.DestroyImmediate(f.CameraGO);
            if (f.RendererHost != null) Object.DestroyImmediate(f.RendererHost);
            foreach (BiomeDefinition b in f.Biomes) Object.DestroyImmediate(b);
        }

        private static void SetBootstrapLatestMap(WorldGeneratorBootstrap bootstrap, WorldMap map)
        {
            // LatestMap has an internal setter; tests live in the same
            // assembly as Runtime via InternalsVisibleTo so direct set
            // works through reflection-free property access.
            var prop = typeof(WorldGeneratorBootstrap).GetProperty("LatestMap");
            prop.SetValue(bootstrap, map);
        }

        private static void WireField(MonoBehaviour target, string field, UnityEngine.Object value)
        {
            var f = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, $"Field {field} not found on {target.GetType().Name}.");
            f.SetValue(target, value);
        }

        // ---- tests ----

        [UnityTest]
        public IEnumerator Hover_OverKnownTile_ResolvesToCorrectCoord()
        {
            HoverFixture f = BuildFixture();
            yield return null;
            try
            {
                // Pick a canonical tile near the world center and project
                // its world center to a screen-space mouse position the
                // mock input will report.
                HexCoord target = HexCoordExtensions.FromOffsetOddR(3, 2);
                Vector3 targetWorld = HexMesh.WorldPosition(3, 2, 0, 0.15f);
                Vector2 screen = f.Camera.WorldToScreenPoint(targetWorld);
                f.Input.MousePositionValue = screen;
                Physics.SyncTransforms();
                f.Hover.Tick();
                yield return null;

                Assert.That(f.Hover.HoveredCoord.HasValue, Is.True,
                    "Expected the cursor over the tile to produce a hovered coord.");
                Assert.That(f.Hover.HoveredCoord.Value, Is.EqualTo(target),
                    $"Hover resolved to {f.Hover.HoveredCoord.Value}, expected {target}.");
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator Hover_OffMap_ReportsNullCoord()
        {
            HoverFixture f = BuildFixture();
            yield return null;
            try
            {
                // Point the cursor far above the screen so the ray
                // shoots off into sky — no tile collider, no plane
                // intersection on the map side.
                f.Input.MousePositionValue = new Vector2(-1000f, -1000f);
                Physics.SyncTransforms();
                f.Hover.Tick();
                yield return null;

                Assert.That(f.Hover.HoveredCoord.HasValue, Is.False);
                Assert.That(f.Hover.HoverDuration, Is.EqualTo(0f));
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator Hover_DurationProgressesWhenStable()
        {
            HoverFixture f = BuildFixture();
            yield return null;
            try
            {
                HexCoord target = HexCoordExtensions.FromOffsetOddR(4, 3);
                Vector3 targetWorld = HexMesh.WorldPosition(4, 3, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(targetWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                yield return null;
                float firstSample = f.Hover.HoverDuration;

                // Hold the same cursor position across two more frames
                // and Ticks; HoverDuration should grow with Time.time.
                yield return null;
                f.Hover.Tick();
                yield return null;
                f.Hover.Tick();
                float laterSample = f.Hover.HoverDuration;

                Assert.That(laterSample, Is.GreaterThan(firstSample),
                    $"HoverDuration should progress while hover is stable; first={firstSample}, later={laterSample}.");
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator Hover_ChangingTile_FiresOnHoverChangedAndResetsDuration()
        {
            HoverFixture f = BuildFixture();
            yield return null;
            try
            {
                var changeLog = new List<HexCoord?>();
                f.Hover.OnHoverChanged += c => changeLog.Add(c);

                HexCoord first = HexCoordExtensions.FromOffsetOddR(2, 2);
                Vector3 firstWorld = HexMesh.WorldPosition(2, 2, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(firstWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                yield return null;
                f.Hover.Tick();
                float durationBeforeChange = f.Hover.HoverDuration;

                HexCoord second = HexCoordExtensions.FromOffsetOddR(5, 4);
                Vector3 secondWorld = HexMesh.WorldPosition(5, 4, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(secondWorld);
                yield return null;
                f.Hover.Tick();
                float durationAfterChange = f.Hover.HoverDuration;

                Assert.That(changeLog.Count, Is.GreaterThanOrEqualTo(2),
                    "Expected at least two OnHoverChanged events (first hover + change).");
                Assert.That(f.Hover.HoveredCoord.HasValue, Is.True);
                Assert.That(f.Hover.HoveredCoord.Value, Is.EqualTo(second));
                Assert.That(durationAfterChange, Is.LessThan(durationBeforeChange),
                    "HoverDuration should reset on hover change.");
            }
            finally { Teardown(f); }
        }
    }
}
