# rbxcs

This is a rewrite of an old project I participated with, a C# -> Luau transpiler meant to be used in Roblox.

The project was originally owned by old time friend @R-unic—he later stopped development to focus on his newer, fancier project, a DSL called [Loom](https://github.com/R-unic/loom/).

I long ago had plans to rewrite roblox-cs completely just for the fun of it, mainly to:
- Simplify initial setup (robloxcs required you to use a separate exe and do a helluva lot of work to set up)
- Allow automatic type bindings
- Not require a new program individually, just be part of the building process of the program
- and whatever i thought of on the go.

To do this, I already had a mock idea of it, so taking advantage of Claude's latest reset I decided to just Claude it all away, the result is this, made in less than 24hrs.

Pure slop code I will later have to review and actually check for correctness in the future.

This is **not** production ready, you may experiment with it, but for now it is not ready for a full blown release.

I will slowly but surely etch away to make it into a prod-worthy version of itself.

The point of this project is to be extremely ambitious for absolutely no reason:
- **FULL** Wally support
- **FULL** auto binding gen
- **FULL** C# API support (where possible)
- **FULL** code generation
- **FULL** LINQ support (best feature)

Reality is, uhh, halfway there.

~SecondNewtonLaw.