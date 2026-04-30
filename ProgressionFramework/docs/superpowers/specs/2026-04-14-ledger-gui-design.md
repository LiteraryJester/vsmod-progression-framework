# Ledger GUI — Design

Combine the existing Training ledger and a new Quest Log into a single tabbed dialog, fix the flaky `L` hotkey, and sync server-scope quest state to clients so players can see shared progress.

## Goals

- One dialog, opened by pressing `L`, with two tabs: **Training** (migrated) and **Quest Log** (new).
- Fix the known issue in `CLAUDE.md` where the `L` hotkey doesn't reliably open the dialog.
- Quest Log shows Active + Completed quests only, grouped by `Quest.GroupLangKey`, each entry expanded by default.
- Server-scope quest state reaches the client so shared quests render with live progress.

## Non-goals (v1)

- Available / Unavailable / Disabled quests in the log. Players discover quests through dialogue, not a browsing UI.
- Per-objective-type custom renderers for third-party `IQuestObjectiveHandler`s. Non-`delivery` objectives use a generic `progress / required` bar.
- Tick throttling of the server→client quest update channel. Events are human-paced; add throttling only if a future objective type produces real spam.
- Cleanup of stale client-side server-quest entries on world-leave. The next snapshot replaces them wholesale.

## 1. Shell: `GuiDialogLedger`

- Rename `Training/Client/GuiDialogTrainingLedger.cs` → `Client/GuiDialogLedger.cs` (move out of `Training/Client/` since it now spans both subsystems).
- Hotkey id: `progressionframeworktrainingledger` → `progressionframeworkledger`.
- Lang keys:
  - `progressionframework:ledger-title` — "Ledger" (new)
  - `progressionframework:ledger-tab-training` — "Training" (new)
  - `progressionframework:ledger-tab-questlog` — "Quest Log" (new)
  - `progressionframework:questlog-empty` — "No active or completed quests." (new)
  - `progressionframework:questlog-group-other` — "Other" (new)
  - `progressionframework:questlog-status-active` — "Active" (new)
  - `progressionframework:questlog-status-completed` — "Completed" (new)
  - `progressionframework:questlog-shared-subtitle` — "(shared)" (new)
  - Existing `progressionframework:training-ledger-title` is removed — it was internal to this mod and nothing else references it.

### Composition

One `GuiComposer`, rebuilt on open and on tab change. Width raised from 420 → 520 (quest descriptions wrap).

```
┌─ Title: "Ledger"                          ✕ ┐
│  [ Training ]  [ Quest Log ]                 │  ← two AddToggleButton entries
│  ─────────────────────────────────────────── │
│  (active tab body)                           │
└──────────────────────────────────────────────┘
```

- `enum LedgerTab { Training, QuestLog }`, `currentTab` field, default `Training`.
- Clicking the non-active tab sets `currentTab` and calls `ComposeDialog()`.
- `currentTab` persists across close/reopen (field on the dialog instance).

### Hotkey fix

Root cause per `CLAUDE.md`: `StartClientSide` constructs the dialog but never registers it, so the base class has nothing binding `ToggleKeyCombinationCode` to the hotkey. Fix in `ProgressionFrameworkModSystem.StartClientSide`:

```csharp
api.Input.RegisterHotKey("progressionframeworkledger",
    Lang.Get("progressionframework:ledger-title"), GlKeys.L,
    HotkeyType.GUIOrOtherControls);

var ledgerDialog = new GuiDialogLedger(api, trainingSystem, questSystem);
api.Gui.RegisterDialog(ledgerDialog);          // ← the missing call
```

No manual `SetHotKeyHandler` needed — `GuiDialog.ToggleKeyCombinationCode` wires it up once the dialog is registered.

## 2. Training tab

Migrated from `GuiDialogTrainingLedger.ComposeDialog` into `GuiDialogLedger.ComposeTrainingTab(composer, ref y)`. Rendering is unchanged. The `TrainingSystem` attribute listener (`RegisterModifiedListener` on `TrainingSystem.TrainingTreeKey`) still recomposes the dialog if open and on the Training tab.

## 3. Quest Log tab

### Data source

- Player-scope quests: read `player.Entity.WatchedAttributes.GetTreeAttribute("progressionframework:questlog").GetTreeAttribute(quest.Code)`.
- Server-scope quests: read from `QuestSystem.clientServerQuestState[quest.Code]` (see §4).
- Both paths produce a `TreeAttribute` with the same layout (status + per-objective counters).

### `QuestSystem` helpers (scope + side aware)

The existing `GetStatus(IServerPlayer?, ...)` and `GetObjectiveProgress(IServerPlayer?, ...)` are server-typed. They get renamed and generalized to `IPlayer?` so the client can call them:

```csharp
public QuestStatus GetStatus(IPlayer? player, string questCode);
public int GetObjectiveProgress(IPlayer? player, string questCode, string objectiveCode);
public bool IsObjectiveComplete(IPlayer? player, string questCode, string objectiveCode);
```

Each helper looks up the backing tree via a new private `GetQuestNodeReadonly(IPlayer? player, string code)`. The new reader dispatches on `Quest.Scope`:
- `Player` scope: read from `player.Entity.WatchedAttributes[PlayerQuestTreeKey]`. Works identically on server and client because `WatchedAttributes` is a synced tree.
- `Server` scope: server reads from `serverQuestState`; client reads from `clientServerQuestState`. A small `api.Side` check decides.

All existing call sites keep working — `IServerPlayer : IPlayer`, so the parameter widening is source-compatible.

### Layout and filtering

- Show only `QuestStatus.Active` and `QuestStatus.Completed`.
- Group by `Lang.Get(quest.GroupLangKey)` when set, else `progressionframework:questlog-group-other`. Groups sorted alphabetically by resolved name; "Other" pinned to the bottom.
- Within a group: Active before Completed; ties broken by resolved `TitleLangKey`.
- Empty state: centered `progressionframework:questlog-empty` text when no entries survive filtering.

### Per-quest rendering

Expanded by default (brainstorm decision B). Example:

```
▸ Drake's Tradeship                               Active
  Drake needs a sturdy vessel to resume trade…
    • Deliver varnish (18 resin, 6 fat)
      ░░░░░░░░░░  0 / 1
    • Deliver timber (1 plank each)
      ▓▓▓▓░░░░░░  3 / 12
```

- Title: `Lang.Get(quest.TitleLangKey)` in bold.
- Description: `Lang.Get(quest.DescriptionLangKey)` as rich text, wrapped to tab width.
- Status badge on the right: `Active` rendered in neutral `#C0C0C0`, `Completed` in `#D4AF37` with a `✓` prefix.
- Shared subtitle: if `quest.Scope == Server`, a muted `(shared)` line appears directly under the title.
- Objectives: one row per `QuestObjective`.
  - Title line: `objective.Title` (a plain string in the JSON — not a lang key), or `objective.Code` as a fallback when Title is null.
  - Requirements line (for `delivery` type only): a comma-joined informational list of `"Nx item-name"` from `objective.Items` — e.g. "18 resin, 6 fat". Item names resolved via `Lang.Get("item-" + item-code-without-domain)` with a fallback to the raw code; good enough for v1.
  - Progress bar: a single `AddStatbar` with current = `QuestSystem.GetObjectiveProgress(player, quest.Code, objective.Code)` and max = `objective.Required`. This matches how the quest tree actually stores progress (one counter per objective, incremented per turn-in). Non-delivery objective types share the same bar without the requirements line.

### Live updates

- Listen to `player.Entity.WatchedAttributes.RegisterModifiedListener("progressionframework:questlog", ...)` for player-scope changes.
- Subscribe to a new client-side event `QuestSystem.ServerQuestStateChanged` (fired by the snapshot/update handlers in §4) for server-scope changes.
- Both handlers recompose only when the dialog is open AND `currentTab == QuestLog`.

## 4. Server-scope quest sync

### Channel and packets

```csharp
// Both sides
api.Network.RegisterChannel("progressionframework:questsync")
   .RegisterMessageType<ServerQuestSnapshot>()
   .RegisterMessageType<ServerQuestUpdate>();

[ProtoContract]
class ServerQuestSnapshot
{
    [ProtoMember(1)] public Dictionary<string, byte[]> States;   // code → TreeAttribute bytes
}

[ProtoContract]
class ServerQuestUpdate
{
    [ProtoMember(1)] public string Code;
    [ProtoMember(2)] public byte[] StateBytes;                   // TreeAttribute bytes; null ⇒ quest no longer has server state
}
```

Wire payload is `TreeAttribute.ToBytes(...)` of the same tree held in `serverQuestState[code]`. No parallel DTO — the client reads the tree with the same code the server uses.

### Server behaviour

- On `PlayerJoin`: serialize every entry in `serverQuestState`, send as `ServerQuestSnapshot` to that player.
- Every site in `QuestSystem` that currently writes to `serverQuestState[code]` calls a new internal `MarkServerQuestDirty(code)`. This method serializes the tree and broadcasts `ServerQuestUpdate` to all connected players immediately. Push-only — no tick listener, no batching. Justification: `QuestStarted`, `ObjectiveProgressed`, `ObjectiveCompleted`, `QuestCompleted` are all human-triggered via NPC turn-ins; frequency is low. Throttling can be added later as a one-liner inside `MarkServerQuestDirty` if a future objective type produces real spam.
- If a quest is removed from `serverQuestState` (rare edge case), `MarkServerQuestDirty` broadcasts `{ code, StateBytes = null }` so clients drop their mirror entry.

### Client behaviour

- `Dictionary<string, TreeAttribute> clientServerQuestState` on `QuestSystem` (side-conditional — only populated client-side).
- `ServerQuestSnapshot` handler: clear the dict, deserialize every entry.
- `ServerQuestUpdate` handler: replace or remove the entry for `Code`.
- Both handlers fire the `ServerQuestStateChanged` event (no args — consumers recompose from scratch).

### Stale-entry cleanup

Not handled in v1. Snapshots are sent on every join and replace the dict wholesale, so stale entries can't outlive a session boundary. Logged as a non-goal.

## 5. File changes

New:
- `ProgressionFramework/Client/GuiDialogLedger.cs` — dialog shell + `ComposeTrainingTab` + delegation to `LedgerQuestLogTab`.
- `ProgressionFramework/Client/LedgerQuestLogTab.cs` — quest-log compose logic kept separate so the shell stays focused.
- `ProgressionFramework/Quests/ServerQuestSyncPackets.cs` — the two `[ProtoContract]` DTOs.

Modified:
- `ProgressionFramework/Quests/QuestSystem.cs` — add `GetQuestStatus`, `GetObjectiveProgress`, `MarkServerQuestDirty`, network registration, snapshot/update handlers, `ServerQuestStateChanged` event, `clientServerQuestState` dict. Route every existing mutation of `serverQuestState` through `MarkServerQuestDirty`.
- `ProgressionFramework/ProgressionFrameworkModSystem.cs` — rename hotkey id, construct `GuiDialogLedger` instead of `GuiDialogTrainingLedger`, call `api.Gui.RegisterDialog(ledgerDialog)`.
- `ProgressionFramework/assets/progressionframework/lang/en.json` — new lang keys; remove `training-ledger-title`.

Removed:
- `ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs` — replaced by the new shell. The `Training/Client/` directory is removed if empty afterward.

## 6. Testing

Manual (there is no test harness for this project):

1. Build with `dotnet build`. Launch with Seafarer loaded.
2. Press `L`. Dialog titled "Ledger" opens with Training tab active by default.
3. Switch to Quest Log tab. With no player quests active, empty-state text shows.
4. Start a delivery quest in game (talk to Drake). Reopen dialog, Quest Log tab shows the quest under its group with progress bars at 0.
5. Turn in part of the delivery. Dialog recomposes live; bars advance.
6. Complete the quest. Status flips to "Completed" with gold check; stays in the log.
7. Close and reopen — last selected tab is retained.
8. Hotkey: press `L` once to open, press `L` again to close. Must work reliably (regression check on the known `CLAUDE.md` bug).
9. Server-scope quest: Seafarer currently ships none, so this is validated in the Seafarer consumer repo once a server-scope quest exists there. Acceptance criterion: two clients connected, client A turns in an objective, client B sees the bar advance within one open/recompose of their Quest Log.

## 7. Rollout

- Framework-only change. Consumers (Seafarer) don't need code changes. Any lang key additions they want (quest titles, descriptions, group names) they already own.
- The removal of `progressionframework:training-ledger-title` is safe: no other mod references it.
