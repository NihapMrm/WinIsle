# Control Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One Control Center window that switches every MyTodo feature on/off and changes every tunable value live, with the prayer dashboard moved inside it as its first page.

**Architecture:** A keyed settings store (`Settings`, `settings.txt`) replaces the code constants; every feature reads it at the moment it acts, so changes apply without restart. The Control Center is a `DashboardWindow` with a left page list; the prayer dashboard's partial files become partial files of `ControlCenter` so they keep using the shared widgets unchanged.

**Tech Stack:** C# 5, WPF (code-only, no XAML), .NET Framework `csc.exe` via `build.cmd`, self-checks in `Checks.cs` (`--check`).

**Spec:** `docs/superpowers/specs/2026-11-10-control-center-design.md`

## Global Constraints

- C# 5 only: no `?.`, `$"..."`, `out var`, tuples, expression-bodied members, `nameof`, `using static`.
- No NuGet, no new references in `build.cmd` beyond what it has.
- All data in `%LOCALAPPDATA%\MyTodo` via `AppPaths.For(name)`. Settings file: `settings.txt`.
- Every change applies live; the only start-time setting is "Show todo panel on start", labelled as such.
- A damaged or missing `settings.txt` never stops the app: unknown keys ignored, bad values → default, out-of-range → clamped.
- Look: reuse `DashboardWindow` widgets and palette; new widgets go into `DashboardWindow`.
- Control Center window: 1320 wide × `Math.Min(816, SystemParameters.WorkArea.Height - 16)` tall; left page list 200 px; prayer content keeps its 1120 px layout.
- Page order: Prayer, General, Water, Battery, Notifications, Flashes, Input, Clipboard, Shelf, Sounds, About.
- Checks never touch the real `settings.txt`: they load from a temp path and restore defaults afterwards.
- Work on branch `control-center` (create it from `main` before Task 1).

## Review Focus

1. **Turning a feature off leaves no side effect** — volume keys reach Windows again when "Take over volume keys" or "Volume flash" is off; Windows banners come back when notifications are off the island. Tests in Task 4 and Task 5.
2. **Damaged `settings.txt`** (garbage lines, `water.every=abc`, `battery.low=999`, empty file, duplicated keys) loads without throwing, with defaults/clamps. Test in Task 1.
3. **Changing an interval while a reminder is waiting** — water `every` lowered from 90 to 15 after 20 min makes water due now, not 70 min later. Test in Task 2.
4. **Previews never change real state** — previewing water doesn't reset the water timer; previewing prayer doesn't mark a prayer. Test in Task 7.
5. **Sound volume 0 and sounds off** — no sound plays, and turning volume back up restores the original peaks. Test in Task 6.

---

### Task 1: Settings store and test build

**Files:**
- Create: `src/Shared/Settings.cs`
- Modify: `build.cmd` (optional output path)
- Modify: `src/Shelf/FileShelf.cs` (screenshots flag through `Settings`)
- Test: `src/Checks.cs` (new `SettingsFailures()`, added to the sum in `Run()`)

**Interfaces:**
- Produces (all `internal static` on `static class Settings`):
  - Key constants (`const string`), with default / min / max:

    | Constant | Key | Default | Min | Max |
    |---|---|---|---|---|
    | `ShowPanel` | `general.showPanel` | true | | |
    | `WaterOn` | `water.on` | true | | |
    | `WaterEvery` | `water.every` (min) | 90 | 15 | 480 |
    | `WaterSnooze` | `water.snooze` (min) | 10 | 1 | 120 |
    | `PrayerOn` | `prayer.on` | true | | |
    | `PrayerSnooze` | `prayer.snooze` (min) | 10 | 1 | 60 |
    | `PrayerOffset(int i)` → `"prayer.offset." + i` (i = 0..5, Fajr..Isha) | | 0 | −30 | 30 |
    | `FajrAngle` | `prayer.fajrAngle` | 18 | 12 | 20 |
    | `IshaAngle` | `prayer.ishaAngle` | 18 | 12 | 20 |
    | `Hanafi` | `prayer.hanafi` | false | | |
    | `BatteryOn` | `battery.on` | true | | |
    | `BatteryLow` | `battery.low` (%) | 15 | 5 | 50 |
    | `BatteryFull` | `battery.full` (%) | 95 | 60 | 100 |
    | `BatteryAgain` | `battery.again` (min) | 1 | 1 | 30 |
    | `ChargeAnim` | `battery.anim` | true | | |
    | `ChargeSeconds` | `battery.animSeconds` | 5 | 2 | 10 |
    | `NotifyOn` | `notify.on` | true | | |
    | `HideBanners` | `notify.hideBanners` | true | | |
    | `NotifySeconds` | `notify.seconds` | 6 | 2 | 30 |
    | `CallWords` | `notify.callWords` (text) | `incoming call, voice call, video call, calling` | | |
    | `FlashVolume` | `flash.volume` | true | | |
    | `FlashBrightness` | `flash.brightness` | true | | |
    | `FlashBluetooth` | `flash.bluetooth` | true | | |
    | `FlashSeconds` | `flash.seconds` | 2.2 | 1 | 6 |
    | `Dots` | `flash.dots` | true | | |
    | `VolumeKeys` | `input.volumeKeys` | true | | |
    | `WheelVolume` | `input.wheelVolume` | true | | |
    | `VolumeStep` | `input.volumeStep` (%) | 2 | 1 | 10 |
    | `ClipOn` | `clip.on` | true | | |
    | `ClipKeep` | `clip.keep` | 60 | 10 | 500 |
    | `Screenshots` | `shelf.screenshots` | false | | |
    | `SoundOn` | `sound.on` | true | | |
    | `SoundVolume` | `sound.volume` (%) | 100 | 0 | 100 |
    | `SoundReminders`, `SoundAlarms`, `SoundNotifications`, `SoundFlashes`, `SoundTicks` | `sound.reminders` … `sound.ticks` | true | | |
    | `LastPage` | `ui.lastPage` (text) | `General` | | |
  - `bool On(string key)`, `double Num(string key)`, `string Text(string key)`
  - `void Set(string key, bool value)`, `void Set(string key, double value)` (clamps), `void Set(string key, string value)`
  - `event Action Changed` — raised after every `Set` and after `ResetAll`
  - `void Load(string path)` — reads file, then migration (below); `void Load()` = `Load(AppPaths.For("settings.txt"))`
  - `void ResetAll()` — defaults, save, raise `Changed`
  - `double Min(string key)`, `double Max(string key)` — for the UI steppers
- Migration: in `Load`, if the file has no `shelf.screenshots` line and `shelf-settings.txt` in the same folder contains `screenshots=on`, set `Screenshots` true and save.
- Format: one `key=value` per line, invariant culture, bools as `true`/`false`. Unknown keys kept in memory and written back (so a newer build's keys survive an older build). Every `Set` saves; save failures are swallowed.
- `build.cmd`: first argument, if given, is the output exe path; default stays `%~dp0TodoPanel.exe`.

- [ ] **Step 1: Write the failing checks** — `SettingsFailures()` in `Checks.cs`, using a temp folder:
  - round-trip: `Set(WaterEvery, 30)`, `Set(Hanafi, true)`, `Set(CallWords, "ring")`, then `Load(path)` again → `Num(WaterEvery)==30`, `On(Hanafi)`, `Text(CallWords)=="ring"`.
  - clamp: `Set(BatteryLow, 999)` → `Num(BatteryLow)==50`; `Set(WaterEvery, 1)` → `15`.
  - damaged file: write `"garbage\nwater.every=abc\nbattery.low=999\nwater.on=maybe\n=\nprayer.offset.2=5\nprayer.offset.2=7"`, `Load` → no exception, `Num(WaterEvery)==90`, `Num(BatteryLow)==50`, `On(WaterOn)`, `Num(PrayerOffset(2))==7` (last wins).
  - empty file and missing file → all defaults (`Num(FlashSeconds)==2.2`, `Text(LastPage)=="General"`).
  - migration: folder with `shelf-settings.txt` = `screenshots=on` and no `settings.txt` → `On(Screenshots)`.
  - `Changed` fires once per `Set`.
  - end: `Load(nonexistent temp path)` so later checks see defaults.
- [ ] **Step 2: Build to a temp exe and run checks; expect a compile failure (`Settings` missing).**
  Run: `cmd /c build.cmd "%TEMP%\MyTodoCheck.exe"` → Expected: `error CS0103: The name 'Settings' does not exist`
- [ ] **Step 3: Implement `build.cmd` output argument and `src/Shared/Settings.cs` as specified above.**
- [ ] **Step 4: Point `FileShelf.AutoScreenshots` at `Settings` (get/set `Screenshots`); the `settingsFile` constructor argument and `shelf-settings.txt` writes go away. `FileShelf` tests in `ShelfFailures()` that used `shelf-settings.txt` move to Settings-based assertions.**
- [ ] **Step 5: Call `Settings.Load()` first in `Program.StartReminders()`.**
- [ ] **Step 6: Build and run checks.**
  Run: `cmd /c build.cmd "%TEMP%\MyTodoCheck.exe" && "%TEMP%\MyTodoCheck.exe" --check & echo exit=%ERRORLEVEL%` → Expected: `exit=0`
- [ ] **Step 7: Commit** — `git add build.cmd src/Shared/Settings.cs src/Shelf/FileShelf.cs src/Checks.cs src/Program.cs` → `git commit -m "feat: add live settings store"`

---

### Task 2: Reminders and prayer times read settings

**Files:**
- Modify: `src/Water/WaterReminder.cs`, `src/Prayer/PrayerReminder.cs`, `src/Prayer/PrayerTimes.cs`, `src/Battery/BatteryReminder.cs`
- Test: `src/Checks.cs` (new `LiveReminderFailures()`)

**Interfaces:**
- Consumes: `Settings.On/Num`, keys from Task 1.
- Produces:
  - `WaterReminder`: `static TimeSpan Every { get; }` and `static TimeSpan Snooze { get; }` read `WaterEvery`/`WaterSnooze` (replacing the readonly fields; the existing check `Every == 90 min` stays valid by default). Live instance keeps `DateTime from` (last answer or start, UTC) and `bool snoozed`; due = `from + (snoozed ? Snooze : Every)`, so a changed interval applies to the wait in progress. `Due()` returns null when `WaterOn` is off. The explicit `(every, snooze, firstIn)` constructor for `--demo-water` keeps fixed values.
  - `PrayerReminder`: live instance snooze reads `PrayerSnooze`; `Due()` null when `PrayerOn` off (tracking unaffected).
  - `Prayers`: `Offset[i]` → `Settings.Num(Settings.PrayerOffset(i))`; `FajrAngle`/`IshaAngle` read settings; Asr shadow factor `On(Hanafi) ? 2 : 1` (replace the `1 +` in the Asr altitude).
  - `BatteryReminder`: `static int Low { get; }`, `static int Full { get; }`, `static TimeSpan Again { get; }` from settings; `Due()` null when `BatteryOn` off.

- [ ] **Step 1: Write failing checks** in `LiveReminderFailures()` (temp settings, defaults restored at end):
  - battery: `Set(BatteryLow, 30)` → `BatteryReminder.Problem(new Power{Percent=25, Charging=false}) == "low"`; `Set(BatteryFull, 80)` → `Problem({85, true}) == "full"`.
  - battery off: `new BatteryReminder(() => new Power{Percent=5}).Due() == null` with `BatteryOn` false.
  - water off: live `new WaterReminder()` with `WaterOn` false → `Due() == null` even when overdue (use a test hook `internal DateTime From` set 200 min ago).
  - water interval change: `From` = 20 min ago, `Set(WaterEvery, 15)` → `Due() != null`.
  - prayer: with `Hanafi` on, Asr from `Prayers.For(2026-11-09, 5.5)` is later than with it off by more than 30 min.
  - prayer offset: `Set(PrayerOffset(2), 5)` → Dhuhr is 5 min later than with 0.
- [ ] **Step 2: Build & run checks; expect failures/compile errors.** (Command as Task 1 Step 6.)
- [ ] **Step 3: Implement the interface changes above.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git commit -am "feat: reminders and prayer times follow settings"`

---

### Task 3: Notifications, clipboard

**Files:**
- Modify: `src/Notifications/WindowsNotifications.cs`, `src/Clipboard/ClipboardHistory.cs`, `src/Program.cs`
- Test: `src/Checks.cs` (extend `NotificationFailures()`, `ClipboardFailures()`)

**Interfaces:**
- Produces:
  - `WindowsNotifications.CallPattern(string phrases)` → `Regex`: phrases split on `,`, trimmed, empty dropped, each `Regex.Escape`d, joined `|`, wrapped `\b(…)\b`, IgnoreCase; no phrases → a regex that never matches. `IsCall` uses the pattern built from `Text(CallWords)` (cache by text).
  - Visible time reads `NotifySeconds` live (the `visible` ctor arg stays for checks).
  - `NotifyOn` off: `Poll` doesn't enqueue; `Due()` returns null.
  - Banners: hide only while `On(NotifyOn) && On(HideBanners)`. On `Settings.Changed`, if that became false → `RestoreBanners(SettingsKey)` once; if it became true → re-run the hide-all pass on the next poll.
  - `ClipboardHistory`: `Trim()` uses `(int)Num(ClipKeep)`; capture skipped while `ClipOn` off; new `internal void ClearUnpinned()`.
  - `Program.Clipboard` (`internal static ClipboardHistory`) set in `StartReminders`, for the Control Center's Clear button.

- [ ] **Step 1: Write failing checks:**
  - `CallPattern("incoming call, voice call, video call, calling")` matches `"Incoming voice call"`, `"Video call"`, `"calling…"`, not `"Recall the meeting"`.
  - `CallPattern("")` matches nothing; `CallPattern("a.b")` doesn't match `"axb"` (escaped).
  - existing `isCall` checks still pass with defaults.
  - clipboard: `Set(ClipKeep, 10)`, add 12 unpinned + 1 pinned → 11 items; `ClearUnpinned()` → only the pinned one.
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git commit -am "feat: notifications and clipboard follow settings"`

---

### Task 4: Island flashes, charge animation, dots

**Files:**
- Modify: `src/Island/IslandWindow.Flash.cs`, `src/Island/IslandWindow.Charge.cs`, `src/Battery/ChargeWatcher.cs`, `src/Privacy/PrivacyWatcher.cs`, `src/Program.cs`
- Test: `src/Checks.cs` (new `FlashSettingFailures()`)

**Interfaces:**
- Produces:
  - `FlashMs` → `(int)(Num(FlashSeconds) * 1000)`; `ChargeMs` → `(int)(Num(ChargeSeconds) * 1000)`.
  - `internal static bool Program.Allowed(IslandNotice n)` — false for key `"volume"` when `FlashVolume` off, `"brightness"` when `FlashBrightness` off, Bluetooth notices when `FlashBluetooth` off (give Bluetooth notices `Key = "bluetooth"` if they don't have it). The flash callbacks passed to the watchers in `StartReminders` become `n => { if (Allowed(n)) island.Flash(n); }`. The player's volume bar still follows volume changes when the flash is off (call `ShowPlayerVolume` before the check).
  - `ChargeWatcher` skips both calls when `ChargeAnim` off.
  - `PrivacyWatcher` passes empty lists when `Dots` off.

- [ ] **Step 1: Write failing checks:** `Allowed(VolumeWatcher.Notice(0.5f,false))` true by default, false after `Set(FlashVolume,false)`; same for `BrightnessWatcher.Notice(40)` / `FlashBrightness`; a Bluetooth notice / `FlashBluetooth`; a `"screenshot"` notice always true.
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git commit -am "feat: island flashes follow settings"`

---

### Task 5: Volume keys and wheel volume

**Files:**
- Modify: `src/Audio/VolumeWatcher.cs`
- Test: `src/Checks.cs` (extend the existing volume step checks in `WatcherFailures()`)

**Interfaces:**
- Produces:
  - `internal static bool TakeKeys()` = `On(VolumeKeys) && On(FlashVolume)`. The keyboard hook calls `CallNextHookEx` (does not swallow) when false.
  - The mouse hook passes everything through when `WheelVolume` off. If it turns off while the wheel is held, release state cleanly (`middleHeld = false`) so no middle click is lost or doubled.
  - `Step(double level, bool up)` uses `Num(VolumeStep) / 100` as the step and grid.

- [ ] **Step 1: Write failing checks:** with `Set(VolumeStep, 5)`: `Step(0.50, true) == 0.55`, `Step(0.52, true) == 0.55`, `Step(0.02, false) == 0`; defaults: existing 2% checks unchanged. `TakeKeys()` true by default, false with `VolumeKeys` off, false with `FlashVolume` off.
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git commit -am "feat: volume keys and wheel follow settings"`

---

### Task 6: Sounds

**Files:**
- Modify: `src/Island/IslandSound.cs`, every `.Play()` / `.PlayLooping()` call site on `IslandSound` players (`IslandWindow.cs`, `IslandWindow.Flash.cs`, `IslandWindow.Charge.cs`, `IslandWindow.Shelf.cs`)
- Test: `src/Checks.cs` (extend `SoundFailures()`)

**Interfaces:**
- Produces:
  - `internal static double Gain` — `Num(SoundVolume) / 100`, applied as a multiplier on `peak` inside `Mix`.
  - `internal static void Rebuild()` — regenerates every player's WAV **into the same `SoundPlayer` object** (`player.Stream = new MemoryStream(wav); player.Load();`) so notices holding a player reference get the new volume. Called on `Settings.Changed` when `SoundVolume` changed.
  - Kind of each player: Appear, Yes, NotYet, Water → reminders; Alarm → alarms; Notify → notifications; Charge, Unplug → flashes; Tick → ticks.
  - `internal static bool Allowed(SoundPlayer p)` — `On(SoundOn)` and its kind's switch, and `Gain > 0`.
  - `internal static void Play(SoundPlayer p)` and `internal static void Loop(SoundPlayer p)` — check `Allowed`, then play. All call sites switch to these.

- [ ] **Step 1: Write failing checks:** at `SoundVolume` 50 the charge chime peak is `0.16 * short.MaxValue` (±2); back at 100 it is `0.32` again; `Allowed(IslandSound.Alarm)` false with `SoundAlarms` off and true with it on; every player not allowed with `SoundOn` off; not allowed at volume 0.
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement; replace all call sites** (verify none left: `grep -n "\.Play()\|PlayLooping" src -r` shows only `IslandSound.cs`).
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git commit -am "feat: sound volume and switches"`

---

### Task 7: Island previews

**Files:**
- Create: `src/Island/PreviewReminder.cs`
- Modify: `src/Program.cs` (put it first in the island's reminder list; expose `Program.Island`, `Program.Preview`)
- Test: `src/Checks.cs` (new `PreviewFailures()`)

**Interfaces:**
- Produces:
  - `class PreviewReminder : IReminder` with `internal void Show(IslandNotice n)`. `Due()` returns the pending notice until answered; `StillCurrent` true; `Answer` clears it and returns `new IslandResult { Left = "Preview", Right = n.Title }`. Touches nothing else.
  - `internal static IslandWindow Program.Island`, `internal static PreviewReminder Program.Preview`.
  - Notice builders the Control Center uses (so previews look real without side effects): `WaterReminder.Sample()`, `PrayerReminder.Sample()`, `BatteryReminder.Sample(bool low)`, `WindowsNotifications.Sample()` — each returns the same `IslandNotice` its `Due()` would build, with fixed sample text. Refactor each `Due()` to use its builder so the look can't drift.

- [ ] **Step 1: Write failing checks:** a `PreviewReminder` shows a notice once, `Answer` returns `Left == "Preview"`, then `Due() == null`. A live `WaterReminder` whose `From` is 5 min ago still isn't due after building `WaterReminder.Sample()`. `Tracker` has no new entry after `PrayerReminder.Sample()` (compare `Tracker.Has(key)` before/after).
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Commit** — `git add src/Island/PreviewReminder.cs && git commit -am "feat: island previews"`

---

### Task 8: Control Center window, widgets, prayer page

**Files:**
- Modify: `src/Dashboard/DashboardWindow.cs` (new widgets)
- Create: `src/ControlCenter/ControlCenter.cs` (window, page list, page switching, `Open`)
- Move + rename: `src/Prayer/PrayerDashboard.cs` → `src/ControlCenter/ControlCenter.Prayer.cs`; `PrayerDashboard.Charts.cs` → `ControlCenter.Prayer.Charts.cs`; `PrayerDashboard.Settings.cs` → `ControlCenter.Prayer.Location.cs` (`git mv`)
- Modify: `src/Todo/TodoPanel.cs` (hotkeys, buttons), `src/Island/IslandWindow.cs` (gear in the player's tool row)
- Test: `src/Checks.cs` (new `ControlCenterFailures()`)

**Interfaces:**
- Consumes: `Settings` (Task 1).
- Produces:
  - `DashboardWindow` (protected static): `Border Switch(bool on, Action<bool> changed, string name)` (animated toggle, keyboard Space/Enter); `FrameworkElement Stepper(string key, double step, string unit)` (− / value / +, typed entry, clamps to `Settings.Min/Max`, writes `Settings.Set` on change, holds-to-repeat); `FrameworkElement Row(string label, string hint, UIElement control)`; `TextBox TextSetting(string key, double width)` (writes on lost focus / Enter); `static double StepValue(double value, double step, double min, double max, int direction)` (pure, rounds to the step grid).
  - `partial class ControlCenter : DashboardWindow`:
    - `internal static readonly string[] Pages = { "Prayer", "General", "Water", "Battery", "Notifications", "Flashes", "Input", "Clipboard", "Shelf", "Sounds", "About" };`
    - `internal static void Open(string page)` — single instance; `page == null` → `Text(LastPage)`; brings the window forward and switches page; raises `internal static event Action Opening` first (TodoPanel hides itself on it, as `OpenDashboard` does today).
    - `void ShowPage(string page)` — swaps the right-hand content with a 120 ms fade; saves `LastPage`; the Prayer page re-renders on show.
    - `FrameworkElement BuildPage(string page)` — Prayer page from the moved dashboard code; other pages return a placeholder until Task 9.
  - The moved prayer code: class becomes `partial class ControlCenter`; its window setup (title, size, close button, drag, Esc, glass, open animation) moves into `ControlCenter.cs` and applies to the whole window; its content becomes `FrameworkElement BuildPrayerPage()`. Rename members that collide with ControlCenter's: prefix with `prayer` (`prayerSettings`, `PrayerRender`, `SavePrayerLocation`, `TogglePrayerLocation`, …). The 20 s refresh timer stops in `Closed`.
  - `TodoPanel`: hotkey id 4 `Ctrl+Alt+S` (`VK_S = 0x53`) → `ControlCenter.Open(null)`; `Ctrl+Alt+P` and the moon button → `ControlCenter.Open("Prayer")`; new gear header button (tooltip "Control Center (Ctrl+Alt+S)") → `Open(null)`; `OpenDashboard` and the `dashboard` field are deleted. After `Program.StartReminders()` (which loads settings), if `!Settings.On(Settings.ShowPanel)` the panel hides at once without its animation.
  - Island player: a gear `RoundButton` in `rightTools` → `ControlCenter.Open(null)`.

- [ ] **Step 1: Write failing checks:** `StepValue(90, 5, 15, 480, +1) == 95`; `StepValue(15, 5, 15, 480, -1) == 15`; `StepValue(2.2, 0.2, 1, 6, +1) == 2.4` (±1e-9); `StepValue(92, 5, 15, 480, +1) == 95` (snaps to grid); `ControlCenter.Pages[0] == "Prayer"` and `Pages.Length == 11`.
- [ ] **Step 2: Build & run checks; expect failures.**
- [ ] **Step 3: Implement widgets, window, page list, prayer page move, hotkeys and buttons.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Run the real app and verify by hand** (quit it first: `Stop-Process -Name TodoPanel`; `build.cmd`; start `TodoPanel.exe`):
  - `Ctrl+Alt+P` opens the Control Center on the Prayer page; today, charts, history and the location row work as before.
  - `Ctrl+Alt+S` opens on the last page; switching pages and reopening remembers it.
  - Pressing `Ctrl+Alt+P` while open on another page switches to Prayer.
  - Moon and gear buttons on the todo panel, gear in the island player, Esc closes, window drags.
- [ ] **Step 6: Commit** — `git add -A src && git commit -m "feat: Control Center window with prayer page"`

---

### Task 9: Settings pages

**Files:**
- Create: `src/ControlCenter/ControlCenter.Pages.cs`, `src/Shared/Startup.cs`
- Test: `src/Checks.cs` (extend `ControlCenterFailures()`)

**Interfaces:**
- Consumes: widgets and `BuildPage` (Task 8), `Settings` (Task 1), `Program.Preview/Island/Clipboard` (Tasks 3, 7), samples (Task 7), `IslandSound.Play` (Task 6).
- Produces:
  - `static class Startup`: `internal static string LinkPath` (`Startup` folder + `Todo Panel.lnk`), `internal static bool Enabled { get; set; }` — set true creates the shortcut to the running exe via `WScript.Shell` (late-bound `Type.GetTypeFromProgID` + `InvokeMember`), false deletes it.
  - One builder per page, each a column of `Card`s per the spec's table, master switches and dimming per the spec's "Master switches" table (dim = opacity 0.4 + `IsEnabled = false`), linked rows per the spec's "Linked switches" (dimmed with their hint):
    - **Prayer** (appended under the dashboard content): card "Prayer reminders" — master `PrayerOn` (dims Snooze only), Snooze, six offsets (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha), Fajr angle, Isha angle, Asr method dropdown (Shafi'i / Hanafi), Preview. Any change re-renders the dashboard times.
    - **General:** Start with Windows (`Startup.Enabled`), Show todo panel on start (hint "Applies next time MyTodo starts"), hotkey list (T, P, V, S — read-only).
    - **Water:** master, Every, Snooze, Preview.
    - **Battery:** card "Alerts" (master, Low, Full, Repeat, Preview low, Preview full); card "Charging animation" (switch, length, Preview charge, Preview unplug — call `Program.Island.ShowCharging/ShowUnplugged` with the current percent).
    - **Notifications:** master `NotifyOn`, Time on screen, Call words (`TextSetting`), Hide Windows banners (dimmed with hint "Banners are back while notifications are off the island" when master off), Preview.
    - **Flashes:** Volume, Brightness, Bluetooth, Camera/mic dots, Flash duration; Preview volume (flash the current volume through `Program.Island.Flash`).
    - **Input:** Take over volume keys (dimmed with hint "Windows handles the keys while the volume flash is off" when `FlashVolume` off), Wheel-press + scroll = volume, Volume step.
    - **Clipboard:** master, Items kept, Clear history (`Program.Clipboard.ClearUnpinned()`), hotkey hint.
    - **Shelf:** Screenshots to shelf (the shelf header switch reads the same setting and refreshes on `Settings.Changed`), Clear shelf.
    - **Sounds:** All sounds, Volume, five kind switches, a ▶ test button per kind (plays a sample via `IslandSound.Play`).
    - **About:** version line (exe date), Open data folder (`explorer.exe` + `AppPaths.Folder`), Run self-checks (start own exe with `--check`, show "All N checks passed" / "N failed" from the exit code, off the UI thread), Quit MyTodo, Reset all settings (two-click confirm: first click turns the pill orange "Click again to reset", 3 s timeout).
  - Pages rebuild their controls' displayed values on `Settings.Changed` (so Reset and the shelf switch show up at once).

- [ ] **Step 1: Write failing check:** `Startup.LinkPath` ends with `\Microsoft\Windows\Start Menu\Programs\Startup\Todo Panel.lnk`.
- [ ] **Step 2: Build & run checks; expect failure.**
- [ ] **Step 3: Implement `Startup` and all pages.**
- [ ] **Step 4: Build & run checks.** Expected: `exit=0`
- [ ] **Step 5: Verify by hand in the running app** (rebuild + restart as in Task 8 Step 5): every switch and stepper takes effect without restart — at minimum: water Preview shows and "Drank" doesn't reset the real timer; volume flash off → volume key shows Windows' own pop-up; notifications off → Windows banners return; sound volume 0 → previews silent; Reset restores defaults on screen; Start with Windows creates/deletes the shortcut.
- [ ] **Step 6: Commit** — `git add src && git commit -m "feat: Control Center settings pages"`

---

### Task 10: README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update:** new "Control Center" section (how to open, pages, live changes, previews, reset); shortcut table adds `Ctrl+Alt+S` and changes `Ctrl+Alt+P` to "Control Center, Prayer page"; Prayer dashboard section says it lives in the Control Center; Tuning table marks which values moved to the Control Center (all except island sizes, `PlayNudge`, sound notes); data table adds `settings.txt` and removes `shelf-settings.txt` (migrated); Charging flash row says about 5 s and live decimals; project layout adds `src/ControlCenter/`, `src/Shared/Settings.cs`, `src/Shared/Startup.cs`, `src/Island/PreviewReminder.cs`; build section mentions `build.cmd <out.exe>`.
- [ ] **Step 2: Final build of the real exe and checks.** Run: `Stop-Process -Name TodoPanel; cmd /c build.cmd && TodoPanel.exe --check & echo exit=%ERRORLEVEL%` → Expected `exit=0`; start `TodoPanel.exe`.
- [ ] **Step 3: Commit** — `git add README.md TodoPanel.exe && git commit -m "docs: Control Center"`
