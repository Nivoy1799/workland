# Adding art (spec 015, decision D26)

Free CC0 art only, behind a swap layer: gameplay objects keep colliders and components, only a
visual child comes from `ArtCatalog`. Switching to another pack means changing catalog entries.

## Steps to add a model
1. **Folder.** Copy only the used model and texture files, unmodified, plus the pack's licence file,
   into `game/Assets/ThirdParty/<Creator>/<Pack>/`. Never edit files in there.
2. **Manifest.** Add one row to `game/Assets/ThirdParty/ASSETS.md`:
   `| Folder | Pack | Creator | Licence | Source URL | Imported |`. Only `CC0-1.0` is allowed;
   another licence is a human decision. Total size cap is 100 MB (then move to Git LFS).
3. **Wrapper prefab.** Add the model to the `Defs` table in
   `game/Assets/_Project/Editor/ArtCatalogBuilder.cs` (key, model path, fit mode). The wrapper
   holds the model as a child; scale, pivot, rotation and material overrides live there. No
   colliders, no scripts. Stations are fitted automatically to 1.2 x 1.2 m footprint, at most 1.1 m high.
4. **Catalog entry.** Add an `ArtKey` in `game/Assets/_Project/Scripts/Art/ArtKey.cs` if needed.
   Station slots are mapped by `ArtCatalogRules.StationKey(StationKind)` (explicit switch); add a case for every new `StationKind`.
5. **Regenerate.** Run `Worktown/Build Graybox Scene` (or `-executeMethod
   Worktown.Game.Editor.GrayboxSceneBuilder.Build` in batch mode). It rebuilds the wrapper
   prefabs, `Art/ArtCatalog.asset` and the scene. Never hand-edit the scene.
6. **Check.** `scripts/verify.sh` runs `scripts/check-assets.sh` and the EditMode art tests.

## Rules
- One pack family per slot type. Kenney first; KayKit or Quaternius only where Kenney has nothing,
  and note that in the manifest.
- A slot without a catalog prefab falls back to the graybox primitive, so a half-filled catalog builds.
- Street and props are visual only: no colliders, static.
