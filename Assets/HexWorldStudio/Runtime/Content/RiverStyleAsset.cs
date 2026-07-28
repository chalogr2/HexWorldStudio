using UnityEngine;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// Buyer-facing visual configuration for the default river
    /// renderer per ADR-0024. Bundles a material, geometric knobs
    /// (width, surface Y offset), and a stable channel color into a
    /// single ScriptableObject buyers assign on the
    /// <c>RiverRenderer</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The material carries the whole visual style: assign a static
    /// URP/Lit material for a still-water look, or the shipped
    /// <c>FlowingWater</c> shader for animated scrolling flow, or
    /// any custom shader graph with its own tiling water texture,
    /// normal map, foam, refraction, etc. The renderer emits UVs
    /// where <b>U</b> runs across the river (0 = one bank, 1 =
    /// other bank) and <b>V</b> runs along the flow — flow
    /// animation is a single UV-offset node in Shader Graph or a
    /// <c>_Time.y * _FlowSpeed</c> add in HLSL.
    /// </para>
    /// <para>
    /// <see cref="WidthRelativeToHex"/> scales the strip's
    /// cross-section width relative to a hex's flat-to-flat width;
    /// <c>0.15</c> is the shipped default (Civ-like readable
    /// ribbon). <see cref="SurfaceYOffset"/> lifts the strip above
    /// the tile top a tiny amount to prevent z-fighting with the
    /// underlying terrain mesh.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "RiverStyle", menuName = "Hex World Studio/River Style")]
    public sealed class RiverStyleAsset : ScriptableObject
    {
        /// <summary>
        /// The material used to render every river segment. Assign a
        /// static URP/Lit material for a still look or the shipped
        /// <c>FlowingWater</c> shader for scrolling flow. The
        /// renderer emits UVs with <b>U</b> across the river and
        /// <b>V</b> along the flow, so any material whose shader
        /// scrolls V produces animated water.
        /// </summary>
        [Tooltip("Material used for every river segment. Use the shipped FlowingWater shader for scrolling flow, or any URP-compatible material — the renderer supplies U-across-V-along-flow UVs so shader-graph water works transparently.")]
        public Material Material;

        /// <summary>
        /// Width of the river strip as a fraction of the hex's
        /// flat-to-flat width. <c>0.15</c> reads as a Civ-style
        /// medium ribbon; <c>0.05</c> is a stream; <c>0.4</c> is a
        /// wide navigable river.
        /// </summary>
        [Range(0.02f, 0.6f)]
        [Tooltip("River strip width as a fraction of hex flat-to-flat width. Default 0.15 = Civ-style medium ribbon. Used as the headwater width; see MaxWidthRelativeToHex.")]
        public float WidthRelativeToHex = 0.15f;

        /// <summary>
        /// Width of a fully-grown trunk river, as a fraction of the
        /// hex's flat-to-flat width. The renderer tapers each river
        /// from <see cref="WidthRelativeToHex"/> at its source up to
        /// this at its mouth, scaled by
        /// <see cref="RiverPath.StrahlerOrder"/>, so a river fed by
        /// many tributaries reads visibly larger than a headwater
        /// stream (ADR-0025). Set equal to
        /// <see cref="WidthRelativeToHex"/> for uniform-width rivers.
        /// </summary>
        [Range(0.02f, 0.9f)]
        [Tooltip("Width of a fully-grown trunk river. Rivers taper from WidthRelativeToHex at the source up to this at the mouth, based on Strahler order. Set equal to WidthRelativeToHex for uniform width.")]
        public float MaxWidthRelativeToHex = 0.3f;

        /// <summary>
        /// Y offset above the tile's pivot where the river strip
        /// sits. The pivot is the tile's transform origin; this
        /// offset lifts the strip to the top of the terrain base
        /// surface. Default <c>0.4</c> matches the shipped Kenney
        /// Hexagon Kit tile height. Buyers with shorter prefabs
        /// decrease it; buyers with taller prefabs (or flat
        /// procedural hexes) tune accordingly.
        /// </summary>
        [Range(-0.1f, 1f)]
        [Tooltip("Y offset above the tile pivot where the strip sits. 0.4 = Kenney Hexagon Kit tile surface (default). Increase for taller prefabs; decrease for flat/procedural tiles.")]
        public float SurfaceYOffset = 0.4f;

        /// <summary>
        /// Base tint applied via the material's <c>_BaseColor</c>
        /// property when the shader supports it. Falls back to a
        /// visible cyan-blue if the material is null or the shader
        /// exposes no color property.
        /// </summary>
        [Tooltip("Base tint applied via the material's _BaseColor property (URP/Lit and the shipped FlowingWater shader both use this).")]
        [ColorUsage(showAlpha: true, hdr: false)]
        public Color BaseColor = new Color(0.35f, 0.55f, 0.85f, 0.9f);

        /// <summary>
        /// Fraction of a waterfall edge spent on the flat lip above the
        /// drop, and again on the flat plunge pool below it (ADR-0025).
        /// The remainder is the near-vertical face. <c>0.25</c> gives a
        /// quarter lip, half fall, quarter pool. Lower values make the
        /// drop longer and more gradual.
        /// </summary>
        [Range(0.05f, 0.45f)]
        [Tooltip("Fraction of a waterfall edge spent on the flat lip above the drop and on the plunge pool below it. The rest is the vertical face.")]
        public float WaterfallLipFraction = 0.25f;
    }
}
