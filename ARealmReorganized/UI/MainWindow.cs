using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;
using ARealmReorganized.Services;
using ARealmReorganized.UI.Tabs;
using Dalamud.Bindings.ImGui;
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
        MultipleCopies = [],
        ArmoireRedundant = [],
    };
    private readonly Dictionary<uint, string> itemNames = new();
    private bool saddlebagAvailable = true;
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
    internal bool SaddlebagAvailable => saddlebagAvailable;

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

    // Sets with a single piece in the dresser aren't worth listing, but they still count
    // towards the pieces we point at elsewhere.
    private const int MinPiecesForSet = 2;

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

    private static void DrawHighlightLegend()
    {
        TextDisabledWrapped("Open your dresser / bags / armoury / saddlebag / retainer, slots in these colors:");
        DrawLegendRow(InventoryHighlighter.DresserToArmoireColor, "in your dresser, can move to the armoire");
        DrawLegendRow(InventoryHighlighter.OutsideToArmoireColor, "in bags / armoury / saddlebag / retainer, can move to the armoire");
        DrawLegendRow(InventoryHighlighter.SetCompletionColor, "would complete a partial dresser set if put into the dresser");
        ImGui.Spacing();
    }

    private static void DrawLegendRow(Vector4 color, string text)
    {
        var swatchSize = ImGui.GetFontSize();
        var topLeft = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRect(
            topLeft, topLeft + new Vector2(swatchSize, swatchSize), ImGui.GetColorU32(color), 2f, ImDrawFlags.None, 2f);
        ImGui.Dummy(new Vector2(swatchSize, swatchSize));
        ImGui.SameLine();
        TextDisabledWrapped(text);
    }

    internal void DrawSaddlebagUnavailableBanner()
    {
        if (saddlebagAvailable) return;
        ImGui.PushTextWrapPos();
        ImGui.TextColored(UiColors.Warning,
            "Your saddlebag wasn't readable when you scanned, which happens in instances and until you open it. "
            + "Anything in it may be missing from the lists below, so open the saddlebag once and scan again.");
        ImGui.PopTextWrapPos();
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
        // The scan runs inside the framework callback, so its cost is a frame stall.
        // Timed so there's a number to point at rather than a guess.
        var timer = Stopwatch.StartNew();
        var snapshot = plugin.Dresser.Snapshot();
        storableCandidates = plugin.Cabinet.ListStorable(snapshot);
        var sets = SetCompression.Analyze(snapshot, MinPiecesForSet);
        setGroups = sets.Groups;
        var bags = InventoryReader.ReadAll();
        var bagEntries = bags.Entries;
        saddlebagAvailable = bags.SaddlebagAvailable;
        duplicates = DuplicateDetection.Find(
            snapshot, bagEntries, plugin.Config.CachedRetainers, plugin.Cabinet.IsAlreadyStored, ItemKinds.IsGear);
        var grouped = InventoryGrouping.FilterAndGroup(
            bagEntries,
            entry => plugin.Cabinet.IsStorable(entry.ItemId));
        inventoryStorable = grouped.Deduped;
        inventoryBySource = grouped.BySource;

        // Everything armoire-eligible sitting outside the dresser drives the blue
        // highlight; storables still in the dresser and set-completing pieces get theirs
        // from the other two sets.
        var outsideToArmoire = new HashSet<uint>();
        foreach (var entry in inventoryStorable) outsideToArmoire.Add(entry.ItemId);

        foreach (var snap in plugin.Config.CachedRetainers.Values)
        {
            foreach (var cached in snap.Entries)
            {
                if (plugin.Cabinet.IsStorable(cached.ItemId)) outsideToArmoire.Add(cached.ItemId);
            }
        }

        // Names are resolved lazily as rows get drawn, so the scan only has to drop the
        // ones it might have invalidated.
        itemNames.Clear();

        plugin.Highlighter.SetHighlightSets(storableCandidates, outsideToArmoire, sets.MissingPieceItemIds);

        hasScanned = true;
        Service.Log.Debug(
            "Scan: {DresserItems} dresser items, {Storable} storable, {SetGroups} set groups, " +
            "{Duplicates} duplicates, {FromInventory} from inventory, took {ElapsedMs}ms.",
            snapshot.Count,
            storableCandidates.Count,
            setGroups.Count,
            duplicates.MultipleCopies.Count + duplicates.ArmoireRedundant.Count,
            inventoryStorable.Count,
            timer.ElapsedMilliseconds);
    }
}
