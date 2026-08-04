<div align="center">

<img src="assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Put the repetitive parts of your PC on autopilot**

Auto-launch your apps at login · timed reminders · one tap to run a whole routine

**[⬇ Download for Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, no installer

<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="License: MIT"></a>
<a href="https://github.com/rockbenben/365opensource"><img src="https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb" alt="365 Open Source Plan #020"></a>

</div>

<div align="center">

**English** · [简体中文](README.zh.md) · [繁體中文](docs/i18n/README.zh-Hant.md) · [日本語](docs/i18n/README.ja.md) · [한국어](docs/i18n/README.ko.md) · [Deutsch](docs/i18n/README.de.md) · [Español](docs/i18n/README.es.md) · [Français](docs/i18n/README.fr.md) · [Italiano](docs/i18n/README.it.md) · [Nederlands](docs/i18n/README.nl.md) · [Português](docs/i18n/README.pt.md) · [Русский](docs/i18n/README.ru.md) · [Türkçe](docs/i18n/README.tr.md) · [Tiếng Việt](docs/i18n/README.vi.md) · [ไทย](docs/i18n/README.th.md) · [Bahasa Indonesia](docs/i18n/README.id.md) · [हिन्दी](docs/i18n/README.hi.md) · [العربية](docs/i18n/README.ar.md)

</div>

![Clockwork's startup list — an ordered set of login steps, each with its own type, delay and conditions](assets/screenshot.png)

## What it does

- 🚀 **Startup list** — open your everyday apps in order at login, with per-step delays, weekday conditions and window styles; close, focus or mute things along the way.
- ⏰ **Scheduled tasks** — a reminder on time, spoken if you like, or an action group run silently. Clicking **Yes** can launch a program, open a file or URL, or fire a group.
- 🧹 **System startup items** — everything on your PC that auto-starts, in one list: switch off what you don't need (disabled, not deleted) or take it over into your own startup list.
- 🎛️ **Action groups** — bundle a routine (Focus / Meeting / Wrap-up / Bedtime…) and fire it from the tray, a **global hotkey**, the startup list or a scheduled task. Templates included.

> **Stop anytime** — the stop button at the right end of the tab bar (it only shows while something is running), tray → **Stop running actions**, or the global panic hotkey (default `Ctrl+Alt+Q`). Long waits are cut short, not waited out.

## Works with

| Aspect | Detail |
| --- | --- |
| **System** | Windows 10 / 11, x64 |
| **Install** | None. One `Clockwork.exe` with the .NET runtime inside it — put it in any folder |
| **Admin** | Only for "Start at login" and for steps you mark **run as admin** |
| **Your setup** | `clockwork.settings.json` next to the exe (or `%APPDATA%\Clockwork\` if that folder is read-only) — nothing leaves the machine |
| **Interface** | 18 languages, following your Windows display language on first run |

**Limits.** No installer means no auto-update — grab the new zip and replace the exe. Sandboxed launchers block send-keys, window actions, activate-if-running and volume (you get a clear notice; plain "launch program" still works). Key remapping and text expansion stay out of scope — that's AutoHotkey's job.

## Getting started

1. Download the latest `Clockwork-<version>.zip` from [Releases](https://github.com/rockbenben/Clockwork/releases), unzip it, and drop the single `Clockwork.exe` into any folder.
2. Double-click it to open the settings window. The samples it loads all start **unticked** — nothing runs until you tick it.
3. To run it every boot: on the **Settings** tab, tick **Start at login** (registers a scheduled task with admin rights, so boot brings no wall of UAC prompts).

It then sits in the tray: double-click the icon to open the window, and the window's close button only hides it again. Quit for real from the tray's right-click **Exit**.

> [!IMPORTANT]
> **The exe isn't code-signed**, so SmartScreen shows "Windows protected your PC" on first run — click **More info → Run anyway**. Some antivirus may flag it too, because writing registry Run keys and scheduled tasks is exactly what a startup manager does and also what malware does; there is no way to tell those apart from the outside. If you'd rather not take that on faith, [build it yourself](CONTRIBUTING.md) — same result, your own binary.

**Full guide** — every field, every edge case: [English](docs/USAGE.md) · [中文](docs/USAGE.zh.md)

## Tips

- **Double-click a row to edit** it. Paths, processes, shortcuts and dates are filled in for you: **Browse…**, **Pick…** (searchable process picker), **Capture**, **Pick date**.
- **Drag a row to reorder it** — in all three lists and in the group editor's step list; the up/down buttons still work.
- **Try it before you save** — the group editor's **▶ Run This Step** and **▶ Run Group** run what's currently on screen, and the button turns into **■ Stop** while it goes.
- **Duplicate** clones the selected task or group right below it — quicker than rebuilding a near-identical one. **Deleting always asks first**, everywhere.
- Double-clicking `Clockwork.exe` only opens the window; it does **not** re-run the startup list. Use the tray's **Re-run startup list** for that.

## About the 365 Open Source Plan

Project **#020** of the [365 Open Source Plan](https://github.com/rockbenben/365opensource) — one person + AI, 300+ open-source projects in a year.

[Submit your idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
