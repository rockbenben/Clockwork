# Clockwork — User Guide

**English** · [中文](USAGE.zh-CN.md) · [← Back to README](../README.md)

Put the repetitive parts of your PC on autopilot: auto-launch your apps at login · timed reminders · one tap to run a whole routine.

A small Windows tray tool that manages four everyday things (plus a Settings tab):

1. **Startup list** — open your everyday apps in order at login, and do a few chores along the way.
2. **Scheduled tasks** — pop a reminder (on-time / read aloud / repeat-nagging / do something when you click **Yes**) or silently run an action group; runs once, on an interval, or on the usual weekday/every-N-days/monthly recurrence.
3. **System startup items** — view and manage everything on your PC that auto-starts; switch off what you don't need.
4. **Action groups** — bundle a series of actions into a group (Focus / Wrap-up / Bedtime…) and trigger it with one tap or a **global hotkey**.

---

## Getting started

1. Unzip `Clockwork-<version>.zip` into any folder (portable — put it wherever); inside is a single `Clockwork.exe`.
2. Double-click **`Clockwork.exe`** to open the settings window.
3. To run it every boot: on the **Settings** tab, click **Start at login** (registers a scheduled task with admin rights, so no wall of UAC prompts at boot).

It sits quietly in the tray. The window's close button only hides it to the tray; quit for real via the tray's right-click **Exit**. Tick **Start minimized to tray** on the Settings tab and opening it manually goes straight to the tray too.

## First run: replace the samples with your own

On first run the startup list and the scheduled tasks each contain a handful of **samples** (marked as such), picked to show the most representative moves — conditional execution, launching an app, opening a URL, sending a key combo, acting on a window. They are there to be copied from, so edit or delete them freely. **All of them start unticked**, so a fresh install does nothing on its own; tick the ones you actually want. The Action groups tab starts with two ready-to-run groups already in place (Stepping away / Wrap up · End of day) — add more from the built-in templates under **Add ▾**.

The most common need, "open my everyday apps at login":

1. Go to **Startup list**.
2. Delete the samples you don't need (select → delete → confirm; the next row is auto-selected, so deleting in a row is quick).
3. **Add ▾ → Launch program**, and fill **Target** with the app you want:
   - Apps the system can find: just the name — `msedge.exe` (Edge), `notepad.exe` (Notepad).
   - Otherwise a **full path**: right-click the app → "Open file location" → right-click the icon → "Properties", and copy the **Target** path.
   - URLs (`https://…`), documents, `.ps1` scripts and shortcuts (`.lnk`) all work too.
4. Want an app to open later (e.g. after another one is up, or after the network is ready)? Raise its **post-step delay**, or move it up/down.
5. Tray → **Re-run startup list** to test it once.
6. Happy with it? On the **Settings** tab, enable **Start at login** — it'll run automatically every boot.

> Only want scheduled tasks or action groups? Adjust the samples on the matching tab the same way; the startup list can be emptied entirely — the four features are independent.

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

Three ways, all doing exactly the same thing: the **stop button** at the right end of the window's tab bar, tray → **Stop running actions**, or the global **panic hotkey** (set on the Settings tab; default `Ctrl+Alt+Q`). Whatever is running (startup list / action group / single step) stops after the current action; long waits (startup delay, waiting for a window) are interrupted immediately. The run log records "manually stopped". If the hotkey is taken by another app and fails to register, a tray toast warns you (use the button or the tray menu's Stop as a fallback).

> The stop button **only exists while something is actually running** — that is the point: its presence tells you something is running, and its disappearance tells you the stop went through. Hover it for the current panic hotkey.

> **Advanced:** to "wait until the network / desktop is ready" instead of a fixed delay, set `startupWaitForReady` to `true` in `clockwork.settings.json` (default `false`; proceeds as soon as ready, capped at 90 s).

## Scheduled tasks

- Each task either **pops a reminder** (text / speech / on-Yes action) or **silently runs an action group**.
- **Trigger:** timed, or **at login** (with "only within N minutes of boot" counting as login — 10 min by default for new tasks).
- **Recurrence:** by weekday / every-N-days / monthly; the reminder can be read aloud.
- **Interval runs**: "every N minutes until HH:mm" (empty = end of day). Distinct from "nag until confirmed" — nagging stops on confirmation, interval runs keep going. Intervals never cross midnight; the next day starts fresh from the task's base time.
- **Run once**: pick "Once" and a date. After it completes, the entry unticks itself but stays in the list — set a new date and re-enable to reuse.
- Reminders with **no On-Yes action** slide in as a **reminder card** in the corner (non-intrusive). How long it shows is set by the **auto-close** seconds — **0 = stays until you dismiss it**, so nothing is missed if you're away. Repeat-nagging reminders still use a dialog (so you can stop the nagging with one click).
- Reminders **with** an On-Yes action (run program / open file / URL / run action group) pop a top-most **Yes / No** dialog with a **Snooze** button (default 10 min, ▾ menu 5 / 10 / 15 / 30 / 60 min). Enter = **Yes** as always; for the first 0.6 s after the dialog appears, **Yes** doesn't respond — a dialog that steals focus mid-typing can't run the action on an in-flight space bar or Enter.
- **An unanswered dialog gets out of the way.** The dialog is modal, so leaving it up would block every later reminder. A dialog with no auto-close stays up for at most 1 minute; when it times out unanswered it turns into an automatic **"snooze 10 minutes"** and comes back later — nothing is blocked, nothing is silently lost (the auto-snooze is persisted like a hand-clicked one, surviving restarts; it expires at midnight, except reminders with **catch up if missed** enabled, which re-fire once the next day). Repeat-nagging reminders keep nagging on your configured cadence instead.
- **Repeat fires of one reminder share a single card** (`×N` at the top right), so the corner never fills up. Cards you dismissed, evicted, or that auto-closed can be reviewed and re-shown from **tray right-click → Recent** (session-only; cleared on restart).
- **Advanced:** auto-close · repeat-nagging (re-pop every N minutes until a deadline) · post-trigger delay + random jitter · grace (catch a fire missed by a brief shutdown/sleep) · **catch up if missed** (re-fire once after hibernation/shutdown skipped it) · an **anchor date** for every-N-days (**Pick date**).
- **State persistence:** "fired today" and "snoozed until" are saved to `clockwork.state.json`, surviving restarts — a snooze carries across a restart and the same reminder never double-fires in a day. Interval progress is persisted the same way, so restarting mid-day keeps the day's remaining rounds.
- **Do-Not-Disturb:** tray → **Pause reminders ▸** → 1 / 2 / 4 hours. Everything (including silent groups) is suppressed and auto-resumes when the time is up; you can also **Resume** early. Anything missed follows the normal grace / catch-up rules.
- **Silent action group:** run a group on time with **no popup**. Selecting a task and clicking **Run** previews it once — note that for a silent task, Run **actually executes** the group.
- **Duplicate** clones the selected task right below it (same text and settings, its own schedule state) — handy for "same task, second time of day": duplicate, then just change the time.

## System startup items

- Lists **everything that auto-starts** (registry Run keys, Startup folders, scheduled tasks).
- Uncheck **Enable** to switch an item off — **disabled, not deleted; re-check to restore** (takes effect immediately).
- Items marked **needs admin**: acting on them prompts to relaunch as administrator, then you can proceed.
- System / policy / one-time items (Group-Policy Run, RunOnce, Winlogon, Active Setup) can't be touched and are **hidden by default** — tick **Show system / read-only items** (top-right) to view them (greyed out; the right-click actions below are disabled for them).
- **Right-click a row** for two actions:
  - **Take over into launch list** — hands the item to Clockwork (disables the original + adds it to your list). Registry Run keys and Startup-folder items only; scheduled tasks aren't supported yet (you'll get a notice).
  - **Delete from system** — removes the entry for good (registry value / Startup-folder shortcut / scheduled task). It asks first and **cannot be undone** — if you only want to stop it running at boot, uncheck **Enable** instead. If the item was taken over earlier and a step still points at its shortcut file, the confirmation says so, because deleting the shortcut breaks that step.
- A top **filter** searches by name / command; hover a truncated command to read it in full.

## Action groups

- **Add ▾** starts a group from a **built-in template** (Focus / Meeting / Wrap-up / Bedtime / Stepping away / Screenshot / Sitting too long) — tweak the process names and save.
- The list shows each group's **step summary** and **hotkey** columns (an empty group's summary reads **(empty)**).
- A group runs **only one copy at a time** (repeat triggers are skipped).
- Trigger it four ways: tray **Run: <group>** · a **global hotkey** · an **action-group step** in the startup list (at boot) · a scheduled task's **On-Yes / silent group**.
- **Global hotkey:** in the group editor, click the hotkey box and press a combo (e.g. `Ctrl+Alt+F`) to run the group from any app — no menu needed. Esc cancels, Delete clears. Changes apply live (no restart). A **disabled** group releases its combo so another group can use it. Refused with a notice: **system-reserved** combos (Alt+F4, Alt+Tab, Ctrl+Shift+Esc…), a combo already bound to **another enabled group** or the **panic hotkey**, or one **already taken by another app** (use a different combo).
- A **message** step can act as a confirmation gate — answering **No** aborts the rest of the group (e.g. "Did you log today's tasks?" before wrap-up).
- **Duplicate** clones the selected group as "… (copy)" — a quick base for a variant. The copy gets **no hotkey** (two groups can't share one), so assign a new one if you want it.
- Deleting a group that is **referenced** (by a scheduled task's On-Yes / silent group, or an action-group step) tells you how many references there are and clears them along with it, so nothing is left pointing at a group that no longer exists.

## Loops

- **Repeat a whole group**: set "Repeat whole group / delay between rounds" in the group editor.
- **Loop a subset of steps**: extract those steps into their own action group, then reference it with a "group" step and set its repeat count.
- Three repeat knobs multiply: per-step repeat × reference-step repeat × whole-group rounds.
- Groups can nest group references; saving validates cycles among action-group **step** references (A→B→A is rejected with the chain shown). A cycle formed through a message step's "on Yes → run group" target is not checked at save time, but is caught at run time by re-entry protection (skipped with a warning, never spinning).
- **Answering "No" to a message step stops everything**: the rest of that group **and its remaining rounds**, and if the group was reached through a reference from another group, that caller's remaining iterations too. So when you loop a subsequence the recommended way (sub-group referenced ×N), declining once is enough — the same dialog will not chase you N times.
- A referenced group that is **missing** (deleted, or no group picked when the step was created), **disabled**, or **already running** (including a cycle) is never silently skipped: missing and re-entrant are reported as warnings, disabled as a plain notice (you turned it off yourself — that is not a fault). Re-entry also stops that reference's remaining iterations, so one notice never repeats N times.
- Safety fuse: a single run executes at most **5000 steps** — every execution of a normal step counts as one, and so does every "action group" reference iteration (otherwise a chain of nothing but references, with empty leaf groups, would slip past the fuse). Past that it stops and says so in the run log. The stop hotkey works at any time.

## Settings

- **Startup delay** (0–600 s, boot only).
- **Start minimized to tray** (opening manually goes straight to the tray).
- **Panic hotkey** — click the box and press your shortcut; Esc cancels, Delete clears; default `Ctrl+Alt+Q`.
- **UI language** — Simplified Chinese, English, 日本語 and 15 more (18 total); switching restarts the app to apply.
- **Export Config** — saves a copy of `clockwork.settings.json` wherever you choose (default name `clockwork.settings.backup.json`). Use it to back up before a big change, or to move your setup to another PC.
- **Import Config** — replaces **all** current config (startup list / scheduled tasks / action groups / settings) with the chosen file. It confirms first, copies the current config to `clockwork.settings.json.bak` as an undo path, verifies the file parses before overwriting, then restarts the app so everything reloads. Task state (`clockwork.state.json`) is not touched.

## Tips

- Double-click `Clockwork.exe` only opens the settings window — it does **not** immediately run the startup list; use the tray's **Re-run startup list** for that.
- **Deleting always asks for confirmation** — list rows, steps inside the group editor, and system startup items alike. The dialog names what you're about to delete, so you can catch a wrong selection before it's gone.
- Your config is `clockwork.settings.json` (local only). Delete it and reopen to reset to the sample. Task state is `clockwork.state.json` (also local; safe to delete — at most a task fires once more today). Prefer the Settings tab's **Export / Import Config** for backups and moving between PCs.
- **Where those files live:** next to `Clockwork.exe` when that folder is writable (the normal portable case). If it isn't — e.g. you put the exe under `C:\Program Files` — both files fall back to `%APPDATA%\Clockwork\` automatically. Export always copies whichever one is actually in use, so you never have to hunt for it.
- When filling paths / processes / shortcuts / dates you don't have to type by hand: **Browse…**, **Pick…** (searchable process picker), **Capture**, and **Pick date**. The process picker and the system-startup list both have a search/filter box.
- **Launch it normally** (double-click / tray / scheduled task). Some sandbox / reduced-privilege launchers (e.g. Lucy) block low-level calls, so send-keys / window actions / activate-if-running / send-text-to-process / volume may not work (you'll get a clear notice; plain "launch program" is unaffected).
- Global hotkeys can **run action groups** (set per group, above). Arbitrary key remapping / text expansion is still out of scope — that's AutoHotkey's strength (an `.ahk` step needs AutoHotkey installed).
