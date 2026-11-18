using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// A translucent color wash laid over one tile: every mesh the tile
    /// shows (base hex, feature models, resource props) is copied with a
    /// flat transparent material, so the whole tile reads as tinted.
    /// Used by the demo hover and selection layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copies live under their own container, not under the tile, so
    /// the tile hierarchy is never modified and the wrap renderer never
    /// clones a highlight. Cliff pieces (children named <c>"Cliff"</c>)
    /// are skipped: they sit under the tile and are not part of it.
    /// </para>
    /// <para>
    /// The material comes from <c>Content/Highlight/TileHighlight.mat</c>
    /// when one is passed in. Pass it from a serialized field so the
    /// shader is included in player builds; when none is given the
    /// shader is looked up by name, which only works in the editor or
    /// when the shader is otherwise referenced by the build.
    /// </para>
    /// </remarks>
    public sealed class TileHighlight : IDisposable
    {
        /// <summary>Shader name of the shipped highlight shader.</summary>
        public const string ShaderName = "Hex World Studio/Tile Highlight";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly GameObject _container;
        private readonly Material _material;
        private GameObject _target;

        /// <summary>
        /// Creates a hidden highlight.
        /// </summary>
        /// <param name="name">Name of the container GameObject.</param>
        /// <param name="color">Tint color. Alpha sets how strong the wash is.</param>
        /// <param name="template">Material to copy (usually <c>TileHighlight.mat</c>). May be null.</param>
        /// <param name="parent">Optional parent for the container.</param>
        public TileHighlight(string name, Color color, Material template, Transform parent = null)
        {
            _container = new GameObject(name);
            if (parent != null) _container.transform.SetParent(parent, worldPositionStays: false);
            _container.SetActive(false);

            if (template != null)
            {
                _material = new Material(template);
            }
            else
            {
                Shader shader = Shader.Find(ShaderName);
                if (shader != null) _material = new Material(shader);
            }
            if (_material != null) _material.SetColor(BaseColorId, color);
        }

        /// <summary>The tile root currently highlighted, or null.</summary>
        public GameObject Target => _target;

        /// <summary>The container holding the tinted copies. Exposed for tests.</summary>
        public GameObject Container => _container;

        /// <summary>
        /// Highlights <paramref name="tileRoot"/>, rebuilding the copies
        /// only when the target changes. Null hides the highlight.
        /// </summary>
        public void Show(GameObject tileRoot)
        {
            if (tileRoot == null) { Hide(); return; }
            if (tileRoot != _target)
            {
                _target = tileRoot;
                Rebuild();
            }
            if (!_container.activeSelf) _container.SetActive(true);
        }

        /// <summary>Hides the highlight and forgets the target.</summary>
        public void Hide()
        {
            _target = null;
            ClearCopies();
            if (_container != null && _container.activeSelf) _container.SetActive(false);
        }

        /// <summary>Destroys the container and the material copy.</summary>
        public void Dispose()
        {
            DestroySafely(_container);
            DestroySafely(_material);
        }

        private void Rebuild()
        {
            ClearCopies();
            if (_material == null) return;

            var filters = new List<MeshFilter>();
            CollectMeshes(_target.transform, filters);

            for (int i = 0; i < filters.Count; i++)
            {
                MeshFilter source = filters[i];
                var sourceRenderer = source.GetComponent<MeshRenderer>();
                if (sourceRenderer == null || !sourceRenderer.enabled || source.sharedMesh == null) continue;

                var copy = new GameObject(source.name);
                Transform t = copy.transform;
                t.SetParent(_container.transform, worldPositionStays: false);
                t.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                t.localScale = source.transform.lossyScale;

                copy.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                int subMeshes = Mathf.Max(1, source.sharedMesh.subMeshCount);
                var materials = new Material[subMeshes];
                for (int m = 0; m < subMeshes; m++) materials[m] = _material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        // Depth-first walk that stops at cliff pieces, which belong under
        // the tile rather than to it.
        private static void CollectMeshes(Transform node, List<MeshFilter> into)
        {
            if (node.name == "Cliff") return;
            if (!node.gameObject.activeInHierarchy) return;
            var filter = node.GetComponent<MeshFilter>();
            if (filter != null) into.Add(filter);
            for (int i = 0; i < node.childCount; i++) CollectMeshes(node.GetChild(i), into);
        }

        private void ClearCopies()
        {
            if (_container == null) return;
            for (int i = _container.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = _container.transform.GetChild(i).gameObject;
                // Detach first so a same-frame Rebuild never sees it
                // (Play-mode Destroy is deferred; see ARCHITECTURE).
                child.transform.SetParent(null, worldPositionStays: false);
                DestroySafely(child);
            }
        }

        private static void DestroySafely(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying)
            {
                if (obj is GameObject go) go.SetActive(false);
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
        }
    }
}
