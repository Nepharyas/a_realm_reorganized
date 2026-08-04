# A Realm Reorganized

Dalamud plugin for FFXIV that helps you clean up the Glamour Dresser, because 800 slots is clearly not enough for glam enthusiasts. Shows what can be moved into the new 7.5 Armoire, points out partial sets you could regroup, flags duplicates with their dyes so you know which copy to keep, and lights the relevant items up right in your bags so you don't have to hunt for them.

## What it does

- Scans the Glamour Dresser and lists items that can now live in the 7.5 Armoire instead
- Highlights everything in-game with a color per intent: items in the dresser that can move to the armoire, armoire-eligible items sitting in your bags/armoury/saddlebag/retainers, and pieces that would complete a partial dresser set. Works across the dresser, bag, armoury chest, saddlebag and retainer windows, with a color legend in the main window
- Detects partial sets (multiple pieces sharing an item series) and complete sets
- Detects duplicates across the dresser, armoire, bags, armoury, saddlebag and retainers, with dyes shown so you know which copy to drop
- Caches dresser/armoire/retainer state across sessions, so retainer data is as fresh as your last visit to the bell

## Install

Submitted to the official Dalamud repo (testing channel). Once it's in: turn on testing builds in `/xlsettings`, then find "A Realm Reorganized" in `/xlplugins`.

## Usage

`/arr` opens the main window. Click Scan, browse the tabs, then open your dresser or bags and follow the colors. Open the Armoire once per session so the plugin knows what's already stored, and visit your retainers at the bell to refresh their contents.

## Tips

If this plugin saves you time, you can [tip me on Ko-fi](https://ko-fi.com/nepharyas). No obligation though! Feedback is welcome too.

## License

AGPL-3.0. See LICENSE.
