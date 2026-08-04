# Building Clockwork

C#/.NET WPF. You need the **.NET 10 SDK**; everything else comes from the repo.

```powershell
dotnet test app.Tests/Clockwork.Tests.csproj          # xUnit
dotnet publish app/Clockwork.csproj -c Release -r win-x64
```

The publish output is `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe` — single-file, self-contained and compressed, all set in the csproj.

## Layout

| Folder | What lives there |
| --- | --- |
| `app/Core/` | Pure logic — no Win32, no UI. This is where the tests point |
| `app/Native/` | Win32 interop (hotkeys, window actions, volume, send-keys) |
| `app/Engine/` | Execution: startup list, action groups, reminder scheduling |
| `app/ViewModels/` + `app/Views/` | WPF UI |
| `app/I18n/` + `app/Resources/` | Localization. Neutral `Strings.resx` is the Chinese source; one `Strings.<code>.resx` satellite per language |
| `app.Tests/` | xUnit, mirroring the `Core/` and `Engine/` layout |

Adding a UI string means adding the key to `Strings.resx` **and** to all 17 satellites — a missing key falls back to the neutral Chinese value, which is worse than an obviously untranslated English one.

## CI and releases

GitHub Actions builds and runs every test on a Windows runner for each push and PR. Pushing a `v*` tag (e.g. `v2.0.0`) builds, stamps the file version from the tag, creates a GitHub Release and attaches `Clockwork-<tag>.zip` containing `Clockwork.exe`.

## Docs

User-facing behaviour is documented in [`docs/USAGE.md`](docs/USAGE.md) (and [`docs/USAGE.zh.md`](docs/USAGE.zh.md)); the READMEs are the short version, translated into 18 languages under [`docs/i18n/`](docs/i18n/). A change that alters what a user sees should land in the same commit as its doc update.
