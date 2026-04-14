# ProgressionFramework Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the training, quest, and evolving-trader subsystems from `vsmod-salt-and-sand` (modid `seafarer`) into a new standalone mod `vsmod-progression-framework` (modid `progressionframework`), leaving all Seafarer-specific content in salt-and-sand.

**Architecture:** Files are copied (not moved) into the empty framework repo and rebased to the `ProgressionFramework.*` namespace. The framework scans asset paths across all loaded mod domains. Hard-coded enums for quest rewards/objectives and training XP kinds are converted into string-keyed extension registries, with built-in handlers registered at framework `Start()`. Storage keys move to the `progressionframework:` namespace (clean break — no save-data migration). Salt-and-sand gains a modinfo hard-dependency and ProjectReference to the framework, its duplicate code is deleted, and its content JSON stays put (content-specific keys are unchanged; a single training-xp.json relocation is the only content change).

**Tech Stack:** Vintage Story modding API, .NET 10.0, Harmony (`0Harmony.dll`), Newtonsoft.Json, `dotnet build`.

**Two source repos involved:**
- Source: `/mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/` (Seafarer, modid `seafarer`)
- Target: `/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/` (modid `progressionframework`)

**Spec:** `docs/superpowers/specs/2026-04-14-progression-framework-extraction-design.md`

**Testing strategy:** Neither repo has a unit-test harness. Validation is `dotnet build` (catches type/namespace errors) plus a manual end-to-end smoke test matrix run inside Vintage Story before declaring the extraction done. Where behavior-equivalence across the refactor matters (extension registry dispatch), the plan calls out explicit grep/diff assertions.

---

## Prerequisites

- `$VINTAGE_STORY` env var points to the VS install (already known: `/mnt/c/Users/alexa/AppData/Roaming/Vintagestory`)
- Build command template: `export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory" && dotnet build <csproj>`
- Framework repo is on branch `1.0.0` and already contains a template `ProgressionFrameworkModSystem.cs` plus a valid `.csproj` (with VS refs) — we keep and edit the csproj, replace the ModSystem stub.
- Do not touch salt-and-sand's `modinfo.json` or files outside the listed paths until the framework-side phases are complete.

---

## Phase 0: Framework project scaffolding

Goal: framework builds cleanly with an empty (stub) `ProgressionFrameworkModSystem` and the target directory layout in place.

### Task 0.1: Update framework modinfo

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/modinfo.json`

- [ ] **Step 1: Replace modinfo contents**

Write exactly:

```json
{
    "$schema": "https://moddbcdn.vintagestory.at/schema/modinfo.latest.json",
    "type": "Code",
    "modid": "progressionframework",
    "name": "Progression Framework",
    "authors": [
        "TechnoGoth",
        "LiteraryJester"
    ],
    "description": "Training, quests, and evolving traders as a reusable framework for other mods.",
    "version": "1.0.0",
    "dependencies": {
        "game": "1.22.0-rc.7"
    }
}
```

- [ ] **Step 2: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/modinfo.json
git commit -m "Update modinfo: name, authors, description for ProgressionFramework"
```

### Task 0.2: Create subsystem directory structure

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Training/` (directory)
- Create: `ProgressionFramework/ProgressionFramework/Training/Client/` (directory)
- Create: `ProgressionFramework/ProgressionFramework/Quests/` (directory)
- Create: `ProgressionFramework/ProgressionFramework/Trading/` (directory)
- Create: `ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/` (directory)

- [ ] **Step 1: Create placeholder directories**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework
mkdir -p Training/Client Quests Trading assets/progressionframework/lang
```

- [ ] **Step 2: Seed an empty lang file**

Create `assets/progressionframework/lang/en.json`:

```json
{
}
```

- [ ] **Step 3: Verify existing csproj already includes content globbing**

Read `ProgressionFramework/ProgressionFramework/ProgressionFramework.csproj`. Confirm it has the `<Content Include="modinfo.json">` stanza. No edit needed; asset files are picked up automatically because the csproj uses SDK-style defaults.

- [ ] **Step 4: Build to confirm still compiles**

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/Training ProgressionFramework/ProgressionFramework/Quests ProgressionFramework/ProgressionFramework/Trading ProgressionFramework/ProgressionFramework/assets
git commit -m "Scaffold subsystem folders for framework extraction"
```

---

## Phase 1: Training subsystem extraction

Goal: all training code lives in `ProgressionFramework/Training/` under `ProgressionFramework.Training` namespace, built into the framework DLL. Salt-and-sand is not yet touched.

### Task 1.1: Copy training files

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Training/TrainingSystem.cs` (copied from salt-and-sand)
- Create: `ProgressionFramework/ProgressionFramework/Training/TrainingData.cs`
- Create: `ProgressionFramework/ProgressionFramework/Training/ItemTrainingBook.cs`
- Create: `ProgressionFramework/ProgressionFramework/Training/CraftingXpPatch.cs`
- Create: `ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs`

- [ ] **Step 1: Copy the five source files**

```bash
SRC=/mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/Training
DST=/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Training
cp "$SRC/TrainingSystem.cs" "$DST/TrainingSystem.cs"
cp "$SRC/TrainingData.cs" "$DST/TrainingData.cs"
cp "$SRC/ItemTrainingBook.cs" "$DST/ItemTrainingBook.cs"
cp "$SRC/CraftingXpPatch.cs" "$DST/CraftingXpPatch.cs"
cp "$SRC/GuiDialogTrainingLedger.cs" "$DST/Client/GuiDialogTrainingLedger.cs"
```

- [ ] **Step 2: Rebase namespace on all five files**

Run an in-place replacement:

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Training
sed -i 's|namespace SaltAndSand\.Training|namespace ProgressionFramework.Training|g' \
    TrainingSystem.cs TrainingData.cs ItemTrainingBook.cs CraftingXpPatch.cs Client/GuiDialogTrainingLedger.cs
```

Verify:

```bash
grep -n "namespace " TrainingSystem.cs TrainingData.cs ItemTrainingBook.cs CraftingXpPatch.cs Client/GuiDialogTrainingLedger.cs
```

Expected: every hit is `namespace ProgressionFramework.Training` (note `ItemTrainingBook.cs` uses the `namespace X { ... }` block form, so the replacement hits line 8 `namespace ProgressionFramework.Training`).

- [ ] **Step 3: Build**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors. (The stub `ProgressionFrameworkModSystem` doesn't yet register any of these classes — that's Phase 7.)

- [ ] **Step 4: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/Training
git commit -m "Copy training subsystem into framework with rebased namespace"
```

### Task 1.2: Migrate training storage key & lang prefixes

The copied code still references `"seafarer-training"` as its WatchedAttributes tree key and `"seafarer:..."` lang keys. Per the spec, framework-owned storage keys move to `progressionframework:*`. Content-facing lang keys (profession/training/book names, stay in salt-and-sand data) continue to use `seafarer:…` — but purely UI chrome strings (`training-ledger-title`, `training-levelup`, `training-complete`, `trainingbook-nosource`, `heldhelp-readtolearn`, `training-title-<trait>`) move to the framework's own lang file under `progressionframework:*`.

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Training/TrainingSystem.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Training/ItemTrainingBook.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs`
- Modify: `ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/en.json`

- [ ] **Step 1: Change the training tree storage key**

In `TrainingSystem.cs`, find:

```csharp
public const string TrainingTreeKey = "seafarer-training";
```

Replace with:

```csharp
public const string TrainingTreeKey = "progressionframework:training";
```

- [ ] **Step 2: Change UI chrome lang keys in TrainingSystem**

In `TrainingSystem.cs` around line 139-142:

```csharp
string title = Lang.Get("seafarer:training-title-" + nextLevel.Trait,
    nextLevel.Title);
sapi?.SendMessage(player, 0,
    Lang.Get("seafarer:training-levelup", title),
    EnumChatType.Notification);
```

Replace with:

```csharp
string title = Lang.Get("progressionframework:training-title-" + nextLevel.Trait,
    nextLevel.Title);
sapi?.SendMessage(player, 0,
    Lang.Get("progressionframework:training-levelup", title),
    EnumChatType.Notification);
```

- [ ] **Step 3: Change UI chrome lang keys in ItemTrainingBook**

In `ItemTrainingBook.cs` change these two lines:

```csharp
player.SendIngameError("notrainingbook", Lang.Get("seafarer:trainingbook-nosource"));
// ...
ActionLangCode = "seafarer:heldhelp-readtolearn",
```

to:

```csharp
player.SendIngameError("notrainingbook", Lang.Get("progressionframework:trainingbook-nosource"));
// ...
ActionLangCode = "progressionframework:heldhelp-readtolearn",
```

- [ ] **Step 4: Change UI chrome lang keys in GuiDialogTrainingLedger**

In `Client/GuiDialogTrainingLedger.cs`:

Replace:
```csharp
public override string ToggleKeyCombinationCode => "seafarertrainingledger";
```
with:
```csharp
public override string ToggleKeyCombinationCode => "progressionframeworktrainingledger";
```

Replace:
```csharp
var composer = capi.Gui.CreateCompo("seafarertrainingledger", dialogBounds)
```
with:
```csharp
var composer = capi.Gui.CreateCompo("progressionframeworktrainingledger", dialogBounds)
```

Replace:
```csharp
.AddDialogTitleBar(Lang.Get("seafarer:training-ledger-title"), () => TryClose())
```
with:
```csharp
.AddDialogTitleBar(Lang.Get("progressionframework:training-ledger-title"), () => TryClose())
```

Replace all remaining `Lang.Get("seafarer:profession-"`, `Lang.Get("seafarer:training-"`, `Lang.Get("seafarer:training-complete")` occurrences with `progressionframework:` prefix — these are all UI chrome owned by the ledger GUI.

Verify no `seafarer:` references remain in the file:

```bash
grep -n "seafarer" /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs
```

Expected: no hits.

- [ ] **Step 5: Seed framework lang file with the chrome keys (English)**

Overwrite `ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/en.json`:

```json
{
    "training-ledger-title": "Training Ledger",
    "training-levelup": "You reached: {0}",
    "training-complete": "(Max level)",
    "trainingbook-nosource": "This book has no registered training.",
    "heldhelp-readtolearn": "Read to learn"
}
```

Notes:
- `training-title-<trait>` and `training-<code>` / `profession-<code>` keys remain content-owned in salt-and-sand's `seafarer:lang/en.json` — these are looked up from inside the framework code, but the prefix lives in the salt-and-sand lang file. Wait: the framework code now passes `progressionframework:profession-<code>` — but those entries live in salt-and-sand's lang file under `seafarer:profession-<code>`. This is a contradiction that needs resolving before moving on — see Step 6.

- [ ] **Step 6: Decide chrome-vs-content lang split and apply it consistently**

The lookups in `GuiDialogTrainingLedger` and `TrainingSystem` use two classes of strings:

**Chrome (framework-owned, moves to `progressionframework:`):**
- `training-ledger-title`
- `training-levelup`
- `training-complete`
- `trainingbook-nosource`
- `heldhelp-readtolearn`

**Content (salt-and-sand-owned, stays `seafarer:`):**
- `training-title-<trait>` (per-trait level-up title, e.g. `training-title-bearhunter-novice`)
- `training-<code>` (per-training display name)
- `profession-<code>` (per-profession display name)

Revert the content keys back to `seafarer:` in code. In `TrainingSystem.cs`, change:

```csharp
string title = Lang.Get("progressionframework:training-title-" + nextLevel.Trait, nextLevel.Title);
```

to:

```csharp
string title = Lang.Get("seafarer:training-title-" + nextLevel.Trait, nextLevel.Title);
```

In `Client/GuiDialogTrainingLedger.cs`, change:

```csharp
string profName = Lang.Get("progressionframework:profession-" + prof.Code);
// ...
string trainingName = Lang.Get("progressionframework:training-" + training.Code);
```

back to:

```csharp
string profName = Lang.Get("seafarer:profession-" + prof.Code);
// ...
string trainingName = Lang.Get("seafarer:training-" + training.Code);
```

This leaves the framework's code calling content-mod lang keys, which is the right thing: the framework has no idea what trainings exist — only content mods do.

After this step, re-grep for `seafarer` in the framework Training folder; remaining hits should only be `seafarer:profession-`, `seafarer:training-`, and `seafarer:training-title-` content lookups. Leave these as-is: the framework calls content-mod-owned lang keys on purpose (the framework has no idea which trainings or professions exist; those are content). This is not a leak of the old namespace — it's a deliberate boundary.

- [ ] **Step 7: Build**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add -A ProgressionFramework/ProgressionFramework/
git commit -m "Migrate training chrome keys to progressionframework namespace"
```

### Task 1.3: Generalize training asset loading (scan all domains)

Today `TrainingSystem.LoadConfigs` reads three hard-coded `seafarer:` asset locations. The framework must instead scan every mod's `config/training/` directory and merge the results, so any mod can contribute professions / XP sources / config.

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Training/TrainingSystem.cs`

- [ ] **Step 1: Rewrite LoadConfigs to scan all domains**

Replace the entire `LoadConfigs` method (currently lines ~40 to ~103) with the following. Keep the `AssetsFinalize` call site untouched.

```csharp
private void LoadConfigs(ICoreAPI api)
{
    Professions = new List<Profession>();
    XpSources = new List<TrainingXpSource>();
    Config = new TrainingConfig();

    // Scan every mod domain for config/training/*.json.
    // Convention:
    //   <domain>:config/training/professions.json   -> appended to Professions
    //   <domain>:config/training/xp.json            -> appended to XpSources
    //   <domain>:config/training/config.json        -> LAST writer wins (global toggles)
    //   <domain>:config/training/*.json (any other) -> inspected by top-level key:
    //       { "professions": [...] } / { "xpSources": [...] } / { "config": {...} }
    //       or a bare array (treated as XP sources for back-compat)
    var assets = api.Assets.GetMany("config/training/");
    foreach (var asset in assets)
    {
        try
        {
            var token = JToken.Parse(asset.ToText());
            string fileName = asset.Location.Path.Split('/').Last();

            if (token is JArray arr)
            {
                // Bare array — treat filename as hint
                if (fileName.Equals("professions.json", StringComparison.OrdinalIgnoreCase))
                {
                    Professions.AddRange(arr.ToObject<List<Profession>>() ?? new());
                }
                else
                {
                    // Default bare-array treatment: XP sources
                    XpSources.AddRange(arr.ToObject<List<TrainingXpSource>>() ?? new());
                }
            }
            else if (token is JObject obj)
            {
                if (obj["professions"] is JArray profs)
                    Professions.AddRange(profs.ToObject<List<Profession>>() ?? new());
                if (obj["xpSources"] is JArray xps)
                    XpSources.AddRange(xps.ToObject<List<TrainingXpSource>>() ?? new());
                if (obj["config"] is JObject cfg)
                    Config = cfg.ToObject<TrainingConfig>() ?? Config;
                // Legacy: filename "config.json" with top-level TrainingConfig shape
                if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase)
                    && obj["showAllTrainings"] != null)
                {
                    Config = obj.ToObject<TrainingConfig>() ?? Config;
                }
            }
        }
        catch (Exception e)
        {
            api.Logger.Error("[Training] Failed to load {0}: {1}", asset.Location, e.Message);
        }
    }

    // Build indexes
    TrainingsByCode.Clear();
    ProfessionByTrainingCode.Clear();
    foreach (var prof in Professions)
    {
        foreach (var training in prof.Trainings)
        {
            TrainingsByCode[training.Code] = training;
            ProfessionByTrainingCode[training.Code] = prof;
        }
    }

    api.Logger.Notification("[Training] Loaded {0} professions, {1} trainings, {2} XP sources from {3} file(s)",
        Professions.Count, TrainingsByCode.Count, XpSources.Count, assets.Count());
}
```

Add `using System.Linq;` and `using System.Collections.Generic;` to the file if they're not already present — they are.

- [ ] **Step 2: Build**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/Training/TrainingSystem.cs
git commit -m "Training: scan config/training/ across all mod domains"
```

### Task 1.4: Extract IXpSourceHandler extension interface

Convert today's implicit `source.Type` switch inside `GetCraftXpSources`, `GetDialogueXpSource`, `GetBookXpSource` into a string-keyed registry. Built-in handlers (`craft`, `dialogue`, `book`) are registered by the framework's own `Start()` — behavior stays identical.

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Training/IXpSourceHandler.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Training/TrainingSystem.cs`

- [ ] **Step 1: Create the handler interface**

Write `Training/IXpSourceHandler.cs`:

```csharp
using Vintagestory.API.Server;

namespace ProgressionFramework.Training;

/// <summary>
/// Resolves XP sources of a specific <see cref="TrainingXpSource.Type"/> into
/// actual XP awards. Built-in kinds: "craft", "dialogue", "book".
/// Third-party mods can register new kinds via
/// <see cref="TrainingSystem.RegisterXpSourceKind"/>.
/// </summary>
public interface IXpSourceHandler
{
    /// <summary>
    /// True if <paramref name="source"/> matches the current event and should
    /// trigger an XP award. <paramref name="eventData"/> is kind-specific:
    /// - "craft": outputCode (string)
    /// - "dialogue": triggerName (string)
    /// - "book": itemCode (string)
    /// </summary>
    bool Matches(TrainingXpSource source, object eventData);
}
```

- [ ] **Step 2: Add the registry + replace matching helpers in TrainingSystem**

In `TrainingSystem.cs`, add a private field and a `Register` method. Near the top of the class body (after the existing public properties):

```csharp
private readonly Dictionary<string, IXpSourceHandler> xpHandlers = new(StringComparer.OrdinalIgnoreCase);

public void RegisterXpSourceKind(string kind, IXpSourceHandler handler)
{
    if (xpHandlers.ContainsKey(kind))
        sapi?.Logger.Warning("[Training] XP source kind '{0}' re-registered; overwriting.", kind);
    xpHandlers[kind] = handler;
}
```

Replace the existing `GetCraftXpSources`, `GetDialogueXpSource`, `GetBookXpSource` methods (if any — look at the file) with a single general lookup:

```csharp
/// <summary>
/// Enumerates every XP source whose registered handler matches
/// <paramref name="eventData"/> for the given source kind.
/// </summary>
public IEnumerable<TrainingXpSource> FindMatchingSources(string kind, object eventData)
{
    if (!xpHandlers.TryGetValue(kind, out var handler))
        yield break;

    foreach (var source in XpSources)
    {
        if (!source.Type.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;
        if (handler.Matches(source, eventData)) yield return source;
    }
}
```

Keep the three legacy methods as thin shims so existing call sites don't need updating yet — we migrate them in the next step.

```csharp
public IEnumerable<TrainingXpSource> GetCraftXpSources(string outputCode) => FindMatchingSources("craft", outputCode);
public TrainingXpSource? GetDialogueXpSource(string triggerName) => FindMatchingSources("dialogue", triggerName).FirstOrDefault();
public TrainingXpSource? GetBookXpSource(string itemCode) => FindMatchingSources("book", itemCode).FirstOrDefault();
```

- [ ] **Step 3: Add built-in handlers as private types inside TrainingSystem.cs**

Below the `TrainingSystem` class (still inside the namespace), add:

```csharp
internal sealed class CraftXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        if (eventData is not string outputCode) return false;
        if (source.Recipe == null) return false;

        if (source.Recipe.Contains('*'))
        {
            string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(source.Recipe).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(outputCode, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return source.Recipe.Equals(outputCode, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class DialogueXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        return eventData is string trigger
            && !string.IsNullOrEmpty(source.Trigger)
            && source.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BookXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        return eventData is string itemCode
            && !string.IsNullOrEmpty(source.Item)
            && source.Item.Equals(itemCode, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 4: Register built-ins during Start**

Add to `TrainingSystem.Start(ICoreAPI)` (overriding the base; if the method doesn't currently exist, add it alongside `StartServerSide`):

```csharp
public override void Start(ICoreAPI api)
{
    base.Start(api);
    RegisterXpSourceKind("craft", new CraftXpHandler());
    RegisterXpSourceKind("dialogue", new DialogueXpHandler());
    RegisterXpSourceKind("book", new BookXpHandler());
}
```

- [ ] **Step 5: Build**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/Training/
git commit -m "Training: extract IXpSourceHandler registry with built-in craft/dialogue/book handlers"
```

---

## Phase 2: Quest subsystem extraction

### Task 2.1: Copy quest files

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`
- Create: `ProgressionFramework/ProgressionFramework/Quests/Quest.cs`
- Create: `ProgressionFramework/ProgressionFramework/Quests/QuestDialogTriggers.cs`
- Create: `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs`

- [ ] **Step 1: Copy the four source files**

```bash
SRC=/mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/quests
DST=/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Quests
cp "$SRC/QuestSystem.cs" "$DST/QuestSystem.cs"
cp "$SRC/Quest.cs" "$DST/Quest.cs"
cp "$SRC/QuestDialogTriggers.cs" "$DST/QuestDialogTriggers.cs"
cp "$SRC/QuestRewardHandler.cs" "$DST/QuestRewardHandler.cs"
```

- [ ] **Step 2: Rebase namespace**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Quests
sed -i 's|namespace SaltAndSand\.Quests|namespace ProgressionFramework.Quests|g' *.cs
```

Quest files reference `SaltAndSand.Training.TrainingSystem` and `SaltAndSand.EntityEvolvingTrader`. These references still resolve to salt-and-sand until Phase 3 copies the trader. Update them preemptively:

```bash
sed -i 's|SaltAndSand\.Training|ProgressionFramework.Training|g' *.cs
sed -i 's|\bSaltAndSand\.EntityEvolvingTrader\b|ProgressionFramework.Trading.EntityEvolvingTrader|g' *.cs
```

Also handle `using` clauses — specifically `QuestRewardHandler.cs` dispatches to `EntityEvolvingTrader` by unqualified name. Grep after the sed:

```bash
grep -n "SaltAndSand\|EntityEvolvingTrader" *.cs
```

Remaining hits should be unqualified `EntityEvolvingTrader` identifiers (no `SaltAndSand.` prefix). Add `using ProgressionFramework.Trading;` to any file that uses `EntityEvolvingTrader` without a namespace qualifier. Files known to use it:
- `QuestRewardHandler.cs` (see `DispatchToTrader` and comment at the top)

Add the using after the existing `using Vintagestory.*;` block:

```csharp
using ProgressionFramework.Trading;
```

- [ ] **Step 3: Build (expected to fail — trader not yet copied)**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: errors pointing at `ProgressionFramework.Trading.EntityEvolvingTrader` being undefined. This is fine — Phase 3 fixes it. **Do not commit a broken build.** Continue directly to Phase 3; both subsystems are committed together.

### Task 2.2: Migrate quest storage keys

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`

- [ ] **Step 1: Update the two storage-key constants**

In `QuestSystem.cs` near the top of the class body:

```csharp
public const string PlayerQuestTreeKey = "seafarer:questlog";
private const string ServerSaveKey = "seafarer:serverQuests";
```

Change to:

```csharp
public const string PlayerQuestTreeKey = "progressionframework:questlog";
private const string ServerSaveKey = "progressionframework:serverquests";
```

- [ ] **Step 2: Update the stale doc-comment references**

Search for `"seafarer:questlog"` inside `<see>` / `<c>` tags in the xmldocs and replace with `progressionframework:questlog`. Example at line ~19:

```csharp
///    tree. Mirrored to player-scope <see cref="VariablesModSystem"/> variables so
```

(No change there — that's fine.) But the adjacent:

```csharp
///  - <b>Player</b>: in the player's <c>WatchedAttributes["seafarer:questlog"]</c>
```

Change to:

```csharp
///  - <b>Player</b>: in the player's <c>WatchedAttributes["progressionframework:questlog"]</c>
```

Also update the Lang.Get key — `seafarer:quest-completed` is a content string (the message referring to completion); leave it as-is since the quest title/message layer is content-facing.

Verify:

```bash
grep -n "seafarer:" /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
```

Remaining hits should be content-facing only (e.g., `seafarer:quest-completed` — which is acceptable because the completion message is user-visible and owned by content). If you prefer, parameterise it later. Not required for this extraction.

### Task 2.3: Generalize quest asset loading

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`

- [ ] **Step 1: Replace the domain-scoped asset load**

Find (around line 67):

```csharp
private void LoadQuests(ICoreAPI api)
{
    QuestsByCode.Clear();
    string modId = Mod.Info.ModID;

    var assets = api.Assets.GetMany("config/quests/", modId);
```

Replace the body to scan all domains and retain each quest's source domain for namespacing fallback:

```csharp
private void LoadQuests(ICoreAPI api)
{
    QuestsByCode.Clear();

    var assets = api.Assets.GetMany("config/quests/");
    int loaded = 0;
    foreach (var asset in assets)
    {
        try
        {
            var token = JToken.Parse(asset.ToText());
            var quest = token.ToObject<Quest>();
            if (quest == null || string.IsNullOrEmpty(quest.Code))
            {
                api.Logger.Warning("[Quest] Skipping malformed quest at {0}", asset.Location);
                continue;
            }

            // Bare codes get prefixed with the *source* asset's domain so cross-mod
            // clashes are impossible. (Previously the system's own ModID was used,
            // which broke when loading quests from a different mod.)
            if (!quest.Code.Contains(':'))
            {
                quest.Code = asset.Location.Domain + ":" + quest.Code;
            }

            if (QuestsByCode.ContainsKey(quest.Code))
            {
                api.Logger.Warning("[Quest] Duplicate quest code '{0}' at {1}; keeping first-loaded.",
                    quest.Code, asset.Location);
                continue;
            }

            QuestsByCode[quest.Code] = quest;
            loaded++;
        }
        catch (Exception e)
        {
            api.Logger.Error("[Quest] Failed to parse {0}: {1}", asset.Location, e.Message);
        }
    }

    api.Logger.Notification("[Quest] Loaded {0} quests", loaded);
}
```

Note: this also fixes the pre-existing bug where `NormalizeCode` used `Mod.Info.ModID` (which is now `progressionframework`) to prefix bare codes — incorrect for cross-mod content. Update `NormalizeCode` (around line 115-125) to use a passed-in domain or be removed. Find:

```csharp
public string NormalizeCode(string code)
{
    if (string.IsNullOrEmpty(code)) return code;
    if (code.Contains(':')) return code;
    return Mod.Info.ModID + ":" + code;
}
```

Delete this method. All call sites should already have fully-qualified codes after the LoadQuests rewrite. Grep to confirm no callers remain:

```bash
grep -n "NormalizeCode" /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Quests/*.cs
```

If callers exist, inline the logic or revisit — the function has no meaningful behavior once quests are always fully qualified at load time.

### Task 2.4: Convert enum dispatch to string-keyed extension registries

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Quests/IQuestObjectiveHandler.cs`
- Create: `ProgressionFramework/ProgressionFramework/Quests/IQuestRewardHandler.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Quests/Quest.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`

- [ ] **Step 1: Create the handler interfaces**

`Quests/IQuestObjectiveHandler.cs`:

```csharp
using Vintagestory.API.Server;

namespace ProgressionFramework.Quests;

/// <summary>
/// Handles the semantics of a quest objective type (e.g. "delivery"). Built-in
/// types: "delivery". Third-party mods register custom types via
/// <see cref="QuestSystem.RegisterObjectiveType"/>.
/// </summary>
public interface IQuestObjectiveHandler
{
    /// <summary>Called when the quest becomes Active for this player.</summary>
    void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective);

    /// <summary>
    /// Attempt to progress the objective in response to an event. The meaning of
    /// <paramref name="eventData"/> depends on the objective type. Returns true
    /// if the objective is now fully complete.
    /// </summary>
    bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData);
}
```

`Quests/IQuestRewardHandler.cs`:

```csharp
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace ProgressionFramework.Quests;

/// <summary>
/// Grants a quest reward to the player. Built-in kinds: "addSellingItem",
/// "giveItem", "awardTrainingXP", "incrementEntityVariable". Register custom
/// kinds via <see cref="QuestSystem.RegisterRewardType"/>.
/// </summary>
public interface IQuestRewardHandler
{
    void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api);
}
```

- [ ] **Step 2: Convert QuestRewardType and QuestObjectiveType from enums to strings**

In `Quest.cs`, delete both enums:

```csharp
public enum QuestObjectiveType { Delivery }
public enum QuestRewardType { AddSellingItem, GiveItem, AwardTrainingXP, IncrementEntityVariable }
```

Change the corresponding fields from enum to `string`:

```csharp
public class QuestObjective
{
    public string Code { get; set; } = "";
    public string? Title { get; set; }
    public string Type { get; set; } = "delivery";   // was QuestObjectiveType Type
    public List<QuestItemRequirement> Items { get; set; } = new();
    public int Required { get; set; } = 1;
    public List<QuestReward> Rewards { get; set; } = new();
}

public class QuestReward
{
    public string Type { get; set; } = "";           // was QuestRewardType Type
    // ... rest of fields unchanged
}
```

Note: JSON serialisation of `"type": "delivery"` etc. continues to deserialise into the new string fields seamlessly. Enum values like `QuestRewardType.AddSellingItem` in JSON were in PascalCase — confirm the content JSON files use **camelCase** (`"addSellingItem"`) by grepping:

```bash
grep -rn '"type":' /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/assets/seafarer/config/quests/
```

Expected: values like `"addSellingItem"`, `"giveItem"`, `"awardTrainingXP"`, `"incrementEntityVariable"`, `"delivery"`. If any files use PascalCase, they'll need a tiny JSON migration — but because Newtonsoft's default enum binding is case-insensitive, the existing files likely use camelCase; Newtonsoft tolerated both. The new string field is strict exact match, so document the canonical form as camelCase and fix any PascalCase offenders.

- [ ] **Step 3: Convert QuestRewardHandler to a registry**

Overwrite `QuestRewardHandler.cs` entirely. The old monolithic dispatcher becomes the registry's built-in handlers plus helper shared methods.

```csharp
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using ProgressionFramework.Trading;

namespace ProgressionFramework.Quests;

/// <summary>Built-in quest reward handlers, registered by <see cref="QuestSystem"/>.</summary>
internal static class BuiltInRewardHandlers
{
    public sealed class AddSellingItem : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
            => DispatchToTrader(npc, player.Entity, "addSellingItem", BuildShopTriggerData(reward));
    }

    public sealed class GiveItem : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
            => GiveItemDirect(reward, player, npc, api);
    }

    public sealed class AwardTrainingXP : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
        {
            DispatchToTrader(npc, player.Entity, "awardTrainingXP", new JsonObject(new JObject
            {
                ["training"] = reward.Training,
                ["xp"] = reward.Xp
            }));
        }
    }

    public sealed class IncrementEntityVariable : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
        {
            DispatchToTrader(npc, player.Entity, "incrementVariable", new JsonObject(new JObject
            {
                ["scope"] = "entity",
                ["name"] = reward.VariableName,
                ["amount"] = reward.Amount,
                ["thresholdValue"] = reward.ThresholdValue,
                ["thresholdVariable"] = reward.ThresholdVariable
            }));
        }
    }

    private static void DispatchToTrader(Entity npc, EntityAgent triggeringEntity, string trigger, JsonObject data)
    {
        if (npc is EntityEvolvingTrader trader)
        {
            trader.InvokeDialogTrigger(triggeringEntity, trigger, data);
        }
    }

    private static void GiveItemDirect(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
    {
        if (reward.ItemCode == null) return;

        var jstack = new JsonItemStack
        {
            Code = new AssetLocation(reward.ItemCode),
            Type = reward.ItemType == "block" ? EnumItemClass.Block : EnumItemClass.Item,
            StackSize = reward.Stacksize > 0 ? reward.Stacksize : 1,
            Attributes = reward.Attributes != null ? new JsonObject(reward.Attributes) : null
        };

        if (!jstack.Resolve(api.World, "quest giveItem reward"))
        {
            api.Logger.Warning("[Quest] giveItem could not resolve '{0}'", reward.ItemCode);
            return;
        }

        var stack = jstack.ResolvedItemstack;
        if (!player.Entity.TryGiveItemStack(stack))
        {
            api.World.SpawnItemEntity(stack, npc.Pos.XYZ);
        }
    }

    private static JsonObject BuildShopTriggerData(QuestReward reward)
    {
        var obj = new JObject
        {
            ["code"] = reward.ItemCode,
            ["type"] = reward.ItemType ?? "item",
            ["stacksize"] = reward.Stacksize,
            ["stock"] = new JObject { ["avg"] = reward.StockAvg, ["var"] = reward.StockVar },
            ["price"] = new JObject { ["avg"] = reward.PriceAvg, ["var"] = reward.PriceVar }
        };
        if (reward.Attributes != null) obj["attributes"] = reward.Attributes;
        return new JsonObject(obj);
    }
}
```

- [ ] **Step 4: Add registries + registration APIs in QuestSystem**

In `QuestSystem.cs`, add private fields + public `Register…` methods + built-in registration in `Start`. Add near the other private fields:

```csharp
private readonly Dictionary<string, IQuestObjectiveHandler> objectiveHandlers =
    new(StringComparer.OrdinalIgnoreCase);
private readonly Dictionary<string, IQuestRewardHandler> rewardHandlers =
    new(StringComparer.OrdinalIgnoreCase);
```

Add:

```csharp
public void RegisterObjectiveType(string type, IQuestObjectiveHandler handler)
{
    if (objectiveHandlers.ContainsKey(type))
        api?.Logger.Warning("[Quest] Objective type '{0}' re-registered; overwriting.", type);
    objectiveHandlers[type] = handler;
}

public void RegisterRewardType(string type, IQuestRewardHandler handler)
{
    if (rewardHandlers.ContainsKey(type))
        api?.Logger.Warning("[Quest] Reward type '{0}' re-registered; overwriting.", type);
    rewardHandlers[type] = handler;
}
```

Add/override `Start(ICoreAPI api)` (if it doesn't already exist — check; if yes, add inside the existing override after `base.Start(api)`):

```csharp
public override void Start(ICoreAPI api)
{
    base.Start(api);
    RegisterRewardType("addSellingItem", new BuiltInRewardHandlers.AddSellingItem());
    RegisterRewardType("giveItem", new BuiltInRewardHandlers.GiveItem());
    RegisterRewardType("awardTrainingXP", new BuiltInRewardHandlers.AwardTrainingXP());
    RegisterRewardType("incrementEntityVariable", new BuiltInRewardHandlers.IncrementEntityVariable());

    RegisterObjectiveType("delivery", new BuiltInObjectiveHandlers.Delivery());
}
```

Replace every call site that used the old `QuestRewardHandler.Apply` static dispatcher with a registry lookup. Search:

```bash
grep -n "QuestRewardHandler\.Apply\|reward\.Type ==\|QuestRewardType\." /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
```

For each hit, replace with:

```csharp
if (rewardHandlers.TryGetValue(reward.Type, out var h))
{
    h.Grant(reward, player, npc, api!);
}
else
{
    api?.Logger.Warning("[Quest] Unknown reward type '{0}' in quest {1}", reward.Type, quest.Code);
}
```

Similarly for objective-type dispatch. In `QuestSystem.cs` around line 305 and line 432:

```csharp
if (objective.Type != QuestObjectiveType.Delivery) ...
```

Change to:

```csharp
if (!objective.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase)) ...
```

**Known issue at this step**: the previous delivery-only shortcut assumed all objectives are Delivery. That shortcut stays for now — future objective types add new registered handlers but Delivery flow is untouched. Document by leaving the `== "delivery"` checks where they guard delivery-specific hand-in code.

- [ ] **Step 5: Add built-in delivery objective handler**

Create a new sibling file — or inline in `QuestRewardHandler.cs` below the other built-ins — `BuiltInObjectiveHandlers`:

```csharp
namespace ProgressionFramework.Quests;

internal static class BuiltInObjectiveHandlers
{
    /// <summary>
    /// Minimal stub: delivery objectives are still progressed via the
    /// hand-in paths inside <see cref="QuestSystem"/>; this handler exists
    /// purely so the registry isn't missing the "delivery" kind.
    /// </summary>
    public sealed class Delivery : IQuestObjectiveHandler
    {
        public void OnObjectiveStarted(
            Vintagestory.API.Server.IServerPlayer player, Quest quest, QuestObjective objective) { }

        public bool TryProgress(
            Vintagestory.API.Server.IServerPlayer player, Quest quest,
            QuestObjective objective, object eventData) => false;
    }
}
```

Place this type inside `QuestRewardHandler.cs` (same file; same namespace), or in its own file `Quests/BuiltInObjectiveHandlers.cs`. Either is fine.

- [ ] **Step 6: Build (still expected to fail — trader missing)**

Expected errors now are only about missing `EntityEvolvingTrader`. Phase 3 resolves them.

### Task 2.5: Commit phase 2 alongside phase 3

The build remains broken until the trader is copied in Phase 3. Do **not** commit until the Phase 3 build goes green. This keeps `main` always-compilable.

---

## Phase 3: Trading subsystem extraction

### Task 3.1: Copy trader files

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Trading/EntityEvolvingTrader.cs`
- Create: `ProgressionFramework/ProgressionFramework/Trading/TradePatch.cs`
- Create: `ProgressionFramework/ProgressionFramework/Trading/DialogueTextPatch.cs`

- [ ] **Step 1: Copy**

```bash
SRC=/mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand
DST=/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Trading
cp "$SRC/EntityEvolvingTrader.cs" "$DST/EntityEvolvingTrader.cs"
cp "$SRC/TradePatch.cs" "$DST/TradePatch.cs"
cp "$SRC/DialogueTextPatch.cs" "$DST/DialogueTextPatch.cs"
```

- [ ] **Step 2: Rebase namespace from `SaltAndSand` to `ProgressionFramework.Trading`**

The three files currently declare `namespace SaltAndSand;` (not `.Trading`). Replace carefully — do not accidentally match `SaltAndSand.Training`.

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/Trading
# Match exactly 'namespace SaltAndSand;' on its own line
sed -i 's|^namespace SaltAndSand;|namespace ProgressionFramework.Trading;|' *.cs
# Cross-subsystem reference in EntityEvolvingTrader.cs: Api.ModLoader.GetModSystem<Training.TrainingSystem>()
sed -i 's|\bTraining\.TrainingSystem\b|ProgressionFramework.Training.TrainingSystem|g' *.cs
```

Verify:

```bash
grep -n "namespace\|SaltAndSand" *.cs
```

Expected: only `namespace ProgressionFramework.Trading;` lines; no remaining `SaltAndSand` references.

- [ ] **Step 3: Build the entire framework project**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors. This is the first phase in which the full extracted code compiles together. If errors persist, most common causes:
- Missing `using ProgressionFramework.Trading;` in a quest file that references `EntityEvolvingTrader` unqualified → add it.
- Missing `using ProgressionFramework.Training;` in `EntityEvolvingTrader.cs` if `sed` didn't match → add `using ProgressionFramework.Training;`.

- [ ] **Step 4: Commit Phase 2 + Phase 3 together**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/Quests ProgressionFramework/ProgressionFramework/Trading
git commit -m "Extract quest + trader subsystems into framework (compiles clean)"
```

---

## Phase 4: Wire ProgressionFrameworkModSystem

Goal: the framework mod system registers all the moved classes and initializes Harmony.

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/ProgressionFrameworkModSystem.cs`

- [ ] **Step 1: Replace the stub mod system with the real one**

Overwrite `ProgressionFrameworkModSystem.cs`:

```csharp
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;
using ProgressionFramework.Trading;

namespace ProgressionFramework
{
    public class ProgressionFrameworkModSystem : ModSystem
    {
        private const string HarmonyId = "progressionframework";
        private Harmony? harmony;
        private GuiDialogTrainingLedger? trainingDialog;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemTrainingBook", typeof(ItemTrainingBook));
            api.RegisterEntity("EntityEvolvingTrader", typeof(EntityEvolvingTrader));

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(ProgressionFrameworkModSystem).Assembly);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            base.Dispose();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("progressionframeworktrainingledger",
                Lang.Get("progressionframework:training-ledger-title"), GlKeys.L,
                HotkeyType.GUIOrOtherControls);

            var trainingSystem = api.ModLoader.GetModSystem<TrainingSystem>();
            trainingDialog = new GuiDialogTrainingLedger(api, trainingSystem);
        }
    }
}
```

Notes:
- `ItemTrainingBook` and `EntityEvolvingTrader` class registrations move here; salt-and-sand must stop calling them.
- The `L` hotkey is kept because it's the training-ledger trigger and it's framework-owned UI.
- `StartServerSide` is omitted because nothing framework-level runs only on the server that isn't already bootstrapped by `Start`.

- [ ] **Step 2: Build**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git add ProgressionFramework/ProgressionFramework/ProgressionFrameworkModSystem.cs
git commit -m "Wire ProgressionFrameworkModSystem: class registrations, Harmony, ledger hotkey"
```

---

## Phase 5: Framework standalone load smoke test

Goal: the framework loads in Vintage Story alone (without salt-and-sand) with no crashes.

### Task 5.1: Launch VS with framework only

- [ ] **Step 1: Confirm the framework's built DLL path**

```bash
ls /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/bin/Debug/Mods/mod/
```

Expected: `ProgressionFramework.dll`, `modinfo.json`, `assets/…`.

- [ ] **Step 2: Launch VS with ONLY the framework loaded**

Start the game pointing at this mod dir via `--addOrigin`. Concretely from WSL you can't launch VS; tell the user to launch it on Windows. Required prompt to user:

> "Please start Vintage Story with the argument `--addOrigin "D:\\Development\\vs\\vsmod-progression-framework\\ProgressionFramework\\ProgressionFramework\\bin\\Debug\\Mods\\mod"` (or use the Mod Manager to add that mod folder). Load into a single-player world. Open the server log (or the in-game F12 console) and report any errors, especially ones mentioning `progressionframework`, missing classes, null references, or asset warnings."

- [ ] **Step 3: Record the outcome**

Accept: zero errors mentioning `progressionframework`. Warnings about missing trader entity definitions are expected (no content mod is loaded). If the log contains an actual exception or class-load failure, stop and fix before proceeding.

- [ ] **Step 4: Commit nothing (smoke-test only)**

---

## Phase 6: Salt-and-sand integration — remove duplicates and depend on framework

Goal: salt-and-sand drops its own copies of the extracted files, adds a project reference to the framework DLL, and declares the framework as a hard modinfo dependency.

All changes in this phase land on a single feature branch in salt-and-sand. Work on the existing branch or cut a new one first:

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand
git status    # ensure clean
git checkout -b extract-progression-framework  # if a clean branch is wanted
```

### Task 6.1: Delete the extracted files from salt-and-sand

**Files:**
- Delete: `SaltAndSand/SaltAndSand/Training/` (entire directory and all 5 .cs files)
- Delete: `SaltAndSand/SaltAndSand/quests/` (entire directory — 4 .cs files AND the design markdown files stay in place; see Step 2)
- Delete: `SaltAndSand/SaltAndSand/EntityEvolvingTrader.cs`
- Delete: `SaltAndSand/SaltAndSand/TradePatch.cs`
- Delete: `SaltAndSand/SaltAndSand/DialogueTextPatch.cs`

- [ ] **Step 1: Delete the training directory**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand
rm -r Training/
```

- [ ] **Step 2: Delete only .cs files under quests/; keep the .md design docs**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/quests
rm Quest.cs QuestDialogTriggers.cs QuestRewardHandler.cs QuestSystem.cs
ls       # should show only .md files now
```

The `quests/*.md` files (character docs, quest-tree, training-system-design) are content documentation — keep them.

- [ ] **Step 3: Delete the trader/trade/dialogue source files**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand
rm EntityEvolvingTrader.cs TradePatch.cs DialogueTextPatch.cs
```

### Task 6.2: Update SaltAndSandModSystem

**Files:**
- Modify: `SaltAndSand/SaltAndSand/SaltAndSandModSystem.cs`

- [ ] **Step 1: Remove framework-owned registrations and imports**

Open `SaltAndSandModSystem.cs`. Remove:

- Line 9: `using SaltAndSand.Training;`
- Line 25: `private GuiDialogTrainingLedger? trainingDialog;`
- Lines inside `Start` that register the moved classes — the exact set:

```csharp
api.RegisterItemClass("ItemTrainingBook", typeof(Training.ItemTrainingBook));
api.RegisterEntity("EntityEvolvingTrader", typeof(EntityEvolvingTrader));
```

Delete both.

- In the client-side startup block (around line 223):

```csharp
api.Input.RegisterHotKey("seafarertrainingledger",
    Lang.Get("seafarer:training-ledger-title"), GlKeys.L,
    HotkeyType.GUIOrOtherControls);

var trainingSystem = api.ModLoader.GetModSystem<TrainingSystem>();
trainingDialog = new GuiDialogTrainingLedger(api, trainingSystem);
```

Delete this entire block — the framework now owns the training ledger hotkey and GUI.

- Harmony init stays — salt-and-sand still has its own patches (griddle, exposure, etc.). The `HarmonyId = "seafarer"` is unique to salt-and-sand and doesn't collide with framework's `progressionframework` id.

- Verify no dangling references:

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand
grep -rn "SaltAndSand\.Training\|SaltAndSand\.Quests\|EntityEvolvingTrader\|TradePatch\|DialogueTextPatch\|GuiDialogTrainingLedger\|TrainingSystem\|QuestSystem\|ItemTrainingBook" SaltAndSand/SaltAndSand/*.cs
```

Any remaining hits → inspect. If salt-and-sand has other code that legitimately calls framework types (for example, if there's a behavior that wakes up when a quest completes), update to `using ProgressionFramework.Training;` / `ProgressionFramework.Quests;` accordingly.

### Task 6.3: Add modinfo dependency

**Files:**
- Modify: `SaltAndSand/SaltAndSand/modinfo.json`

- [ ] **Step 1: Add progressionframework to dependencies**

Edit the `"dependencies"` block so it reads:

```json
"dependencies": {
    "game": "1.22.0-rc.7",
    "progressionframework": "1.0.0"
}
```

### Task 6.4: Add project reference from salt-and-sand to framework

**Files:**
- Modify: `SaltAndSand/SaltAndSand/SaltAndSand.csproj`

- [ ] **Step 1: Add a ProjectReference via absolute path**

Inside the existing `<ItemGroup>` that holds references, add:

```xml
<ProjectReference Include="/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework/ProgressionFramework/ProgressionFramework.csproj">
  <Private>false</Private>
  <ReferenceOutputAssembly>true</ReferenceOutputAssembly>
</ProjectReference>
```

Path notes:
- WSL-style (`/mnt/d/...`) matches how `$(VINTAGE_STORY)` is set in this dev environment. If the user builds from Windows directly instead of WSL, change to `D:\Development\vs\vsmod-progression-framework\ProgressionFramework\ProgressionFramework\ProgressionFramework.csproj`.
- `Private=false` prevents the framework DLL from being copied into salt-and-sand's output — VS loads each mod's DLL from its own mod folder, so salt-and-sand should not bundle the framework.
- `ReferenceOutputAssembly=true` is the default; stated explicitly so future readers don't wonder.

- [ ] **Step 2: Build salt-and-sand**

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand
dotnet build SaltAndSand/SaltAndSand.csproj
```

Expected: 0 errors.

Likely errors and fixes:
- `The type or namespace name 'Training' does not exist`: stale `using SaltAndSand.Training;` still present — remove.
- `EntityEvolvingTrader` unresolved: add `using ProgressionFramework.Trading;`.
- `QuestSystem` / `TrainingSystem` unresolved: add `using ProgressionFramework.Quests;` / `using ProgressionFramework.Training;`.

- [ ] **Step 3: Commit salt-and-sand changes**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand
git add -A SaltAndSand/
git commit -m "Depend on ProgressionFramework; remove extracted subsystems"
```

---

## Phase 7: Content relocation — training JSON

Goal: `assets/seafarer/config/training-xp.json` is renamed to `assets/seafarer/config/training/xp.json` so it lines up with the framework's new `config/training/*.json` scan pattern.

**Files:**
- Rename: `SaltAndSand/SaltAndSand/assets/seafarer/config/training-xp.json` → `SaltAndSand/SaltAndSand/assets/seafarer/config/training/xp.json`

Note: salt-and-sand's repo may also have `config/professions.json` and `config/training-config.json` (referenced by the old asset loader). If present, move them alongside: `training/professions.json`, `training/config.json`. Verify before renaming:

- [ ] **Step 1: Discover existing training-related config files**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand/assets/seafarer/config
ls | grep -iE "training|profession"
```

Expected hits: `training-xp.json`, possibly `training-config.json`, `professions.json`.

- [ ] **Step 2: Create training/ and move files**

```bash
mkdir training
git mv training-xp.json training/xp.json
# If these exist:
[ -f professions.json ] && git mv professions.json training/professions.json
[ -f training-config.json ] && git mv training-config.json training/config.json
```

- [ ] **Step 3: Confirm nothing else references the old paths**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand/SaltAndSand
grep -rn "config/training-xp\|config/professions\|config/training-config" .
```

Expected: the only remaining hits should be inside the old deleted Training code (already removed) or design docs (acceptable; those are historical). If there are asset patches or other JSONs referencing the old paths, update them.

- [ ] **Step 4: Commit**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand
git add -A SaltAndSand/
git commit -m "Relocate training config to assets/seafarer/config/training/ (xp.json)"
```

---

## Phase 8: End-to-end smoke test

All previous phases converge here. The framework plus salt-and-sand should load together and behave identically to the pre-extraction build.

### Task 8.1: Build both mods

- [ ] **Step 1: Build framework**

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
cd /mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: 0 errors.

- [ ] **Step 2: Build salt-and-sand**

```bash
cd /mnt/d/Development/vs/vsmod-salt-and-sand/SaltAndSand
dotnet build SaltAndSand/SaltAndSand.csproj
```

Expected: 0 errors.

### Task 8.2: Manual in-game verification

All steps require the user to launch the game. Prompt explicitly.

- [ ] **Step 1: Launch VS with both mods loaded**

Prompt to user:

> "Please launch Vintage Story with both mod folders added via `--addOrigin` (or via Mod Manager):
>
> 1. `D:\Development\vs\vsmod-progression-framework\ProgressionFramework\ProgressionFramework\bin\Debug\Mods\mod`
> 2. `D:\Development\vs\vsmod-salt-and-sand\SaltAndSand\SaltAndSand\bin\Debug\Mods\mod`
>
> Load (or create) a test world."

- [ ] **Step 2: Verify the training ledger**

> "Press the `L` key. The Training Ledger GUI should open. Confirm:
> - Title bar reads 'Training Ledger'.
> - At least one profession is listed (if training config is loaded).
> - No red errors in the console."

- [ ] **Step 3: Verify an evolving trader spawns**

> "Spawn a trader with `/entity spawn drake` (or find the NPC through worldgen). The entity should render as the Drake trader. Right-click to open his trade dialog. Confirm trade list populates."

- [ ] **Step 4: Verify a quest can be accepted via dialogue**

> "Talk to Drake. Navigate to the quest-offering line for `drake-tradeship`. Accept the quest. Check that the quest appears as Active by trying to progress it (hand in a requirement). Confirm the game console shows no `[Quest]` errors."

- [ ] **Step 5: Verify crafting XP**

> "Craft a recipe that grants XP (e.g. a pie, which maps to `piemaster` training). Confirm the console shows a training level-up notification if thresholds are crossed, or at least that the Training Ledger XP count increments the next time you open it."

- [ ] **Step 6: Verify dependency enforcement**

> "Disable the ProgressionFramework mod while leaving Seafarer enabled. Attempt to load the world. VS should refuse to load Seafarer and report a missing dependency on `progressionframework`."

- [ ] **Step 7: Record results**

Any failure at steps 2–6 → stop, diagnose, file a follow-up task. Otherwise proceed.

### Task 8.3: Final commit — smoke test passed

- [ ] **Step 1: Tag the working state**

```bash
cd /mnt/d/Development/vs/vsmod-progression-framework
git tag -a extraction-smoke-passed -m "Framework+Seafarer load and smoke tests pass after extraction"
cd /mnt/d/Development/vs/vsmod-salt-and-sand
git tag -a extraction-smoke-passed -m "Framework+Seafarer load and smoke tests pass after extraction"
```

---

## Spec coverage self-review

Spec section → plan task:

| Spec section | Covered by |
|---|---|
| Scope & boundaries (what moves / what stays) | Phases 1, 2, 3, 6, 7 |
| Repository layout | Phase 0, Phase 1.1, Phase 2.1, Phase 3.1 |
| Asset discovery (scan all domains) | Task 1.3 (training), Task 2.3 (quests); tradelists unchanged |
| Storage-key & ID migration (clean break) | Task 1.2, Task 2.2; entity class-name stability (Phase 4 registration) |
| Extension API | Task 1.4 (IXpSourceHandler), Task 2.4 (IQuestObjectiveHandler, IQuestRewardHandler) |
| Salt-and-sand side effects | Phase 6 |
| Training-xp.json relocation | Phase 7 |
| Build & validation | Each task ends with `dotnet build`; Phase 5 standalone load; Phase 8 full matrix |
| Smoke-test matrix (4 items in spec) | Phase 5 (framework-alone), Phase 8.2 (all four items) |

No gaps identified.

## Known placeholders / deferred decisions

None — every task has concrete code and commands.

## Execution notes

- Phases 0 through 5 happen entirely in `vsmod-progression-framework`; salt-and-sand is untouched. These phases are naturally sequential and share context.
- Phases 6 through 7 happen entirely in `vsmod-salt-and-sand`. These can ride on the working framework build from Phase 5.
- Phase 8 spans both repos and requires the user in front of the game.
- If the user wants to split execution across sessions, Phase 5 (framework loads alone) is a clean checkpoint.
