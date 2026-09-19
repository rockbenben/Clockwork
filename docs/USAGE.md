# Clockwork — User Guide

**English** · [中文](USAGE.zh.md) · [← Back to README](../README.md)

Put the repetitive parts of your PC on autopilot: auto-launch your apps at login · timed reminders · one tap to run a whole routine.

A small Windows tray tool that manages five everyday things (plus a Settings tab):

1. **Startup list** — open your everyday apps in order at login, and do a few chores along the way.
2. **Scheduled tasks** — pop a reminder (on-time / read aloud / repeat-nagging / do something when you click **Yes**) or silently run an action; runs once, on an interval, or on the usual weekday/every-N-days/monthly recurrence.
3. **Actions** — bundle a series of actions into a group (Focus / Wrap-up / Bedtime…) and trigger it with one tap or a **global hotkey**.
4. **System startup items** — view and manage everything on your PC that auto-starts; switch off what you don't need.
5. **Ports** — see which ports are being listened on and what is holding them; double-click to open `localhost:3000` in your browser, right-click to free the port.

---

## Getting started

1. Unzip `Clockwork-<version>-win-x64.zip` into any folder (portable — put it wherever); inside is a single `Clockwork.exe`. The `-needs-dotnet10` package holds the same exe minus the bundled .NET runtime, a fraction of the size, but the machine needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) installed; that build is also attached raw as a plain `Clockwork.exe` if you'd rather skip the unzipping.
2. Double-click **`Clockwork.exe`** to open the settings window.
3. To run it every boot: on the **Settings** tab, tick **Start at login** (registers a scheduled task with admin rights, so no wall of UAC prompts at boot).

It sits quietly in the tray. The window's close button only hides it to the tray; quit for real via the tray's right-click **Exit**. Tick **Start minimized to tray** on the Settings tab and opening it manually goes straight to the tray too.

## First run: replace the samples with your own

On first run the startup list and the scheduled tasks each contain a handful of **samples** (marked as such), picked to show the most representative moves — muting the speakers, launching an app, opening a URL on weekdays only (conditional execution), starting a chat app minimized in the background, sending a key combo. They are there to be copied from, so edit or delete them freely. **All of them start unticked**, so a fresh install does nothing on its own; tick the ones you actually want. The Actions tab starts with two ready-to-run groups already in place (Stepping away / Wrap up · End of day) — add more from the built-in templates under **From a template…**.

The most common need, "open my everyday apps at login":

1. Go to **Startup list**.
2. Delete the samples you don't need (select → delete → confirm; the next row is auto-selected, so deleting in a row is quick).
3. **Add ▾ → Open → Launch program**, and fill **Target** with the app you want:
   - Apps the system can find: just the name — `msedge.exe` (Edge), `notepad.exe` (Notepad).
   - Otherwise a **full path**: right-click the app → "Open file location" → right-click the icon → "Properties", and copy the **Target** path.
   - URLs (`https://…`), documents, `.ps1` scripts and shortcuts (`.lnk`) all work too; paths may include surrounding quotes or environment variables like `%USERPROFILE%` — both are handled for you.
   - Give `.ps1` scripts a **full path** (PowerShell can't resolve a bare filename); which PowerShell runs them is picked for you — see the notes under *Step types* below.
4. Want an app to open later (e.g. after another one is up, or after the network is ready)? Raise its **post-step delay**, or move it up/down.
5. Tray → **Rerun startup list** to test it once.
6. Happy with it? On the **Settings** tab, enable **Start at login** — it'll run automatically every boot.

> Only want scheduled tasks or actions? Adjust the samples on the matching tab the same way; the startup list can be emptied entirely — the four features are independent.

## Startup list

- An **ordered list of steps** run top-to-bottom at login. Add/remove; **drag a row to reorder it** — each row has a **drag handle** (≡) at its left edge that lights up as you hover the row; the up/down buttons still work too (keyboard-reachable, and better for nudging one step at a time). **Double-click a row to edit** it; **right-click** for Duplicate (plus Skip today on the Scheduled tasks page). (All three list pages work the same way.)
- Each step can be enabled/disabled, given a **post-step delay**, a **repeat count** (loop it N times, waiting the post-step delay between each), and **run conditions** (see below). *Message* steps have no repeat count — they always show once, and a count left over from switching a step to *Message* has no effect.
- Selecting a step and clicking **Run** runs *just that step* immediately (ignoring its enabled state and time conditions — pure test); a tray toast reports the result.
- **Under Add ▾ → Open, the first entry is "Pick from Start menu…"** — it lists every program in your Start menu — **including Store / UWP apps like Sticky Notes or Paint, which have no exe path** (they don't exist as files, so you could never type them in). Search, multi-select with Ctrl / Shift, and each one becomes a *Launch program* step. No more right-click → Properties → copy the target path. Added steps arrive **unticked**, so you get a look before anything runs.
- **Add ▾ groups the step types by what you want to do, one submenu each** (Common / Run action / Open / Control an app / System & sound / Flow). The top level is just those intent words and the mechanism names live inside — so "close WeChat: is that a window action or send keys?" never has to be answered up front; you pick *Control an app* and read from there. (It was flat once: a dozen-plus types plus section headings made a twenty-row wall that grew scroll arrows on a 1366×768 laptop, hiding the last few entries.)
- **Add ▾ → Common** — a list of ready-filled steps: copy / paste / search the selected text / back / forward / play-pause · previous · next track / volume up · down / minimize · maximize · toggle-on-top · close the current window. They are not new step types, just existing ones with their fields filled in (*Copy* is really *Send keys `Ctrl+C`*), and picking one still opens the same step editor, where you can change it. **They are here for mouse gestures**: the word in your head while drawing one is “copy”, not “send keys”.
  The startup list has no such section — every entry in it asks what you are looking at right now, and at login nobody is there.

### Step types

| Type | What it does |
| --- | --- |
| **Launch program** | A program (full path or bare name), shortcut, script, folder, URL, document, or a protocol like `ms-settings:` (the **…** button to the right picks a file); `.ps1` runs via PowerShell, and **the right PowerShell is picked for you** (see the notes below this table). Target and working dir may both include surrounding quotes or environment variables like `%USERPROFILE%` — both are handled for you. Working dir blank = target's folder. Advanced: **window style** (minimized / maximized / hidden), **activate if already running** (**on by default**: bring to front instead of relaunching; process name via the **…** button. For a URL / `.lnk` / `.ps1` / document target the process name can't be derived, so the option quietly does nothing and the target launches as usual), **fallback paths** (one full path per line; the first existing one is used — handy when install paths differ across machines). |
| **Send keys** | e.g. Win+D, Alt+K, Ctrl+Enter, F5 (supports Enter / Tab / Esc / Del / arrows…; **Capture** records a shortcut by pressing it). **Wheel with modifiers** (e.g. `Ctrl+wheel` to zoom) lives here too: hold Ctrl and scroll inside the combo box to record `Ctrl+WheelDown`; **hold Shift and scroll to record a horizontal scroll** — a key is recorded by pressing it, a wheel by rolling it, no syntax to memorise (you can still double-click and type `WheelUp` / `WheelDown`). **For plain scrolling or clicking, the Mouse step below is more direct** (a dropdown, nothing to memorise). Typed forms: `LeftClick` / `DoubleClick` / `RightClick` / `MiddleClick` / `MouseBack` / `MouseForward`. Wheel can be sent but not bound as a global hotkey — the OS hotkey registration cannot express it. |
| **Mouse** | One dropdown for every mouse action: **scroll** (up / down / left / right, one notch per run), **click** (left / double / right / middle) and the **side buttons** (back / forward). Raise the **repeat count** for N clicks or notches and use the post-step delay for the rhythm (e.g. 5 × 50 ms).<br>Horizontal scrolling emits a real tilt-wheel event (`HWHEEL`), which more apps honour than the `Shift+wheel` workaround — handy for wide tables, Gantt charts and timelines.<br>**The targeting rules differ, so keep them straight:** scrolling goes to the window **under the pointer** (Windows’ “Scroll inactive windows when I hover over them” setting, on by default on Win10/11; turn it off and it goes to the foreground window), while **clicks land wherever the pointer already is** — this app never moves it (moving it would mean absolute vs relative coordinates, multi-monitor and DPI scaling, and it would interrupt whatever you are doing). So either put the target in place first with **Window action → bring to front** and make sure the pointer is where it needs to be, or stick to position-independent actions like **back / forward**.<br>Prefer the keyboard where it works: `PageDown` via **Send keys** scrolls more predictably and `Alt+←` goes back; reach for the mouse only where nothing else does (maps, image viewers, Electron virtual lists, CAD). |
| **Send text** | Type a string into the focused window (newline = Enter, Tab works). Optional **target process** (the **…** button picks one) — brings its window to front first, then types; blank = current focus. **An active IME doesn't interfere**: text goes in as Unicode, bypassing the keyboard layout, so it can't be swallowed into a candidate window — CJK, accents and emoji all arrive intact. |
| **Volume** | Mute / unmute / set level (setting a level unmutes first) / **mute or unmute the microphone** — that one mutes the default recording device itself, so no app hears you, which is stronger than any meeting app's own mute button. |
| **Window action** | By process name (the **…** button picks one, searchable): close / minimize / maximize / **keep on top** / bring-to-front / bring-to-front-and-send-keys / **back to the previous window**. **Keeping a window on top is a toggle** — run the same step again to release it, so one gesture covers both pinning and unpinning. **It is tracked per window**: pin A, click over to B, and triggering it again pins B; to release A, bring A to the front first. It does not steal focus, because what you pin is usually a secondary window (a reference doc, a player) and stealing focus would bury the one you are typing in. **Type `*` as the process name for the current window** — the one you were using before you drew the gesture or opened the panel. (An empty process name means the step does nothing at all.) Gestures need this: whichever window you draw on is the one to act on, and there is no process name to type in advance. (If Clockwork itself is in front, nothing happens — otherwise clicking Run to test a *Close current window* step would close Clockwork.) Slow apps can **wait up to N seconds for the window to appear** — acts the moment it shows, instead of a blind fixed delay. **Back to the previous window takes no target**: it returns to whatever you were using at the moment the action fired. Put it at the end of a run of window steps — each of those grabs the foreground before acting, so after "minimize Slack, minimize Discord" focus lands wherever Windows feels like, and where you want to be is where you started. If Clockwork itself was in front when it fired (which is what happens when you hit Run to test a step), it says plainly that there is nowhere to go back to. Only **real application windows** are targeted — a process's hidden helper windows (the taskbar, the desktop, background shells) are filtered out, so "close explorer" can't hit your taskbar. After acting it **verifies the window actually changed** and reports honestly when it didn't, worded separately from "no window found" (the first points at foreground lock / an elevated window / an unsaved-changes dialog; the second at why the process isn't up). |
| **System command** | Show desktop / lock (needs password to return) / turn off monitor (wakes on mouse move) / empty recycle bin / clear clipboard / **set clipboard text** / **search the selected text** / **play a sound** / open Windows Settings / open Task Manager / screenshot / **display mode: PC screen only · duplicate · extend · second screen only** / **turn notifications off · on** / **screen brightness** / sleep / hibernate / sign out / restart / shut down (the last three confirm first). Four take an argument, shown only when that command is selected: *set clipboard text* adds a text box, *play a sound* a file path (**leave it empty for the system notification sound**, which follows your Windows sound scheme; give a path and it plays that `.wav`. Wav only — for other formats point **Launch App** at the file and let the system player have it. The sound plays asynchronously, so it never holds the action up), *screen brightness* a 0–100 field, and *search the selected text* a **search engine** dropdown — Bing / Google / Baidu / DuckDuckGo / Yandex / Naver, Bing by default. Pick *Custom…* to type your own URL, with `{0}` where the query belongs.<br>*Search the selected text* presses `Ctrl+C` for you and searches whatever lands on the clipboard — nothing can ask the system what is selected, so this is the only way to get it, and **it does overwrite your clipboard**. With nothing selected it tells you so instead of opening an empty search page.<br>**In a terminal it tries a different set of copy keys** (cmd / PowerShell / Windows Terminal / conhost / mintty / ConEmu): there `Ctrl+C` means *interrupt the running command*, so searching for a word would kill your `npm install`. The order is **`Ctrl+Insert` → `Ctrl+Shift+C` → `Ctrl+C`**, stopping at the first one that works. The first two only ever copy and never interrupt, and on a default configuration one of them always lands. `Ctrl+C` is last because measurement forced it: **Windows Terminal does not bind `Ctrl+C` to copy by default**, so many people rebind it themselves — and that rebinding shadows the first two. The cost, stated plainly: draw this gesture in a terminal with **nothing selected** and the last attempt interrupts whatever is running. Editors that host a terminal (VS Code, Visual Studio) deliberately stay on `Ctrl+C`: a process name cannot tell whether focus is in the editor or the terminal pane.<br>The clipboard wait is capped at 1s: a successful copy continues immediately, and only a genuine failure waits it out. Complex web pages and PDF readers can take hundreds of milliseconds (Quicker documents this exact failure). |
| **Message** | Show a line of text. Two forms: a **dialog** (must be clicked away; can ask Yes/No and act on Yes) or a **card** (slides in from the corner, non-blocking, auto-closes after the seconds you set, 0 = stays until you dismiss it). **Only the card form pops from the startup list** — a dialog at login would stall the whole list, so dialog-form messages are silently skipped there. A message step always shows once and has no repeat count. See the Actions section below for the details. |
| **Open URL** | Just an address box. It was split out of *Launch program*, whose box is labelled "program / shortcut / script / folder / URL / document" — anyone wanting to open a web page had to first recognise which of six things they had. The address can contain `{name}` placeholders, substituted at run time and escaped for you (see *Chaining steps into one action*). Program paths deliberately do **not** get this substitution — that is an injection surface. |
| **Get the selected text** | Presses the copy key for you and puts the selection on the clipboard for later steps to read as `{clipboard}`; it can also **save the answer to a variable**. Same machinery as *search the selected text*: clipboard sequence numbers, a different key order inside terminals, and a named diagnosis when nothing comes back. |
| **Wait for the clipboard** | Waits until the clipboard changes, or until N seconds pass (1–60). A timeout is **not** a failure — it continues either way and just says so. |
| **User input** | Pops a box, asks a question, and **saves what you type into a variable**. The question is the text on the box; a default answer is optional (prefilled and selected, so type to replace or press Enter to accept). Esc / Cancel means *don't do this action* and stops the remaining steps — not "carry on with a blank", because the steps after it are usually waiting on that value. Pressing Enter on an empty box is **not** a cancel: that's "I want this one blank". |
| **User choice** | The same, with a list instead of a text box: one option per line, listed at run time for you to pick one, and the line you picked goes into the variable. Double-clicking an option picks it and confirms. With no options configured it says so and skips, rather than popping an empty list you cannot dismiss with a choice. |
| **Delay** | Just wait N seconds before the next step; at the top of the list it delays the whole run. |
| **Action** | Run a defined action; set a repeat count to loop the whole group. |

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

### When the startup list goes wrong

Two things exist for the case where the list does something you didn't want.

**You get told when a step fails at boot.** The run always writes `clockwork.run.log` next to your config (tray → **View last startup log**), but at login nobody is watching the screen, so a failure now also raises a card: "12 steps, 3 failed". A clean run stays silent — the card only appears when something failed, the run hit the step budget, or it was stopped. This is the run you are least able to watch and the one most worth reporting.

**You can stop the list from running at all.** A step that grabs focus, locks the screen or signs you out turns every login into the same ambush, and fixing it requires getting in first. Two ways out, for two different situations:

- **Hold Shift while logging in.** Use this when you're already locked out — it's the only input you still have. It skips the list for that boot only; the next one runs as usual.
- **Create an empty file named `clockwork.disable-startup` next to `clockwork.settings.json`.** Use this when you can still get to the desktop and just want the list to stop running — while you debug one step, or for a day you don't want the usual dozen apps. It keeps working until you delete it, and it says so in the card each boot, naming the exact path.

Either way the app still starts into the tray as usual, so **Rerun startup list** is right there when you want to test a fix.

### Stop anytime

Three ways, all doing exactly the same thing: the **stop button** at the right end of the window's tab bar, tray → **Stop running actions**, or the global **panic hotkey** (set on the Settings tab; default `Ctrl+Alt+Q`). Whatever is running (startup list / action / single step) stops after the current action; long waits (startup delay, waiting for a window) are interrupted immediately. The run log records "manually stopped". If the hotkey is taken by another app and fails to register, a tray toast warns you (use the button or the tray menu's Stop as a fallback).

> The stop button **only exists while something is actually running** — that is the point: its presence tells you something is running, and its disappearance tells you the stop went through. Hover it for the current panic hotkey.

> **To stop just one action**, press **that group's own hotkey** a second time (see "Actions → the hotkey is a toggle"). The panic hotkey is the master switch: it stops the startup list and every running group at once.
> This only works for groups that have a hotkey bound. A group started as a scheduled task's silent group, as an On-Yes action, or as a nested reference has no per-run cancel of its own unless you also give it a hotkey — for those the panic hotkey is the only way out. If you want an unattended long-running group to be stoppable, bind it a hotkey of its own.

> To "wait until the network / desktop is ready" instead of sitting out a fixed delay, tick **Wait until the system is ready** on the Settings tab (it used to be the json-only `startupWaitForReady`): it goes as soon as both are ready, waits at most 90 s, and the fixed delay above is then added on top. When the list runs too early for your apps, this beats simply raising the delay.

## Scheduled tasks

- Each task either **pops a reminder** (text / speech / on-Yes action) or **silently runs an action**.
- **Trigger:** timed, **at login** (with "only within N minutes of boot" counting as login — 10 min by default for new tasks), or one of the 7 **events** below.
- **Recurrence:** by weekday / every-N-days / monthly; the reminder can be read aloud.

### Event triggers

These don't watch the clock, they watch the machine — the task fires the moment the event happens, and does exactly what a timed task does (reminder / speech / on-Yes action / silent action).

| Event | When it happens |
| --- | --- |
| **When idle** | No keyboard or mouse for N minutes (10 by default). **Fires once per absence**; the count restarts when you come back. |
| **After continuous use** | The mirror of idle: you have been at it for N minutes without a break (30 by default) — the "stand up and stretch" reminder. **Fires once per streak**; a full minute away from the keyboard restarts the count. |
| **On unlock / on lock** | The moment you hit Win+L, and the moment you come back. Switching users and remote connects don't count — that's a different thing. |
| **On wake from sleep** | Coming back from lid-close or sleep. Good for the tidy-up work: reconnect the VPN, remount a share. |
| **When plugged in / unplugged** | The instant the charger goes in or out. *Unplugged → switch to power saving* is the classic. |
| **On low battery** | On battery and the charge drops below N% (20 by default). **Fires once per drop**; it re-arms after charging back above the threshold or plugging in. |
| **On display change** | A monitor plugged in or unplugged, a resolution change, a switch of projection mode. Pair it with the **display mode** system command and docking becomes one routine. It reports "something changed", not which way — a dock and an undock look the same here, so gate the two directions with a process or path condition on the steps. |
| **On network connected / lost** | The machine has usable network again, or just lost it. This is the precise trigger for the reconnect chores that "on wake from sleep" only approximates — remount the share once the network is actually up, not 200 ms after the lid opens. |
| **On USB drive inserted** | A volume arrived. Backup-on-plug without polling for the drive letter. A multi-partition stick fires once, not once per partition. |

- **Weekday limits still apply** ("clock in on unlock, weekdays only") — the weekday row stays visible in the editor.
- **Grace** and **catch-up** are meaningless for events and are hidden: an event fires as it happens, and if the machine was off it simply never happened, so there is nothing to make up.
- **Snooze, nagging and interval runs all work as usual** — everything that follows a reminder going off is shared with timed tasks.
- **Pause reminders (do not disturb)** suppresses events too, same as timed tasks.
- Idle, continuous use and battery are **polled** (they ride the reminder timer, 30 s by default), so they can be up to half a minute late; lock / unlock / wake / power changes, display changes, network changes and USB arrivals are pushed by Windows and are immediate.
- **A reminder will not interrupt a fullscreen game or a presentation.** When Windows reports that the screen is busy — an exclusive-fullscreen game, a fullscreen app, projection mode — a reminder that would put something on screen quietly waits 5 minutes and tries again, for as long as that lasts. Nothing is dropped and nothing is capped: you see it when you come out. Two deliberate exceptions still come through, because for them "don't interrupt" is the wrong answer: **silent actions** (they never put anything on screen, so there is nothing to interrupt) and the tray's **quick reminder** (you set that timer by hand a few minutes ago — "you said 5 minutes" outranks the courtesy). This is separate from **Pause reminders**, which is yours to switch on and off; Windows Focus Assist is deliberately *not* wired in, because two invisible do-not-disturbs that can't see each other is the hardest kind of silence to diagnose.
- **Interval runs**: "every N minutes until HH:mm" (empty = end of day). Distinct from "nag until confirmed" — nagging stops on confirmation, interval runs keep going. Intervals never cross midnight; the next day starts fresh from the task's base time.
- **Skip today**: right-click the row → **Skip today** (**click it again to undo**). Once skipped the time column reads “22:00 · skipped today” — you do it in the menu and see it on the row, so there is nothing to remember. And the rest of today is written off for that reminder — in-flight nags, interval rounds and snoozes included — while tomorrow runs as usual. All three trigger kinds (timed / at login / event) answer the same way. It is safer than unticking: unticking has no expiry, and one you forget to re-tick is a reminder that fails silently forever. The skip is persisted, so a restart does not undo it.
- **Quick reminder**: tray right-click → **Quick reminder** → 5 / 15 / 25 / 30 / 60 minutes. It is accurate to within one tick (30 s by default) and never fires early; while reminders are paused it says so plainly instead of pretending the timer is armed. Creates a one-shot reminder on the spot — **it shows up in the list on this page**, so you can delete it if you change your mind — with a sound and a card that stays until you dismiss it, and **deletes itself once it has fired** — a scratch timer should not leave a dead row in your config. For any other length, just add a normal reminder on this page. If its minute lands during do-not-disturb or sleep it fires late rather than being dropped; if a whole day passed and it never fired, the next start clears it out instead of leaving a dead row.
- **Run once**: pick "Once" and a date. After it completes, the entry unticks itself but stays in the list — set a new date and re-enable to reuse.
- **Sound**: one **Sound** dropdown in the editor, four choices — **Silent / Chime / Read aloud / Chime + read aloud**. The chime is the system "Asterisk" sound, there only to make you look up; read-aloud speaks the whole message. Pick both and the chime lands first, before the speech starts (otherwise the ding sits on top of the first word). A card neither steals focus nor sits on top, so when you are looking at another screen the sound is often the only part you notice. It follows your Windows sound scheme, so a silent scheme stays silent, and silent actions never make a sound (that is what "silent" means). Fresh samples ship with the chime on; reminders you already had keep their current setting.
- Reminders with **no On-Yes action** slide in as a **reminder card** in the corner (non-intrusive). How long it shows is set by the **auto-close** seconds — **0 = stays until you dismiss it**, so nothing is missed if you're away. Repeat-nagging reminders still use a dialog (so you can stop the nagging with one click).
- Reminders **with** an On-Yes action (run program / open file / URL / run action) pop a top-most **Yes / No** dialog with a **Snooze** button (default 10 min, ▾ menu 5 / 10 / 15 / 30 / 60 min). Enter = **Yes** as always; for the first 0.6 s after the dialog appears, **Yes** doesn't respond — a dialog that steals focus mid-typing can't run the action on an in-flight space bar or Enter.
- **An unanswered dialog gets out of the way.** The dialog is modal, so leaving it up would block every later reminder. A dialog with no auto-close stays up for at most 1 minute; when it times out unanswered it turns into an automatic **"snooze 10 minutes"** and comes back later — nothing is blocked, nothing is silently lost (the auto-snooze is persisted like a hand-clicked one, surviving restarts; it expires at midnight, except reminders with **catch up if missed** enabled, which re-fire once the next day). Repeat-nagging reminders keep nagging on your configured cadence instead. After **6 rounds** with no answer, the dialog stops re-popping and degrades into a **persistent corner card** waiting for your return — re-interrupting an empty desk is pointless. Note the card has no Yes/No buttons: once degraded, the On-Yes action no longer runs. Hand-clicked snoozes don't count toward those 6 rounds and reset the streak — you were there, so the clock starts over. The cap counts **rounds, not elapsed time**: with the defaults six rounds is about an hour, but if you set a long auto-close (say 1800 s) each round also holds the dialog up for that long, stretching the total several-fold.
- **Repeat fires of one reminder share a single card** (`×N` at the top right), so the corner never fills up. Cards you dismissed, evicted, or that auto-closed — and dialog-form reminders too (unanswered ones marked in the warning color) — can be reviewed and re-shown from **tray right-click → Recent** (session-only; cleared on restart).
- **Advanced:** auto-close · repeat-nagging (re-pop every N minutes until a deadline; a blank deadline caps it at 20 nags) · post-trigger delay + random jitter · grace (catch a fire missed by a brief shutdown/sleep) · **catch up if missed** (re-fire once after hibernation/shutdown skipped it) · an **anchor date** for every-N-days (the **…** button picks it; left blank it is pinned to today on save, and every N days counts from there).
- **State persistence:** "fired today" and "snoozed until" are saved to `clockwork.state.json`, surviving restarts — a snooze carries across a restart and the same reminder never double-fires in a day. Interval progress is persisted the same way, so restarting mid-day keeps the day's remaining rounds.
- **Midnight and long-standby edges all resolve to "fires exactly once":** a late reminder (say 23:59) that actually pops after midnight is still recorded against **the day it was due**, so it fires again the next day instead of degrading to every-other-day; a nag chain that slept past its own deadline ends there rather than reviving the next morning and nagging for hours; and an absurd future timestamp left behind by a wrong system clock (VM snapshot restore, dead CMOS battery) is discarded as junk instead of silencing that reminder forever with no way back but deleting the state file.
- **Do-Not-Disturb:** tray → **Pause reminders ▸** → 1 / 2 / 4 / 8 hours, or **until the end of today** (the one entry you never have to do arithmetic for — away all day, recording, in meetings). Everything (including silent groups) is suppressed and auto-resumes when the time is up; you can also **Resume** early. Anything missed follows the normal grace / catch-up rules.
- **Silent action:** run a group on time with **no popup**. Selecting a task and clicking **Run** runs it once — note that for a silent task, Run **actually executes** the group.
- **What the list columns say:** a task triggered **at login** shows **Every login** in the period column (it never consults a weekday/monthly recurrence, so the editor hides that block too), and a **silent** task shows the group it runs in the text column instead of an empty cell.
- **Duplicate** clones the selected task right below it (same text and settings, its own schedule state) — handy for "same task, second time of day": duplicate, then just change the time.

## Actions

- **Add** opens an empty action straight away. To start from something that already works, use **From a template…** next to it: pick a **built-in template** (Focus mode / Meeting mode / Back to normal / End of day / Bedtime / Stepping away / Time for a break / Quick search), tweak the process names and save.
  - **Back to normal is the way out.** Muting notifications and muting the mic are stateful — they don't undo themselves. Focus and Meeting each switch something off, so keep a *Back to normal* around (a hotkey suits it well) to switch them all back on.
- The list shows each group's **step summary** and **hotkey** columns (an empty group's summary reads **(empty)**).
- A group runs **only one copy at a time** (repeat triggers are skipped — except its hotkey, see "the hotkey is a toggle" below).
- Trigger it four ways: tray **Run: <group>** · a **global hotkey** · an **action-group step** in the startup list (at boot) · a scheduled task's **On-Yes / silent group**. You can also select a row on the Actions tab and hit **Run** to fire it once by hand.
- **Show in tray menu** (in the group editor; **off by default for new groups**): once you have a few groups the tray menu turns into a long strip, and most groups are triggered by a hotkey, a reminder or another group anyway — they don't need a row. A hidden group still works everywhere else: hotkeys, reminders, references and the **Run** button on the Actions tab are all unaffected; it just isn't listed in the tray. Groups that existed before this option was added keep showing, so nothing disappears on upgrade.
- **Global hotkey:** in the group editor, click the hotkey box and press a combo (e.g. `Ctrl+Alt+F`) to run the group from any app — no menu needed. Esc cancels, Del clears. Changes apply live (no restart). A **disabled** group releases its combo so another group can use it. Refused with a notice: **system-reserved** combos (Alt+F4, Alt+Tab, Ctrl+Shift+Esc…), a combo already bound to **another enabled group** or the **panic hotkey**, or one **already taken by another app** (use a different combo).
- **The hotkey is a toggle — press it again to cancel.** Pressing the same hotkey while the group is still running cancels **that run**: the remaining steps and rounds are dropped and a tray toast confirms it. The cancel is scoped to that one run — **the startup list and other groups keep going** (use the panic hotkey to stop everything). What that means in practice:
  - Most groups finish in a few hundred milliseconds, so a second press then simply **runs it again** — cancelling only matters for groups that are still running (ones with delays or repeat rounds).
  - Delays between rounds and between steps are **interrupted on the spot**; you don't wait out the current sleep.
  - If the group is sitting on a **message** confirmation box, the box still needs dismissing, but the answer is discarded — clicking **Yes** no longer fires its On-Yes branch, and the group stops there.
  - If group A references group B and you press **B's** hotkey while B runs, **the whole run is cancelled** (A stops too) — stopping only B and letting A carry on would leave you with a half-finished state that is harder to clean up than not cancelling at all.
- A **message** step can act as a confirmation gate — answering **No** aborts the rest of the group (e.g. "Did you log today's tasks?" before wrap-up).
- **Presentation:** a message step shows as either **Dialog (blocks)** — the default, same as above — or **Card (non-blocking)**: it slides into the corner and **auto-closes** after the seconds you set (**0 = stays until clicked**), and the group carries straight on without waiting for an answer. Switching to card hides and clears the confirm (Yes/No) and On-Yes fields, since a card's only interaction is click-to-dismiss. Cards are also the only message form that fires from the **startup list** — a dialog at boot would block the whole list, so dialog-form message steps are silently skipped there.
- Inside the group editor, **drag a step to reorder it** — same as the three list pages, each row carries a drag handle (≡) at its left edge; the up/down buttons still work too.
- **Duplicate** clones the selected group as "… (copy)" — a quick base for a variant. The copy gets **no hotkey** (two groups can't share one), so assign a new one if you want it.
- Deleting a group that is **referenced** (by a scheduled task's On-Yes / silent group, or an action-group step) tells you how many references there are and clears them along with it, so nothing is left pointing at a group that no longer exists.

### Try it before you save

The group editor has two run buttons that act on **whatever is currently in the editor**, not the last-saved version — so you can test a step you just tweaked without saving first.

- **▶ Run This Step** runs just the selected row. If that row is itself an action-group step, it resolves and runs the referenced group as currently saved, not any unsaved edits made to that sub-group elsewhere.
- **▶ Run Group** runs every step top to bottom, exactly like a real trigger — including delays, repeat counts, and weekday / before-N-o'clock conditions, so a weekday-only step really is skipped on the wrong day.

While a whole-group run is going, **▶ Run Group** turns into **■ Stop**; closing the group editor also stops the run. Confirmation dialogs (e.g. from a message step) pop up in front of the editor window instead of behind it.

### Chaining steps into one action

**Steps that produce a value have a "save answer as" field, and later steps refer to it as `{name}` in any text they send out.** That is the whole rule.

Three kinds produce a value: **user input**, **user choice** and **get the selected text**. The places that can *read* one are the text you send somewhere: the address of *open URL*, the body of *send text*, and the text argument of a system command. Program paths and arguments are **not** substituted — that is an injection surface.

So "translate whatever I selected" is two steps:

1. **Get the selected text**
2. **Open URL** `https://www.deepl.com/translator#auto/en/{clipboard}`

And "ask me what to search, then search it" is also two:

1. **User input**, question "Search for what?", saved as `query`
2. **Open URL** `https://www.bing.com/search?q={query}`

Bind that to a gesture or a panel tile and one flick gets you a box; one Enter gets you the results.

A few things worth knowing:

- **`{clipboard}` is the one built-in name.** It reads the real clipboard right now, so no step has to produce it first — whatever another app just copied counts.
- **Names ignore case and surrounding spaces** (`{ query }` and `{query}` are the same one).
- **A misspelled name stays visible**: an unknown name is left in the text as-is, so you can see at a glance which word didn't take. A name that exists but is *empty* becomes an empty string — that isn't a typo, it's a step that genuinely got nothing.
- **The scope is one run.** A single trigger (including any actions it references) shares one set of variables, and they're gone when it finishes. No global variables, nothing saved to disk, nothing shared between actions.
- **Everything is a string.** No types, no expressions.
- **The startup list can't ask.** It runs unattended, so a question step there is skipped and logged — a box blocking your whole startup list at login is nobody's idea of a feature.

### Triggering a group from outside

Anything that can start a program can now run a group:

```
Clockwork.exe --run-group "Focus"
Clockwork.exe --run-group=Focus
```

The name is matched case-insensitively with surrounding spaces trimmed, and a group **id** works too. Quote names containing spaces, as with any other command line.

Point Task Scheduler, a desktop shortcut, a Stream Deck button or an AutoHotkey binding at it. If Clockwork is already in the tray the request is handed to that copy and the launched process exits immediately — it does **not** pop the settings window, which would be a strange side effect for a group meant to run silently at 3 a.m. If nothing is running yet, that process becomes the tray instance and runs the group itself, then stays resident like a normal launch.

You get told when it doesn't work: an unknown name or a switched-off group raises a card naming it, rather than failing silently and leaving you to debug a command line that was fine all along. The forwarding process also exits with a non-zero code if it could not deliver the request, so a script can check.

## Quick panel

A tile grid at your mouse, one press away — for when the tray icon is a long way from where your hand already is.

- **Press the panel hotkey** (default `Ctrl+Alt+Space`, set on the **Settings** tab — in that box, Esc cancels and **Del clears it**, leaving the middle-button hold and the tray entry as the ways in) and the panel appears at the pointer. Press it again, press **Esc**, or click anywhere else to dismiss it.
- **A page is a thing you make, not a side job of some action.** Hit the **+** at the end of the left rail in the **panel manager** and a card drops in with its name ready to type, **in whichever category you were looking at**; the **+** at the end of the top row makes a category instead — just the category, no page with it, so its rail starts empty and you add the first page yourself. click an empty slot to put something on it. A page's name, category and app binding are all edited in place on its header — there is no separate settings window.
  - **Pages and actions are two different things.** An action is a run of operations plus how it fires (hotkey, tray, referenced elsewhere); a page is a screenful of tiles plus when it shows (category, app). They share no properties, so they no longer share a stored object — configs from older versions are moved across on first launch, and you get a card listing what changed.
  - **Every step kind works as a tile** — launch program, open URL, send keys, mouse, send text, volume, window action, system command, run action, delay, message, and the rest. A tile runs that one operation the same way the **Run** button next to a step does.
  - **Want a tile that runs a whole action?** Add a **Run action** step pointing at it — that is what the step kind is for. So a whole routine ("Focus", "Wrap up") and a single operation (lock screen, mute) sit side by side on the same page. A tile with no icon of its own borrows the icon of the action it points at.
  - Page order is set by dragging tabs; tile order by dragging tiles.
- **Switched-off tiles are greyed, not missing** — so "it's disabled" and "it's gone" stay distinguishable.
- **Then the handful you reach for anyway**, below a divider so they never get confused with your own actions: rerun the startup list, stop running actions, pause/resume reminders, open the window.
- **Mouse or keyboard.** Click a tile, or arrow to it and press Enter — focus starts on the first tile, so you never have to touch the mouse to use it.
- **It closes before it runs.** Actions that bring a window to the front or send keystrokes need the foreground back first; leaving a topmost panel in the way would mean keys landing on the panel and the window you just raised being covered by it.
- **Or hold the middle mouse button.** Tick it on the **Settings** tab and a press of ~350 ms (adjustable) opens the panel — no keyboard at all, and the panel appears exactly where your hand already is.
  - **A normal middle click keeps working.** At the moment the button goes down there is no way to know yet whether it will be a click or a hold, so the press is held back and, if you let go early, a real middle click is sent in its place. Opening a link in a new tab, pasting, autoscroll — all unchanged.
  - **Middle-drag still works too**: move while holding and it's treated as a drag, the held-back press is handed straight to the app underneath, and the panel stays away.
  - **The panel opens when you release**, not the instant the threshold passes. You can't click tiles while holding a button down, so opening on release means the panel is up and your pointer is already next to it.
  - **Off by default**, because it installs a global mouse hook — a thing worth switching on knowingly rather than inheriting from an update. If Windows refuses to install the hook (security software, group policy), you get a card saying so instead of a feature that silently isn't there.
  - Raise the hold time if ordinary clicks start opening the panel; lower it if holding feels sluggish. It clamps to 150–2000 ms — below that, a slightly slow click would register as a hold and middle-clicking would get unreliable.
- **The tray menu has an entry too**, so clearing the hotkey turns off the shortcut, not the feature.
- **To turn off the feature itself**, untick **Use the quick panel** on the **Settings** tab. Hotkey, middle-button hold and
  the tray entry all go with it, and — the point of the switch — no mouse hook is installed at all.
- **The panel opens centred on the pointer** — every tile is the same short distance away, rather than the far corner being three times further than the near one. Near a screen edge it slides back just enough to stay fully on the monitor your pointer is on, mixed-DPI multi-monitor setups included.
- **Tiles are an icon over a label.** The icon says what kind of thing it is at a glance — launch, keys, volume, window, system command — which a label that narrow cannot. Icons come from the font Windows already ships, so they cost nothing in download size and stay sharp at any scaling.
- **Every tile keeps a tally of how often you press it.** The count is keyed on the step's own identity rather than on the slot, so renaming a tile, dragging it to another page, or deleting and re-making one under the same name keeps the same tally. Open the **panel manager** and each tile carries its count as a badge and in its tooltip; **Reset click stats** in the footer — shown only when there is something to clear — wipes them all.
  - **It never changes what the panel shows.** No tile is reordered, hidden or promoted by its count: this is a record of what you reach for, not a suggestion about what you should.
  - **The tally is kept apart from your settings**, in `clockwork.usage.json`: a click has to be counted on the click, and saving the settings means re-binding every hotkey and re-installing the mouse hook — a price a counter should not be paying, least of all while you are clicking. Losing one costs nothing either, so it keeps no retry queue.
  - **Reset asks first**, because that history is the one thing in there with no second copy. It touches the counts only — no setting, no tile.

### Pages

Pages follow content, not a tile count: **each one is a page you made and named yourself.** So you flip between "Handy" and "Dev", not "2 of 4". Only when a page overflows is it continued onto a second screen under the same name — that is a capacity problem, not a content one.

**Two rows — the top one picks a category, the left one lists that category's pages.** A category is a name you give
a batch of pages (*Coding*, *Winding down*). Pages have none by default, so everything sits under *Uncategorised*,
which is what most people will see forever. A page lives in exactly one place — the left row — so there is never a
question of which row a page is on.

- **With one category the top row is not drawn**, and with one page in the current category neither is the left one
  (a tab would just repeat the name). The panel manager's appearance strip has a tick for each, to force one out of
  sight when you do have several.
- **The dots in the waist answer a different question.** A page that does not fit is split into screens, and the dots
  say which screen you are on. Tabs answer *which page*, dots answer *which screen* — only when both rows are off do
  the dots fall back to meaning pages, so that turning the tabs off does not take page-flipping with it.
- **Both rows hide while you search**: results come from every page, so a highlighted tab would claim you are
  searching inside one.
- The panel manager shows the same two rows (same styles). **Putting a page in a category** has three routes, take
  whichever is nearest: drag its tab **onto a category tab**, or click the button in its heading that names its
  current category. The **+** at the end of the top row makes a new category **with a page already in it** (an empty
  category opens onto nothing), puts it last in the row, and lets you name it on the spot. **Double-click a category
  tab** to rename it; that rewrites the category name on every page in it, because a category is not a record of its
  own, it emerges from the pages. The manager always keeps its category row, even with only *Uncategorised* on it,
  because that + is the only way to make a category.
- **Both rows sort freely**: drag a page tab to reorder pages, drag a category tab to reorder categories (the whole
  category travels, and the order inside it is kept). Categorising a page also slides it to the end of that category's
  run — without that the two categories' pages interleave, and since category order is *whose first page comes first*,
  a later drag inside one category could reorder the categories: you drag a page and a category moves.
- **Right-click a tab** (or the page heading): rename, or **remove from the panel**. Removing only takes the page off
  the panel — the action stays on the *Actions* page, where hotkeys and schedules still reach it. Deleting
  it for real still happens only there, and shows you the reference count first.

**A group can also belong to one app.** In the panel manager every page heading says which app it is bound to — a faint *Any app* by default; click it to change or to unbind. (It is the same field as **Only on this app** in the group editor.) Give it a process name and the group appears on the panel *only while that app is the one you were using when you opened it*.

**This is a separate thing from the category.** A category is a name you group pages under; the app binding is *when the page should appear* — a page can sit in *Coding* and be bound to Code at the same time. Nor is the binding a layer of filtering inside the manager: every page is always on the rows, bound ones included, because you have to see a page to change what it is bound to. The filtering happens only when the panel is actually opened.

This is what makes the panel worth opening in the first place: pop it over your editor and you get your dev actions; pop it over the file manager and you get file actions. The matching page sorts to the front and the panel opens on it, so the right actions are already under the cursor — no page turning. With no match for the current app, the panel opens on the general page exactly as it would without the feature.

- **The app is read the instant you summon the panel**, before it appears — the panel takes the foreground itself, so a moment later the answer would always be "Clockwork".
- **Don't confuse this with a step's process condition.** *Only on this app* asks "which app are you using right now" and decides whether the group is **shown**; a step's *process condition* asks "is that program running at all" and decides whether the step **executes**. The second can be true in the background while you work in something else.
- Scene tiles also sort ahead of global ones on the general page.
- If the foreground app can't be read (a restricted process, no foreground window), scene groups stay hidden rather than appearing in the wrong place.

- **Turn pages** with the mouse wheel, **PageUp / PageDown**, or by clicking a segment of the index. Arrow keys are left alone for moving between tiles — if they did both, reaching the end of a row would have two meanings.
- **The index across the top is a graduated scale, one segment per page**, with the current one in brass. It replaces a numeric page counter: a scale that shows position already answers "which page, out of how many". The page name sits next to it.
- Pages wrap, so rolling the wheel past the last one comes back to the first.
- A page with more tiles than fit **scrolls inside the page** — that's a capacity problem, not a reason to invent a second page.
- With only one page the index and the page name disappear entirely; there is nothing to navigate.

### Making it look how you want

On the **Settings** tab, under **Quick panel**:

- **Layout — read it as `columns × top rows + bottom rows`** (4 × 3 + 4 by default). The well is split into two bands with a hairline between them. They mean nothing different from each other; the split is rhythm. A single 7-row slab of squares reads as a wall, while 3 + 4 gives the eye a place to land, the same reason a keyboard has rows. Set bottom rows to **0** for one band.
  - Columns × total rows is also **the page capacity** — 28 tiles by default. Anything past that continues onto the next page under the same name.
  - The bands are limits, not reserved space: with five actions the bottom band isn't drawn at all, so a small panel stays small.
- **Tile size** (compact / normal / roomy) — compact at 8 across gives you a dense keypad; roomy at 3 gives you big targets for a touchscreen.
- **Icons only** — drops the labels and shrinks the tiles to near-square, so far more actions fit on one screen. The names stay in the tooltip, so this hides them until you need them rather than throwing them away.
- **Show built-in controls** — the rerun / stop / do-not-disturb / open-window row along the bottom. Turn it off if you reach those from the tray and would rather have the space.

## Loops

- **Repeat a whole group**: set "Repeat whole group / delay between rounds" in the group editor.
- **Loop a subset of steps**: extract those steps into their own action, then reference it with a "group" step and set its repeat count.
- Three repeat knobs multiply: per-step repeat × reference-step repeat × whole-group rounds.
- Groups can nest group references; saving validates cycles among action-group **step** references (A→B→A is rejected with the chain shown). A cycle formed through a message step's "on Yes → run group" target is not checked at save time, but is caught at run time by re-entry protection (skipped with a warning, never spinning).
- **Answering "No" to a message step stops everything**: the rest of that group **and its remaining rounds**, and if the group was reached through a reference from another group, that caller's remaining iterations too. So when you loop a subsequence the recommended way (sub-group referenced ×N), declining once is enough — the same dialog will not chase you N times.
- A referenced group that is **missing** (deleted, or no group picked when the step was created), **disabled**, or **already running** (including a cycle) is never silently skipped: missing and re-entrant are reported as warnings, disabled as a plain notice (you turned it off yourself — that is not a fault). Re-entry also stops that reference's remaining iterations, so one notice never repeats N times.
- **A step inside a group that ran but didn't take now speaks up too.** A missing script file, an uninstalled program, a window that couldn't be found — these raise a tray notice and a line in `clockwork.error.log`, and the group carries on. This path used to be completely silent: the same step in the startup list showed a "⚠", while hotkeys and silent scheduled groups — the unattended path — said nothing at all. Repeated failures of the same step merge into one notice with a count instead of stacking up.
- Safety fuse: a single run executes at most **5000 steps** — every execution of a normal step counts as one, and so does every "action" reference iteration (otherwise a chain of nothing but references, with empty leaf groups, would slip past the fuse). Past that it stops and says so in the run log. The stop hotkey works at any time.

## System startup items

- Lists **everything that auto-starts** (registry Run keys, Startup folders, scheduled tasks).
- Uncheck **Enable** to switch an item off — **disabled, not deleted; re-check to restore** (takes effect immediately).
- Items marked **needs admin**: acting on them prompts to relaunch as administrator, then you can proceed.
- System / policy / one-time items (Group-Policy Run, RunOnce, Winlogon, Active Setup) can't be touched and are **hidden by default** — tick **Show system / read-only items** (top-right) to view them (greyed out; the right-click actions below are disabled for them).
- **Right-click a row** for two actions:
  - **Take over into launch list** — hands the item to Clockwork (disables the original + adds it to your list). Registry Run keys and Startup-folder items only; scheduled tasks aren't supported yet (you'll get a notice).
  - **Delete from system** — removes the entry for good (registry value / Startup-folder shortcut / scheduled task). It asks first and **cannot be undone** — if you only want to stop it running at boot, uncheck **Enable** instead. If the item was taken over earlier and a step still points at its shortcut file, the confirmation says so, because deleting the shortcut breaks that step.
- A top **filter** searches by name / command.

## Ports

- **One row is one port**, not one process, followed by every process holding it. Both the IPv4 and IPv6 tables are read, so a service listening on `127.0.0.1` and `::1` at once is still a single row (the Address column shows both).
- **A port really can be held by several processes at once.** The Process column then reads `python ×2` and the PID column lists them all. Windows' `SO_REUSEADDR` lets a later process bind an address that is already taken (on Linux that option mostly just affects TIME_WAIT — the semantics are not the same), and **which one actually receives new connections is documented as indeterminate** — measured here, three connections in a row all went to the process that started **first**, so guessing by start time guesses wrong. Python's `HTTPServer` enables it by default, so running the same script twice raises no error: the earlier one becomes a zombie that receives nothing, while everything looks fine from the outside. Grouping by port is what makes that visible.
- **Rescans when you open the tab, and every 5 seconds while you stay on it** — a server you just started shows up within 5 seconds on its own. **Refresh** at the bottom left is still there for when you want it now. The bottom right carries a running **`5 of 49 ports · refreshes every 5s`**: the first number is what the current view kept, the second is everything listening — one number answers “am I being shown less than there is?” faster than a paragraph explaining the filters.
  - When a scan comes back identical to the last one, **nothing in the UI is touched at all**: no flicker, no lost selection, no scroll jumping back to the top.
  - The polling stops when the window is hidden to the tray or you switch to another tab.
- The **Source** column answers "what is this port, really", as **project · entry**: two `node` rows look identical by process name, but `web-tools-by-ai · start-server.js` and `035-shiye · cdp-proxy.mjs` do not.
  - **The project comes from the process's working directory** — the most reliable signal there is, because a dev server's cwd is its project root. For a relative path like `tools\serve.py` there is no other way to know which project it belongs to.
  - When the cwd is an anonymous directory such as `C:\Windows`, the path in the command line is used instead: the segment before `node_modules` is the project name, and failing that, generic container directories (`scripts`, `bin`, `dist`) are skipped on the way up.
  - **Hover a row for the full command line and working directory.** Elevated and protected processes hand over neither, so the column stays empty for those (13 of 34 port-owning processes were readable in testing; the rest were svchost and friends).
- Note that Source **plays no part in the filtering**: a resident tool process like `cdp-proxy` still shows up under Dev servers only. That is deliberate — the origin is right there to read, which beats a rule that can be wrong and hide a server you actually care about.
- **Double-click a row** to open `http://localhost:<port>` in your default browser; **Open in browser** in the right-click menu does the same thing.
- A process often holds **several ports** (Next's build workers do, and so do QQ and WeChat). The two filtered views **show only the outward-facing one** (bound to `0.0.0.0` / `::`) and fold the rest away, with the Port column reading `3000 +7` to say how many went — one Next server was measured holding 14 ports, exactly one public and the rest loopback-only worker IPC, and a row each buries the list.
  - If none is outward-facing, the lowest port stands in; if a process really serves two (HTTP and HTTPS), both stay. **Show all ports does not fold**, since listing every one is what that view is for.
  - Folded ports have not vanished: **freeing any one of them frees them all**. **Hover the row** and it names them (`Also listening on 10290, 10291`); the confirmation before freeing lists them again, and search still finds them (below).
- Right-click **Free the port** — ends **every** process holding it, each **together with its child processes** (something like `npm run dev` spawns its own workers, and killing only the listener leaves them orphaned). The confirmation names each one (`python (8588)、python (9800)`) — **anything unsaved is lost**. A row held only by kernel placeholders (PID 0 or 4) has nothing to end, so the menu item is greyed out.
- The two checkboxes at the top right give you **three views**, narrowest first (**each one carries its own rule in its tooltip** — “what does this switch actually go by?” is a question people ask while pointing at the switch):
  - **Project services only (on by default)** — the union of two tests:
    - **The working directory carries a project marker** (walking up to six levels from the cwd, looking for `.git`, `package.json`, `pyproject.toml`, `go.mod`, `Cargo.toml` and friends) — the **primary** test. It never looks at the process name, so a Go or Rust binary with an arbitrary name is recognised too — precisely the blind spot a name allowlist has.
    - **The process name is a known runtime** (node / bun / deno, python, java, dotnet, ruby / php, nginx / caddy, postgres / mysqld / mongod / redis-server, Docker's port-forwarding processes, ngrok / cloudflared / ollama) — the **fallback**. Elevated processes never hand over their cwd, and the primary test alone would hide those.
    - This view **skips the 1024 floor**, so a local nginx on port 80 still shows up.
  - **Neither ticked** — system services hidden: port ≥ 1024, kernel placeholders excluded, and noisy owners such as `svchost` and `services` filtered out (svchost alone holds a dozen dynamic ports from boot). **A Go or Rust binary can be named anything, so the allowlist will miss it — this is the view to find it in.**
  - **Show all ports** — no filter. Ticking it greys out Dev servers only, since having both on is a contradiction.
- The Project services only tick is **remembered in your settings**, so you set it once. Show all ports is not: it is a one-off "let me see everything".
- The **filter box** at the top matches on port number, process name or command line (any owner counts, so searching "serve" or a project name beats remembering the port).
  - **Searching turns folding off**: folding exists to keep the default browse view quiet, and typing a query says exactly what you want — otherwise typing a port number that was folded away (and really is listening) would return nothing.
  - It does **not** bypass the view: it narrows whatever the current tier left. Tick "Show all ports" to reach system services; search will not pull them back.

## Mouse gestures

Hold the **right button**, draw a stroke, release, and the matching action runs. Open the manager from the **polyline icon at the right end of the main window's tab strip**, or from the quick panel's title row.

- **A gesture is one action plus one stroke.** The action is an ordinary step, any of the ten kinds; to run a whole action, point a **Run action** step at it. So actions that only ever exist as a gesture — paste, lock screen — need no group of their own. The most common ones — copy, paste, search the selected text, back and forward, play-pause and track skip, volume up and down, minimize / maximize / toggle-on-top / close the current window — are already there under **Add ▾ → Common**: pick one, draw a stroke, done.
- **You can see what you are drawing — and the line brightens the moment it is recognised.** The moment the stroke starts, a line follows the pointer on screen and vanishes when you release. **As soon as what you have drawn matches a gesture, that line turns bright and thickens**, so you know it has locked on before you let go. It goes dark again as the stroke grows past the match and lights up again when it matches something else: draw ↑ and Copy lights up, continue into ↑↓ and it goes dark, and it returns as Search. Colour and weight only — no flash, no popup, no text: the line is already where your eyes are. The line is **click-through and never takes focus**: whatever sits under the pointer keeps receiving your input, so *Minimize current window* and friends still mean the window you were actually using, not the line.
- **Eight directions**: ← ↑ → ↓ and the four diagonals ↖ ↗ ↙ ↘. A stroke is stored as a direction string (`L U R D` for the axes, numpad-shaped `7 9 1 3` for the diagonals) and shown as arrows.
- **Drawing roughly is fine.** The eight sectors are not equal: 56° each for the axes, 34° each for the diagonals. Axes are the default intent and diagonals are deliberate, so the tolerance goes to the axes — a horizontal off by 28° still reads as "→", while a deliberate 45° diagonal still has 17° of margin either side. A line drawn right on a boundary still won't flip back and forth: the alternating short legs it produces get absorbed into the longest one (see the next point).
- **Two legs give two directions.** Direction is measured per *leg*, not over a sliding window: the segment at a corner spans both legs and is itself diagonal, so using it would invent an extra direction. Instead the stroke is sampled finely, split into legs, legs shorter than the minimum are absorbed into their longer neighbour, and each surviving leg is re-measured end to end.
- **Ten samples ship with a fresh install, ready to use.** Gestures differ from the sample startup list and the
  sample reminders, which arrive switched off: those open programs and interrupt you with popups, while a gesture
  happens only when you deliberately draw one — before that it does nothing.

  **The cost, stated up front:** one enabled gesture is enough to take over the right button. The press is held back
  until you either let go or **hold still for 0.2 s**, so **right-drag needs a short pause first** — press, wait a
  beat, then drag (dragging a file with the right button in Explorer, rotating in a 3D app, right-drag scrolling).
  The pause is what tells the two apart: a gesture starts moving the instant you press it, so a press that doesn't
  move isn't one, and the button goes back to the system. To leave the right button untouched altogether, flip the
  master switch below; to drop a single gesture, untick it in the manager.

  | Stroke | Action | | Stroke | Action |
  |---|---|---|---|---|
  | ↑ | Copy | | ↙ | Minimise the current window |
  | ↓ | Paste | | ↗ | Maximise the current window |
  | ← | Back | | ↖ | Toggle current window on top |
  | → | Forward | | ↘ | Close the current window |
  | ↑↓ | Search the selected text | | →↓ | Jump to the bottom |

  The four axial strokes carry the four most-used actions, and they are also the easiest to draw (an axial sector is
  56°, a diagonal only 34°). **The four diagonals are the window set**, and the direction is the meaning: pull down
  to stow, push up to fill, pin to the top-left, sweep out to the bottom-right. On top is a toggle, so one gesture
  both pins and releases.

  Close sits on a diagonal deliberately: a diagonal sector is only 34° wide, so a sloppy stroke simply misses. For an
  action that closes your window, **being hard to draw by accident is the property you want** — the 56° of latitude
  an axial stroke gets belongs to the harmless actions.

  **Both two-stroke gestures are axial, not diagonal.** "Search the selected text" used to be ∧ (↗↘). Measured, that
  only reads as ↗↘ when both legs land between 40° and 60° *and* are drawn cleanly: draw a caret the way people
  actually draw one, at 65–80°, and both legs fall into the ↑ and ↓ sectors — it reads ↑↓ every time, and a little
  curvature adds one or two more directions at the apex. A diagonal sector is only 34° wide to begin with; across two
  legs plus an apex, the usable window is almost nothing. ↑↓ is solid from 80° to 90° at any curvature — **recognise
  the shape the hand is already drawing rather than forcing the hand to 45°**. This is what comparable tools do:
  their default sets are all axial combinations (WGestures ships four directions, with eight as an option).

  Two-stroke gestures have one built-in quirk: too short a second leg reads as the first stroke alone. A dropped
  second leg on ↑↓ runs Copy, which is harmless — that's why it isn't ↓↑, where the same slip would run Paste and
  change your content.

  A single diagonal (the four window ones) is far more reliable than a diagonal pair, but still wants to be drawn
  **at the corner of the screen**: drawn sloppily, the usable window is roughly 40°–50°, beyond which it lands on the
  neighbouring axial direction.
- **Master switch: Use mouse gestures** — on the **Settings** tab, and in the gesture manager's title row. Turn it off and the right button is not
  watched at all — one switch instead of unticking every gesture and ticking them all back afterwards. Right-drag
  works without it (just pause first), so reach for this when you want the button left strictly alone: a program that
  does its own thing with a held right button, or another gesture tool doing the drawing. It takes effect
  immediately, no restart.
- **With no gestures the right button is untouched.** The global mouse hook is only installed while the master switch
  is on *and* at least one enabled gesture exists.
- **Draw it twice and it runs twice.** The Run buttons carry a double-fire guard (the toast takes a second to appear, so an impatient double-click would run the step twice); **gestures skip that guard** — a gesture takes a second of deliberate drawing, so drawing it twice is deliberate, and toggling on-top with ↖ (pin, then release) is exactly that. The guard is silent when it bites, which on a gesture would read as "I drew it twice and the second one vanished".
- **A successful gesture is silent; only failures report.** Eight of the ten shipped gestures have a visible effect (text appears, the window minimises, the browser opens) — a card saying so would repeat something you just watched happen, dozens of times a day. But **failures must speak**: a failed gesture is otherwise completely silent (a window held open by an unsaved-changes dialog, a search with nothing actually selected), leaving only "I drew it and nothing happened". So the three outcomes stay distinguishable: **not bound** — the pill at the end of your stroke shows what you drew; **ran fine** — nothing, the action itself is the answer; **failed** — a card naming the step and why.
  (The **▶ Run** buttons in the editors keep their receipt: there you are testing a step, usually with nothing visible on screen, and that card is the only result.)
- **Drew something that matches nothing: nothing happens — but it tells you what you drew, where you drew it.** Replaying a right-click would pop a context menu at the end of your stroke, which is worse than no response; staying silent would fold "not bound", "drawn crookedly" and "the action failed" into one indistinguishable "nothing happened". So a small pill appears at the end of the stroke with what you actually drew (`↑↓`, say) and fades after 0.9s — **not a notification card**: a gesture is a flick, its feedback shouldn't outweigh it or land a screen's width away from your hand. **Release without drawing** and a real right-click is replayed — the context menu appears on button-up anyway, so you don't notice. **Hold still without drawing** and after 0.2 s the press itself is handed back, so the menu arrives while your finger is still down instead of waiting for you to let go.
- **When it's bound to an action, drawing it again stops it** — the same as triggering a group by hotkey, so there's one rule to learn.
- **You record it the way you use it.** The canvas in the manager runs the same quantiser as the live hook, so what you draw is what gets stored; the list thumbnails are the real strokes, because you recognise a gesture by its shape, not by reading its name.
- Two gestures can't share a stroke (it's refused on the spot, naming the one that has it) — otherwise which one runs would depend on list order.

> **Right-button drag-and-drop needs a short pause while gestures are listening** — press, hold still for about 0.2 s, then drag (a file in Explorer, the camera in a 3D app). Comparable tools work the same way, and it only applies once you have at least one gesture.

### Prefer another gesture tool? Point it at Clockwork

Clockwork's gestures aren't an all-or-nothing choice. **Actions can already be triggered from outside** (see
[Triggering a group from outside](#triggering-a-group-from-outside)), so a dedicated gesture tool can own the
*drawing* while the actions stay here:

1. **Turn off the master switch** on the gesture manager's title row — the right button goes back to normal at once
   and no hook is installed.
2. Draw the gestures you want in WGestures / StrokesPlus.net / Quicker.
3. Set that gesture's action to run:

   ```
   Clockwork.exe --run-group "Focus"
   ```

The request is handed to the resident tray instance, and the process you just started exits immediately — **no window
appears**.

Two things come with it: those tools have spent far longer than we have on stroke recognition, and **the right button
is never touched at all** — no hook, so right-drag needs no pause and nothing can go wrong with a program that has its
own ideas about a held right button. Most of them also offer per-application exclusion lists, whereas the trade-off
here is "simple, with the cost stated up front".

The other way round holds too: the built-in gestures are enough on their own — ten samples work out of the box, with
no gesture tool to install first.

## Settings

Three sections, ordered by when you'd reach for them.

**Startup**

- **Start at login** — the master switch of this section, so it comes first: ticking it registers a scheduled task with admin rights (so boot brings no UAC prompts), unticking removes it. If the change needs elevation, Clockwork relaunches itself to do it; if it fails, the box springs back rather than claiming a state that isn't real.
- **Startup delay** (0–600 s) — waits this long after login before running the list, so it misses the login storm. **Only applies when Start at login is on**; a manual *Re-run startup list* is unaffected.
- **Wait until the system is ready** (desktop / network) — goes as soon as both are ready, waits at most 90 s, then the fixed delay above is added on top. Better than simply raising the delay when the list runs too early for your apps.
- **Start minimized to tray** (opening manually goes straight to the tray).

**General** — app-wide things; the first two are both "how you summon it globally".

- **Panic hotkey** — click the box and press your shortcut; Esc cancels, Del clears; default `Ctrl+Alt+Q`.
- **Mouse gestures** — the way in is the **polyline icon at the right end of the main window's tab strip** (*Manage gestures…*), not a row on this page; the quick panel's title row has one too. With no enabled gesture the right button behaves exactly as usual.
- **UI language** — Simplified Chinese, English, 日本語 and 15 more (18 total); switching restarts the app to apply.
- **Theme** — Dark / Light / Follow system; switching restarts the app to apply. "Follow system" reads the **app** mode in Windows settings (not the system mode), matching File Explorer.
- **Export settings** — saves a copy of `clockwork.settings.json` wherever you choose (default name `clockwork.settings.backup.json`). Use it to back up before a big change, or to move your setup to another PC.
- **Import settings** — replaces **all** current config (startup list / scheduled tasks / actions / gestures / settings) with the chosen file. It confirms first, copies the current config to `clockwork.settings.json.bak` as an undo path, verifies the file parses before overwriting, then restarts the app so everything reloads. Task state (`clockwork.state.json`) is not touched.
- **A config file that can't be read is never overwritten.** Hand-edit the json and miss a comma, or lose power mid-save, and the app starts on defaults, tells you so, and saves your file aside as `clockwork.settings.json.bad`. The original is not clobbered by the defaults — fix it and restart to recover. Even if you miss the notice, rebuild everything by hand and save, that `.bad` copy is still there.

**Quick panel** — six settings, all of which affect only that one overlay; ordered "how you summon it, then how it looks". Two for summoning, four for appearance; details under **Quick panel** above.

- **Quick panel hotkey** — same box behaviour as the panic hotkey; default `Ctrl+Alt+Space`. Clearing it leaves the tray menu entry, so the panel is still reachable. If the combo is already taken by another program, registration fails with a card naming it — pick another; the panic hotkey is registered first and wins any clash.
- **Hold the middle mouse button to open the panel** — off by default, with the hold time next to it (350 ms, clamps to 150–2000). Details and the passthrough rules are under **Quick panel** above.
- **Layout / tile size / icons only / show built-in controls** — see **Tune it to taste** above.

## Tips

- Double-click `Clockwork.exe` only opens the settings window — it does **not** immediately run the startup list; use the tray's **Rerun startup list** for that.
- **The side buttons follow the selection** — with no row selected, **Edit / Delete / Up / Down / Run** are greyed out, since they only ever act on the selected row. **Add** always works. The right-click menu's **Duplicate** (plus **Skip today** on the Scheduled tasks page) behaves the same way: with nothing selected, or when you right-click the header or empty space, the menu simply does not open — a menu that acts somewhere else is worse than no menu.
- **Nothing is cut off silently** — in all four lists, a cell too wide for its column ends in "…"; hover it to read the whole thing.
- **Deleting always asks for confirmation** — list rows, steps inside the group editor, and system startup items alike. The dialog names what you're about to delete, so you can catch a wrong selection before it's gone.
- Your config is `clockwork.settings.json` (local only). Delete it and reopen to reset to the sample. Task state is `clockwork.state.json` (also local; safe to delete — at most a task fires once more today), and the panel's click tallies are `clockwork.usage.json` (likewise safe to delete — you lose the counts and nothing else). Prefer the Settings tab's **Export / Import settings** for backups and moving between PCs.
- **Where those files live:** on first run, next to `Clockwork.exe` when that folder is writable (the normal portable case); if it isn't — e.g. the exe sits under `C:\Program Files` — all of them go to `%APPDATA%\Clockwork\`. **After that the app follows wherever the config already is, rather than re-testing writability each launch** — otherwise a double-click (not elevated) and autostart (elevated) would pick different copies on the same machine, showing up as "my settings vanished after reopening as administrator" or "autostart runs a list I never configured". Export always copies whichever one is actually in use, so you never have to hunt for it.
- When filling paths / processes / dates you don't have to type by hand: **the … button at the end of a row** opens the matching picker (file, searchable process list, date), and **Capture** records a shortcut by pressing it. The process picker and the system-startup list both have a search/filter box.
- **Launch it normally** (double-click / tray / scheduled task). Some sandbox / reduced-privilege launchers (e.g. Lucy) block low-level calls, so send-keys / mouse / window actions / activate-if-running / send-text-to-process / volume may not work (you'll get a clear notice; plain "launch program" is unaffected).
- Global hotkeys can **run actions** (set per group, above). Arbitrary key remapping / text expansion is still out of scope — that's AutoHotkey's strength (an `.ahk` step needs AutoHotkey installed).
