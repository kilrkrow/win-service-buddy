# Chocolatey package: `wsbuddy`

Installs Win Service Buddy from the official GitHub Release ZIPs. Published on the
community feed as [`wsbuddy`](https://community.chocolatey.org/packages/wsbuddy).

The files in `wsbuddy/` are a template. The version, the two SHA256 checksums and
`VERIFICATION.txt` are stamped at pack time from the release tag and the built ZIPs by
`build/choco-pack.ps1`; do not edit them by hand. The `.nupkg` is build output and is
gitignored; do not commit it.

## Release (normal path)

Push a `vX.Y.Z` tag. `.github/workflows/release.yml` builds both ZIPs, creates the GitHub
release with the ZIPs and their `.sha256` files, packs this package, install-tests it,
and pushes it to the community feed (only for a tag without `-`, in
`kilrkrow/win-service-buddy`, public, with the `CHOCOLATEY_API_KEY` secret set).

## Pack and test locally

```powershell
powershell -ExecutionPolicy Bypass -File build\release.ps1 -Version v0.2.1
# Package that downloads from the GitHub release (works once the release is published):
powershell -ExecutionPolicy Bypass -File build\choco-pack.ps1 -Version v0.2.1
# Test package that installs from the local release\ folder instead:
powershell -ExecutionPolicy Bypass -File build\choco-pack.ps1 -Version v0.2.1 -OutDir release\choco-test -BaseUrl "file:///$((Resolve-Path release).Path.Replace('\','/'))"
choco install wsbuddy -y -s release\choco-test   # elevated prompt
choco uninstall wsbuddy -y
```

## Bumping a version

1. Bump `<Version>` in `Directory.Build.props` at the repo root.
2. Merge, then tag and push: `git tag vX.Y.Z && git push origin vX.Y.Z`.
