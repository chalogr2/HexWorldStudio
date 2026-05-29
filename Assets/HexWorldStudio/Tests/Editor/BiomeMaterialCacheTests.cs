using System;
using HexWorldStudio.Content;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="BiomeMaterialCache"/> — caching
    /// reuse, color flow, dispose semantics.
    /// </summary>
    public class BiomeMaterialCacheTests
    {
        private static BiomeDefinition NewBiome(string name, Color color)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.Color = color;
            return b;
        }

        [Test]
        public void Get_SameBiomeTwice_ReturnsSameMaterialInstance()
        {
            var cache = new BiomeMaterialCache();
            BiomeDefinition biome = NewBiome("A", Color.red);
            try
            {
                Material first = cache.Get(biome);
                Material second = cache.Get(biome);
                Assert.That(second, Is.SameAs(first));
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void Get_DifferentBiomes_ReturnsDifferentMaterials()
        {
            var cache = new BiomeMaterialCache();
            BiomeDefinition a = NewBiome("A", Color.red);
            BiomeDefinition b = NewBiome("B", Color.blue);
            try
            {
                Assert.That(cache.Get(a), Is.Not.SameAs(cache.Get(b)));
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Get_MaterialColorMatchesBiomeColor()
        {
            var cache = new BiomeMaterialCache();
            BiomeDefinition biome = NewBiome("Temperate", new Color(0.4f, 0.7f, 0.3f, 1f));
            try
            {
                Material mat = cache.Get(biome);
                Color actual = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                Assert.That(actual.r, Is.EqualTo(0.4f).Within(1e-5f));
                Assert.That(actual.g, Is.EqualTo(0.7f).Within(1e-5f));
                Assert.That(actual.b, Is.EqualTo(0.3f).Within(1e-5f));
            }
            finally
            {
                cache.Dispose();
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void Get_NullBiome_Throws()
        {
            var cache = new BiomeMaterialCache();
            try
            {
                Assert.Throws<ArgumentNullException>(() => cache.Get(null));
            }
            finally
            {
                cache.Dispose();
            }
        }

        [Test]
        public void Get_AfterDispose_Throws()
        {
            var cache = new BiomeMaterialCache();
            BiomeDefinition biome = NewBiome("A", Color.red);
            try
            {
                cache.Dispose();
                Assert.Throws<ObjectDisposedException>(() => cache.Get(biome));
            }
            finally
            {
                Object.DestroyImmediate(biome);
            }
        }

        [Test]
        public void Dispose_TwiceIsSafe()
        {
            var cache = new BiomeMaterialCache();
            Assert.DoesNotThrow(() =>
            {
                cache.Dispose();
                cache.Dispose();
            });
        }
    }
}
