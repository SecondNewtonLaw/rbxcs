using RobloxCS;
using System.Threading.Tasks;

namespace Demo;

[Shared]
public class AsyncTest
{
    public async Task<int> GetValue()
    {
        return 42;
    }

    public async Task<int> AddAsync(int a, int b)
    {
        var x = await GetValue();
        return a + b + (x - 42);
    }

    public async Task<string> Chain()
    {
        var v = await AddAsync(1, 2);
        return "sum:" + v;
    }
}
