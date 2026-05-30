# Quest Objective Alternates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a delivery objective accept multiple item codes (and wildcard patterns) via an optional `Alternates` list on `QuestItemRequirement`, so cross-mod compat patches can additively widen what counts as a turn-in instead of exclusively replacing the accepted code.

**Architecture:** Add `Alternates: List<string>` to `QuestItemRequirement`. Introduce one static matcher `RequirementMatches(AssetLocation, QuestItemRequirement)` shared between server-side count/take helpers and the client-side mirror. Add requirement-aware `CountInInventory` / `TakeFromInventory` overloads on `QuestSystem` and expose them as new `CountReq` / `TakeReq` delegates on `QuestProgressContext` (alongside the existing single-code delegates, which stay for third-party objective handlers). Built-in `Delivery.TryProgress` switches to the new delegates. The Seafarer-side LTF dialogue patch ships in a sibling PR.

**Tech Stack:** C# / .NET 10, Vintagestory API (`WildcardUtil`, `IServerPlayer`, `EntityPlayer.WalkInventory`, `ITreeAttribute`), Newtonsoft.Json.

**Testing note:** PF has no in-process test harness. Per-task verification uses `dotnet build` for compile checks; a final manual in-game checklist (Task 8) covers behavior end-to-end with the sibling Seafarer change.

**Reference spec:** `docs/superpowers/specs/2026-05-29-quest-objective-alternates-design.md`

All build commands run from `/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework`. Export once per shell:

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
```

Build command (used in every task):

```bash
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

---

## File Structure

Modified (PF):

- `ProgressionFramework/ProgressionFramework/Quests/Quest.cs` — add `Alternates` property to `QuestItemRequirement`.
- `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs` — add `RequirementMatches`, add requirement-aware `CountInInventory` / `TakeFromInventory` overloads, populate new context delegates, swap client mirror to the new matcher.
- `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs` — add `CountReq` / `TakeReq` to `QuestProgressContext`; built-in `Delivery.TryProgress` switches to them.

Modified (Seafarer, sibling repo at `/mnt/d/Development/vs/vsmod-seafarer/`):

- `Seafarer/Seafarer/assets/seafarer/patches/longtermfood-quest-potatoking.json` — swap `replace` ops for additive `add` ops (objective alternates + parallel dialogue branch).

No new files; no deletions of files.

---

## Task 1: Add `Alternates` field to `QuestItemRequirement`

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/Quest.cs:37-44`

- [ ] **Step 1: Edit `QuestItemRequirement` to add the `Alternates` list**

Open `ProgressionFramework/ProgressionFramework/Quests/Quest.cs` and replace the `QuestItemRequirement` class (currently at lines 37–44) with:

```csharp
/// <summary>
/// A single item requirement inside a Delivery objective. Quantity is taken per turn-in.
/// </summary>
public class QuestItemRequirement
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;

    /// <summary>Additional item codes accepted in place of <see cref="Item"/>.
    /// Each entry may be an exact code or a VS wildcard pattern (e.g.
    /// <c>"*:seeds-potato"</c>, <c>"game:plank-*"</c>). At turn-in time the
    /// player's inventory is searched for any item whose code matches
    /// <see cref="Item"/> OR any entry here. Empty by default — existing
    /// single-code quests behave exactly as before.</summary>
    public List<string> Alternates { get; set; } = new();
}
```

(`System.Collections.Generic` is already imported at the top of `Quest.cs:1`.)

- [ ] **Step 2: Build to verify the type compiles**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/Quest.cs
git commit -m "feat(quests): add Alternates list to QuestItemRequirement"
```

---

## Task 2: Add `RequirementMatches` helper

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs` (add a private static method)

- [ ] **Step 1: Add the matcher**

Open `QuestSystem.cs`. Find the existing private static `CountInInventory(IServerPlayer, string)` method around line 881. Insert this new method immediately above it (so all the inventory-related helpers stay grouped):

```csharp
    /// <summary>
    /// True if <paramref name="code"/> matches the requirement's
    /// <see cref="QuestItemRequirement.Item"/> or any entry in
    /// <see cref="QuestItemRequirement.Alternates"/>. Each pattern is matched
    /// via <see cref="WildcardUtil.Match(string, string)"/>, which falls
    /// through to equality on non-wildcard inputs — so exact codes still
    /// behave like the legacy single-code matcher.
    /// </summary>
    internal static bool RequirementMatches(AssetLocation code, QuestItemRequirement req)
    {
        string codeStr = code.ToString();
        if (WildcardUtil.Match(req.Item, codeStr)) return true;
        foreach (var alt in req.Alternates)
            if (WildcardUtil.Match(alt, codeStr)) return true;
        return false;
    }
```

(`WildcardUtil` is already imported via `using Vintagestory.API.Util;` at `QuestSystem.cs:11`. `AssetLocation` comes in through `using Vintagestory.API.Common;` at line 6.)

- [ ] **Step 2: Build**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
git commit -m "feat(quests): add RequirementMatches helper for alternates+wildcards"
```

---

## Task 3: Add requirement-aware `CountInInventory` / `TakeFromInventory` overloads

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs:881-915`

- [ ] **Step 1: Append new overloads after the existing string-typed ones**

Find the existing `TakeFromInventory(IServerPlayer, string, int)` method (it ends around line 915, just before the `// State tree helpers — scope-aware` section comment). Insert these two new overloads **immediately after** the closing `}` of `TakeFromInventory(player, string, int)` and **before** the next comment block:

```csharp
    /// <summary>Sums the stack sizes of every inventory slot whose item code
    /// matches <paramref name="req"/> (the requirement's <see cref="QuestItemRequirement.Item"/>
    /// or any of its <see cref="QuestItemRequirement.Alternates"/>). Hotbar + backpacks +
    /// cursor are all walked.</summary>
    private static int CountInInventory(IServerPlayer player, QuestItemRequirement req)
    {
        int count = 0;
        player.Entity.WalkInventory(slot =>
        {
            var code = slot?.Itemstack?.Collectible?.Code;
            if (code != null && RequirementMatches(code, req))
                count += slot.Itemstack.StackSize;
            return true;
        });
        return count;
    }

    /// <summary>Consumes up to <paramref name="quantity"/> items from the player's
    /// inventory whose code matches <paramref name="req"/>. Slots are visited in
    /// the standard walk order (hotbar → backpacks → cursor); whichever matching
    /// stack the walk reaches first is drawn from first. No preference between
    /// <see cref="QuestItemRequirement.Item"/> and <see cref="QuestItemRequirement.Alternates"/>.</summary>
    private static void TakeFromInventory(IServerPlayer player, QuestItemRequirement req, int quantity)
    {
        int remaining = quantity;
        player.Entity.WalkInventory(slot =>
        {
            if (remaining <= 0) return false;
            var code = slot?.Itemstack?.Collectible?.Code;
            if (code == null || !RequirementMatches(code, req)) return true;
            int take = Math.Min(slot.Itemstack.StackSize, remaining);
            slot.TakeOut(take);
            slot.MarkDirty();
            remaining -= take;
            return remaining > 0;
        });
    }
```

The existing `CountInInventory(IServerPlayer, string)` and `TakeFromInventory(IServerPlayer, string, int)` methods stay unchanged — they remain wired to the original delegates on `QuestProgressContext` for backward compatibility (Task 4).

- [ ] **Step 2: Build**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors. (At this point the new overloads exist but are not yet called by anything — Tasks 4–6 wire them up.)

- [ ] **Step 3: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
git commit -m "feat(quests): add requirement-aware Count/Take inventory overloads"
```

---

## Task 4: Add `CountReq` / `TakeReq` to `QuestProgressContext`

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs:200-210`
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs:627-635`

- [ ] **Step 1: Extend `QuestProgressContext`**

Open `QuestRewardHandler.cs`. Find the `QuestProgressContext` class (currently `QuestRewardHandler.cs:200-210`) and replace it with:

```csharp
/// <summary>Shared context the handlers receive for <c>TryProgress</c>. Lets them
/// read/write the objective's state tree and use inventory helpers without
/// duplicating them per handler.</summary>
public sealed class QuestProgressContext
{
    public ICoreServerAPI Api { get; init; } = null!;
    public ITreeAttribute ObjTree { get; init; } = null!;

    // Legacy single-code helpers — kept so existing third-party objective
    // handlers that read these delegates keep compiling. New code (including
    // built-in Delivery) should prefer CountReq / TakeReq below, which honor
    // alternates and wildcards.
    public System.Func<IServerPlayer, string, int> CountInInventory { get; init; } = null!;
    public System.Action<IServerPlayer, string, int> TakeFromInventory { get; init; } = null!;

    /// <summary>Alternates-aware counterpart of <see cref="CountInInventory"/>.
    /// Sums inventory stacks matching the requirement's primary code OR any of
    /// its <c>Alternates</c> (each may be a VS wildcard pattern).</summary>
    public System.Func<IServerPlayer, QuestItemRequirement, int> CountReq { get; init; } = null!;

    /// <summary>Alternates-aware counterpart of <see cref="TakeFromInventory"/>.
    /// Consumes up to <c>quantity</c> matching items in inventory walk order.</summary>
    public System.Action<IServerPlayer, QuestItemRequirement, int> TakeReq { get; init; } = null!;

    public Entity Npc { get; init; } = null!;
}
```

- [ ] **Step 2: Populate the new delegates in `QuestSystem.TryDeliver`**

Open `QuestSystem.cs`. Find the `var ctx = new QuestProgressContext { ... }` block inside `TryDeliver` (currently `QuestSystem.cs:628-635`) and replace it with:

```csharp
        var ctx = new QuestProgressContext
        {
            Api = sapi!,
            ObjTree = objTree,
            CountInInventory = CountInInventory,
            TakeFromInventory = TakeFromInventory,
            CountReq = CountInInventory,
            TakeReq = TakeFromInventory,
            Npc = npc
        };
```

Method-group conversion picks the right overload for each delegate (`Func<IServerPlayer, string, int>` binds to the string-typed `CountInInventory`; `Func<IServerPlayer, QuestItemRequirement, int>` binds to the requirement-typed one).

- [ ] **Step 3: Build**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors. If the compiler complains "ambiguous match" on the method-group binding, it means one of the new overloads from Task 3 wasn't added — re-check Task 3 step 1.

- [ ] **Step 4: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs \
        ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
git commit -m "feat(quests): expose CountReq/TakeReq on QuestProgressContext"
```

---

## Task 5: Built-in `Delivery.TryProgress` uses the new delegates

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs:108-149`

- [ ] **Step 1: Swap the two `req.Item` call sites in `Delivery.TryProgress`**

Find `BuiltInObjectiveHandlers.Delivery.TryProgress` in `QuestRewardHandler.cs` (around lines 112–148). Locate this section near the start:

```csharp
            int maxTurnIns = int.MaxValue;
            foreach (var req in objective.Items)
            {
                if (req.Quantity <= 0) continue;
                int have = ctx.CountInInventory(player, req.Item);
                int canDo = have / req.Quantity;
                if (canDo < maxTurnIns) maxTurnIns = canDo;
            }
```

Replace the inner `int have = ...` line so the loop body reads:

```csharp
            int maxTurnIns = int.MaxValue;
            foreach (var req in objective.Items)
            {
                if (req.Quantity <= 0) continue;
                int have = ctx.CountReq(player, req);
                int canDo = have / req.Quantity;
                if (canDo < maxTurnIns) maxTurnIns = canDo;
            }
```

Further down in the same method, locate this loop:

```csharp
            foreach (var req in objective.Items)
            {
                ctx.TakeFromInventory(player, req.Item, req.Quantity * turnIns);
            }
```

Replace it with:

```csharp
            foreach (var req in objective.Items)
            {
                ctx.TakeReq(player, req, req.Quantity * turnIns);
            }
```

Everything else in `Delivery.TryProgress` stays as-is.

- [ ] **Step 2: Build**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs
git commit -m "feat(quests): Delivery handler uses alternates-aware Count/Take"
```

---

## Task 6: Client mirror uses the new matcher

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs:721-817`

- [ ] **Step 1: Replace `CountInInventoryEntity` with a requirement-aware version**

Find the private static helper `CountInInventoryEntity(EntityPlayer, string)` in `QuestSystem.cs` (currently around lines 807–817):

```csharp
    private static int CountInInventoryEntity(EntityPlayer playerEntity, string itemCode)
    {
        int count = 0;
        var loc = new AssetLocation(itemCode);
        playerEntity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Collectible?.Code == loc) count += slot.Itemstack.StackSize;
            return true;
        });
        return count;
    }
```

Replace the whole method with:

```csharp
    /// <summary>Client-side companion of <see cref="CountInInventory(IServerPlayer, QuestItemRequirement)"/>.
    /// Used by <see cref="MirrorDeliveryOnClient"/> to predict turn-in count
    /// before the server broadcast arrives, so subsequent dialog conditions in
    /// the same frame see a correct flat-variable value.</summary>
    private static int CountInInventoryEntity(EntityPlayer playerEntity, QuestItemRequirement req)
    {
        int count = 0;
        playerEntity.WalkInventory(slot =>
        {
            var code = slot?.Itemstack?.Collectible?.Code;
            if (code != null && RequirementMatches(code, req))
                count += slot.Itemstack.StackSize;
            return true;
        });
        return count;
    }
```

- [ ] **Step 2: Update the single call site in `MirrorDeliveryOnClient`**

Still in `QuestSystem.cs`, find the delivery loop inside `MirrorDeliveryOnClient` (around line 742):

```csharp
            int maxTurnIns = int.MaxValue;
            foreach (var req in objective.Items)
            {
                if (req.Quantity <= 0) continue;
                int have = CountInInventoryEntity(playerEntity, req.Item);
                int canDo = have / req.Quantity;
                if (canDo < maxTurnIns) maxTurnIns = canDo;
            }
```

Change the `int have = ...` line to pass the requirement instead of `req.Item`:

```csharp
            int maxTurnIns = int.MaxValue;
            foreach (var req in objective.Items)
            {
                if (req.Quantity <= 0) continue;
                int have = CountInInventoryEntity(playerEntity, req);
                int canDo = have / req.Quantity;
                if (canDo < maxTurnIns) maxTurnIns = canDo;
            }
```

- [ ] **Step 3: Build**

Run: `dotnet build ProgressionFramework/ProgressionFramework.csproj`
Expected: `Build succeeded`, 0 errors. (If the compiler reports an unused method warning anywhere about the old string-typed helper, double-check Step 1 fully replaced it — there should only be one `CountInInventoryEntity` in the file now.)

- [ ] **Step 4: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
git commit -m "feat(quests): client mirror predicts turn-ins via alternates matcher"
```

---

## Task 7: Update Seafarer LTF compat patch

**Files:**
- Modify: `/mnt/d/Development/vs/vsmod-seafarer/Seafarer/Seafarer/assets/seafarer/patches/longtermfood-quest-potatoking.json`

This is a sibling-repo change — switch shells/directories as needed. Build command for the Seafarer side:

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
dotnet build /mnt/d/Development/vs/vsmod-seafarer/Seafarer/Seafarer/Seafarer.csproj
```

- [ ] **Step 1: Inspect the current dialogue file to find a safe insertion point**

The current dialogue text entries that surround the `lastpotato-deliver` branch live in `Seafarer/Seafarer/assets/seafarer/config/dialogue/potatoking.json`, components index 3, text indices 0–3. The existing "have seed → deliver" entry is at `/components/3/text/2`. We will *append a fourth parallel entry* at `/components/3/text/-` rather than mutating the existing one.

Read the file first to confirm the deliver entry's exact shape (jumpTo target, value lang key) so the new entry mirrors it:

```bash
cat /mnt/d/Development/vs/vsmod-seafarer/Seafarer/Seafarer/assets/seafarer/config/dialogue/potatoking.json
```

Locate the entry at `/components/3/text/2`. It looks like:

```json
{
    "value": "seafarer:dialogue-potatoking-have-seed",
    "jumpTo": "lastpotato-deliver",
    "conditions": [
        { "variable": "global.quest-seafarer-potatoking-lastpotato-status", "isValue": "active" },
        { "variable": "player.inventory", "isValue": "{type: 'item', code: 'seafarer:seeds-potato', stacksize: 1}" }
    ]
}
```

Use these values verbatim in Step 2; the only change is the inventory condition's item code.

- [ ] **Step 2: Replace the patch file with additive `add` ops**

Overwrite `Seafarer/Seafarer/assets/seafarer/patches/longtermfood-quest-potatoking.json` with:

```json
[
    // ==================================================================
    // Long-Term Food compat. The Potato King quest "potatoking-lastpotato"
    // delivers a potato seed. With `longtermfood` installed, LTF's potato
    // seed (pemmican:seeds-potato) is the one the player most likely has,
    // but Seafarer's own seafarer:seeds-potato may also still be in their
    // inventory (e.g. mid-game install). Two additive changes:
    //  1. the quest objective gains pemmican:seeds-potato as an alternate
    //     accepted code (Seafarer's seed stays the primary),
    //  2. the potatoking dialogue gains a parallel "have seed" → deliver
    //     branch keyed on the LTF seed, so the deliver option shows up
    //     whenever the player holds EITHER seed.
    // ==================================================================

    // --- 1. Quest objective: also accept the LTF potato seed ---
    { "op": "add", "path": "/objectives/0/items/0/alternates",
        "value": ["pemmican:seeds-potato"],
        "file": "seafarer:config/quests/potatoking-lastpotato.json",
        "dependsOn": [{ "modid": "longtermfood" }] },

    // --- 2. Dialogue: parallel deliver branch for the LTF seed ---
    { "op": "add", "path": "/components/3/text/-",
        "value": {
            "value": "seafarer:dialogue-potatoking-have-seed",
            "jumpTo": "lastpotato-deliver",
            "conditions": [
                { "variable": "global.quest-seafarer-potatoking-lastpotato-status", "isValue": "active" },
                { "variable": "player.inventory", "isValue": "{type: 'item', code: 'pemmican:seeds-potato', stacksize: 1}" }
            ]
        },
        "file": "seafarer:config/dialogue/potatoking.json",
        "dependsOn": [{ "modid": "longtermfood" }] }
]
```

Note: `path: "/components/3/text/-"` is the JSON Patch idiom for "append to the end of the array".

- [ ] **Step 3: Build Seafarer**

Run: `dotnet build /mnt/d/Development/vs/vsmod-seafarer/Seafarer/Seafarer/Seafarer.csproj`
Expected: `Build succeeded`, 0 errors. (The build does not validate JSON patch correctness — that's verified in Task 8.)

- [ ] **Step 4: Commit (in the Seafarer repo)**

```bash
cd /mnt/d/Development/vs/vsmod-seafarer
git add Seafarer/Seafarer/assets/seafarer/patches/longtermfood-quest-potatoking.json
git commit -m "fix(longtermfood-compat): accept either potato seed for Potato King quest

Switches the LTF compat patch from exclusive 'replace' ops to additive
'add' ops: the objective gains pemmican:seeds-potato as an alternate,
and the potatoking dialogue gains a parallel deliver branch for the LTF
seed. Requires the alternates-aware quest objective change in
ProgressionFramework 1.1+."
```

---

## Task 8: Manual in-game verification

**Files:**
- (none — runtime verification only)

This task documents the pass/fail criteria that close out the change. PF has no in-process test harness; running the game with both built mods is the only way to confirm behavior.

Setup before each scenario: launch the world, talk to the Potato King NPC to start the quest if it isn't already active.

- [ ] **Step 1: Scenario A — no LTF, seafarer seed only (regression check)**

Run a world with **only the PF + Seafarer mods** loaded (LTF disabled in the Mod Manager). Give yourself a `seafarer:seeds-potato` via creative inventory or harvest. Talk to the Potato King.

Expected:
- The "I have a seed" deliver branch appears in the dialogue.
- Selecting it consumes the seed and completes the quest objective.
- Quest log marks the quest completed.

If this fails, the framework change has regressed the single-code path — diff `Quest.cs` and `QuestRewardHandler.cs` against the spec.

- [ ] **Step 2: Scenario B — with LTF, LTF seed only**

Enable LTF alongside PF + Seafarer; rebuild Seafarer if not already built with Task 7's patch active. Start a fresh world (or use a save where the seafarer potato variants are disabled by LTF as usual). Give yourself a `pemmican:seeds-potato`.

Expected:
- The "I have a seed" deliver branch appears.
- Turn-in consumes the LTF seed and completes the objective.

- [ ] **Step 3: Scenario C — with LTF, both seeds in inventory**

Same setup as Scenario B, but place one `seafarer:seeds-potato` and one `pemmican:seeds-potato` in inventory (use creative if needed).

Expected:
- The deliver branch appears (driven by either dialogue condition).
- Selecting it consumes exactly one seed — whichever the inventory walk reaches first (hotbar slot order); the other seed remains untouched in inventory.
- Objective completes.

- [ ] **Step 4: Scenario D — wildcard smoke test (uncommitted)**

Temporarily edit `Seafarer/Seafarer/assets/seafarer/config/quests/potatoking-lastpotato.json` to:

```json
{ "item": "*:seeds-potato", "quantity": 1 }
```

(Remove the alternates field entirely for this test.) Reload assets / restart the world. Repeat Scenario A and Scenario B. Both should still complete — the wildcard matches both `seafarer:seeds-potato` and `pemmican:seeds-potato`.

After the test, revert the file:

```bash
cd /mnt/d/Development/vs/vsmod-seafarer
git checkout -- Seafarer/Seafarer/assets/seafarer/config/quests/potatoking-lastpotato.json
```

Do **not** commit any change from this scenario.

- [ ] **Step 5: Final sign-off**

If Scenarios A–D pass, the framework + compat change is complete. Tag the PF commit chain in the PR description, link the Seafarer commit from Task 7, and note the verified scenarios.
