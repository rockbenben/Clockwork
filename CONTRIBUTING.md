# Building Clockwork

C#/.NET WPF. You need the **.NET 10 SDK**; everything else comes from the repo.

```powershell
dotnet test --project app.Tests/Clockwork.Tests.csproj                            # xunit.v3 on Microsoft.Testing.Platform
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64                  # self-contained
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64-needs-dotnet10   # needs .NET 10 Desktop Runtime
```

`dotnet test` runs in **Microsoft.Testing.Platform mode** — the repo's `global.json` selects it
(`"test": { "runner": "Microsoft.Testing.Platform" }`). That is the mode the .NET 10 SDK ships for
MTP test apps, and the only one left: running MTP under the old VSTest mode was removed in MTP 2.
In this mode the project goes in as `--project` (the positional form still works, but that is the
compatibility path), and MTP's own options follow `--` — `-- --minimum-expected-tests 1` is the
guard against a run that silently executes zero tests.

The publish output is `app/bin/Release/publish/Clockwork.exe` (the needs-dotnet10 build lands in `publish-needs-dotnet10/`) — single-file either way, with everything shaped by the profiles under `app/Properties/PublishProfiles/`. The default carries the .NET runtime inside and is compressed; the `needs-dotnet10` profile drops both (compression only exists for self-contained bundles) and leaves an exe that needs the runtime installed.

Those settings live in the profiles rather than the csproj on purpose: on the csproj they would drag every `dotnet build` / `dotnet run` through the RID-specific single-file path, and a self-contained executable project cannot be referenced by `app.Tests` at all (NETSDK1151) now that the test project builds as an exe.

## Self-checks

Five switches built into the exe (see `app/DevChecks.cs` and `app/DevSelfTest.cs`). They run before the single-instance check, so a Clockwork already sitting in your tray keeps working; each exits on its own and writes a verdict marker to `%TEMP%\clockwork-smoke.txt` / `clockwork-selftest.txt` / `clockwork-shots.txt` / `clockwork-hookprobe.txt`:

```powershell
.\Clockwork.exe --smoke              # constructs and lays out every window, asserts each got a real size —
                                     # XAML is lazy-loaded, so a broken window throws nothing until opened
.\Clockwork.exe --selftest           # drives the question dialogs (prompt / choice) end to end in a real
                                     # process — the checks that need a live window and therefore cannot
                                     # live in app.Tests. CI gates on this one too
.\Clockwork.exe --shots shots-dir    # renders every tab of every window to PNG: 3 widths × 5 work-area
                                     # heights × 6 languages, plus one light-theme pass — 7,080 images
                                     # as of this writing (the count moves with every window and tab added).
                                     # Every axis was added after a real defect slipped past the one
                                     # before it (see the DevChecks.cs header)
.\Clockwork.exe --screenshots out    # the three README screenshots (en / zh-CN / zh-TW) at a fixed size.
                                     # Separate from --shots because README wants three fixed languages
                                     # at one size, not the "find the clipping" matrix
.\Clockwork.exe --hookprobe          # installs a bare WH_MOUSE_LL *and* a real MouseHook side by side,
                                     # counts events for 12 s, exits. Answers the one question the app
                                     # itself cannot: is the low-level mouse hook getting input on this
                                     # machine at all, and does the right button reach it? Move the mouse
                                     # and right-click while it runs. Written after a long hunt where
                                     # "gestures don't work" turned out to be another program swallowing
                                     # WM_RBUTTONDOWN — invisible from inside the app, obvious here.
```

Run `--smoke` before any PR that touches XAML (CI runs it, and `--selftest`, on every push too). After layout changes, run `--shots` and eyeball the images. The languages are `zh-CN, en, de, es, ru, ar`, and the tight combinations are where things break: German and Russian run longest, Spanish beats German on some strings, and Arabic is the only RTL. Chinese at a comfortable size shows none of it.

### Running them on the machine you are using

Two of those touch the machine they run on, and the difference matters when that machine is the one under your hands:

- **`--smoke` / `--selftest` open real windows.** The gesture-trail entry is the full-screen topmost overlay, and it genuinely goes on screen. The harness now disables window ghosting for its own process — the same one-way switch the production path uses — so a hung harness can no longer leave a click-eating DWM ghost over your desktop. The overlay still flashes, so run it where that does not interrupt you.
- **`dotnet test` installs a real global `WH_MOUSE_LL` hook.** Global is the operative word: it is not scoped to your session, it lives in every desktop on the machine. The hooks the suite really installs carry no press gate, so they swallow no button and inject nothing while tearing down — a hook that did would take over the mouse of the machine you are sitting at. Windows also silently drops any low-level hook whose callback overruns `LowLevelHooksTimeout` (300 ms by default), so the hook can vanish mid-run on a busy box.

Both are fine in CI. On a workstation, run them when you are not driving the mouse.

The tallest tier does not give you a taller window: work-area height is the *available* height, and each window is still capped by its own `Height` (720 for the main window, 640 for the group editor). That tier tests "more room than the window needs", not a bigger window.

## Layout

| Folder | What lives there |
| --- | --- |
| `app/Core/` | Pure logic — no Win32, no UI. The cheapest thing to test, so put logic here when you can |
| `app/Native/` | Win32 interop (hotkeys, window actions, volume, send-keys, mouse injection; also listening-port enumeration and reading another process's command line / working directory) |
| `app/Engine/` | Execution: startup list, action groups, system commands, and the system-startup / listening-port readers |
| `app/Services/` | App-domain collaborators, one per responsibility lifted out of the `App` god-class (diagnostics sink, hotkeys, gestures & mouse hook, quick panel, reminder scheduling, run orchestration, click statistics). They never reference `App`; cross-domain edges arrive as callbacks/delegates App wires at composition, and the App shell keeps same-name facades for Views and TrayIcon |
| `app/ViewModels/` + `app/Views/` | WPF UI |
| `app/I18n/` + `app/Resources/` | Localization. Neutral `Strings.resx` is the Chinese source; one `Strings.<code>.resx` satellite per language |
| `app.Tests/` | xunit.v3 on Microsoft.Testing.Platform, mirroring the app layout: `Core/`, `Engine/`, `Native/`, `ViewModels/`, `Views/`, plus `I18n/` which enforces resx coverage across every language |

Adding a UI string means adding the key to `Strings.resx` **and** to all 17 satellites — a missing key falls back to the neutral Chinese value, which is worse than an obviously untranslated English one.

## CI and releases

GitHub Actions builds, runs every test and then both `--smoke` and `--selftest` on a Windows runner for each push and PR. Pushing a `v*` tag (e.g. `v2.0.0`) builds, stamps the file version from the tag, creates a GitHub Release and attaches four assets: `Clockwork-<tag>-win-x64.zip` (self-contained) and `Clockwork-<tag>-win-x64-needs-dotnet10.zip` (framework-dependent), each holding one `Clockwork.exe`; that framework-dependent build attached raw under the plain name `Clockwork.exe` for one-click download-and-run; and `SHA256SUMS.txt` covering all three. Every downloadable also gets a GitHub build-provenance attestation — `gh attestation verify <file> -R rockbenben/Clockwork` proves it was built by this repository's workflow, which is a stronger claim than a checksum that sits on the same release page as the files it checks. Release runs only after the test job passes (`needs:`), so a red tree can't produce one.

**The release is published in two steps: assets go to a draft, and a separate job on Linux checks them before the draft goes public.** The check is `sha256sum -c SHA256SUMS.txt` against the files downloaded back from the draft — the same command a user runs on Linux or macOS, and the only place a CRLF in that file shows up (GNU coreutils reads the trailing `\r` as part of the file name, so every line fails with "No such file or directory"). The application itself is unaffected by such a file — it trims each line — which is exactly why the defect can ship unnoticed; v3.2.1 shipped one. A broken checksum file therefore stays in the draft instead of reaching users.

**The publish job is gated by the protected `release` environment.** Create it under Settings → Environments with Required reviewers before the first tag; without a protection rule GitHub creates an unprotected environment and the job can publish without review.

Dependency versions are checked before a release (`dotnet list package --outdated`, `dotnet list package --vulnerable --include-transitive`, and the Action major check); this repository intentionally does not use Dependabot.

## Docs

User-facing behaviour is documented in [`docs/USAGE.md`](docs/USAGE.md) (and [`docs/USAGE.zh.md`](docs/USAGE.zh.md)); the READMEs are the short version, in 18 languages — English and Chinese at the repo root, the other 16 under [`docs/i18n/`](docs/i18n/). A change that alters what a user sees should land in the same commit as its doc update.

## Tools

Two scripts under `tools/`, both plain Python with no dependencies:

- `make_icon.py` — the only generator for `assets/logo.svg`, `logo.ico`, `logo-256.png` and the social card. Edit the mark there, never in the outputs; the four copies of the path are byte-identical because they all come from this file.
- `audit_comment_refs.py` — checks that every `Type.Member`, resx key and file path named in a comment or doc still exists, that the 18 READMEs are structurally in sync, and that UI labels quoted in the docs match the resx of that language. Run it after a rename or a doc pass and read the output: it is deliberately a tool and not a test, because this repo quotes removed code in comments on purpose and a name-must-exist assertion would fight that forever. Hits tagged `[历史?]` are that class.
