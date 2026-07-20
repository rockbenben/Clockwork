<div align="center">

<img src="assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Put the repetitive parts of your PC on autopilot**

Auto-launch your apps at login · timed reminders · one tap to run a whole routine

</div>

<div align="center">

**English** · [简体中文](docs/README.zh-CN.md) · [繁體中文](docs/README.zh-TW.md) · [日本語](docs/README.ja.md) · [한국어](docs/README.ko.md) · [Deutsch](docs/README.de.md) · [Español](docs/README.es.md) · [Français](docs/README.fr.md) · [Italiano](docs/README.it.md) · [Nederlands](docs/README.nl.md) · [Português](docs/README.pt.md) · [Русский](docs/README.ru.md) · [Türkçe](docs/README.tr.md) · [Tiếng Việt](docs/README.vi.md) · [ไทย](docs/README.th.md) · [Bahasa Indonesia](docs/README.id.md) · [हिन्दी](docs/README.hi.md) · [العربية](docs/README.ar.md)

</div>

> 365 Open-Source Plan #020 · A Windows tray tool: startup launcher · reminders · system startup items · action groups

![Clockwork](assets/social-card.png)

A small Windows tray tool that takes care of the routine parts of starting your day at the computer:

- 🚀 **Startup list** — automatically open your everyday apps at login, in order (per-step admin rights, delays, only-on-certain-weekdays / only-before-N-o'clock, window style, activate-if-running, fallback paths), and do a few chores along the way (close or focus windows, send keystrokes / text, set volume…).
- ⏰ **Reminders** — pop a reminder on time; speak it aloud; repeat by weekday / every-N-days / monthly; or trigger "at login". Clicking **Yes** can run a program, open a file (e.g. music) or a URL, or run an action group.
- 🧹 **System startup items** — list **everything on your PC that auto-starts** and switch off what you don't need (disabled, not deleted — flip it back anytime). One click "takes over" an item into your own startup list.
- 🎛️ **Action groups** — bundle a series of actions into a reusable group (Focus / Meeting / Wrap-up / Bedtime…) and trigger it with one click from the tray, a **global hotkey**, the startup list, or a reminder. Built-in templates included.

No install, fully portable single folder, everything configurable by mouse; dark UI, high-DPI aware. The UI ships in **18 languages** and follows your Windows display language on first run.

> 📖 **Full guide:** [English](docs/USAGE.md) · [中文](docs/USAGE.zh-CN.md)

## Requirements

- Windows 10 / 11 (x64)
- Nothing to install: a self-contained single-file `Clockwork.exe` with the .NET runtime bundled in.

## Getting started

1. Download the latest `Clockwork-<version>.zip` from [Releases](https://github.com/rockbenben/Clockwork/releases) and unzip it — inside is a single `Clockwork.exe`; drop it into any folder (portable — put it wherever). To build it yourself, see **For developers** below.
2. Double-click **`Clockwork.exe`** to open the settings window.
   - On **first run** it loads a few **samples** in the startup list and the reminders so you can adapt them to your own — all of them start unticked, so nothing runs until you tick it. Your settings live in `clockwork.settings.json` next to the exe — local only, never committed.
3. To run it every boot: on the **Settings** tab, click **Start at login** (registers a scheduled task with admin rights, so no wall of UAC prompts at boot).

> It sits quietly in the tray. Double-click the tray icon to open the window; the window's close button only hides it to the tray. Quit for real via the tray's right-click **Exit**.

## Screenshot

![Screenshot](assets/screenshot.png)

## The five tabs

### Startup list

An **ordered list of steps** run top-to-bottom at login. Click **Add ▾** to pick a type; add/remove/reorder freely; each step can be enabled/disabled, given a **post-step delay**, a **repeat count** (loop it N times), and conditions (**only on certain weekdays / only before N o'clock**). Step types:

- **Launch program** — target (**Browse…** to pick a file) / arguments / working dir (leave blank = target's folder) / admin. Target can be an `.exe`, document, shortcut or URL; a `.ps1` runs via PowerShell. Advanced: **window style** (minimized / maximized / hidden), **activate if already running** (bring it to front instead of relaunching; process name via **Pick…**), **fallback paths** (one full path per line; the first existing one is used — handy when install paths differ across machines).
- **Send keys** — e.g. Win+D, Alt+K, Ctrl+Enter, F5 (**Capture** to record a shortcut by pressing it).
- **Send text** — type a string into the focused window (or a chosen **target process** via **Pick…**).
- **Volume** — mute / unmute / set level.
- **Window action** — by process name (**Pick…**, searchable): close / minimize / maximize / bring-to-front / bring-to-front-and-send-keys; slow apps can **wait up to N seconds for the window to appear**.
- **System command** — show desktop / lock / turn off monitor / empty recycle bin / clear clipboard / open Settings / Task Manager / screenshot / sleep / hibernate / sign out / restart / shut down (the last three confirm first).
- **Delay** — just wait N seconds before the next step.
- **Action group** — run a defined action group; set a repeat count to loop the whole group.

> **Startup delay** (Settings tab, boot only): wait a fixed number of seconds after login so the "login storm" (disk/CPU contention from every autostart) passes before the list runs; a manual re-run is not affected. Raise it (0–600 s) if things start too early.

> **Stop anytime** — tray → **Stop running actions**, or the global **panic hotkey** (set on the Settings tab; default `Ctrl+Alt+Q`). Whatever is running stops after the current action; long waits (startup delay, waiting for a window) are interrupted immediately.

### Reminders

Set a **time** (or switch to **at login**), a **recurrence** (weekdays / every-N-days / monthly), and the **text**; optionally speak it aloud. Reminders with an **On-Yes** action (run program / open file / URL / run action group) pop a **Yes / No** dialog with a **Snooze** button (default 10 min, ▾ menu 5–60 min); the rest slide in as a **reminder card** in the corner (auto-close after the configured seconds, **0 = stays until you dismiss it**). You can also set a **silent action group** — run a group on time with no popup.

Advanced: **auto-close**, **repeat nagging** (re-pop every N minutes until a deadline), **post-trigger delay + random jitter**, **grace** (catch a fire missed by a brief shutdown/sleep), **catch up if missed** (re-fire once after hibernation/shutdown skipped it), and an **anchor date** for every-N-days (**Pick date**). "Fired today" and "snoozed until" survive restarts (`clockwork.state.json`), so a snooze carries across a restart and nothing double-fires.

Need to focus or take a meeting? The tray offers **Pause reminders for 1 / 2 / 4 hours** (Do-Not-Disturb): everything (including silent groups) is suppressed and auto-resumes when the time is up.

### System startup items

Lists **everything that auto-starts** (registry Run keys, Startup folders, scheduled tasks). Uncheck **Enable** to switch an item off — **disabled, not deleted; re-check to restore** (takes effect immediately). Items marked **needs admin** prompt to relaunch elevated. System / policy / one-time items (Group-Policy Run, RunOnce, Winlogon, Active Setup) can't be touched and are **hidden by default** — tick **Show system / read-only items** to view them (greyed out). Right-click a row for **Take over into launch list** (hands the item to Clockwork; registry Run keys and Startup-folder items only) or **Delete from system** (removes the entry for good — asks first, and can't be undone; unchecking is the reversible option). A top **filter** searches by name / command; hover a truncated command to read it in full.

### Action groups

Bundle actions into a reusable group. **Add ▾** starts one from a **built-in template** (Focus / Meeting / Wrap-up / Bedtime / Stepping away / Screenshot) — tweak the process names and save. A group **only defines actions**; trigger it four ways: from the tray (**Run: <group>**), a **global hotkey**, an **action-group step** in the startup list (at boot), or a reminder (**On-Yes / silent group**). A group runs only one copy at a time; a **message** step can act as a confirmation gate (answering **No** aborts the rest).

> **Global hotkey** — in the group editor, click the hotkey box and press a shortcut (e.g. `Ctrl+Alt+F`) to run that group from anywhere, no menu needed. Esc cancels, Delete clears. Disabled groups release their combo; system-reserved combos (Alt+F4, Ctrl+Shift+Esc…) and combos already taken by another group or the panic hotkey are refused with a notice.

### Settings

**Startup delay** (0–600 s, boot only), **start minimized to tray**, **panic hotkey** (click the box and press your shortcut; Esc cancels, Delete clears; default `Ctrl+Alt+Q`), and **UI language** (Simplified Chinese, English, 日本語 and 15 more — 18 total; switching restarts the app to apply).

**Export / Import Config** — move your whole setup to another PC or keep a backup. Export writes a copy of `clockwork.settings.json` anywhere you like; import replaces **everything** (startup list / reminders / action groups / settings), so it confirms first, backs the current config up to `clockwork.settings.json.bak`, and restarts the app to apply.

## Tips

- **Double-click a row to edit** it. When filling paths / processes / shortcuts / dates you don't have to type by hand: **Browse…**, **Pick…** (searchable process picker), **Capture**, and **Pick date**.
- **Duplicate** (Reminders / Action groups tabs) clones the selected row right below it — quicker than rebuilding a near-identical one; a duplicated group is named "… (copy)".
- **Deleting always asks first**, everywhere — list rows, steps inside the group editor, and system startup items.
- Double-clicking `Clockwork.exe` only opens settings — it does **not** immediately run the startup list; use the tray's **Re-run startup list** for that.
- **Launch it normally** (double-click / tray / scheduled task). Some sandbox / reduced-privilege launchers block low-level calls, so send-keys / window actions / activate-if-running / send-text-to-process / volume may not work (you'll get a clear notice; plain "launch program" is unaffected).
- Your config is `clockwork.settings.json` (local only). Delete it to reset to the sample. Reminder state is `clockwork.state.json` (also local; safe to delete).
- Adding an `.ahk` step needs AutoHotkey installed. Global hotkeys / text expansion are out of scope — that's AutoHotkey's strength.

## For developers

C#/.NET WPF; source in `app/` (needs the .NET 10 SDK). Layers: `Core/` pure logic · `Native/` Win32 interop · `Engine/` execution · `ViewModels/` + `Views/` UI · `I18n/` + `Resources/` localization (neutral = Chinese source, one `Strings.<code>.resx` satellite per language).

- Run tests (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Build the self-contained single-file exe (single-file / self-contained / compression are set in the csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Output: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / releases** (GitHub Actions): push / PR builds and runs all tests on a Windows runner; pushing a `v*` tag (e.g. `v2.0.0`) builds, stamps the file version from the tag, creates a GitHub Release and attaches `Clockwork-<tag>.zip` (containing `Clockwork.exe`).

## About the 365 Open-Source Plan

This is project #20 of the [365 Open-Source Plan](https://github.com/rockbenben/365opensource) — one person + AI, 300+ open-source projects in a year. [Submit a request →](https://365.aishort.top/)

## License

[MIT](LICENSE) © rockbenben
