# AGENTS.md

This is the repository-level working agreement for AI coding agents. Read it before changing code. If your tool has its own instruction file, treat it as a pointer to this file, not as a second source of rules.

Clockwork is a **Windows-only WPF / .NET 10** tray application. It is distributed as a portable executable. There is no web stack and no plugin system. The user-visible docs are `README.md`, `README.zh.md`, `docs/USAGE.md` and `docs/USAGE.zh.md`; the developer docs are `CONTRIBUTING.md`.

## 1. Read before changing

- **`CONTRIBUTING.md`** — repository layout, localization rules, CI and release flow.
- **`docs/USAGE.md` / `docs/USAGE.zh.md`** — what each screen and field actually does.
- **`.github/workflows/ci.yml`** — the real build, test and release gates. Do not infer a release guarantee from a green local build.
- **The WPF-specific skill**, if available (`wpf-desktop`) — especially its pitfall and dev-switch references. Read the matching section before touching layout, DPI, activation, tray behavior or the test harness.
- **`git status` and `git log`** — the working tree may contain someone else's unfinished work. Never treat it as a clean release tree.

## 2. Build and verify

Run the narrowest useful check first, then the full gate:

```bash
dotnet build app/Clockwork.csproj -c Debug
dotnet test app.Tests/Clockwork.Tests.csproj -c Debug
```

The application has two in-process self-checks:

```bash
app/bin/Debug/net10.0-windows/Clockwork.exe --smoke
app/bin/Debug/net10.0-windows/Clockwork.exe --selftest
```

- `--smoke` constructs and lays out every XAML window. It must be run after XAML or window-option changes.
- `--selftest` drives the modal ask-dialog chain end to end.
- For UI changes, also run `--shots <directory>`. The full matrix is expensive; use `CW_SHOT_WINDOWS` and `CW_SHOT_LANGS` to render only the changed windows/languages first, then run the full matrix for broad layout changes.
- The screenshot harness is not a substitute for a screen-level check: it sets `MaxHeight` itself and renders the whole window offscreen. It can hide an off-screen action button, and it can also show a button that is outside the real monitor. For a scrollable dialog or pop-up, also verify the primary button's screen rectangle against the monitor work area, or take a full-screen capture.
- Do not run a harness that installs a real global mouse hook, or a full-screen topmost overlay, on a machine you are using. Read the WPF skill's warnings first and use the app's opt-in switches.

## 3. WPF and layout invariants

- The process is **PerMonitorV2** aware (`app.manifest`). Never assume one DPI for the whole desktop. Use `WindowSizing` / `VisualTreeHelper.GetDpi` for the window's actual monitor.
- Do not hardcode a window or content height that must serve both a 1366×768 laptop and a 4K display. Derive it from the work area of the monitor the window is on; keep a conservative XAML fallback for when monitor information is unavailable.
- Every `SizeToContent` window needs a runtime height cap **and** a XAML `MaxHeight` fallback. A window-level cap is not automatically a content cap: a `SizeToContent` window with a scrollable body can still lay out past the work area. Cap the body as well, and keep the action buttons outside the scroll area.
- Do not bypass the existing foreground / z-order guards. In particular, never use `SetWindowPos` on a `Topmost` window; read the WPF skill's pitfall reference before changing activation.
- Keep the application's single-instance, config-location and startup behavior intact. A test that needs a second instance must use an isolated copy or a dedicated test path; it must not disturb a running user instance.
- Put **first-run defaults** in `RootConfig.Default()` (or the new-item seed in the relevant editor), not in a model property that existing configs rely on. Old configs may omit a field; a missing field must keep its previous behavior. Test both a fresh config and a missing-field old config.
- The application is offline by design. The update check is the only network path and is opt-in. Do not add telemetry, cloud calls or silent background network behavior.

## 4. Localization and documentation

- The neutral `Strings.resx` is the Simplified Chinese source. Every UI string also exists in the 17 satellite resx files. Add a new key to **all 18** files; never leave a raw key or an empty translation.
- Respect the existing punctuation and number-format conventions. Parameter order and placeholders must match the neutral string.
- User-visible behavior changes land with their documentation in the same change. `README` is the short version; `docs/USAGE.*` is the detailed version.
- Code comments explain **why**, not what. Do not write drifting line numbers, test counts, release versions or “this round” notes into comments or docs. Historical references to removed code are allowed when the surrounding text says so.
- After renames or documentation edits, run:

```bash
python tools/audit_comment_refs.py
```

Read the hits; `[历史?]` entries and illustrative example paths are often intentional. Do not “fix” inflected UI labels, step-type names or ordinary prose just to satisfy the heuristic.

## 5. User data and safety

- User configuration and state live next to the portable exe or under `%APPDATA%\Clockwork\`. Tests must not edit the user's real settings, state, startup entries or running instance.
- Destructive actions (deleting entries, releasing a port, removing startup items) keep their existing confirmation and honest failure reporting. Do not silently swallow an error in a user-triggered flow.
- Do not add a confirmation dialog, extra step or notification unless it removes a demonstrated confusion or risk. Prefer the smallest fix in copy, defaults, validation or feedback.

## 6. CI and releases

- `git tag vX.Y.Z` is the version source. The release workflow injects it into the build; do not manually maintain a second version number.
- The release path is: test + self-checks → draft release → Linux verification of `SHA256SUMS.txt` with `sha256sum -c` → publish with `make_latest` → assert `releases/latest`. The checksum file must use LF line endings and every downloadable asset must be covered by it.
- **A release never waits for a human.** The `release` environment carries no protection rule on purpose — Required reviewers was considered and rejected, so publishing must not block on an approval. The environment exists so each release leaves a deployment record. The mechanical gate is instead that the released commit must be on `main`; this repository has no branch protection, so without that check any branch could push a `v*` tag. Check the environment's actual protection rules before claiming any gate is active.
- Do not add Dependabot. Dependency versions are checked manually before a release with `dotnet list package --outdated`, `dotnet list package --vulnerable --include-transitive`, and the Action major check.
- Do not push, create a tag, publish a release or edit published assets without explicit maintainer approval. A local commit is not a release. For a bad published release, forward-fix with a new version; do not overwrite or delete published assets.
- For release changes, re-run the version check and remember that a green workflow is not proof that users received the new build. Verify the latest release and its assets separately.

## 7. Working with the tree

- Keep the change minimal and match the surrounding style. Do not add abstractions, settings or mechanisms for hypothetical future needs.
- Separate commits by feature domain. Do not stage unrelated dirty files, generated screenshots, logs, local settings or the user's configuration.
- Do not rewrite historical planning/spec files under `docs/superpowers/` or `docs/research/`; they are records, not current product documentation.
- Do not commit build outputs, screenshots, temporary configs or files under `.qoder-credits/`.

## 8. Definition of done

Before reporting a change as complete:

1. Build and the relevant tests pass.
2. `--smoke` passes; `--selftest` passes when the modal chain is affected.
3. UI changes include the relevant `--shots` pass and a screen-level check where the harness has a blind spot.
4. Localization tests pass and new strings exist in all 18 resx files.
5. `python tools/audit_comment_refs.py` has been read; intentional historical/example hits are explained.
6. `git diff --check` passes.
7. The report states what was verified, what was not, and whether anything was pushed or released. Never claim a visual or release check that was not actually run.
