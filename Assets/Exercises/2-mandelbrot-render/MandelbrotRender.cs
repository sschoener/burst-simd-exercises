// Port a renderer for the Mandelbrot fractal to SIMD!
// The Mandelbrot fractal can be computed as follows:
//  1. Take a subset of the complex plane (e.g. all float2 between (-1, -1) and (1, 1))
//  2. Map each pixel of your image to its corresponding point in that subset of the plane
//  3. For each such point c, iterate the function z => z * z + c with z = (0, 0) as the initial value
//     and * as complex multiplication.
//  4. Count the number of iterations it takes until dot(z, z) >= 4.
//  5. Color the pixel in proportion to the number of steps taken.

using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

[BurstCompile]
public unsafe class MandelbrotRender : MonoBehaviour
{
    public Material TargetMaterial;

    delegate void Render(float* pixels, float minX, float minY, float maxX, float maxY);

    Render m_RenderMandelbrot;
    Render m_RenderMandelbrotSimd;

    const int k_Height = 512;
    const int k_Width = 512;
    const int k_MaxSteps = 64;
    bool m_UseSimd;
    Texture2D m_Texture;

    static float2 s_Center = new float2(-0.7106f, 0.246575f);
    static float2 s_Min = new float2(-1.5f, -1.5f);
    static float2 s_Max = new float2(1, 1.5f);

    float m_Zoom = 1.0f;

    ProfilerMarker m_RenderMandelbrotMarker = new ProfilerMarker("RenderMandelbrot");

    void Start()
    {
        m_Texture = new Texture2D(k_Width, k_Height, TextureFormat.RFloat, true, true);
        TargetMaterial.mainTexture = m_Texture;

        m_RenderMandelbrot = BurstCompiler.CompileFunctionPointer<Render>(RenderMandelbrot).Invoke;
        m_RenderMandelbrotSimd = BurstCompiler.CompileFunctionPointer<Render>(RenderMandelbrotSimd).Invoke;
    }

    void Update()
    {
        var pixels = m_Texture.GetRawTextureData<float>();
        if (Input.GetKeyDown(KeyCode.Space))
        {
            m_UseSimd = !m_UseSimd;
            UnsafeUtility.MemClear(pixels.GetUnsafePtr(), pixels.Length * sizeof(float));
            Debug.Log("SIMD is " + (m_UseSimd ? "on" : "off"));
        }
        else
        {
            m_RenderMandelbrotMarker.Begin();

            m_Zoom *= 0.995f;
            var min = s_Min * m_Zoom + s_Center;
            var max = s_Max * m_Zoom + s_Center;
            if (m_UseSimd)
                m_RenderMandelbrotSimd((float*)pixels.GetUnsafePtr(), min.x, min.y, max.x, max.y);
            else
                m_RenderMandelbrot((float*)pixels.GetUnsafePtr(), min.x, min.y, max.x, max.y);
            m_RenderMandelbrotMarker.End();
        }
        m_Texture.Apply();
    }

    /// <summary>
    /// Colors the given pixels by the number of iterations that the corresponding point needs until to escape.
    /// </summary>
    /// <param name="pixels">Represents each pixel as a float that will be used as the red-channel.</param>
    /// <param name="minX">The minimum x value of the viewport in the complex plane.</param>
    /// <param name="minY">The minimum y value of the viewport in the complex plane.</param>
    /// <param name="maxX">The maximum x value of the viewport in the complex plane.</param>
    /// <param name="maxY">The maximum y value of the viewport in the complex plane.</param>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static void RenderMandelbrot(float* pixels, float minX, float minY, float maxX, float maxY)
    {
        float2 min = new float2(minX, minY);
        float2 max = new float2(maxX, maxY);
        float2 delta = (max - min) / new float2(k_Width, k_Height);
        const float t = 1f / k_MaxSteps;

        for (int y = 0; y < k_Height; y++)
        {
            for (int x = 0; x < k_Width; x++)
            {
                // This loop-body iterates
                //   z => z * z + c
                // until |z| >= 2, with * denoting multiplication of complex numbers.
                // We don't actually use complex multiplication, but simplify it to what you
                // see below
                var c = min + new float2(x, y) * delta;
                var z = new float2(0, 0);

                int step = 0;
                for (; step < k_MaxSteps; step++)
                {
                    var sq = z * z;
                    if (math.lengthsq(sq) > 4)
                        break;
                    z = new float2(sq.x - sq.y, 2 * z.x * z.y) + c;
                }

                pixels[y * k_Width + x] = t * step;
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static void RenderMandelbrotSimd(float* pixels, float minX, float minY, float maxX, float maxY)
    {
        // YOUR SIMD CODE HERE
        // You can assume that height/width is set to a constant that is divisible by 16
    }
}
