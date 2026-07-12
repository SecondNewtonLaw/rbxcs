--!strict
-- rbxcs runtime library. Shipped in the SDK, mounted at ReplicatedStorage.rbxcs.runtime.
-- Every transpiled module `require`s this. Faithful C# semantics live here.

local RBXCS = {}

-- Class model: metatable + __index chain (single inheritance + virtual dispatch).
-- `class(name, base)` returns a class table; instances are tables with the class as metatable.
-- Methods are shared on the class table (low per-instance memory). Virtual dispatch = __index lookup.
export type Class = {
	__index: any,
	__className: string,
	new: (...any) -> any,
	[any]: any,
}

function RBXCS.class(name: string, base: Class?): Class
	local c = {} :: any
	c.__index = c
	c.__className = name

	if base ~= nil then
		-- inherit statics/methods not overridden on c
		setmetatable(c, { __index = base })
	end

	function c.new(...): any
		local self = setmetatable({}, c)
		if self.__ctor ~= nil then
			self:__ctor(...)
		end
		return self
	end

	-- C# `x is T` / typeof support (faithful): walk the metatable chain by class identity.
	function c.__is(instance: any): boolean
		local mt = getmetatable(instance)
		while mt ~= nil do
			if mt == c then
				return true
			end
			local parent = getmetatable(mt)
			mt = parent ~= nil and parent.__index or nil
		end
		return false
	end

	return c
end

-- Value type (C# struct). Same metatable model as class, marked __isStruct so copy() recurses
-- into struct-typed fields. Structs have no inheritance.
function RBXCS.struct(name: string): Class
	local c = RBXCS.class(name, nil)
	c.__isStruct = true
	return c
end

-- default(T) for a reified type token. Tokens: "number" (0), "boolean" (false), a struct class
-- table (zeroed instance), or anything else -> nil (ref types / string).
function RBXCS.default(token: any): any
	if token == "number" then
		return 0
	elseif token == "boolean" then
		return false
	elseif type(token) == "table" and token.__isStruct then
		return token.new()
	end
	return nil
end

-- new T() via a reified token: class/struct tokens construct; primitive tokens give their default.
function RBXCS.new(token: any, ...: any): any
	if type(token) == "table" then
		return token.new(...)
	end
	return RBXCS.default(token)
end

-- C# `x is T` / typed-catch dispatch: walk the metatable chain by class identity.
function RBXCS.is(instance: any, cls: any): boolean
	if type(instance) ~= "table" then
		return false
	end
	local mt = getmetatable(instance)
	while mt ~= nil do
		if mt == cls then
			return true
		end
		local super = getmetatable(mt)
		mt = super ~= nil and super.__index or nil
	end
	return false
end

-- Faithful try/catch/finally. body/catch return a control marker:
--   { kind = "ret", values = {...} }  -> a C# `return` happened; propagate out of the method
--   { kind = "handled" }              -> caught and swallowed; continue after the try
--   nil (from catch)                  -> no clause matched; rethrow
-- finally runs on every path (normal, caught, uncaught, return-in-try) before propagation.
function RBXCS.try(body: () -> any, catch: ((any) -> any)?, fin: (() -> ())?): any
	local function run()
		local ok, res = pcall(body)
		if ok then
			return res
		end
		if catch == nil then
			error(res)
		end
		local c = catch(res)
		if c == nil then
			error(res)
		end
		return c
	end
	local ok, res = pcall(run)
	if fin ~= nil then
		fin()
	end
	if not ok then
		error(res)
	end
	if res ~= nil and res.kind == "ret" then
		return res
	end
	return nil
end

-- Value-semantics copy for structs: fresh table, same metatable, fields copied; nested struct
-- fields cloned recursively (C# copies value types deeply). Primitives/refs copied as-is.
function RBXCS.copy(v: any): any
	if type(v) ~= "table" then
		return v
	end
	local mt = getmetatable(v)
	local c = setmetatable({}, mt)
	for k, val in pairs(v) do
		if type(val) == "table" then
			local vmt = getmetatable(val)
			c[k] = (vmt ~= nil and vmt.__isStruct) and RBXCS.copy(val) or val
		else
			c[k] = val
		end
	end
	return c
end

-- System.Collections.Generic.List<T> (F9). 0-based indexing mapped to Luau 1-based internally.
RBXCS.List = RBXCS.class("List", nil)
function RBXCS.List:__ctor()
	self._items = {}
end
function RBXCS.List:Add(v: any)
	table.insert(self._items, v)
end
function RBXCS.List:Count(): number
	return #self._items
end
function RBXCS.List:get_Item(i: number): any
	return self._items[i + 1]
end
function RBXCS.List:set_Item(i: number, v: any)
	self._items[i + 1] = v
end
function RBXCS.List:__iter()
	local items = self._items
	local i = 0
	return function()
		i += 1
		if items[i] ~= nil then
			return i - 1, items[i]
		end
		return nil
	end
end

-- C# Task<T> <-> Promise (decision #12). Minimal coroutine-driven promise: async runs its body in
-- a coroutine, await yields the awaited promise, the driver resumes when it settles.
type Promise = { state: string, value: any, queue: { () -> () } }

function RBXCS.promise(executor: (((any) -> ()), ((any) -> ())) -> ()): Promise
	local p: Promise = { state = "pending", value = nil, queue = {} }
	local function settle(state: string, value: any)
		if p.state ~= "pending" then
			return
		end
		p.state = state
		p.value = value
		for _, cb in p.queue do
			cb()
		end
		p.queue = {}
	end
	local ok, err = pcall(executor, function(v)
		settle("resolved", v)
	end, function(e)
		settle("rejected", e)
	end)
	if not ok then
		settle("rejected", err)
	end
	return p
end

function RBXCS.resolved(v: any): Promise
	return RBXCS.promise(function(res)
		res(v)
	end)
end

local function onSettled(p: Promise, cb: () -> ())
	if p.state ~= "pending" then
		cb()
	else
		table.insert(p.queue, cb)
	end
end

function RBXCS.async(fn: (...any) -> any, ...: any): Promise
	local packed = table.pack(...)
	return RBXCS.promise(function(resolve, reject)
		local co: thread
		local function step(...)
			local ok, res = coroutine.resume(co, ...)
			if not ok then
				reject(res)
			elseif coroutine.status(co) == "dead" then
				resolve(res)
			else
				onSettled(res, function()
					step(res.state == "resolved", res.value)
				end)
			end
		end
		co = coroutine.create(fn)
		step(table.unpack(packed, 1, packed.n))
	end)
end

function RBXCS.await(p: any): any
	if type(p) ~= "table" or (p :: any).state == nil then
		return p
	end
	local ok, value = coroutine.yield(p)
	if not ok then
		error(value)
	end
	return value
end

-- Task combinators.
function RBXCS.whenAll(promises: { Promise }): Promise
	return RBXCS.promise(function(resolve, reject)
		local results = {}
		local remaining = #promises
		if remaining == 0 then
			resolve(results)
			return
		end
		for i, p in promises do
			onSettled(p, function()
				if p.state == "rejected" then
					reject(p.value)
					return
				end
				results[i] = p.value
				remaining -= 1
				if remaining == 0 then
					resolve(results)
				end
			end)
		end
	end)
end

function RBXCS.whenAny(promises: { Promise }): Promise
	return RBXCS.promise(function(resolve, reject)
		for _, p in promises do
			onSettled(p, function()
				if p.state == "resolved" then
					resolve(p.value)
				else
					reject(p.value)
				end
			end)
		end
	end)
end

-- Task.Delay(seconds) -> resolves after the delay via the Roblox scheduler (immediate off-Roblox).
function RBXCS.delay(seconds: number): Promise
	return RBXCS.promise(function(resolve)
		if task ~= nil and (task :: any).delay ~= nil then
			(task :: any).delay(seconds, resolve)
		else
			resolve(nil)
		end
	end)
end

-- System.Collections.Generic.Dictionary<K,V> (F-linq).
RBXCS.Dictionary = RBXCS.class("Dictionary", nil)
function RBXCS.Dictionary:__ctor()
	self._map = {}
	self._count = 0
end
function RBXCS.Dictionary:set_Item(k: any, v: any)
	if self._map[k] == nil and v ~= nil then
		self._count += 1
	elseif self._map[k] ~= nil and v == nil then
		self._count -= 1
	end
	self._map[k] = v
end
function RBXCS.Dictionary:get_Item(k: any): any
	return self._map[k]
end
function RBXCS.Dictionary:Add(k: any, v: any)
	self:set_Item(k, v)
end
function RBXCS.Dictionary:ContainsKey(k: any): boolean
	return self._map[k] ~= nil
end
function RBXCS.Dictionary:Remove(k: any): boolean
	if self._map[k] ~= nil then
		self._map[k] = nil
		self._count -= 1
		return true
	end
	return false
end
function RBXCS.Dictionary:Count(): number
	return self._count
end
-- foreach yields KeyValuePair-shaped { Key, Value }.
function RBXCS.Dictionary:__iter()
	local map = self._map
	local k = nil
	return function()
		local nk, nv = next(map, k)
		k = nk
		if nk == nil then
			return nil
		end
		return nk, { Key = nk, Value = nv }
	end
end

-- System.Collections.Generic.HashSet<T>.
RBXCS.HashSet = RBXCS.class("HashSet", nil)
function RBXCS.HashSet:__ctor()
	self._set = {}
	self._count = 0
end
function RBXCS.HashSet:Add(v: any): boolean
	if self._set[v] then
		return false
	end
	self._set[v] = true
	self._count += 1
	return true
end
function RBXCS.HashSet:Contains(v: any): boolean
	return self._set[v] == true
end
function RBXCS.HashSet:Remove(v: any): boolean
	if self._set[v] then
		self._set[v] = nil
		self._count -= 1
		return true
	end
	return false
end
function RBXCS.HashSet:Count(): number
	return self._count
end
function RBXCS.HashSet:__iter()
	local k = nil
	return function()
		local nk = next(self._set, k)
		k = nk
		return nk, nk
	end
end

-- Event/signal (C# event). Connect/Disconnect handlers, Fire raises them.
RBXCS.Signal = RBXCS.class("Signal", nil)
function RBXCS.Signal:__ctor()
	self._handlers = {}
end
function RBXCS.Signal:Connect(handler: (...any) -> ())
	table.insert(self._handlers, handler)
end
function RBXCS.Signal:Disconnect(handler: (...any) -> ())
	for i, h in self._handlers do
		if h == handler then
			table.remove(self._handlers, i)
			return
		end
	end
end
function RBXCS.Signal:Fire(...: any)
	for _, h in table.clone(self._handlers) do
		h(...)
	end
end
function RBXCS.signal(): any
	return RBXCS.Signal.new()
end

-- LINQ (System.Linq.Enumerable subset). Deferred/lazy: where/select build no collection; the chain
-- only runs when a terminal op (ToList/Count/Sum/First/Any/All) or foreach forces it. A lazy query is
-- a table with _each(fn) plus an __iter so `for x in query` works.
RBXCS.Linq = {}

local function forEach(src: any, fn: (any) -> ())
	if type(src) == "table" and src._each ~= nil then
		src._each(fn)
	elseif type(src) == "table" and src._items ~= nil then
		for _, v in src._items do
			fn(v)
		end
	else
		for _, v in src do
			fn(v)
		end
	end
end

local function makeLazy(each: (fn: (any) -> ()) -> ()): any
	return setmetatable({ _each = each }, {
		__iter = function(self)
			local co = coroutine.create(function()
				self._each(function(v)
					coroutine.yield(v)
				end)
			end)
			local i = 0
			return function()
				local ok, v = coroutine.resume(co)
				if not ok or coroutine.status(co) == "dead" and v == nil then
					return nil
				end
				i += 1
				return i, v
			end
		end,
	})
end

-- C# iterator method (`yield return`). gen(yield) drives element production lazily; the result is a
-- deferred enumerable usable by LINQ / foreach (same shape as a lazy query).
function RBXCS.iterator(gen: (yield: (any) -> ()) -> ()): any
	return makeLazy(gen)
end

function RBXCS.Linq.where(src: any, pred: (any) -> boolean): any
	return makeLazy(function(fn)
		forEach(src, function(v)
			if pred(v) then
				fn(v)
			end
		end)
	end)
end
function RBXCS.Linq.select(src: any, sel: (any) -> any): any
	return makeLazy(function(fn)
		forEach(src, function(v)
			fn(sel(v))
		end)
	end)
end
function RBXCS.Linq.toList(src: any): any
	local out = RBXCS.List.new()
	forEach(src, function(v)
		out:Add(v)
	end)
	return out
end
function RBXCS.Linq.count(src: any): number
	local n = 0
	forEach(src, function()
		n += 1
	end)
	return n
end
function RBXCS.Linq.sum(src: any): number
	local total = 0
	forEach(src, function(v)
		total += v
	end)
	return total
end
function RBXCS.Linq.first(src: any): any
	local result = nil
	local found = false
	forEach(src, function(v)
		if not found then
			result = v
			found = true
		end
	end)
	return result
end
function RBXCS.Linq.any(src: any, pred: ((any) -> boolean)?): boolean
	local found = false
	forEach(src, function(v)
		if not found and (pred == nil or pred(v)) then
			found = true
		end
	end)
	return found
end
function RBXCS.Linq.all(src: any, pred: (any) -> boolean): boolean
	local ok = true
	forEach(src, function(v)
		if not pred(v) then
			ok = false
		end
	end)
	return ok
end

return RBXCS
