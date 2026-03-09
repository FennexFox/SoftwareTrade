[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$PdxEmail = $env:PDX_EMAIL,

    [string]$PdxPassword = $env:PDX_PASSWORD,

    [switch]$SkipParadoxPublish,

    [switch]$SkipGitTag,

    [switch]$SkipGitChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Command {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if (-not (Get-Command -Name $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Get-RequiredEnvironmentValue {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $item = Get-Item -Path "Env:$Name" -ErrorAction SilentlyContinue
    $value = if ($item) { [string]$item.Value } else { "" }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required environment variable: $Name"
    }

    return $value
}

function Assert-PathValue {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [ValidateSet("Container", "Leaf")]
        [string]$PathType
    )

    $value = Get-RequiredEnvironmentValue -Name $Name
    if (-not (Test-Path -LiteralPath $value -PathType $PathType)) {
        throw "$Name points to a missing path: $value"
    }

    return $value
}

function Convert-SecureStringToPlainText {
    param(
        [Parameter(Mandatory)]
        [Security.SecureString]$SecureString
    )

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "NoOfficeDemandFix/NoOfficeDemandFix.csproj"
$publishConfigurationPath = Join-Path $repoRoot "NoOfficeDemandFix/Properties/PublishConfiguration.xml"
$normalizedVersion = if ($Version.StartsWith("v")) { $Version.Substring(1) } else { $Version }
$tag = "v$normalizedVersion"
$modName = "NoOfficeDemandFix"

Push-Location $repoRoot
try {
    Assert-Command -Name "git"
    Assert-Command -Name "dotnet"

    $selectedSdkOutput = & dotnet --version
    $selectedSdk = ([string]($selectedSdkOutput | Select-Object -Last 1)).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($selectedSdk)) {
        throw "dotnet is not available on PATH."
    }

    if (-not $SkipGitChecks) {
        $gitStatus = (& git status --porcelain=v1) | Where-Object { $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to read git status."
        }

        if ($gitStatus) {
            throw "Working tree is not clean. Commit or stash changes before running a release."
        }
    }

    if (-not $SkipGitTag) {
        $localTag = [string]((& git tag --list $tag) | Select-Object -Last 1)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect local tags."
        }

        if ($localTag) {
            throw "Local tag $tag already exists."
        }

        $remoteTag = [string]((& git ls-remote --tags origin "refs/tags/$tag") | Select-Object -Last 1)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect remote tags."
        }

        if ($remoteTag) {
            throw "Remote tag $tag already exists."
        }
    }

    $toolPath = Get-RequiredEnvironmentValue -Name "CSII_TOOLPATH"
    $managedPath = Assert-PathValue -Name "CSII_MANAGEDPATH" -PathType "Container"
    $userDataPath = Assert-PathValue -Name "CSII_USERDATAPATH" -PathType "Container"
    $localModsPath = Assert-PathValue -Name "CSII_LOCALMODSPATH" -PathType "Container"
    $unityProjectPath = Assert-PathValue -Name "CSII_UNITYMODPROJECTPATH" -PathType "Container"
    $modPostProcessorPath = Assert-PathValue -Name "CSII_MODPOSTPROCESSORPATH" -PathType "Leaf"
    $modPublisherPath = Assert-PathValue -Name "CSII_MODPUBLISHERPATH" -PathType "Leaf"
    $mscorlibPath = Assert-PathValue -Name "CSII_MSCORLIBPATH" -PathType "Leaf"
    $entitiesVersion = Get-RequiredEnvironmentValue -Name "CSII_ENTITIESVERSION"

    $gameDll = Join-Path $managedPath "Game.dll"
    if (-not (Test-Path -LiteralPath $gameDll -PathType Leaf)) {
        throw "Game.dll was not found in CSII_MANAGEDPATH: $gameDll"
    }

    [xml]$publishConfiguration = Get-Content -LiteralPath $publishConfigurationPath
    $configuredVersion = $publishConfiguration.Publish.ModVersion.Value
    if ($configuredVersion -ne $normalizedVersion) {
        throw "PublishConfiguration.xml has ModVersion '$configuredVersion', but the requested release version is '$normalizedVersion'."
    }

    $changeLog = ([string]$publishConfiguration.Publish.ChangeLog).Trim()
    if ($changeLog -notmatch [regex]::Escape($normalizedVersion)) {
        Write-Warning "ChangeLog does not mention version $normalizedVersion."
    }

    if (-not $SkipParadoxPublish) {
        if ([string]::IsNullOrWhiteSpace($PdxEmail)) {
            $PdxEmail = Read-Host "Paradox email"
        }

        if ([string]::IsNullOrWhiteSpace($PdxPassword)) {
            $securePassword = Read-Host "Paradox password" -AsSecureString
            $PdxPassword = Convert-SecureStringToPlainText -SecureString $securePassword
        }
    }

    $branch = ([string]((& git branch --show-current) | Select-Object -Last 1)).Trim()
    $commit = ([string]((& git rev-parse --short HEAD) | Select-Object -Last 1)).Trim()
    Write-Host "Releasing $tag from $branch at $commit"
    Write-Host "Using .NET SDK $selectedSdk"
    Write-Host "CSII_TOOLPATH=$toolPath"
    Write-Host "CSII_USERDATAPATH=$userDataPath"
    Write-Host "CSII_UNITYMODPROJECTPATH=$unityProjectPath"
    Write-Host "CSII_MODPOSTPROCESSORPATH=$modPostProcessorPath"
    Write-Host "CSII_MSCORLIBPATH=$mscorlibPath"
    Write-Host "CSII_ENTITIESVERSION=$entitiesVersion"

    Write-Host "Restoring project..."
    & dotnet restore $projectPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE."
    }

    Write-Host "Building project..."
    & dotnet build $projectPath -c Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }

    $deployDir = Join-Path $localModsPath $modName
    if (-not (Test-Path -LiteralPath $deployDir -PathType Container)) {
        throw "Expected deploy directory was not created: $deployDir"
    }

    if (-not $SkipParadoxPublish) {
        $publishCommand = if ([string]::IsNullOrWhiteSpace($publishConfiguration.Publish.ModId.Value)) { "Publish" } else { "NewVersion" }

        Write-Host "Publishing to Paradox Mods with command $publishCommand..."
        & $modPublisherPath `
            $publishCommand `
            $publishConfigurationPath `
            --contentFolder $deployDir `
            --email $PdxEmail `
            --password $PdxPassword `
            --noAutoLogin `
            --verbose

        if ($LASTEXITCODE -ne 0) {
            throw "ModPublisher failed with exit code $LASTEXITCODE."
        }
    }
    else {
        Write-Host "Skipping Paradox publish."
    }

    if (-not $SkipGitTag) {
        Write-Host "Creating and pushing tag $tag..."
        & git tag -a $tag -m $tag
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create git tag $tag."
        }

        & git push origin $tag
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push git tag $tag."
        }

        Write-Host "Pushed $tag. GitHub Actions will create the GitHub release."
    }
    else {
        Write-Host "Skipping git tag creation and push."
    }

    Write-Host ""
    Write-Host "Release preparation complete."
}
finally {
    Pop-Location
}
