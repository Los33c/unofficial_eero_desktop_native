param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $PSScriptRoot }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
& dotnet build (Join-Path $PSScriptRoot 'EeroDesktop.csproj') -c Release --nologo -v:quiet ('-p:OutputPath=' + $OutputDirectory.TrimEnd('\') + '\')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE-WebView2.txt') -Destination $OutputDirectory -Force -ErrorAction SilentlyContinue
Write-Output (Join-Path $OutputDirectory 'EeroDesktop.exe')
