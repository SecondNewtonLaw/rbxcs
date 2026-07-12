using RobloxCS;

namespace Sample;

// Overrides the default output layout + Wally packages location.
public class HelloProject : ProjectDescriptor
{
    public override string ProjectName => "HelloGame";
    public override string SharedMount => "ReplicatedStorage.Common";
    public override string PackagesMount => "ReplicatedStorage.Packages";
    public override string PackagesPath => "Packages";
}
