<div align="center">

<img src="assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Put the repetitive parts of your PC on autopilot**

Auto-launch your apps at login · timed reminders · one tap to run a whole routine

**[⬇ Download for Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, no installer

<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="License: MIT"></a> <a href="https://github.com/rockbenben/365opensource"><img src="https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-3466b2" alt="365 Open Source Plan #020"></a>

</div>

<div align="center">

**English** · [简体中文](README.zh.md) · [繁體中文](docs/i18n/README.zh-Hant.md) · [日本語](docs/i18n/README.ja.md) · [한국어](docs/i18n/README.ko.md) · [Deutsch](docs/i18n/README.de.md) · [Español](docs/i18n/README.es.md) · [Français](docs/i18n/README.fr.md) · [Italiano](docs/i18n/README.it.md) · [Nederlands](docs/i18n/README.nl.md) · [Português](docs/i18n/README.pt.md) · [Русский](docs/i18n/README.ru.md) · [Türkçe](docs/i18n/README.tr.md) · [Tiếng Việt](docs/i18n/README.vi.md) · [ไทย](docs/i18n/README.th.md) · [Bahasa Indonesia](docs/i18n/README.id.md) · [हिन्दी](docs/i18n/README.hi.md) · [العربية](docs/i18n/README.ar.md)

</div>

![Clockwork's startup list — an ordered set of login steps, each with its own type, delay and conditions](assets/screenshot.png)

## What it does

- 🚀 **Startup list** — open your everyday apps in order at login, with per-step delays, weekday conditions and window styles; close, focus or mute things along the way. Steps can also be gated on what the machine is doing: only while an app is running (or isn't), only on AC or only on battery, only when a file or folder exists.
- ⏰ **Scheduled tasks** — a reminder on time, spoken if you like, or an action run silently. Clicking **Yes** can launch a program, open a file or URL, or fire a group. Or trigger on an event instead of a clock — on unlock, on lock, on wake from sleep, after N minutes idle, when the charger goes in or out, or when the battery runs low. Need something once, right now? The tray has a **quick reminder** — 5 to 60 minutes, fires once and deletes itself. More triggers watch the hardware — a display change, the network coming back or dropping out, a USB drive plugged in — and one watches you instead, firing after N minutes of unbroken use so you remember to get up.
- 🎛️ **Actions** — bundle a routine (Focus / Meeting / Wrap-up / Bedtime…) and fire it from the tray, a **global hotkey**, the startup list or a scheduled task. Templates included. Steps can pass values along: **user input, user choice** and **get the selected text** each save into a variable, and later steps refer to it as `{name}` in a URL or a piece of text — "ask me what to search, then search it" is two steps.
- 🧹 **System startup items** — everything on your PC that auto-starts, in one list: switch off what you don't need (disabled, not deleted) or take it over into your own startup list.
- 🔌 **Ports** — every TCP port being listened on, with the process holding it and the project it came from: double-click a row to open `localhost:3000` in your browser, right-click to free the port, ending every process holding it along with their children. By default only services started from your own project directories show up, so the dev server you forgot to stop is not buried under chat apps and system services.
- ⚡ **Quick panel** — one hotkey (default `Ctrl+Alt+Space`) pops a tile grid right where your mouse already is: your own actions, plus rerun, stop, do-not-disturb and open-window. Click a tile, or arrow to it and press Enter; **Esc** or a click elsewhere dismisses it. It's on the tray menu too, so clearing the hotkey turns off the shortcut, not the feature — and **Settings → Use the quick panel** turns off the feature itself, hotkey and middle button together. Prefer the mouse? Switch on **hold the middle button** and it opens with no keyboard at all — a normal middle click still works. Panel pages are yours to make and arrange in the panel manager, and every tile on one is a single operation — so lock the screen, mute, launch an app, open a URL and "run a whole action" all sit side by side. Once you have a lot of pages, group them: **the top row picks a category, the left row lists that category's pages**, and both rows sort by dragging; with nothing categorised everything sits under *Uncategorised* and the category row never appears.
- 🖱️ **Mouse gestures** — hold the **right button**, draw a stroke, and the matching action runs. Eight directions, any step kind (including running a whole action). The stroke is drawn on screen as you go, and disappears when you release. **Ten come ready to use** (copy / paste / back / forward / search the selected text (↑↓) / jump to the bottom, plus the four diagonals for minimise, maximise, toggle-on-top and close) — use them as they are or edit them. The cost, stated up front: one enabled gesture takes over the right button, so **right-drag needs a short pause first** — press, hold still for about 0.2 s, then drag (dragging files with the right button in Explorer, rotating in 3D apps); there is a master switch on the **Settings** tab — **Use mouse gestures** — and another in the gesture manager's title row, for when you want the right button left alone entirely. You don't have to use the built-in ones: turn that switch off, let WGestures / StrokesPlus / Quicker do the drawing, and keep the actions here via `Clockwork.exe --run-group "Focus"`.

> **Stop anytime** — the stop button at the right end of the tab bar (it only shows while something is running), tray → **Stop running actions**, or the global panic hotkey (default `Ctrl+Alt+Q`). Long waits are cut short, not waited out.

## Works with

| Aspect | Detail |
| --- | --- |
| **System** | Windows 10 / 11, x64 |
| **Install** | None. A single portable `Clockwork.exe` — put it in any folder |
| **Admin** | Only for "Start at login" and for steps you mark **run as admin** |
| **Your setup** | `clockwork.settings.json` next to the exe (or `%APPDATA%\Clockwork\` if that folder was read-only on first run; it stays wherever it landed) — nothing leaves the machine |
| **Interface** | 18 languages and a dark / light theme. The language follows Windows on first run; the theme starts dark, and can be set to follow Windows |

**Limits.** No installer means no auto-update — grab the new zip and replace the exe. Sandboxed launchers block send-keys, mouse actions, window actions, activate-if-running and volume (you get a clear notice; plain "launch program" still works). Key remapping and text expansion stay out of scope — that's AutoHotkey's job.

## Getting started

1. Download the latest release from [Releases](https://github.com/rockbenben/Clockwork/releases) — two builds, three downloads — and drop the single `Clockwork.exe` you end up with into any folder.
   - **`Clockwork-<version>-win-x64.zip`** — .NET runtime included, runs as-is on any Windows 10/11. Take this if you're unsure, or if the PC is offline or locked down.
   - **`Clockwork-<version>-win-x64-needs-dotnet10.zip`** — needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed. Install it once on an online PC and every update after that is a small download.
   - **`Clockwork.exe`** — the same build as the zip above, with no zip around it: click, run, or drop it over your existing copy to update. Windows offers the runtime download if it is missing.
2. Double-click it to open the settings window. The samples it loads all start **unticked** — nothing runs until you tick it.
3. To run it every boot: on the **Settings** tab, tick **Start at login** (registers a scheduled task with admin rights, so boot brings no wall of UAC prompts).

It then sits in the tray: double-click the icon to open the window, and the window's close button only hides it again. Quit for real from the tray's right-click **Exit**.

> [!IMPORTANT]
> **The exe isn't code-signed**, so SmartScreen shows "Windows protected your PC" on first run — click **More info → Run anyway**. Some antivirus may flag it too, because writing registry Run keys and scheduled tasks is exactly what a startup manager does and also what malware does; there is no way to tell those apart from the outside. If you'd rather not take that on faith, [build it yourself](CONTRIBUTING.md) — same result, your own binary. Every release also ships a `SHA256SUMS.txt` and a GitHub build attestation: `gh attestation verify <file> -R rockbenben/Clockwork` proves a download was built by this repository's CI, not on someone's laptop.

**Full guide** — every field, every edge case: [English](docs/USAGE.md) · [中文](docs/USAGE.zh.md)

## Tips

- **Double-click a row to edit** it. Paths, processes and dates are filled in for you: **the … button at the end of a row** opens the matching picker (a file, a searchable process list, a date), and **Capture** records a shortcut by pressing it.
- **Drag a row to reorder it** — in all three lists and in the group editor's step list; the up/down buttons still work.
- **Try it before you save** — the group editor's **▶ Run This Step** and **▶ Run Group** run what's currently on screen, and the button turns into **■ Stop** while it goes.
- **Duplicate** clones the selected task or group right below it — quicker than rebuilding a near-identical one. **Deleting always asks first**, everywhere.
- Double-clicking `Clockwork.exe` only opens the window; it does **not** re-run the startup list. Use the tray's **Rerun startup list** for that.

## About the 365 Open Source Plan

Project **#020** of the [365 Open Source Plan](https://github.com/rockbenben/365opensource) — one person + AI, 300+ open-source projects in a year.

[Submit your idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
