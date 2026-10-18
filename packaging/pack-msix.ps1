<#
.SYNOPSIS
Packages the published win-x64 and win-arm64 builds as an unsigned .msixbundle with an application execution alias.

.DESCRIPTION
Expects the publish output in publish/win-x64-PublishArtifacts and publish/win-arm64-PublishArtifacts
under the repository root, and writes artifacts/msix/docker-credential-acr.msixbundle.

.PARAMETER Version
Four-part package version, e.g. 1.2.3.0.
#>
param(
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path $PSScriptRoot
$staging = Join-Path ([IO.Path]::GetTempPath()) "acr-msix-$([guid]::NewGuid().ToString('N'))"

try {
    $slices = foreach ($rid in 'win-x64', 'win-arm64') {
        $exe = Get-ChildItem "$root/publish/$rid-PublishArtifacts" -Filter docker-credential-acr.exe -Recurse | Select-Object -First 1
        if (-not $exe) { throw "docker-credential-acr.exe not found for $rid" }

        $slice = New-Item -ItemType Directory "$staging/$rid"
        # The publish output includes native dependencies beside the exe (e.g. msalruntime.dll for WAM); debug symbols are excluded.
        Copy-Item "$($exe.DirectoryName)/*" $slice -Exclude *.pdb
        Copy-Item "$PSScriptRoot/msix/Assets" $slice -Recurse
        (Get-Content "$PSScriptRoot/msix/Package.appxmanifest" -Raw) -replace '(<Identity[^>]*?Version=")[^"]*"', "`${1}$Version`"" |
            Set-Content "$slice/Package.appxmanifest" -Encoding utf8
        $slice.FullName
    }

    $outputDir = New-Item -ItemType Directory "$root/artifacts/msix" -Force
    winapp pack $slices --output "$outputDir/docker-credential-acr.msixbundle" --no-sign
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
