using System;
using System.Collections.Generic;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using ARealmReorganized.Services;
using ARealmReorganized.UI.Tabs;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace ARealmReorganized.UI;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private IReadOnlyList<uint> storableCandidates = Array.Empty<uint>();
    private IReadOnlyList<SetGroup> setGroups = Array.Empty<SetGroup>();
    private IReadOnlyList<InventoryEntry> inventoryStorable = Array.Empty<InventoryEntry>();
    private IReadOnlyDictionary<InventorySource, IReadOnlyList<InventoryEntry>> inventoryBySource =
        new Dictionary<InventorySource, IReadOnlyList<InventoryEntry>>();
    private DuplicateDetection.Result duplicates = new()
    {
        MultipleCopies = Array.Empty<DresserItem>(),
        ArmoireRedundant = Array.Empty<DresserItem>(),
    };
    private readonly Dictionary<uint, string> itemNames = new();
    private bool hasScanned;

    private readonly ArmoireTab armoireTab;
    private readonly CompressTab compressTab;
    private readonly DuplicatesTab duplicatesTab;
    private readonly InventoryTab inventoryTab;
    private readonly RetainersTab retainersTab;

    public MainWindow(Plugin plugin) : base("A Realm Reorganized##main")
    {
        this.plugin = plugin;
        Size = new Vector2(720, 560);
        SizeCondition = ImGuiCond.FirstUseEver;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => plugin.SettingsWindow.IsOpen = true,
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });

        armoireTab = new ArmoireTab(this);
        compressTab = new CompressTab(this);
        duplicatesTab = new DuplicatesTab(this);
        inventoryTab = new InventoryTab(this);
        retainersTab = new RetainersTab(plugin, this);
    }

    public void Dispose() { }

    // --- scan-state read-only views (consumed by tabs) ---
    internal IReadOnlyList<uint> StorableCandidates => storableCandidates;
    internal IReadOnlyList<SetGroup> SetGroups => setGroups;
    internal IReadOnlyList<InventoryEntry> InventoryStorable => inventoryStorable;
    internal IReadOnlyDictionary<InventorySource, IReadOnlyList<InventoryEntry>> InventoryBySource => inventoryBySource;
    internal DuplicateDetection.Result Duplicates => duplicates;

    public override void Draw()
    {
        ImGui.TextWrapped(
            "Tidy up your glam collection! Scans your Glamour Dresser for items that can be moved to the Armoire, " +
            "detects sets that can be regrouped, and helps you free inventory/retainers/chocobo space.");
        ImGui.Separator();

        DrawServiceStatus();
        ImGui.Spacing();
        DrawScanRow();
        ImGui.Separator();

        var footerHeight = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginChild("##body", new Vector2(0, -footerHeight)))
        {
            if (!hasScanned)
            {
                TextDisabledWrapped("Press Scan to populate results.");
            }
            else
            {
                DrawHighlightLegend();
                if (ImGui.BeginTabBar("##arrtabs"))
                {
                    if (ImGui.BeginTabItem(armoireTab.TabLabel)) { armoireTab.Draw(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(compressTab.TabLabel)) { compressTab.Draw(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(duplicatesTab.TabLabel)) { duplicatesTab.Draw(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(inventoryTab.TabLabel)) { inventoryTab.Draw(); ImGui.EndTabItem(); }
                    if (ImGui.BeginTabItem(retainersTab.TabLabel)) { retainersTab.Draw(); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
            }
        }
        ImGui.EndChild();

        DrawFooter();
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1f, 0.37f, 0.36f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.5f, 0.48f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.85f, 0.3f, 0.3f, 1f));
        if (ImGui.SmallButton("♥ Support on Ko-fi"))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://ko-fi.com/nepharyas",
                UseShellExecute = true,
            });
        }
        ImGui.PopStyleColor(3);
    }

    private void DrawServiceStatus()
    {
        var dresserCache = plugin.Config.CachedDresser;
        var cabinetCache = plugin.Config.CachedCabinet;

        var dresserMsg = dresserCache.RefreshedAt == DateTime.MinValue
            ? "dresser: never seen yet"
            : $"dresser: {Humanize(DateTime.UtcNow - dresserCache.RefreshedAt)} ago ({dresserCache.Slots.Count} items)";
        var cabinetMsg = cabinetCache.RefreshedAt == DateTime.MinValue
            ? "armoire: never seen yet"
            : $"armoire: {Humanize(DateTime.UtcNow - cabinetCache.RefreshedAt)} ago ({cabinetCache.StoredIds.Count} stored)";

        TextDisabledWrapped($"{dresserMsg}    {cabinetMsg}");
    }

    internal static string Humanize(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes}m";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours}h";
        return $"{(int)ts.TotalDays}d";
    }

    internal static void TextDisabledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    // Cap is well above the eligible-set size for any plausible session; if exceeded we just
    // wipe and rebuild on demand rather than tracking LRU.
    private const int ItemNameCacheCap = 5000;

    internal string ResolveItemName(uint itemId)
    {
        if (itemNames.TryGetValue(itemId, out var cached)) return cached;
        if (itemNames.Count >= ItemNameCacheCap) itemNames.Clear();
        var resolved = ItemNames.Resolve(itemId);
        itemNames[itemId] = resolved;
        return resolved;
    }

    // Legend swatch colors match the outline colors drawn by InventoryHighlighter. Keep
    // these in sync with the constants there.
    private static readonly Vector4 LegendDresserToArmoireColor = new(0.4f, 1.0f, 0.4f, 1f); // green
    private static readonly Vector4 LegendOutsideToArmoireColor = new(0.3f, 0.6f, 1.0f, 1f); // blue
    private static readonly Vector4 LegendSetCompletionColor    = new(1.0f, 0.85f, 0.3f, 1f); // gold

    private static void DrawHighlightLegend()
    {
        TextDisabledWrapped("Open your bags / armoury / saddlebag / retainer, slots with these outlines:");
        ImGui.TextColored(LegendDresserToArmoireColor, "□ in your dresser, can move to the armoire");
        ImGui.TextColored(LegendOutsideToArmoireColor, "□ in bags / armoury / saddlebag / retainer, can move to the armoire");
        ImGui.TextColored(LegendSetCompletionColor,    "□ would complete a partial dresser set if put into the dresser");
        ImGui.Spacing();
    }

    internal void DrawCabinetUnavailableBanner()
    {
        if (plugin.Cabinet.IsFresh) return;
        ImGui.PushTextWrapPos();
        ImGui.TextColored(UiColors.Warning,
            "Open the Armoire once this session to load stored-item data. Until then, items already in the armoire may show in the lists below.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    internal void DrawItemRow(InventoryEntry entry)
    {
        var name = ResolveItemName(entry.ItemId);
        ImGui.TextUnformatted(entry.IsHq ? $"{name} HQ" : name);
    }

    private void DrawScanRow()
    {
        if (ImGui.Button("Scan")) RunScan();
    }

    private void RunScan()
    {
        var snapshot = plugin.Dresser.Snapshot();
        storableCandidates = plugin.Cabinet.ListStorable(snapshot);
        setGroups = SetCompression.GroupBySeries(snapshot, 2);
        duplicates = DuplicateDetection.Find(snapshot, plugin.Cabinet);
        var grouped = InventoryGrouping.FilterAndGroup(
            InventoryReader.ReadAll(),
            entry => plugin.Cabinet.IsStorable(entry.ItemId));
        inventoryStorable = grouped.Deduped;
        inventoryBySource = grouped.BySource;

        // Feed the highlighter three id sets, one per intent.
        // - dresser→armoire: items already in the dresser that can move to the armoire.
        // - outside→armoire: armoire-eligible items found in bags / armoury / saddlebag /
        //   retainer (the "go put these in the dresser or armoire" set).
        // - set completion: items that would finish a partial dresser set if added.
        var outsideToArmoire = new HashSet<uint>();
        foreach (var entry in inventoryStorable) outsideToArmoire.Add(entry.ItemId);
        foreach (var snap in plugin.Config.CachedRetainers.Values)
            foreach (var cached in snap.Entries)
                if (plugin.Cabinet.IsStorable(cached.ItemId)) outsideToArmoire.Add(cached.ItemId);
        var setCompletion = SetCompression.GetMissingPieceItemIds(snapshot);
        plugin.Highlighter.SetHighlightSets(storableCandidates, outsideToArmoire, setCompletion);

        itemNames.Clear();
        var allIds = new HashSet<uint>(storableCandidates);
        foreach (var dresserItem in duplicates.MultipleCopies) allIds.Add(dresserItem.ItemId);
        foreach (var dresserItem in duplicates.ArmoireRedundant) allIds.Add(dresserItem.ItemId);
        foreach (var inventoryEntry in inventoryStorable) allIds.Add(inventoryEntry.ItemId);
        foreach (var snap in plugin.Config.CachedRetainers.Values)
            foreach (var cached in snap.Entries) allIds.Add(cached.ItemId);
        foreach (var itemId in allIds) itemNames[itemId] = ItemNames.Resolve(itemId);

        hasScanned = true;
        var scanMsg =
            $"Scan: {snapshot.Count} dresser items, {storableCandidates.Count} storable, " +
            $"{setGroups.Count} set groups, " +
            $"{duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count} duplicates, " +
            $"{inventoryStorable.Count} from inventory.";
        Service.Log.Information(scanMsg);
        plugin.LogBuffer.Add(scanMsg);
    }
}
