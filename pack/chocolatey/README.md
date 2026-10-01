# Chocolatey package: `wsbuddy`

Installs Win Service Buddy from the official GitHub Release ZIPs.

## Pack (local)

```powershell
cd pack/chocolatey/wsbuddy
choco pack
```

Produces `wsbuddy.<version>.nupkg` (the version comes from `wsbuddy.nuspec`). The
`.nupkg` is build output and is gitignored — do not commit it.

## Install from local nupkg

```powershell
choco install wsbuddy -y --source "'.;https://community.chocolatey.org/api/v2/'"
# or
choco install wsbuddy -y -s .
```

## Push to community feed (maintainers)

```powershell
choco push wsbuddy.<version>.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
```

Requires a [Chocolatey.org](https://community.chocolatey.org) account and package moderation for first publish.

## Bumping a version

1. Bump `<Version>` in `Directory.Build.props` at the repo root.  
2. Run `scripts/publish.ps1` to build both ZIPs; it prints the SHA256 of each.  
3. Publish GitHub release `vX.Y.Z` with those App/CLI ZIPs attached.  
4. Update `wsbuddy.nuspec` (`version` and the `releaseNotes` URL).  
5. Update `$version` and both SHA256 values in `tools/chocolateyinstall.ps1`.  
6. Update `tools/VERIFICATION.txt`.  
7. `choco pack` and push.
