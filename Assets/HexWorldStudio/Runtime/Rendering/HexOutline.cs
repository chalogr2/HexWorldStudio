using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Static factory for hex-shaped outline GameObjects used by the
    /// demo's hover and selection visuals (ADR-0016). Builds a
    /// <see cref="LineRenderer"/> with six vertices in local space,
    /// closed via <see cref="LineRenderer.loop"/>, sitting slightly
    /// above the tile so it draws above the surface mesh.
    /// </summary>
    /// <remarks>
    /// The outline is purely visual and stays out of the renderer's
    /// material flow — buyer scripts can move it, hide it, recolor it,
    /// or destroy it without touching tile materials or
    /// <see cref="MaterialPropertyBlock"/>s.
    /// </remarks>
    public static class HexOutline
    {
        /// <summary>Default world-space Y offset applied to the outline so it draws above the tile mesh.</summary>
        public const float DefaultYOffset = 0.05f;

        /// <summary>
        /// Creates a fresh hex-outline GameObject. The caller owns it
        /// (sets its parent, position, visibility, lifetime).
        /// </summary>
        /// <param name="name">Name applied to the GameObject for inspector readability.</param>
        /// <param name="color">Line color (start = end).</param>
        /// <param name="width">Line width.</param>
        /// <param name="yOffset">Local-space Y of the six outline vertices. Defaults to <see cref="DefaultYOffset"/>.</param>
        public static GameObject Create(string name, Color color, float width, float yOffset = DefaultYOffset)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 6;
            lr.startWidth = width;
            lr.endWidth = width;

            // Built-in unlit color shader; doesn't depend on URP material
            // graph or biome material cache. In -nographics batch mode
            // Shader.Find can return null because shaders aren't
            // loaded — skip the material assignment in that case and
            // leave LineRenderer with its default material. The
            // outline geometry stays valid for tests and buyer code
            // that inspects positions / loop / vertex count regardless
            // of headless rendering.
            Shader shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Standard");
            if (shader != null) lr.material = new Material(shader);
            lr.startColor = color;
            lr.endColor = color;

            Vector3[] points = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                // Match HexMesh's ring layout: top vertex at +Z, going CCW
                // around when viewed from above.
                float angleDeg = 90f + i * 60f;
                float rad = angleDeg * Mathf.Deg2Rad;
                points[i] = new Vector3(
                    HexMesh.OuterRadius * Mathf.Cos(rad),
                    yOffset,
                    HexMesh.OuterRadius * Mathf.Sin(rad));
            }
            lr.SetPositions(points);
            return go;
        }
    }
}
