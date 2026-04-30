# ProgressionFramework extraction — design

**Date:** 2026-04-14
**Status:** Draft — awaiting user review before implementation planning

## Goal

Extract the training, quest, and evolving-trader systems from `vsmod-salt-and-sand` (modid `seafarer`) into a new standalone Vintage Story mod `vsmod-progression-framework` (modid `progressionframework`). The framework becomes a generic, reusable library; salt-and-sand retains all Seafarer-specific content and hard-depends on the framework.

## Non-goals

- Backwards compatibility with existing save data (clean break; both mods are pre-release).
- Migration shims or feature flags — salt-and-sand hard-depends on the framework.
- Refactoring beyond what the extraction requires (the three subsystems move largely intact, with only the changes needed for generalization).
- Shipping framework-owned demo/example content.

## Principles driving the split

- **Framework = mechanisms, content = definitions.** Only the C# systems and their generic JSON schemas move. Every Seafarer-specific JSON (quests, NPC dialogue, tradelists, training book variants, trader entities) stays in salt-and-sand and is discovered by the framework at load time.
- **Asset-convention first, C# API second.** Consuming mods describe quests / training / tradelists in JSON at well-known paths. A public C# registry exists for custom objective types, reward types, and XP-source kinds — unused by salt-and-sand on day one, but critical for the "framework" promise.
- **Hard dependency, clean break.** Salt-and-sand lists `progressionframework` as required in modinfo. Storage keys move to the `progressionframework:` namespace; existing save data is not preserved.

## What moves vs. what stays

### Moves to ProgressionFramework (C# code)

- `Training/` — all five files (`TrainingSystem`, `TrainingData`, `ItemTrainingBook`, `GuiDialogTrainingLedger`, `CraftingXpPatch`)
- `quests/` → `Quests/` — all four files (`QuestSystem`, `Quest`, `QuestDialogTriggers`, `QuestRewardHandler`)
- `EntityEvolvingTrader.cs`, `TradePatch.cs`, `DialogueTextPatch.cs` → `Trading/`
- Harmony init: the `PatchAll` call for the XP and dialogue patches moves into the framework's mod system, using its own Harmony id `"progressionframework"`

### Moves to ProgressionFramework (assets)

- Framework-owned UI lang strings (training ledger chrome, generic error messages like "this book has no registered training") → `assets/progressionframework/lang/en.json`
- Nothing else. No entity JSONs, no quest JSONs, no tradelists, no dialogue JSON, no training-book variants, no textures, no shapes.

### Stays in salt-and-sand

- Entity JSONs for Drake / Morgan / Celeste / Reva — these reference the framework's `EntityEvolvingTrader` class by name (VS resolves by class name, not namespace, so no JSON changes needed)
- `assets/seafarer/config/quests/*.json`
- `assets/seafarer/config/dialogue/*.json`
- `assets/seafarer/config/tradelists/*.json`
- `assets/seafarer/config/training-xp.json` (renamed and relocated — see below)
- Training-book variant itemtypes, textures, shapes, recipes
- Content-facing lang strings (profession names, book names, quest text)
- Quest/character design docs (`celeste.md`, `drake.md`, `quest-tree.md`, etc.)

## Repository layout

```
vsmod-progression-framework/
├── LICENSE
└── ProgressionFramework/                  # solution root
    ├── ProgressionFramework.sln
    ├── ProgressionFramework.slnx
    ├── build.ps1 / build.sh
    ├── ZZCakeBuild/
    ├── docs/superpowers/specs/            # this document
    └── ProgressionFramework/              # C# project
        ├── ProgressionFramework.csproj
        ├── ProgressionFrameworkModSystem.cs
        ├── modinfo.json                   # modid: "progressionframework"
        ├── Training/
        │   ├── TrainingSystem.cs
        │   ├── TrainingData.cs
        │   ├── TrainingConfig.cs          # split from TrainingSystem if currently inline
        │   ├── ItemTrainingBook.cs
        │   ├── CraftingXpPatch.cs
        │   ├── IXpSourceHandler.cs
        │   └── Client/
        │       └── GuiDialogTrainingLedger.cs
        ├── Quests/
        │   ├── QuestSystem.cs
        │   ├── Quest.cs
        │   ├── QuestDialogTriggers.cs
        │   ├── QuestRewardHandler.cs
        │   ├── IQuestObjectiveHandler.cs
        │   └── IQuestRewardHandler.cs
        ├── Trading/
        │   ├── EntityEvolvingTrader.cs
        │   ├── TradePatch.cs
        │   └── DialogueTextPatch.cs
        └── assets/
            └── progressionframework/
                └── lang/en.json
```

C# namespace: everything moved is rebased from `SaltAndSand.*` to `ProgressionFramework.*` (`ProgressionFramework.Training`, `.Quests`, `.Trading`).

## Asset discovery

Today, `QuestSystem.LoadQuests` calls `api.Assets.GetMany("config/quests/", modId)` — only the framework's own domain. The extracted framework must scan **every loaded mod's domain**.

Convention paths:

| System | Path pattern |
|---|---|
| Quests | `<anyDomain>:config/quests/*.json` |
| Training | `<anyDomain>:config/training/*.json` (Seafarer's current `config/training-xp.json` is renamed / relocated and may be split) |
| Tradelists | `<anyDomain>:config/tradelists/*.json` |
| Dialogue | unchanged — handled by base-game NPC conversation system |

- Load hook: `AssetsFinalize` (matches current behavior).
- Conflict handling: codes are unique; on collision, log a warning and keep the first-loaded definition.
- Origin: each loaded definition remembers its `AssetLocation.Domain` for logging and diagnostics.

## Storage keys & identifier migration (clean break)

| Old | New |
|---|---|
| `seafarer:questlog` (WatchedAttributes) | `progressionframework:questlog` |
| `seafarer:serverQuests` (SaveGame) | `progressionframework:serverquests` |
| `seafarer-training` (WatchedAttributes) | `progressionframework:training` |
| Harmony id `"seafarer"` (for extracted patches) | `"progressionframework"` |
| VariablesModSystem prefixes `quest-<code>-*` | unchanged (these are content-facing names consumed by dialogue JSON) |
| Dialog trigger names in `QuestDialogTriggers` | unchanged |

Framework-owned lang keys are re-prefixed (`progressionframework:…`); content lang keys stay `seafarer:…`.

Entity class registration `RegisterEntity("EntityEvolvingTrader", …)` moves to the framework's mod system. Seafarer's trader entity JSONs continue to specify `"class": "EntityEvolvingTrader"` — VS resolves by short class name, so no JSON change is needed.

## Extension API (public C#)

Current dispatch sites use hard-coded enums:

- `QuestRewardType`: `AddSellingItem`, `GiveItem`, `AwardTrainingXP`, `IncrementEntityVariable`
- `QuestObjectiveType`: `Delivery` (only one currently in use)
- Training XP source kinds (string-typed already): `dialogue`, `book`, `crafting`

These become string-keyed registries, each populated by the framework's own `Start()` with the built-in handlers — behavior identical to today. External mods register additional types during their own `Start()`.

```csharp
// Quests/IQuestObjectiveHandler.cs
namespace ProgressionFramework.Quests;
public interface IQuestObjectiveHandler
{
    void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective);
    bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData);
}

// Quests/IQuestRewardHandler.cs
public interface IQuestRewardHandler
{
    void Grant(IServerPlayer player, Quest quest, QuestReward reward);
}

// Training/IXpSourceHandler.cs
namespace ProgressionFramework.Training;
public interface IXpSourceHandler
{
    int ComputeXp(IServerPlayer player, TrainingXpSource source, object eventData);
}
```

Registration API on the mod systems:

```csharp
questSystem.RegisterObjectiveType("delivery", new DeliveryObjectiveHandler());
questSystem.RegisterRewardType("giveItem", new GiveItemRewardHandler());
trainingSystem.RegisterXpSourceKind("book", new BookXpHandler());
```

Timing: registrations must happen during `Start()`, before `AssetsFinalize` reads JSON. Unknown `type` values in JSON → warning log + skip. Duplicate registration → last wins, with a warning.

JSON schema stays string-typed. Internal enums are replaced with `string Type` fields. JSON like `"type": "delivery"` continues to parse — this is a refactor, not a format break.

## Salt-and-sand side-effects

After extraction, salt-and-sand:

- Loses the `Training/`, `quests/`, `EntityEvolvingTrader.cs`, `TradePatch.cs`, `DialogueTextPatch.cs` files
- Loses the corresponding `RegisterEntity`, `RegisterItemClass(ItemTrainingBook)`, and Harmony registrations from `SaltAndSandModSystem`
- Gains `"progressionframework": "1.0.0"` under `dependencies` in `modinfo.json`
- Keeps its own Harmony id `"seafarer"` for patches that remain (griddle, exposure, etc.)
- Moves `assets/seafarer/config/training-xp.json` → `assets/seafarer/config/training/xp.json` (directory layout to match the new scan pattern; content is copied verbatim, no schema changes)
- Adds a project reference (or DLL reference) to `ProgressionFramework.dll` so code still referencing framework types compiles

## Build & validation

- Framework builds standalone via `dotnet build` with the existing `$(VINTAGE_STORY)` env var, producing a loadable mod dll under `bin/Debug/Mods/mod/`.
- Salt-and-sand references the framework via `<ProjectReference>` with an absolute path across repos (acceptable for single-dev setup; can be replaced by `HintPath` DLL reference later if needed).
- Salt-and-sand's existing `validate-assets.py` continues to apply to its content; the framework has too few assets to need its own validators.

## Manual smoke-test matrix (must pass before marking extraction complete)

1. Framework mod alone loads into VS with no errors (no content, but no crashes or missing-asset warnings beyond expected).
2. Framework + salt-and-sand load together, and all three subsystems work end-to-end:
   - An evolving trader entity (e.g., Drake) spawns and renders normally.
   - The training ledger hotkey (`L`) opens the GUI.
   - A known quest (e.g., `drake-tradeship`) can be accepted via dialogue and its objectives progress.
3. Training XP increments when the player crafts a target item (exercises the Harmony patch path).
4. Salt-and-sand loaded **without** the framework fails to load with a clear "missing dependency: progressionframework" message.

## Out of scope (noted for future work)

- Shipping a minimal demo trader/quest inside the framework mod so it's usable standalone.
- Splitting `QuestSystem.cs` (743 lines) into smaller files — the current size is a smell but isolated refactor, not part of this extraction.
- Formalizing a public C# API surface package / XML-doc contract.
- CI pipeline for the new framework repo.
