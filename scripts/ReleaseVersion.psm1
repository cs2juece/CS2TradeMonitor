Set-StrictMode -Version Latest

function Normalize-ReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    $normalized = $Version.Trim()
    if ($normalized.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring(1)
    }

    if ($normalized -notmatch '^(0|[1-9]\d*)\.(0|\d+)(?:\.(0|\d+))?(?:\.(0|\d+))?$') {
        throw "Release version must contain two to four numeric components: '$Version'."
    }

    return $normalized
}

function Get-ComparableReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    $normalized = Normalize-ReleaseVersion -Version $Version -ErrorAction Stop
    $parts = @($normalized.Split('.'))
    while ($parts.Count -lt 4) {
        $parts += '0'
    }

    return ($parts -join '.')
}

function Get-ProjectReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)

    if (-not (Test-Path -LiteralPath $ProjectFile -PathType Leaf)) {
        throw "Project file not found: $ProjectFile"
    }

    [xml]$project = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ProjectFile).Path)
    $versions = @(
        @($project.Project.PropertyGroup.Version) | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_)
        }
    )
    if ($versions.Count -ne 1) {
        throw "Project must define exactly one non-empty Version property. Found $($versions.Count)."
    }

    return Normalize-ReleaseVersion -Version ([string]$versions[0])
}

function Assert-ReleaseVersionEquivalent {
    param(
        [Parameter(Mandatory = $true)][string]$Expected,
        [Parameter(Mandatory = $true)][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $expectedComparable = Get-ComparableReleaseVersion -Version $Expected
    $actualComparable = Get-ComparableReleaseVersion -Version $Actual
    if (-not [string]::Equals($expectedComparable, $actualComparable, [StringComparison]::Ordinal)) {
        throw "$Label version mismatch: expected '$Expected', actual '$Actual'."
    }
}

Export-ModuleMember -Function Normalize-ReleaseVersion
Export-ModuleMember -Function Get-ProjectReleaseVersion
Export-ModuleMember -Function Assert-ReleaseVersionEquivalent
