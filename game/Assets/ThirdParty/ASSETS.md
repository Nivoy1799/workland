# Third-party asset manifest

One row per pack folder `<Creator>/<Pack>/` under this directory. `scripts/check-assets.sh`
(run by `scripts/verify.sh`) fails when a folder has no row, a row has no folder, a licence is
not on the allow-list (only `CC0-1.0` for now; adding one is a human decision), or this folder
grows past 100 MB. Pack files are kept unmodified, only the files that are used.

| Folder | Pack | Creator | Licence | Source URL | Imported |
|---|---|---|---|---|---|
| Kenney/FurnitureKit | Furniture Kit | Kenney | CC0-1.0 | https://kenney.nl/assets/furniture-kit | 2026-11-07 |
| Kenney/CityKitSuburban | City Kit (Suburban) | Kenney | CC0-1.0 | https://kenney.nl/assets/city-kit-suburban | 2026-11-07 |
| Kenney/CityKitCommercial | City Kit (Commercial) | Kenney | CC0-1.0 | https://kenney.nl/assets/city-kit-commercial | 2026-11-07 |
| Kenney/CityKitRoads | City Kit (Roads) | Kenney | CC0-1.0 | https://kenney.nl/assets/city-kit-roads | 2026-11-07 |
| Kenney/FoodKit | Food Kit | Kenney | CC0-1.0 | https://kenney.nl/assets/food-kit | 2026-11-07 |
| Kenney/MiniCharacters | Mini Characters (2.0) | Kenney | CC0-1.0 | https://kenney.nl/assets/mini-characters | 2026-11-08 |
| Kenney/AnimatedCharactersProtagonists | Animated Characters Protagonists (1.1) | Kenney | CC0-1.0 | https://kenney.nl/assets/animated-characters-protagonists | 2026-11-08 |
| Quaternius/UniversalAnimationLibrary | Universal Animation Library (Standard, v3.0; the root-motion file UAL1_Standard_RM.fbx, stored as UAL1_Standard.fbx) | Quaternius | CC0-1.0 | https://quaternius.itch.io/universal-animation-library | 2026-11-08 |
