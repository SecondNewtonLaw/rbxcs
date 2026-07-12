namespace RobloxCS;

// Inherit once per project to override where transpiled modules land in the Roblox DataModel.
// Override any property with a string-literal getter; unspecified ones keep the rbxcs defaults.
// Mounts are dotted DataModel paths: first segment is a service, the rest are child instances.
//
//   public class MyProject : ProjectDescriptor
//   {
//       public override string ProjectName => "MyGame";
//       public override string SharedMount => "ReplicatedStorage.Common";
//   }
public abstract class ProjectDescriptor
{
    public virtual string ProjectName => "rbxcs";
    public virtual string SharedMount => "ReplicatedStorage.rbxcs";
    public virtual string ServerMount => "ServerScriptService.rbxcs";
    public virtual string ClientMount => "StarterPlayer.StarterPlayerScripts.rbxcs";

    // Where Wally packages are mounted in the DataModel (matches your Rojo/Wally setup).
    public virtual string PackagesMount => "ReplicatedStorage.Packages";

    // On-disk Wally packages folder (relative to the project). Empty = don't add it to the Rojo project.
    public virtual string PackagesPath => "";
}
