--!strict
-- Hand-written Luau module. Copied verbatim into the tree, mounts alongside transpiled modules,
-- and is requirable from C# via [LuauModule("Shared/Handwritten")] (mount-config resolved).
local Handwritten = {}

function Handwritten.greet(name: string): string
	return "hello " .. name
end

return Handwritten
