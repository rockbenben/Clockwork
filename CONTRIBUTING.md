# Building Clockwork

C#/.NET WPF. You need the **.NET 10 SDK**; everything else comes from the repo.

```powershell
dotnet test app.Tests/Clockwork.Tests.csproj                                              # xunit.v3 on Microsoft.Testing.Platform
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64                  # self-contained
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64-needs-dotnet10   # needs .NET 10 Desktop Runtime
```

The publish output is `app/bin/Release/publish/Clockwork.exe` (the needs-dotnet10 build lands in `publish-needs-dotnet10/`) — single-file either way, with everything shaped by the profiles under `app/Properties/PublishProfiles/`. The default carries the .NET runtime inside and is compressed; the `needs-dotnet10` profile drops both (compression only exists for self-contained bundles) and leaves an exe that needs the runtime installed.

Those settings live in the profiles rather than the csproj on purpose: on the csproj they would drag every `dotnet build` / `dotnet run` through the RID-specific single-file path, and a self-contained executable project cannot be referenced by `app.Tests` at all (NETSDK1151) now that the test project builds as an exe.

## Self-checks

Two switches built into the exe (see `app/DevChecks.cs`). Both run before the single-instance check, so a Clockwork already sitting in your tray keeps working; both exit on their own and write a verdict marker to `%TEMP%\clockwork-smoke.txt` / `clockwork-shots.txt`:

```powershell
.\Clockwork.exe --smoke              # constructs and lays out every window, asserts each got a real size —
                                     # XAML is lazy-loaded, so a broken window throws nothing until opened
.\Clockwork.exe --shots shots-dir    # renders every tab of every window to PNG: 2 widths × 3 work-area
                                     # heights × 6 languages = 432 images. Every axis was added after a
                                     # real defect slipped past the one before it (see DevChecks.cs header)
```

Run `--smoke` before any PR that touches XAML (CI runs it on every push too). After layout changes, run `--shots` and eyeball the images. The languages are `zh-CN, en, de, es, ru, ar`, and the tight combinations are where things break: German and Russian run longest, Spanish beats German on some strings, and Arabic is the only RTL. Chinese at a comfortable size shows none of it.

The tallest tier does not give you a taller window: work-area height is the *available* height, and each window is still capped by its own `Height` (720 for the main window, 640 for the group editor). That tier tests "more room than the window needs", not a bigger window.

## Layout

| Folder | What lives there |
| --- | --- |
| `app/Core/` | Pure logic — no Win32, no UI. The cheapest thing to test, so put logic here when you can |
| `app/Native/` | Win32 interop (hotkeys, window actions, volume, send-keys, mouse injection; also listening-port enumeration and reading another process's command line / working directory) |
| `app/Engine/` | Execution: startup list, action groups, reminder scheduling, and the system-startup / listening-port readers |
| `app/ViewModels/` + `app/Views/` | WPF UI |
| `app/I18n/` + `app/Resources/` | Localization. Neutral `Strings.resx` is the Chinese source; one `Strings.<code>.resx` satellite per language |
| `app.Tests/` | xunit.v3 on Microsoft.Testing.Platform, mirroring the app layout: `Core/`, `Engine/`, `Native/`, `ViewModels/`, `Views/`, plus `I18n/` which enforces resx coverage across every language |

Adding a UI string means adding the key to `Strings.resx` **and** to all 17 satellites — a missing key falls back to the neutral Chinese value, which is worse than an obviously untranslated English one.

## CI and releases

GitHub Actions builds, runs every test and then `--smoke` on a Windows runner for each push and PR. Pushing a `v*` tag (e.g. `v2.0.0`) builds, stamps the file version from the tag, creates a GitHub Release and attaches four assets: `Clockwork-<tag>-win-x64.zip` (self-contained) and `Clockwork-<tag>-win-x64-needs-dotnet10.zip` (framework-dependent), each holding one `Clockwork.exe`; that framework-dependent build attached raw under the plain name `Clockwork.exe` for one-click download-and-run; and `SHA256SUMS.txt` covering all three. Every downloadable also gets a GitHub build-provenance attestation — `gh attestation verify <file> -R rockbenben/Clockwork` proves it was built by this repository's workflow, which is a stronger claim than a checksum that sits on the same release page as the files it checks. Release runs only after the test job passes (`needs:`), so a red tree can't produce one.

## Docs

User-facing behaviour is documented in [`docs/USAGE.md`](docs/USAGE.md) (and [`docs/USAGE.zh.md`](docs/USAGE.zh.md)); the READMEs are the short version, in 18 languages — English and Chinese at the repo root, the other 16 under [`docs/i18n/`](docs/i18n/). A change that alters what a user sees should land in the same commit as its doc update.
