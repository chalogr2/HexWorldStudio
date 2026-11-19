# Hex World Studio

A procedural hex map generator for Unity. You give it a seed and it builds a whole world: continents, mountain ranges, climate, biomes, forests and deserts, rivers that actually find their way to the sea, lakes, waterfalls, and resources scattered around the way you'd expect from a 4X game. If you've played Civilization, you know the kind of map I was going for.

![A generated world seen from above](docs/images/hero.png)

I built this as a code-first Unity tool, not as a game. The generator only produces data, and a separate renderer turns that data into low-poly 3D tiles. You can use the data without my renderer, or keep the renderer and feed it your own data.

## Try it

There's a ready to play Windows build, so you don't need Unity to try it. You need a 64-bit Windows PC.

1. Go to the **[latest release](https://github.com/chalogr2/Hex-Tile-World-Generator/releases/latest)**. You can also get there from the repo's main page: click **Releases** in the right-hand sidebar.
2. Under **Assets**, download `HexWorldStudio-v1.0-mvp-Windows.zip` (about 36 MB).
3. Right-click the zip and choose **Extract All**. Don't run it from inside the zip, Windows won't find the files it needs next to the exe.
4. Open the extracted folder and double-click `Hex Tile World Generator.exe`.
5. Windows may show a blue "Windows protected your PC" box, because the exe isn't signed. Click **More info**, then **Run anyway**.

Nothing gets installed. To remove it, just delete the folder.

A world is generated as soon as it starts. The panel in the top left corner lets you make more:

| Key / control | What it does |
|---|---|
| **Space** or **N** | Build a brand new world from a random seed |
| Seed field + **Build** | Rebuild a specific seed (type it and press Enter) |
| **Map** `<` `>` | Switch map type on the same seed: Continents, Pangaea, Archipelago, Fractal, Inland Sea, Lakes |
| **Land** `-` `+` | More or less land, same seed |
| **H** | Hide or show the panel |
| **Esc** | Quit |

Getting around:

| Input | Action |
|---|---|
| WASD, arrow keys, or push the mouse against a screen edge | Pan |
| Mouse wheel | Zoom |
| Q / E, or drag with the middle mouse button | Rotate |
| R / F, or drag with the right mouse button | Tilt |
| Left click | Select a tile |
| Right click | Clear the selection |

The map wraps east to west like a globe, so if you keep panning sideways you'll end up back where you started.

<table>
  <tr>
    <th>Archipelago</th>
    <th>Pangaea</th>
    <th>Up close</th>
  </tr>
  <tr>
    <td><img src="docs/images/archipelago.png" alt="Archipelago map type" width="270"></td>
    <td><img src="docs/images/pangaea.png" alt="Pangaea map type" width="270"></td>
    <td><img src="docs/images/closeup.png" alt="Close view of a coastline" width="270"></td>
  </tr>
</table>

## How a world gets made

Generation is a fixed sequence of stages. Each one reads what the earlier ones left behind:

1. **Continent shape.** Noise decides which tiles are land, then a percentile cut makes sure you get exactly the land ratio you asked for. There are seven algorithms to pick from.
2. **Elevation.** A smooth height field, then cut into six terrace levels so the map reads as stacked hex tiles instead of a rolling heightmap.
3. **Climate.** Temperature comes from latitude and altitude. Moisture is carried by wind from east to west, so mountains get a wet side and a dry rain shadow behind them.
4. **Biomes.** Every biome claims a range of temperature and moisture. A smoothing pass then cleans up lone tiles so deserts and forests show up as regions rather than noise.
5. **Features and resources.** Forests, hills, mountains and so on are picked per tile from what its biome allows. After that comes an optional resource pass with rarity and spacing rules.
6. **Rivers and lakes.** First the basins in the terrain get filled, and the deep ones become lakes. Every lake sits at one level, flush with its lowest shore. Rivers are grown backwards from the coast and lake shores along the corners where three hexes meet, so every river is guaranteed to drain somewhere. When a river needs to drop a terrace, it does it in one go as a waterfall.

Same seed and same settings always give the same world, down to the last tile. That matters for multiplayer, for sharing seeds with friends, and for being able to reproduce a bug.

On the rendering side, tiles that stand above their neighbors get cliff pieces stacked under them so nothing floats, all sea water sits at one height, and the world sits in an endless ocean that fades into haze at the horizon.

## Opening the project

1. Open the folder in **Unity 6000.0.68f1** (Unity 6.0 LTS). Anything from 6000.0.58f2 up should work. Older editors have a known security issue, so please don't go below that.
2. Open `Assets/HexWorldStudio/Examples/QuickStart/QuickStartScene.unity`.
3. Press Play.

Everything you saw in the build is in that scene. The panel is a single script (`RegenerateOverlay`) on its own GameObject, so you can drop it into another scene or delete it without breaking anything.

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

Nothing in `WorldMap` or `HexTile` knows about GameObjects, so you can generate maps on a server, inside tests, or anywhere else without a scene.

## Making it your own

Most of the content lives in ScriptableObjects, so you change it in the Inspector instead of in code:

- **Biomes** (`Content/Biomes/`) set a climate range, a color and an optional prefab. The biome inspector draws a temperature by moisture chart where you can drag each biome's range around and spot any gaps.
- **Tile features** (`Content/Features/`) are forests, hills, mountains and the like, each with its own prefab.
- **Resources** (`Content/Resources/`) say where they can show up, how rare they are, and how far apart they have to be.
- **World presets** (`Content/Presets/`) bundle map size, land ratio, map type and content into one asset. Earthlike, Pangaea, Archipelago and Inland Sea are included.

Drop a prefab onto a biome or feature and every tile of that type uses it. Leave the slot empty and you get a plain procedural hex instead.

## Project layout

```
Assets/HexWorldStudio/
  Runtime/      generator, data model, renderer, input
  Editor/       custom inspectors
  Content/      biomes, features, resources, presets, prefabs, shaders
  Examples/     the QuickStart scene and its scripts
  Tests/        480 EditMode and PlayMode tests
  ThirdParty/   Kenney model kits (CC0)
Assets/DevTools/  editor script that renders the screenshots in docs/images
```

## The dev docs

There are four Markdown files in the root besides this one. I kept them up to date as the project went along, so they're the best place to understand why the code looks the way it does.

- **[`ARCHITECTURE.md`](ARCHITECTURE.md)** is the map of the code: how the data layer, the generation pipeline and the renderer fit together, the order the stages run in, and the rules each part has to follow.
- **[`DECISIONS.md`](DECISIONS.md)** is a decision log with 28 entries. Every time I made a choice that shaped the project (Unity version, hex orientation, how rivers are modelled, dropping the Asset Store plan) it got written down with the date, the options I considered and why I picked the one I did. Old entries are never edited. When I changed my mind, a new entry says so and points back to the old one, so you can follow how the thinking moved.
- **[`FEATURES.md`](FEATURES.md)** is the feature list with a status on every item: done, in progress, planned or dropped. It's the quickest way to see what works today and what's still on the wish list.
- **[`CLAUDE.md`](CLAUDE.md)** is the orientation file for working on the project with Claude Code. It holds the ground rules (no third-party dependencies, deterministic output, docs updated in the same commit as the code) and a running log of every development slice. If you want the history of the project in order, read its "Current project state" section from top to bottom.

## Tests

There are 480 tests covering coordinates, the RNG, every generation stage, river and lake invariants, the renderer and the demo scripts. Run them from **Window > General > Test Runner** in Unity. One of them runs in real Play mode on purpose, because it guards against a bug that only shows up there.

## Stack

Unity 6.0 LTS, URP, C#. No third-party plugins and no paid assets. The Input System package is optional; the project works with the old Input Manager, the new one, or both turned on.

## Credits

The tile, animal and food models come from [Kenney](https://kenney.nl): the Hexagon Kit, Cube Pets and the Food Kit, all released as CC0. Details are in [`Third-Party Notices.txt`](Third-Party%20Notices.txt).

## License

Copyright (c) 2026 Fernando González Ruiz. All rights reserved for now. You're welcome to read the code and play the build, but please ask before reusing any of it. See [`LICENSE`](LICENSE).
