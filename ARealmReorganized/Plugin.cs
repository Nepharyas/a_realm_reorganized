using System;
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
    internal RetainerCacheService Retainers { get; }
    internal InventoryHighlighter Highlighter { get; }

    public Plugin(IDalamudPluginInterface pi)
    {
        pi.Create<Service>();

        Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Eligibility = new ArmoireEligibility();
        Service.Log.Information("Loaded {Count} armoire-eligible items.", Eligibility.Count);
        Cabinet = new CabinetService(this, Eligibility);
        Dresser = new GlamourDresserService(this);
        Retainers = new RetainerCacheService(this);
        Highlighter = new InventoryHighlighter(pi);

        MainWindow = new MainWindow(this);
        Windows.AddWindow(MainWindow);

        Service.CommandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open A Realm Reorganized.",
        });

        Service.PluginInterface.UiBuilder.Draw += Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        Service.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        Service.PluginInterface.UiBuilder.Draw -= Windows.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
        Service.CommandManager.RemoveHandler(MainCommand);
        DisposeOnGameThread(Highlighter);
        Windows.RemoveAllWindows();
        MainWindow.Dispose();
    }

    // The highlighter owns nodes inside the game's windows, which only the game thread
    // may free. Unload normally happens there already; this covers the case where it doesn't.
    private static void DisposeOnGameThread(IDisposable disposable)
    {
        if (Service.Framework.IsInFrameworkUpdateThread)
        {
            disposable.Dispose();
            return;
        }
        Service.Framework.RunOnFrameworkThread(disposable.Dispose).Wait();
    }

    private void OpenMain() => MainWindow.IsOpen = true;
    private void OnCommand(string _, string __) => MainWindow.Toggle();

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!Service.ClientState.IsLoggedIn) return;
        Dresser.RefreshCacheIfLive();
        Cabinet.RefreshCacheIfLive();
        Retainers.RefreshCacheIfLive();
    }
}
