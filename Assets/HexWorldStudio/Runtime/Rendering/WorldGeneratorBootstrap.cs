using HexWorldStudio.Content;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Demo-scene MonoBehaviour: configure generation settings in the
    /// inspector, generate a <see cref="WorldMap"/> on
    /// <see cref="Start"/>, and feed it to a <see cref="WorldRenderer"/>.
    /// </summary>
    /// <remarks>
    /// Buyers use this as a copy-paste starting point. The QuickStart
    /// scene under <c>Examples/QuickStart/</c> ships a pre-wired
    /// instance with the seven default biomes assigned.
    /// </remarks>
    public sealed class WorldGeneratorBootstrap : MonoBehaviour
    {
        [Header("World")]
        [Tooltip("Number of columns in the generated map.")]
        [SerializeField] private int _width = 32;

        [Tooltip("Number of rows in the generated map.")]
        [SerializeField] private int _height = 20;

        [Tooltip("Generation seed. Identical settings + identical seed produce a byte-identical WorldMap. " +
                 "When 'Randomize Seed On Regenerate' is enabled, this field is overwritten with a fresh " +
                 "ulong at every Regenerate so the inspector always reflects the seed actually used.")]
        [SerializeField] private ulong _seed = 12345UL;

        [Tooltip("If true, Regenerate() picks a fresh random seed before generating. Uncheck and edit the Seed field to pin a specific world. Determinism is preserved: same seed + same settings still produce byte-identical output.")]
        [SerializeField] private bool _randomizeSeedOnRegenerate = true;

        [Tooltip("Target land ratio in (0, 1). The percentile-threshold continent algorithm honors this exactly.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float _landRatio = WorldGenerationSettings.DefaultLandRatio;

        [Tooltip("Biome content set. Drag in the 7 default .asset files (Ocean, Coast, Snow, Tundra, Arid, Temperate, Tropical) or your own.")]
        [SerializeField] private BiomeDefinition[] _biomes;

        [Header("Cohesion (continuity smoothing — ADR-0019)")]

        [Range(0f, 1f)]
        [Tooltip("How aggressively neighboring tiles flip toward consensus to form coherent biome regions. " +
                 "0 = raw classification (speckled at climate boundaries). " +
                 "0.5 = Civ-like mild blobs (recommended). " +
                 "1 = strong smoothing (large continuous regions, may erase small biomes).")]
        [SerializeField] private float _biomeCohesion = WorldGenerationSettings.DefaultBiomeCohesion;

        [Range(0f, 1f)]
        [Tooltip("How aggressively neighboring tiles flip toward feature consensus. " +
                 "Defaulted lower than biome cohesion so feature variety inside a biome reads as natural diversity.")]
        [SerializeField] private float _featureCohesion = WorldGenerationSettings.DefaultFeatureCohesion;

        [Header("Rendering")]
        [Tooltip("World renderer that will draw the generated map. If null, this script searches its own GameObject and children.")]
        [SerializeField] private WorldRenderer _renderer;

        [Tooltip("Optional wrap renderer. When set, Regenerate() refreshes it after rendering so the cylindrical wrap stays in sync.")]
        [SerializeField] private WorldWrapRenderer _wrapRenderer;

        [Header("Behavior")]
        [Tooltip("Automatically generate on Start. Disable to drive Regenerate() from your own code.")]
        [SerializeField] private bool _generateOnStart = true;

        /// <summary>The most recent <see cref="WorldMap"/> produced by <see cref="Regenerate"/>.</summary>
        public WorldMap LatestMap { get; internal set; }

        // Process-lifetime seed picker. Lives outside the deterministic
        // generator pipeline — it only chooses the ulong handed to the
        // RngService, which itself is fully deterministic per seed.
        private static readonly System.Random _seedPicker = new System.Random();

        /// <summary>
        /// Fires at the end of every successful <see cref="Regenerate"/>
        /// call. Buyer scripts subscribe to clear per-tile UI state
        /// (selection, hover, custom overlays) tied to the previous
        /// world.
        /// </summary>
        public event System.Action OnRegenerated;

        private void Start()
        {
            if (_generateOnStart) Regenerate();
        }

        /// <summary>
        /// Builds a <see cref="WorldGenerationSettings"/> from the
        /// inspector fields, generates a <see cref="WorldMap"/>, and
        /// renders it. Safe to call repeatedly.
        /// </summary>
        [ContextMenu("Regenerate")]
        public void Regenerate()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<WorldRenderer>();
            }
            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<WorldRenderer>();
            }
            if (_renderer == null)
            {
                Debug.LogError("[WorldGeneratorBootstrap] No WorldRenderer assigned and none found on this GameObject or its children.", this);
                return;
            }

            if (_randomizeSeedOnRegenerate)
            {
                _seed = PickRandomSeed();
            }

            var settings = new WorldGenerationSettings(
                _width, _height, _seed, _landRatio, _biomes,
                _biomeCohesion, _featureCohesion);
            LatestMap = new HexWorldGenerator().Generate(settings);
            _renderer.Render(LatestMap);

            if (_wrapRenderer == null) _wrapRenderer = GetComponent<WorldWrapRenderer>();
            if (_wrapRenderer != null)
            {
                _wrapRenderer.WorldTileWidth = LatestMap.Width;
                _wrapRenderer.Refresh();
            }

            OnRegenerated?.Invoke();
        }

        private static ulong PickRandomSeed()
        {
            var buf = new byte[8];
            lock (_seedPicker)
            {
                _seedPicker.NextBytes(buf);
            }
            return System.BitConverter.ToUInt64(buf, 0);
        }
    }
}
