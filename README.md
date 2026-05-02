# A Realm Reorganized

Dalamud plugin for FFXIV that helps you clean up the Glamour Dresser, because 800 slots is clearly not enough for glam enthusiasts. Moves what it can into the new 7.5 Armoire, groups stray set pieces back together (and shows you what sets are incomplete), and removes duplicates for your Dresser.

## Status

Still WIP. Builds, the UI loads, the scans run — the actual reads/writes against the in-game cabinet and dresser are still ongoing. Currently polishing what I can.

## What it'll do

- Scan the Glamour Dresser and flag items that can now live in the 7.5 Armoire instead
- Detect partial sets (multiple pieces sharing an item series) and complete sets for regrouping
- Detect duplicates and dyes associated to them so you know which one to remove
- Show you everything as a preview first — nothing moves until you click Apply
- Caches items currently in your Dresser and Armoire for ease of use
- Soon (TM): auto sort from your inventory/armoury


## Install

Not in the official Dalamud plugin repo yet. Once it's there, you'll be able to find it via `/xlplugins`, search for "A Realm Reorganized", and install in one click.

## Usage

`/arr` opens the main window.

## Tips

If this plugin saves you time, you can [tip me on Ko-fi](https://ko-fi.com/nepharyas). No obligation though! Feedback is welcome too.

## License

TBD.
