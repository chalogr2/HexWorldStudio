using System;
using UnityEngine;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// One entry in <see cref="ResourceDefinition.ReplacementPrefabs"/>:
    /// the prefab to spawn when a resource of this definition lands on
    /// a tile carrying the given <see cref="Biome"/> + <see cref="Feature"/>
    /// combination. Either reference may be <c>null</c> when the
    /// matching axis is wildcard (empty <c>AllowedBiomes</c> or
    /// <c>AllowedFeatures</c>) — the inspector auto-populates entries
    /// from <c>AllowedBiomes</c> × <c>AllowedFeatures</c> per ADR-0023.
    /// </summary>
    [Serializable]
    public struct ResourceReplacementEntry
    {
        /// <summary>The biome key for this replacement combo. <c>null</c> = wildcard.</summary>
        public BiomeDefinition Biome;

        /// <summary>The feature key for this replacement combo. <c>null</c> = wildcard.</summary>
        public TileFeatureDefinition Feature;

        /// <summary>
        /// Prefab that replaces the tile visual when a resource of
        /// this definition lands on a tile matching this entry's
        /// (Biome, Feature) key. <c>null</c> means "no replacement
        /// authored for this combo" — the renderer falls back to the
        /// default add-on-top behavior for tiles with this combo.
        /// </summary>
        public GameObject Prefab;
    }

    /// <summary>
    /// A single placeable resource — wheat, iron, gold, fish, etc. —
    /// authored as a ScriptableObject so buyers add, remove, or
    /// reconfigure resources purely from the inspector with no engine
    /// code changes. Resource placement is opt-in (per ADR-0022): a
    /// world with an empty <c>WorldGenerationSettings.Resources</c>
    /// roster generates with no resources at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A resource appears on a tile only when every eligibility filter
    /// passes: the tile's biome is in
    /// <see cref="AllowedBiomes"/> (or that array is empty / null,
    /// which means "any biome"); its feature is in
    /// <see cref="AllowedFeatures"/> (or that array is empty / null);
    /// and its <c>ElevationLevel</c> is within
    /// <see cref="MinElevationLevel"/>..<see cref="MaxElevationLevel"/>.
    /// Among the eligible resources, the placement stage picks one
    /// weighted by <see cref="Rarity"/>.
    /// </para>
    /// <para>
    /// The renderer instantiates <see cref="Prefab"/> (when assigned)
    /// as a child of the tile root, layered above any biome / feature
    /// prefab. The flat-hex <c>MeshCollider</c> remains on the tile
    /// root, so hover / click picking continues to resolve to the
    /// canonical coord regardless of resource prefab geometry.
    /// </para>
    /// <para>
    /// 18 shipped resources live under
    /// <c>Assets/HexWorldStudio/Content/Resources/</c>. Buyers
    /// duplicate-and-edit those to author their own, or build from
    /// scratch via Create → Hex World Studio → Resource Definition.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Resource", menuName = "Hex World Studio/Resource Definition")]
    public sealed class ResourceDefinition : ScriptableObject
    {
        /// <summary>Human-readable name for diagnostics, overlays, and tooling.</summary>
        public string ResourceName;

        /// <summary>
        /// Display color used by editor overlays / diagnostic views
        /// (resource map mode, etc.). The runtime renderer prefers
        /// <see cref="Prefab"/> for visualization; the color is the
        /// fallback when no prefab is assigned and a buyer is rendering
        /// resources via the procedural path.
        /// </summary>
        public Color Color = Color.yellow;

        /// <summary>
        /// Optional 3D prefab. When non-null the default renderer
        /// instantiates it as a child of the tile root on every tile
        /// that carries this resource. When null the resource is still
        /// placed in the data layer (<see cref="HexTile.Resource"/>)
        /// but contributes no scene-graph geometry.
        /// </summary>
        public GameObject Prefab;

        /// <summary>
        /// Biome filter. Empty or null = wildcard (the resource can
        /// appear in any biome). Non-empty = only tiles whose
        /// <see cref="HexTile.Biome"/> reference is contained in this
        /// array are eligible.
        /// </summary>
        public BiomeDefinition[] AllowedBiomes;

        /// <summary>
        /// Tile-feature filter. Empty or null = wildcard (the resource
        /// is feature-agnostic). Non-empty = only tiles whose
        /// <see cref="HexTile.Feature"/> reference is contained in
        /// this array are eligible. Note: a tile with a null feature
        /// (water tiles, or land tiles whose biome has no allow-list)
        /// never matches a non-empty <c>AllowedFeatures</c> filter.
        /// </summary>
        public TileFeatureDefinition[] AllowedFeatures;

        /// <summary>
        /// Lower bound of the elevation-level filter (inclusive).
        /// Default <c>0</c> matches every tile from sea-level land
        /// upward; water tiles also have level 0 per Slice 11.1.
        /// </summary>
        public int MinElevationLevel = 0;

        /// <summary>
        /// Upper bound of the elevation-level filter (inclusive).
        /// Default <c>5</c> matches all six default elevation levels.
        /// Set to a smaller value to constrain a resource to lowland
        /// terrain (e.g., wheat) or to hills / mountains (e.g., gold).
        /// </summary>
        public int MaxElevationLevel = 5;

        /// <summary>
        /// Weight used by the placement stage's weighted random
        /// draw among eligible resources. Higher = more likely to be
        /// chosen when multiple resources are eligible on the same
        /// tile. Set to <c>0</c> to disable the resource (its
        /// asset stays in the roster but is never picked).
        /// </summary>
        public float Rarity = 1f;

        /// <summary>
        /// Minimum hex distance between two tiles carrying this same
        /// resource. The placement stage skips a placement that would
        /// be within this radius of an earlier same-type placement,
        /// preventing visual clumping. <c>0</c> disables the check.
        /// </summary>
        public int MinimumSpacing = 2;

        /// <summary>
        /// When <c>true</c>, the renderer replaces the tile's biome /
        /// feature visual with a per-(biome, feature) prefab from
        /// <see cref="ReplacementPrefabs"/> instead of adding a
        /// resource prefab on top of it. Default <c>false</c> — the
        /// resource <see cref="Prefab"/> is spawned as a layered child
        /// on the tile's normal biome / feature visual.
        /// </summary>
        /// <remarks>
        /// See ADR-0023. The inspector auto-syncs
        /// <see cref="ReplacementPrefabs"/> entries to the cross-product
        /// of <see cref="AllowedBiomes"/> × <see cref="AllowedFeatures"/>
        /// when this flag is on, so the buyer sees one prefab slot per
        /// combo the resource can land on. When the runtime resource
        /// placement chooses this resource for a tile, the renderer
        /// looks up the entry whose (Biome, Feature) matches the tile
        /// and instantiates that entry's prefab as the tile's sole
        /// visual; if no matching entry has a non-null prefab, the
        /// renderer falls back to the default add-on-top behavior.
        /// </remarks>
        public bool ReplaceTile;

        /// <summary>
        /// One prefab per (Biome, Feature) combination the resource
        /// can land on. Populated automatically by the inspector when
        /// <see cref="ReplaceTile"/> is on — buyers fill the prefab
        /// slots; the entries themselves are kept in sync with
        /// <see cref="AllowedBiomes"/> × <see cref="AllowedFeatures"/>.
        /// Each entry's prefab fully replaces the tile visual for the
        /// matching combo (no biome / feature prefab is instantiated
        /// alongside it).
        /// </summary>
        public ResourceReplacementEntry[] ReplacementPrefabs;

        /// <summary>
        /// Returns the replacement prefab the renderer should use for
        /// a tile carrying the given (biome, feature) combination, or
        /// <c>null</c> when no entry matches (renderer should fall
        /// back to the default add-on-top path).
        /// </summary>
        public GameObject GetReplacementPrefab(BiomeDefinition biome, TileFeatureDefinition feature)
        {
            if (!ReplaceTile) return null;
            if (ReplacementPrefabs == null) return null;
            for (int i = 0; i < ReplacementPrefabs.Length; i++)
            {
                ResourceReplacementEntry e = ReplacementPrefabs[i];
                if (e.Biome == biome && e.Feature == feature) return e.Prefab;
            }
            return null;
        }

        /// <summary>
        /// Returns <c>true</c> when this resource's filters are
        /// satisfied by the tile's current biome, feature, and
        /// elevation level. Spacing is checked separately by the
        /// placement stage (it requires global state).
        /// </summary>
        /// <param name="biome">Tile biome reference, may be <c>null</c>.</param>
        /// <param name="feature">Tile feature reference, may be <c>null</c>.</param>
        /// <param name="elevationLevel">Tile elevation level in <c>[0, N-1]</c>.</param>
        public bool MatchesEnvironment(BiomeDefinition biome, TileFeatureDefinition feature, int elevationLevel)
        {
            if (elevationLevel < MinElevationLevel || elevationLevel > MaxElevationLevel) return false;

            if (AllowedBiomes != null && AllowedBiomes.Length > 0)
            {
                bool biomeOk = false;
                for (int i = 0; i < AllowedBiomes.Length; i++)
                {
                    if (AllowedBiomes[i] == biome) { biomeOk = true; break; }
                }
                if (!biomeOk) return false;
            }

            if (AllowedFeatures != null && AllowedFeatures.Length > 0)
            {
                bool featureOk = false;
                for (int i = 0; i < AllowedFeatures.Length; i++)
                {
                    if (AllowedFeatures[i] == feature) { featureOk = true; break; }
                }
                if (!featureOk) return false;
            }

            return true;
        }
    }
}
