using HexWorldStudio.Generation;
using HexWorldStudio.Rendering;
using UnityEngine;

namespace HexWorldStudio.Examples.QuickStart
{
    /// <summary>
    /// Small on-screen panel that lets someone at the keyboard build a new
    /// world while the scene is running: new random world, rebuild a typed
    /// seed, step through map types, nudge the land ratio.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built to be dropped in and pulled out freely. Add it to any
    /// GameObject in a scene that has a <see cref="WorldGeneratorBootstrap"/>
    /// and it finds the bootstrap on its own. Remove it and nothing else
    /// notices, because no other script references it.
    /// </para>
    /// <para>
    /// Drawn with IMGUI and reads keys through <see cref="Event"/>, so it
    /// needs no Canvas, no EventSystem, no fonts, and works the same under
    /// the legacy Input Manager, the Input System package, or both.
    /// </para>
    /// <para>
    /// Keys: Space or N builds a new random world, H hides or shows the
    /// panel, Esc quits (stops Play mode in the editor). Hotkeys are
    /// ignored while the seed field has focus; there, Esc just leaves the
    /// field so typing a seed can never quit by accident.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RegenerateOverlay : MonoBehaviour
    {
        // Shuffle is left out on purpose: it picks one of these at random,
        // so the label would not tell the viewer what they are looking at.
        private static readonly ContinentShapeAlgorithm[] s_MapTypes =
        {
            ContinentShapeAlgorithm.Continents,
            ContinentShapeAlgorithm.Pangaea,
            ContinentShapeAlgorithm.Archipelago,
            ContinentShapeAlgorithm.Fractal,
            ContinentShapeAlgorithm.InlandSea,
            ContinentShapeAlgorithm.Lakes,
        };

        private const float LandStep = 0.05f;
        private const float PanelWidth = 260f;
        private const string SeedControlName = "RegenerateOverlay.Seed";

        [Tooltip("Bootstrap to drive. Leave empty to use the first one found in the scene.")]
        [SerializeField] private WorldGeneratorBootstrap _bootstrap;

        [Tooltip("Show the panel when the scene starts. H toggles it either way.")]
        [SerializeField] private bool _visibleOnStart = true;

        [Tooltip("Quit the application when Esc is pressed (stops Play mode in the editor). Turn off if the host game owns Esc.")]
        [SerializeField] private bool _escapeQuits = true;

        [Tooltip("Panel scale. 0 picks one from the screen height so it stays readable on large displays.")]
        [Min(0f)]
        [SerializeField] private float _scale;

        private bool _visible;
        private string _seedText = string.Empty;
        private ulong _shownSeed;
        private bool _hasShownSeed;
        private Rect _panelRect;

        private void Awake()
        {
            _visible = _visibleOnStart;
        }

        private WorldGeneratorBootstrap Bootstrap
        {
            get
            {
                if (_bootstrap == null) _bootstrap = FindFirstObjectByType<WorldGeneratorBootstrap>();
                return _bootstrap;
            }
        }

        private void OnGUI()
        {
            WorldGeneratorBootstrap bootstrap = Bootstrap;
            if (bootstrap == null) return;

            float scale = _scale > 0f ? _scale : Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            SyncSeedText(bootstrap);
            HandleKeys(bootstrap);

            if (!_visible) return;

            // Height is measured after layout, so the first frame draws
            // with the previous rect. Harmless, and it keeps this simple.
            _panelRect = GUILayout.Window(GetInstanceID(), new Rect(24f, 24f, PanelWidth, 0f),
                id => DrawPanel(bootstrap), "Hex World");
        }

        // Keep the field showing the seed actually on screen, but never
        // overwrite what someone is typing.
        private void SyncSeedText(WorldGeneratorBootstrap bootstrap)
        {
            if (_hasShownSeed && _shownSeed == bootstrap.Seed) return;
            if (GUI.GetNameOfFocusedControl() == SeedControlName) return;
            _shownSeed = bootstrap.Seed;
            _hasShownSeed = true;
            _seedText = bootstrap.Seed.ToString();
        }

        private void HandleKeys(WorldGeneratorBootstrap bootstrap)
        {
            Event e = Event.current;

            // Clicking the world should give hotkeys back even if the seed
            // field had focus.
            if (e.type == EventType.MouseDown)
            {
                Vector2 p = e.mousePosition;
                if (!_visible || !_panelRect.Contains(p)) GUIUtility.keyboardControl = 0;
            }

            if (e.type != EventType.KeyDown) return;

            bool typing = GUI.GetNameOfFocusedControl() == SeedControlName;
            if (typing)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    BuildTypedSeed(bootstrap);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    GUIUtility.keyboardControl = 0;
                    _seedText = bootstrap.Seed.ToString();
                    e.Use();
                }
                return;
            }

            switch (e.keyCode)
            {
                case KeyCode.Space:
                case KeyCode.N:
                    bootstrap.Regenerate();
                    e.Use();
                    break;
                case KeyCode.H:
                    _visible = !_visible;
                    e.Use();
                    break;
                case KeyCode.Escape:
                    if (!_escapeQuits) break;
                    e.Use();
                    Quit();
                    break;
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawPanel(WorldGeneratorBootstrap bootstrap)
        {
            if (GUILayout.Button("New world  (Space)", GUILayout.Height(32f)))
            {
                bootstrap.Regenerate();
            }

            GUILayout.Space(6f);
            GUILayout.Label("Seed");
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(SeedControlName);
            _seedText = GUILayout.TextField(_seedText, 20);
            if (GUILayout.Button("Build", GUILayout.Width(56f)))
            {
                BuildTypedSeed(bootstrap);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (bootstrap.Preset != null)
            {
                GUILayout.Label("Preset: " + bootstrap.Preset.name);
            }
            else
            {
                DrawMapTypeRow(bootstrap);
                DrawLandRow(bootstrap);
            }

            GUILayout.Space(4f);
            GUILayout.Label("H hides this panel");
            if (_escapeQuits) GUILayout.Label("Esc quits");
        }

        private void DrawMapTypeRow(WorldGeneratorBootstrap bootstrap)
        {
            int index = System.Array.IndexOf(s_MapTypes, bootstrap.ContinentShapeAlgorithm);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Map", GUILayout.Width(40f));
            if (GUILayout.Button("<", GUILayout.Width(28f))) StepMapType(bootstrap, index, -1);
            GUILayout.Label(index >= 0 ? MapTypeLabel(s_MapTypes[index]) : bootstrap.ContinentShapeAlgorithm.ToString(),
                CenteredLabel());
            if (GUILayout.Button(">", GUILayout.Width(28f))) StepMapType(bootstrap, index, +1);
            GUILayout.EndHorizontal();
        }

        private void DrawLandRow(WorldGeneratorBootstrap bootstrap)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Land", GUILayout.Width(40f));
            if (GUILayout.Button("-", GUILayout.Width(28f))) NudgeLand(bootstrap, -LandStep);
            GUILayout.Label(Mathf.RoundToInt(bootstrap.LandRatio * 100f) + "%", CenteredLabel());
            if (GUILayout.Button("+", GUILayout.Width(28f))) NudgeLand(bootstrap, +LandStep);
            GUILayout.EndHorizontal();
        }

        // Map type and land changes rebuild the same seed so the viewer
        // sees what the setting does, not a different world.
        private static void StepMapType(WorldGeneratorBootstrap bootstrap, int index, int step)
        {
            int next = index < 0 ? 0 : (index + step + s_MapTypes.Length) % s_MapTypes.Length;
            bootstrap.ContinentShapeAlgorithm = s_MapTypes[next];
            bootstrap.Regenerate(bootstrap.Seed);
        }

        private static void NudgeLand(WorldGeneratorBootstrap bootstrap, float delta)
        {
            bootstrap.LandRatio = Mathf.Round((bootstrap.LandRatio + delta) * 20f) / 20f;
            bootstrap.Regenerate(bootstrap.Seed);
        }

        private void BuildTypedSeed(WorldGeneratorBootstrap bootstrap)
        {
            if (ulong.TryParse(_seedText.Trim(), out ulong seed))
            {
                GUIUtility.keyboardControl = 0;
                bootstrap.Regenerate(seed);
            }
            else
            {
                // Not a number: put the current seed back rather than
                // leaving junk in the field.
                _seedText = bootstrap.Seed.ToString();
            }
        }

        private static string MapTypeLabel(ContinentShapeAlgorithm type) =>
            type == ContinentShapeAlgorithm.InlandSea ? "Inland Sea" : type.ToString();

        private static GUIStyle s_Centered;

        private static GUIStyle CenteredLabel()
        {
            if (s_Centered == null)
            {
                s_Centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            }
            return s_Centered;
        }
    }
}
