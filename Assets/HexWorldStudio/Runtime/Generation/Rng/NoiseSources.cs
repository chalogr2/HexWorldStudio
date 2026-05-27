using System;

namespace HexWorldStudio.Generation.Rng
{
    /// <summary>
    /// Vendored classic Perlin noise (Ken Perlin's improved 2002
    /// formulation). 2D and 3D sample paths, plus a cylindrical
    /// convenience that wraps the X axis around a circle so the
    /// resulting field is seam-continuous across a cylindrical
    /// <c>WorldMap</c>'s horizontal seam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The permutation table is built from a seeded <see cref="RngService"/>
    /// via Fisher-Yates shuffle, so two <see cref="Perlin"/> instances
    /// constructed from RNGs with the same state produce byte-identical
    /// output for any sample query.
    /// </para>
    /// <para>
    /// Sample output is approximately in <c>[-1, 1]</c>. The fractal
    /// Brownian variant accumulates octaves with decreasing amplitude
    /// (persistence) and increasing frequency (lacunarity), then
    /// normalizes by the summed amplitudes so the output range is
    /// roughly preserved.
    /// </para>
    /// <para>
    /// Internal: pipeline stages consume this directly; buyers do not.
    /// </para>
    /// </remarks>
    internal sealed class Perlin
    {
        // 512-entry doubled permutation. Indexing with byte-arithmetic
        // avoids modulo and bounds checks in the hot path.
        private readonly byte[] _perm;

        /// <summary>
        /// Constructs a Perlin instance whose permutation table is
        /// derived from <paramref name="rng"/>. Consumes 255
        /// <see cref="RngService.NextInt(int, int)"/> draws.
        /// </summary>
        public Perlin(RngService rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            byte[] values = new byte[256];
            for (int i = 0; i < 256; i++) values[i] = (byte)i;

            // Fisher-Yates shuffle, deterministic given the RNG state.
            for (int i = 255; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                byte tmp = values[i];
                values[i] = values[j];
                values[j] = tmp;
            }

            _perm = new byte[512];
            for (int i = 0; i < 512; i++) _perm[i] = values[i & 255];
        }

        /// <summary>Samples 2D Perlin noise at <c>(x, y)</c>.</summary>
        /// <returns>A value in approximately <c>[-1, 1]</c>.</returns>
        public float Sample2D(float x, float y)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            float xf = x - (float)Math.Floor(x);
            float yf = y - (float)Math.Floor(y);

            float u = Fade(xf);
            float v = Fade(yf);

            int aa = _perm[_perm[xi] + yi];
            int ab = _perm[_perm[xi] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi];
            int bb = _perm[_perm[xi + 1] + yi + 1];

            float x1 = Lerp(Grad2D(aa, xf, yf),        Grad2D(ba, xf - 1, yf),     u);
            float x2 = Lerp(Grad2D(ab, xf, yf - 1),    Grad2D(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v);
        }

        /// <summary>Samples 3D Perlin noise at <c>(x, y, z)</c>.</summary>
        /// <returns>A value in approximately <c>[-1, 1]</c>.</returns>
        public float Sample3D(float x, float y, float z)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            int zi = (int)Math.Floor(z) & 255;
            float xf = x - (float)Math.Floor(x);
            float yf = y - (float)Math.Floor(y);
            float zf = z - (float)Math.Floor(z);

            float u = Fade(xf);
            float v = Fade(yf);
            float w = Fade(zf);

            int a  = _perm[xi]     + yi;
            int aa = _perm[a]      + zi;
            int ab = _perm[a + 1]  + zi;
            int b  = _perm[xi + 1] + yi;
            int ba = _perm[b]      + zi;
            int bb = _perm[b + 1]  + zi;

            float x1 = Lerp(Grad3D(_perm[aa],     xf,     yf,     zf),
                            Grad3D(_perm[ba],     xf - 1, yf,     zf),     u);
            float x2 = Lerp(Grad3D(_perm[ab],     xf,     yf - 1, zf),
                            Grad3D(_perm[bb],     xf - 1, yf - 1, zf),     u);
            float y1 = Lerp(x1, x2, v);

            x1 = Lerp(Grad3D(_perm[aa + 1],       xf,     yf,     zf - 1),
                      Grad3D(_perm[ba + 1],       xf - 1, yf,     zf - 1), u);
            x2 = Lerp(Grad3D(_perm[ab + 1],       xf,     yf - 1, zf - 1),
                      Grad3D(_perm[bb + 1],       xf - 1, yf - 1, zf - 1), u);
            float y2 = Lerp(x1, x2, v);

            return Lerp(y1, y2, w);
        }

        /// <summary>
        /// Cylindrical sample: treats <paramref name="colNorm"/> as a
        /// position around a circle of radius <c>cellsX / (2π)</c> and
        /// <paramref name="rowNorm"/> as a linear axis scaled to
        /// <paramref name="cellsY"/>. The result is seam-continuous in
        /// the column axis (the value at <c>colNorm = 0</c> matches the
        /// value at <c>colNorm = 1</c> exactly).
        /// </summary>
        /// <param name="colNorm">Column position in <c>[0, 1)</c>.</param>
        /// <param name="rowNorm">Row position in <c>[0, 1]</c>.</param>
        /// <param name="cellsX">Number of noise cells around the circle.</param>
        /// <param name="cellsY">Number of noise cells along the vertical axis.</param>
        public float CylindricalSample(float colNorm, float rowNorm, float cellsX, float cellsY)
        {
            double angle = colNorm * 2.0 * Math.PI;
            float radius = cellsX / (2f * (float)Math.PI);
            float cx = radius * (float)Math.Cos(angle);
            float cy = radius * (float)Math.Sin(angle);
            float cz = rowNorm * cellsY;
            return Sample3D(cx, cy, cz);
        }

        /// <summary>
        /// Cylindrical fractal Brownian sample: stacks
        /// <paramref name="octaves"/> octaves of
        /// <see cref="CylindricalSample"/> with amplitude scaled by
        /// <paramref name="persistence"/> and frequency by
        /// <paramref name="lacunarity"/>, normalizing by the summed
        /// amplitudes to keep output in roughly <c>[-1, 1]</c>.
        /// </summary>
        public float CylindricalFractalSample(
            float colNorm,
            float rowNorm,
            float cellsX,
            float cellsY,
            int octaves,
            float persistence,
            float lacunarity)
        {
            if (octaves < 1) throw new ArgumentOutOfRangeException(nameof(octaves));

            float total = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float maxAmplitude = 0f;

            for (int i = 0; i < octaves; i++)
            {
                total += CylindricalSample(colNorm, rowNorm, cellsX * frequency, cellsY * frequency) * amplitude;
                maxAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxAmplitude;
        }

        // 6t^5 - 15t^4 + 10t^3 — Ken Perlin's improved fade.
        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Lerp(float a, float b, float t) => a + t * (b - a);

        private static float Grad2D(int hash, float x, float y)
        {
            // 8 gradient directions around the unit circle.
            switch (hash & 7)
            {
                case 0: return  x + y;
                case 1: return -x + y;
                case 2: return  x - y;
                case 3: return -x - y;
                case 4: return  x;
                case 5: return -x;
                case 6: return  y;
                default: return -y;
            }
        }

        private static float Grad3D(int hash, float x, float y, float z)
        {
            // 12 edge-midpoint gradients of the unit cube.
            switch (hash & 15)
            {
                case  0: return  x + y;
                case  1: return -x + y;
                case  2: return  x - y;
                case  3: return -x - y;
                case  4: return  x + z;
                case  5: return -x + z;
                case  6: return  x - z;
                case  7: return -x - z;
                case  8: return  y + z;
                case  9: return -y + z;
                case 10: return  y - z;
                case 11: return -y - z;
                case 12: return  x + y;
                case 13: return -y + z;
                case 14: return -x + y;
                default: return -y - z;
            }
        }
    }
}
