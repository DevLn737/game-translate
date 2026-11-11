Set-StrictMode -Version Latest

# Keep all host/package/process effects behind delegates. Tests supply the complete
# table and never construct or invoke the production operations.
function New-ScreenTranslateOperations {
    @{
        Publish = {
            param($Dotnet, $Project, $Output, $Root)
            Push-Location $Root
            try {
                & $Dotnet publish $Project -c Release -r win-x64 --self-contained true -o $Output --nologo | Out-Host
                if ($LASTEXITCODE -ne 0) { throw "Windows publication failed (exit $LASTEXITCODE)." }
            }
            finally { Pop-Location }
        }
        RunningProcesses = { @(Get-Process -Name ScreenTranslate -ErrorAction SilentlyContinue) }
        DeveloperMode = {
            (Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
                -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue) -eq 1
        }
        GetPackages = { param($Name) @(Get-AppxPackage -Name $Name -ErrorAction Stop) }
        Register = { param($Manifest) Add-AppxPackage -Register $Manifest -ErrorAction Stop }
        Launch = {
            param($Package)
            Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($Package.PackageFamilyName)!Translator" -WindowStyle Hidden
        }
    }
}

function Read-ScreenTranslateManifest {
    param([string] $Path)
    [xml] $manifest = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $identity = $manifest.Package.Identity
    if (-not $identity -or $identity.Name -ne 'DevLn737.ScreenTranslate' `
        -or $identity.Publisher -ne 'CN=LazyDictionaryDev' -or $identity.ProcessorArchitecture -ne 'x64') {
        throw 'The package identity must remain DevLn737.ScreenTranslate / CN=LazyDictionaryDev / x64.'
    }
    $versionText = [string] $identity.Version
    if ($versionText -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Manifest version must have four numeric components.' }
    $version = [version] $versionText
    foreach ($part in @($version.Major, $version.Minor, $version.Build, $version.Revision)) {
        if ($part -lt 0 -or $part -gt 65535) { throw 'Manifest version component is outside the Appx range.' }
    }
    [pscustomobject] @{ Name = [string] $identity.Name; Publisher = [string] $identity.Publisher; Version = $version; Xml = $manifest }
}

function New-ScreenTranslateAssets {
    param([string] $SourcePath, [string] $Layout)
    $assets = Join-Path $Layout 'Assets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    $source = [System.Drawing.Image]::FromFile($SourcePath)
    try {
        foreach ($asset in @(
            @{ Name = 'StoreLogo.png'; Size = 50 },
            @{ Name = 'Square44x44Logo.png'; Size = 44 },
            @{ Name = 'Square150x150Logo.png'; Size = 150 }
        )) {
            $bitmap = [System.Drawing.Bitmap]::new($asset.Size, $asset.Size)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.DrawImage($source, 0, 0, $asset.Size, $asset.Size)
                }
                finally { $graphics.Dispose() }
                $bitmap.Save((Join-Path $assets $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $bitmap.Dispose() }
        }
    }
    finally { $source.Dispose() }
}

function Assert-ScreenTranslateLayout {
    param([string] $Layout, $Manifest)
    $required = @('ScreenTranslate.exe', 'ScreenTranslate.dll', 'ScreenTranslate.deps.json',
        'ScreenTranslate.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll',
        'Licenses\GTranslate-LICENSE.txt', 'AppxManifest.xml')
    # Validate manifest references too, rather than assuming the generated logos
    # will always match whichever manifest was published.
    $references = @($Manifest.Xml.SelectNodes('//@Executable | //@Square44x44Logo | //@Square150x150Logo'))
    foreach ($reference in $references) { $required += [string] $reference.Value }
    $required += [string] $Manifest.Xml.Package.Properties.Logo
    $root = [IO.Path]::GetFullPath($Layout).TrimEnd('\') + '\'
    foreach ($relative in $required) {
        $path = [IO.Path]::GetFullPath((Join-Path $Layout $relative))
        if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Manifest asset is outside the layout: $relative"
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Incomplete publication: missing or empty $relative"
        }
    }
}

function Test-ScreenTranslateSamePath {
    param([string] $Left, [string] $Right)
    if (-not $Left -or -not $Right) { return $false }
    [string]::Equals([IO.Path]::GetFullPath($Left).TrimEnd('\'),
        [IO.Path]::GetFullPath($Right).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-ScreenTranslatePreparation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] [string] $RepositoryRoot,
        [Parameter(Mandatory = $true)] [string] $Dotnet,
        [switch] $BuildOnly,
        [switch] $NoLaunch,
        [hashtable] $Operations = (New-ScreenTranslateOperations)
    )
    $ErrorActionPreference = 'Stop'
    $projectDirectory = Join-Path $RepositoryRoot 'apps\windows\ScreenTranslate'
    $sourceManifest = Join-Path $projectDirectory 'Package.appxmanifest'
    $manifest = Read-ScreenTranslateManifest $sourceManifest
    $builds = Join-Path $RepositoryRoot 'artifacts\screen-translate-builds'
    $id = [guid]::NewGuid().ToString('N')
    $staging = Join-Path $builds ('.staging\' + $id)
    $layout = Join-Path $builds ($manifest.Version.ToString() + '-' + $id)
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        # Snapshot the manifest before publish; no later source edit can silently
        # change the version or identity of this particular build.
        $manifestPath = Join-Path $staging 'AppxManifest.xml'
        Copy-Item -LiteralPath $sourceManifest -Destination $manifestPath
        $snapshot = Read-ScreenTranslateManifest $manifestPath
        if ($snapshot.Version -ne $manifest.Version) { throw 'Manifest changed while preparing the build. Retry.' }
        $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        & $Operations.Publish $Dotnet (Join-Path $projectDirectory 'ScreenTranslate.csproj') $staging $RepositoryRoot | Out-Host
        if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $manifestHash) {
            throw 'Publication unexpectedly changed the staged manifest.'
        }
        New-ScreenTranslateAssets (Join-Path $projectDirectory 'Assets\app.png') $staging
        Assert-ScreenTranslateLayout $staging $snapshot
        # This unique final directory is never a publish destination and is never
        # overwritten or cleaned by subsequent runs (including failed runs).
        Move-Item -LiteralPath $staging -Destination $layout -ErrorAction Stop
    }
    finally {
        # Only this invocation's disposable staging may be removed.
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }

    Write-Host "Development layout: $layout"
    if ($BuildOnly) {
        Write-Host 'BuildOnly: no process checks, Developer Mode checks, registration or launch performed.'
        return $layout
    }

    if (@(& $Operations.RunningProcesses).Count -gt 0) {
        throw 'Exit ScreenTranslate using its tray menu before registration. The new layout was retained; the existing installation was not changed.'
    }
    if (-not (& $Operations.DeveloperMode)) { throw 'Windows Developer Mode is required for development package registration.' }
    $installed = @(& $Operations.GetPackages $manifest.Name)
    foreach ($package in $installed) {
        if ($package.Publisher -ne $manifest.Publisher) { throw 'Installed package publisher differs from the manifest identity.' }
        $installedVersion = [version] $package.Version
        if (-not (Test-ScreenTranslateSamePath $package.InstallLocation $layout) -and $manifest.Version -le $installedVersion) {
            throw "Relocating an installed package requires a higher manifest version (build $($manifest.Version), installed $installedVersion). Update the source manifest explicitly; no version was changed automatically."
        }
    }
    # Recheck after the package query, immediately before invoking registration.
    if (@(& $Operations.RunningProcesses).Count -gt 0) { throw 'ScreenTranslate started during preparation. Registration was not attempted.' }
    & $Operations.Register (Join-Path $layout 'AppxManifest.xml') | Out-Host
    $registered = @(& $Operations.GetPackages $manifest.Name)
    if ($registered.Count -ne 1 -or $registered[0].Publisher -ne $manifest.Publisher `
        -or [version] $registered[0].Version -ne $manifest.Version `
        -or -not (Test-ScreenTranslateSamePath $registered[0].InstallLocation $layout)) {
        throw 'Registration postcondition failed: package version/identity/InstallLocation does not match this layout. Windows registration is not rolled back automatically.'
    }
    if (-not $NoLaunch) { & $Operations.Launch $registered[0] | Out-Host }
    Write-Host 'Ready: Screen Translate. Home to select, End to repeat.'
    return $layout
}

Export-ModuleMember -Function Invoke-ScreenTranslatePreparation
