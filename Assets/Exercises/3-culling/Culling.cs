// This exercise is about implementing culling using SIMD. In the scene, there are blue spheres and red cubes rotating
// around the camera. Both kinds of objects are drawn using the BatchRendererGroup interface. This exposes an explicit
// culling interface. There are scalar culling jobs already that show you how culling could be performed for spheres
// and AABBs computed from the boxes.
// You can also look into using SIMD to compute the movement of the objects.
//
// Feel free to change the data layout wherever it is helpful for the task at hand.
//
// As a follow-up exercise, try porting all of this to ECS and collect your thoughts on how easy/hard it is to manually
// use SIMD intrinsics with ECS right now.

// About the structure of the code:
//  - we use the BatchRendererGroup interface from Unity to add a batches of spheres and cubes,
//  - there are k_BatchCount many batches of spheres and k_BatchCount many batches of cubes,
//  - the sphere batches are added first, then the cubes,
//  - when the batches are drawn, the BatchRendererGroup calls a culling callback we provide (OnPerformCulling)
//  - this is where you need to start.

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
public unsafe class Culling : MonoBehaviour
{
    public Mesh SphereMesh;
    public Mesh CubeMesh;
    public Material SphereMaterial;
    public Material CubeMaterial;

    bool m_IsPaused;
    bool m_UseSimdCulling;
    Camera m_Camera;
    BatchRendererGroup m_BatchRendererGroup;

    NativeArray<Sphere> m_Spheres;
    NativeArray<float> m_SpherePhase;
    NativeArray<float> m_SphereHeight;
    NativeArray<float> m_SphereSpeed;
    NativeArray<float> m_SphereDistance;

    NativeArray<Cube> m_Cubes;
    NativeArray<float> m_CubeHeight;
    NativeArray<float> m_CubePhase;
    NativeArray<float> m_CubeDistance;
    NativeArray<float> m_CubeSpeed;

    float m_Time;

    struct Sphere
    {
        public float3 Position;
        public float Radius;
    }

    struct Cube
    {
        public float3 Position;
        public quaternion Rotation;
        public float Size;
    }

    const int k_BatchSize = 1024 - 64;
    const int k_BatchCount = 20;
    const int k_MovementJobBatchSize = 16; // 16, 32, 64 are fine choices

    readonly int[] m_BatchIndices = new int[k_BatchCount * 2];

    /// <summary>
    /// This is the culling callback. The batch renderer calls this once and you need schedule your culling jobs here.
    /// The culling context contains an array of visible indices of the total size of all batches. Each batch has an
    /// offset into that array in the batch visibility struct. To perform the culling, write the number of visible
    /// objects per batch back into that batch's batch visibility struct and write the indices of all visible objects
    /// into the array of visible indices. The indices must be the indices of the objects in the batch.
    /// </summary>
    JobHandle PerformCulling(BatchRendererGroup renderergroup, BatchCullingContext cullingcontext)
    {
        if (m_UseSimdCulling)
        {
            // kick-off your SIMD jobs here!
            return default;
        }
        else
        {
            var sphereCulling = new SphereCullingJob
            {
                VisibleIndices = cullingcontext.visibleIndices,
                BatchVisibility = cullingcontext.batchVisibility,
                CullingPlanes = cullingcontext.cullingPlanes,
                Spheres = m_Spheres,
            }.Schedule(cullingcontext.batchVisibility.Length / 2, 1);

            var cubeCulling = new CubeCullingJob
            {
                VisibleIndices = cullingcontext.visibleIndices,
                BatchVisibility = cullingcontext.batchVisibility,
                CullingPlanes = cullingcontext.cullingPlanes,
                Cubes = m_Cubes
            }.Schedule(cullingcontext.batchVisibility.Length / 2, 1);
            return JobHandle.CombineDependencies(sphereCulling, cubeCulling);
        }
    }


    [BurstCompile]
    struct SphereCullingJob : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int> VisibleIndices;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<BatchVisibility> BatchVisibility;
        [ReadOnly]
        public NativeArray<Plane> CullingPlanes;
        [ReadOnly]
        public NativeArray<Sphere> Spheres;

        public void Execute(int batchIndex)
        {
            var planes = CullingPlanes;
            int numVisible = 0;
            var indexOffset = BatchVisibility[batchIndex].offset;
            var spheres = Spheres.Slice(k_BatchSize * batchIndex, k_BatchSize);
            for (int s = 0; s < k_BatchSize; s++)
            {
                float3 center = spheres[s].Position;
                float radius = spheres[s].Radius;
                bool visible = true;
                for (int p = 0; p < planes.Length; p++)
                {
                    var d = math.dot(planes[p].normal, center) + planes[p].distance;
                    if (d <= -radius)
                    {
                        visible = false;
                        break;
                    }
                }

                if (visible)
                {
                    VisibleIndices[indexOffset + numVisible] = s;
                    numVisible++;
                }
            }

            var output = BatchVisibility[batchIndex];
            output.visibleCount = numVisible;
            BatchVisibility[batchIndex] = output;
        }
    }

    [BurstCompile]
    struct CubeCullingJob : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int> VisibleIndices;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<BatchVisibility> BatchVisibility;

        [ReadOnly]
        public NativeArray<Plane> CullingPlanes;
        [ReadOnly]
        public NativeArray<Cube> Cubes;

        public void Execute(int batchIndexWithoutOffset)
        {
            int batchIndex = batchIndexWithoutOffset + k_BatchCount;
            var planes = CullingPlanes;
            int numVisible = 0;
            var offset = BatchVisibility[batchIndex].offset;
            var cubes = Cubes.Slice(k_BatchSize * batchIndexWithoutOffset, k_BatchSize);

            for (int c = 0; c < k_BatchSize; c++)
            {
                var halfSize = cubes[c].Size * new float3(.5f);
                var rotated = math.mul(cubes[c].Rotation, halfSize);

                // compute center and extent of an AABB for the cube
                float3 center = cubes[c].Position;
                float3 extent = math.abs(2 * rotated);

                bool visible = true;
                for (int i = 0; i < planes.Length; i++)
                {
                    float3 normal = planes[i].normal;
                    float dist = math.dot(normal, center) + planes[i].distance;
                    float radius = math.dot(extent, math.abs(normal));
                    if (dist + radius <= 0)
                    {
                        visible = false;
                        break;
                    }
                }

                if (visible)
                {
                    VisibleIndices[offset + numVisible] = c;
                    numVisible++;
                }
            }

            var output = BatchVisibility[batchIndex];
            output.visibleCount = numVisible;
            BatchVisibility[batchIndex] = output;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            m_UseSimdCulling = !m_UseSimdCulling;
            Debug.Log("SIMD culling " + (m_UseSimdCulling ? "on" : "off"));
        }

        if (Input.GetKeyDown(KeyCode.P))
            m_IsPaused = !m_IsPaused;

        JobHandle movementJob = default;
        // compute movement
        if (!m_IsPaused) {
            m_Time += Time.deltaTime;
            movementJob = UpdateMovement(default);
        }

        CopyLocalToWorld(movementJob).Complete();
    }

    #region setup & teardown
    void Start()
    {
        m_Camera = Camera.main;
        m_Spheres = new NativeArray<Sphere>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_SphereHeight = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_SpherePhase = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_SphereDistance = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_SphereSpeed = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_Cubes = new NativeArray<Cube>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_CubeHeight = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_CubePhase = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_CubeDistance = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);
        m_CubeSpeed = new NativeArray<float>(k_BatchCount * k_BatchSize, Allocator.Persistent);

        m_BatchRendererGroup = new BatchRendererGroup(PerformCulling);

        for (int batch = 0; batch < k_BatchCount; batch++)
        {
            m_BatchIndices[batch] = m_BatchRendererGroup.AddBatch(
                SphereMesh, 0, SphereMaterial, 0, ShadowCastingMode.Off, false, false,
                new Bounds(new Vector3(), new Vector3(1000, 1000, 1000)), k_BatchSize, null, null
            );
        }

        for (int batch = 0; batch < k_BatchCount; batch++)
        {
            m_BatchIndices[batch + k_BatchCount] = m_BatchRendererGroup.AddBatch(
                CubeMesh, 0, CubeMaterial, 0, ShadowCastingMode.Off, false, false,
                new Bounds(new Vector3(), new Vector3(1000, 1000, 1000)), k_BatchSize, null, null
            );
        }

        {
            var rng = new Unity.Mathematics.Random(2860486313);
            for (int s = 0; s < k_BatchCount * k_BatchSize; s++)
            {
                m_SphereDistance[s] = rng.NextFloat(5, 50);
                m_SphereHeight[s] = rng.NextFloat(-5, 5);
                m_SpherePhase[s] = rng.NextFloat(0, 2 * math.PI);
                m_SphereSpeed[s] = rng.NextFloat(.2f, 1);
                m_Spheres[s] = new Sphere
                {
                    Radius = rng.NextFloat(.3f, .8f)
                };
            }
        }

        {
            var rng = new Unity.Mathematics.Random(3267000013);
            for (int s = 0; s < k_BatchCount * k_BatchSize; s++)
            {
                m_CubeDistance[s] = rng.NextFloat(5, 50);
                m_CubeHeight[s] = rng.NextFloat(-5, 5);
                m_CubePhase[s] = rng.NextFloat(0, 2 * math.PI);
                m_CubeSpeed[s] = rng.NextFloat(.2f, 1);
                m_Cubes[s] = new Cube
                {
                    Size = rng.NextFloat(0.1f, 0.6f),
                    Rotation = rng.NextQuaternionRotation()
                };
            }
        }
    }

    void OnDestroy()
    {
        m_Spheres.Dispose();
        m_SphereDistance.Dispose();
        m_SphereHeight.Dispose();
        m_SpherePhase.Dispose();
        m_SphereSpeed.Dispose();

        m_Cubes.Dispose();
        m_CubeDistance.Dispose();
        m_CubeHeight.Dispose();
        m_CubePhase.Dispose();
        m_CubeSpeed.Dispose();

        for (int batch = 2 * k_BatchCount - 1; batch >= 0; batch--)
            m_BatchRendererGroup.RemoveBatch(batch);
        m_BatchRendererGroup.Dispose();
    }
    #endregion

    #region update movement
    JobHandle UpdateMovement(JobHandle input)
    {
        var camPos = m_Camera.transform.position;
        var sphereMovementJob = new UpdateSphereMovementJob
        {
            Height = m_SphereHeight,
            Distance = m_SphereDistance,
            Offset = camPos,
            Phase = m_SpherePhase,
            Speed = m_SphereSpeed,
            Time = m_Time,
            Spheres = m_Spheres,
        }.Schedule(k_BatchSize * k_BatchCount / k_MovementJobBatchSize, 1, input);

        var cubeMovementJob = new UpdateCubeMovementJob
        {
            Height = m_CubeHeight,
            Distance = m_CubeDistance,
            Offset = camPos,
            Phase = m_CubePhase,
            Speed = m_CubeSpeed,
            Time = m_Time,
            DeltaTime = Time.deltaTime,
            Cubes = m_Cubes
        }.Schedule(k_BatchSize * k_BatchCount / k_MovementJobBatchSize, 1, input);

        return JobHandle.CombineDependencies(sphereMovementJob, cubeMovementJob);
    }

    [BurstCompile]
    struct UpdateSphereMovementJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float> Height;
        [ReadOnly]
        public NativeArray<float> Phase;
        [ReadOnly]
        public NativeArray<float> Speed;
        [ReadOnly]
        public NativeArray<float> Distance;
        public float Time;
        public float3 Offset;

        [NativeDisableParallelForRestriction]
        public NativeArray<Sphere> Spheres;
        public void Execute(int index)
        {
            int baseOffset = index * k_MovementJobBatchSize;
            var spheres = (Sphere*)Spheres.GetUnsafePtr();
            var speed = (float*)Speed.GetUnsafeReadOnlyPtr();
            var phase = (float*)Phase.GetUnsafeReadOnlyPtr();
            var distance = (float*)Distance.GetUnsafeReadOnlyPtr();
            var height = (float*)Height.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < k_MovementJobBatchSize; i++)
            {
                int o = baseOffset + i;
                math.sincos(phase[o] + Time * speed[o], out var sin, out var cos);
                spheres[o].Position = Offset + new float3(cos, 0, sin) * distance[o] + new float3(0, height[o], 0);
            }
        }
    }

    [BurstCompile]
    struct UpdateCubeMovementJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float> Height;
        [ReadOnly]
        public NativeArray<float> Phase;
        [ReadOnly]
        public NativeArray<float> Speed;
        [ReadOnly]
        public NativeArray<float> Distance;
        public float Time;
        public float DeltaTime;
        public float3 Offset;

        [NativeDisableParallelForRestriction]
        public NativeArray<Cube> Cubes;
        public void Execute(int index)
        {
            int baseOffset = index * k_MovementJobBatchSize;
            var cubes = (Cube*)Cubes.GetUnsafePtr();
            var speed = (float*)Speed.GetUnsafeReadOnlyPtr();
            var phase = (float*)Phase.GetUnsafeReadOnlyPtr();
            var distance = (float*)Distance.GetUnsafeReadOnlyPtr();
            var height = (float*)Height.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < k_MovementJobBatchSize; i++)
            {
                int o = baseOffset + i;
                math.sincos(phase[o] + Time * speed[o], out var sin, out var cos);
                cubes[o].Position = Offset + new float3(cos, 0, sin) * distance[o] + new float3(0, height[o], 0);
                cubes[o].Rotation *= Quaternion.Euler(speed[o] * DeltaTime * .1f, speed[o] * DeltaTime * .1f, 0);
            }
        }
    }
    #endregion

    #region local to world
    JobHandle CopyLocalToWorld(JobHandle input)
    {
        JobHandle combinedHandle = default;

        for (int i = 0; i < k_BatchCount; i++)
        {
            var sphereJobHandle = new ComputeSpheresLocalToWorldJob
            {
                LocalToWorld = m_BatchRendererGroup.GetBatchMatrices(m_BatchIndices[i]),
                Spheres = m_Spheres.Slice(i * k_BatchSize, k_BatchSize)
            }.Schedule(k_BatchSize, 16, input);
            combinedHandle = JobHandle.CombineDependencies(combinedHandle, sphereJobHandle);
        }

        for (int i = 0; i < k_BatchCount; i++)
        {
            var cubeJobHandle = new ComputeCubesLocalToWorldJob
            {
                LocalToWorld = m_BatchRendererGroup.GetBatchMatrices(m_BatchIndices[i + k_BatchCount]),
                Cubes = m_Cubes.Slice(i * k_BatchSize, k_BatchSize)
            }.Schedule(k_BatchSize, 16, input);
            combinedHandle = JobHandle.CombineDependencies(combinedHandle, cubeJobHandle);
        }

        return combinedHandle;
    }

    [BurstCompile]
    struct ComputeCubesLocalToWorldJob : IJobParallelFor
    {
        [WriteOnly]
        public NativeArray<Matrix4x4> LocalToWorld;
        [ReadOnly]
        public NativeSlice<Cube> Cubes;

        public void Execute(int index)
        {
            var c = Cubes[index];
            LocalToWorld[index] = float4x4.TRS(c.Position, c.Rotation, new float3(c.Size));
        }
    }

    [BurstCompile]
    struct ComputeSpheresLocalToWorldJob : IJobParallelFor
    {
        [WriteOnly]
        public NativeArray<Matrix4x4> LocalToWorld;
        [ReadOnly]
        public NativeSlice<Sphere> Spheres;

        public void Execute(int index)
        {
            float r = Spheres[index].Radius;
            LocalToWorld[index] =  new float4x4(
                new float4(r, 0, 0, 0),
                new float4(0, r, 0, 0),
                new float4(0, 0, r, 0),
                new float4(Spheres[index].Position, 1)
            );
        }
    }
    #endregion
}