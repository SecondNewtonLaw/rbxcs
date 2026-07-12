--!strict
local MathX = {}
MathX.PI = 3.14159
MathX.name = "mathx"
function MathX.add(a: number, b: number): number
    return a + b
end
function MathX.clamp(v: number, lo: number?, hi: number?): number
    return v
end
function MathX.sum(...): number
    return 0
end
return MathX
