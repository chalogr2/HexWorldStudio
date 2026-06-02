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
    /// PlayMode tests for <see cref="TileSelection"/> — commits on
    /// primary click, clears on secondary click, clears on
    /// Bootstrap.OnRegenerated.
    /// </summary>
    public class TileSelectionPlayModeTests
    {
        private const int Width = 8;
        private const int Height = 6;
        private const ulong Seed = 0xD1D2D3D4D5D6D7D8UL;

        // Reuse the mock from TileHoverPlayModeTests' pattern.
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
                NewBiome("SOcean", new Color(0.1f, 0.25f, 0.5f), isWater: true),
                NewBiome("SCoast", new Color(0.23f, 0.49f, 0.72f), isWater: true, isCoastalWater: true),
                NewBiome("SLand",  new Color(0.44f, 0.66f, 0.36f)),
            };
        }

        private sealed class SelectionFixture
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
            public TileSelection Selection;
        }

        private static SelectionFixture BuildFixture()
        {
            var f = new SelectionFixture();

            f.RendererHost = new GameObject("SelectionHost");
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
            f.Bootstrap.LatestMap = f.Map;

            f.CameraGO = new GameObject("SelectionCamera");
            f.Camera = f.CameraGO.AddComponent<Camera>();
            f.Camera.orthographic = true;
            f.Camera.orthographicSize = 8f;
            f.Camera.aspect = 1f;
            f.Camera.nearClipPlane = 0.1f;
            f.Camera.farClipPlane = 200f;
            f.Camera.pixelRect = new Rect(0f, 0f, 512f, 512f);
            f.CameraGO.transform.position = new Vector3(7f, 30f, 4.5f);
            f.CameraGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            f.InteractionGO = new GameObject("Interaction");
            f.Input = f.InteractionGO.AddComponent<MockCameraInput>();
            f.Hover = f.InteractionGO.AddComponent<TileHover>();
            f.Selection = f.InteractionGO.AddComponent<TileSelection>();
            WireField(f.Hover, "_camera", f.Camera);
            WireField(f.Hover, "_wrapRenderer", f.Wrap);
            WireField(f.Hover, "_bootstrap", f.Bootstrap);
            WireField(f.Hover, "_inputSource", f.Input);
            WireField(f.Selection, "_hover", f.Hover);
            WireField(f.Selection, "_bootstrap", f.Bootstrap);
            WireField(f.Selection, "_inputSource", f.Input);
            return f;
        }

        private static void Teardown(SelectionFixture f)
        {
            if (f.InteractionGO != null) Object.DestroyImmediate(f.InteractionGO);
            if (f.CameraGO != null) Object.DestroyImmediate(f.CameraGO);
            if (f.RendererHost != null) Object.DestroyImmediate(f.RendererHost);
            foreach (BiomeDefinition b in f.Biomes) Object.DestroyImmediate(b);
        }

        private static void WireField(MonoBehaviour target, string field, UnityEngine.Object value)
        {
            var fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Field {field} not found on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }

        // ---- tests ----

        [UnityTest]
        public IEnumerator PrimaryClick_OverHoveredTile_CommitsSelection()
        {
            SelectionFixture f = BuildFixture();
            yield return null;
            try
            {
                HexCoord target = HexCoordExtensions.FromOffsetOddR(3, 2);
                Vector3 targetWorld = HexMesh.WorldPosition(3, 2, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(targetWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                yield return null;

                Assert.That(f.Hover.HoveredCoord.HasValue, Is.True, "Pre-condition: hover should resolve.");

                // Press the primary button this frame.
                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;
                yield return null;

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.True);
                Assert.That(f.Selection.SelectedCoord.Value, Is.EqualTo(target));
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator PrimaryClick_WithNoHoveredTile_DoesNothing()
        {
            SelectionFixture f = BuildFixture();
            yield return null;
            try
            {
                f.Input.MousePositionValue = new Vector2(-1000f, -1000f);
                f.Hover.Tick();
                Assert.That(f.Hover.HoveredCoord.HasValue, Is.False);

                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;
                yield return null;

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.False);
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator SecondaryClick_ClearsSelection()
        {
            SelectionFixture f = BuildFixture();
            yield return null;
            try
            {
                // Commit a selection first.
                HexCoord target = HexCoordExtensions.FromOffsetOddR(4, 3);
                Vector3 targetWorld = HexMesh.WorldPosition(4, 3, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(targetWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;
                yield return null;

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.True);

                // Now right-click.
                f.Input.SecondaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.SecondaryClickPressedValue = false;

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.False);
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator Regenerate_ClearsSelection()
        {
            SelectionFixture f = BuildFixture();
            yield return null;
            try
            {
                HexCoord target = HexCoordExtensions.FromOffsetOddR(2, 1);
                Vector3 targetWorld = HexMesh.WorldPosition(2, 1, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(targetWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;
                yield return null;

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.True,
                    "Pre-condition: selection should be set before Regenerate.");

                // Subscribe needs to happen via Tick (lazy resolution); a
                // tick already ran above, so we're subscribed. Now fire
                // Regenerate and expect selection to clear.
                f.Bootstrap.Regenerate();

                Assert.That(f.Selection.SelectedCoord.HasValue, Is.False,
                    "Selection should clear when Bootstrap.OnRegenerated fires.");
            }
            finally { Teardown(f); }
        }

        [UnityTest]
        public IEnumerator SelectionChange_FiresOnSelectionChangedWithNewCoord()
        {
            SelectionFixture f = BuildFixture();
            yield return null;
            try
            {
                var log = new List<HexCoord?>();
                f.Selection.OnSelectionChanged += c => log.Add(c);

                HexCoord first = HexCoordExtensions.FromOffsetOddR(2, 1);
                Vector3 firstWorld = HexMesh.WorldPosition(2, 1, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(firstWorld);
                Physics.SyncTransforms();
                f.Hover.Tick();
                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;
                yield return null;

                HexCoord second = HexCoordExtensions.FromOffsetOddR(5, 4);
                Vector3 secondWorld = HexMesh.WorldPosition(5, 4, 0, 0.15f);
                f.Input.MousePositionValue = f.Camera.WorldToScreenPoint(secondWorld);
                f.Hover.Tick();
                f.Input.PrimaryClickPressedValue = true;
                f.Selection.Tick();
                f.Input.PrimaryClickPressedValue = false;

                Assert.That(log.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(f.Selection.SelectedCoord.HasValue, Is.True);
                Assert.That(f.Selection.SelectedCoord.Value, Is.EqualTo(second));
            }
            finally { Teardown(f); }
        }
    }
}
