# /game

**Purpose:** the Unity 6 LTS project. One codebase, two build targets: Windows client and
Linux dedicated server (headless). Networking via FishNet (FishySteamworks + Tugboat).
MonoBehaviours stay thin: they call into `/sim` and networking and hold no game rules.

**Typically built by:** Claude for anything touching FishNet/networking; Qwen Code for
simple UI wiring and MonoBehaviours.

**How to test:** set `UNITY_PATH` to the editor binary, close the Editor (batch mode needs the
project unlocked), then `bash scripts/verify.sh` runs the EditMode tests. In the Editor,
use the Test Runner or MCP for Unity.

**Creating the project (human, task 0.8):** Unity Hub will not create a project inside a
non-empty folder. Create it elsewhere (3D URP, any name), then move its contents into this
folder, keeping this README. Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`.
