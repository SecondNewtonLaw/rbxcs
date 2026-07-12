using RobloxCS;
using static Roblox.Globals;

namespace Game;

// [Actor] -> the runner Script mounts beneath a Roblox Actor, so its thread runs in an isolated Luau
// VM. [Parallel] -> Compute runs desynchronized there (serial fallback off-actor). Pure compute only:
// the parallel phase must not touch the DataModel.
[Server]
[Script]
[Actor]
public static class Worker
{
    [Parallel]
    public static long SumSquares(int n)
    {
        long total = 0;
        for (int i = 1; i <= n; i++)
            total += i * i;
        return total;
    }

    public static void Main()
    {
        print(SumSquares(100)); // 338350
    }
}
