# Clockwork — User Guide

**English** · [中文](USAGE.zh-CN.md) · [← Back to README](../README.md)

Put the repetitive parts of your PC on autopilot: auto-launch your apps at login · timed reminders · one tap to run a whole routine.

A small Windows tray tool that manages four everyday things (plus a Settings tab):

1. **Startup list** — open your everyday apps in order at login, and do a few chores along the way.
2. **Reminders** — on-time reminders / read aloud / repeat-nagging / do something when you click **Yes**.
3. **System startup items** — view and manage everything on your PC that auto-starts; switch off what you don't need.
4. **Action groups** — bundle a series of actions into a group (Focus / Wrap-up / Bedtime…) and trigger it with one tap.

---

## Getting started

1. Unzip into any folder (portable — put it wherever).
2. Double-click **`Clockwork.exe`** to open the settings window.
3. To run it every boot: on the **Settings** tab, click **Start at login** (registers a scheduled task with admin rights, so no wall of UAC prompts at boot).

It sits quietly in the tray. The window's close button only hides it to the tray; quit for real via the tray's right-click **Exit**. Tick **Start minimized to tray** on the Settings tab and opening it manually goes straight to the tray too.

## First run: replace the samples with your own

On first run the startup list / reminders / action groups already contain **samples** (each marked as such) — they just demonstrate what's possible, so edit or delete them freely. In the startup list the first few are checked (they run at boot); trailing ones like "Windows Settings / Task Manager" are unchecked (samples only — they run only if you tick them).

The most common need, "open my everyday apps at login":

1. Go to **Startup list**.
2. Delete the samples you don't need (select → delete; the next row is auto-selected, so deleting in a row is quick).
3. **Add ▾ → Launch program**, and fill **Target** with the app you want:
   - Apps the system can find: just the name — `msedge.exe` (Edge), `notepad.exe` (Notepad).
   - Otherwise a **full path**: right-click the app → "Open file location" → right-click the icon → "Properties", and copy the **Target** path.
   - URLs (`https://…`), documents, `.ps1` scripts and shortcuts (`.lnk`) all work too.
4. Want an app to open later (e.g. after another one is up, or after the network is ready)? Raise its **post-step delay**, or move it up/down.
5. Tray → **Re-run startup list** to test it once.
6. Happy with it? On the **Settings** tab, enable **Start at login** — it'll run automatically every boot.

> Only want reminders or action groups? Adjust the samples on the matching tab the same way; the startup list can be emptied entirely — the four features are independent.

## Startup list

- An **ordered list of steps** run top-to-bottom at login. Add/remove, move up/down; **double-click a row to edit** it.
- Each step can be enabled/disabled, given a **post-step delay**, a **repeat count** (loop it N times, waiting the post-step delay between each), and conditions (**only on certain weekdays / only before N o'clock**).
- Selecting a step and clicking **Run** runs *just that step* immediately (ignoring its enabled state and time conditions — pure test); a tray toast reports the result.

### Step types

| Type | What it does |
| --- | --- |
| **Launch program** | `.exe` / document / shortcut / URL (**Browse…** to pick a file); `.ps1` runs via PowerShell. Working dir blank = target's folder. Advanced: **window style** (minimized / maximized / hidden), **activate if already running** (bring to front instead of relaunching; process name via **Pick…**), **fallback paths** (one full path per line; the first existing one is used — handy when install paths differ across machines). |
| **Send keys** | e.g. Win+D, Alt+K, Ctrl+Enter, F5 (supports Enter / Tab / Esc / Del / arrows…; **Capture** records a shortcut by pressing it). |
| **Send text** | Type a string into the focused window (newline = Enter, Tab works). Optional **target process** (**Pick…**) — brings its window to front first, then types; blank = current focus. |
| **Volume** | Mute / unmute / set level (setting a level unmutes first). |
| **Window action** | By process name (**Pick…**, searchable): close / minimize / maximize / bring-to-front / bring-to-front-and-send-keys. Slow apps can **wait up to N seconds for the window to appear** — acts the moment it shows, instead of a blind fixed delay. |
| **System command** | Show desktop / lock (needs password to return) / turn off monitor (wakes on mouse move) / empty recycle bin / clear clipboard / open Windows Settings / open Task Manager / screenshot / sleep / hibernate / sign out / restart / shut down (the last three confirm first). |
| **Delay** | Just wait N seconds before the next step; at the top of the list it delays the whole run. |
| **Action group** | Run a defined action group; set a repeat count to loop the whole group. |

### Startup delay

On the **Settings** tab, "Startup delay N seconds" applies **only when auto-started at boot**. After login it waits a fixed number of seconds so the "login storm" (disk/CPU contention from every autostart) passes before the list runs; a manual re-run is not affected. Raise it (0–600 s) if things start too early. This is the *one* knob for overall delay; to slow a single step, use that step's post-step delay.

### Stop anytime

Tray → **Stop running actions**, or the global **panic hotkey** (set on the Settings tab; default `Ctrl+Alt+Q`). Whatever is running (startup list / action group / single step) stops after the current action; long waits (startup delay, waiting for a window) are interrupted immediately. The run log records "manually stopped". If the hotkey is taken by another app and fails to register, a tray toast warns you (use the tray menu's Stop as a fallback).

> **Advanced:** to "wait until the network / desktop is ready" instead of a fixed delay, set `startupWaitForReady` to `true` in `clockwork.settings.json` (default `false`; proceeds as soon as ready, capped at 90 s).

## Reminders

- **Trigger:** timed, or **at login** (with "only within N minutes of boot" counting as login — 10 min by default for new reminders).
- **Recurrence:** by weekday / every-N-days / monthly; the reminder can be read aloud.
- Reminders with **no On-Yes action** slide in as a **reminder card** in the corner (non-intrusive). How long it shows is set by the **auto-close** seconds — **0 = stays until you dismiss it**, so nothing is missed if you're away. Repeat-nagging reminders still use a dialog (so you can stop the nagging with one click).
- Reminders **with** an On-Yes action (run program / open file / URL / run action group) pop a top-most **Yes / No** dialog with a **Snooze** button (default 10 min, ▾ menu 5 / 10 / 15 / 30 / 60 min).
- **Advanced:** auto-close · repeat-nagging (re-pop every N minutes until a deadline) · post-trigger delay + random jitter · grace (catch a fire missed by a brief shutdown/sleep) · **catch up if missed** (re-fire once after hibernation/shutdown skipped it) · an **anchor date** for every-N-days (**Pick date**).
- **State persistence:** "fired today" and "snoozed until" are saved to `clockwork.state.json`, surviving restarts — a snooze carries across a restart and the same reminder never double-fires in a day.
- **Do-Not-Disturb:** tray → **Pause reminders for 1 / 2 / 4 hours**. Everything (including silent groups) is suppressed and auto-resumes when the time is up; you can also **Resume** early. Anything missed follows the normal grace / catch-up rules.
- **Silent action group:** run a group on time with **no popup**. Selecting a reminder and clicking **Run** previews it once — note that for a silent reminder, Run **actually executes** the group.

## System startup items

- Lists **everything that auto-starts** (registry Run keys, Startup folders, scheduled tasks).
- Uncheck **Enable** to switch an item off — **disabled, not deleted; re-check to restore** (takes effect immediately).
- Items marked **needs admin**: acting on them prompts to relaunch as administrator, then you can proceed.
- System / policy / one-time items (Group-Policy Run, RunOnce, Winlogon, Active Setup) can't be toggled normally and are **hidden by default** — tick **Show system / read-only items** (top-right) to view them (greyed out).
- **Take over into startup list** hands an item to Clockwork (disables the original + adds it to your list). Registry Run keys and Startup-folder items only; scheduled tasks aren't supported yet (you'll get a notice).
- A top **filter** searches by name / command; hover a truncated command to read it in full.

## Action groups

- **Add ▾** starts a group from a **built-in template** (Focus / Meeting / Wrap-up / Bedtime / Stepping away / Screenshot) — tweak the process names and save.
- A group runs **only one copy at a time** (repeat triggers are skipped).
- Trigger it three ways: tray **Run: <group>** · an **action-group step** in the startup list (at boot) · a reminder's **On-Yes / silent group**.
- A **message** step can act as a confirmation gate — answering **No** aborts the rest of the group (e.g. "Did you log today's tasks?" before wrap-up).

## Settings

- **Startup delay** (0–600 s, boot only).
- **Start minimized to tray** (opening manually goes straight to the tray).
- **Panic hotkey** — click the box and press your shortcut; Esc cancels, Delete clears; default `Ctrl+Alt+Q`.
- **UI language** — Simplified Chinese, English, 日本語 and 15 more (18 total); switching restarts the app to apply.

## Tips

- Double-click `Clockwork.exe` only opens the settings window — it does **not** immediately run the startup list; use the tray's **Re-run startup list** for that.
- Your config is `clockwork.settings.json` (local only). Delete it and reopen to reset to the sample. Reminder state is `clockwork.state.json` (also local; safe to delete — at most a reminder fires once more today).
- When filling paths / processes / shortcuts / dates you don't have to type by hand: **Browse…**, **Pick…** (searchable process picker), **Capture**, and **Pick date**. The process picker and the system-startup list both have a search/filter box.
- **Launch it normally** (double-click / tray / scheduled task). Some sandbox / reduced-privilege launchers (e.g. Lucy) block low-level calls, so send-keys / window actions / activate-if-running / send-text-to-process / volume may not work (you'll get a clear notice; plain "launch program" is unaffected).
- An `.ahk` step needs AutoHotkey installed. Global hotkeys / text expansion are out of scope — that's AutoHotkey's strength.
