---
slug: /
sidebar_position: 1
title: Introduction
---

# rbxcs

**C# → Luau for Roblox.** Write typed C# with IntelliSense; `dotnet build` emits idiomatic `.luau`
into a Rojo tree. Faithful C# semantics, zero manual setup — it ships as one NuGet.

## What this site is

Everything here is generated **from the source**:

- **[API Reference](/docs/api)** — the rbxcs-specific C# surface (attributes, parallel primitives,
  interop, project config), generated from the XML doc comments.
- **[Examples](/docs/examples)** — real sample C# next to the exact Luau rbxcs emits for it, straight
  from the transpiler output.
- **[Tutorials](/blog)** — deep dives on the sharp edges: what rbxcs supports, what it does not, and
  why. Start there if a feature surprised you.

## The 30-second model

```csharp
using RobloxCS;
using static Roblox.Globals;

[Server]
[Script]
public static class Bootstrap
{
    public static void Main() => print("hello from C#");
}
```

`[Server]`/`[Client]`/`[Shared]` choose the DataModel mount; `[Script]` makes it an executable
`Script` running `Main()`. C# OOP lowers to Luau metatables; the BCL subset maps to Luau natives.

## Read the tutorials for the edges

rbxcs is *faithful*, not magic. Roblox has no shared-memory threads, no 64-bit integers, and a
parallel phase that can't touch the DataModel. The [tutorials](/blog) explain each limit and the
idiomatic way around it.
