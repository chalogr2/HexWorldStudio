using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexOutline"/> — six closed-loop
    /// vertices at OuterRadius, width / color / Y-offset round-trip.
    /// </summary>
    public class HexOutlineTests
    {
        [Test]
        public void Create_ProducesGameObjectWithLineRenderer()
        {
            GameObject go = HexOutline.Create("OutlineTest", Color.white, 0.05f);
            try
            {
                Assert.That(go, Is.Not.Null);
                Assert.That(go.name, Is.EqualTo("OutlineTest"));
                Assert.That(go.GetComponent<LineRenderer>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_LineRendererHasSixPositions()
        {
            GameObject go = HexOutline.Create("Outline", Color.white, 0.05f);
            try
            {
                var lr = go.GetComponent<LineRenderer>();
                Assert.That(lr.positionCount, Is.EqualTo(6));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_LineRendererIsLooped()
        {
            GameObject go = HexOutline.Create("Outline", Color.white, 0.05f);
            try
            {
                var lr = go.GetComponent<LineRenderer>();
                Assert.That(lr.loop, Is.True, "Outline should be closed via LineRenderer.loop.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_AllVerticesSitAtOuterRadiusOnXZ()
        {
            GameObject go = HexOutline.Create("Outline", Color.white, 0.05f);
            try
            {
                var lr = go.GetComponent<LineRenderer>();
                var positions = new Vector3[lr.positionCount];
                lr.GetPositions(positions);

                for (int i = 0; i < positions.Length; i++)
                {
                    float xzMagnitude = Mathf.Sqrt(positions[i].x * positions[i].x + positions[i].z * positions[i].z);
                    Assert.That(xzMagnitude, Is.EqualTo(HexMesh.OuterRadius).Within(1e-4f),
                        $"Vertex {i} sits at XZ distance {xzMagnitude}, expected OuterRadius.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_AllVerticesShareYOffset()
        {
            const float expectedY = 0.1f;
            GameObject go = HexOutline.Create("Outline", Color.white, 0.05f, yOffset: expectedY);
            try
            {
                var lr = go.GetComponent<LineRenderer>();
                var positions = new Vector3[lr.positionCount];
                lr.GetPositions(positions);

                foreach (Vector3 p in positions)
                {
                    Assert.That(p.y, Is.EqualTo(expectedY).Within(1e-5f));
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_WidthAndColor_RoundTrip()
        {
            GameObject go = HexOutline.Create("Outline", new Color(0.2f, 0.6f, 0.9f, 1f), 0.12f);
            try
            {
                var lr = go.GetComponent<LineRenderer>();
                Assert.That(lr.startWidth, Is.EqualTo(0.12f).Within(1e-5f));
                Assert.That(lr.endWidth, Is.EqualTo(0.12f).Within(1e-5f));
                // LineRenderer encodes color via an 8-bit gradient so
                // each channel can land within one 1/255 step of the
                // requested float.
                const float channelTolerance = 1f / 255f;
                Assert.That(lr.startColor.r, Is.EqualTo(0.2f).Within(channelTolerance));
                Assert.That(lr.startColor.g, Is.EqualTo(0.6f).Within(channelTolerance));
                Assert.That(lr.startColor.b, Is.EqualTo(0.9f).Within(channelTolerance));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Create_LineRendererUsesLocalSpace()
        {
            GameObject go = HexOutline.Create("Outline", Color.white, 0.05f);
            try
            {
                Assert.That(go.GetComponent<LineRenderer>().useWorldSpace, Is.False,
                    "Outline vertices are authored in local space so the caller can position the parent.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
