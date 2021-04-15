using NUnit.Framework;
using Unity.Burst;
using Unity.Mathematics;
using Random = UnityEngine.Random;

[BurstCompile]
public unsafe class SimdMathTests
{
    delegate void FilterSmallNumbers(float* input, int length, float* output, out int numCopied);
    delegate void MatrixVectorMultiply(float4x4* matrixPtr, float4* vectors, int numVectors);
    delegate void RunningSum(int* arr, int length);

    static void FilterSmallNumbersTestCase(uint seed, int length, FilterSmallNumbers scalar, FilterSmallNumbers simd)
    {
        var rng = new Unity.Mathematics.Random(seed);
        var inputs = new float[length];
        var outputsScalar = new float[length];
        var outputsSimd = new float[length];
        for (int i = 0; i < length; i++)
            inputs[i] = rng.NextFloat() * 3;

        int outLengthScalar, outLengthSimd;
        fixed (float* inputPtr = inputs)
        {
            fixed (float* outputPtr = outputsScalar)
                scalar(inputPtr, length, outputPtr, out outLengthScalar);
            fixed (float* outputPtr = outputsSimd)
                simd(inputPtr, length, outputPtr, out outLengthSimd);
        }

        Assert.AreEqual(outLengthScalar, outLengthSimd, $"SIMD version failed with seed {seed} with input length {length}: Different number of results");
        for (int i = 0; i < outLengthScalar; i++)
        {
            Assert.AreEqual(outputsScalar[i], outputsSimd[i], $"SIMD version failed with seed {seed} with input length {length}: Different output at index {i}");
        }
    }

    [Test]
    public static void FilterSmallNumbersWorksWith16()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<FilterSmallNumbers>(SimdMath.FilterSmallNumbers).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<FilterSmallNumbers>(SimdMath.FilterSmallNumbersSIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5, 20) * 16;
            uint seed = 3628273133 * ((uint)testCase + 1);
            FilterSmallNumbersTestCase(seed, length, scalar, simd);
        }
    }

    [Test]
    public static void FilterSmallNumbersWorksWithOddLengths()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<FilterSmallNumbers>(SimdMath.FilterSmallNumbers).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<FilterSmallNumbers>(SimdMath.FilterSmallNumbersSIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5 * 16, 20 * 16);
            uint seed = 3628273133 * ((uint)testCase + 1);
            FilterSmallNumbersTestCase(seed, length, scalar, simd);
        }
    }

    static void MatrixVectorMultiplyTestCase(uint seed, int length, MatrixVectorMultiply scalar, MatrixVectorMultiply simd)
    {
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(seed);
        var matrix = new float4x4(rng.NextFloat4(), rng.NextFloat4(), rng.NextFloat4(), rng.NextFloat4());
        var vectorsScalar = new float4[length];
        var vectorsSimd = new float4[length];
        for (int i = 0; i < length; i++)
            vectorsScalar[i] = vectorsSimd[i] = rng.NextFloat4();

        fixed (float4* outputPtr = vectorsScalar)
            scalar(&matrix, outputPtr, length);
        fixed (float4* outputPtr = vectorsSimd)
            simd(&matrix, outputPtr, length);

        for (int i = 0; i < length; i++)
        {
            bool almostEqual = math.all(
                math.abs(vectorsScalar[i] - vectorsSimd[i]) < new float4(0.01f)
            );
            Assert.IsTrue(almostEqual, $"SIMD version failed with seed {seed} with input length {length}: Different output at index {i}");
        }
    }

    [Test]
    public static void MatrixVectorMultiplyWorksWith16()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<MatrixVectorMultiply>(SimdMath.MatrixVectorMultiply).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<MatrixVectorMultiply>(SimdMath.MatrixVectorMultiplySIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5, 20) * 16;
            uint seed = 3628273133 * ((uint)testCase + 1);
            MatrixVectorMultiplyTestCase(seed, length, scalar, simd);
        }
    }

    [Test]
    public static void MatrixVectorMultiplyWorksWithOddLengths()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<MatrixVectorMultiply>(SimdMath.MatrixVectorMultiply).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<MatrixVectorMultiply>(SimdMath.MatrixVectorMultiplySIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5 * 16, 20 * 16);
            uint seed = 3628273133 * ((uint)testCase + 1);
            MatrixVectorMultiplyTestCase(seed, length, scalar, simd);
        }
    }

    static void RunningSumTestCase(uint seed, int length, RunningSum scalar, RunningSum simd)
    {
        Unity.Mathematics.Random rng = new Unity.Mathematics.Random(seed);
        var arrayScalar = new int[length];
        var arraySimd = new int[length];
        for (int i = 0; i < length; i++)
            arrayScalar[i] = arraySimd[i] = rng.NextInt();

        fixed (int* outputPtr = arrayScalar)
            scalar(outputPtr, length);
        fixed (int* outputPtr = arraySimd)
            simd(outputPtr, length);

        for (int i = 0; i < length; i++)
        {
            Assert.AreEqual(arrayScalar[i], arraySimd[i], $"SIMD version failed with seed {seed} with input length {length}: Different output at index {i}");
        }
    }

    [Test]
    public static void RunningSumWorksWith16()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<RunningSum>(SimdMath.RunningSum).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<RunningSum>(SimdMath.RunningSumSIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5, 20) * 16;
            uint seed = 3628273133 * ((uint)testCase + 1);
            RunningSumTestCase(seed, length, scalar, simd);
        }
    }

    [Test]
    public static void RunningSumWorksWithOddLengths()
    {
        var scalar = BurstCompiler.CompileFunctionPointer<RunningSum>(SimdMath.RunningSum).Invoke;
        var simd = BurstCompiler.CompileFunctionPointer<RunningSum>(SimdMath.RunningSumSIMD).Invoke;

        for (int testCase = 0; testCase < 100; testCase++)
        {
            Random.InitState(testCase);
            int length = Random.Range(5 * 16, 20 * 16);
            uint seed = 3628273133 * ((uint)testCase + 1);
            RunningSumTestCase(seed, length, scalar, simd);
        }
    }
}
