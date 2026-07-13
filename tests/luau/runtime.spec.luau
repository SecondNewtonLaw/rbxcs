--!nocheck
-- Behavior tests for the RBXCS Luau runtime. Run with the luau CLI:
--   luau tests/luau/runtime.spec.luau
-- Only the platform-independent surface is covered here (Actor/SharedTable/task need real Roblox);
-- the C#->Luau lowering itself is covered by the xUnit suite.
local RBXCS = require("../../src/RobloxCS.Runtime/runtime/RBXCS")

local function eq(a, b, msg)
	assert(a == b, `{msg}: got {tostring(a)} want {tostring(b)}`)
end

-- Integer division: truncate toward zero; remainder takes the dividend's sign (C# semantics).
eq(RBXCS.idiv(-7, 2), -3, "idiv -7/2")
eq(RBXCS.imod(-7, 2), -1, "imod -7%2")
eq(RBXCS.idiv(7, -2), -3, "idiv 7/-2")
eq(RBXCS.imod(7, -2), 1, "imod 7%-2")

-- Parallel.For range split: contiguous, gap-free, covers [from, to).
local ranges = RBXCS.__pforRanges(0, 10, 4)
eq(#ranges, 4, "ranges count")
eq(ranges[1][1], 0, "first lo")
eq(ranges[4][2], 10, "last hi")
local prev, total = 0, 0
for _, seg in ranges do
	eq(seg[1], prev, "contiguous")
	prev = seg[2]
	total += seg[2] - seg[1]
end
eq(total, 10, "covers all")

-- ToString(format) specifiers.
eq(RBXCS.tostringf(255, "X"), "FF", "hex")
eq(RBXCS.tostringf(-7, "D3"), "-007", "zero-pad")
eq(RBXCS.tostringf(3.14159, "F2"), "3.14", "fixed")

-- GetHashCode + interpolation alignment.
eq(RBXCS.hashCode(5), 5, "hash number")
eq(RBXCS.hashCode("abc"), RBXCS.hashCode("abc"), "hash stable")
eq(RBXCS.align(7, 4), "   7", "align right")
eq(RBXCS.align("x", -3), "x  ", "align left")

-- Parallel.For sequential fallback + [Parallel] serial fallback (no Actor VM present).
local n = 0
RBXCS.pfor(1, 5, function()
	n += 1
end)
eq(n, 4, "pfor iterations")
eq(RBXCS.parallel(function()
	return 42
end), 42, "parallel serial fallback")

print("RUNTIME SPEC OK")
