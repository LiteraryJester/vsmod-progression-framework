# Quest Objective Alternates

Date: 2026-05-29
Status: Design approved, ready for implementation plan.

## Problem

Quest delivery objectives currently match a single, exact item code. The Potato King quest (`seafarer:potatoking-lastpotato`) asks for `seafarer:seeds-potato`. When Long-Term Food (LTF) is installed, Seafarer's potato is disabled and an existing compat patch *replaces* the accepted code with `pemmican:seeds-potato` — fine on a fresh world, but a player who already has a `seafarer:seeds-potato` from a save started before installing LTF (or who has both because they enabled LTF mid-game) can no longer turn it in.

The framework should let an objective accept multiple item codes (and wildcard patterns) so a single quest definition + a small compat patch covers all permutations without needing exclusive `replace` ops.

Scope: framework change only. The Seafarer-side dialogue patch is described under [Out of scope](#out-of-scope-dialogue-gate) for traceability; the actual edit lives in the Seafarer repo.

## Schema change

Extend `QuestItemRequirement` in `Quests/Quest.cs` with an optional list:

```csharp
public class QuestItemRequirement
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
    /// <summary>Additional item codes accepted in place of <see cref="Item"/>.
    /// Each entry may be an exact code or a VS wildcard pattern (e.g.
    /// "*:seeds-potato", "game:plank-*"). At turn-in time the player's
    /// inventory is searched for any item whose code matches Item OR any
    /// entry here.</summary>
    public List<string> Alternates { get; set; } = new();
}
```

JSON example:

```json
{
  "item": "seafarer:seeds-potato",
  "alternates": ["pemmican:seeds-potato"],
  "quantity": 1
}
```

`Item` stays required (canonical name; drives quest description, logbook display, and the implicit "name this quest knows the seed by"). `Alternates` is optional and absent in every existing quest, so all existing quests keep their current exact-match behavior.

Wildcards are honored in both `Item` and each `Alternates` entry via
`Vintagestory.API.Util.WildcardUtil.Match`. This is the same matcher already
used for `kill` objective `Pattern` strings, so behavior is consistent.

## Matching helper

Add a single static helper in `QuestSystem.cs`:

```csharp
internal static bool RequirementMatches(AssetLocation code, QuestItemRequirement req)
{
    string codeStr = code.ToString();
    if (WildcardUtil.Match(req.Item, codeStr)) return true;
    foreach (var alt in req.Alternates)
        if (WildcardUtil.Match(alt, codeStr)) return true;
    return false;
}
```

`WildcardUtil.Match` falls through to equality on non-wildcard patterns, so exact codes still match correctly. Marked `internal` so both the server-side helpers in `QuestSystem` and the client mirror in the same assembly can share one implementation.

## Server-side inventory helpers

In `QuestSystem.cs`, add requirement-aware versions alongside the existing
single-code methods (which stay in place for backward compatibility — see
[`QuestProgressContext`](#questprogresscontext-extension) below):

```csharp
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

Consumption order is the inventory walk order (hotbar → backpacks → cursor). If a player has both seafarer and LTF potato seeds, whichever the walk reaches first is consumed first. No tie-breaking by `Item` vs `Alternates` membership — both are equally valid.

## `QuestProgressContext` extension

`QuestProgressContext` exposes its inventory helpers as public delegates that third-party objective handlers may already be reading. To avoid breaking those, the new requirement-aware helpers are added alongside the existing string-keyed ones rather than replacing them:

```csharp
public sealed class QuestProgressContext
{
    public ICoreServerAPI Api { get; init; } = null!;
    public ITreeAttribute ObjTree { get; init; } = null!;
    public Func<IServerPlayer, string, int> CountInInventory { get; init; } = null!;
    public Action<IServerPlayer, string, int> TakeFromInventory { get; init; } = null!;
    // NEW — alternates-aware versions, used by built-in Delivery.
    public Func<IServerPlayer, QuestItemRequirement, int> CountReq { get; init; } = null!;
    public Action<IServerPlayer, QuestItemRequirement, int> TakeReq { get; init; } = null!;
    public Entity Npc { get; init; } = null!;
}
```

`QuestSystem.TryDeliver` populates both pairs from the corresponding private methods. External objective handlers using `CountInInventory` / `TakeFromInventory` keep compiling and behave exactly as before (single-code, no alternates).

## Built-in `Delivery.TryProgress`

Switch `BuiltInObjectiveHandlers.Delivery` in `QuestRewardHandler.cs` over to the new delegates:

```csharp
int maxTurnIns = int.MaxValue;
foreach (var req in objective.Items)
{
    if (req.Quantity <= 0) continue;
    int have = ctx.CountReq(player, req);
    int canDo = have / req.Quantity;
    if (canDo < maxTurnIns) maxTurnIns = canDo;
}

int turnIns = Math.Min(maxTurnIns, remainingNeeded);
if (turnIns <= 0) { /* unchanged */ return false; }

foreach (var req in objective.Items)
{
    ctx.TakeReq(player, req, req.Quantity * turnIns);
}
```

Algorithm is unchanged — same "max integer turn-ins given the limiting requirement" math — just sourcing `have` from the alternates-aware pool.

## Client mirror

`QuestSystem.MirrorDeliveryOnClient` runs on the client side after dialog triggers fire and pre-computes the optimistic flat-variable result so the dialog graph's next condition node sees the correct value before the server broadcast arrives.

It currently calls a private `CountInInventoryEntity(EntityPlayer, string)`. Replace that with a requirement-aware version using the same matcher:

```csharp
private static int CountInInventoryEntity(EntityPlayer player, QuestItemRequirement req)
{
    int count = 0;
    player.WalkInventory(slot =>
    {
        var code = slot?.Itemstack?.Collectible?.Code;
        if (code != null && RequirementMatches(code, req))
            count += slot.Itemstack.StackSize;
        return true;
    });
    return count;
}
```

The old string-typed overload has only one caller (`MirrorDeliveryOnClient` itself), so it is removed rather than kept around.

## Migration

* All existing quest JSONs are unchanged. No `alternates` field means an empty list, which means exact-match-on-`item` only — identical to today.
* `seafarer:config/quests/potatoking-lastpotato.json` itself stays unchanged.
* The existing LTF compat patch `assets/seafarer/patches/longtermfood-quest-potatoking.json` is rewritten from `replace`-the-item to `add`-an-alternate:

  ```json
  { "op": "add", "path": "/objectives/0/items/0/alternates",
    "value": ["pemmican:seeds-potato"],
    "file": "seafarer:config/quests/potatoking-lastpotato.json",
    "dependsOn": [{ "modid": "longtermfood" }] }
  ```

The quest's `item` field stays `seafarer:seeds-potato`, so quest log text and any reward-side resolution that uses the canonical code keep working.

## Out of scope: dialogue gate

The potatoking dialog's "have seed → deliver" branch (`seafarer:config/dialogue/potatoking.json`, component index 3, text index 2) is gated by a vanilla VS `player.inventory` condition that does exact-code matching:

```json
{ "variable": "player.inventory",
  "isValue": "{type: 'item', code: 'seafarer:seeds-potato', stacksize: 1}" }
```

PF does not own that condition. The current LTF patch *replaces* it with the pemmican code, which is what makes the seafarer seed unacceptable even after this PF change lands.

The Seafarer-side change (separate PR, separate repo) updates `longtermfood-quest-potatoking.json` to *add* a parallel text entry checking the LTF code (jumping to the same `lastpotato-deliver` step) rather than replacing the existing one. With LTF installed, both branches appear and either seed unlocks the deliver option. With LTF absent, the patch does nothing and the current single-branch behavior is preserved.

That edit is documented here so the framework-side and content-side changes ship together.

## Testing

PF has no in-process test harness today; this lands as a docs/manual-verification change. Verification plan, run after building both PF and Seafarer:

1. **No LTF, seafarer seed only.** Start the quest, hold `seafarer:seeds-potato`. Dialog shows the deliver branch. Turn in completes the quest. (Regression check — must match current behavior.)
2. **With LTF, LTF seed only.** Start the quest, hold `pemmican:seeds-potato`. Dialog shows the deliver branch. Turn in completes the quest.
3. **With LTF, both seeds in inventory.** Both dialog branches available; turn-in consumes one seed (whichever the inventory walk hits first) and completes the objective. Inventory ends with the other seed intact.
4. **Wildcard sanity check.** Temporarily edit a dev-only objective to `item: "*:seeds-potato"` with no alternates, confirm it still matches the same set of items. (Not committed; smoke test only.)

## Affected files

Framework (this PR):

* `ProgressionFramework/ProgressionFramework/Quests/Quest.cs` — add `Alternates` to `QuestItemRequirement`.
* `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs` — add `RequirementMatches`, requirement-aware `CountInInventory` / `TakeFromInventory`, populate new `CountReq` / `TakeReq` on `QuestProgressContext`, swap `MirrorDeliveryOnClient`'s helper.
* `ProgressionFramework/ProgressionFramework/Quests/QuestRewardHandler.cs` — `QuestProgressContext` gains `CountReq` / `TakeReq`; `Delivery.TryProgress` uses them.

Seafarer (sibling PR in `vsmod-seafarer`):

* `Seafarer/Seafarer/assets/seafarer/patches/longtermfood-quest-potatoking.json` — switch the objective op from `replace` to `add` (targeting `alternates`), and switch the dialogue op from `replace` to a second `add` that inserts a parallel deliver branch.
