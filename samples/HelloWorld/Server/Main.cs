using RobloxCS;
using Demo;
using static Roblox.Globals;

namespace Game;

// [Server] -> ServerScriptService.rbxcs ; [Script] -> executable Script running static Main().
[Server]
[Script]
public static class Bootstrap
{
    public static void Main()
    {
        var dog = new Dog("Rex");
        print(dog.Describe());
        print(dog.Speak());
    }
}
