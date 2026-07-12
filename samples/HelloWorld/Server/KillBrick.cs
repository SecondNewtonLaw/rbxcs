using RobloxCS;
using Roblox;
using static Roblox.Globals;

namespace Game;

// A small but real server script: spawns an anchored kill-brick, kills any Humanoid that touches it,
// and raycasts downward to find the floor. Exercises instance creation, property sets, events,
// raycasting, casting, and Humanoid movement.
[Server]
[Script]
public static class KillBrickServer
{
    public static void Main()
    {
        var brick = SpawnKillBrick(new Vector3(0, 5, 0));
        print("kill brick spawned at", brick.Position);

        var hit = CastToFloor(brick.Position);
        if (hit != null)
            print("floor found at", hit.Position);
    }

    private static Part SpawnKillBrick(Vector3 position)
    {
        var part = new Part();
        part.Size = new Vector3(8, 1, 8);
        part.Position = position;
        part.Anchored = true;
        part.BrickColor = new BrickColor(21);
        part.Parent = workspace;

        part.Touched.Connect(otherPart =>
        {
            var character = otherPart.Parent;
            var humanoid = character.FindFirstChildOfClass("Humanoid");
            if (humanoid != null)
            {
                var h = (Humanoid)humanoid;
                h.Health = 0;
            }
        });

        return part;
    }

    private static RaycastResult CastToFloor(Vector3 from)
    {
        var rayParams = new RaycastParams();
        rayParams.IgnoreWater = true;
        return workspace.Raycast(from, new Vector3(0, -50, 0), rayParams);
    }

    private static void Boost(Humanoid humanoid)
    {
        humanoid.WalkSpeed = 50;
        humanoid.JumpPower = 100;
    }
}
