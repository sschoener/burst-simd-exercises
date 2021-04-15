// This file contains a bunch of smaller exercises, each with a scalar implementation and a stub for
// you to start implementing your SIMD version.
//
// In these exercises, you are not expected to change the layout of the input data.
//
// Check out the tests in SimdMathTests to check your implementations.

using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public static unsafe class SimdMath
{
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void MatrixVectorMultiply(float4x4* matrixPtr, float4* vectors, int numVectors)
    {
        float4x4 matrix = *matrixPtr;
        for (int i = 0; i < numVectors; i++)
            vectors[i] = math.mul(matrix, vectors[i]);
    }

    /// <summary>
    /// Multiply all vectors in-place with the given matrix and store them back.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void MatrixVectorMultiplySIMD(float4x4* matrixPtr, float4* vectors, int numVectors)
    {
        // YOUR SIMD CODE HERE
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void RunningSum(int* arr, int length)
    {
        int sum = 0;
        for (int i = 0; i < length; i++)
        {
            sum += arr[i];
            arr[i] = sum;
        }
    }

    /// <summary>
    /// Compute a running sum and write it back to the input array, so the i-th entry contains the
    /// sum of all elements up-to and including itself.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void RunningSumSIMD(int* arr, int length)
    {
        // YOUR SIMD CODE HERE
    }

    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void FilterSmallNumbers(float* input, int n, float* output, out int numCopied)
    {
        int copied = 0;
        for (int i = 0; i < n; i++)
        {
            if (*input < 1)
            {
                *output = *input;
                output++;
                copied++;
            }

            input++;
        }
        numCopied = copied;
    }

    /// <summary>
    /// Copy all numbers smaller 1 from the input to the output. The output is big enough to hold all of the input.
    /// Do not change the order of the numbers in the output.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Fast, CompileSynchronously = true)]
    public static void FilterSmallNumbersSIMD(float* input, int n, float* output, out int numCopied)
    {
        // YOUR SIMD CODE HERE
        numCopied = 0;
    }
}
