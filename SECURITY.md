# Security policy

## Supported versions

Only the latest release receives fixes. Update to the newest [WinIsle-Setup.exe](https://github.com/NihapMrm/WinIsle/releases/latest) before reporting a problem.

## Reporting a vulnerability

Please **don't** open a public issue for a security problem.

Report it privately through GitHub instead: [report a vulnerability](https://github.com/NihapMrm/WinIsle/security/advisories/new). Include:

- what the problem is and what an attacker could do with it,
- the steps to reproduce it,
- the WinIsle and Windows versions you tested.

You'll get a reply within 7 days. Once a fix is released, the advisory is published and you'll be credited, unless you'd rather not be.

## What WinIsle can access

WinIsle runs as your user, without administrator rights, and makes no network connections. It reads:

- the text of Windows notifications, to show them on the island,
- the clipboard, for clipboard history,
- the camera, only while the mirror is open,
- the Windows record of which apps use the camera and microphone.

It writes only to `%LOCALAPPDATA%\WinIsle`, the per-app notification banner settings under `HKEY_CURRENT_USER`, and, if you turn on Start with Windows, a shortcut in your Startup folder. The [user guide](docs/guide.md#privacy-and-permissions) has the details.

## Known risks

- **Clipboard history records passwords** copied from password managers. Anyone with access to your unlocked PC can open it with `Ctrl+Alt+V`. Pinned text is stored as Base64, which is not encryption. You can turn clipboard history off in the setup or the Control Center.
- **The installer and the exe are not code-signed yet,** so Windows SmartScreen warns about them. Download WinIsle only from this repository's [releases](https://github.com/NihapMrm/WinIsle/releases).
