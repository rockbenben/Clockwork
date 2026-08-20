# Clockwork — User Guide

**English** · [中文](USAGE.zh.md) · [← Back to README](../README.md)

Put the repetitive parts of your PC on autopilot: auto-launch your apps at login · timed reminders · one tap to run a whole routine.

A small Windows tray tool that manages four everyday things (plus a Settings tab):

1. **Startup list** — open your everyday apps in order at login, and do a few chores along the way.
2. **Scheduled tasks** — pop a reminder (on-time / read aloud / repeat-nagging / do something when you click **Yes**) or silently run an action group; runs once, on an interval, or on the usual weekday/every-N-days/monthly recurrence.
3. **System startup items** — view and manage everything on your PC that auto-starts; switch off what you don't need.
4. **Action groups** — bundle a series of actions into a group (Focus / Wrap-up / Bedtime…) and trigger it with one tap or a **global hotkey**.

---

## Getting started

1. Unzip `Clockwork-<version>-win-x64.zip` into any folder (portable — put it wherever); inside is a single `Clockwork.exe`. The `-needs-dotnet10` package holds the same exe minus the bundled .NET runtime, a fraction of the size, but the machine needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed; that build is also attached raw as a plain `Clockwork.exe` if you'd rather skip the unzipping.
2. Double-click **`Clockwork.exe`** to open the settings window.
3. To run it every boot: on the **Settings** tab, tick **Start at login** (registers a scheduled task with admin rights, so no wall of UAC prompts at boot).

It sits quietly in the tray. The window's close button only hides it to the tray; quit for real via the tray's right-click **Exit**. Tick **Start minimized to tray** on the Settings tab and opening it manually goes straight to the tray too.

## First run: replace the samples with your own

On first run the startup list and the scheduled tasks each contain a handful of **samples** (marked as such), picked to show the most representative moves — muting the speakers, launching an app, opening a URL on weekdays only (conditional execution), starting a chat app minimized in the background, sending a key combo. They are there to be copied from, so edit or delete them freely. **All of them start unticked**, so a fresh install does nothing on its own; tick the ones you actually want. The Action groups tab starts with two ready-to-run groups already in place (Stepping away / Wrap up · End of day) — add more from the built-in templates under **Add ▾**.

The most common need, "open my everyday apps at login":

1. Go to **Startup list**.
2. Delete the samples you don't need (select → delete → confirm; the next row is auto-selected, so deleting in a row is quick).
3. **Add ▾ → Launch program**, and fill **Target** with the app you want:
   - Apps the system can find: just the name — `msedge.exe` (Edge), `notepad.exe` (Notepad).
   - Otherwise a **full path**: right-click the app → "Open file location" → right-click the icon → "Properties", and copy the **Target** path.
   - URLs (`https://…`), documents, `.ps1` scripts and shortcuts (`.lnk`) all work too; paths may include surrounding quotes or environment variables like `%USERPROFILE%` — both are handled for you.
   - Give `.ps1` scripts a **full path** (PowerShell can't resolve a bare filename); which PowerShell runs them is picked for you — see the notes under *Step types* below.
4. Want an app to open later (e.g. after another one is up, or after the network is ready)? Raise its **post-step delay**, or move it up/down.
5. Tray → **Re-run startup list** to test it once.
6. Happy with it? On the **Settings** tab, enable **Start at login** — it'll run automatically every boot.

> Only want scheduled tasks or action groups? Adjust the samples on the matching tab the same way; the startup list can be emptied entirely — the four features are independent.

## Startup list

- An **ordered list of steps** run top-to-bottom at login. Add/remove; **drag a row to reorder it**, or use the up/down buttons; **double-click a row to edit** it. (Dragging works the same way on the Scheduled tasks and Action groups lists.)
- Each step can be enabled/disabled, given a **post-step delay**, a **repeat count** (loop it N times, waiting the post-step delay between each), and **run conditions** (see below). *Message* steps have no repeat count — they always show once, and a count left over from switching a step to *Message* has no effect.
- Selecting a step and clicking **Run** runs *just that step* immediately (ignoring its enabled state and time conditions — pure test); a tray toast reports the result.
- **The first entry under Add ▾ is "Pick from Start menu…"** — it lists every program in your Start menu — **including Store / UWP apps like Sticky Notes or Paint, which have no exe path** (they don't exist as files, so you could never type them in). Search, multi-select with Ctrl / Shift, and each one becomes a *Launch program* step. No more right-click → Properties → copy the target path. Added steps arrive **unticked**, so you get a look before anything runs.

### Step types

| Type | What it does |
| --- | --- |
| **Launch program** | A program (full path or bare name), shortcut, script, folder, URL, document, or a protocol like `ms-settings:` (the **…** button to the right picks a file); `.ps1` runs via PowerShell, and **the right PowerShell is picked for you** (see the notes below this table). Target and working dir may both include surrounding quotes or environment variables like `%USERPROFILE%` — both are handled for you. Working dir blank = target's folder. Advanced: **window style** (minimized / maximized / hidden), **activate if already running** (**on by default**: bring to front instead of relaunching; process name via the **…** button. For a URL / `.lnk` / `.ps1` / document target the process name can't be derived, so the option quietly does nothing and the target launches as usual), **fallback paths** (one full path per line; the first existing one is used — handy when install paths differ across machines). |
| **Send keys** | e.g. Win+D, Alt+K, Ctrl+Enter, F5 (supports Enter / Tab / Esc / Del / arrows…; **Capture** records a shortcut by pressing it). **Wheel with modifiers** (e.g. `Ctrl+wheel` to zoom) lives here too: hold Ctrl and scroll inside the combo box to record `Ctrl+WheelDown`; **hold Shift and scroll to record a horizontal scroll** — a key is recorded by pressing it, a wheel by rolling it, no syntax to memorise (you can still double-click and type `WheelUp` / `WheelDown`). **For plain scrolling or clicking, the Mouse step below is more direct** (a dropdown, nothing to memorise). Typed forms: `LeftClick` / `DoubleClick` / `RightClick` / `MiddleClick` / `MouseBack` / `MouseForward`. Wheel can be sent but not bound as a global hotkey — the OS hotkey registration cannot express it. |
| **Mouse** | One dropdown for every mouse action: **scroll** (up / down / left / right, one notch per run), **click** (left / double / right / middle) and the **side buttons** (back / forward). Raise the **repeat count** for N clicks or notches and use the post-step delay for the rhythm (e.g. 5 × 50 ms).<br>Horizontal scrolling emits a real tilt-wheel event (`HWHEEL`), which more apps honour than the `Shift+wheel` workaround — handy for wide tables, Gantt charts and timelines.<br>**The targeting rules differ, so keep them straight:** scrolling goes to the window **under the pointer** (Windows’ “Scroll inactive windows when I hover over them” setting, on by default on Win10/11; turn it off and it goes to the foreground window), while **clicks land wherever the pointer already is** — this app never moves it (moving it would mean absolute vs relative coordinates, multi-monitor and DPI scaling, and it would interrupt whatever you are doing). So either put the target in place first with **Window action → bring to front** and make sure the pointer is where it needs to be, or stick to position-independent actions like **back / forward**.<br>Prefer the keyboard where it works: `PageDown` via **Send keys** scrolls more predictably and `Alt+←` goes back; reach for the mouse only where nothing else does (maps, image viewers, Electron virtual lists, CAD). |
| **Send text** | Type a string into the focused window (newline = Enter, Tab works). Optional **target process** (the **…** button picks one) — brings its window to front first, then types; blank = current focus. **An active IME doesn't interfere**: text goes in as Unicode, bypassing the keyboard layout, so it can't be swallowed into a candidate window — CJK, accents and emoji all arrive intact. |
| **Volume** | Mute / unmute / set level (setting a level unmutes first) / **mute or unmute the microphone** — that one mutes the default recording device itself, so no app hears you, which is stronger than any meeting app's own mute button. |
| **Window action** | By process name (the **…** button picks one, searchable): close / minimize / maximize / bring-to-front / bring-to-front-and-send-keys. Slow apps can **wait up to N seconds for the window to appear** — acts the moment it shows, instead of a blind fixed delay. Only **real application windows** are targeted — a process's hidden helper windows (the taskbar, the desktop, background shells) are filtered out, so "close explorer" can't hit your taskbar. After acting it **verifies the window actually changed** and reports honestly when it didn't, worded separately from "no window found" (the first points at foreground lock / an elevated window / an unsaved-changes dialog; the second at why the process isn't up). |
| **System command** | Show desktop / lock (needs password to return) / turn off monitor (wakes on mouse move) / empty recycle bin / clear clipboard / **set clipboard text** / open Windows Settings / open Task Manager / screenshot / **display mode: PC screen only · duplicate · extend · second screen only** / **turn notifications off · on** / **screen brightness** / sleep / hibernate / sign out / restart / shut down (the last three confirm first). Three take an argument: *set clipboard text* adds a text box and *screen brightness* a 0–100 field, shown only when that command is selected. |
| **Message** | Show a line of text. Two forms: a **dialog** (must be clicked away; can ask Yes/No and act on Yes) or a **card** (slides in from the corner, non-blocking, auto-closes after the seconds you set, 0 = stays until you dismiss it). **Only the card form pops from the startup list** — a dialog at login would stall the whole list, so dialog-form messages are silently skipped there. A message step always shows once and has no repeat count. See the Action groups section below for the details. |
| **Delay** | Just wait N seconds before the next step; at the top of the list it delays the whole run. |
| **Action group** | Run a defined action group; set a repeat count to loop the whole group. |

> **A few caveats**
> - **Display mode** shells out to Windows' own `DisplaySwitch.exe`, exactly like picking an option under Win+P. The switch takes a second or two to settle, so leave a delay before whatever follows.
> - **Turn notifications off** flips Windows' master notification switch (the *Get notifications from apps and other senders* setting) — not Windows 11's Focus / Do not disturb, which has no public switch to write. It does **not** restore itself, so always pair it with a *turn notifications on*; the built-in **Back to normal** template exists for exactly this.
> - **Screen brightness** only works on displays the system drives (laptop panels, some all-in-ones). External monitors speak a different protocol and are out of reach — you get an honest error rather than a fake success. This step shells out to PowerShell once, so it takes roughly half a second to a second.
> - **Which PowerShell runs a `.ps1` is decided for you.** Saved as BOM-less UTF-8 — today's default — with any non-ASCII text, a script cannot be decoded by the built-in Windows PowerShell 5.1: it reads the file in the system code page and dies while parsing, before a single line runs. Those scripts are handed to PowerShell 7 instead. If PowerShell 7 isn't installed you get a message saying so (install it, or re-save the script as UTF-8 with BOM), not a bare exit code. Legacy ANSI/GBK scripts still go to 5.1, which is where they are correct. Give `.ps1` targets a full path — PowerShell can't resolve a bare filename.
> - **Sleep and hibernate don't fake success.** Either state can be disabled by a driver or group policy (`powercfg /a` shows what's available); when it is, the step reports an honest error instead of recording success and leaving the machine awake all night.

### Run conditions (available on every step)

A step whose conditions aren't met is skipped and the list carries on. Conditions are **AND**-ed — set several and all of them must hold.

| Condition | What it means |
| --- | --- |
| **Only on these weekdays** | None ticked = every day. |
| **Only before / only after** | Each takes `HH:mm` (just the hour works too). Use both for a window: *only after 09:00* + *only before 18:00* = office hours only. |
| **Process condition** | Pick *that process is running* / *is not running* and name the process (the **…** button picks one). Mute chat only while the game is up; start a backup only when the backup tool isn't already running. |
| **Power condition** | Only on AC / only on battery — don't kick off heavy work once the charger is out. Desktops report no battery and always count as "on AC". |
| **Path exists** | If filled in, the step runs only when that file or folder exists: back up once the USB drive is mounted, send the mail once the report has been exported. A folder counts as existing. Accepts the same spellings as **Target**: surrounding quotes and environment variables like `%USERPROFILE%`. |

> Conditions show up in the list's **Summary** column (`(Mon Tue Wed)`, `(after 18:00)`, `(Slack running)`, `(on battery)`, `(E:\backup exists)`) — when a step didn't run, that column is the only clue you have, and you shouldn't need to open the editor to guess.

### Startup delay

On the **Settings** tab, "Startup delay N seconds" applies **only when auto-started at boot**. After login it waits a fixed number of seconds so the "login storm" (disk/CPU contention from every autostart) passes before the list runs; a manual re-run is not affected. Raise it (0–600 s) if things start too early. This is the *one* knob for overall delay; to slow a single step, use that step's post-step delay.

### Stop anytime

Three ways, all doing exactly the same thing: the **stop button** at the right end of the window's tab bar, tray → **Stop running actions**, or the global **panic hotkey** (set on the Settings tab; default `Ctrl+Alt+Q`). Whatever is running (startup list / action group / single step) stops after the current action; long waits (startup delay, waiting for a window) are interrupted immediately. The run log records "manually stopped". If the hotkey is taken by another app and fails to register, a tray toast warns you (use the button or the tray menu's Stop as a fallback).

> The stop button **only exists while something is actually running** — that is the point: its presence tells you something is running, and its disappearance tells you the stop went through. Hover it for the current panic hotkey.

> **To stop just one action group**, press **that group's own hotkey** a second time (see "Action groups → the hotkey is a toggle"). The panic hotkey is the master switch: it stops the startup list and every running group at once.
> This only works for groups that have a hotkey bound. A group started as a scheduled task's silent group, as an On-Yes action, or as a nested reference has no per-run cancel of its own unless you also give it a hotkey — for those the panic hotkey is the only way out. If you want an unattended long-running group to be stoppable, bind it a hotkey of its own.

> To "wait until the network / desktop is ready" instead of sitting out a fixed delay, tick **Wait until the system is ready** on the Settings tab (it used to be the json-only `startupWaitForReady`): it goes as soon as both are ready, waits at most 90 s, and the fixed delay above is then added on top. When the list runs too early for your apps, this beats simply raising the delay.

## Scheduled tasks

- Each task either **pops a reminder** (text / speech / on-Yes action) or **silently runs an action group**.
- **Trigger:** timed, **at login** (with "only within N minutes of boot" counting as login — 10 min by default for new tasks), or one of the 7 **events** below.
- **Recurrence:** by weekday / every-N-days / monthly; the reminder can be read aloud.

### Event triggers

These don't watch the clock, they watch the machine — the task fires the moment the event happens, and does exactly what a timed task does (reminder / speech / on-Yes action / silent action group).

| Event | When it happens |
| --- | --- |
| **When idle** | No keyboard or mouse for N minutes (10 by default). **Fires once per absence**; the count restarts when you come back. |
| **On unlock / on lock** | The moment you hit Win+L, and the moment you come back. Switching users and remote connects don't count — that's a different thing. |
| **On wake from sleep** | Coming back from lid-close or sleep. Good for the tidy-up work: reconnect the VPN, remount a share. |
| **When plugged in / unplugged** | The instant the charger goes in or out. *Unplugged → switch to power saving* is the classic. |
| **On low battery** | On battery and the charge drops below N% (20 by default). **Fires once per drop**; it re-arms after charging back above the threshold or plugging in. |

- **Weekday limits still apply** ("clock in on unlock, weekdays only") — the weekday row stays visible in the editor.
- **Grace** and **catch-up** are meaningless for events and are hidden: an event fires as it happens, and if the machine was off it simply never happened, so there is nothing to make up.
- **Snooze, nagging and interval runs all work as usual** — everything that follows a reminder going off is shared with timed tasks.
- **Pause reminders (do not disturb)** suppresses events too, same as timed tasks.
- Idle and battery are **polled** (they ride the reminder timer, 30 s by default), so they can be up to half a minute late; lock / unlock / wake / power changes are pushed by Windows and are immediate.
- **Interval runs**: "every N minutes until HH:mm" (empty = end of day). Distinct from "nag until confirmed" — nagging stops on confirmation, interval runs keep going. Intervals never cross midnight; the next day starts fresh from the task's base time.
- **Skip today**: select a row → **Skip today** (**click it again to undo**), and the rest of today is written off for that reminder — in-flight nags, interval rounds and snoozes included — while tomorrow runs as usual. All three trigger kinds (timed / at login / event) answer the same way. It is safer than unticking: unticking has no expiry, and one you forget to re-tick is a reminder that fails silently forever. The skip is persisted, so a restart does not undo it.
- **Quick reminder**: tray right-click → **Quick reminder** → 5 / 15 / 25 / 30 / 60 minutes. It is accurate to within one tick (30 s by default) and never fires early; while reminders are paused it says so plainly instead of pretending the timer is armed. Creates a one-shot reminder on the spot — **it shows up in the list on this page**, so you can delete it if you change your mind — with a sound and a card that stays until you dismiss it, and **deletes itself once it has fired** — a scratch timer should not leave a dead row in your config. For any other length, just add a normal reminder on this page. If its minute lands during do-not-disturb or sleep it fires late rather than being dropped; if a whole day passed and it never fired, the next start clears it out instead of leaving a dead row.
- **Run once**: pick "Once" and a date. After it completes, the entry unticks itself but stays in the list — set a new date and re-enable to reuse.
- **Sound**: the **Sound** checkbox under "Speak" plays the system "Asterisk" chime when the reminder fires. A card neither steals focus nor sits on top, so if you are looking at another screen you simply miss it — speaking is for hearing the content, the chime is only to make you look up; either can be on without the other. It follows your Windows sound scheme, so a silent scheme stays silent, and silent action groups never chime (that is what "silent" means). Fresh samples ship with it on; reminders you already had keep their current setting.
- Reminders with **no On-Yes action** slide in as a **reminder card** in the corner (non-intrusive). How long it shows is set by the **auto-close** seconds — **0 = stays until you dismiss it**, so nothing is missed if you're away. Repeat-nagging reminders still use a dialog (so you can stop the nagging with one click).
- Reminders **with** an On-Yes action (run program / open file / URL / run action group) pop a top-most **Yes / No** dialog with a **Snooze** button (default 10 min, ▾ menu 5 / 10 / 15 / 30 / 60 min). Enter = **Yes** as always; for the first 0.6 s after the dialog appears, **Yes** doesn't respond — a dialog that steals focus mid-typing can't run the action on an in-flight space bar or Enter.
- **An unanswered dialog gets out of the way.** The dialog is modal, so leaving it up would block every later reminder. A dialog with no auto-close stays up for at most 1 minute; when it times out unanswered it turns into an automatic **"snooze 10 minutes"** and comes back later — nothing is blocked, nothing is silently lost (the auto-snooze is persisted like a hand-clicked one, surviving restarts; it expires at midnight, except reminders with **catch up if missed** enabled, which re-fire once the next day). Repeat-nagging reminders keep nagging on your configured cadence instead. After **6 rounds** with no answer, the dialog stops re-popping and degrades into a **persistent corner card** waiting for your return — re-interrupting an empty desk is pointless. Note the card has no Yes/No buttons: once degraded, the On-Yes action no longer runs. Hand-clicked snoozes don't count toward those 6 rounds and reset the streak — you were there, so the clock starts over. The cap counts **rounds, not elapsed time**: with the defaults six rounds is about an hour, but if you set a long auto-close (say 1800 s) each round also holds the dialog up for that long, stretching the total several-fold.
- **Repeat fires of one reminder share a single card** (`×N` at the top right), so the corner never fills up. Cards you dismissed, evicted, or that auto-closed — and dialog-form reminders too (unanswered ones marked in the warning color) — can be reviewed and re-shown from **tray right-click → Recent** (session-only; cleared on restart).
- **Advanced:** auto-close · repeat-nagging (re-pop every N minutes until a deadline; a blank deadline caps it at 20 nags) · post-trigger delay + random jitter · grace (catch a fire missed by a brief shutdown/sleep) · **catch up if missed** (re-fire once after hibernation/shutdown skipped it) · an **anchor date** for every-N-days (the **…** button picks it; left blank it is pinned to today on save, and every N days counts from there).
- **State persistence:** "fired today" and "snoozed until" are saved to `clockwork.state.json`, surviving restarts — a snooze carries across a restart and the same reminder never double-fires in a day. Interval progress is persisted the same way, so restarting mid-day keeps the day's remaining rounds.
- **Midnight and long-standby edges all resolve to "fires exactly once":** a late reminder (say 23:59) that actually pops after midnight is still recorded against **the day it was due**, so it fires again the next day instead of degrading to every-other-day; a nag chain that slept past its own deadline ends there rather than reviving the next morning and nagging for hours; and an absurd future timestamp left behind by a wrong system clock (VM snapshot restore, dead CMOS battery) is discarded as junk instead of silencing that reminder forever with no way back but deleting the state file.
- **Do-Not-Disturb:** tray → **Pause reminders ▸** → 1 / 2 / 4 hours. Everything (including silent groups) is suppressed and auto-resumes when the time is up; you can also **Resume** early. Anything missed follows the normal grace / catch-up rules.
- **Silent action group:** run a group on time with **no popup**. Selecting a task and clicking **Run** runs it once — note that for a silent task, Run **actually executes** the group.
- **What the list columns say:** a task triggered **at login** shows **Every login** in the period column (it never consults a weekday/monthly recurrence, so the editor hides that block too), and a **silent** task shows the group it runs in the text column instead of an empty cell.
- **Duplicate** clones the selected task right below it (same text and settings, its own schedule state) — handy for "same task, second time of day": duplicate, then just change the time.

## System startup items

- Lists **everything that auto-starts** (registry Run keys, Startup folders, scheduled tasks).
- Uncheck **Enable** to switch an item off — **disabled, not deleted; re-check to restore** (takes effect immediately).
- Items marked **needs admin**: acting on them prompts to relaunch as administrator, then you can proceed.
- System / policy / one-time items (Group-Policy Run, RunOnce, Winlogon, Active Setup) can't be touched and are **hidden by default** — tick **Show system / read-only items** (top-right) to view them (greyed out; the right-click actions below are disabled for them).
- **Right-click a row** for two actions:
  - **Take over into launch list** — hands the item to Clockwork (disables the original + adds it to your list). Registry Run keys and Startup-folder items only; scheduled tasks aren't supported yet (you'll get a notice).
  - **Delete from system** — removes the entry for good (registry value / Startup-folder shortcut / scheduled task). It asks first and **cannot be undone** — if you only want to stop it running at boot, uncheck **Enable** instead. If the item was taken over earlier and a step still points at its shortcut file, the confirmation says so, because deleting the shortcut breaks that step.
- A top **filter** searches by name / command.

## Action groups

- **Add ▾** starts a group from a **built-in template** (Focus / Meeting / Back to normal / Wrap-up / Bedtime / Stepping away / Sitting too long) — tweak the process names and save.
  - **Back to normal is the way out.** Muting notifications and muting the mic are stateful — they don't undo themselves. Focus and Meeting each switch something off, so keep a *Back to normal* around (a hotkey suits it well) to switch them all back on.
- The list shows each group's **step summary** and **hotkey** columns (an empty group's summary reads **(empty)**).
- A group runs **only one copy at a time** (repeat triggers are skipped — except its hotkey, see "the hotkey is a toggle" below).
- Trigger it four ways: tray **Run: <group>** · a **global hotkey** · an **action-group step** in the startup list (at boot) · a scheduled task's **On-Yes / silent group**. You can also select a row on the Action groups tab and hit **Run** to fire it once by hand.
- **Show in tray menu** (in the group editor; **off by default for new groups**): once you have a few groups the tray menu turns into a long strip, and most groups are triggered by a hotkey, a reminder or another group anyway — they don't need a row. A hidden group still works everywhere else: hotkeys, reminders, references and the **Run** button on the Action groups tab are all unaffected; it just isn't listed in the tray. Groups that existed before this option was added keep showing, so nothing disappears on upgrade.
- **Global hotkey:** in the group editor, click the hotkey box and press a combo (e.g. `Ctrl+Alt+F`) to run the group from any app — no menu needed. Esc cancels, Del clears. Changes apply live (no restart). A **disabled** group releases its combo so another group can use it. Refused with a notice: **system-reserved** combos (Alt+F4, Alt+Tab, Ctrl+Shift+Esc…), a combo already bound to **another enabled group** or the **panic hotkey**, or one **already taken by another app** (use a different combo).
- **The hotkey is a toggle — press it again to cancel.** Pressing the same hotkey while the group is still running cancels **that run**: the remaining steps and rounds are dropped and a tray toast confirms it. The cancel is scoped to that one run — **the startup list and other groups keep going** (use the panic hotkey to stop everything). What that means in practice:
  - Most groups finish in a few hundred milliseconds, so a second press then simply **runs it again** — cancelling only matters for groups that are still running (ones with delays or repeat rounds).
  - Delays between rounds and between steps are **interrupted on the spot**; you don't wait out the current sleep.
  - If the group is sitting on a **message** confirmation box, the box still needs dismissing, but the answer is discarded — clicking **Yes** no longer fires its On-Yes branch, and the group stops there.
  - If group A references group B and you press **B's** hotkey while B runs, **the whole run is cancelled** (A stops too) — stopping only B and letting A carry on would leave you with a half-finished state that is harder to clean up than not cancelling at all.
- A **message** step can act as a confirmation gate — answering **No** aborts the rest of the group (e.g. "Did you log today's tasks?" before wrap-up).
- **Presentation:** a message step shows as either **Dialog (blocks)** — the default, same as above — or **Card (non-blocking)**: it slides into the corner and **auto-closes** after the seconds you set (**0 = stays until clicked**), and the group carries straight on without waiting for an answer. Switching to card hides and clears the confirm (Yes/No) and On-Yes fields, since a card's only interaction is click-to-dismiss. Cards are also the only message form that fires from the **startup list** — a dialog at boot would block the whole list, so dialog-form message steps are silently skipped there.
- Inside the group editor, **drag a step to reorder it** (the up/down buttons still work).
- **Duplicate** clones the selected group as "… (copy)" — a quick base for a variant. The copy gets **no hotkey** (two groups can't share one), so assign a new one if you want it.
- Deleting a group that is **referenced** (by a scheduled task's On-Yes / silent group, or an action-group step) tells you how many references there are and clears them along with it, so nothing is left pointing at a group that no longer exists.

### Try it before you save

The group editor has two run buttons that act on **whatever is currently in the editor**, not the last-saved version — so you can test a step you just tweaked without saving first.

- **▶ Run This Step** runs just the selected row. If that row is itself an action-group step, it resolves and runs the referenced group as currently saved, not any unsaved edits made to that sub-group elsewhere.
- **▶ Run Group** runs every step top to bottom, exactly like a real trigger — including delays, repeat counts, and weekday / before-N-o'clock conditions, so a weekday-only step really is skipped on the wrong day.

While a whole-group run is going, **▶ Run Group** turns into **■ Stop**; closing the group editor also stops the run. Confirmation dialogs (e.g. from a message step) pop up in front of the editor window instead of behind it.

## Loops

- **Repeat a whole group**: set "Repeat whole group / delay between rounds" in the group editor.
- **Loop a subset of steps**: extract those steps into their own action group, then reference it with a "group" step and set its repeat count.
- Three repeat knobs multiply: per-step repeat × reference-step repeat × whole-group rounds.
- Groups can nest group references; saving validates cycles among action-group **step** references (A→B→A is rejected with the chain shown). A cycle formed through a message step's "on Yes → run group" target is not checked at save time, but is caught at run time by re-entry protection (skipped with a warning, never spinning).
- **Answering "No" to a message step stops everything**: the rest of that group **and its remaining rounds**, and if the group was reached through a reference from another group, that caller's remaining iterations too. So when you loop a subsequence the recommended way (sub-group referenced ×N), declining once is enough — the same dialog will not chase you N times.
- A referenced group that is **missing** (deleted, or no group picked when the step was created), **disabled**, or **already running** (including a cycle) is never silently skipped: missing and re-entrant are reported as warnings, disabled as a plain notice (you turned it off yourself — that is not a fault). Re-entry also stops that reference's remaining iterations, so one notice never repeats N times.
- **A step inside a group that ran but didn't take now speaks up too.** A missing script file, an uninstalled program, a window that couldn't be found — these raise a tray notice and a line in `clockwork.error.log`, and the group carries on. This path used to be completely silent: the same step in the startup list showed a "⚠", while hotkeys and silent scheduled groups — the unattended path — said nothing at all. Repeated failures of the same step merge into one notice with a count instead of stacking up.
- Safety fuse: a single run executes at most **5000 steps** — every execution of a normal step counts as one, and so does every "action group" reference iteration (otherwise a chain of nothing but references, with empty leaf groups, would slip past the fuse). Past that it stops and says so in the run log. The stop hotkey works at any time.

## Settings

Three sections, ordered by when you'd reach for them.

**Startup**

- **Start at login** — the master switch of this section, so it comes first: ticking it registers a scheduled task with admin rights (so boot brings no UAC prompts), unticking removes it. If the change needs elevation, Clockwork relaunches itself to do it; if it fails, the box springs back rather than claiming a state that isn't real.
- **Startup delay** (0–600 s) — waits this long after logon before running the list, so it misses the logon storm. **Only applies when Start at login is on**; a manual *Re-run startup list* is unaffected.
- **Wait until the system is ready** (desktop / network) — goes as soon as both are ready, waits at most 90 s, then the fixed delay above is added on top. Better than simply raising the delay when the list runs too early for your apps.
- **Start minimized to tray** (opening manually goes straight to the tray).

**General**

- **Panic hotkey** — click the box and press your shortcut; Esc cancels, Del clears; default `Ctrl+Alt+Q`.
- **UI language** — Simplified Chinese, English, 日本語 and 15 more (18 total); switching restarts the app to apply.

**Backup**

- **Export Config** — saves a copy of `clockwork.settings.json` wherever you choose (default name `clockwork.settings.backup.json`). Use it to back up before a big change, or to move your setup to another PC.
- **Import Config** — replaces **all** current config (startup list / scheduled tasks / action groups / settings) with the chosen file. It confirms first, copies the current config to `clockwork.settings.json.bak` as an undo path, verifies the file parses before overwriting, then restarts the app so everything reloads. Task state (`clockwork.state.json`) is not touched.
- **A config file that can't be read is never overwritten.** Hand-edit the json and miss a comma, or lose power mid-save, and the app starts on defaults, tells you so, and saves your file aside as `clockwork.settings.json.bad`. The original is not clobbered by the defaults — fix it and restart to recover. Even if you miss the notice, rebuild everything by hand and save, that `.bad` copy is still there.

## Tips

- Double-click `Clockwork.exe` only opens the settings window — it does **not** immediately run the startup list; use the tray's **Re-run startup list** for that.
- **The side buttons follow the selection** — with no row selected, **Edit / Delete / Up / Down / Duplicate / Run** are greyed out, since they only ever act on the selected row. **Add** always works.
- **Nothing is cut off silently** — in all four lists, a cell too wide for its column ends in "…"; hover it to read the whole thing.
- **Deleting always asks for confirmation** — list rows, steps inside the group editor, and system startup items alike. The dialog names what you're about to delete, so you can catch a wrong selection before it's gone.
- Your config is `clockwork.settings.json` (local only). Delete it and reopen to reset to the sample. Task state is `clockwork.state.json` (also local; safe to delete — at most a task fires once more today). Prefer the Settings tab's **Export / Import Config** for backups and moving between PCs.
- **Where those files live:** on first run, next to `Clockwork.exe` when that folder is writable (the normal portable case); if it isn't — e.g. the exe sits under `C:\Program Files` — both files go to `%APPDATA%\Clockwork\`. **After that the app follows wherever the config already is, rather than re-testing writability each launch** — otherwise a double-click (not elevated) and autostart (elevated) would pick different copies on the same machine, showing up as "my settings vanished after reopening as administrator" or "autostart runs a list I never configured". Export always copies whichever one is actually in use, so you never have to hunt for it.
- When filling paths / processes / dates you don't have to type by hand: **the … button at the end of a row** opens the matching picker (file, searchable process list, date), and **Capture** records a shortcut by pressing it. The process picker and the system-startup list both have a search/filter box.
- **Launch it normally** (double-click / tray / scheduled task). Some sandbox / reduced-privilege launchers (e.g. Lucy) block low-level calls, so send-keys / mouse / window actions / activate-if-running / send-text-to-process / volume may not work (you'll get a clear notice; plain "launch program" is unaffected).
- Global hotkeys can **run action groups** (set per group, above). Arbitrary key remapping / text expansion is still out of scope — that's AutoHotkey's strength (an `.ahk` step needs AutoHotkey installed).
