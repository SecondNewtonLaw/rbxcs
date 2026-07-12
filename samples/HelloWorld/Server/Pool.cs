using RobloxCS;
using Roblox;
using static Roblox.Globals;

namespace Game;

// Manual parallel worker under an Actor VM. Demonstrates the primitives: desynchronize into the
// parallel phase, reduce into a SharedTable (the only value shared by reference across Actor VMs),
// synchronize back. Runs on Roblox only (the luau CLI has no task.desynchronize/SharedTable/Actor);
// off-actor, Desynchronize raises — this file is meant to run beneath the emitted Actor instance.
[Server]
[Script]
[Actor]
public static class Pool
{
    public static void Main()
    {
        var totals = new SharedTable();

        // parallel phase: heavy compute only, no DataModel writes
        ParallelLuau.Desynchronize();
        double sum = 0;
        for (int i = 1; i <= 100; i++)
            sum += i * i;
        totals.Increment("sum", sum);
        ParallelLuau.Synchronize();

        // ParallelLuau.For fans the body out (Phase 1: sequential; same result, upgraded to Actors later).
        ParallelLuau.For(1, 5, i => totals.Increment("count", 1));

        print(totals.Get("sum"), totals.Get("count")); // 338350  4
    }

    // Parallel-safety check: reads are fine in parallel; the Unsafe call + read-only write are flagged
    // at compile time (RobloxThreadSafety, from the API dump's ThreadSafety tags).
    [Parallel]
    public static void Inspect(Part p)
    {
        if (p.Name == "")   // Instance.Name is ReadSafe -> read OK, no warning
            return;
        p.Anchored = true;  // BasePart.Anchored is ReadSafe -> write flagged
        p.Destroy();        // Instance.Destroy is Unsafe -> flagged
    }
}
