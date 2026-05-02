using ARealmReorganized.Logic;
using ARealmReorganized.Services;
using ARealmReorganized.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ARealmReorganized;

public sealed class Plugin : IDalamudPlugin
{
    public const string Name = "A Realm Reorganized";
    private const string MainCommand = "/arr";

    public Configuration Config { get; }
    public WindowSystem Windows { get; } = new("ARealmReorganized");
    public MainWindow MainWindow { get; }

    public ICabinetService Cabinet { get; }
    public IGlamourDresserService Dresser { get; }
    public ArmoireEligibility Eligibility { get; }
    public IActionExecutor Executor { get; }

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Create<Service>();

        Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Eligibility = new ArmoireEligibility();
        Cabinet = new CabinetService(this, Eligibility);
        Dresser = new GlamourDresserService(this);
        Executor = new ActionExecutor(this);

        MainWindow = new MainWindow(this);
        Windows.AddWindow(MainWindow);

        Service.CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open A Realm Reorganized.",
        });

        Service.PluginInterface.UiBuilder.Draw += Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenMain;
        Service.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        Service.PluginInterface.UiBuilder.Draw -= Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenMain;
        Service.CommandManager.RemoveHandler(MainCommand);
        Windows.RemoveAllWindows();
        MainWindow.Dispose();
    }

    private void OpenMain() => MainWindow.IsOpen = true;
    private void OnCommand(string _, string __) => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!Service.ClientState.IsLoggedIn) return;
        Dresser.RefreshCacheIfLive();
        Cabinet.RefreshCacheIfLive();
    }
}
