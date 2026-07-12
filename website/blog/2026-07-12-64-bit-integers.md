---
slug: 64-bit-integers
title: "long doesn't fit: 64-bit integers in rbxcs"
authors: [rbxcs]
tags: [numbers, gotcha, ceiling]
date: 2026-07-12
---

`long` and `ulong` compile, and small values behave. Past 2^53 they quietly drift. Here's the why and
the how-to-avoid.

<!-- truncate -->

## The one-line reason

Luau's `number` is an IEEE 754 double. It represents every integer **exactly up to 2^53**
(9,007,199,254,740,992) and no further. Luau has no native 64-bit integer type — `bit32` is 32-bit,
`buffer` reads fixed widths but does no 64-bit arithmetic, and `vector` is floats. There is no cheap
widening.

So `long x = 9007199254740993; x + 1` does not do what C# does. The literal already can't be held
exactly.

## Why rbxcs doesn't "fix" it

A faithful `long` would need a **software int64**: two 32-bit words with carry across add / sub / mul /
div / mod / compare / bit-ops, plus literal parsing, threaded through every arithmetic site. That's a
large runtime subsystem and a per-operation cost — for close to zero real-world benefit on Roblox:

- positions and sizes are `f32`/`f64`;
- `UserId`s currently fit under 2^53;
- DataStore keys and other genuinely-large ids arrive as **strings**.

So rbxcs keeps `f64` semantics and documents the boundary, rather than shipping a bignum nobody asked
to pay for.

## What to do instead

- **Stay under 2^53.** Almost all game math does. Counters, indices, currency in whole units — fine.
- **Large ids: keep them strings.** If a value is an identifier, not a number you do arithmetic on,
  type it as `string`. DataStore and web APIs already hand them to you that way.
- **Need exact >2^53 arithmetic?** That's the rare case. Split into two 32-bit halves yourself, or model
  the value as a string and operate on it deliberately. If this becomes common for real projects, the
  software-int64 path is the documented upgrade — open an issue.

## Related edges

The same "faithful, not magic" rule shows up elsewhere: `checked` overflow detection isn't supported
(the analyzer flags `checked`; `unchecked` is the erased default), and `ToString("X")`-style format
specifiers map to `string.format` but only cover the common `D`/`X`/`F` set. When a number surprises
you, assume `f64` and reach for the [API reference](/docs/api) or these tutorials.
