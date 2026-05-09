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
    public SettingsWindow SettingsWindow { get; }

    public ICabinetService Cabinet { get; }
    public IGlamourDresserService Dresser { get; }
    public ArmoireEligibility Eligibility { get; }
    internal RetainerCacheService Retainers { get; }
    internal InventoryHighlighter Highlighter { get; }
    public PluginLogBuffer LogBuffer { get; } = new();

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Create<Service>();

        Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Eligibility = new ArmoireEligibility();
        LogBuffer.Add($"Armoire-eligible items in current game data: {Eligibility.Count}");
        Cabinet = new CabinetService(this, Eligibility);
        Dresser = new GlamourDresserService(this);
        Retainers = new RetainerCacheService(this);
        Highlighter = new InventoryHighlighter(this);

        MainWindow = new MainWindow(this);
        SettingsWindow = new SettingsWindow(this);
        Windows.AddWindow(MainWindow);
        Windows.AddWindow(SettingsWindow);

        Service.CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open A Realm Reorganized.",
        });

        Service.PluginInterface.UiBuilder.Draw += Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
        Service.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        Service.PluginInterface.UiBuilder.Draw -= Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
        Service.CommandManager.RemoveHandler(MainCommand);
        Windows.RemoveAllWindows();
        MainWindow.Dispose();
        SettingsWindow.Dispose();
    }

    private void OpenMain() => MainWindow.IsOpen = true;
    private void OpenSettings() => SettingsWindow.IsOpen = true;
    private void OnCommand(string _, string __) => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!Service.ClientState.IsLoggedIn) return;
        Dresser.RefreshCacheIfLive();
        Cabinet.RefreshCacheIfLive();
        Retainers.RefreshCacheIfLive();
        Highlighter.Tick();
    }
}
