<#
.SYNOPSIS
  Build the C# code and launch the game (or the Godot editor).

.EXAMPLE
  .\run.ps1            # build and run the game
  .\run.ps1 -Editor    # build and open the project in the Godot editor
  .\run.ps1 -Console   # run with a console window that shows logs and errors

  Set $env:GODOT to a Godot .NET executable to use a specific install.
#>
param(
    [switch]$Editor,
    [switch]$Console
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Find-Godot([bool]$wantConsole) {
    if ($env:GODOT) { return $env:GODOT }
    $pattern = if ($wantConsole) { 'Godot_v4*_mono_*_console.exe' } else { 'Godot_v4*_mono_win64.exe' }
    $cmd = Get-Command $pattern -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($cmd) { return $cmd.Source }
    throw "Godot .NET not found. Install it with 'winget install GodotEngine.GodotEngine.Mono' or set `$env:GODOT to its .exe path."
}

$godot = Find-Godot $Console.IsPresent

# Godot only builds C# by itself when you press Play in the editor,
# so build first to make sure the game runs the latest code.
Write-Host 'Building...'
dotnet build "$root\Game\Game.csproj" -v q --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Editor) {
    & $godot --path "$root\Game" --editor
} elseif ($Console) {
    # The console build blocks and prints the game's output here.
    & $godot --path "$root\Game"
} else {
    Start-Process $godot -ArgumentList '--path', "`"$root\Game`""
}
