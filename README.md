# Win Service Buddy

Windows-native tool for managing **product-scoped** Windows Services and documented prerequisites.

- **Simple mode** — filter services by substring (e.g. `Milestone`)
- **Profile mode** — load a shareable `.wsb.json` profile (services, SCM deps, role-based prereqs like MSMQ)
- **CLI + GUI** — same core library
- **Bulk edits** — startup type and crash recovery presets
- **Target** — Windows Server 2012 R2+ / Windows 10/11 (self-contained publish recommended)

UI direction: dark Avalonia ops console (see `docs/mockups/`).

## Projects

| Project | Role |
|---------|------|
| `WinServiceBuddy.Core` | SCM, recovery P/Invoke, profiles, prerequisites |
| `WinServiceBuddy.Cli` | `wsbuddy` CLI (`System.CommandLine`) |
| `WinServiceBuddy.App` | Avalonia desktop GUI |
| `WinServiceBuddy.Core.Tests` | Unit tests |

## Build

```powershell
dotnet build WinServiceBuddy.sln
dotnet test WinServiceBuddy.sln
```

### CLI

```powershell
dotnet run --project src/WinServiceBuddy.Cli -- list --substring Spooler
dotnet run --project src/WinServiceBuddy.Cli -- list --profile profiles/examples/everbridge-control-center-sample.wsb.json --role Server
dotnet run --project src/WinServiceBuddy.Cli -- prereq check --profile profiles/examples/everbridge-control-center-sample.wsb.json --role Server
dotnet run --project src/WinServiceBuddy.Cli -- info
```

Mutating commands (`start`, `stop`, `restart`, `set-startup`, `set-recovery`) require an elevated prompt.

### GUI

```powershell
dotnet run --project src/WinServiceBuddy.App
```

If not elevated, use **Run elevated** in the status bar.

## Profiles

Examples live in `profiles/examples/`:

- `everbridge-control-center-sample.wsb.json` — includes MSMQ prereq for Server/Client
- `milestone-xprotect-sample.wsb.json` — substring match sample
- `substring-template.wsb.json` — blank template

Import:

```powershell
dotnet run --project src/WinServiceBuddy.Cli -- profile import profiles/examples/milestone-xprotect-sample.wsb.json
dotnet run --project src/WinServiceBuddy.Cli -- profile list
```

User profiles: `%LocalAppData%\WinServiceBuddy\Profiles`  
Machine profiles: `%ProgramData%\WinServiceBuddy\Profiles`

## Packaging (planned)

- Portable ZIP (self-contained `win-x64`)
- MSI
- Chocolatey (`wsbuddy`)

```powershell
dotnet publish src/WinServiceBuddy.Cli -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/cli
dotnet publish src/WinServiceBuddy.App -c Release -r win-x64 --self-contained true -o artifacts/app
```

## License

TBD.
