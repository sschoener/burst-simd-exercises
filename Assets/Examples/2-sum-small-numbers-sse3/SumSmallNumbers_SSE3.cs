// Demonstrates how to use SSE to compute the sum of all numbers below a threshold in an array of floats.

using System;
using Unity.Burst;
using UnityEngine;

using Unity.Burst.Intrinsics;
using Unity.Profiling;
using UnityEngine.Assertions;
using static Unity.Burst.Intrinsics.X86;
using static Unity.Burst.Intrinsics.X86.Sse;
using static Unity.Burst.Intrinsics.Arm.Neon;
using Random = UnityEngine.Random;

[BurstCompile]
public unsafe class SumSmallNumbers_SSE3 : MonoBehaviour
{
    delegate float F(float* arr, int count, float threshold);
    F m_SumNumbers;
    F m_SumNumbersSimd;

    float[] m_Data; 
    
    void Start()
    {
        m_SumNumbers = BurstCompiler.CompileFunctionPointer<F>(ComputeSum).Invoke;
        m_SumNumbersSimd = BurstCompiler.CompileFunctionPointer<F>(ComputeSumSimd).Invoke;

        m_Data = new float[1024 * 1024 * 16];
        for (int i = 0; i < m_Data.Length; i++)
            m_Data[i] = Random.value;
    }

    ProfilerMarker m_SumNumbersMarker = new ProfilerMarker(nameof(ComputeSum));
    ProfilerMarker m_SumNumbersSimdMarker = new ProfilerMarker(nameof(ComputeSumSimd));
    
    void Update()
    {
        fixed (float* arr = m_Data)
        {
            const float threshold = 0.75f;
            m_SumNumbersMarker.Begin();
            float r1 = m_SumNumbers(arr, m_Data.Length, threshold);
            m_SumNumbersMarker.End();

            m_SumNumbersSimdMarker.Begin();
            float r2 = m_SumNumbersSimd(arr, m_Data.Length, threshold);
            m_SumNumbersSimdMarker.End();

            // Highly scientific way to detect errors in the implementation
            Debug.Assert(Mathf.Abs(r2 - r1) <= 400, $"{nameof(ComputeSumSimd)} returned an unreasonable result.");
            Debug.Log("Sum: " + r1);
            Debug.Log("Sum SIMD: " + r2);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static float ComputeSum(float* arr, int count, float threshold)
    {
        float sum = 0;
        for (int i = 0; i < count; i++)
        {
            if (arr[i] < threshold)
                sum += arr[i];
        }

        return sum;
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    static float ComputeSumSimd(float* arr, int count, float threshold)
    {
        // We're just going to assume that the length of the data is a multiple of 4, otherwise we'd have to handle the
        // other cases. It's not hard, but tedious.
        Assert.IsTrue(count % 4 == 0);

        if (Ssse3.IsSsse3Supported)
        {
            // To sum up all values in the array, we split the array into 4 subarrays and store their sums in the variable
            // `sum` below.
            v128 sum = new v128(0f);
            v128 th = new v128(threshold);
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = loadu_ps(arr + i);

                // Compare the loaded data against the threshold. If the value in a lane is smaller than the threshold, the
                // lane will be set to 0xFFFFFFFF (all-one bitmask), otherwise to 0x0.
                v128 mask = cmplt_ps(reg, th);

                // Compute the binary-and with the mask to set all numbers above the threshold to zero.
                sum = add_ps(sum, and_ps(reg, mask));
            }

            return sum.Float0 + sum.Float1 + sum.Float2 + sum.Float3;
        }
        else if (IsNeonSupported)
        {
            // To sum up all values in the array, we split the array into 4 subarrays and store their sums in the variable
            // `sum` below.
            v128 sum = new v128(0f);
            v128 th = new v128(threshold);
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = vld1q_f32(arr + i);

                // Compare the loaded data against the threshold. If the value in a lane is smaller than the threshold, the
                // lane will be set to 0xFFFFFFFF (all-one bitmask), otherwise to 0x0.
                v128 mask = vcltq_f32(reg, th);

                // Compute the binary-and with the mask to set all numbers above the threshold to zero.
                sum = vaddq_f32(sum, vandq_s32(reg, mask));
            }

            return vaddvq_f32(sum);
        }
        else
        {
            // Managed fallback, equivalent to ComputeSum()
            float sum = 0;
            for (int i = 0; i < count; i++)
            {
                if (arr[i] < threshold)
                    sum += arr[i];
            }

            return sum;
        }
    }
}
