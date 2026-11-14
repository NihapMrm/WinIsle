# WinIsle

WinIsle is a small Windows desktop companion. It has two parts:

- **Todo panel.** An always-on-top glass panel for a short task list.
- **Island.** A Dynamic Island–style pill at the top centre of the screen. It shows reminders, Windows notifications, media controls, a timer, a camera mirror, a file shelf and, from a separate panel, clipboard history.

It is written in C# 5 with WPF, built with the .NET Framework compiler that comes with Windows. There are no NuGet packages or installers. All data is kept on drive C.

---

## Contents

1. [Quick start](#quick-start)
2. [Keyboard shortcuts](#keyboard-shortcuts)
3. [Control Center](#control-center)
4. [Todo panel](#todo-panel)
5. [The island](#the-island)
6. [Prayer reminders and dashboard](#prayer-reminders-and-dashboard)
7. [Water reminder](#water-reminder)
8. [Battery alarm](#battery-alarm)
9. [Windows notifications on the island](#windows-notifications-on-the-island)
10. [Media player](#media-player)
11. [Timer](#timer)
12. [Mirror](#mirror)
13. [File shelf](#file-shelf)
14. [Clipboard history](#clipboard-history)
15. [Where data is stored](#where-data-is-stored)
16. [Privacy and permissions](#privacy-and-permissions)
17. [Building and running](#building-and-running)
18. [Command-line options](#command-line-options)
19. [Tuning](#tuning)
20. [Project layout](#project-layout)
21. [Troubleshooting](#troubleshooting)
22. [Known limitations](#known-limitations)

---

## Quick start

1. Get `WinIsle.exe`: download it from [Releases](https://github.com/NihapMrm/WinIsle/releases), or build it yourself by double-clicking `build.cmd` (see [Building and running](#building-and-running)).
2. Run `WinIsle.exe`. The todo panel appears, and a thin glass line appears at the top centre of the screen. That line is the island.
3. To start it with Windows, turn on **Start with Windows** in the Control Center's General page. It adds **WinIsle.lnk** to your Startup folder.

Only one copy can run at a time. A second copy reports that `Ctrl+Alt+T` is already taken and exits.

---

## Keyboard shortcuts

| Shortcut | What it does |
|---|---|
| `Ctrl+Alt+T` | Show or hide the todo panel |
| `Ctrl+Alt+S` | Open the [Control Center](#control-center) on the page you last used |
| `Ctrl+Alt+P` | Open the Control Center on the Prayer page |
| `Ctrl+Alt+V` | Open or close clipboard history |
| `Esc` or `Ctrl+Q` in the panel | Hide the panel (the app keeps running) |
| `Ctrl+Shift+Q` in the panel | Quit the app completely |

Clipboard history has its own keys, listed in [Clipboard history](#clipboard-history).

---

## Control Center

One window that controls the whole app. Open it with `Ctrl+Alt+S`, the gear button on the todo panel, or the gear in the island's media player. `Ctrl+Alt+P` and the moon button open it on the Prayer page.

- **Pages:** Prayer, General, Water, Battery, Notifications, Flashes, Input, Clipboard, Shelf, Sounds and About. Click one in the list on the left. The window opens on the page you used last.
- **Changes apply at once.** There is no Save button and no restart. The one exception is "Show todo panel on start", which applies the next time WinIsle starts.
- **Switches:** every feature can be turned off: water, prayer and battery reminders, notifications on the island, each flash, the camera and microphone dots, taking over the volume keys, wheel volume, clipboard history, screenshots to the shelf, and sounds (all, or by kind).
- **Values:** reminder intervals and snoozes, battery thresholds, how long notifications and flashes show, the charging animation's length, prayer offsets and angles, the Asr method (Shafi'i or Hanafi), call words, volume step, clipboard size and sound volume.
- **Linked switches:** turning notifications off the island brings Windows' own banners back. Turning the volume flash off gives the volume keys back to Windows, so its own pop-up shows.
- **Preview** buttons show a reminder or animation on the island without changing anything real: no water timer reset, no prayer marked.
- **About:** open the data folder, run the self-checks, quit, or reset every setting (click twice). Reset keeps your tasks, prayer log and pins.
- **Start with Windows** (General) adds or removes the shortcut in your Startup folder.

Settings are saved in `settings.txt` in the [data folder](#where-data-is-stored). A damaged file never stops the app: unknown lines are ignored, and bad values fall back to their defaults.

---

## Todo panel

- **Add a task:** type in the box at the top and press Enter.
- **Change status:** click a task to cycle it through *to do* (empty ring), *ongoing* (blue dot) and *done* (green tick, struck through).
- **Rename:** double-click the text, edit, then press Enter or click elsewhere. Esc cancels.
- **Delete:** hover a task and click the **×** on its right.
- **Move the panel:** drag it by any empty area. It remembers its position.
- **Header buttons:** the moon opens the Control Center on the Prayer page, and the gear opens the [Control Center](#control-center). The **×** hides the panel. Bring it back with `Ctrl+Alt+T`.
- The count at the top shows how many tasks are not done yet.

The panel uses a dark glass tint, so it stays readable on white pages as well as dark ones.

---

## The island

The island sits at the very top centre of the screen and changes shape with smooth spring animations.

**Resting states:**

| State | What you see |
|---|---|
| Idle | A thin glass line tucked into the top edge |
| Timer running | A small pill showing the time left, for example `4:59` |
| Shelf holding files | A small pill showing, for example, `3 files` |
| Camera or microphone in use | A green dot (camera) or orange dot (microphone) inside the island at its right end. In the thin line only the part of the dot that fits shows; hover it to see which apps |

**Hover** over the top centre to open the media player. From the player you can open the timer, clipboard, shelf and mirror. When a pill shows the shelf count, hovering opens the shelf instead.

**Reminders** (prayer, water, battery, timer, notifications) take over the island when they are due. They grow from a small pill into a card with two buttons. Afterwards the island goes back to its resting state.

**Priority order** (highest first): timer alarm, battery, prayer, water, Windows notifications. A higher one replaces a lower one that is waiting, and the lower one comes back afterwards. Calls and the timer alarm stay until you answer them.

**Do not disturb** (Control Center: General, or the bell button at the top right of the media player, slashed and purple while on) keeps every reminder and notification off the island. Prayer, water and battery reminders that come due wait until it is off. Windows notifications are dropped from the island but stay in Windows' Notification Center. The timer alarm, Control Center previews and flashes still show.

While your mouse is on the island, or the mirror is open, reminders wait. Calls and the timer alarm do not wait.

**Flashes** are brief messages without buttons. Each one shows briefly:

| Flash | When |
|---|---|
| **Charging** | You plug in the charger. The island springs into a wide pill: a battery fills to the level (green, or orange at 20% and below) with a glow and a shine, a lightning bolt pops in, and the percentage counts up. The percentage shows three small decimals and keeps climbing at the battery's real charge rate, and the subtitle shows the charging power in watts. A power-up glide and rising chime play. About 5 seconds (Control Center: Battery) |
| **On battery** | You unplug the charger. The reverse: the battery starts green and glowing, the glow fades, the fill cools to white (orange at 20% and below), and the bolt shrinks, drops and fades away. A falling glide and two descending notes play. About 5 seconds |
| **Volume** | Volume or mute changes from keys, the taskbar or an app. A bar shows the level; holding a key moves the bar smoothly |
| **Brightness** | Built-in screen brightness changes, including automatic brightness |
| **Bluetooth** | A device connects (with its battery level when Windows knows it) or disconnects |
| **Screenshot** | A new screenshot is saved. It goes onto the [file shelf](#file-shelf), and the flash shows its thumbnail |

Flashes last about 2.2 seconds (Control Center: Flashes), and each kind can be switched off there. Flashes never interrupt a reminder, an open panel, or the player while your mouse is on it.

**Volume and brightness pills can be adjusted:** scroll on the pill (2% per notch), or click or drag its bar. The pill stays while the mouse is on it.

**Volume keys** are handled by the island itself: it takes the volume up, down and mute keys before Windows does, so Windows' own volume pop-up no longer appears, and the pill shows even at 0% and 100%. If WinIsle closes, Windows handles the keys again.

**Mouse volume:** hold the mouse wheel down and scroll, anywhere on screen: up raises the volume, down lowers it, 2% per notch, with the island's pill. The page under the mouse doesn't scroll meanwhile. A wheel press released without scrolling still works as a normal middle click (open link in new tab, close tab); only holding the wheel down to drag (browser autoscroll, panning in some apps) is taken over.

**Brightness keys** are handled by the laptop's firmware, which Windows apps can't intercept. So Windows' brightness pop-up still appears, and pressing past 0% or 100% shows nothing (no change happens, so nothing is reported).

**Sounds:** each reminder plays a short sound. Water uses water drops. Battery and timer alarms loop an alarm-clock beep until you answer them or the condition is gone.

---

## Prayer reminders and dashboard

### Reminders

- At each prayer time, the island shows the prayer with **Not yet**, **Later** and **Prayed**.
- **Not yet** brings the reminder back every 10 minutes until you press **Prayed**.
- **Later** offers 30 min, 1 hr, 2 hr and 3 hr. If the chosen time reaches the next prayer's time, the pill says **Skipped**: the reminder doesn't come back, and the prayer counts as **Missed** unless you mark it prayed in the dashboard.
- If you never press **Prayed** before the next prayer starts, that prayer counts as **Missed**.
- The Fajr reminder stops at sunrise. Fajr is still marked Missed only when Dhuhr starts.
- If the PC was off during a prayer, that prayer counts as Missed. You can correct it in the dashboard.

### Prayer times

Times are calculated offline:

- Default location: Batticaloa, Sri Lanka (7.717, 81.70)
- Fajr and Isha: 18°
- Asr: Shafi'i method

These were checked against aladhan.com to within one minute. The ACJU (Sri Lanka) timetable may differ slightly. The angles, the Asr method (Shafi'i or Hanafi) and a per-prayer offset in minutes can be changed on the Control Center's Prayer page.

### Dashboard

The dashboard is the Prayer page of the [Control Center](#control-center). Open it with `Ctrl+Alt+P` or the moon button on the todo panel.

- **Location & time zone:** set place name, latitude, longitude and time zone.
- **Prayer reminders** (below the history): switch reminders off, change the snooze, the Fajr and Isha angles, the Asr method and each prayer's offset. The times on the page update as you change them.
- **Today:** shows each prayer's status. Click a cell to correct it.
- **History and charts:** show prayed and missed counts over time, streaks and per-prayer analytics. Click a history cell to correct a past day.
- Prayed is shown in blue and Missed in orange, each with a shape icon, so the colours remain distinguishable for colour-blind users.

---

## Water reminder

- Every 90 minutes, the island asks whether you have had a drink.
- **Drank** starts the next 90 minutes. **Not yet** asks again in 10 minutes.
- The timer starts again each time the app starts.

---

## Battery alarm

| Battery | Charger | Island shows |
|---|---|---|
| 15% or lower | Unplugged | **Battery low**, plug in the charger (orange) |
| 95% or higher | Plugged in | **Battery charged**, unplug the charger (green) |

- A looping alarm sound plays while the alert is showing.
- **Dismiss** stops it for 1 minute, then it returns if nothing has changed.
- **Settings** opens Windows battery settings, and the alert returns after 1 minute.
- Plugging in or unplugging answers the alert at once: the alarm stops and the alert pill morphs straight into the charging or unplug animation.
- On a PC without a battery, this feature does nothing.
- If Windows smart charging stops the battery at 80%, the 95% alert never appears.

---

## Windows notifications on the island

Notifications from all apps appear on the island with:

- the app's icon filling the circle
- the title and message text, up to 3 lines
- a soft chime
- **Open** and **Dismiss** buttons

They close by themselves after 6 seconds.

- **Click the text** to expand the island and see the full message. A merged group lists every message. An expanded notification stays until you click a button.
- **Merged:** several notifications from the same app in a row become one, for example "2 new · latest message".
- **Calls** stay until you click a button. They are detected from English text such as "Incoming voice call", "Video call" or "calling".
- **Open** launches the app. It cannot open the exact chat or email, because Windows does not share that link.

**Windows banners:** to avoid seeing every notification twice, the app turns off Windows' own pop-up banners for each app. Notifications still go to the Windows Notification Center.

To bring the Windows banners back:

1. Quit WinIsle with `Ctrl+Shift+Q` in the panel.
2. Run `WinIsle.exe --restore-banners`.

While WinIsle is closed, banners stay off. New notifications then go only to the Notification Center.

---

## Media player

Hover over the top centre to open the player. It hangs from the top edge of the screen.

- It shows whatever Windows is playing, from Spotify, a browser, a media player or another app: cover art, title, artist and app icon.
- The timeline shows elapsed and remaining time. Click the bar to seek.
- **Volume:** scroll anywhere on the open player to change the volume (2% per notch), or click or drag the volume bar under the buttons. The bar follows every volume change, from keys or other apps too. Scrolling keeps its own job in the timer wheels, the shelf list and the volume/brightness pill.
- Buttons: previous, play/pause and next.
- Tool buttons: timer and clipboard on the left; shelf and mirror on the right.
- Paused media stays available, so you can resume from the island. With no media app open, it shows "Nothing playing".
- Move the mouse away and the island tucks back up.

---

## Timer

Open it with the stopwatch button in the player.

- Three rolling wheels: **hours : minutes : seconds**, up to 23:59:59.
- **Scroll** a wheel to change it. Scrolling up brings the smaller numbers above into the centre. You can also click a faded number to jump to it.
- Each step plays a tick and rolls with a motion blur and a small overshoot.
- The white button starts and pauses, the square resets, and the arrow goes back to the player.
- While it runs, the wheels count down and are locked. The top edge shows the time left.
- The timer panel stays open when the mouse leaves. Use the back arrow to close it.
- At zero, **Time's up** rings a looping alarm. **+1 min** snoozes; **Stop** ends it.
- A running timer is not kept if the app restarts.

---

## Mirror

Open it with the mirror button in the player. The island becomes a live, mirrored view from the laptop camera.

- Click the mirror to turn the camera off.
- The camera light is on only while the mirror is showing. Frames stay in memory and are never saved.
- If another app is using the camera, the mirror shares it.
- If Windows blocks camera access for desktop apps, the mirror tells you so. To fix it, go to **Settings > Privacy & security > Camera** and turn on **Let desktop apps access your camera**.

---

## File shelf

The shelf temporarily holds files while you work.

- **Add:** drag files or folders from Explorer or the desktop to the very top centre of the screen. The island opens as a shelf; drop them there. You can also open it with the shelf button in the player.
- **Use:** each file shows its Explorer thumbnail and name. Click to open it. Drag a tile, or use **Drag all**, out to any folder or app.
- **Copy or move:** Windows decides when you drop, exactly as when dragging from Explorer. Files that are moved away leave the shelf.
- **Remove:** **×** on a tile removes it from the shelf. The bin clears the shelf. Your files are never deleted.
- **Screenshots:** turn on the switch next to the camera icon in the shelf's header, and every new screenshot from Snipping Tool (`Win+Shift+S` or Print Screen) goes onto the shelf automatically, ready to drag into a chat or email. It is off until you turn it on, and remembers your choice. This needs Snipping Tool's **Automatically save screenshots** setting, which is on by default; it saves to `Pictures\Screenshots`.
- Files deleted or moved elsewhere leave the shelf on their own within a second.
- The shelf stores file paths, not copies, and remembers them after a restart.

---

## Clipboard history

Open it with `Ctrl+Alt+V` or the clipboard button in the player. It hangs from the top edge like the island.

It records what you copy: text, links (shown in blue), images (shown as thumbnails with their size) and files (shown with Explorer thumbnails). Copying something again moves it to the top. Up to 60 items are kept, plus pinned items.

| Action | How |
|---|---|
| Search | Type |
| Choose | `↑` `↓`, `Page Up` / `Page Down`, or move the mouse |
| Paste into the app you were in | `Enter` or click |
| Paste item 1–9 directly | `Ctrl+1` … `Ctrl+9` |
| Pin or unpin (pins stay at the top) | `Ctrl+P` or the pin button |
| Remove | `Del` (`Shift+Del` while searching) or the bin button |
| Close | `Esc` (clears the search first), `Ctrl+Alt+V`, or click elsewhere |

Notes:

- The panel never takes the keyboard focus, so the text cursor stays where it was in your app and a paste lands exactly there. While it is open, the panel reads only its own keys (the ones above, plus typed characters for the search); everything else, including other Ctrl and Alt shortcuts, still reaches your app.
- History is kept in memory and clears when the app restarts. Pinned **text** is saved to disk. Pinned images and files are kept only until the app closes.
- Text is pasted as plain text, without formatting.
- Pasting works by sending `Ctrl+V`. Apps running as administrator may ignore it, but the item is still on the clipboard, so you can paste it yourself.

---

## Where data is stored

Everything is in `C:\Users\<you>\AppData\Local\WinIsle\`, also reachable as `%LOCALAPPDATA%\WinIsle`:

| File | Contents |
|---|---|
| `todos.txt` | Tasks, one per line: `[ ] ` to do, `[-] ` ongoing, `[x] ` done |
| `position.txt` | Todo panel position |
| `prayed.txt` | Prayer log: when tracking started, then each prayed prayer and time |
| `prayer-settings.txt` | Prayer location and time zone |
| `clipboard-pins.txt` | Pinned clipboard text, one Base64 entry per line |
| `shelf.txt` | Paths of files on the shelf |
| `settings.txt` | Every Control Center setting, as `key=value` lines. Replaces `shelf-settings.txt`, which is read once and then no longer used |

None of these files are part of the Git repository. If an older version kept data next to `WinIsle.exe`, it is copied here once, and existing files are never overwritten.

To back up or move your data, copy this folder.

---

## Privacy and permissions

- **Notifications:** the app reads the text of every Windows notification to show it on the island. Windows asks for, or already has, this permission under **Settings > Privacy & security > Notifications**. Nothing is saved or sent anywhere.
- **Clipboard:** everything you copy is recorded, **including passwords** copied from password managers, by choice. Anyone at your PC can open the history with `Ctrl+Alt+V`. Remove sensitive items with `Del`. Pinned text is saved to disk as Base64, which is not encryption, so avoid pinning passwords.
- **Camera:** used only while the mirror is open. Nothing is recorded.
- **Camera and microphone dots:** read from the record Windows keeps for its own privacy indicator. WinIsle does not use the camera or microphone to do this.
- **Screen:** the island and clipboard panel take a blurred snapshot of the screen behind them to draw their glass. It is kept only in memory and is never saved.
- **Network:** the app makes no internet connections. Prayer times are calculated on your PC.

---

## Building and running

**Requirements:** Windows 10 or 11 with .NET Framework 4.x, which is included with Windows. No other installs are needed.

```cmd
build.cmd
```

`build.cmd` calls `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` on every `.cs` file under `src\`. It references WPF, `System.Drawing` and the Windows Runtime metadata in `%WINDIR%\System32\WinMetadata`, used for notifications, media, camera and battery. The output is `WinIsle.exe` in the project root. An optional first argument writes the exe somewhere else, for example `build.cmd "%TEMP%\WinIsleCheck.exe"` to run `--check` on a copy while the app keeps running.

The running app locks `WinIsle.exe`, so quit it with `Ctrl+Shift+Q` before rebuilding. The compiler supports C# 5 only, so newer features such as `?.`, `$"..."` and `out var` will not compile.

A warning about async calls not being awaited (CS4014) is expected. The media controls are deliberately started without waiting.

---

## Command-line options

| Option | What it does |
|---|---|
| `WinIsle.exe` | Normal start |
| `WinIsle.exe --check` | Runs the built-in self-checks and exits. The exit code is the number of failures, so `0` means all passed |
| `WinIsle.exe --demo` | Shows a prayer reminder island alone. "Not yet" comes back after 10 seconds |
| `WinIsle.exe --demo-water` | Same, for the water reminder |
| `WinIsle.exe --demo-charge` | Plays the charging animation once (at 72%) and exits |
| `WinIsle.exe --demo-unplug` | Plays the unplug animation once (at 72%) and exits |
| `WinIsle.exe --demo-power` | Plays the whole battery story with a pretend battery (low alert, plug in, full alert, unplug), about 20 s, then exits |
| `WinIsle.exe --restore-banners` | Turns Windows notification banners back on for every app. Quit WinIsle first |

The self-checks cover:

- prayer times and Missed rules
- streaks and water intervals
- data moving
- sound files
- notification merging, calls and banners
- media timing
- battery thresholds
- flashes (charging, volume, brightness, Bluetooth battery matching, screenshots, camera/mic app names)
- timer and wheels
- clipboard history
- file thumbnails
- shelf
- settings (loading, clamping, damaged files) and every feature following them

---

## Tuning

Most values now live in the [Control Center](#control-center) and apply at once: reminder intervals and snoozes, battery thresholds, prayer offsets and angles, the Asr method, notification time and call words, flash and charging-animation length, volume step, clipboard size and sound volume. Their defaults are in `src/Shared/Settings.cs`.

What is still set in code:

| What | Where |
|---|---|
| Sounds' notes and rhythm | `src/Island/IslandSound.cs` (generated in code; there are no sound files) |
| Island sizes (regular and small) | `Regular`, `Small` in `src/Island/IslandWindow.cs` |
| Play-icon optical centring | `PlayNudge` in `src/Island/IslandWindow.cs` |

Rebuild after any change.

---

## Project layout

```
build.cmd                     Builds WinIsle.exe
src/
  Program.cs                  Entry point, command-line options, starts the island and clipboard
  Checks.cs                   --check self-tests
  Shared/
    GlassWindow.cs            Base window: colours, animation helpers, acrylic, screen snapshot
    AppPaths.cs               Data folder on C:
    Settings.cs               Every Control Center setting, kept in settings.txt
    Startup.cs                Start with Windows (Startup folder shortcut)
  Todo/TodoPanel.cs           Todo panel and global hotkeys
  Dashboard/DashboardWindow.cs  Shared widgets: cards, pills, dropdowns, switches, steppers
  ControlCenter/
    ControlCenter.cs          The window, page list and page switching
    ControlCenter.Pages.cs    The settings pages
    ControlCenter.Prayer*.cs  The Prayer page (dashboard, charts, location)
  Prayer/                     Prayer times, tracker, reminder
  Water/WaterReminder.cs
  Battery/
    BatteryReminder.cs        Low / full battery alarm
    ChargeWatcher.cs          Charging flash
  Audio/VolumeWatcher.cs      Volume flash (Windows Core Audio)
  Display/BrightnessWatcher.cs  Brightness flash (WMI)
  Bluetooth/BluetoothWatcher.cs Bluetooth connect / disconnect flash with battery
  Privacy/PrivacyWatcher.cs   Camera / microphone in-use dots
  Notifications/
    WindowsNotifications.cs   Reads Windows notifications, hides banners, detects calls
    AppIcon.cs                App icons and file thumbnails from the Windows Shell
  Media/MediaSession.cs       What's playing, and media controls
  Mirror/Camera.cs            Camera frames for the mirror
  Timer/
    CountdownTimer.cs         Timer logic and alarm
    TimeWheel.cs              Rolling hh:mm:ss wheel
  Clipboard/
    ClipboardHistory.cs       Records clipboard changes, pins, pasting
    ClipboardWindow.cs        Clipboard panel
  Shelf/
    FileShelf.cs              Files held on the shelf
    ScreenshotWatcher.cs      New screenshots onto the shelf
  Island/
    Reminder.cs               IReminder contract and IslandNotice
    PreviewReminder.cs        The Control Center's Preview buttons
    IslandWindow.cs           The island: shapes, reminders, player, mirror, timer
    IslandWindow.Shelf.cs     The island's file shelf
    IslandWindow.Flash.cs     Flashes and the camera / microphone dots
    IslandSound.cs            Generated sounds
```

**Adding a new island reminder:**

1. Implement `IReminder` (`Due`, `StillCurrent`, `Answer`).
2. Add it to the list in `Program.StartReminders`, which is ordered highest priority first.

`IslandNotice` sets the title, detail, buttons, icon, colour, sound and options such as `Small`, `Sticky`, `LoopSound` and `More`.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| "Ctrl+Alt+T is already taken" | WinIsle is already running, or another app uses that shortcut |
| Build fails: "Could not write to output file ... WinIsle.exe" | The app is still running. Quit it with `Ctrl+Shift+Q`, wait a second, and build again |
| Notifications don't reach the island | Check **Settings > Privacy & security > Notifications** and allow apps to access notifications |
| Seeing both a Windows banner and the island | A brand-new app shows one Windows banner before the island takes over. If banners keep appearing, sign out and back in |
| Want Windows banners back | Quit WinIsle, then run `WinIsle.exe --restore-banners` |
| Mirror says camera access is off | **Settings > Privacy & security > Camera** > turn on **Let desktop apps access your camera** |
| Clipboard paste does nothing in one app | That app may run as administrator. The item is still on the clipboard; press `Ctrl+V` yourself |
| A prayer time is a minute or two off your mosque's timetable | Adjust `Offset` (see [Tuning](#tuning)) |
| Screenshots don't go onto the shelf | Turn on the camera switch in the shelf's header. Also check Snipping Tool settings: **Automatically save screenshots** must be on (WinIsle watches `Pictures\Screenshots`; restart WinIsle if that folder was just created) |
| Brightness flash appears by itself | Automatic (adaptive) brightness is changing it; that counts as a change |
| Shift a prayer that the PC was off for | Open the dashboard and click that prayer's cell to correct it |

---

## Known limitations

- The island's glass is a blurred snapshot taken when it opens, so it doesn't update if windows move behind it.
- Notification **Open** can't jump to the exact chat or email. Windows doesn't share that link with other apps.
- Call detection works on English text only.
- The water timer, a running countdown timer and unpinned clipboard history reset when the app restarts.
- The thin top-edge peek also shows over full-screen videos and games.
- Windows notifications and media are checked once a second, which uses a little CPU continuously.
- Windows' brightness pop-up can't be turned off, and the brightness pill can't show at 0% or 100%: the keys are handled by the laptop firmware.
- Media keys (play/pause, next) still show Windows' own pop-up.
- Bluetooth mice and keyboards that reconnect after sleep also flash. Battery level is known only for devices Windows shows a battery for in Settings.

---

## Contributing

Issues and pull requests are welcome. Keep to C# 5 (see [Building and running](#building-and-running)) and run `WinIsle.exe --check` before sending a change.

## License

[MIT](LICENSE)
