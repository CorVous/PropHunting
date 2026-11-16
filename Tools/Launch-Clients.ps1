# Launches local VRChat test clients into one shared instance of the last exported world,
# the same way the SDK's "Build & Test" does. All clients use the logged-in account.
#
#   .\Launch-Clients.ps1 -Count 2                 # start a fresh instance with two clients
#   .\Launch-Clients.ps1 -Count 1 -RoomId 12345   # add a late joiner to an existing instance
param(
    [int]$Count = 2,
    [string]$RoomId = "",
    [string]$World = "",
    [string]$VRChat = "C:\Program Files (x86)\Steam\steamapps\common\VRChat\VRChat.exe",
    [int]$Width = 800,
    [int]$Height = 450
)

$project = Split-Path $PSScriptRoot -Parent
if (-not $World) {
    $result = Get-Content (Join-Path $project "PropHunt_last_build.txt")
    if ($result[0] -ne "OK") { throw "Last build did not succeed: $($result[0])" }
    $World = $result[1]
}
if (-not (Test-Path $World)) { throw "World file not found: $World" }
if (-not $RoomId) { $RoomId = (Get-Random -Minimum 1000000000 -Maximum 2147483647).ToString() }

$url = "create?roomId=$RoomId&hidden=true&name=BuildAndRun&url=file:///" + ($World -replace '\\', '/' -replace ' ', '%20')
$logDir = Join-Path $env:USERPROFILE "AppData\LocalLow\VRChat\VRChat"

for ($i = 0; $i -lt $Count; $i++) {
    $before = @(Get-ChildItem $logDir -Filter "output_log*" | Select-Object -ExpandProperty Name)
    $clientArgs = @(
        "--url=$url", "--no-vr", "--enable-debug-gui", "--enable-sdk-log-levels", "--enable-udon-debug-logging",
        "-screen-fullscreen", "0", "-screen-width", "$Width", "-screen-height", "$Height"
    )
    Start-Process -FilePath $VRChat -ArgumentList $clientArgs -WorkingDirectory (Split-Path $VRChat) | Out-Null

    # Each client names its log by start time; wait for the new file so clients can be told apart.
    $log = $null
    for ($w = 0; $w -lt 60 -and -not $log; $w++) {
        Start-Sleep -Seconds 1
        $log = Get-ChildItem $logDir -Filter "output_log*" | Where-Object { $before -notcontains $_.Name } | Select-Object -First 1
    }
    "client log: $(if ($log) { $log.FullName } else { '<none after 60 s>' })"
}
"roomId: $RoomId"
