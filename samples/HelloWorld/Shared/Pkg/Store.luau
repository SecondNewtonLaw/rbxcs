--!strict
local Store = {}
function Store.get(key: string): number?
    return nil
end
function Store.keys(): {string}
    return {}
end
function Store.map(): {[string]: number}
    return {}
end
function Store.on(cb: (string)->()): RBXScriptConnection
    return nil :: any
end
function Store.custom(x: MysteryType): MysteryType
    return x
end
return Store
