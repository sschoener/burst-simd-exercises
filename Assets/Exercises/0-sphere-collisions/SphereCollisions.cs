// Given a list of spheres and another sphere, compute the index of the first sphere that overlaps with the additional
// sphere, and the number of intersections in total.
// Feel free to change the data around to whatever format is the most suitable for doing SIMD.
using Unity.Burst;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

[BurstCompile]
public unsafe class SphereCollisions : MonoBehaviour
{
    delegate int SphereVsSpheres(Sphere* spheres, int numSpheres, float3* sphere, float radius, out int numIntersections);
    delegate int SphereVsSpheresSimd(Sphere* spheres, int numSpheres, float3* sphere, float radius, out int numIntersections);

    SphereVsSpheres m_SphereVsSpheres;
    SphereVsSpheresSimd m_SphereVsSpheresSimd;

    bool m_UseSimd;

    ProfilerMarker m_SphereVsSpheresMarker = new ProfilerMarker("SphereVsSpheres");
    ProfilerMarker m_SphereVsSpheresSimdMarker = new ProfilerMarker("SphereVsSpheresSimd");

    Sphere[] m_Spheres;

    struct Sphere
    {
        public float3 Position;
        public float Radius;
    }

    void Start()
    {
        m_SphereVsSpheres = BurstCompiler.CompileFunctionPointer<SphereVsSpheres>(DoSphereVsSpheres).Invoke;
        m_SphereVsSpheresSimd = BurstCompiler.CompileFunctionPointer<SphereVsSpheresSimd>(DoSphereVsSpheresSimd).Invoke;
        m_Spheres = new Sphere[4096];
    }

    void Update()
    {
        for (int i = 0; i < m_Spheres.Length; i++)
        {
            m_Spheres[i] = new Sphere
            {
                Position = new float3(Random.value, Random.value, Random.value),
                Radius = Random.value * .01f
            };
        }

        float3 center = new float3(Random.value, Random.value, Random.value);
        float radius = Random.value * .01f;

        int firstOverlap, numIntersections;
        fixed (Sphere* spheres = m_Spheres)
        {
            m_SphereVsSpheresMarker.Begin();
            firstOverlap = m_SphereVsSpheres(spheres, m_Spheres.Length, &center, radius, out numIntersections);
            m_SphereVsSpheresMarker.End();
        }

        int firstOverlapSimd, numIntersectionsSimd;
        fixed (Sphere* spheres = m_Spheres)
        {
            m_SphereVsSpheresSimdMarker.Begin();
            firstOverlapSimd = m_SphereVsSpheresSimd(spheres, m_Spheres.Length, &center, radius, out numIntersectionsSimd);
            m_SphereVsSpheresSimdMarker.End();
        }
        Assert.AreEqual(firstOverlap, firstOverlapSimd, "The index of the first overlap must be the same!");
        Assert.AreEqual(numIntersections, numIntersectionsSimd, "The number of intersections must be the same!");
    }

    /// <summary>
    /// Computes the index of the first sphere that overlaps with the given sphere.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static int DoSphereVsSpheres(Sphere* spheres, int numSpheres, float3* centerPtr, float radius, out int numIntersections)
    {
        float3 center = *centerPtr;
        int first = numSpheres;
        int n = 0;
        for (int i = 0; i < numSpheres; i++)
        {
            float r = spheres[i].Radius + radius;
            if (math.distancesq(spheres[i].Position, center) < r * r)
            {
                if (i < first)
                    first = i;
                n++;
            }
        }

        numIntersections = n;

        return first == numSpheres ? -1 : first;
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static int DoSphereVsSpheresSimd(Sphere* spheres, int numSpheres, float3* center, float radius, out int numIntersections)
    {
        // YOUR SIMD CODE HERE
        numIntersections = 0;
        return -1;
    }
}
