// This is a pathtracer inspired by https://www.kevinbeason.com/smallpt/.
// Porting this to SIMD is a fun exercise, but it is not straight-forward.
// A few notes:
//   * everything is represented as spheres (which produces artifacts that we will ignore)
//   * each pixel is sampled multiple times for some anti-aliasing
//   * we accumulate samples over many frames so you can immediately see some output
// Most of the code should be straight forward, except for the parts about sampling and reflection/refraction. It's
// fine to treat them as black boxes.
// If you want to SIMD this code, I'd suggest trying to compute multiple rays at once. For simplicity, you might want
// to get rid of all non-diffuse materials. This will already be interesting :)
//
// Also, note that this code was originally using double precision floating pointer numbers since representing the
// walls of the Cornell Box with spheres only works with huge spheres. Using single precision float leads to plenty of
// artifacts in the resulting image, but that's something we're happy to live with for the moment.

using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using static Unity.Mathematics.math;
using Random = Unity.Mathematics.Random;

[BurstCompile]
public unsafe class Raytracer : MonoBehaviour
{
    public Material TargetMaterial;

    delegate void Render(float4* pixels, Sphere* spheres, int numSpheres, int step);
    delegate void RenderSimd(float4* pixels, Sphere* spheres, int numSpheres, int step);

    Render m_Raytrace;
    RenderSimd m_RaytraceSimd;

    const int k_Height = 128;
    const int k_Width = 128;
    bool m_UseSimd;
    Texture2D m_Texture;
    Sphere[] m_Spheres;
    int m_Step;

    ProfilerMarker m_RaytraceMarker = new ProfilerMarker("Raytrace");

    void Start()
    {
        m_Texture = new Texture2D(k_Width, k_Height, TextureFormat.RGBAFloat, true, false);
        m_Texture.wrapMode = TextureWrapMode.Clamp;
        TargetMaterial.mainTexture = m_Texture;

        m_Spheres = new[]
        {
            new Sphere(1E5f, float3(1+1E5f, 40.8f, 81.6f), float3(.75f, .25f, .25f)), // left
            new Sphere(1E5f, float3(99-1E5f, 40.8f, 81.6f), float3(.25f, .25f, .75f)), // right
            new Sphere(1E5f, float3(50, 40.8f, 1E5f), float3(.75f)), // back
            new Sphere(1E5f, float3(50, 40.8f, 170 - 1E5f), float3(0)), // front
            new Sphere(1E5f, float3(50, 1E5f, 81.6f), float3(.75f)), // bottom
            new Sphere(1E5f, float3(50, 81 - 1E5f, 81.6f), float3(.75f)), // top
            new Sphere(16.5f, float3(27, 16.5f, 47), float3(.999f), Mat.Specular), // mirror
            new Sphere(16.5f, float3(73, 16.5f, 78), float3(.999f), Mat.Refract), // glas
            new Sphere(600f, float3(50, 681.6f-.27f, 81.6f), float3(0), Mat.Diffuse, float3(12)) // light
        };

        m_Raytrace = BurstCompiler.CompileFunctionPointer<Render>(Raytrace).Invoke;
        m_RaytraceSimd = BurstCompiler.CompileFunctionPointer<RenderSimd>(RaytraceSimd).Invoke;
    }

    void Update()
    {
        var pixels = m_Texture.GetRawTextureData<float4>();
        if (Input.GetKeyDown(KeyCode.Space))
        {
            m_UseSimd = !m_UseSimd;
            m_Step = 0;
            UnsafeUtility.MemClear(pixels.GetUnsafePtr(), pixels.Length * sizeof(float4));
            Debug.Log("SIMD is " + (m_UseSimd ? "on" : "off"));
        }
        else
        {
            m_RaytraceMarker.Begin();

            float4* pixelPtr = (float4*)pixels.GetUnsafePtr();
            if (m_UseSimd)
            {
                fixed (Sphere* spheres = m_Spheres)
                    m_RaytraceSimd.Invoke(pixelPtr, spheres, m_Spheres.Length, m_Step);
            }
            else
            {
                fixed (Sphere* spheres = m_Spheres)
                    m_Raytrace.Invoke(pixelPtr, spheres, m_Spheres.Length, m_Step);
            }

            m_Step += 1;
            m_RaytraceMarker.End();
        }
        m_Texture.Apply();
    }

    struct Ray
    {
        public Ray(float3 origin, float3 dir)
        {
            Origin = origin;
            Dir = dir;
        }
        public float3 Origin;
        public float3 Dir;
        public float3 At(float t) => Origin + t * Dir;
    }

    struct Sphere
    {
        public Sphere(float radius, float3 pos, float3 color, Mat material = Mat.Diffuse, float3 emission = new float3())
        {
            Radius = radius;
            Position = pos;
            Color = color;
            Emission = emission;
            Material = material;
        }
        public float Radius;
        public float3 Position;
        public float3 Emission;
        public float3 Color;
        public Mat Material;

        public float Intersect(in Ray ray)
        {
            float3 op = Position - ray.Origin;
            float b = dot(op, ray.Dir);
            float det = b * b - dot(op, op) + Radius * Radius;
            if (det < 0)
                return 0;
            det = sqrt(det);
            const float eps = 1E-4f;
            if (b - det > eps) return b - det;
            if (b + det > eps) return b + det;
            return 0;
        }
    }

    enum Mat : byte {
        Diffuse, Specular, Refract
    }

    static bool Intersect(Sphere* spheres, int numSpheres, in Ray ray, out float t, out int id)
    {
        t = float.PositiveInfinity;
        id = -1;
        for (int i = 0; i < numSpheres; i++)
        {
            float d = spheres[i].Intersect(ray);
            if (d != 0 && d < t)
            {
                t = d;
                id = i;
            }
        }
        return t < float.PositiveInfinity;
    }

    static float3 Radiance(Sphere* spheres, int numSpheres, Ray r, ref Random rng)
    {
        Ray currentRay = r;
        float3 accColor = float3(0);
        float3 accReflectance = float3(1);
        int depth = 0;
        while (true)
        {
            if (!Intersect(spheres, numSpheres, currentRay, out float t, out int id))
                return accColor;
            ref Sphere obj = ref spheres[id];
            float3 intersection = currentRay.At(t);
            float3 normal = normalize(intersection - obj.Position);
            float3 nl = dot(normal, currentRay.Dir) < 0 ? normal : normal * -1;
            float3 f = obj.Color;
            accColor += accReflectance * obj.Emission;
            depth += 1;
            if (depth > 5)
            {
                float p = cmax(f);
                if (rng.NextFloat() < p)
                    f = f * (1f / p);
                else
                    return accColor;
            }
            accReflectance *= f;

            switch (obj.Material)
            {
                case Mat.Diffuse:
                {
                    // ideal diffuse reflection

                    // construct orthonormal coordinate system (w,u,v) at intersection
                    float3 w = nl;
                    float3 u;
                    if (abs(w.x) > .1f)
                        u = float3(0, 1, 0);
                    else
                        u = normalize(cross(float3(1, 0, 0), w));
                    float3 v = cross(w, u);

                    // produce cos-weighted sample on the hemisphere
                    float angle = 2 * PI * rng.NextFloat();
                    float dist = rng.NextFloat();
                    float distSqrt = sqrt(dist);
                    sincos(angle, out var r1Sin, out var r1Cos);
                    float3 dir = normalize(u * r1Cos * distSqrt + v * r1Sin * distSqrt + w * sqrt(1 - dist));
                    currentRay = new Ray(intersection, dir);
                    break;
                }
                case Mat.Specular:
                {
                    // ideal specular reflection
                    currentRay = new Ray(intersection, currentRay.Dir - normal * 2 * dot(normal, currentRay.Dir));
                    break;
                }
                case Mat.Refract:
                {
                    // ideal dielectric refraction
                    Ray reflRay = new Ray(intersection, currentRay.Dir - normal * 2 * dot(normal, currentRay.Dir));
                    bool into = dot(normal, nl) > 0;
                    const float nc = 1;
                    const float nt = 1.5f;
                    float nnt = into ? nc / nt : nt / nc;
                    float ddn = dot(currentRay.Dir, nl);
                    float cos2t = 1 - nnt * nnt * (1 - ddn * ddn);
                    if (cos2t < 0)
                    {
                        // Total internal reflection
                        currentRay = reflRay;
                        break;
                    }

                    float3 transmissionDir = normalize(currentRay.Dir * nnt - normal * ((into ? 1 : -1) * (ddn * nnt + sqrt(cos2t))));
                    const float a = nt - nc;
                    const float b = nt + nc;
                    const float R0 = a * a / (b * b);
                    float c = 1 - (into ? -ddn : dot(transmissionDir, normal));
                    float reflectance = R0 + (1 - R0) * c * c * c * c * c;
                    float p = .25f + .5f * reflectance;

                    if (rng.NextFloat() < p)
                    {
                        accReflectance *= reflectance / p;
                        currentRay = reflRay;
                    }
                    else
                    {
                        accReflectance *= (1 - reflectance) / (1 - p);
                        currentRay = new Ray(intersection, transmissionDir);
                    }
                    break;
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static void Raytrace(float4* pixels, Sphere* spheres, int numSpheres, int step)
    {
        var dim = float2(k_Width, k_Height);
        const int numSamples = 4;
        Ray cam = new Ray
        {
            Origin = float3(50, 52, 295.6f),
            Dir = normalize(float3(0, -0.042612f, -1))
        };

        float3 cx = float3(k_Width * .5135f / k_Height, 0, 0);
        float3 cy = normalize(cross(cx, cam.Dir)) * .5135f;
        for (int y = 0; y < k_Height; y++)
        {
            for (int x = 0; x < k_Width; x++)
            {
                int pixelIdx = y * k_Width + x;
                var rng = new Random((uint)(1500450271 * (y + 1) * (step + 1)));

                // subpixels
                for (int sy = 0; sy < 2; sy++)
                {
                    for (int sx = 0; sx < 2; sx++)
                    {
                        float3 result = float3(0);
                        for (int sample = 0; sample < numSamples; sample++)
                        {
                            float2 d;
                            {
                                // this sampling code makes it more probably to sample points close to the center of
                                // the pixel.
                                float2 r = 2 * rng.NextFloat2();
                                float dx = r.x < 1 ? sqrt(r.x) - 1 : 1 - sqrt(2 - r.x);
                                float dy = r.y < 1 ? sqrt(r.y) - 1 : 1 - sqrt(2 - r.y);
                                d = float2(dx, dy);
                            }

                            var s = float2(sx, sy);
                            var f = ((s + .5f + d) / 2 + float2(x, y)) / dim - .5f;
                            var dir = cx * f.x + cy * f.y + cam.Dir;
                            var ray = new Ray(cam.Origin + dir * 140, normalize(dir));
                            result += Radiance(spheres, numSpheres, ray, ref rng);
                        }

                        float3 existingMass = pixels[pixelIdx].xyz * step * numSamples;
                        int totalSamples = (step + 1) * numSamples;
                        result = (result + existingMass) / totalSamples;
                        pixels[pixelIdx] = float4(result, 1);
                    }
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static void RaytraceSimd(float4* pixels, Sphere* spheres, int numSpheres, int step)
    {
        // YOUR SIMD CODE HERE
    }
}
