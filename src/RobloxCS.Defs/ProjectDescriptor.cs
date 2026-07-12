namespace RobloxCS;

/// <summary>
/// Override where transpiled modules land in the Roblox DataModel. Inherit once per project and
/// override any property with a string-literal getter; unspecified ones keep the rbxcs defaults.
/// </summary>
/// <remarks>
/// Mounts are dotted DataModel paths: the first segment is a service, the rest are child instances.
/// The descriptor class itself is not transpiled.
/// </remarks>
/// <example>
/// <code>
/// public class MyProject : ProjectDescriptor
/// {
///     public override string ProjectName => "MyGame";
///     public override string SharedMount => "ReplicatedStorage.Common";
/// }
/// </code>
/// </example>
public abstract class ProjectDescriptor
{
    /// <summary>Rojo project name (the DataModel root name). Default <c>rbxcs</c>.</summary>
    public virtual string ProjectName => "rbxcs";

    /// <summary>Mount for <c>[Shared]</c> modules. Default <c>ReplicatedStorage.rbxcs</c>.</summary>
    public virtual string SharedMount => "ReplicatedStorage.rbxcs";

    /// <summary>Mount for <c>[Server]</c> modules. Default <c>ServerScriptService.rbxcs</c>.</summary>
    public virtual string ServerMount => "ServerScriptService.rbxcs";

    /// <summary>Mount for <c>[Client]</c> modules. Default <c>StarterPlayer.StarterPlayerScripts.rbxcs</c>.</summary>
    public virtual string ClientMount => "StarterPlayer.StarterPlayerScripts.rbxcs";

    /// <summary>
    /// Where Wally packages mount in the DataModel (match your Rojo/Wally setup). Default
    /// <c>ReplicatedStorage.Packages</c>.
    /// </summary>
    public virtual string PackagesMount => "ReplicatedStorage.Packages";

    /// <summary>
    /// On-disk Wally packages folder, relative to the project. Empty means "don't add it to the Rojo
    /// project".
    /// </summary>
    public virtual string PackagesPath => "";
}
