---
slug: no-threads-on-roblox
title: "No threads on Roblox: doing parallelism the faithful way"
authors: [rbxcs]
tags: [parallel, actors, gotcha]
date: 2026-07-12
---

You wrote `new Thread(() => Work())` and rbxcs refused. That's on purpose. Here's why, and what to do
instead.

<!-- truncate -->

## Why `System.Threading.Thread` can't work

Roblox has no shared-memory threads. Its unit of parallelism is the **Actor**: an isolated Luau VM.
Two facts kill a faithful `Thread`:

1. **Closures can't cross a VM.** A `Thread`'s delegate captures variables by reference. Actor VMs
   share no Luau object references — only `SharedTable`-serializable values cross. So the lambda you'd
   hand a thread literally cannot run "over there" with its captures intact.
2. **No runtime code generation.** `loadstring` is off by default and server-only; `require` is banned
   in the parallel phase. Worker code must exist at *build* time, not be shipped at runtime.

So rbxcs flags `new Thread(...)` at compile time (`RBXCS001`) rather than emit something that breaks on
Roblox.

## The model that does work

Two attributes and one primitive:

- **`[Actor]`** on an entry `[Script]`/`[LocalScript]` class runs it beneath a Roblox `Actor`, so its
  thread lives in that Actor's VM — the only place `task.desynchronize` is legal.
- **`[Parallel]`** on a method runs its body in the parallel phase, with an automatic serial fallback
  when the thread isn't under an Actor.
- **`ParallelLuau.For(from, to, i => ...)`** fans a loop across a pool of Actors — *when the body is
  self-contained* (captures only `SharedTable`s, requires no other module). Otherwise it runs a correct
  sequential loop and tells you why.

```csharp
[Server, Script, Actor]
public static class Worker
{
    public static void Main()
    {
        var totals = new SharedTable();
        ParallelLuau.For(0, 1000, i => totals.Increment("sum", i)); // real Actor fan-out
        print(totals.Get("sum"));
    }
}
```

## The parallel phase can't touch the DataModel

Inside a `[Parallel]` body or a `Parallel.For` lambda, most Roblox members are **Unsafe**. rbxcs checks
every member you use against the API dump's `ThreadSafety` tags and flags violations at compile time:

```csharp
[Parallel]
void Bad(Part p)
{
    var name = p.Name;   // ok: ReadSafe, reading is fine
    p.Anchored = true;   // flagged: ReadSafe, writing in parallel is illegal
    p.Destroy();         // flagged: Unsafe in the parallel phase
}
```

Do reads and math in parallel; do DataModel writes back on the serial phase (after `Synchronize()`, or
outside the parallel body). Share results through a `SharedTable` — the one thing that crosses a VM by
reference.

## Takeaway

There is no `Thread`, and that's not a gap — it's the platform. `[Actor]` + `[Parallel]` +
`ParallelLuau.For` are the faithful mapping, and the compiler keeps you inside the rules.
