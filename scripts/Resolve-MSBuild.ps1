function Resolve-MSBuild {
    $command = Get-Command "MSBuild.exe" -ErrorAction SilentlyContinue
    if ($command -and (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return $command.Source
    }

    $vswhereCandidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "$env:ProgramFiles\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($vswhere in $vswhereCandidates) {
        if (!(Test-Path -LiteralPath $vswhere -PathType Leaf)) {
            continue
        }

        $resolved = & $vswhere -latest -products * `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if (![string]::IsNullOrWhiteSpace($resolved) -and (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            return $resolved
        }
    }

    $roots = @("$env:ProgramFiles", "${env:ProgramFiles(x86)}") |
        Where-Object { ![string]::IsNullOrWhiteSpace($_) }
    foreach ($root in $roots) {
        foreach ($edition in @("Enterprise", "Professional", "Community", "BuildTools")) {
            $candidate = Join-Path $root "Microsoft Visual Studio\2022\$edition\MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return $candidate
            }
        }
    }

    throw "Visual Studio 2022 MSBuild with the C++ desktop workload is required."
}
