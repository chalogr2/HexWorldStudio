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

        [Tooltip("Generation seed. Identical settings + identical seed produce a byte-identical WorldMap.")]
        [SerializeField] private ulong _seed = 12345UL;

        [Tooltip("Target land ratio in (0, 1). The percentile-threshold continent algorithm honors this exactly.")]
        [Range(0.05f, 0.95f)]
        [SerializeField] private float _landRatio = WorldGenerationSettings.DefaultLandRatio;

        [Tooltip("Biome content set. Drag in the 7 default .asset files (Ocean, Coast, Snow, Tundra, Arid, Temperate, Tropical) or your own.")]
        [SerializeField] private BiomeDefinition[] _biomes;

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

            var settings = new WorldGenerationSettings(_width, _height, _seed, _landRatio, _biomes);
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
    }
}
