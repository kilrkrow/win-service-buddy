# Chocolatey package: `wsbuddy`

Installs Win Service Buddy from the official GitHub Release ZIPs.

## Pack (local)

```powershell
cd pack/chocolatey/wsbuddy
choco pack
```

Produces `wsbuddy.0.2.0.nupkg`.

## Install from local nupkg

```powershell
choco install wsbuddy -y --source "'.;https://community.chocolatey.org/api/v2/'"
# or
choco install wsbuddy -y -s .
```

## Push to community feed (maintainers)

```powershell
choco push wsbuddy.0.2.0.nupkg --source https://push.chocolatey.org/ --api-key <YOUR_KEY>
```

Requires a [Chocolatey.org](https://community.chocolatey.org) account and package moderation for first publish.

## Bumping a version

1. Publish GitHub release `vX.Y.Z` with CLI/App ZIPs.  
2. Update `wsbuddy.nuspec` version.  
3. Update `$version` and SHA256 values in `tools/chocolateyinstall.ps1`.  
4. Update `tools/VERIFICATION.txt`.  
5. `choco pack` and push.
