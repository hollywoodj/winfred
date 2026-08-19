# Builds Winfred into .\dist\Winfred.exe
# Uses the user-local .NET SDK if present (installed by dotnet-install), else PATH dotnet.
$localDotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $dotnet = $localDotnet
    $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
} else {
    $dotnet = "dotnet"
}

& $dotnet publish "$PSScriptRoot\Winfred.csproj" -c Release -o "$PSScriptRoot\dist"
if ($LASTEXITCODE -eq 0) {
    Write-Host "`nDone. Run: $PSScriptRoot\dist\Winfred.exe"
}
