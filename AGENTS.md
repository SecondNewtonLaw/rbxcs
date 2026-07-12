# AGENTS.md — rbxcs

Guidance for any coding agent working in this repo. [CLAUDE.md](CLAUDE.md) is the fuller version;
[PLAN.md](PLAN.md) holds the design + roadmap (phases `F0…F12`, all landed). Read both before large
changes.

## What this is

A C# → Luau transpiler for Roblox with **faithful C# semantics**, shipped as one NuGet
(`RobloxCS.Sdk`): `dotnet build` on a consumer project produces typed C# + IntelliSense + `.luau`
output, zero manual setup. C# OOP is lowered into Luau primitives + a runtime `RBXCS.class(...)`
helper — there is no dedicated class AST node.

## Layout & layering

```
src/RobloxCS.Luau.Ast   Luau AST nodes only, zero deps
   RobloxCS.Luau        LuauWriter: AST -> Luau text
   RobloxCS.Transpiler  SemanticModel-driven C# -> Luau lowering
   RobloxCS.BuildTask   MSBuild task: CSharpCompilation -> transpiler -> Rojo tree
   RobloxCS.Defs        C# refs the consumer compiles against
   RobloxCS.Runtime     Luau runtime lib (RBXCS.luau)
   RobloxCS.ApiGen      Roblox API Dump -> generated C# ref
   RobloxCS.Analyzer    Roslyn analyzer
   RobloxCS.Bindgen     Loretta-based Luau -> typed C# binding generator
   RobloxCS.Cli         `rbxcs` dotnet tool (add-package, bind)
   RobloxCS.Sdk         the single NuGet (build/*.props+*.targets)
samples/HelloWorld      consumer; build emits out-luau/
```

Layering: `Ast` ← `Luau` ← `Transpiler` ← `BuildTask`. Nodes never depend on the writer. Adding an
AST node means a matching case in `LuauWriter` (single switch — keep both in sync).

## Target frameworks (hard constraint)

`Luau.Ast`, `Luau`, `Transpiler`, `BuildTask`, `Analyzer`, `Bindgen` → **netstandard2.0** (loaded into
MSBuild/Roslyn hosts; everything they transitively load must be too). `Cli` → net10.0. The consumer
sample is net10.0 but never runs as .NET — its C# is only source to transpile.

## Build & verify

```bash
dotnet build src/RobloxCS.BuildTask/RobloxCS.BuildTask.csproj   # Ast+Luau+Transpiler+Task
dotnet build samples/HelloWorld/HelloWorld.csproj               # emits out-luau/
dotnet build rbxcs.slnx                                         # whole solution
```

Verify emitted Luau with the vendored Luau CLI at
`F:\Coding\cxx_cpp\Fission\cmake-build-release\_deps\luau-build\` (`luau-compile.exe` syntax check,
`luau.exe` run). For non-trivial transpiler changes: emit the sample, `luau-compile` every file, and
assert behavior. **Do not claim verified without running it.**

If a build fails with MSB3021/MSB3027 (task dll file-locked by a reused MSBuild node): delete the
locked `bin` dirs, or build with `-nodeReuse:false`.

## Conventions

- **Comments (STRICT):** no comments by default — good names over prose. Add one only when VITAL: a
  `HACK:`/`FIX:`/`WORKAROUND:`, a non-inferable gotcha, or a deliberate simplification with a named
  ceiling (`ponytail:`). No restating what code does, no section banners, no docstrings-by-default.
- Match surrounding style. Smallest change that works; no unrequested abstractions/flags/refactors.
- Read before edit. Verify with the tools above, not by assertion.
- Keep the PLAN.md checkboxes in sync as work lands.
