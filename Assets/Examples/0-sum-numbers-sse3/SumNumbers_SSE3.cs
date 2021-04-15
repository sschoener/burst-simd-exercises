// Demonstrates how to use SSE to compute the sum of an array of floats.

using System;
using Unity.Burst;
using UnityEngine;

using Unity.Burst.Intrinsics;
using Unity.Profiling;
using UnityEngine.Assertions;
using static Unity.Burst.Intrinsics.X86;
// Get all SSE intrinsics in scope
using static Unity.Burst.Intrinsics.X86.Sse;
using static Unity.Burst.Intrinsics.Arm.Neon;
using Random = UnityEngine.Random;

[BurstCompile]
public unsafe class SumNumbers_SSE3 : MonoBehaviour
{
    delegate float F(float* arr, int count);
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
            m_SumNumbersMarker.Begin();
            float r1 = m_SumNumbers(arr, m_Data.Length);
            m_SumNumbersMarker.End();

            m_SumNumbersSimdMarker.Begin();
            float r2 = m_SumNumbersSimd(arr, m_Data.Length);
            m_SumNumbersSimdMarker.End();

            // Highly scientific way to detect errors in the implementation
            Debug.Assert(Mathf.Abs(r2 - r1) <= 500, $"{nameof(ComputeSumSimd)} returned an unreasonable result.");
            Debug.Log("Sum: " + r1);
            Debug.Log("Sum SIMD: " + r2);
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    static float ComputeSum(float* arr, int count)
    {
        float sum = 0;
        for (int i = 0; i < count; i++)
            sum += arr[i];
        return sum;
    }

    [BurstCompile(CompileSynchronously = true)]
    static float ComputeSumSimd(float* arr, int count)
    {
        // We're just going to assume that the length of the data is a multiple of 4, otherwise we'd have to handle the
        // other cases. It's not hard, but tedious.
        Assert.IsTrue(count % 4 == 0);

        if (Ssse3.IsSsse3Supported)
        {
            // To sum up all values in the array, we split the array into 4 subarrays and store their sums in the variable
            // `sum` below.
            v128 sum = new v128(0f);
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = loadu_ps(arr + i);
                sum = add_ps(sum, reg);
            }

            // At this point, we have the sums of 4 subarrays in `sum` and we still need to merge them. SSE3 has a helpful
            // instruction for this:
            sum = Sse3.hadd_ps(sum, sum);
            // Now the first and third lane hold the sum of the first two subarrays and the second and fourth lane contain
            // the sum of the last two subarrays.
            sum = Sse3.hadd_ps(sum, sum);
            // Finally, all four lanes hold the same value (the sum of all subarrays) and we can return the first value
            // as a float.
            return cvtss_f32(sum);

            // or alternatively, simply write:
            // return sum.Float0 + sum.Float1 + sum.Float2 + sum.Float3;
        }
        else if (IsNeonSupported)
        {
            // Same as above: 4 subarrays to accumulate the sum
            v128 sum = new v128(0f);
            for (int i = 0; i < count; i += 4)
            {
                // Load 4 floats from memory.
                v128 reg = vld1q_f32(arr + i);
                sum = vaddq_f32(sum, reg);
            }
            return vaddvq_f32(sum);
        }
        else
        {
            // Managed fallback, equivalent to ComputeSum()
            float sum = 0;
            for (int i = 0; i < count; i++)
                sum += arr[i];
            return sum;
        }
    }
}
