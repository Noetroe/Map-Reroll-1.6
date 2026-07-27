# Map Reroll

Map Reroll lets you reroll your starting map and steam geyser locations while keeping your scenario, colonists, starting items, and setup intact.

This update brings the mod forward for RimWorld 1.6 while keeping the existing 1.4 and 1.5 folders in place.

## Features

- Reroll the current starting map from an in-game preview grid.
- Reroll steam geyser locations on the current map.
- Keep the same starting scenario, pawns, possessions, and settlement context.
- Optional resource cost based on unmined resources on the map.
- Optional Map Preview integration for more accurate RimWorld 1.6 preview generation.
- Geological Landforms compatibility for generated previews and rerolled maps.

## Requirements

- RimWorld 1.4, 1.5, or 1.6.
- Harmony is required and must load first.
- HugsLib is required and should load before Map Reroll.
- Map Preview is optional, but recommended for the 1.6 preview generator path. If enabled, load Map Preview before Map Reroll.

Suggested load order:

```text
Harmony
Core and DLC
HugsLib
Map Preview (optional)
Map Reroll
Geological Landforms (optional)
```

## How To Use

Start a new game or settle a new map. Click the red dice button in the top-right corner of the screen.

Use **Reroll Map** to open map previews and choose the map you want. Use **Reroll Geysers** to randomize steam geyser locations on the current map.

By default, generating preview pages and rerolling geysers costs a percentage of unmined resources from the map. The cost can be disabled in `Options > Mod Settings`.

## Compatibility Notes

Save before rerolling. Map generation is a sensitive part of RimWorld, and mods that add, replace, or heavily alter map generation can behave unexpectedly.

When Map Preview is loaded, Map Reroll uses Map Preview's preview pipeline and synchronizes the selected map seed with Map Preview's per-tile seed data. If Map Preview is not loaded, Map Reroll falls back to its internal preview generator.

Geological Landforms should load after Map Reroll. The 1.6 fallback preview generator now runs through RimWorld's normal map-generation hook so landform terrain steps can participate, and Map Reroll falls back locally if Map Preview rejects a preview request.

Version 2.8.6 also initializes fallback previews with a visible placeholder instead of a black texture, uses the current map's size and generator, and rejects blank results returned by Map Preview before retrying locally.

Version 2.8.7 limits map and geyser rerolls to vanilla player-home settlements. Scripted maps, quest sites, and unknown custom map parents are blocked so their mod-owned state cannot be invalidated. It also captures each map's central generation recipe and blocks destructive rerolls when the original request used custom extra GenSteps, a pre-content callback, pocket-map generation, or the step debugger. These transient inputs are classified instead of being enumerated or reconstructed later.

The existing Setup Camp `CaravanCamp` integration remains supported, and selecting **Keep this map** is enforced by the execution path as well as the UI.

Mods that provide a custom map parent verified to support full rerolls can opt in during initialization:

```csharp
MapRerollSafetyPolicy.RegisterSafeMapParentType(typeof(MyMapParent));
```

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
