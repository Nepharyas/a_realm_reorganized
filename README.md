# A Realm Reorganized

Dalamud plugin for FFXIV that cleans up the Glamour Dresser. Moves what it can into the new 7.5 Armoire and groups stray set pieces back together.

## Status

Very WIP. Builds, the UI loads, the scan runs — but the actual reads/writes against the in-game cabinet and dresser are still stubbed out. Waiting for ClientStructs to catch up on the 7.5 cabinet layout before wiring those in. You can poke at the empty UI in the meantime.

## What it'll do

- Scan the Glamour Dresser and flag items that can now live in the 7.5 Armoire instead
- Detect partial sets (multiple pieces sharing an item series) for regrouping
- Show you everything as a preview first — nothing moves until you click Apply

No timers, no background passes. Everything's user-initiated, which is the rule for the official Dalamud plugin repo.

## Install

Not in the official Dalamud plugin repo yet. Once it's there, you'll be able to find it via `/xlplugins`, search for "A Realm Reorganized", and install in one click.

## Usage

`/arr` opens the main window.

## Development

Needs the .NET 10 SDK and a working Dalamud install (the SDK pulls assemblies from `XIVLauncher\addon\Hooks\dev\`). From the project root:

```
dotnet build
```

The output ends up in `ARealmReorganized\bin\...`. Add that folder to your Dalamud dev plugin paths (`/xlsettings → Experimental → Dev Plugin Locations`), then load it from `/xlplugins → Dev Tools`.

## Tips

If this plugin saves you time, you can [tip me on Ko-fi](https://ko-fi.com/nepharyas). No obligation — feedback is welcome too.

## License

TBD.
