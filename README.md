# Map Reroll

Map Reroll lets you reroll your starting map and steam geyser locations while keeping your scenario, colonists, starting items, and setup intact.

This update brings the mod forward for RimWorld 1.6 while keeping the existing 1.4 and 1.5 folders in place.

## Features

- Reroll the current starting map from an in-game preview grid.
- Reroll steam geyser locations on the current map.
- Keep the same starting scenario, pawns, possessions, and settlement context.
- Optional resource cost based on unmined resources on the map.
- Optional Map Preview integration for more accurate RimWorld 1.6 preview generation.

## Requirements

- RimWorld 1.4, 1.5, or 1.6.
- HugsLib is required and should load before Map Reroll.
- Map Preview is optional, but recommended for the 1.6 preview generator path. If enabled, load Map Preview before Map Reroll.

Suggested load order:

```text
HugsLib
Map Preview (optional)
Map Reroll
```

## How To Use

Start a new game or settle a new map. Click the red dice button in the top-right corner of the screen.

Use **Reroll Map** to open map previews and choose the map you want. Use **Reroll Geysers** to randomize steam geyser locations on the current map.

By default, generating preview pages and rerolling geysers costs a percentage of unmined resources from the map. The cost can be disabled in `Options > Mod Settings`.

## Compatibility Notes

Save before rerolling. Map generation is a sensitive part of RimWorld, and mods that add, replace, or heavily alter map generation can behave unexpectedly.

When Map Preview is loaded, Map Reroll uses Map Preview's preview pipeline and synchronizes the selected map seed with Map Preview's per-tile seed data. If Map Preview is not loaded, Map Reroll falls back to its internal preview generator.

## Building

```powershell
dotnet build .\Source\MapReroll.csproj -c Release
```

The RimWorld 1.6 assembly is written to:

```text
1.6/Assemblies/MapReroll.dll
```

## Credits

Original mod by UnlimitedHugs and TheRealLemon.

Source: https://github.com/Noetroe/Map-Reroll-1.6

Original Workshop item: https://steamcommunity.com/sharedfiles/filedetails/?id=2915575236
