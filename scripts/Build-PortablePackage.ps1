param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Changelog = "",
    [string]$ExpectedVersion = ""
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$projectFile = Join-Path $projectDir "CS2TradeMonitor.csproj"
$quantWebProjectFile = Join-Path $projectDir "CS2QuantWeb\CS2QuantWeb.csproj"
$bootstrapperProjectFile = Join-Path $projectDir "CS2TradeMonitor.Bootstrapper\CS2TradeMonitor.Bootstrapper.vcxproj"
$bootstrapperPublishScript = Join-Path $projectDir "scripts\Publish-Bootstrapper.ps1"
$updaterProjectFile = Join-Path $projectDir "CS2TradeMonitor.Updater\CS2TradeMonitor.Updater.csproj"
$releaseVersionModule = Join-Path $projectDir "scripts\ReleaseVersion.psm1"
Import-Module $releaseVersionModule -Force -DisableNameChecking
$version = Get-ProjectReleaseVersion -ProjectFile $projectFile
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    Assert-ReleaseVersionEquivalent -Expected $version -Actual $ExpectedVersion -Label "Expected release"
}

if ([string]::IsNullOrWhiteSpace($Changelog)) {
    $Changelog = "CS2 Trade Monitor $version 发布包。"
}

$authorizedResourceHashes = [ordered]@{
    "resources\steamdt_items.json.gz" = "D581F288CFC0F633FAFEFA1265E9CACDE41FBE7486EDBE0A4B7B28AC2905F463"
    "resources\api-help\steamdt-api-key.png" = "F0EE20B3C9BE47FE091C6CAC23DA61CBEBC04645994BFD4A36BEFF7124DCA833"
    "resources\api-help\steamdt-api-menu.png" = "9381E560F6A76B80E898ED17C9155FE6183780E67800FD2213BD1E6B01036DCF"
    "resources\api-help\steamdt-api-open.png" = "A3E49505AE91B132E68DA9DA9BD6566B828E87A37242FDC4B0CE063CF644B486"
}
foreach ($entry in $authorizedResourceHashes.GetEnumerator()) {
    $resourcePath = Join-Path $projectDir $entry.Key
    if (!(Test-Path -LiteralPath $resourcePath -PathType Leaf)) {
        throw "Authorized release resource is missing: $($entry.Key)"
    }
    $actualHash = (Get-FileHash -LiteralPath $resourcePath -Algorithm SHA256).Hash
    if ($actualHash -ne $entry.Value) {
        throw "Authorized release resource changed and requires renewed approval: $($entry.Key)"
    }
}

$appName = "CS2TradeMonitor"
$packageName = "${appName}_v$version-$Runtime"
$artifactsRoot = Join-Path $projectDir "bin\release-package"
$stagingRoot = Join-Path $artifactsRoot "staging"
$publishDir = Join-Path $stagingRoot $packageName
$quantWebPublishDir = Join-Path $artifactsRoot "quant-web-publish"
$bootstrapperPublishDir = Join-Path $artifactsRoot "bootstrapper-publish"
$updaterPublishDir = Join-Path $artifactsRoot "updater-publish"
$zipPath = Join-Path $artifactsRoot "$packageName.zip"
$manifestPath = Join-Path $artifactsRoot "latest.json"
$legacyManifestPath = Join-Path $artifactsRoot "version.json"
$sha256Path = Join-Path $artifactsRoot "sha256.txt"
$changelogPath = Join-Path $artifactsRoot "changelog.txt"

function Assert-UnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $pathFull = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $rootPrefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar

    if ($pathFull -ne $rootFull -and
        !$pathFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate outside release artifacts directory: $pathFull"
    }
}

function Remove-PathSafe {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    Assert-UnderPath -Path $Path -Root $Root
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$FullPath
    )

    $baseUri = [System.Uri](([System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar))
    $fileUri = [System.Uri]([System.IO.Path]::GetFullPath($FullPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

function Assert-PublishedExecutableVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label executable not found: $Path"
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    Assert-ReleaseVersionEquivalent -Expected $version -Actual $versionInfo.ProductVersion -Label "$Label product"
    Assert-ReleaseVersionEquivalent -Expected $version -Actual $versionInfo.FileVersion -Label "$Label file"
}

function Test-AllowedPackageFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $rel = $RelativePath.Replace('\', '/')
    return $rel -eq "$appName.exe" `
        -or $rel -eq "使用说明(必读).txt" `
        -or $rel -eq "app/$appName.exe" `
        -or $rel -eq "app/$appName.dll" `
        -or $rel -eq "app/$appName.deps.json" `
        -or $rel -eq "app/$appName.runtimeconfig.json" `
        -or $rel -eq "app/$appName.Updater.exe" `
        -or $rel -eq "app/program-files.json" `
        -or $rel -like "app/*.dll" `
        -or $rel -like "app/runtimes/*" `
        -or $rel -eq "docs/THIRD_PARTY_NOTICES.txt" `
        -or ($rel -like "docs/*.txt" -and $rel -notlike "docs/*/*") `
        -or $rel -like "resources/api-help/*.png" `
        -or $rel -eq "resources/steamdt_items.json.gz" `
        -or $rel -eq "resources/lang/zh.json" `
        -or $rel -eq "resources/author-note.txt" `
        -or $rel -eq "quant-web/CS2QuantWeb.exe" `
        -or $rel -eq "quant-web/CS2QuantWeb.dll" `
        -or $rel -eq "quant-web/CS2QuantWeb.Core.dll" `
        -or $rel -eq "quant-web/CS2MarketData.Core.dll" `
        -or $rel -eq "quant-web/CS2QuantWeb.deps.json" `
        -or $rel -eq "quant-web/CS2QuantWeb.runtimeconfig.json" `
        -or $rel -eq "quant-web/CS2QuantWeb.staticwebassets.endpoints.json" `
        -or $rel -eq "quant-web/CS2QuantWeb.staticwebassets.runtime.json" `
        -or $rel -eq "quant-web/appsettings.json" `
        -or $rel -like "quant-web/wwwroot/*" `
        -or $rel -like "runtimes/*"
}

function Assert-PackageFiles {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File)
    $forbiddenNamePatterns = @(
        ".env",
        ".env.*",
        "*.env",
        "settings*.json",
        "*.log",
        "*.pdb",
        "*.bak",
        "*.tmp",
        "*.download",
        "*.flag",
        "*.dmp",
        "*.dump",
        "*.har",
        "steam_tokens.dat",
        "steam_auth.dat",
        "steam_manual_proxy.dat",
        "youpin_auth.dat",
        "youpin_device_profile.json",
        "SteamDT_API_填写说明.txt",
        "PawnIO_setup.exe",
        "driver.zip"
    )

    $forbiddenDirectories = @(Get-ChildItem -LiteralPath $Directory -Recurse -Directory | Where-Object {
        $_.Name -in @("user-data", "secure", "logs", "backup")
    })
    if ($forbiddenDirectories.Count -gt 0) {
        throw "Forbidden directory in package: $(Get-RelativePath -BasePath $Directory -FullPath $forbiddenDirectories[0].FullName)"
    }

    foreach ($file in $files) {
        foreach ($pattern in $forbiddenNamePatterns) {
            if ($file.Name -like $pattern) {
                throw "Forbidden file in package: $(Get-RelativePath -BasePath $Directory -FullPath $file.FullName)"
            }
        }

        $relative = Get-RelativePath -BasePath $Directory -FullPath $file.FullName
        if (!(Test-AllowedPackageFile -RelativePath $relative)) {
            throw "Unexpected file in package: $relative"
        }
    }

    $requiredFiles = @(
        "$appName.exe",
        "app\$appName.exe",
        "app\$appName.dll",
        "app\$appName.deps.json",
        "app\$appName.runtimeconfig.json",
        "app\$appName.Updater.exe",
        "app\program-files.json",
        "docs\THIRD_PARTY_NOTICES.txt",
        "使用说明(必读).txt",
        "resources\api-help\steamdt-api-key.png",
        "resources\api-help\steamdt-api-menu.png",
        "resources\api-help\steamdt-api-open.png",
        "resources\lang\zh.json",
        "resources\author-note.txt",
        "resources\steamdt_items.json.gz",
        "quant-web\CS2QuantWeb.exe",
        "quant-web\CS2QuantWeb.dll",
        "quant-web\CS2QuantWeb.Core.dll",
        "quant-web\CS2MarketData.Core.dll",
        "quant-web\CS2QuantWeb.deps.json",
        "quant-web\CS2QuantWeb.runtimeconfig.json",
        "quant-web\appsettings.json",
        "quant-web\wwwroot\index.html",
        "quant-web\wwwroot\app.js",
        "quant-web\wwwroot\app.css"
    )

    foreach ($required in $requiredFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $Directory $required))) {
            throw "Required package file missing: $required"
        }
    }

    foreach ($obsoleteAppHost in @("$appName.App.exe", "app\$appName.App.exe")) {
        if (Test-Path -LiteralPath (Join-Path $Directory $obsoleteAppHost)) {
            throw "Obsolete managed apphost must not be packaged: $obsoleteAppHost"
        }
    }

    $allowedRootFiles = @("$appName.exe", "使用说明(必读).txt")
    $unexpectedRootFiles = @(Get-ChildItem -LiteralPath $Directory -File |
        Where-Object { $_.Name -notin $allowedRootFiles })
    if ($unexpectedRootFiles.Count -gt 0) {
        throw "Package root contains unexpected files: $($unexpectedRootFiles.Name -join ', ')"
    }

    if (Test-Path -LiteralPath (Join-Path $Directory "resources\steamdt_items.json")) {
        throw "Unexpected uncompressed SteamDT item library in package: resources\steamdt_items.json"
    }

    $apiHelpImagesDir = Join-Path $Directory "resources\api-help"
    $apiHelpImages = @(Get-ChildItem -LiteralPath $apiHelpImagesDir -Filter "*.png" -File -ErrorAction SilentlyContinue)
    if ($apiHelpImages.Count -ne 3) {
        throw "Package must contain exactly three authorized resources\api-help PNG files."
    }

    $rootDocs = @(Get-ChildItem -LiteralPath $Directory -Filter "*.txt" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne "使用说明(必读).txt" })
    if ($rootDocs.Count -gt 0) {
        throw "Package root contains unexpected documentation: $($rootDocs.Name -join ', ')"
    }

    $programManifestPath = Join-Path $Directory "app\program-files.json"
    $programManifest = Get-Content -LiteralPath $programManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([int]$programManifest.version -ne 1) {
        throw "Program file manifest version must be 1."
    }
    $declaredFiles = @($programManifest.files | ForEach-Object { ([string]$_).Replace('\', '/') } | Sort-Object -Unique)
    $actualFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object {
        (Get-RelativePath -BasePath $Directory -FullPath $_.FullName).Replace('\', '/')
    } | Sort-Object -Unique)
    if ((Compare-Object -ReferenceObject $declaredFiles -DifferenceObject $actualFiles).Count -ne 0) {
        throw "Program file manifest does not match staged files."
    }

}

function Write-ProgramFileManifest {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $relativeManifest = "app/program-files.json"
    $path = Join-Path $Directory $relativeManifest.Replace('/', '\')
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | ForEach-Object {
        (Get-RelativePath -BasePath $Directory -FullPath $_.FullName).Replace('\', '/')
    })
    $files = @($files + $relativeManifest | Sort-Object -Unique)
    $obsolete = @(
        "CS2TradeMonitor.App.exe",
        "CS2TradeMonitor.Updater.exe",
        "WebView2Loader.dll",
        "THIRD_PARTY_NOTICES.txt",
        "SteamDT_API_填写说明.txt",
        "docs/SteamDT_API_填写说明.txt",
        "docs/使用说明(必读).txt",
        "quant-web/data/sample.csv"
    )
    [ordered]@{ version = 1; files = $files; obsolete = $obsolete } |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $path -Encoding UTF8
}

function Assert-DependencyFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $depsPath = Join-Path $Directory "$appName.deps.json"
    if (!(Test-Path -LiteralPath $depsPath)) {
        $appDll = Join-Path $Directory "$appName.dll"
        if (Test-Path -LiteralPath $appDll) {
            throw "Dependency audit failed: $appName.dll exists, but $appName.deps.json is missing from the package."
        }

        Write-Host "Dependency audit skipped: single-file package has no $appName.deps.json."
        return
    }

    $deps = Get-Content -LiteralPath $depsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $libraryProps = @($deps.libraries.PSObject.Properties)

    $unsupportedJson = @($libraryProps | Where-Object {
            $_.Name -match '^System\.Text\.Json/([0-9]+)\.' -and [int]$Matches[1] -gt 10
        })
    if ($unsupportedJson.Count -gt 0 -and !(Test-Path -LiteralPath (Join-Path $Directory "System.Text.Json.dll"))) {
        throw "Dependency audit failed: deps.json references a System.Text.Json version newer than the .NET 10 framework, but System.Text.Json.dll is not in the package. Include the matching DLL or remove the incompatible dependency."
    }

    $targetNames = @($deps.targets.PSObject.Properties.Name)
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($targetName in $targetNames) {
        $target = ($deps.targets.PSObject.Properties | Where-Object { $_.Name -eq $targetName } | Select-Object -First 1).Value
        if ($null -eq $target) {
            continue
        }

        foreach ($targetLibrary in @($target.PSObject.Properties)) {
            $libraryName = [string]$targetLibrary.Name
            if ($libraryName -like "$appName/*") {
                continue
            }

            $libraryInfo = ($libraryProps | Where-Object { $_.Name -eq $libraryName } | Select-Object -First 1).Value
            if ($null -ne $libraryInfo -and [string]$libraryInfo.type -eq "framework") {
                continue
            }

            $runtime = $targetLibrary.Value.runtime
            if ($null -eq $runtime) {
                continue
            }

            foreach ($asset in @($runtime.PSObject.Properties.Name)) {
                if ($asset -notlike "*.dll") {
                    continue
                }

                $fileName = [System.IO.Path]::GetFileName($asset)
                if ($fileName -like "System.*.dll" -or $fileName -like "Microsoft.*.dll") {
                    continue
                }

                $rootFile = Join-Path $Directory $fileName
                $relativeFile = Join-Path $Directory ($asset.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                if (!(Test-Path -LiteralPath $rootFile) -and !(Test-Path -LiteralPath $relativeFile)) {
                    $missing.Add("$libraryName -> $asset")
                }
            }
        }
    }

    if ($missing.Count -gt 0) {
        throw "Dependency audit failed: non-framework DLL references are missing from package:`n$($missing -join "`n")"
    }
}

function Assert-ZipStructure {
    param(
        [Parameter(Mandatory = $true)][string]$ZipFile,
        [Parameter(Mandatory = $true)][string]$TopDirectory
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipFile)
    try {
        $names = @($archive.Entries |
            Where-Object { ![string]::IsNullOrWhiteSpace($_.FullName) } |
            ForEach-Object { $_.FullName.Replace('\', '/') })
        if ($names.Count -eq 0) {
            throw "ZIP is empty."
        }

        $invalid = @($names | Where-Object {
                $_ -ne $TopDirectory `
                    -and $_ -ne "$TopDirectory/" `
                    -and $_ -notlike "$TopDirectory/*"
            })
        if ($invalid.Count -gt 0) {
            throw "ZIP must contain exactly one top-level directory: $TopDirectory"
        }

        if ($names -notcontains "$TopDirectory/$appName.exe") {
            throw "ZIP is missing $appName.exe."
        }
        if ($names -notcontains "$TopDirectory/app/$appName.dll") {
            throw "ZIP is missing app/$appName.dll."
        }
        if ($names -notcontains "$TopDirectory/app/$appName.exe") {
            throw "ZIP is missing app/$appName.exe."
        }
        if ($names -notcontains "$TopDirectory/app/$appName.deps.json") {
            throw "ZIP is missing app/$appName.deps.json."
        }
        if ($names -notcontains "$TopDirectory/app/$appName.runtimeconfig.json") {
            throw "ZIP is missing app/$appName.runtimeconfig.json."
        }
        foreach ($obsoleteAppHost in @(
            "$TopDirectory/$appName.App.exe",
            "$TopDirectory/app/$appName.App.exe")) {
            if ($names -contains $obsoleteAppHost) {
                throw "ZIP contains obsolete apphost $obsoleteAppHost."
            }
        }
        if ($names -notcontains "$TopDirectory/app/$appName.Updater.exe") {
            throw "ZIP is missing app/$appName.Updater.exe."
        }
        if ($names -notcontains "$TopDirectory/app/program-files.json") {
            throw "ZIP is missing app/program-files.json."
        }
        foreach ($quantWebFile in @(
            "CS2QuantWeb.exe",
            "CS2QuantWeb.dll",
            "CS2QuantWeb.Core.dll",
            "CS2MarketData.Core.dll",
            "CS2QuantWeb.deps.json",
            "CS2QuantWeb.runtimeconfig.json",
            "appsettings.json",
            "wwwroot/index.html",
            "wwwroot/app.js",
            "wwwroot/app.css")) {
            if ($names -notcontains "$TopDirectory/quant-web/$quantWebFile") {
                throw "ZIP is missing quant-web/$quantWebFile."
            }
        }
        $forbiddenPaths = @($names | Where-Object {
            $_ -match '(^|/)(user-data|secure|logs|backup)(/|$)' `
                -or $_ -match '(?i)\.(pdb|dmp|dump|har|download|tmp|bak|log|flag)$'
        })
        if ($forbiddenPaths.Count -gt 0) {
            throw "ZIP contains forbidden runtime data: $($forbiddenPaths[0])"
        }
        $rootFiles = @($names | Where-Object {
                $_ -match "^$([regex]::Escape($TopDirectory))/[^/]+$" `
                    -and $_ -ne "$TopDirectory/$appName.exe" `
                    -and $_ -ne "$TopDirectory/使用说明(必读).txt"
            })
        if ($rootFiles.Count -gt 0) {
            throw "ZIP root contains unexpected files: $($rootFiles -join ', ')"
        }
        $apiHelpImages = @($names | Where-Object { $_ -like "$TopDirectory/resources/api-help/*.png" })
        if ($apiHelpImages.Count -ne 3) {
            throw "ZIP must contain exactly three authorized resources/api-help PNG files."
        }
        if ($names -notcontains "$TopDirectory/resources/steamdt_items.json.gz") {
            throw "ZIP is missing resources/steamdt_items.json.gz."
        }
        if ($names -notcontains "$TopDirectory/使用说明(必读).txt") {
            throw "ZIP is missing root usage txt."
        }
        if ($names -notcontains "$TopDirectory/docs/THIRD_PARTY_NOTICES.txt") {
            throw "ZIP is missing docs/THIRD_PARTY_NOTICES.txt."
        }
        $topDirectoryPattern = [regex]::Escape($TopDirectory)
        $rootDocs = @($names | Where-Object {
                $_ -match "^$topDirectoryPattern/[^/]+\.txt$" `
                    -and $_ -ne "$TopDirectory/使用说明(必读).txt"
            })
        if ($rootDocs.Count -gt 0) {
            throw "ZIP contains unexpected root documentation: $($rootDocs -join ', ')"
        }
        if ($names -notcontains "$TopDirectory/resources/lang/zh.json") {
            throw "ZIP is missing resources/lang/zh.json."
        }
    }
    finally {
        $archive.Dispose()
    }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
Get-ChildItem -LiteralPath $artifactsRoot -Filter "*.zip" -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "$($appName)_v*-$Runtime.zip"
    } |
    ForEach-Object {
        Remove-PathSafe -Path $_.FullName -Root $artifactsRoot
    }
Remove-PathSafe -Path $stagingRoot -Root $artifactsRoot
Remove-PathSafe -Path $quantWebPublishDir -Root $artifactsRoot
Remove-PathSafe -Path $bootstrapperPublishDir -Root $artifactsRoot
Remove-PathSafe -Path $updaterPublishDir -Root $artifactsRoot
Remove-PathSafe -Path $zipPath -Root $artifactsRoot
Remove-PathSafe -Path $manifestPath -Root $artifactsRoot
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $projectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:UseAppHost=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    "-p:Version=$version" `
    "-p:PublishDir=$publishDir"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedAppDll = Join-Path $publishDir "$appName.dll"
$publishedAppHost = Join-Path $publishDir "$appName.exe"
$appPublishDir = Join-Path $publishDir "app"
if (!(Test-Path -LiteralPath $publishedAppDll)) {
    throw "App publish failed: $appName.dll was not produced."
}
if (!(Test-Path -LiteralPath $publishedAppHost)) {
    throw "App publish failed: $appName.exe AppHost was not produced."
}
New-Item -ItemType Directory -Path $appPublishDir -Force | Out-Null
Get-ChildItem -LiteralPath $publishDir -File |
    Where-Object { $_.Name -ne "使用说明(必读).txt" } |
    Move-Item -Destination $appPublishDir -Force
$publishedRuntimes = Join-Path $publishDir "runtimes"
if (Test-Path -LiteralPath $publishedRuntimes -PathType Container) {
    Move-Item -LiteralPath $publishedRuntimes -Destination (Join-Path $appPublishDir "runtimes") -Force
}
$packagedAppDll = Join-Path $appPublishDir "$appName.dll"
$packagedAppHost = Join-Path $appPublishDir "$appName.exe"

dotnet publish $quantWebProjectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=false `
    -p:UseAppHost=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    "-p:Version=$version" `
    "-p:PublishDir=$quantWebPublishDir"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish quant research service failed with exit code $LASTEXITCODE."
}

foreach ($unneededQuantFile in @("appsettings.Development.json", "web.config")) {
    $path = Join-Path $quantWebPublishDir $unneededQuantFile
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}
Get-ChildItem -LiteralPath $quantWebPublishDir -Include "*.pdb", "*.xml" -File -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force
$quantWebPackageDir = Join-Path $publishDir "quant-web"
Move-Item -LiteralPath $quantWebPublishDir -Destination $quantWebPackageDir

& $bootstrapperPublishScript `
    -ProjectFile $bootstrapperProjectFile `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -Version $version `
    -PublishDirectory $bootstrapperPublishDir

dotnet publish $updaterProjectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    "-p:Version=$version" `
    "-p:PublishDir=$updaterPublishDir"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish updater failed with exit code $LASTEXITCODE."
}

$updaterExe = Join-Path $updaterPublishDir "$appName.Updater.exe"
if (!(Test-Path -LiteralPath $updaterExe)) {
    throw "Updater publish failed: $appName.Updater.exe was not produced."
}
$bootstrapperExe = Join-Path $bootstrapperPublishDir "$appName.Bootstrapper.exe"
if (!(Test-Path -LiteralPath $bootstrapperExe)) {
    throw "Bootstrapper publish failed: $appName.Bootstrapper.exe was not produced."
}
Assert-PublishedExecutableVersion -Path $packagedAppDll -Label "$appName app DLL"
Assert-PublishedExecutableVersion -Path $packagedAppHost -Label "$appName app AppHost"
Assert-PublishedExecutableVersion -Path (Join-Path $quantWebPackageDir "CS2QuantWeb.exe") -Label "CS2QuantWeb"
Assert-PublishedExecutableVersion -Path $bootstrapperExe -Label $appName
Assert-PublishedExecutableVersion -Path $updaterExe -Label "$appName.Updater"
Copy-Item -LiteralPath $bootstrapperExe -Destination (Join-Path $publishDir "$appName.exe") -Force
Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $appPublishDir "$appName.Updater.exe") -Force

Get-ChildItem -LiteralPath $publishDir -Filter "*.xml" -File -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force

Write-ProgramFileManifest -Directory $publishDir
Assert-PackageFiles -Directory $publishDir
Assert-DependencyFiles -Directory $appPublishDir

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stagingRoot,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

Assert-ZipStructure -ZipFile $zipPath -TopDirectory $packageName

$zipItem = Get-Item -LiteralPath $zipPath
$zipMB = $zipItem.Length / 1MB
$zipSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$supportInfoPath = Join-Path $projectDir "src\Core\SupportInfo.cs"
$supportUrlsPath = Join-Path $projectDir "src\Core\SupportUrls.cs"
$assetUrls = @()
if ((Test-Path -LiteralPath $supportInfoPath) -or (Test-Path -LiteralPath $supportUrlsPath)) {
    $supportInfoText = ""
    if (Test-Path -LiteralPath $supportInfoPath) {
        $supportInfoText += Get-Content -LiteralPath $supportInfoPath -Raw -Encoding UTF8
    }
    if (Test-Path -LiteralPath $supportUrlsPath) {
        $supportInfoText += "`n" + (Get-Content -LiteralPath $supportUrlsPath -Raw -Encoding UTF8)
    }

    function Get-SupportConst {
        param([Parameter(Mandatory = $true)][string]$Name)
        $match = [regex]::Match($supportInfoText, "$Name\s*=\s*`"([^`"]*)`"")
        if ($match.Success) { return $match.Groups[1].Value }
        return ""
    }

    $giteeBase = Get-SupportConst -Name "GiteePackageBaseUrl"
    if ([string]::IsNullOrWhiteSpace($giteeBase)) {
        $giteeBase = Get-SupportConst -Name "GiteePackageBase"
    }
    if (![string]::IsNullOrWhiteSpace($giteeBase)) {
        $assetUrls += [ordered]@{
            name = "Gitee 国内";
            priority = 1;
            url = ($giteeBase.TrimEnd('/') + "/" + "$packageName.zip")
        }
    }

    $githubMatch = [regex]::Match($supportInfoText, '(GitHubUrl|GitHubRepository)\s*=\s*"([^"]+)"')
    if ($githubMatch.Success) {
        $githubUrl = $githubMatch.Groups[2].Value
        $repoMatch = [regex]::Match($githubUrl, 'github\.com/([^/]+)/([^/"\s]+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($repoMatch.Success) {
            $owner = $repoMatch.Groups[1].Value
            $repo = $repoMatch.Groups[2].Value.TrimEnd('/')
            $assetUrls += [ordered]@{
                name = "GitHub 官方";
                priority = 2;
                url = "https://github.com/$owner/$repo/releases/download/v$version/$packageName.zip"
            }
        }
    }

    $cloudflareBase = Get-SupportConst -Name "CloudflarePackageBaseUrl"
    if ([string]::IsNullOrWhiteSpace($cloudflareBase)) {
        $cloudflareBase = Get-SupportConst -Name "CloudflarePackageBase"
    }
    if (![string]::IsNullOrWhiteSpace($cloudflareBase)) {
        $assetUrls += [ordered]@{
            name = "Cloudflare 备用";
            priority = 3;
            url = ($cloudflareBase.TrimEnd('/') + "/" + "$packageName.zip")
        }
    }
}

$primaryAssetUrl = ""
if ($assetUrls.Count -gt 0) {
    $primaryAssetUrl = ($assetUrls | Sort-Object priority | Select-Object -First 1).url
}

$manifest = [ordered]@{
    version = $version;
    releaseDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    changelog = $Changelog;
    minSupportedVersion = "1.0";
    assets = [ordered]@{
        winX64 = [ordered]@{
            url = $primaryAssetUrl;
            urls = @($assetUrls);
            sha256 = $zipSha256;
            sizeBytes = $zipItem.Length
        }
    }
}

($manifest | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $manifestPath -Encoding UTF8
($manifest | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $legacyManifestPath -Encoding UTF8
"$zipSha256  $packageName.zip" | Set-Content -LiteralPath $sha256Path -Encoding UTF8
$Changelog | Set-Content -LiteralPath $changelogPath -Encoding UTF8

Write-Host ""
Write-Host "Publish completed:"
Write-Host $publishDir
Write-Host ""
Write-Host "ZIP:"
$zipMBText = "{0:N2}" -f $zipMB
Write-Host "$zipPath ($zipMBText MB)"
Write-Host "SHA256:"
Write-Host $zipSha256
Write-Host ""
Write-Host "latest.json:"
Write-Host $manifestPath
Write-Host "version.json (legacy compatibility):"
Write-Host $legacyManifestPath
Write-Host "sha256.txt:"
Write-Host $sha256Path
Write-Host "changelog.txt:"
Write-Host $changelogPath
Write-Host ""
Write-Host "Run after extract:"
Write-Host "$packageName\$appName.exe"

Write-Host ""
Write-Host "Staging kept for inspection:"
Write-Host $publishDir
