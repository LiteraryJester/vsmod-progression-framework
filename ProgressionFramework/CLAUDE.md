# ProgressionFramework - Vintage Story Mod

## Project Overview

A Vintage Story framework mod (modid: `progressionframework`) providing reusable training, quest, and evolving-trader systems for other mods to build on. Extracted from `vsmod-salt-and-sand` (modid `seafarer`) in April 2026; Seafarer is now its first consumer via hard dependency.

The framework supplies mechanisms (systems, schemas, extension points), not content. Consuming mods ship quests, training definitions, tradelists, and dialogue as JSON in their own asset domains; the framework discovers them at `AssetsFinalize` time by scanning conventional paths across every loaded mod domain.

## Build & Run

- **Build**: `dotnet build` from solution root (`ProgressionFramework/`) or `ProgressionFramework/build.ps1` / `build.sh` (Cake build)
- **Target**: .NET 10.0
- **Output**: `ProgressionFramework/ProgressionFramework/bin/Debug/Mods/mod/` → contains `ProgressionFramework.dll` + `modinfo.json`
- **Dependencies**: Vintage Story install at `$(VINTAGE_STORY)` env var (WSL-friendly path: `/mnt/c/Users/alexa/AppData/Roaming/Vintagestory`) — references `VintagestoryAPI`, `VSSurvivalMod`, `VSEssentials`, `VSCreativeMod`, `VintagestoryLib`, `0Harmony`, `protobuf-net`, `cairo-sharp`, `Newtonsoft.Json`, `Microsoft.Data.Sqlite`
- **Game version**: 1.22.0-rc.7+

Full build command from WSL:

    export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory" && dotnet build ProgressionFramework/ProgressionFramework.csproj

Note: assets under `assets/progressionframework/` are NOT copied into `bin/…/Mods/mod/` at build time. They are loaded at dev time via the standard VS mod discovery when the mod folder is added via `--addOrigin` or via the in-game Mod Manager — VS scans the source folder for assets alongside the compiled DLL.

## Project Structure

```
ProgressionFramework/
├── ProgressionFramework.sln / .slnx
├── build.ps1 / build.sh                       # Cake entry points
├── ZZCakeBuild/                               # Cake build system
├── docs/superpowers/
│   ├── specs/ … -extraction-design.md         # extraction spec
│   └── plans/ … -extraction.md                # extraction plan (reference)
└── ProgressionFramework/                      # C# project
    ├── ProgressionFramework.csproj
    ├── ProgressionFrameworkModSystem.cs       # entry point (class registrations, Harmony)
    ├── modinfo.json
    ├── Training/
    │   ├── TrainingSystem.cs                  # ModSystem — professions, XP sources, trait grants
    │   ├── TrainingData.cs                    # Profession, Training, TrainingLevel, TrainingXpSource
    │   ├── ItemTrainingBook.cs                # Item class for "read to learn" books
    │   ├── CraftingXpPatch.cs                 # Harmony patch: grants craft XP on GridRecipe.ConsumeInput
    │   ├── IXpSourceHandler.cs                # public extension interface
    │   └── Client/GuiDialogTrainingLedger.cs  # client-side ledger GUI
    ├── Quests/
    │   ├── QuestSystem.cs                     # ModSystem — loads JSON quests, tracks state
    │   ├── Quest.cs                           # Quest / QuestObjective / QuestReward / enums
    │   ├── QuestDialogTriggers.cs             # hooks dialogue-triggered quest state changes
    │   ├── QuestRewardHandler.cs              # built-in reward handlers + dispatcher helpers
    │   ├── IQuestObjectiveHandler.cs          # public extension interface
    │   └── IQuestRewardHandler.cs             # public extension interface
    ├── Trading/
    │   ├── EntityEvolvingTrader.cs            # Entity class — trade props + runtime patches
    │   ├── TradePatch.cs                      # persistent trade-item patches (add/remove/set)
    │   └── DialogueTextPatch.cs               # Harmony patch for dialogue variable substitution
    └── assets/progressionframework/
        └── lang/en.json                       # framework UI chrome (ledger title, level-up message, etc.)
```

## Architecture

### Content discovery conventions

The framework scans assets across ALL loaded mod domains at `AssetsFinalize`:

| Subsystem | Path pattern | Notes |
|---|---|---|
| Quests | `<anyDomain>:config/quests/*.json` | Bare codes are domain-prefixed at load time (so `"foo"` in `seafarer:config/quests/foo.json` becomes `seafarer:foo`). First-loaded wins on collisions. |
| Training | `<anyDomain>:config/training/*.json` | Recognised filenames: `professions.json` (profession list), `xp.json` (XP sources), `config.json` (global toggles). Unknown filenames: inspected by top-level key `professions` / `xpSources` / `config`; bare arrays treated as XP sources. |
| Tradelists | `<anyDomain>:config/tradelists/*.json` | Looked up by filename stem from `EntityEvolvingTrader` JSONs. |
| Dialogue | unchanged | Handled by the base-game NPC conversation system; quests listen for dialogue triggers. |

### Extension API (for other mods)

All three subsystems expose string-keyed registries of handlers. Built-in handlers are registered by the framework's own `Start()` — external mods register custom kinds during their own `Start()`:

```csharp
var qs = api.ModLoader.GetModSystem<ProgressionFramework.Quests.QuestSystem>();
qs.RegisterObjectiveType("fetch", new MyFetchObjectiveHandler());
qs.RegisterRewardType("unlockSkill", new MyUnlockSkillHandler());

var ts = api.ModLoader.GetModSystem<ProgressionFramework.Training.TrainingSystem>();
ts.RegisterXpSourceKind("fishing", new MyFishingXpHandler());
```

Interfaces: `IQuestObjectiveHandler`, `IQuestRewardHandler`, `IXpSourceHandler`.

Built-in quest reward types: `addSellingItem`, `giveItem`, `awardTrainingXP`, `incrementEntityVariable`.
Built-in quest objective type: `delivery`.
Built-in XP source kinds: `craft`, `dialogue`, `book`.

### Storage keys

- `WatchedAttributes["progressionframework:questlog"]` — per-player quest state
- `SaveGame["progressionframework:serverquests"]` — server-scoped quest state
- `WatchedAttributes["progressionframework:training"]` — per-player training XP + levels

Registered identifiers that stay content-stable: entity class name `EntityEvolvingTrader`, item class name `ItemTrainingBook`, Harmony id `progressionframework`, hotkey id `progressionframeworktrainingledger`.

### Quest code resolution

`QuestSystem.ResolveCode(code)` is domain-aware: a bare code `"drake-tradeship"` is looked up against `QuestsByCode` entries of the form `<domain>:drake-tradeship` and returns the unique match. If multiple domains define the same bare code, a warning is logged and the lookup fails — pass fully-qualified codes (`"seafarer:drake-tradeship"`) to disambiguate.

## Vintage Story Modding Conventions

### Asset Prefixing (CRITICAL)

When referencing base-game or base-survival assets from mod JSON, always use the `game:` prefix — NEVER `survival:` or no prefix.

- Correct: `"game:block/wood/debarked/{material}"`, `"game:item/resource/rope"`, `"game:log-placed-{material}-ud"`
- The framework's own assets use `progressionframework:` prefix (or no prefix within the mod's own namespace)
- Other mod prefixes: `seafarer:` (Salt and Sand content), `expandedfoods:`, `aculinaryartillery:`, etc. — always with `dependsOn` when patching them

### JSON Format

VS uses JSON5 (relaxed JSON) for asset files — trailing commas, unquoted keys, and comments are allowed.

### Key VS API Patterns

- Mod entry point: class extending `ModSystem` with `Start()`, `StartServerSide()`, `StartClientSide()`, `AssetsFinalize()`
- Register custom classes with `api.RegisterItemClass`, `api.RegisterEntity`, `api.RegisterBlockClass`, `api.RegisterCollectibleBehaviorClass`, etc.
- Use `Mod.Logger` (or `api.Logger`) for logging; `Lang.Get()` for translations
- Mod systems retrieved via `api.ModLoader.GetModSystem<T>()`

### Harmony Patches

- Framework Harmony id is `"progressionframework"` — distinct from salt-and-sand's `"seafarer"` so they don't collide when loaded together
- `PatchAll(typeof(ProgressionFrameworkModSystem).Assembly)` scans this assembly only

## Reference Resources (local paths)

When designing mod code or understanding existing game behavior, consult:

- **VintagestoryAPI source**: `D:\Development\vs\vsapi\` — canonical interfaces (`ICoreAPI`, `ICoreServerAPI`, `ModSystem`, `Entity`, etc.). For WSL: `/mnt/d/Development/vs/vsapi/`.
- **VSSurvivalMod source**: `D:\Development\vs\vssurvivalmod\` — base game content implementations (`EntityTrader`, `BehaviorConversable`, `GridRecipe`, etc.). WSL: `/mnt/d/Development/vs/vssurvivalmod/`.
- **Extracted game assets**: `D:\Development\vs\assets\` — WSL: `/mnt/d/Development/vs/assets/`.
  - `game/` — core game data (blocktypes, entities, shapes, textures, sounds, lang, config)
  - `survival/` — survival mod data (blocktypes, itemtypes, entities, recipes, worldgen, patches)
  - `creative/` — creative mod data
- **Popular community mods**: `D:\Development\vs\existing mods\` — WSL: `/mnt/d/Development/vs/existing mods/` — ACulinaryArtillery, AttributeRenderingLibrary, BetterRuins, Carry On, and others. Useful reference for how community mods structure code, handle compat, and patch base assets.

## Upstream references

- **VS API (GitHub)**: https://github.com/anegostudios/vsapi
- **VS Survival Mod (GitHub)**: https://github.com/anegostudios/vssurvivalmod
- **VS Essentials Mod (GitHub)**: https://github.com/anegostudios/vsessentialsmod

## Consumers

- **Salt and Sand** (`vsmod-salt-and-sand`, modid `seafarer`) — hard dependency in its modinfo. Its content JSONs (`assets/seafarer/config/quests/`, `config/training/`, `config/tradelists/`, `config/dialogue/`) are discovered automatically.

## Known Follow-ups

- Training ledger `L` hotkey may not open the GUI reliably — pre-existing behavior; the hotkey registration path in `StartClientSide` may need the dialog to be registered with the client API's dialog system, not just instantiated. Investigate `GuiDialog` base-class registration patterns in `VintagestoryAPI`.
