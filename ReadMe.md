**PRs welcome! Find something that could be improved? Do it!** :)

# SIMD using Burst
Since version 1.5, Burst supports intrinsics for both x86 and ARM SIMD extensions. This repository contains some examples and exercises for using SIMD in Burst.

The code in this repository is for educational use and aims to demonstrate how to use intrinsics using Burst. The examples are not necessarily the fastest way to compute these operations

## Where to start?
 1. Open the project in Unity 2020.3 or later.
 2. Take a look at the examples (see below) from the `Examples` folder. The examples are aimed at demonstrating both how to use intrinsics in Burst and how to use intrinsics in general. If you already have a lot of experience programming with SIMD intrinsics, you probably just want to skim the examples.
 3. Pick an exercise from the `Exercises` folder and work on it using your preferred set of intrinsics. The exercises are roughly ordered by difficulty. Each example usually consists of a scene that either displays something or will at least tell you whether you're doing the right thing when entering playmode.

With any exercise you are more than welcome to change the structure of the data around, and sometimes you won't get the best results without doing that. Also, it is instructive to look at the code Burst generated for the original implementation. Did Burst vectorize it? Is the code faster than your implementation? Slower? Why? Pick a sampling profile like VTune and figure out whether your implementation is memory-bound or compute-bound. Be curious!

## SIM-What?
SIMD is short for **S**ingle **I**nstruction **M**ultiple **D**ata. It describes the capability of your CPU to apply the same operation to multiple inputs at (usually) little to no additional cost - provided your program is structured with that idea in mind. Pretty much all modern CPUs support some form of SIMD instructions. The architecture that this repository is concerned with is x86/x64, which includes all modern PCs, Macs, and consoles, but excludes mobile devices for the largest part.

For now, it is probably sufficient to know that there are a bunch of SIMD instruction set extensions that build on each other:
 * **SSE/SSE2** are present on all modern x64 machines and allow you to process up to four floats at a time (by using 128bit vector registers called `XMM` registers),
 * **SSE3/SSE4** and its subfamilies are wide-spread on desktop platforms but not yet universally adopted; it mostly expands on the previous SSE iteration and fleshes out support for integer operations among other things,
 * **AVX** extends the `XMM` registers to 256bits `YMM` registers,
 * **AVX2** builds on AVX and extends the instruction set,
 * **AVX512** is the latest iteration of AVX but only really supported on a select few CPUs in the server space.

Burst 1.3 adds support for directly using instructions from all SSE sets plus AVX and AVX2 using *intrinsics*: short functions that the compiler recognizes and will (usually) replace with exact instructions from that instruction set.

### Where can I learn more?
 * Andreas Fredrikson's Unite CPH 2019 talk ["Intrinsics: Low-level engine development with Burst"](https://www.youtube.com/watch?v=BpwvXkoFcp8)
 * Andreas Fredrikson's GDC 2015 talk ["SIMD at Insomniac Games"](https://www.gdcvault.com/play/1022248/SIMD-at-Insomniac-Games-How)
 * Intel's [Intrinsic Guide](https://software.intel.com/sites/landingpage/IntrinsicsGuide/)

## FAQ

#### How can I easily write intrinsics without having to prefix them with their class name?
Each instruction set extension has its own class, `Unity.Burst.Intrinsics.X86.Sse` for example contains all intrinsics that are available with `SSE`. To access them without the `Sse` prefix, use a [static import](https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-static): 
```
using static Unity.Burst.Intrinsics.X86.Sse;
```

#### How can I detect whether `SSE4.2` or some other extension is available?
Burst exposes some fields that you can read from to determine whether a specific instruction set extension is supported. For example, you can use this to check for `SSE4.2` support:

```
// This branch is resolved at compile time
if (Sse4_2.IsSse42Supported) {
    // SSE4.2 code
} else {
    // probably SSE2 code as a fallback
}
```
The branches on these special fields are resolved at compile-time and Windows/Mac/Linux builds of the project ship with all different versions of your code; the right version is then automatically selected at runtime.

#### Where can I find more information about intrinsics?
A good overview of all available intrinsics in the different x86 instruction set extensions is avaiable in the [Intel Intrinsics Guide](https://software.intel.com/sites/landingpage/IntrinsicsGuide/). The naming of the intrinsics is almost exactly the same as in Burst if you drop some prefixes, e.g. `_mm256_andnot_ps` becomes simply `andnot_ps` and `m256` is called `v256` in Burst.

#### How do I make sense of the names of the intrinsics?
Most intrinsic names follow a relatively simple structure:
 * a prefix denoting the instruction set (e.g. `_mm_` for `SSE`, `_mm256_` for `AVX` etc.),
 * a short mnemonic denoting the operation (e.g. `andnot`, `add`, `cvt` for convert, `cmpgt` for compare-greater-than),
 * a suffix to describe the lane-layout of the inputs and outputs.

With Burst, the prefix is dropped since it is encoded in the class containing the intrinsic. The mnemonics are usually self-descriptive. This leaves the suffix. There are different type-codes that generally fall into two classes:
 * type-codes starting with `p` or `ep` describe a _packed_ layout; this is what you usually want and means that the intrinsic operates on all lanes of a register,
 * type-code starting with `s` describe a _scalar_ layout; those operations usually only operate on the lower-most lane of the register.

Here are the commonly used type-codes with some explanations:

 * `epiN` - packed N bit integer; the register is split into lanes of size N bits
 * `piN` - packed N bit integer; old spelling for `epiN`
 * `epuN` - packed N bit unsigned; the register is split into lanes of size N bits
 * `ps/pd` - packed single or double; the register is split into 32/64 bit IEEE754 floating point values
 * `siN` - scalar N bit integer; only operates on the lower N bits
 * `ss/sd` - scalar single or double; only operates on the lower 32/64 bits
 * `f32/f64` - float or double; usually used to convert to an actual `float` in the host language

#### How can I debug SIMD code?
The Burst implementation for SIMD intrinsics comes with a full Mono implementation of all intrinsics. Disabling Burst compilation for the function in question allows you to step through it and inspect the contents of your registers and see how the SIMD intrinsics operate on them.

#### Why does SSE code look different when compiled with `AVX` support? What does the `v` prefix mean that I see on some SSE opcodes in the Burst inspector?
Depending on your platform, Burst will select a different set of instructions to compile your code with. The machine you are working on most likely has support for `AVX`, which extends the 128bit `XMM` registers introduced with `SSE` to 256bit `YMM` registers. Unfortunately, the old `SSE` instructions only operate on the lower 128bit of any register and the processor has to do some internal book-keeping to make sure that the `YMM` registers maintain their proper state. This leads to delays when switching between `AVX` instructions and `SSE` instructions. To avoid these delays, `AVX` introduces new versions of all `SSE` opcodes that use the new `VEX` prefix, denoted by an additional `v` in the mnemonic of the instruction. Hence `SSE` intrinsics produce different code when `AVX` is enabled. See section 2.8, Programming Considerations with 128-bit SIMD instructions in [Intel's AVX manual](https://software.intel.com/sites/default/files/4f/5b/36945) for more information.
