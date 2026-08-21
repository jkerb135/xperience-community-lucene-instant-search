<#
.SYNOPSIS
    Builds samples/CustomWidget.Dropdown against packed packages. See pack-and-build.mjs, which
    holds the logic — this is the PowerShell entry point for it.
#>
$ErrorActionPreference = 'Stop'
& node (Join-Path $PSScriptRoot 'pack-and-build.mjs') @args
exit $LASTEXITCODE
