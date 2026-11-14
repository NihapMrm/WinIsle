# Changelog

All notable changes to WinIsle are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Contributing guide, code of conduct, security policy, issue and pull request templates.
- README with screenshots of each feature; the full manual moved to `docs/guide.md`.

## [1.2.0] - 2026-11-14

### Added
- First-run setup. It explains each feature, lets you turn it on or off, and has **Try it** previews. Control Center › About can run it again.

## [1.1.1] - 2026-11-14

### Fixed
- Starting WinIsle while it is already running now shows its todo panel instead of a "Ctrl+Alt+T is already taken" error.

## [1.1.0] - 2026-11-14

### Added
- Installer (`WinIsle-Setup.exe`): per-user, with a Start menu entry, optional Start with Windows, and an uninstaller that turns Windows notification banners back on.
- Install page at <https://nihapmrm.github.io/WinIsle/>.
- Logo and app icon.
- A Prayer switch on the Control Center's General page that hides the Prayer page, the moon button and prayer reminders.

### Changed
- The Prayer page moved below Clipboard in the Control Center.

## [1.0.0] - 2026-11-14

First public release, renamed from MyTodo to WinIsle.

### Added
- The island: reminders, Windows notifications, media player, timer, camera mirror and file shelf.
- Flashes for charging, volume, brightness, Bluetooth and screenshots, and camera and microphone dots.
- Water, battery and prayer reminders, with a prayer dashboard.
- Clipboard history with search, pins and paste.
- Todo panel.
- Control Center with every setting, applied at once.
- MIT license.

### Changed
- The data folder moved from `%LOCALAPPDATA%\MyTodo` to `%LOCALAPPDATA%\WinIsle`, automatically on first start.

[Unreleased]: https://github.com/NihapMrm/WinIsle/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/NihapMrm/WinIsle/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/NihapMrm/WinIsle/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/NihapMrm/WinIsle/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/NihapMrm/WinIsle/releases/tag/v1.0.0
