# rbxcs — C# → Luau for Roblox, zero-setup

Rebuild of [roblox-cs](../roblox-cs). Old design = standalone `RobloxCS.exe` you install and run
as a separate step, plus a hand-maintained `Roblox.dll` for API types. Setup pain.

**New design: one NuGet package.** Drop it in, `dotnet build`, get typed C# + IntelliSense +
`.luau` output. No CLI to install, no separate run step, no hand-kept API assembly.

---

## Why not "just a source generator" for everything

A C# source generator can **only add C# back into the same compilation**. It cannot emit Luau as a
build product — its output feeds the C# compiler, not Rojo. Generators *can* write files as a side
effect, but that runs on every IDE keystroke (design-time builds), has non-deterministic ordering,
and breaks incremental caching. Wrong tool for emitting Luau.

So the work splits across three Roslyn surfaces, each used for what it is good at:

| Surface | Job | Runs when |
|---|---|---|
| **Incremental source generator** | Emit Roblox API C# stubs (`game`, services, DataTypes, enums) for IntelliSense + type-checking | Continuously in IDE, cached |
| **Analyzer** (later) | Flag unsupported C# constructs live in the editor | Continuously in IDE |
| **MSBuild task / `<Target>`** | Read the compiled syntax trees, emit `.luau` to an output dir | On `dotnet build` |

All three ship in **one NuGet** (`RobloxCS.Sdk`). That single reference is the whole setup.

---

## Project layout

```
rbxcs.slnx
PLAN.md
src/
  RobloxCS.Luau.Ast/     Luau AST nodes only, zero deps (namespace RobloxCS.Luau)
  RobloxCS.Luau/         LuauWriter: AST -> Luau text
  RobloxCS.Transpiler/   SemanticModel-driven C# -> Luau lowering (Transpiler, ModuleMap, ModuleEmitter)
  RobloxCS.BuildTask/    MSBuild task: builds a CSharpCompilation, runs transpiler, emits Rojo tree
  RobloxCS.Analyzer/     Roslyn analyzer: flags unsupported C# constructs live
  RobloxCS.ApiGen/       Roblox API Dump -> generated C# ref (RobloxApi.g.cs) + DataTypes
  RobloxCS.Defs/         C# refs the consumer compiles against (context attrs + Roblox globals + BCL subset)
  RobloxCS.Bindgen/      Loretta-based Luau -> typed C# binding generator
  RobloxCS.Cli/          `rbxcs` dotnet tool: add-package, bind
  RobloxCS.Runtime/      Luau runtime lib (RBXCS.luau); copied into the output tree
  RobloxCS.Sdk/          Packaging-only. build/*.props+*.targets bundles task+runtime = the single NuGet
samples/
  HelloWorld/            Consumer project referencing RobloxCS.Sdk, builds to out-luau/
templates/               dotnet new templates
```

### Target frameworks (hard constraints)
- `RobloxCS.Luau.Ast`, `RobloxCS.Luau`, `RobloxCS.Transpiler`, `RobloxCS.BuildTask`, `RobloxCS.Analyzer`,
  `RobloxCS.Bindgen` → **netstandard2.0**. Roslyn analyzers/generators and MSBuild tasks are loaded into
  the compiler/MSBuild host and MUST be netstandard2.0; everything the task transitively loads must be too.
  (`RobloxCS.Bindgen` is netstandard2.0 so the BuildTask can consume it later.)
- `RobloxCS.Cli` → **net10.0** (a `dotnet tool`, runs standalone, references Bindgen).
- `samples/HelloWorld` → net10.0 but never runs as .NET. C# here is just source to transpile.

---

## Build-time flow (consumer)

```
dotnet build HelloWorld
  │
  ├─ ApiGenerator injects Roblox API C# stubs  → IntelliSense + compiler type-checks user code
  ├─ C# compiles (correctness gate; output assembly is discarded/ignored)
  └─ RobloxCS.Sdk .targets runs TranspileToLuau task (AfterCompile)
        → Transpiler reads the same .cs syntax trees
        → emits obj/luau/*.luau (then Rojo/your tooling syncs to Roblox)
```

---

## Known risks / decisions

- **MSBuild task dependency hell.** The task references `Microsoft.CodeAnalysis`, which the .NET SDK
  MSBuild host also loads — version conflicts are the classic failure. Mitigation options, in order:
  (1) match the SDK's Roslyn version; (2) bundle deps under the task and load via a custom
  `AssemblyLoadContext`; (3) fall back to invoking the transpiler as an out-of-proc `dotnet` tool from
  the target. Start with (1); escalate only if it bites.
- **API stubs source.** Phase 1 hand-seeds a tiny subset (`game`, `print`, one service). Phase 3 wires
  the official Roblox **API Dump** JSON so the whole API generates. Generator reads the dump from an
  `AdditionalFiles` entry the SDK ships.
- **Transpiler coverage.** Old `LuauGenerator` (630 LOC) + Luau AST (~700 LOC) already handle a big C#
  subset. Port incrementally; don't block the pipeline proof on full coverage.

---

## Phased roadmap

- [ ] **P0 — Skeleton wired end-to-end.** All projects build, sample references SDK, build emits a real
  `.luau` from a minimal transpiler (class + method + a `print`). Proves the integration.
- [ ] **P1 — Port Luau AST + writer** from old `RobloxCS.Luau`, trimmed, netstandard2.0, no external
  `RobloxCS.Types` dep.
- [ ] **P2 — Port transpiler coverage** from old `LuauGenerator` (control flow, types, namespaces,
  operators, calls).
- [ ] **P3 — API generator from API Dump JSON.** Full typed Roblox surface, IntelliSense parity with
  the old `Roblox.dll`.
- [ ] **P4 — Analyzer.** Diagnostics for unsupported constructs, live in editor.
- [ ] **P5 — Pack + publish** `RobloxCS.Sdk` to NuGet; consumer template / `dotnet new` template.

(Superseded by the faithful-track roadmap below.)

---

## Locked decisions (from design review)

Every choice below is committed. They collectively define rbxcs as a **faithful C#-semantics**
transpiler, not a C#-flavored-Luau one.

| # | Decision | Choice | Consequence |
|---|---|---|---|
| 1 | Semantic contract | **Faithful C# semantics** | Heavy Luau runtime lib; predictable C#-correct behavior |
| 2 | Semantic model source | **Task builds its own `CSharpCompilation`** from `@(Compile)` + `@(ReferencePathWithRefAssemblies)` | Full `SemanticModel`; P0 syntax-only transpiler retired |
| 3 | Object model | **Metatable + `__index` chain**; runtime `class(name, base)` helper | Low per-instance memory, single-inheritance vtable dispatch. Compile-time devirtualization is a later optimization |
| 4 | Structs | **Copy-insertion, Roblox natives exempt** | Transpiler inserts value copies at assign/pass/return for user structs; Vector3/CFrame/… pass by ref (immutable userdata) |
| 5 | Generics | **Erasure + selective reification** | One erased Luau class; hidden type-token threaded only when `default(T)`/`typeof(T)`/`new T()`/`is T` used |
| 6 | BCL surface | **Custom ref assembly (subset)** | Compiler itself rejects unimplemented `System.*`; IntelliSense honest; every member has a runtime mapping |
| 7 | Type delivery | **Prebuilt ref assemblies** (Roblox API + BCL), generated at SDK-build time | Fast consumer builds; P0 source-generator retired; rebuild SDK on API-dump update |
| 8 | Module model | **Per-file ModuleScript, native `require`**, namespace→folder, Rojo tree | Idiomatic, lazy, tree-shakeable; transpiler computes instance paths |
| 9 | Rojo/runtime | **rbxcs owns Rojo project + fixed runtime mount**, override allowed | Zero-config default; runtime lib at a known mount |
| 10 | Exec model | **Attributes** `[Server]`/`[Client]`/`[Shared]` + entry `[Script]`/`[LocalScript]` | Placement + entry kind co-located with code, refactor-safe |
| 11 | Numerics | **64-bit int types (`long`/`ulong`) → Luau `integer`, else f64** | `integer` is Luau's native 64-bit int type; 32-bit `int` wrap is a separate sub-gap |
| 12 | Async | **Promise-backed `Task`** | `Task`↔Promise; `await` suspends enclosing coroutine; Roblox yielders wrapped |
| 13 | Exceptions | **Faithful try/catch/finally** | throw→error(instance); typed catch dispatch by runtime type-check; finally on all paths; rethrow |
| 14 | Properties | **Auto-props = table fields; bodied props = accessor methods**; indexers → get_Item/set_Item | Zero overhead for auto-props; call-site rewrite for bodied |

### Revised project layout (supersedes P0 sketch above)

```
src/
  RobloxCS.Luau.Ast/     Luau AST node types only (Base/Statements/ControlFlow/Functions/Expressions/Operators/Literals), namespace RobloxCS.Luau, zero deps
  RobloxCS.Luau/         LuauWriter (AST -> Luau text); references RobloxCS.Luau.Ast
  RobloxCS.Transpiler/   SemanticModel-driven C#->Luau lowering
  RobloxCS.BuildTask/    builds Compilation, runs transpiler, emits Rojo tree + copies runtime + writes project.json
  RobloxCS.Runtime/      the Luau runtime lib (.luau): class helper, struct copy, Promise, exceptions, BCL impls
  RobloxCS.Defs/         C# reference defs consumer compiles against (context attributes + Roblox globals + BCL subset)
                         → P3 splits into generated Roblox-API ref .dll + BCL ref .dll
  RobloxCS.Sdk/          packaging + targets
samples/HelloWorld/      consumer
```

### Revised roadmap (faithful track)

- [x] **F0 — decisions locked** (this section)
- [x] **F1 — SemanticModel pivot**: task builds Compilation, transpiler consumes SemanticModel
- [x] **F2 — Faithful OOP slice**: class→metatable module, fields, auto-props, ctor+base, methods, inheritance, `new`, per-file require, Rojo tree, context attributes, runtime `class` helper
- [x] **F3 — Control flow + expressions breadth**: if/elseif/else, while, for (numeric + desugared), foreach, do-while, switch→if-chain, break/continue, ternary, unary, compound assign, `++`/`--`, string interpolation
- [x] **Bitwise operators → `bit32`**: `&`/`|`/`^`/`<<`/`>>`/`~` (and compound `&=`/`|=`/`^=`/`<<=`/`>>=`) lower to `bit32.band`/`bor`/`bxor`/`lshift`/`rshift`/`bnot` (Luau has no bitwise operators). Bool operands use logical `and`/`or`/`~=` instead. Compound forms desugar to `x = bit32.op(x, y)`. Ceiling: `bit32` is 32-bit **unsigned**, so results differ from C# signed `int` on complement/overflow. Verified: sample emits `bit32.*`, `luau-compile` clean, runs.
- [x] **Correctness pass 2** (semantic gaps): (a) **multi-variable declarations** — `int a=1, b, c=3;` now emits one `local` per declarator (`MultiStatement` node) instead of silently dropping all but the first; (b) **compound-assign to a bodied property** — `Prop += x` → `set_Prop(get_Prop() op x)` (honors string/bit32/idiv mappings; static + instance); (c) **keyword member/method/enum/class names** — user-declared members named as Luau keywords are `_`-suffixed at declaration + access, gated on user source (`UserId`) so Roblox/BCL/import names are untouched; (d) **`char`** — stays a 1-char string, but arithmetic/bitwise operands are wrapped in `string.byte` (C# char→int promotion) and `(int)ch`/`(char)n` casts convert via `string.byte`/`string.char` (ceiling: only ASCII exact — `string.byte` is the first UTF-8 byte). Also fixed **expression-bodied setters** (`set => _n = value`) which lowered the assignment as a value expression → `unsupported`; now shares statement lowering. Verified: `samples/HelloWorld/Shared/Gaps.cs`, all 24 files `luau-compile` clean, harness matches C# (`MultiDecl=6`, `Compound=30`, `Members=6`, `CharMath=157`).
- [x] **Lexical/semantics correctness pass** (guided by luau.org): (a) **Reserved-word identifiers** — locals, params, `for`/`foreach`/`catch`/pattern vars named as Luau keywords (`end`, `and`, `local`, `function`, …) are suffixed `_` consistently at declaration + use (`LuauId`); (b) **numeric literals** — C# suffixes (`f`/`d`/`m`/`u`/`l`) and digit-separator `_` stripped (Luau has one number type, no suffixes); (c) **string/char literals** — re-emitted from the parsed token value with Luau escaping, so verbatim `@"…"` and `\uXXXX` are handled; `char` → 1-char string; (d) **integer `/` and `%`** — unsigned operands use native Luau `//` and `%` (exact, ≥0); signed operands use `RBXCS.idiv`/`imod` (truncate toward zero, dividend-signed remainder) since Luau `//` floors toward -inf and `%` takes the divisor's sign; compound `/=`/`%=` likewise. `continue` is emitted natively. Verified: sample exercises all four; emits `//`/`idiv`/escaped idents/normalized literals, all 23 files `luau-compile` clean, behavior harness matches C# (`IntMath=-2`, `Continues=25`).
- [x] **F4 — Structs** copy-insertion: `struct()` runtime helper + `copy()` (recursive for nested structs); copy at assign/param-pass/return/field-init from a struct *place*; fresh values + primitives + Roblox natives exempt
- [x] **F5 — Exceptions** faithful dispatch: throw→error(instance), `RBXCS.try(body,catch,fin)` + `RBXCS.is` typed dispatch, finally on all paths, return-in-try via control markers, rethrow (`throw;`)
- [x] **F6 — Generics** erasure by default; method-level token threading (`__t_<T>`) for `default(T)`/`new T()`/`typeof(T)`/`is T`; concrete `is`/`default`/`typeof`; `RBXCS.default`/`RBXCS.new` runtime. Deferred: class-type-param reification, is-pattern binding
- [x] **F7 — Bodied properties + indexers**: get_X/set_X accessor methods (auto-props stay fields), indexers→get_Item/set_Item, call-site read/write rewrite, raw IndexAccess for arrays
- [x] **F8 — Promise Task / async-await**: `async T M()`→returns `RBXCS.async(closure)`, `await`→`RBXCS.await` (coroutine yield/resume), Promise runtime (resolve/reject/settle-queue). Deferred: WhenAll/WhenAny, Task.Delay (needs Roblox scheduler), cancellation
- [x] **F9 — BCL mappings + Luau impls (core)**: Math.*→math.*, string Length/ToUpper/ToLower/Substring→Luau string lib, `List<T>`→`RBXCS.List` (Add/Count/indexer/foreach via __iter), Console→print. Deferred: Dictionary/HashSet, full LINQ, ref-assembly surface *enforcement* (NoStdLib)
- [x] **F10 — Roblox API generator** (`RobloxCS.ApiGen`): API Dump JSON → C# ref declarations (class hierarchy + inheritance, properties, methods, enums; keyword-safe). Verified: generated C# compiles. Deferred: DataTypes (Vector3/CFrame section), events/callbacks, SDK-build wiring + ref-dll packaging with the official dump
- [x] **F11 — nullable, enums, interfaces, delegates/lambdas**: enums→tables, `??`/`?.`→if-expr, lambdas→function values, delegate/Func/Action invoke, interfaces erased (duck-typed dispatch). Deferred: `event`+=/-=, Nullable<T> HasValue/Value, is-pattern binding, cross-module enum require
- [x] **F12 — Analyzer + pack + node-lock fix**: `UnsupportedConstructAnalyzer` (RBXCS001: goto/lock/unsafe/fixed/stackalloc/pointer), full NuGet pack (build/tasks/runtime/analyzers/lib), MSBuild node-lock fixed via `TaskHostFactory` (verified: rebuild-after-sample no longer locks). Deferred: publish to nuget.org, `dotnet new` template

**All phases F0–F12 complete and verified in real Luau.**

### Post-F12 additions
- [x] **Task combinators**: `Task.WhenAll`→`RBXCS.whenAll`, `WhenAny`→`whenAny`, `FromResult`→`resolved`, `Delay`→`RBXCS.delay` (ms→s). Verified: WhenAll resolves to result array.
- [x] **Dictionary + LINQ**: `Dictionary<K,V>`→`RBXCS.Dictionary` (indexer/Add/ContainsKey/Remove/Count); LINQ `Where/Select/ToList/Count/Sum/First/Any/All`→`RBXCS.Linq.*` (chainable, return List). Verified. Also: C# 0-based arrays → Luau 1-based (`arr[i]`→`arr[i+1]`).
- [x] **DataTypes**: `RobloxCS.ApiGen` emits **all 43 Roblox datatype structs** (full engine list from create.roblox.com/docs/reference/engine/datatypes minus Instance/Enum/EnumItem/Enums which are classes/enum-system) — float32 components, `DateTime`, int16 variants, sequences, Random, Font, raycast params, etc. `DataTypeNames` auto-derived from the const. Transpiler maps native Roblox types → bare globals (`new Vector3(...)`→`Vector3.new(...)`, `DateTime.now()`, no require). Verified: generated C# compiles, `DateTime` no longer warns.

### Deferred items — all cleared (verified in real Luau)
- [x] **Dictionary `foreach`** (KeyValuePair Key/Value via `__iter`)
- [x] **HashSet<T>** (`RBXCS.HashSet`: Add/Contains/Remove/Count/iter)
- [x] **Nullable<T>** `HasValue`→`~= nil`, `Value`→passthrough
- [x] **is-pattern binding** `x is T v` → `RBXCS.is` + `local v = x` in the branch
- [x] **Lazy LINQ** — deferred; `where`/`select` build nothing until a terminal op or `foreach` (`__iter`)
- [x] **Events** — `event`→signal object, `+=`/`-=`→Connect/Disconnect, `.Invoke`→Fire
- [x] **Cross-module enums** — enums mapped in ModuleMap; resolve via `require` across files
- [x] **ref/in/out params** — no value-copy on aliasing args
- [x] **Value-type field zero-init** — uninitialized `int`/`bool` fields → `0`/`false`
- [x] **`continue` in non-canonical `for`** — guarded increment at loop top runs every iteration incl. after `continue`
- [x] **DataType operators** — native Luau operators (`v1 + v2`); Roblox provides the runtime
- [x] **`dotnet new` template** — `templates/rbxcs-game`

### Later additions
- [x] **Raw Luau file handling**: hand-written `.luau` files in the project are copied verbatim into the output tree at their project-relative path (build task `LuauContentFiles`, gathered by the SDK targets, excluding emitted output/obj/bin) — they mount alongside transpiled modules and interop both ways. Verified: `Shared/Handwritten.luau` → `out-luau/Shared/Handwritten.luau`.
- [x] **`rbxcs` CLI tool** (`RobloxCS.Cli`, packs as a dotnet tool, command `rbxcs`): `add-package [alias] [--output <dir>] [--namespace <ns>] [--packages <dir>] [--force]` reads `wally.toml` ([dependencies]/[dev-/server-dependencies]) and, for each dep, **generates a typed binding from the installed Luau source** — resolves `Packages/<Alias>.luau` (parsing the wally redirect to `_Index/<scope_name@ver>/<name>/`, or deriving it from the spec) and runs bindgen (`--wally` mode → `[WallyPackage]` entry + `Packages/<alias>/…` nested paths). Idempotent (skips existing unless `--force`); falls back to an empty stub with a "run `wally install`" note when a dep isn't installed. Verified: synthetic wally layout → typed `[WallyPackage("Signal")]` with real members; uninstalled dep → stub.
- [x] **Wally compatibility**: `[WallyPackage("alias")]` attribute (alias defaults to type name = wally.toml dependency alias) → binds to `<PackagesMount>.<alias>`, emitting `require(...Packages.<alias>)`. `ProjectDescriptor.PackagesMount` (default `ReplicatedStorage.Packages`) + `PackagesPath` (on-disk folder) drive the require path and add a `Packages` mount to the generated Rojo project so `wally install` output coexists with transpiled code. Wally types aren't transpiled (declarations only). Unified with `[LuauImport]` via `ImportModule`. Verified: `Signal.@new()`→`require(...Packages.Signal); Signal.new()`.
- [x] **Instance API completeness (dump-driven, no hardcoding)**: events (`MemberType: Event`, 531) → `RBXScriptSignal`/`RBXScriptSignal<T>` properties; **creatable ctors from dump tags** — `public` ctor for creatable classes, `protected` for `NotCreatable`/`Service` (so `new Part()` compiles, `new BasePart()` doesn't); transpiler maps `new <InstanceClass>()` → `Instance.new("ClassName")` via the hierarchy. Fixed `Hidden`-tag over-filtering (`Hidden` is still scriptable — only `NotScriptable` skips; `BasePart.Position` was wrongly dropped). `RaycastResult` modeled as class (nullable). Unqualified `Roblox.Globals` (workspace/game) → bare global.
- [x] **Realistic sample** (`Server/KillBrick.cs`): spawns an anchored kill-brick (`Instance.new`, property sets, `BrickColor.new`), kills touching Humanoids (`part.Touched:Connect`, `FindFirstChildOfClass`, cast, `Health = 0`), downward `workspace:Raycast`, Humanoid movement. Type-checks against the live 682-class API; emits idiomatic Roblox Luau; compiles.
- [x] **Third-party / Luau-library interop** (`[LuauImport]`): compiled NuGets are metadata-only and are NOT transpiled (no bodies). For Luau-backed libs, declare the surface in C# with `[LuauImport("Service.Path.Module")]` (+ optional `[LuauName]` per member); the transpiler emits `local M = require(<path>)` and `M.Member(...)` / `M.new(...)`. Import types are excluded from transpilation. Verified: `ZString.Format(...)`→`require(...).Format(...)`, `[LuauName]` rename applied.
- [x] **Unmapped-external detection (RBXCS100)**: transpiler collects a build warning (file:line) whenever a used symbol comes from an external assembly with no Luau path — not Roblox, not a mapped BCL type (allowlist), not `[LuauImport]`. Deduped per type; message names the type + suggests `[LuauImport]`. Verified: `System.TimeSpan` warns, supported types don't.
- [x] **`[LuauModule]` (mount-decoupled binding)**: for your own hand-written `.luau` files, `[LuauModule("Shared/Handwritten")]` takes a **folder-relative** path whose leading segment (`Shared`/`Server`/`Client`/`Packages`) is resolved through the mount config at build time (`ModuleMap.ContentModulePath`), so renaming a mount doesn't strand the binding (unlike a fixed `[LuauImport]` service path). Excluded from transpilation. Verified: `[LuauModule("Shared/Handwritten")]` with `SharedMount = ReplicatedStorage.Common` → `require(...Common.Handwritten)`.
- [x] **Luau→C# binding generator (`RobloxCS.Bindgen`, `rbxcs bind`)**: parses `.luau` with **Loretta** (`LuaSyntaxOptions.Luau`), reads the public surface off the returned table (`function M.x`, `M.x = lit`), follows an `init.luau` require chain one level (re-exports → nested static classes bound to their own file paths), and emits a typed `.g.cs` (`[LuauModule]`/`[WallyPackage]`, `[LuauName]` when the Luau name isn't a usable C# identifier). Type mapper: primitives→`double`/`string`/`bool`, `T?`→nullable, `{T}`→array, `{[K]:V}`→`Dictionary`, `(A)->R`→`Func`/`Action`, `...`→`params object[]`, curated Roblox names→`Roblox.*`, unknown→`object` (reported). `rbxcs bind <path> [--module <base>] [--namespace <ns>] [--out <file>] [--wally [alias]]`. Verified end-to-end: `Handwritten.luau`→byte-identical hand binding; a 3-file index package → nested `[LuauModule]` classes, sample compiles, emits `require(...Common.Pkg.MathX)`, all emitted files `luau-compile` clean.
- [x] **Metatable-OOP → C# class binding**: bindgen detects a class module (`M.__index = M`, `:` methods, or `M.new` returning `setmetatable`) and emits a real `public sealed class` instead of a static one — `M.new` → **C# constructor** (`new Signal(x)` → `require(...).new(x)`), other `.` fns → static factories, `:`/`self`-first methods → **instance methods** (transpiler already lowers these to `sig:Method(...)` colon-calls), `M.x = lit` → static field. Self-typed returns (`export type Signal`, `: Signal`) resolve to the class name (aliases), not `object`. `()`/`nil` returns → `void` (body `{ }`). No transpiler change needed. Also fixed null-forgiving `x!` (`SuppressNullableWarningExpression`) lowering (was `nil --[[unsupported]]`). Verified: Wally `Signal` class → `new Signal("evt")`→`Signal.new("evt")`, `sig:Connect`/`:Fire`/`:Destroy`, `Signal.wrap(...)`, `require(...Packages.Signal)`; sample compiles, all 21 files `luau-compile` clean.
- [x] **ProjectDescriptor** (configurable output layout, decision #9 override): user inherits `RobloxCS.ProjectDescriptor`, overrides `ProjectName`/`SharedMount`/`ServerMount`/`ClientMount` with string-literal getters. `ProjectConfig.Resolve` reads them via the semantic model; drives require paths, runtime mount, and the generated `default.project.json`. Descriptor classes are not transpiled. Verified: custom `ReplicatedStorage.Common` mount flows to requires + Rojo.
- [x] **yield-return iterators**: methods with `yield return`/`yield break` → `RBXCS.iterator(fn)` (lazy enumerable); consumable by LINQ and `foreach`. Verified: `Squares(n).Where(>0).Sum()`→14.
- [x] **ModuleEmitter refactor**: split 1154-line class into partial-class files — `ModuleEmitter.cs` (core/Emit), `.Types.cs`, `.Statements.cs`, `.Expressions.cs`, `.Names.cs`.
- [x] **Correctness pass 3** (`char` ++/--, `ToString`): (1) `ch++`/`ch--` on a `char` emitted `ch + 1` on a Luau string → runtime error; now `ch = string.char(string.byte(ch) ± 1)` (stays a 1-char string). Note: `ch += n` is a C# compile error (int→char not implicit), so only `++`/`--` are reachable. (2) parameterless `.ToString()` had **no** mapping → `x:ToString()` on any primitive (number/bool/char/string) errored at runtime; now `tostring(x)`. A user-overridden `ToString` has source refs → still a `recv:ToString()` call into the emitted method. Ceiling: `ToString(format)` (with args) still unmapped; `Equals`/`GetHashCode` on primitives likewise. Verified: sample `Gaps.CharShift`→`"B"`, `Gaps.NumStr`→`"42/true"`; all 25 emitted files `luau-compile` clean; runtime harness asserts both.
- [x] **Parallel Luau (Actors)**: no faithful `System.Threading.Thread` — Roblox actors are isolated, message-passing VMs, not shared-memory threads (analyzer flags `new Thread()` → RBXCS001, steers to the model below). Instead: `[Actor]` on an entry `[Script]`/`[LocalScript]` class → its runner mounts beneath a Rojo `Actor` instance (`init.meta.json {"className":"Actor"}`), so its thread runs in that Actor's Luau VM. `[Parallel]` on a method → body wrapped in `RBXCS.parallel`, which `pcall`-probes `task.desynchronize` (legal only in an Actor-VM thread) → runs parallel + resyncs, else serial fallback; correct on all exit paths (result/error captured, resync before rethrow). Data sharing via the already-typed `Actor:SendMessage`/`BindToMessage` + `SharedTable`. Verified: sample `Server/Worker.cs` emits Actor folder + `RBXCS.parallel`-wrapped body; runtime harness confirms serial fallback value, multi-return, rethrow; all emitted luau `luau-compile`s clean. Ceiling: real parallelism only on Roblox (CLI has no `task.desynchronize`, so verify exercises the serial path); no manual `task.desynchronize` C# binding yet.

- [x] **Correctness pass 4** (ceilings closed): (1) `ToString(format)` → `RBXCS.tostringf` (D/X/x/F specifiers + optional precision; else `tostring`). (2) `Equals(x)` → `(a == b)` (parenthesized — value/ref equality matches Luau `==`; may sit under `not`). (3) `GetHashCode()` → `RBXCS.hashCode` (value-based, run-stable; C# doesn't promise cross-run stability either). (4) `checked`/`unchecked` expressions + blocks now lower (overflow semantics erased — Luau is f64); the analyzer flags **`checked`** as RBXCS001 (no cheap overflow detection) but not `unchecked` (it *is* the erased default). All resolve only when the member has no user override (no source refs); an override falls through to a real method call. Verified: `Gaps.Formats`→`"FF|00255|3.14"`, `Gaps.Eq`→`true`, neg-pad `-007`, hash stable; 25 emitted files compile clean; harness asserts all.

- [x] **Interpolation format/alignment clauses**: `$"Test {x}"` already lowered to a native Luau backtick string (``` `Test {x}` ```), but the `{expr,align:format}` clauses were **dropped** — `$"{pi:F2}"`/`$"{n,4}"` came out unformatted. Now the format clause routes through `RBXCS.tostringf` and the alignment clause through `RBXCS.align` (pad to `|width|`, negative = left-justify), applied format-then-align per C#. Also fixed literal-text escaping to escape `\` first (was: `\t` in text → tab). Verified: `Gaps.Interp` → `"pi=3.14|   7|x  !"`; 25 emitted files compile clean.

- [x] **Parallel primitives (Phase 1)**: typed C# surface so hand-written multi-actor code is fully writable. `RobloxCS.ParallelLuau.Desynchronize()`/`Synchronize()` → `task.desynchronize`/`synchronize` (named `ParallelLuau` to avoid colliding with `System.Threading.Tasks.Parallel`). `SharedTable` ops as extension methods (`Roblox.SharedTableOps`): `Get`→`st[k]`, `Set`→`RBXCS.stset`, `Increment`/`Update`/`Size`/`Clear`→`SharedTable.*`. Added `script` global (`script:GetActor()`). `ParallelLuau.For(from,to,body)` → `RBXCS.pfor` (**sequential** — correct semantics; Phase 2 swaps the lowering to Actor fan-out, since a runtime closure can't cross a VM). Sample `Server/Pool.cs` (a `[Actor]` worker: desync → SharedTable reduce → sync → `For`). Verified: emits idiomatic Luau, all 26 files `luau-compile` clean, `pfor`/`stset` harness asserts range + set (SharedTable/task/Actor themselves run on Roblox only).

- [x] **Parallel.For fan-out (Phase 2)**: `ParallelLuau.For(from, to, body)` now compiles to a real Actor pool when the body is self-contained — it **captures only `SharedTable`s** (they cross a VM by reference) and pulls in no other module. The transpiler lifts the body into a synthetic `[Actor]` worker Script (`<Module>__pforN`, emitted as an extra `ModuleResult` via `ModuleEmitter.Synthetic`) that binds a parallel `run` handler over a `[lo,hi)` slice and signals a shared done-counter; the call site emits `RBXCS.parallelFor(<template Actor>, from, to, { caps })`, which clones one Actor per range (`RBXCS.__pforRanges`, pool≤16), dispatches via `SendMessage`, and joins. Anything else (non-SharedTable capture via `AnalyzeDataFlow`, or a body that requires another module) → **sequential `RBXCS.pfor` + RBXCS100 diagnostic** (safe: a runtime closure can't cross a VM). Verified: `Server/Pool.cs` emits the worker Actor folder + driver; fallback path warns + goes sequential; all 27 emitted files `luau-compile` clean; `__pforRanges` unit test asserts contiguous, gap-free, exact-split coverage. **Behavior-verified** (not just compiled): the *literal* generated worker + real `RBXCS.parallelFor`/`__pforRanges` run under a cooperative Roblox-parallel shim (Actor/SharedTable/task mocks) — `count==4` for `[1,5)` and `50` for `[0,50)` across multiple actor clones, exercising capture-passing, slicing, dispatch, reduce, and join. True multicore timing is still Roblox-only, but the generated logic is executed, not assumed.
- [x] **Parallel-safety checking (API-dump ThreadSafety tags)**: `RobloxCS.ApiGen --threadsafety <dump> <out>` emits `RobloxThreadSafety.g.cs` (compiled into the transpiler) from the dump's `ThreadSafety` field — `Unsafe` (illegal in the parallel phase) and `ReadSafe` (readable, not writable), keyed `Class.Member`. The transpiler runs `CheckParallelSafety` over every parallel-dispatched body (`[Parallel]` methods **and** `ParallelLuau.For` fan-out lambdas): a use of an `Unsafe` member, or a *write* to a `ReadSafe` member, emits an RBXCS100 diagnostic with `file:line`. Verified: `Pool.Inspect` — `p.Name` read (ReadSafe) is silent, `p.Anchored = true` (ReadSafe write) and `p.Destroy()` (Unsafe) are flagged. Regenerate the table when the dump updates (same command).

- [x] **Dump-derived `.g.cs` generated at build, not committed**: `RobloxApi.g.cs` (Defs, ~12k lines) and `RobloxThreadSafety.g.cs` (Transpiler, 4.6k) are no longer tracked source — a `BeforeTargets="CoreCompile"` target Execs the built `RobloxCS.ApiGen` tool (`--fetch` / `--threadsafety --fetch`) into `$(IntermediateOutputPath)` (obj/, gitignored) and adds it to `@(Compile)`. The dump is fetched from Roblox's CDN once; an `Exists()` guard skips the network on incremental builds (delete obj to refresh). Path is computed inside the target because `$(IntermediateOutputPath)` isn't set until the SDK targets import. Each project takes a build-order `ProjectReference` on ApiGen (`ReferenceOutputAssembly=false`). Verified: Defs emits 11868 lines + Transpiler 4606 entries into obj; full solution builds; parallel-safety check still fires; all 28 emitted sample files compile.

- [x] **Consumer bindings generated at build (`BindTask`), not committed**: `GenerateBindingsTask` (MSBuild, TaskHostFactory, references `RobloxCS.Bindgen`) runs `BeforeTargets="CoreCompile"` over `@(RobloxCSBinding)` items — each names a Luau/Wally package with `Namespace`/`Wally`/`Alias`/`ModuleBase` metadata — and writes typed bindings into `$(IntermediateOutputPath)rbxcs-bindings/*.g.cs` (obj/, gitignored), fed straight to `@(Compile)`. Committed `Pkg.g.cs`/`Signal.g.cs` removed; `Pkg` regenerates from `Shared/Pkg/*.luau`, and a new `Shared/Packages/Signal.luau` fixture (metatable-OOP) regenerates the `[WallyPackage]` `Signal` class. Verified: sample emits `Vendor.Pkg` + `Demo.Signal` bindings into obj, `Wally.cs`/`BindTest.cs` compile against them, all 29 emitted `.luau` compile clean.

- [x] **MSB3021/MSB3027 task-lock fixed**: the consumer loaded the MSBuild task straight from `RobloxCS.BuildTask/bin`, so a lingering TaskHost held that exact dll and blocked rebuilding the task project. Now `RobloxCSCopyTasks` copies the task assembly + deps into the consumer's `obj/rbxcs-tasks/` (absolute path via `[MSBuild]::NormalizeDirectory`, `SkipUnchangedFiles`) and the `UsingTask`s load from that private copy — the source `bin` is never held by consumer builds. Dev wiring switched from `RobloxCSTaskAssembly` (dll) to `RobloxCSTaskDir` (folder). Verified: 3 back-to-back sample-build → task-rebuild cycles are lock-free; full solution builds; sample still emits 29 modules.

- [x] **Docs site (`website/`, Docusaurus) — generated from source**: XML doc comments (`///`) on the curated public `RobloxCS.Defs` surface (attributes, `ParallelLuau`, `SharedTableOps`, interop, `ProjectDescriptor`) + `<GenerateDocumentationFile>` (CS1591 suppressed for the generated Roblox API). `scripts/gen-api-docs.mjs` runs **DefaultDocumentation** over `Defs.dll`+`.xml` → markdown, keeping only the **type-level** rbxcs-specific pages (16 — members are tabulated inline; drops per-member pages + the ~5700 Roblox API stubs; dead intra-links to dropped pages are stripped to text). `scripts/gen-examples.mjs` pairs each sample `.cs` with its transpiled `.luau` into 21 C#/Luau tabbed pages. Two seed **tutorial blog** posts authored from the ceilings (no-threads/Actors, 64-bit). Docusaurus (Node, **pnpm**) with docs + first-class blog; generated `docs/api` + `docs/examples` are gitignored. Verified: `pnpm install` + `docusaurus build` succeeds — 44 API + 21 example + blog pages; remaining warnings are cross-links to the intentionally-excluded Roblox API types. Policy: `///` on the public surface is a documented carve-out from the no-comments rule (CLAUDE.md).

**64-bit integers — deliberate ceiling (not fixed).** Luau `number` is IEEE f64: exact to 2^53, and Luau has **no** native i64 (`buffer`/`vector`/`bit32` are 32-bit or float; there is no cheap widening). A faithful `long`/`ulong` would require a software int64 (two-word add/sub/mul/div/mod/compare + bit ops with carry, literal parsing, and threading through every arithmetic site) — a large runtime subsystem and a per-op perf cost, for near-zero real-world benefit: Roblox positions are f32/f64, UserIds currently fit under 2^53, and DataStore/large ids arrive as strings. Decision: keep f64 semantics, document the 2^53 boundary; revisit only if a concrete need for exact >2^53 arithmetic appears.

**Still genuinely external / minor:** publish to nuget.org (needs an account); `Task.Delay` real timing (works on Roblox via `task.delay`, not verifiable off-Roblox); LINQ `First`/`Any` early-break (behaviorally correct, minor perf); `out var` multi-return binding; `ToString(format)` beyond D/X/F specifiers.
