// Implement ray-triangle intersection using SIMD.
// Pay special attention to how you load vertices using the indices (maybe try AVX2 gather instructions?).

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Assertions;

[BurstCompile]
public unsafe class TrianglePicking : MonoBehaviour
{
    public Mesh Mesh;
    public Transform MeshTransform;
    public Transform IntersectionMarker;

    Camera m_Camera;

    NativeArray<float3> m_Vertices;
    NativeArray<int> m_Indices;

    delegate float FindIntersection(float3* vertices, int* indices, int numTriangles, RayData* ray);
    delegate float FindIntersectionSimd(float3* vertices, int* indices, int numTriangles, RayData* ray);

    delegate void TransformVertices(float3* vertices, int numVertices, float4x4* matrix);

    FindIntersection m_FindIntersection;
    FindIntersectionSimd m_FindIntersectionSimd;
    TransformVertices m_TransformVertices;

    ProfilerMarker m_FindIntersectionMarker = new ProfilerMarker("FindIntersection");
    ProfilerMarker m_FindIntersectionSimdMarker = new ProfilerMarker("FindIntersectionSimd");

    void Start()
    {
        m_Camera = Camera.main;
        Debug.Assert(Mesh.GetTopology(1) == MeshTopology.Triangles);

        var vertices = Mesh.vertices;
        m_Vertices = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        m_Vertices.Reinterpret<Vector3>().CopyFrom(vertices);
        m_Indices = new NativeArray<int>(Mesh.triangles, Allocator.Persistent);

        m_FindIntersection = BurstCompiler.CompileFunctionPointer<FindIntersection>(FindTriangleIntersection).Invoke;
        m_FindIntersectionSimd = BurstCompiler.CompileFunctionPointer<FindIntersectionSimd>(FindTriangleIntersectionSimd).Invoke;
        m_TransformVertices = BurstCompiler.CompileFunctionPointer<TransformVertices>(UpdateVertices).Invoke;

        float4x4 localToWorld = MeshTransform.localToWorldMatrix;
        m_TransformVertices((float3*)m_Vertices.GetUnsafePtr(), m_Vertices.Length, &localToWorld);
    }

    void OnDestroy()
    {
        m_Vertices.Dispose();
        m_Indices.Dispose();
    }

    struct RayData
    {
        public float3 Origin;
        public float3 Direction;
    }

    void Update()
    {
        var unityRay = m_Camera.ScreenPointToRay(Input.mousePosition);
        var ray = new RayData
        {
            Direction = unityRay.direction,
            Origin = unityRay.origin
        };
        float3* vertices = (float3*)m_Vertices.GetUnsafeReadOnlyPtr();
        int* indices = (int*)m_Indices.GetUnsafeReadOnlyPtr();

        m_FindIntersectionMarker.Begin();
        var rayIntersectionT = m_FindIntersection(vertices, indices, m_Indices.Length / 3, &ray);
        m_FindIntersectionMarker.End();

        m_FindIntersectionSimdMarker.Begin();
        var rayIntersectionTSimd = m_FindIntersectionSimd(vertices, indices, m_Indices.Length / 3, &ray);
        m_FindIntersectionSimdMarker.End();

        float3 intersection = ray.Origin + rayIntersectionT * ray.Direction;
        bool hasIntersection = !float.IsNaN(rayIntersectionT) && !float.IsInfinity(rayIntersectionT);
        bool hasIntersectionSimd = !float.IsNaN(rayIntersectionTSimd) && !float.IsInfinity(rayIntersectionTSimd);

        if (hasIntersection)
            IntersectionMarker.transform.position = intersection;
        IntersectionMarker.gameObject.SetActive(hasIntersection);

        Assert.AreEqual(hasIntersection, hasIntersectionSimd, "SIMD code doesn't agree with reference as to whether there is an intersection.");
        if (hasIntersection && hasIntersectionSimd)
            Assert.AreApproximatelyEqual(rayIntersectionT, rayIntersectionTSimd, $"SIMD code returned intersection parameter {rayIntersectionTSimd} which is too far from actual intersection {rayIntersectionT}.");
    }

    [BurstCompile(CompileSynchronously = true)] // note the lack of Fast-math; doesn't play nicely with NaNs
    static float FindTriangleIntersection(float3* vertices, int* indices, int numTriangles, RayData* rayPtr)
    {
        RayData ray = *rayPtr;
        float minT = float.PositiveInfinity;
        for (int tri = 0; tri < numTriangles; tri++)
        {
            int idx0 = indices[3 * tri + 0];
            int idx1 = indices[3 * tri + 1];
            int idx2 = indices[3 * tri + 2];

            float t = IntersectRayTriangle(ray, vertices[idx0], vertices[idx1], vertices[idx2]);
            minT = math.min(t, minT);
        }
        return minT;
    }

    /// <summary>
    /// Ray-versus-triangle intersection test suitable for ray-tracing etc.
    /// Port of Möller–Trumbore algorithm c++ version from:
    /// https://en.wikipedia.org/wiki/Möller–Trumbore_intersection_algorithm
    ///
    /// Adapted from https://answers.unity.com/questions/861719/a-fast-triangle-triangle-intersection-algorithm-fo.html
    /// </summary>
    /// <returns><c>The distance along the ray to the intersection</c> if one exists, <c>NaN</c> if one does not.</returns>
    /// <param name="ray">Le ray.</param>
    /// <param name="v0">A vertex of the triangle.</param>
    /// <param name="v1">A vertex of the triangle.</param>
    /// <param name="v2">A vertex of the triangle.</param>
    static float IntersectRayTriangle(RayData ray, float3 v0, float3 v1, float3 v2)
    {
        const float epsilon = 0.000001f;
        float3 e1 = v1 - v0;
        float3 e2 = v2 - v0;

        float3 h = math.cross(ray.Direction, e2);
        float a = math.dot(e1, h);
        if (a > -epsilon && a < epsilon)
            return float.NaN;

        float f = 1.0f / a;
        float3 s = ray.Origin - v0;
        float u = f * math.dot(s, h);
        if (u < 0.0f || u > 1.0f)
            return float.NaN;

        float3 q = math.cross(s, e1);
        float v = f * math.dot(ray.Direction, q);
        if (v < 0.0f || u  + v > 1.0f)
            return float.NaN;

        float t = f * math.dot(e2, q);
        return t > epsilon ? t : float.NaN;
    }

    /// <summary>
    /// Compute the minimum parameter along the input ray where the given ray intersects any of the given triangles. It
    /// is assumed that you do not perform backside culling.
    /// The return value should be greater than 0 and finite.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static float FindTriangleIntersectionSimd(float3* vertices, int* indices, int numTriangles, RayData* ray)
    {
        // YOUR SIMD CODE HERE
        return float.PositiveInfinity;
    }

    [BurstCompile(CompileSynchronously = true)]
    static void UpdateVertices(float3* vertices, int numVertices, float4x4* matrixPtr)
    {
        float4x4 matrix = *matrixPtr;
        for (int v = 0; v < numVertices; v++)
        {
            vertices[v] = math.mul(matrix, new float4(vertices[v], 1)).xyz;
        }
    }
}
