# Offline domain verification with an installed .NET 10 SDK.
# This does NOT compile or validate the API. It exercises only dependency-free rules.
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot '.tools/domain-check'
$taskDotnetHost = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path $taskDotnetHost)) { $taskDotnetHost = (Get-Command dotnet).Source }
$taskDotnet = Split-Path $taskDotnetHost
$taskSdkVersion = & $taskDotnetHost --version
if ($LASTEXITCODE -ne 0 -or $taskSdkVersion -notlike '10.*') { throw 'Install the .NET 10 SDK selected by global.json. Use dotnet test for normal validation.' }
$taskSdk = Join-Path $taskDotnet "sdk/$taskSdkVersion/Roslyn/bincore/csc.dll"
$taskRefPack = Get-ChildItem (Join-Path $taskDotnet 'packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object Name -Like '10.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$taskRefPack -or !(Test-Path $taskSdk)) { throw 'A .NET 10 SDK and reference pack are required.' }
$taskReferences = Join-Path $taskRefPack.FullName 'ref/net10.0'
New-Item -ItemType Directory -Force $taskOutput | Out-Null
@'
global using System;
global using System.Linq;
using Hearth.Domain;
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
foreach (var owner in new[] { false, true })
foreach (var actor in new[] { false, true })
foreach (var targetOwner in new[] { false, true })
foreach (var targetModerator in new[] { false, true })
{
    var actual = Rules.CanModerate(owner, actor, targetOwner, targetModerator);
    if (targetOwner) Check(!actual, "Owner protection");
    else if (owner) Check(actual, "Owner moderation");
    else Check(actual == (actor && !targetModerator), "Peer protection");
}
var now = DateTimeOffset.UtcNow;
var invite = new Invitation { ExpiresAt = now.AddHours(1), MaxUses = 1 };
Check(Rules.CanInvite(invite, now), "Usable invitation");
invite.Uses = 1;
Check(!Rules.CanInvite(invite, now), "Exhausted invitation");
invite.MaxUses = null;
Check(Rules.CanInvite(invite, now), "Unlimited invitation");
Check(!Rules.CanInvite(invite, invite.ExpiresAt), "Expiration boundary");
invite.Revoked = true;
Check(!Rules.CanInvite(invite, now), "Revoked invitation");
for (var i = 0; i < 100; i++) { var a = Guid.NewGuid(); var b = Guid.NewGuid(); Check(Rules.Pair(a, b) == Rules.Pair(b, a), "Canonical pair"); }
Console.WriteLine($"{checks} dependency-free domain assertions passed (.NET 10 offline check only).");
'@ | Set-Content -LiteralPath (Join-Path $taskOutput 'Check.cs')
$taskArgs = @('/nologo', '/langversion:latest', '/nullable:enable', '/target:exe', ('/out:"' + (Join-Path $taskOutput 'Check.dll') + '"'))
$taskArgs += Get-ChildItem $taskReferences -Filter *.dll | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$taskArgs += '"' + (Join-Path $taskRoot 'src/Hearth.Domain/Models.cs') + '"'
$taskArgs += '"' + (Join-Path $taskOutput 'Check.cs') + '"'
$taskArgs | Set-Content -LiteralPath (Join-Path $taskOutput 'compile.rsp')
& $taskDotnetHost $taskSdk ('@' + (Join-Path $taskOutput 'compile.rsp'))
if ($LASTEXITCODE -ne 0) { throw 'Domain compilation failed.' }
@{ runtimeOptions = @{ tfm = 'net10.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = $taskRefPack.Name } } } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskOutput 'Check.runtimeconfig.json')
& $taskDotnetHost (Join-Path $taskOutput 'Check.dll')
if ($LASTEXITCODE -ne 0) { throw 'Domain assertions failed.' }
