# Building Clockwork

C#/.NET WPF. You need the **.NET 10 SDK**; everything else comes from the repo.

```powershell
dotnet test app.Tests/Clockwork.Tests.csproj                                              # xunit.v3 on Microsoft.Testing.Platform
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64                  # self-contained, ~72 MB
dotnet publish app/Clockwork.csproj -c Release -p:PublishProfile=win-x64-needs-dotnet10   # needs .NET 10 Desktop Runtime, ~1.2 MB
```

The publish output is `app/bin/Release/publish/Clockwork.exe` (the needs-dotnet10 build lands in `publish-needs-dotnet10/`) — single-file either way, with everything shaped by the profiles under `app/Properties/PublishProfiles/`. The default carries the .NET runtime inside and is compressed; the `needs-dotnet10` profile drops both (compression only exists for self-contained bundles) and leaves an exe that needs the runtime installed.

Those settings live in the profiles rather than the csproj on purpose: on the csproj they would drag every `dotnet build` / `dotnet run` through the RID-specific single-file path, and a self-contained executable project cannot be referenced by `app.Tests` at all (NETSDK1151) now that the test project builds as an exe.

## Layout

| Folder | What lives there |
| --- | --- |
| `app/Core/` | Pure logic — no Win32, no UI. This is where the tests point |
| `app/Native/` | Win32 interop (hotkeys, window actions, volume, send-keys) |
| `app/Engine/` | Execution: startup list, action groups, reminder scheduling |
| `app/ViewModels/` + `app/Views/` | WPF UI |
| `app/I18n/` + `app/Resources/` | Localization. Neutral `Strings.resx` is the Chinese source; one `Strings.<code>.resx` satellite per language |
| `app.Tests/` | xunit.v3 on Microsoft.Testing.Platform, mirroring the `Core/` and `Engine/` layout |

Adding a UI string means adding the key to `Strings.resx` **and** to all 17 satellites — a missing key falls back to the neutral Chinese value, which is worse than an obviously untranslated English one.

## CI and releases

GitHub Actions builds and runs every test on a Windows runner for each push and PR. Pushing a `v*` tag (e.g. `v2.0.0`) builds, stamps the file version from the tag, creates a GitHub Release and attaches three assets: `Clockwork-<tag>-win-x64.zip` (self-contained) and `Clockwork-<tag>-win-x64-needs-dotnet10.zip` (framework-dependent), each holding one `Clockwork.exe`, plus that framework-dependent build attached raw under the plain name `Clockwork.exe` for one-click download-and-run. Release runs only after the test job passes (`needs:`), so a red tree can't produce one.

## Docs

User-facing behaviour is documented in [`docs/USAGE.md`](docs/USAGE.md) (and [`docs/USAGE.zh.md`](docs/USAGE.zh.md)); the READMEs are the short version, translated into 18 languages under [`docs/i18n/`](docs/i18n/). A change that alters what a user sees should land in the same commit as its doc update.
