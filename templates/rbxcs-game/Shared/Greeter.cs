using RobloxCS;

namespace GameTemplate.Shared;

[Shared]
public class Greeter
{
    public string Greet(string who)
    {
        return $"Hello, {who}!";
    }
}
