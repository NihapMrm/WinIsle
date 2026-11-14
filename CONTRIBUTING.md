# Contributing to WinIsle

Thanks for helping. Bug reports, ideas, documentation fixes and code are all welcome.

## Ways to help

- **Report a bug:** open a [bug report](https://github.com/NihapMrm/WinIsle/issues/new?template=bug_report.yml). Include your Windows version, the WinIsle version (Control Center › About) and the steps that cause it.
- **Suggest a feature:** open a [feature request](https://github.com/NihapMrm/WinIsle/issues/new?template=feature_request.yml) and describe the problem it solves before the solution.
- **Improve the docs:** the [README](README.md), the [user guide](docs/guide.md) and the [install page](docs/index.html) all live in this repository.
- **Send code:** for anything bigger than a small fix, open an issue first so we can agree on the approach before you spend time on it.

## Set up

You need Windows 10 or 11 and Git. Nothing else: the build uses the C# compiler and .NET Framework that come with Windows.

```cmd
git clone https://github.com/NihapMrm/WinIsle.git
cd WinIsle
build.cmd
WinIsle.exe --check
```

`--check` runs the self-tests and exits. Its exit code is the number of failures, so `0` means everything passed.

Only one copy of WinIsle runs at a time. If you have it installed and running, quit it with `Ctrl+Shift+Q` in the todo panel before starting your build. To run the checks while WinIsle keeps running, build to another path:

```cmd
build.cmd "%TEMP%\WinIsleCheck.exe"
"%TEMP%\WinIsleCheck.exe" --check
```

The `--demo`, `--demo-water`, `--demo-charge`, `--demo-unplug` and `--demo-power` options show one island animation on its own, which helps when working on the island. See [Command-line options](docs/guide.md#command-line-options).

## Code rules

- **C# 5 only.** The compiler that ships with Windows stops at C# 5. `?.`, `$"..."`, `nameof`, `out var`, expression-bodied members and auto-property initializers don't compile.
- **No new dependencies.** No NuGet packages and no extra DLLs. Use the .NET Framework, WPF and the Windows Runtime metadata that `build.cmd` already references.
- **Settings apply at once.** Features read `Settings` at the moment they act, so a change in the Control Center takes effect without a restart. Keep it that way.
- **Fail quietly where hardware is missing.** A PC without a battery, camera or Bluetooth must still start and run.
- **Keep data local.** WinIsle makes no network connections. Data goes in `%LOCALAPPDATA%\WinIsle` through `AppPaths`.
- **Match the code around you:** small classes, comments that say *why*, and one feature per folder under `src/`.

## Common changes

**A new setting.** Add a key in `src/Shared/Settings.cs` and register its default in the static constructor with `Flag`, `Number` or `Words`. Show it in the Control Center from `src/ControlCenter/ControlCenter.Pages.cs` with `Toggle`, `Number` or `Words`. If it is a feature switch, add it to the first-run setup in `src/Setup/SetupWindow.cs` too.

**A new island reminder.** Implement `IReminder` (`Due`, `StillCurrent`, `Answer`) from `src/Island/Reminder.cs`, then add it to the list in `Program.StartReminders`, which is ordered highest priority first. `IslandNotice` sets the title, buttons, icon, colour and sound.

**A new Control Center page.** Add its name to `ControlCenter.Pages` and build its cards in `BuildPage` in `ControlCenter.Pages.cs`.

**Tests.** Add checks to `src/Checks.cs`. Each area has a method that returns its number of failures and is added up in `Checks.Run`. Use a temporary settings file (`Settings.Load` with a path under `%TEMP%`) so checks never touch real data.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make your change, and update the README or the user guide if behaviour changes.
3. Run `build.cmd` and `WinIsle.exe --check`. Both must pass.
4. Try the change in the running app. Say in the pull request what you tested.
5. Write commit messages in the style the history uses: `feat:`, `fix:`, `docs:` or `chore:`, then a short summary.
6. Open the pull request and fill in the template.

## Releases

The maintainer builds releases: `build.cmd`, then `iscc /DAppVersion=x.y.z installer.iss` with [Inno Setup 6](https://jrsoftware.org/isinfo.php), then a GitHub release with `dist\WinIsle-Setup.exe` and `WinIsle.exe`. Each release gets an entry in [CHANGELOG.md](CHANGELOG.md).

## Code of conduct

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md).
