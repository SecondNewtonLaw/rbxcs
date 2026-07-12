--!strict
-- Metatable-OOP package (Wally-style), bound to a C# class by rbxcs bindgen at build time.
local Signal = {}
Signal.__index = Signal

export type Signal = typeof(setmetatable({} :: { _name: string }, Signal))

function Signal.new(name: string): Signal
	return setmetatable({ _name = name }, Signal)
end

function Signal.wrap(rbx: RBXScriptSignal): Signal
	return setmetatable({ _name = "wrapped" }, Signal)
end

function Signal.Fire(self: Signal, ...: any) end

function Signal.Connect(self: Signal, fn: (any) -> ()): RBXScriptConnection
	return nil :: any
end

function Signal.Destroy(self: Signal) end

return Signal
