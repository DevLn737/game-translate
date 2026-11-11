# Dependency-free regression tests. Only temporary fixtures and injected fake
# operations are used; no app, package, registry, process or dotnet operations.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot '..\..\scripts\ScreenTranslate.Build.psm1') -Force
Add-Type -AssemblyName System.Drawing

function Assert-True($Condition, [string] $Message) {
    if (-not $Condition) { throw "Assertion failed: $Message" }
}
function Assert-Throws([scriptblock] $Action, [string] $Pattern) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_ }
    Assert-True ($null -ne $caught) "Expected error matching $Pattern"
    Assert-True ($caught.Exception.Message -like $Pattern) "Unexpected error: $($caught.Exception.Message)"
}
function New-Fixture {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('ScreenTranslate-script-test-' + [guid]::NewGuid().ToString('N'))
    $project = Join-Path $root 'apps\windows\ScreenTranslate'
    New-Item -ItemType Directory -Path (Join-Path $project 'Assets') -Force | Out-Null
    $manifest = Join-Path $project 'Package.appxmanifest'
    @'
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
<Identity Name="DevLn737.ScreenTranslate" Publisher="CN=LazyDictionaryDev" Version="0.1.0.10" ProcessorArchitecture="x64" />
<Properties><Logo>Assets\StoreLogo.png</Logo></Properties>
<Applications><Application Id="Translator" Executable="ScreenTranslate.exe"><uap:VisualElements Square44x44Logo="Assets\Square44x44Logo.png" Square150x150Logo="Assets\Square150x150Logo.png" /></Application></Applications>
</Package>
'@ | Set-Content -LiteralPath $manifest -Encoding UTF8
    $bitmap = [System.Drawing.Bitmap]::new(8, 8)
    try { $bitmap.Save((Join-Path $project 'Assets\app.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
    $old = Join-Path $root 'artifacts\screen-translate'
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    'old installed bytes' | Set-Content -LiteralPath (Join-Path $old 'ScreenTranslate.exe')
    $state = [pscustomobject] @{
        Calls = [Collections.Generic.List[string]]::new(); Outputs = [Collections.Generic.List[string]]::new()
        PublishFails = $false; Omit = ''; DeveloperMode = $true; Running = $false; StartDuringQuery = $false
        Packages = @(); RegisterFails = $false; WrongLocation = $false; SourceManifest = $manifest
        MutateStagedManifest = $false; PostRegistrationResult = ''
    }
    $ops = @{
        Publish = {
            param($Dotnet, $Project, $Output, $Root)
            $state.Calls.Add('Publish'); $state.Outputs.Add($Output)
            Assert-True ($Dotnet -eq 'fake-dotnet') 'Only the fake publisher may run'
            foreach ($name in @('ScreenTranslate.exe', 'ScreenTranslate.dll', 'ScreenTranslate.deps.json',
                'ScreenTranslate.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'Licenses\GTranslate-LICENSE.txt')) {
                if ($name -eq $state.Omit) { continue }
                $path = Join-Path $Output $name
                New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
                'fake publication bytes' | Set-Content -LiteralPath $path
            }
            if ($state.PublishFails) { throw 'Fake publish failure' }
            if ($state.MutateStagedManifest) {
                $stagedManifest = Join-Path $Output 'AppxManifest.xml'
                (Get-Content -LiteralPath $stagedManifest -Raw).Replace('0.1.0.10', '0.1.0.11') |
                    Set-Content -LiteralPath $stagedManifest -Encoding UTF8
            }
        }.GetNewClosure()
        RunningProcesses = { $state.Calls.Add('Processes'); if ($state.Running) { [pscustomobject] @{ Path = $null } } }.GetNewClosure()
        DeveloperMode = { $state.Calls.Add('DeveloperMode'); $state.DeveloperMode }.GetNewClosure()
        GetPackages = {
            param($Name)
            $state.Calls.Add('Packages')
            Assert-True ($Name -eq 'DevLn737.ScreenTranslate') 'Package name was preserved'
            if ($state.StartDuringQuery) { $state.Running = $true }
            if ($state.Calls.Contains('Register')) {
                switch ($state.PostRegistrationResult) {
                    'wrongVersion' {
                        $package = $state.Packages[0].PSObject.Copy()
                        $package.Version = [version] '0.1.0.9'
                        return $package
                    }
                    'wrongPublisher' {
                        $package = $state.Packages[0].PSObject.Copy()
                        $package.Publisher = 'CN=Other'
                        return $package
                    }
                    'empty' { return }
                    'multiple' { return @($state.Packages[0], $state.Packages[0]) }
                }
            }
            $state.Packages
        }.GetNewClosure()
        Register = {
            param($Manifest)
            $state.Calls.Add('Register')
            if ($state.RegisterFails) { throw 'Fake registration failure' }
            [xml] $xml = Get-Content -LiteralPath $Manifest -Raw
            $location = Split-Path -Parent $Manifest
            if ($state.WrongLocation) { $location = 'C:\wrong-layout' }
            $state.Packages = @([pscustomobject] @{
                Publisher = [string] $xml.Package.Identity.Publisher; Version = [version] $xml.Package.Identity.Version
                InstallLocation = $location; PackageFamilyName = 'fake-family'
            })
        }.GetNewClosure()
        Launch = { param($Package) $state.Calls.Add('Launch') }.GetNewClosure()
    }
    [pscustomobject] @{ Root = $root; Project = $project; Old = $old; State = $state; Operations = $ops }
}
function Invoke-Build($Fixture, [switch] $Register, [switch] $NoLaunch) {
    Invoke-ScreenTranslatePreparation -RepositoryRoot $Fixture.Root -Dotnet 'fake-dotnet' `
        -BuildOnly:(-not $Register) -NoLaunch:$NoLaunch -Operations $Fixture.Operations
}
function Set-Installed($Fixture, [string] $Version) {
    $Fixture.State.Packages = @([pscustomobject] @{
        Publisher = 'CN=LazyDictionaryDev'; Version = [version] $Version
        InstallLocation = $Fixture.Old; PackageFamilyName = 'old-family'
    })
}
function Assert-NoRegistration($Fixture) {
    Assert-True (-not $Fixture.State.Calls.Contains('Register')) 'Registration must not be called'
    Assert-True (-not $Fixture.State.Calls.Contains('Launch')) 'Launch must not be called'
}
$script:passed = 0
function Test-Case([string] $Name, [scriptblock] $Body) {
    $f = New-Fixture
    $oldHash = (Get-FileHash -LiteralPath (Join-Path $f.Old 'ScreenTranslate.exe')).Hash
    try {
        & $Body $f
        Assert-True ((Get-FileHash -LiteralPath (Join-Path $f.Old 'ScreenTranslate.exe')).Hash -eq $oldHash) 'Old layout unchanged'
        Assert-True (@(Get-ChildItem -LiteralPath $f.Old -Recurse -File).Count -eq 1) 'Old layout has no additional files'
        $stage = Join-Path $f.Root 'artifacts\screen-translate-builds\.staging'
        if (Test-Path -LiteralPath $stage) { Assert-True (@(Get-ChildItem -LiteralPath $stage).Count -eq 0) 'No leftover staging' }
        $script:passed++
        Write-Host "PASS $Name"
    }
    finally { Remove-Item -LiteralPath $f.Root -Recurse -Force }
}

Test-Case 'BuildOnly ignores running process, packages and Developer Mode' {
    param($f)
    $f.State.Running = $true; $f.State.DeveloperMode = $false
    Set-Installed $f '99.0.0.0'
    $beforeManifest = (Get-FileHash -LiteralPath $f.State.SourceManifest).Hash
    $layout = Invoke-Build $f
    Assert-True (($f.State.Calls -join ',') -eq 'Publish') 'BuildOnly invoked only Publish'
    Assert-True ($layout -like '*\screen-translate-builds\0.1.0.10-*') 'Versioned final layout returned'
    Assert-True ($f.State.Outputs[0] -ne $layout) 'Publish uses staging, not final directory'
    Assert-True ((Get-FileHash -LiteralPath $f.State.SourceManifest).Hash -eq $beforeManifest) 'No automatic version bump'
    foreach ($asset in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
        $image = [System.Drawing.Image]::FromFile((Join-Path $layout ('Assets\' + $asset[0])))
        try { Assert-True ($image.Width -eq $asset[1] -and $image.Height -eq $asset[1]) 'Correct generated asset dimensions' }
        finally { $image.Dispose() }
    }
}
Test-Case 'Same-version builds are distinct and earlier layouts stay unchanged' {
    param($f)
    $first = Invoke-Build $f
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $first -Recurse -File) { $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    $second = Invoke-Build $f
    Assert-True ($first -ne $second) 'Unique final layout for repeated same-version build'
    Assert-True ($f.State.Outputs[0] -ne $f.State.Outputs[1]) 'Unique staging directories'
    $f.State.PublishFails = $true
    Assert-Throws { Invoke-Build $f } '*Fake publish failure*'
    foreach ($path in $hashes.Keys) { Assert-True ((Get-FileHash -LiteralPath $path).Hash -eq $hashes[$path]) 'Earlier layout bytes unchanged' }
    Assert-True (($f.State.Calls -join ',') -eq 'Publish,Publish,Publish') 'Builds never query host state'
}
Test-Case 'Partial failed publish never registers or finalizes' {
    param($f)
    Set-Installed $f '0.1.0.9'; $f.State.PublishFails = $true
    Assert-Throws { Invoke-Build $f -Register } '*Fake publish failure*'
    Assert-True (($f.State.Calls -join ',') -eq 'Publish') 'No OS operations after publish failure'
    Assert-True ($f.State.Packages[0].InstallLocation -eq $f.Old) 'Old registration unchanged'
    Assert-True (@(Get-ChildItem (Join-Path $f.Root 'artifacts\screen-translate-builds') -Directory | Where-Object Name -ne '.staging').Count -eq 0) 'No final layout for failed publish'
}
foreach ($required in @('ScreenTranslate.exe', 'ScreenTranslate.dll', 'ScreenTranslate.deps.json', 'ScreenTranslate.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'Licenses\GTranslate-LICENSE.txt')) {
    Test-Case "Missing $required refuses finalization" {
        param($f)
        $f.State.Omit = $required
        Assert-Throws { Invoke-Build $f -Register } '*Incomplete publication*'
        Assert-NoRegistration $f
    }
}
Test-Case 'Invalid source image fails without registration' {
    param($f)
    'not an image' | Set-Content -LiteralPath (Join-Path $f.Project 'Assets\app.png')
    Assert-Throws { Invoke-Build $f -Register } '*'
    Assert-NoRegistration $f
}
Test-Case 'Missing manifest logo fails completeness check' {
    param($f)
    (Get-Content -LiteralPath $f.State.SourceManifest -Raw).Replace('StoreLogo.png', 'Absent.png') | Set-Content -LiteralPath $f.State.SourceManifest -Encoding UTF8
    Assert-Throws { Invoke-Build $f -Register } '*Incomplete publication*Absent.png*'
    Assert-NoRegistration $f
}
Test-Case 'Manifest traversal cannot reference files outside layout' {
    param($f)
    (Get-Content -LiteralPath $f.State.SourceManifest -Raw).Replace('Assets\StoreLogo.png', '..\outside.png') | Set-Content -LiteralPath $f.State.SourceManifest -Encoding UTF8
    Assert-Throws { Invoke-Build $f } '*outside the layout*'
}
Test-Case 'Identity change is rejected before publish' {
    param($f)
    (Get-Content -LiteralPath $f.State.SourceManifest -Raw).Replace('DevLn737.ScreenTranslate', 'Other.App') | Set-Content -LiteralPath $f.State.SourceManifest -Encoding UTF8
    Assert-Throws { Invoke-Build $f } '*identity must remain*'
    Assert-True ($f.State.Calls.Count -eq 0) 'No effects for changed identity'
}
foreach ($version in @('0.1.0.10', '0.1.0.11', '1.0.0.0')) {
    Test-Case "Registration refuses equal or older build against $version" {
        param($f)
        Set-Installed $f $version
        Assert-Throws { Invoke-Build $f -Register } '*higher manifest version*'
        Assert-NoRegistration $f
        Assert-True ($f.State.Packages[0].InstallLocation -eq $f.Old) 'Old registration unchanged'
    }
}
Test-Case 'Higher numeric version registers, verifies location, then launches' {
    param($f)
    Set-Installed $f '0.1.0.9'
    $layout = Invoke-Build $f -Register
    Assert-True ($f.State.Packages[0].InstallLocation -eq $layout) 'Final layout registered'
    Assert-True (($f.State.Calls -join ',') -eq 'Publish,Processes,DeveloperMode,Packages,Processes,Register,Packages,Launch') 'Expected ordered external operations'
}
Test-Case 'Fresh registration supports NoLaunch' {
    param($f)
    $null = Invoke-Build $f -Register -NoLaunch
    Assert-True ($f.State.Calls.Contains('Register')) 'Registration called'
    Assert-True (-not $f.State.Calls.Contains('Launch')) 'NoLaunch respected'
}
Test-Case 'Running app blocks registration without killing it' {
    param($f)
    $f.State.Running = $true
    Assert-Throws { Invoke-Build $f -Register } '*Exit ScreenTranslate*'
    Assert-NoRegistration $f
    Assert-True (($f.State.Calls -join ',') -eq 'Publish,Processes') 'No package/registry access when app is running'
}
Test-Case 'App starting during preflight blocks registration' {
    param($f)
    $f.State.StartDuringQuery = $true
    Assert-Throws { Invoke-Build $f -Register } '*started during preparation*'
    Assert-NoRegistration $f
}
Test-Case 'Registration requires Developer Mode' {
    param($f)
    $f.State.DeveloperMode = $false
    Assert-Throws { Invoke-Build $f -Register } '*Developer Mode*'
    Assert-NoRegistration $f
}
Test-Case 'Wrong installed publisher is refused' {
    param($f)
    Set-Installed $f '0.1.0.9'; $f.State.Packages[0].Publisher = 'CN=Other'
    Assert-Throws { Invoke-Build $f -Register } '*publisher differs*'
    Assert-NoRegistration $f
}
Test-Case 'Registration failure does not launch or delete final build' {
    param($f)
    $f.State.RegisterFails = $true
    Assert-Throws { Invoke-Build $f -Register } '*Fake registration failure*'
    Assert-True (-not $f.State.Calls.Contains('Launch')) 'No launch after registration failure'
    Assert-True (@(Get-ChildItem (Join-Path $f.Root 'artifacts\screen-translate-builds') -Directory | Where-Object Name -ne '.staging').Count -eq 1) 'Validated layout retained'
}
Test-Case 'Registration InstallLocation postcondition blocks launch' {
    param($f)
    $f.State.WrongLocation = $true
    Assert-Throws { Invoke-Build $f -Register } '*postcondition failed*'
    Assert-True (-not $f.State.Calls.Contains('Launch')) 'No launch after location mismatch'
}
foreach ($result in @('wrongVersion', 'wrongPublisher', 'empty', 'multiple')) {
    Test-Case "Post-registration query with $result blocks launch" {
        param($f)
        Set-Installed $f '0.1.0.9'
        $f.State.PostRegistrationResult = $result
        Assert-Throws { Invoke-Build $f -Register } '*postcondition failed*'
        Assert-True (($f.State.Calls -join ',') -eq 'Publish,Processes,DeveloperMode,Packages,Processes,Register,Packages') 'Failure occurs after registration query, without launch'
        Assert-True (-not $f.State.Calls.Contains('Launch')) 'No launch after invalid package query result'
    }
}
Test-Case 'Publish mutation of staged manifest fails before finalization' {
    param($f)
    Set-Installed $f '0.1.0.9'
    $sourceHash = (Get-FileHash -LiteralPath $f.State.SourceManifest).Hash
    $f.State.MutateStagedManifest = $true
    Assert-Throws { Invoke-Build $f -Register } '*Publication unexpectedly changed the staged manifest*'
    Assert-True (($f.State.Calls -join ',') -eq 'Publish') 'No host operations after staged manifest mutation'
    Assert-NoRegistration $f
    Assert-True ((Get-FileHash -LiteralPath $f.State.SourceManifest).Hash -eq $sourceHash) 'Source manifest unchanged'
    Assert-True ($f.State.Packages[0].InstallLocation -eq $f.Old) 'Old registration unchanged'
    Assert-True (-not (Test-Path -LiteralPath $f.State.Outputs[0])) 'Mutated staging was removed'
    Assert-True (@(Get-ChildItem (Join-Path $f.Root 'artifacts\screen-translate-builds') -Directory | Where-Object Name -ne '.staging').Count -eq 0) 'No final layout after staged manifest mutation'
}
Write-Host "$script:passed script regression tests passed. No real package/process/launch/publish operations were invoked."
