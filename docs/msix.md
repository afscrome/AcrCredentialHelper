# MSIX packaging

On Windows the helper is also packaged as an MSIX. The package declares an app execution alias, so installing it puts
`docker-credential-acr.exe` on `PATH` without editing environment variables. Docker finds the helper by that name.

## How it works

- `packaging/msix/Package.appxmanifest` is the package manifest. Its `windows.appExecutionAlias` extension registers
  the `docker-credential-acr.exe` alias. Windows creates a small stub for it in
  `%LOCALAPPDATA%\Microsoft\WindowsApps`, which is already on `PATH`, that launches the packaged exe.
- The app runs full trust (`Windows.FullTrustApplication` plus the `runFullTrust` capability). It is a normal Win32
  process, not an AppContainer app, so it can use the Entra broker (WAM) and read and write the Docker config.
- `AppListEntry="none"` keeps it out of the Start menu; it is a command-line tool.
- `packaging/pack-msix.ps1` builds one `.msixbundle` from the `win-x64` and `win-arm64` publish output. The `msix` job
  in `.github/workflows/build.yml` runs it after the Windows builds and uploads the `MsixBundle` artifact.
- Each slice contains the exe and `msalruntime.dll` (needed for WAM sign-in), but not the `.pdb`.

> [!IMPORTANT]
> Packages with an execution alias cannot be installed unsigned. The bundle CI produces is unsigned, so it is not
> installable as it stands; it must be signed first. For local testing, sign with a development certificate as below.
> The manifest `Publisher` (`CN=AlexCrome`) must match the certificate subject.

## Prerequisites

Install the [winapp CLI](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/guides/packaging-cli):

```powershell
winget install microsoft.winappcli --source winget
```

Restart the shell so `winapp` is on `PATH`.

## Build and install a local package

Run these from the repository root. A local build targets your own architecture only, so use `win-arm64` on Arm.

1. Publish the app:

   ```powershell
   dotnet publish app -c Release -r win-x64
   ```

2. Stage the package contents: the exe, `msalruntime.dll`, the assets and the manifest.

   ```powershell
   $publish = Get-ChildItem artifacts/publish -Recurse -Filter docker-credential-acr.exe |
       Where-Object FullName -match 'win-x64' | Select-Object -First 1
   $stage = "$env:TEMP/acr-msix-stage"
   New-Item -ItemType Directory $stage -Force | Out-Null
   Copy-Item "$($publish.DirectoryName)/*" $stage -Exclude *.pdb
   Copy-Item packaging/msix/Assets $stage -Recurse -Force
   Copy-Item packaging/msix/Package.appxmanifest $stage
   ```

3. Create a development certificate. Keep it outside the repository.

   ```powershell
   $out = "$env:TEMP/acr-msix"
   New-Item -ItemType Directory $out -Force | Out-Null
   Push-Location $out
   winapp cert generate --publisher "CN=AlexCrome"
   Pop-Location
   ```

   This writes `devcert.pfx`, protected by the default password `password`. Treat it as development-only.

4. Pack and sign:

   ```powershell
   winapp pack $stage --output "$out/docker-credential-acr.msix" --cert "$out/devcert.pfx"
   ```

5. Trust the certificate. This needs an **Administrator** PowerShell and adds the certificate to the machine's trusted
   store, so only do it on a development machine:

   ```powershell
   winapp cert install "$env:TEMP/acr-msix/devcert.pfx"
   ```

6. Install the package from a normal shell:

   ```powershell
   Add-AppxPackage "$env:TEMP/acr-msix/docker-credential-acr.msix"
   ```

7. Check the alias:

   ```powershell
   (Get-Command docker-credential-acr).Source   # ...\Microsoft\WindowsApps\docker-credential-acr.exe
   docker-credential-acr --help
   ```

If another `docker-credential-acr` appears earlier on `PATH` it wins over the alias; `Get-Command` shows which one runs.

## Clean up

Remove the package; Windows removes the alias with it:

```powershell
Get-AppxPackage AlexCrome.AcrCredentialHelper | Remove-AppxPackage
```

Then remove the development certificate from the Local Machine **Trusted People** store (Administrator PowerShell):

```powershell
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=AlexCrome' | Remove-Item
```

Finally delete `$env:TEMP/acr-msix` and `$env:TEMP/acr-msix-stage`.

To switch an alias off without uninstalling, use **Settings > Apps > Advanced app settings > App execution aliases**.

## Troubleshooting

- **`0x80073D2C` / `0x80073D2B` on install:** the package is unsigned. Sign it as above.
- **A certificate trust error on install:** the development certificate is not trusted; run step 5.
- **`DllNotFound ... msalruntime`:** `msalruntime.dll` is missing from the package. Copy the whole publish folder, minus
  `.pdb` files, as in step 2.
- **Stack traces show `+0x…` offsets:** the `.pdb` is deliberately left out of the package. Keep the publish output to
  resolve them.
