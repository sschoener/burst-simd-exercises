// Demonstrates how to use SSE to count the number of floats below a certain threshold in an array.

using System;
using Unity.Burst;
using UnityEngine;

using Unity.Burst.Intrinsics;
using Unity.Profiling;
using UnityEngine.Assertions;
using static Unity.Burst.Intrinsics.X86;
using static Unity.Burst.Intrinsics.X86.Sse;
using static Unity.Burst.Intrinsics.X86.Sse2;
using static Unity.Burst.Intrinsics.Arm.Neon;
using Random = UnityEngine.Random;

[BurstCompile]
public unsafe class CountSmallNumbers_SSE2 : MonoBehaviour
{
    delegate int F(float* arr, int count, float threshold);
    F m_CountSmallNumbers;
    F m_CountSmallNumbersSimd;

    float[] m_Data; 
    
    void Start()
    {
        m_CountSmallNumbers = BurstCompiler.CompileFunctionPointer<F>(CountSmallNumbers).Invoke;
        m_CountSmallNumbersSimd = BurstCompiler.CompileFunctionPointer<F>(CountSmallNumbersSimd).Invoke;

        m_Data = new float[1024 * 1024 * 16];
        for (int i = 0; i < m_Data.Length; i++)
            m_Data[i] = Random.value;
    }

    ProfilerMarker m_CountSmallNumbersMarker = new ProfilerMarker(nameof(CountSmallNumbers));
    ProfilerMarker m_CountSmallNumbersSimdMarker = new ProfilerMarker(nameof(CountSmallNumbersSimd));
    
    void Update()
    {
        fixed (float* arr = m_Data)
        {
            const float threshold = 0.45f;
            m_CountSmallNumbersMarker.Begin();
            int r1 = m_CountSmallNumbers(arr, m_Data.Length, threshold);
            m_CountSmallNumbersMarker.End();

            m_CountSmallNumbersSimdMarker.Begin();
            int r2 = m_CountSmallNumbersSimd(arr, m_Data.Length, threshold);
            m_CountSmallNumbersSimdMarker.End();

            Debug.Assert(r1 == r2, $"{nameof(CountSmallNumbersSimd)} returned an unreasonable result.");
            Debug.Log("Count: " + r1);
            Debug.Log("Count SIMD: " + r2);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    static int CountSmallNumbers(float* arr, int count, float threshold)
    {
        int c = 0;
        for (int i = 0; i < count; i++)
            c += arr[i] < threshold ? 1 : 0;
        return c;
    }

    [BurstCompile(CompileSynchronously = true)]
    static int CountSmallNumbersSimd(float* arr, int count, float threshold)
    {
        // We're just going to assume that the length of the data is a multiple of 4, otherwise we'd have to handle the
        // other cases. It's not hard, but tedious.
        Assert.IsTrue(count % 4 == 0);

        // Create a 128bit vector that has all its lanes set to `threshold`.
        v128 th = new v128(threshold);
        if (IsSse2Supported)
        {
            v128 accum = new v128();
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = loadu_ps(arr + i);

                // Compare the loaded data against the threshold. If the value in a lane is smaller than the threshold, the
                // lane will be set to 0xFFFFFFFF (all-one bitmask), otherwise to 0x0.
                v128 cmpResult = cmplt_ps(reg, th);

                // Subtract the compare result (thus, adding since we're subtracting -1) into 4 parallel accumulators
                accum = sub_epi32(accum, cmpResult);
            }
            return accum.SInt0 + accum.SInt1 + accum.SInt2 + accum.SInt3;
        }
        else if (IsNeonSupported)
        {
            v128 accum = new v128();
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = vld1q_f32(arr + i);

                // Compare the loaded data against the threshold. If the value in a lane is smaller than the threshold, the
                // lane will be set to 0xFFFFFFFF (all-one bitmask), otherwise to 0x0.
                v128 cmpResult = vcltq_f32(reg, th);

                // Subtract the compare result (thus, adding since we're subtracting -1) into 4 parallel accumulators
                accum = vsubq_s32(accum, cmpResult);
            }
            return vaddvq_s32(accum);
        }
        else
        {
            // Managed fallback, equivalent to CountSmallNumbers()
            int c = 0;
            for (int i = 0; i < count; i++)
                c += arr[i] < threshold ? 1 : 0;
            return c;
        }
    }
}
