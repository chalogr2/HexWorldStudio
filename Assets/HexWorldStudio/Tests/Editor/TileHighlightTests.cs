using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// <see cref="TileHighlight"/> copies a tile's visible meshes with the
    /// shipped translucent material, skips cliffs, and never touches the
    /// tile itself.
    /// </summary>
    public class TileHighlightTests
    {
        private const string MaterialPath = "Assets/HexWorldStudio/Content/Highlight/TileHighlight.mat";

        private GameObject _tile;
        private TileHighlight _highlight;

        private static GameObject MeshChild(string name, Transform parent, Vector3 localPos)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            return go;
        }

        [SetUp]
        public void SetUp()
        {
            // Tile root with a base mesh, a nested feature mesh, and a
            // cliff piece underneath.
            _tile = new GameObject("Tile_3_4");
            _tile.transform.position = new Vector3(5f, 0.6f, 2f);
            GameObject baseHex = MeshChild("Grassland", _tile.transform, Vector3.zero);
            MeshChild("Tree", baseHex.transform, new Vector3(0f, 0.5f, 0f));
            GameObject cliff = MeshChild("Cliff", _tile.transform, new Vector3(0f, -0.5f, 0f));
            MeshChild("CliffChild", cliff.transform, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            _highlight?.Dispose();
            if (_tile != null) Object.DestroyImmediate(_tile);
        }

        [Test]
        public void ShippedMaterial_UsesTheHighlightShader()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.That(mat, Is.Not.Null, "TileHighlight.mat is missing.");
            Assert.That(mat.shader.name, Is.EqualTo(TileHighlight.ShaderName));
        }

        [Test]
        public void Show_CopiesEveryMeshExceptCliffs_AtTheSameWorldPose()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            _highlight = new TileHighlight("Test", new Color(1f, 1f, 1f, 0.3f), mat);

            _highlight.Show(_tile);

            Transform container = _highlight.Container.transform;
            Assert.That(_highlight.Container.activeSelf, Is.True);
            Assert.That(container.childCount, Is.EqualTo(2), "Base hex and tree only; cliff pieces are not part of the tile.");

            Transform tree = _tile.transform.Find("Grassland/Tree");
            Transform treeCopy = null;
            foreach (Transform c in container) if (c.name == "Tree") treeCopy = c;
            Assert.That(treeCopy, Is.Not.Null);
            Assert.That(Vector3.Distance(treeCopy.position, tree.position), Is.LessThan(1e-4f));

            Material used = treeCopy.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(used.shader.name, Is.EqualTo(TileHighlight.ShaderName));
            Assert.That(used.GetColor("_BaseColor").a, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(used, Is.Not.SameAs(mat), "The shared asset must not be recolored.");
        }

        [Test]
        public void Show_DoesNotModifyTheTile()
        {
            int before = _tile.GetComponentsInChildren<Transform>(true).Length;
            _highlight = new TileHighlight("Test", Color.white, AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
            _highlight.Show(_tile);
            Assert.That(_tile.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(before));
            Assert.That(_highlight.Container.transform.IsChildOf(_tile.transform), Is.False);
        }

        [Test]
        public void Hide_RemovesCopies_AndShowNullHides()
        {
            _highlight = new TileHighlight("Test", Color.white, AssetDatabase.LoadAssetAtPath<Material>(MaterialPath));
            _highlight.Show(_tile);
            _highlight.Hide();
            Assert.That(_highlight.Container.activeSelf, Is.False);
            Assert.That(_highlight.Container.transform.childCount, Is.EqualTo(0));
            Assert.That(_highlight.Target, Is.Null);

            _highlight.Show(_tile);
            _highlight.Show(null);
            Assert.That(_highlight.Container.activeSelf, Is.False);
        }
    }
}
