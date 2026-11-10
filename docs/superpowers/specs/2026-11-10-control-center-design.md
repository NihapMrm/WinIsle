# Control Center — design

Date: 2026-11-10
Status: draft, awaiting approval

## Intent

MyTodo has grown into many features (island, reminders, flashes, input hooks, clipboard, shelf, prayer
tracking). Almost every behaviour is a constant in code, and the only way to change one is to edit it and
rebuild. Nothing can be switched off. The user wants **full control** of the app from one window: every
feature can be turned on or off, every tunable value can be changed, and changes take effect immediately,
without a rebuild or restart.

## Goals

- One **Control Center** window that controls every feature and every value in the README's Tuning table.
- Every change applies **live**, the moment it is made. No Save button, no restart.
- Settings survive restarts.
- Bad or missing settings never break the app: they fall back to defaults.
- It looks like the rest of the app (the prayer dashboard's dark glass and widgets).

## Non-goals

- **Rebinding hotkeys.** `Ctrl+Alt+T/P/V` stay fixed; the panel lists them. (Builder's call to revisit later.)
- **Island sizes, spring physics, colours, individual sound notes.** These are design constants, not
  preferences.
- Import/export of settings, profiles, themes.

## Anti-goals (would be a failure even if it works)

- A setting that looks changed but secretly needs a restart.
- Turning a feature off that leaves a side effect behind: for example the volume-key hook still swallowing
  keys, or Windows notification banners still hidden.
- A damaged `settings.txt` stopping the app from starting.
- The panel feeling like a different app from the rest of MyTodo.

## Constraints

- C# 5, .NET Framework `csc.exe` via `build.cmd`. No `?.`, `$"..."`, `out var`, tuples. No NuGet.
- All data in `%LOCALAPPDATA%\MyTodo` (`AppPaths.For`).
- WPF, code-only UI (no XAML), built on `DashboardWindow` widgets: `Card`, `Pill`, `Dropdown`, `Field`,
  `Labelled`, `CardHeader`, `T`, `Glyph`, `Clickable`.
- `Checks.cs` (`--check`) is the test suite. New logic adds checks there.

## Approaches considered

1. **Control Center window inside the app with live settings** — chosen. One window, one settings store,
   every feature reads it live. Most work, but it is the only option that gives full control.
2. **Settings file only, reloaded on save** — rejected. No window; the user must know key names, and typos
   fail silently. It moves constants into a text file rather than giving control.
3. **Separate settings app or web page** — rejected. A second process can't switch the running app's
   features without extra inter-process plumbing, and the app deliberately makes no network connections.

## Design

### 1. Settings store — `src/Shared/Settings.cs`

A static class holding every setting as a typed static field with its default, plus:

- `Load()` — reads `settings.txt` (`key=value` per line, invariant culture). Unknown keys are ignored,
  missing keys keep their default, unparsable or out-of-range values are clamped or ignored.
  Called first thing in `Program.StartReminders`.
- `Set(key, value)` style setters (or one setter per property — builder's call) that clamp, store, save
  the file, and raise `Changed`.
- `event Action Changed` — raised on the UI thread after any change, for features that must react
  (re-arm a timer, rebuild sounds).
- `ResetAll()` — restores defaults, saves, raises `Changed`.
- Saving failures are swallowed (setting still applies for this session), matching `Prayers.SaveSettings`.

**Migration:** on first load, if `shelf-settings.txt` has `screenshots=on`, carry it over into
`settings.txt`. After that `FileShelf.AutoScreenshots` reads and writes through `Settings`.

### 2. What is controlled, and how each applies live

The pattern everywhere: the feature keeps running, and **checks the setting at the moment it acts**. This
makes every switch instant without start/stop plumbing, except where noted.

| Section | Setting | Default | Range | How it applies live |
|---|---|---|---|---|
| General | Start with Windows | current state of `Todo Panel.lnk` in Startup folder | on/off | Create / delete the shortcut |
| General | Show todo panel on start | on | on/off | Read at next start (the only start-time setting; labelled as such) |
| Water | Enabled | on | on/off | `Due()` returns null when off |
| Water | Every | 90 min | 15–480 | Next due recalculated from last answer |
| Water | Snooze | 10 min | 1–120 | Used on next "Not yet" |
| Prayer | Reminders enabled | on | on/off | `Due()` null when off (tracking/Missed rules unchanged) |
| Prayer | Snooze | 10 min | 1–60 | Next "Not yet" |
| Prayer | Offset per prayer (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha) | 0 | −30…+30 min | `Prayers.Offset` read live |
| Prayer | Fajr angle / Isha angle | 18° | 12–20 | Read live by `Prayers.For` |
| Prayer | Asr method | Shafi'i | Shafi'i / Hanafi | Shadow factor 1 or 2 |
| Prayer | Location, latitude, longitude, time zone | Batticaloa | as today | The existing location row, now on the Prayer page (still saved in `prayer-settings.txt`) |
| Battery | Alerts enabled | on | on/off | `Due()` null when off |
| Battery | Low threshold | 15% | 5–50 | `Problem()` reads live |
| Battery | Full threshold | 95% | 60–100 | `Problem()` reads live |
| Battery | Repeat after dismiss | 1 min | 1–30 | Next dismiss |
| Battery | Charging / unplug animation | on | on/off | `ChargeWatcher` skips the call |
| Battery | Animation length | 5 s | 2–10 | `ChargeMs` read live |
| Notifications | Show on island | on | on/off | Poll skips enqueue; `Due()` null |
| Notifications | Hide Windows banners | on | on/off | Off: stop calling `HideBanner` and run `RestoreBanners` once. On: hide all again |
| Notifications | Time on screen | 6 s | 2–30 | Read on next notification |
| Flashes | Volume flash | on | on/off | Flash wrapper in `Program` drops it |
| Flashes | Brightness flash | on | on/off | same |
| Flashes | Bluetooth flash | on | on/off | same |
| Flashes | Flash duration | 2.2 s | 1–6 | `FlashMs` read live |
| Flashes | Camera / mic dots | on | on/off | `PrivacyWatcher` hides dots when off |
| Input | Take over volume keys | on | on/off | Hook checks the flag and calls `CallNextHookEx` when off, so Windows handles keys again |
| Input | Wheel-press + scroll = volume | on | on/off | Mouse hook passes everything through when off |
| Input | Volume step | 2% | 1–10 | `VolumeWatcher.Step` reads live |
| Clipboard | History enabled | on | on/off | Off: stop recording new copies (existing items stay until cleared) |
| Clipboard | Items kept | 60 | 10–500 | `Keep` read live; trimming happens on next copy |
| Clipboard | Clear history now | — | button | Clears unpinned items |
| Shelf | Screenshots to shelf | off | on/off | Same flag as the shelf header switch, kept in sync |
| Shelf | Clear shelf now | — | button | `FileShelf.Clear()` |
| Sounds | All sounds | on | on/off | One play wrapper checks the flag |
| Sounds | Volume | 100% | 0–100 | Regenerate the `SoundPlayer`s with scaled peak on `Changed` |
| Sounds | Per kind: reminders, alarms, notifications, flashes, timer ticks | on | on/off | Same wrapper |
| About | Data folder | — | button | Opens `%LOCALAPPDATA%\MyTodo` in Explorer |
| About | Run self-checks | — | button | Runs `TodoPanel.exe --check` and shows pass/fail count |
| About | Quit MyTodo | — | button | Same as `Ctrl+Shift+Q` |
| About | Reset all settings | — | button with confirm | `Settings.ResetAll()` |

**Linked switches (builder-check answer, our call):** two pairs would otherwise leave the user with nothing.

- *Notifications:* "Hide Windows banners" only applies while "Show on island" is on. Turning "Show on island"
  off restores the Windows banners at once (`RestoreBanners`) and dims the banner row with the hint "Banners
  are back while notifications are off the island". Turning it on again re-hides banners if that row is on.
- *Volume:* "Take over volume keys" only applies while "Volume flash" is on. Turning the flash off releases
  the keys to Windows (its own pop-up returns) and dims that row with a hint. The wheel-press volume gesture
  stays independent: it changes volume either way.

**Master switches (our call):** a card has a master switch only where the whole card is one feature, and
the master dims only the rows that mean nothing without it:

| Card | Master | Dims | Stays live with master off |
|---|---|---|---|
| Water | Enabled | Every, Snooze | — |
| Prayer | Reminders enabled | Snooze | Offsets, angles, Asr method, location (they drive the dashboard times and Missed tracking) |
| Battery | Alerts enabled | Low, Full, Repeat | Charging animation and its length (own switch) |
| Notifications | Show on island | Time on screen, Call words, Hide banners | — |
| Clipboard | History enabled | Items kept | Clear history |
| General, Flashes, Input, Sounds, Shelf, About | none | — | each row has its own switch |

**Call words (our call):** in scope. Shown as an editable comma-separated phrase list (default: `incoming
call, voice call, video call, calling`). The app builds the regex from the phrases (escaped), so no regex
validation is needed; an empty list means no notification is treated as a call. Add a check for this.
`PlayNudge` is a design constant and is out of scope with the island sizes.

**Try it buttons:** each feature section has a "Preview" pill that plays its island moment in the running
app (water reminder, prayer reminder, battery low, charge, unplug, volume flash, notification). Previews
must not change real state (no water timer reset, no prayer marked).

### 3. The window — `src/Settings/ControlCenter.cs`

- Subclass of `DashboardWindow`, **1320 wide** × `min(816, work area − 16)` tall (the prayer dashboard's
  height today), centred, draggable, Esc closes. 1320 = 200 px section list + the dashboard's current 1120 px,
  so the dashboard keeps its layout unchanged. (The user's work area is 1536 × 864.)
- **Left (200 px):** list of pages: Prayer, General, Water, Battery, Notifications, Flashes, Input,
  Clipboard, Shelf, Sounds, About. The selected page is highlighted. Clicking one **switches the page**
  (not a scroll), with a short fade.
- **Right:** the selected page in its own scroll view. Settings pages are a column of `Card`s: each card is
  `CardHeader(title, one-line description, master switch)`, then rows of `label + control + hint`.

**Prayer page — the prayer dashboard moves inside the Control Center.** The separate `PrayerDashboard`
window goes away. Its content (today, tracking analytics, charts, history, and the "Location & time zone"
row) becomes the Prayer page, unchanged in look and behaviour, built into the page instead of its own
window. Below the dashboard content sits a **Prayer reminders** settings card: reminders on/off, snooze,
per-prayer offsets, Fajr/Isha angles, Asr method. Changing any of these re-renders the dashboard's times at
once.

- `PrayerDashboard` stops being a `Window`: its partial files keep their code but build into a panel the
  Control Center hosts (for example a class that returns its root `FrameworkElement` and exposes
  `Render(animate)`). Its own window chrome (title bar, close button, drag) is dropped in favour of the
  Control Center's.
- The dashboard re-renders when the Prayer page is shown, so it is current each time it is opened.
- **New widgets** (added to `DashboardWindow` so they can be reused): `Switch` (animated on/off toggle),
  `Stepper` (number with − / + and typed entry, clamped, with unit), `Row(label, hint, control)`.
- Rows under a switched-off master dim to 40% and are disabled.
- A row whose value is out of range when typed shows the clamped value; no error dialogs.
- One single instance: opening it again brings the existing window forward.

**Opening it, and which page it opens on:**

| How | Opens on |
|---|---|
| `Ctrl+Alt+P` (existing prayer hotkey) | Prayer page |
| Moon button in the todo panel header | Prayer page |
| `Ctrl+Alt+S` (new, registered with the others in `TodoPanel`) | The page last shown (General the first time) |
| Gear button in the todo panel header (new) | The page last shown |
| Gear in the island's media player tool row (new) | The page last shown |

If the window is already open, the hotkey brings it forward and switches to that page (no toggle-close,
matching today's `OpenDashboard`). Opening it hides the todo panel first, as `OpenDashboard` does today,
because the panel is topmost and would sit on top of it.

```
┌───────────────────────────────────────────────────────────────┐
│ Control Center                                            ✕   │
│ ┌────────────┐  ┌─────────────────────────────────────────┐   │
│ │ Prayer     │  │ Water reminder                    [●──] │   │
│ │ General    │  │ Asks whether you've had a drink         │   │
│ │▸Water      │  │  Every          [ − 90 min + ]          │   │
│ │ Battery    │  │  Snooze         [ − 10 min + ]          │   │
│ │ Notific.   │  │                       (Preview)         │   │
│ │ Flashes    │  └─────────────────────────────────────────┘   │
│ │ Input      │  ┌─────────────────────────────────────────┐   │
│ │ Clipboard  │  │ Battery                           [●──] │   │
│ │ Shelf      │  │  Low at         [ − 15 % + ]            │   │
│ │ Sounds     │  │  Full at        [ − 95 % + ]            │   │
│ │ About      │  │  Charging animation               [●──] │   │
│ └────────────┘  └─────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────┘
```

### 4. Code changes outside the new files

- Constants listed in section 2 become reads of `Settings` (keep the `const` names as the defaults in
  `Settings`, and delete them from their old homes).
- `Program.StartReminders`: `Settings.Load()` first; flash callbacks wrapped with their on/off checks.
- `IslandSound`: plays go through one method that checks sound settings; players rebuilt on volume change.
- `README.md`: new Control Center section; Tuning table updated to say which values now live in the
  Control Center; data table gains `settings.txt`.

### 5. Checks

Add to `Checks.cs`:

- Settings round-trip: set values, save, reload, equal.
- Damaged file: garbage lines, wrong types, out-of-range numbers load as defaults/clamped.
- Migration from `shelf-settings.txt`.
- Battery `Problem()` follows changed thresholds.
- Water `Due()` is null when disabled.
- Volume `Step` follows changed step size.

## Left to the builder

- Exact widget visuals (switch size, stepper layout) within the dashboard style.
- Whether setters are per-property or keyed.
- Debouncing saves while a stepper is held.
- How previews are wired for each feature, provided they don't touch real state.
- Order of implementation.
