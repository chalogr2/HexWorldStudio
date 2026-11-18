# Hex World Studio

A procedural hex map generator for Unity. Give it a seed and it builds a whole world: continents, mountains, climate, biomes, forests and deserts, rivers that actually find their way to the sea, lakes, waterfalls, and resources scattered the way you'd expect from a 4X game. If you've played Civilization, you know the kind of map I'm going for.

<!-- Screenshots / GIF go here once the exhibition build is captured. -->

I built it as a code-first Unity tool rather than a game. The generator produces plain data, and a separate renderer turns that data into low-poly 3D tiles. You can use the data without my renderer, or keep the renderer and feed it your own data.

## Trying it

1. Open the project in **Unity 6000.0.68f1** (Unity 6.0 LTS). Anything from 6000.0.58f2 up should work. Older editors have a known security issue, so please don't go below that.
2. Open `Assets/HexWorldStudio/Examples/QuickStart/QuickStartScene.unity`.
3. Press Play.

A world gets generated as soon as the scene starts. The panel in the top left lets you make more:

| Key / control | What it does |
|---|---|
| **Space** or **N** | Build a brand new world from a random seed |
| Seed field + **Build** | Rebuild a specific seed (type it and press Enter) |
| **Map** `<` `>` | Switch map type (Continents, Pangaea, Archipelago, Fractal, Inland Sea, Lakes) on the same seed |
| **Land** `-` `+` | More or less land, same seed |
| **H** | Hide or show the panel |
| **Esc** | Quit |

Getting around the map:

| Input | Action |
|---|---|
| WASD, arrow keys, or push the mouse to a screen edge | Pan |
| Mouse wheel | Zoom |
| Q / E, or middle mouse drag | Rotate |
| R / F, or right mouse drag | Tilt |
| Left click | Select a tile |
| Right click | Clear the selection |

The map wraps east to west like a globe, so if you keep panning sideways you'll come back around to where you started.

## How a world gets made

Generation runs as a fixed sequence of stages, each one reading what the earlier ones left behind:

1. **Continent shape.** Noise decides which tiles are land, then a percentile cut makes sure the land ratio you asked for is what you get. There are seven algorithms to choose from.
2. **Elevation.** A continuous height field, then quantized into six terrace levels so the map reads like stacked hex tiles rather than a smooth heightmap.
3. **Climate.** Temperature comes from latitude and altitude. Moisture is carried by wind from east to west, so mountains get a wet side and a dry rain shadow behind them.
4. **Biomes.** Each biome claims a temperature and moisture range. A smoothing pass then cleans up single-tile speckles so deserts and forests show up as regions, not noise.
5. **Features and resources.** Forests, hills, mountains and so on get picked per tile from what its biome allows, followed by an optional resource pass with rarity and spacing rules.
6. **Rivers and lakes.** Basins in the terrain get filled first, and the deepest ones become lakes. Rivers are grown backwards from the coast and lake shores, along the corners where three hexes meet, so every river is guaranteed to drain somewhere. When a river has to drop a terrace it does it in one go, as a waterfall.

Same seed and same settings always give the same world, down to the last tile. That matters for anything multiplayer, for sharing seeds, and for being able to reproduce a bug.

## Using it from code

```csharp
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using UnityEngine;

var settings = new WorldGenerationSettings(
    width: 64, height: 40, seed: 12345UL,
    landRatio: 0.4f, biomes: myBiomes);

WorldMap map = new HexWorldGenerator().Generate(settings);

if (map.TryGetTile(HexCoordExtensions.FromOffsetOddR(10, 5), out HexTile tile))
{
    Debug.Log($"{tile.Biome.BiomeName}, level {tile.ElevationLevel}, lake: {tile.IsLake}");
}

Debug.Log($"{map.RiverPaths.Count} rivers");
```

Nothing in `WorldMap` or `HexTile` knows about GameObjects, so you can generate maps on a server, in tests, or anywhere else without a scene.

## Making it your own

Most of the content lives in ScriptableObjects, so you change it from the Inspector instead of editing code:

- **Biomes** (`Content/Biomes/`) set a climate envelope, a color, and an optional prefab. The biome inspector draws a temperature by moisture chart where you can drag each biome's range around and see any gaps.
- **Tile features** (`Content/Features/`) are forests, hills, mountains and the like, each with its own prefab.
- **Resources** (`Content/Resources/`) say where they can appear, how rare they are, and how far apart they have to be.
- **World presets** (`Content/Presets/`) bundle map size, land ratio, map type and content into one asset. Earthlike, Pangaea, Archipelago and Inland Sea are included.

Drop a prefab onto a biome or feature and every tile of that type uses it. Leave it empty and you get a plain procedural hex instead.

## Project layout

```
Assets/HexWorldStudio/
  Runtime/      generator, data model, renderer, input
  Editor/       custom inspectors
  Content/      biomes, features, resources, presets, prefabs
  Examples/     the QuickStart scene and its scripts
  Tests/        EditMode and PlayMode tests (469 of them)
  ThirdParty/   Kenney model kits (CC0)
```

If you want the reasoning behind the design, [`ARCHITECTURE.md`](ARCHITECTURE.md) covers the overall structure and [`DECISIONS.md`](DECISIONS.md) has every significant design decision written down as it was made, including the ones I later reversed.

## Stack

Unity 6.0 LTS, URP, C#. No third-party plugins. The Input System package is optional; the project works with the old Input Manager, the new one, or both turned on.

## Credits

The tile, animal and food models come from [Kenney](https://kenney.nl): the Hexagon Kit, Cube Pets and the Food Kit, all released as CC0. Details are in [`Third-Party Notices.txt`](Third-Party%20Notices.txt).

## License

Copyright (c) 2026 Fernando González Ruiz. All rights reserved for now. You're welcome to read the code, but please don't reuse it without asking. See [`LICENSE`](LICENSE).
