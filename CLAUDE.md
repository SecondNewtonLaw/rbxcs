# CLAUDE.md — rbxcs

C# → Luau transpiler for Roblox. Faithful C# semantics. Ships as one NuGet: `dotnet build` →
typed C# + IntelliSense + `.luau` output, zero manual setup. Full design + decisions in [PLAN.md](PLAN.md).

## Comment policy (STRICT)

Do **not** add comments when writing code. Code should read on its own — good names over prose.

Add a comment **only** when it is VITAL to understanding, i.e. one of:
- `HACK:` / `FIX:` / `WORKAROUND:` — non-obvious code that exists for a specific reason.
- A specific condition or gotcha the reader cannot infer from the code (e.g. "Luau `integer` loses
  nothing here because…", "must run before base ctor per C# field-init order").
- A deliberate simplification with a known ceiling (`ponytail:` style — name the ceiling + upgrade path).

No restating what the code does. No section banners. No docstrings-by-default. No "// loop over items".
If the explanation is longer than the code it explains, the code needs a better name, not a comment.

**Exception — XML doc comments on the PUBLIC API surface.** `///` `<summary>`/`<param>`/`<returns>`/
`<remarks>`/`<example>` on public members of `RobloxCS.Defs` (and other public entry points) are
documentation contracts, not implementation narration — they are the source the docs site extracts
(`website/`, via DefaultDocumentation). Keep them; they are not the banned kind of comment.
Implementation bodies stay comment-free per the rule above.

## Architecture

```
RobloxCS.Luau.Ast    Luau AST nodes only, zero deps, namespace RobloxCS.Luau
RobloxCS.Luau        LuauWriter: AST -> Luau text
RobloxCS.Transpiler  SemanticModel-driven C# -> Luau lowering (Transpiler, ModuleMap, ModuleEmitter)
RobloxCS.BuildTask   MSBuild task: builds a CSharpCompilation, runs transpiler, emits Rojo tree
RobloxCS.Defs        C# refs the consumer compiles against (context attrs + Roblox globals + BCL subset)
RobloxCS.Runtime     Luau runtime lib (RBXCS.luau: class helper, …); copied into output tree
RobloxCS.Sdk         packaging: build/*.props+*.targets (the single NuGet)
RobloxCS.ApiGen      Roblox API Dump -> generated C# ref (RobloxApi.g.cs) + curated DataTypes
RobloxCS.Analyzer    Roslyn analyzer: flags unsupported C# constructs in the editor
RobloxCS.Bindgen     Loretta-based Luau -> typed C# binding generator (netstandard2.0)
RobloxCS.Cli         `rbxcs` dotnet tool (net10.0): add-package, bind
samples/HelloWorld   consumer; build emits samples/HelloWorld/out-luau/
```

Layering: `Ast` ← `Luau` ← `Transpiler` ← `BuildTask`. Nodes never depend on the writer.

C# OOP is **lowered** into Luau primitives + runtime `RBXCS.class(...)` calls — there is no dedicated
class node. Add an AST node only when a construct genuinely needs one; adding a node means a matching
case in `LuauWriter` (single switch — keep both in sync).

## Build & verify

netstandard2.0 for all transpiler/task/AST libs (loaded by MSBuild/Roslyn hosts). Consumer sample is net10.0
but never runs as .NET — its C# is only source to transpile.

```bash
dotnet build src/RobloxCS.BuildTask/RobloxCS.BuildTask.csproj   # builds Ast+Luau+Transpiler+Task
dotnet build samples/HelloWorld/HelloWorld.csproj               # emits out-luau/
dotnet build rbxcs.slnx                                         # whole solution (incl. Bindgen+Cli)
```

`RobloxCS.Bindgen` depends on `Loretta.CodeAnalysis.Lua` (a Roslyn-style Luau parser). `rbxcs bind` /
`rbxcs add-package` parse installed `.luau`, extract the public surface, and emit typed C# bindings
(metatable OOP → C# class; static module → static class).

Verify emitted Luau with the Luau CLI vendored in the Fission build:
`F:\Coding\cxx_cpp\Fission\cmake-build-release\_deps\luau-build\` — `luau.exe` (run), `luau-compile.exe`
(syntax check). Non-trivial transpiler changes: emit the sample, `luau-compile` every file, and run a
harness asserting behavior. Don't claim verified without running it.

MSB3021/MSB3027 task-dll lock: **fixed.** The SDK targets (`RobloxCSCopyTasks`) copy the task
assembly + deps into the consumer's `obj/rbxcs-tasks/` and load from that private copy, so a lingering
TaskHost holds the copy — never `RobloxCS.BuildTask/bin` — and rebuilding the task project is never
blocked. If you still hit a lock from a stale host, kill lingering `dotnet` processes; you should not
need to delete `bin` anymore.

## Conventions

- Match surrounding style. Smallest change that works; no unrequested abstractions/flags/refactors.
- Read before edit. Verify with the tools above, not by assertion.
- Roadmap phases are `F0…F12` in PLAN.md; keep the task list + PLAN checkboxes in sync as phases land.
