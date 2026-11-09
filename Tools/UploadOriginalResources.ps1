param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$CheckoutPath = (Join-Path ([IO.Path]::GetTempPath()) ("Throne-resource-upload-" + [Guid]::NewGuid().ToString("N")))
)
$ErrorActionPreference = "Stop"
$source = Join-Path ([IO.Path]::GetFullPath($SourceRoot)) "NestOfThorns"
if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "NestOfThorns folder not found: $source" }
$files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
if ($files.Count -lt 2) { throw "No original game resources found." }
if ($files | Where-Object { $_.Length -ge 100MB }) { throw "A resource exceeds GitHub's 100 MB file limit; Git LFS is required." }
if (Test-Path -LiteralPath $CheckoutPath) { throw "CheckoutPath must be a new directory." }

function Invoke-Git {
    param([string[]]$GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "Git failed (exit $LASTEXITCODE)." }
}

Invoke-Git -GitArgs @("clone", "--branch", "main", "--single-branch", "https://github.com/N7rtaS/Throne.git", $CheckoutPath)
$destination = Join-Path $CheckoutPath "NestOfThorns"
[IO.Directory]::CreateDirectory($destination) | Out-Null
$rows = [Collections.Generic.List[string]]::new()
$rows.Add("path" + [char]9 + "bytes" + [char]9 + "sha256")
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]"\/")
    # Preserve the repository's newer extraction manifest.
    $outputName = if ($relative -eq "manifest.tsv") { "imported-windows-manifest.tsv" } else { $relative }
    $target = Join-Path $destination $outputName
    if ((Test-Path -LiteralPath $target) -and $relative -ne "manifest.tsv") {
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw "A different resource already exists in the repository: $relative"
        }
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $rows.Add($outputName.Replace("\", "/") + [char]9 + $file.Length + [char]9 + (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant())
}
[IO.File]::WriteAllLines((Join-Path $destination "uploaded-resources.tsv"), $rows, [Text.UTF8Encoding]::new($false))
Invoke-Git -GitArgs @("-C", $CheckoutPath, "add", "--force", "--", "NestOfThorns")
& git -C $CheckoutPath diff --cached --quiet
if ($LASTEXITCODE -eq 0) { Write-Host "All resources are already uploaded."; return }
if ($LASTEXITCODE -ne 1) { throw "Cannot inspect staged resources." }
Invoke-Git -GitArgs @("-C", $CheckoutPath, "-c", "user.name=N7rtaS", "-c", "user.email=82465801+N7rtaS@users.noreply.github.com", "commit", "-m", "Add locally extracted original game resources")
Invoke-Git -GitArgs @("-C", $CheckoutPath, "push", "origin", "HEAD:main")
Write-Host ("Uploaded {0} files; checkout retained at {1}" -f $files.Count, $CheckoutPath)
