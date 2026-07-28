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
        [Header("Preset (optional)")]
        [Tooltip("Drag a WorldPreset asset here to override the per-field generation settings below. " +
                 "When set, Width/Height/LandRatio/Biomes/Cohesion/ContinentShape are all read from the preset. " +
                 "Seed and RandomizeSeedOnRegenerate stay on the bootstrap — the preset describes 'what kind " +
                 "of world', the seed picks the instance.")]
        [SerializeField] private WorldPreset _preset;

        [Header("World (used when no preset is assigned)")]
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

        [Tooltip("Which continent-shape algorithm to use. Continents = default (multiple landmasses). " +
                 "Pangaea = single supercontinent. Archipelago = many small islands. " +
                 "Fractal = irregular coastlines with fjords. InlandSea = land ring around central sea. " +
                 "Lakes = mostly land with scattered water. Shuffle = pick one at random per seed.")]
        [SerializeField] private ContinentShapeAlgorithm _continentShapeAlgorithm = WorldGenerationSettings.DefaultContinentShape;

        [Header("Resources (optional — ADR-0022)")]

        [Tooltip("Optional resource roster. Leave empty to disable resource placement entirely — the stage no-ops and no tile receives a resource. " +
                 "Populate to enable: drop ResourceDefinition assets here, hit Regenerate, and resources appear on eligible tiles.")]
        [SerializeField] private ResourceDefinition[] _resources;

        [Range(0f, 1f)]
        [Tooltip("Per-tile probability of attempting a resource placement. " +
                 "0 = no placements (effectively the same as empty resource roster). " +
                 "0.2 = Civ-style density (default; ~one in five eligible tiles attempts a placement, with per-resource spacing filtering further). " +
                 "1 = attempt placement on every tile (still gated by eligibility filters).")]
        [SerializeField] private float _resourceDensity = WorldGenerationSettings.DefaultResourceDensity;

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

        [Header("Rivers (ADR-0025)")]

        [Range(0f, 1f)]
        [Tooltip("How many rivers the world generates. " +
                 "0 = no rivers (stage no-ops entirely, lakes included). " +
                 "0.15 = default (Civ-style). " +
                 "Effective source rate is RiverDensity × moisture, so dry areas get fewer rivers than wet ones.")]
        [SerializeField] private float _riverDensity = WorldGenerationSettings.DefaultRiverDensity;

        [Range(0, 5)]
        [Tooltip("Minimum elevation level across the three tiles meeting at a river's source corner. " +
                 "Default 3 (of 6) keeps sources in the highlands; raising it toward 5 makes rivers rare.")]
        [SerializeField] private int _riverSourceMinElevation = WorldGenerationSettings.DefaultRiverSourceMinElevation;

        [Range(5, 200)]
        [Tooltip("Safety cap on river length (in steps). Rivers that reach water or merge with an existing river before this cap terminate normally.")]
        [SerializeField] private int _maxRiverLength = WorldGenerationSettings.DefaultMaxRiverLength;

        [Range(1, 20)]
        [Tooltip("Rivers shorter than this many edges are discarded instead of being left as one- or two-segment stubs.")]
        [SerializeField] private int _minRiverLength = WorldGenerationSettings.DefaultMinRiverLength;

        [Range(1, 5)]
        [Tooltip("River channels only ever run between tiles of the same elevation level. A terrace boundary is crossed by a single waterfall edge, never run along; this caps how many levels one waterfall may drop.")]
        [SerializeField] private int _maxWaterfallDrop = WorldGenerationSettings.DefaultMaxWaterfallDrop;

        [Range(0f, 0.5f)]
        [Tooltip("How deeply a basin must fill before it becomes a lake. Higher values give fewer, deeper lakes; 0 disables lake creation.")]
        [SerializeField] private float _lakeDepthThreshold = WorldGenerationSettings.DefaultLakeDepthThreshold;

        [Header("Rendering")]
        [Tooltip("World renderer that will draw the generated map. If null, this script searches its own GameObject and children.")]
        [SerializeField] private WorldRenderer _renderer;

        [Tooltip("Optional river renderer. When set, Regenerate() rebuilds river geometry after the world is rendered. Leave null to disable default river rendering — RiverEdges remains queryable on the WorldMap for custom visualizations.")]
        [SerializeField] private RiverRenderer _riverRenderer;

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

            // Preset wins when assigned — buyer-facing recipe for
            // "what kind of world". Seed stays on the bootstrap so the
            // same preset can produce many seeded worlds.
            WorldGenerationSettings settings = _preset != null
                ? _preset.ToSettings(_seed)
                : new WorldGenerationSettings(
                    _width, _height, _seed, _landRatio, _biomes,
                    _biomeCohesion, _featureCohesion,
                    _continentShapeAlgorithm,
                    _resources, _resourceDensity,
                    _riverDensity, _riverSourceMinElevation, _maxRiverLength,
                    _minRiverLength, _maxWaterfallDrop, _lakeDepthThreshold);
            LatestMap = new HexWorldGenerator().Generate(settings);
            _renderer.Render(LatestMap);

            if (_riverRenderer == null) _riverRenderer = GetComponent<RiverRenderer>();
            if (_riverRenderer != null)
            {
                _riverRenderer.Render(LatestMap);
            }

            if (_wrapRenderer == null) _wrapRenderer = GetComponent<WorldWrapRenderer>();
            if (_wrapRenderer != null)
            {
                _wrapRenderer.WorldTileWidth = LatestMap.Width;
                // Runs after the river render above, so the geometry to
                // mirror already exists. Harmless when the two
                // renderers share a GameObject.
                if (_wrapRenderer.RiverRenderer == null) _wrapRenderer.RiverRenderer = _riverRenderer;
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
