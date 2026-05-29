using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Lazy URP/Lit material cache. One <see cref="Material"/> instance
    /// per <see cref="BiomeDefinition"/>, shared by every tile of that
    /// biome so the SRP batcher can group draw calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The renderer instantiates one cache per <c>WorldRenderer</c> and
    /// disposes it during <c>Clear</c> / <c>OnDestroy</c>. Materials are
    /// constructed lazily on first <see cref="Get"/> for a given biome.
    /// </para>
    /// <para>
    /// Per-tile color variation (e.g., feature-driven tinting in Part B)
    /// is applied via <see cref="MaterialPropertyBlock"/> on the
    /// renderer instance, not via per-tile material instances —
    /// property blocks preserve SRP-batcher compatibility.
    /// </para>
    /// </remarks>
    internal sealed class BiomeMaterialCache : IDisposable
    {
        // URP base-color shader property name. URP/Lit exposes the
        // albedo/base color as "_BaseColor"; the Built-in standard
        // material used "_Color". We unconditionally target URP per
        // ADR-0002.
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private readonly Dictionary<BiomeDefinition, Material> _cache
            = new Dictionary<BiomeDefinition, Material>();

        private bool _disposed;

        /// <summary>
        /// Returns the material for <paramref name="biome"/>, creating
        /// it on first request. Same biome reference → same material
        /// instance.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="biome"/> is null.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when called after <see cref="Dispose"/>.
        /// </exception>
        public Material Get(BiomeDefinition biome)
        {
            if (biome == null) throw new ArgumentNullException(nameof(biome));
            if (_disposed) throw new ObjectDisposedException(nameof(BiomeMaterialCache));

            if (_cache.TryGetValue(biome, out Material existing))
            {
                return existing;
            }

            Material mat = CreateMaterialFor(biome);
            _cache[biome] = mat;
            return mat;
        }

        /// <summary>
        /// Destroys every cached material and clears the cache.
        /// Subsequent <see cref="Get"/> calls throw
        /// <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            foreach (Material mat in _cache.Values)
            {
                if (mat != null) DestroyMaterial(mat);
            }
            _cache.Clear();
            _disposed = true;
        }

        private static Material CreateMaterialFor(BiomeDefinition biome)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"); // Standard is a defensive fallback
            var mat = new Material(shader)
            {
                name = $"HexWorldStudio.Biome.{biome.BiomeName}",
            };
            if (mat.HasProperty(BaseColorProperty))
            {
                mat.SetColor(BaseColorProperty, biome.Color);
            }
            else
            {
                mat.color = biome.Color;
            }
            return mat;
        }

        // EditMode vs PlayMode: Destroy is the runtime path; in EditMode
        // (most of our tests) it leaves the material live until the next
        // frame, which can leak. DestroyImmediate handles both.
        private static void DestroyMaterial(Material mat)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mat);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }
    }
}
