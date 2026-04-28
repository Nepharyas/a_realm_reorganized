using Dalamud.Game.Command;
using Dalamud.Plugin;

namespace ARealmReorganized;

public sealed class Plugin : IDalamudPlugin
{
    public const string Name = "A Realm Reorganized";
    private const string MainCommand = "/arr";

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Create<Service>();

        Service.CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open A Realm Reorganized.",
        });
    }

    public void Dispose()
    {
        Service.CommandManager.RemoveHandler(MainCommand);
    }

    private void OnCommand(string _, string __)
    {
        Service.Log.Information("not yet implemented");
    }
}
