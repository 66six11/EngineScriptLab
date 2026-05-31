param(
    [string]$ProjectRoot,
    [string]$DotnetPath,
    [string]$ScriptPath = "Samples\PlayerMove.ash.cs",
    [string]$OutputDirectory = "bin\ScriptDebug\EngineHostBridge\emit",
    [string]$BridgeDirectory = "bin\ScriptDebug\EngineHostBridge\bridge",
    [string]$BridgeManifestPath = "bin\ScriptDebug\EngineHostBridge\scriptlab.bridge.json",
    [string]$HostFxrPath,
    [int]$EntityId = 101,
    [string]$InputKey = "W",
    [string]$Delta = "0.016",
    [string]$Configuration = "Debug",
    [switch]$Json
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

function Resolve-DefaultProjectRoot {
    if ($ProjectRoot) {
        return [System.IO.Path]::GetFullPath($ProjectRoot)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}

function Resolve-DefaultDotnetPath {
    if ($DotnetPath) {
        return $DotnetPath
    }

    if ($env:SCRIPTLAB_DOTNET) {
        return $env:SCRIPTLAB_DOTNET
    }

    $riderDotnet = "C:\Program Files\JetBrains\Rider\r2r\2026.1.1R\33CE8650EFF983231DE196E2331D5D3\windows-x64\dotnet\dotnet.exe"
    if (Test-Path -LiteralPath $riderDotnet) {
        return $riderDotnet
    }

    return "dotnet"
}

function Resolve-FullPath([string]$Path, [string]$BaseDirectory) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
}

function Assert-ExistingFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description does not exist: $Path"
    }
}

function Assert-ExecutableIfQualified([string]$Path, [string]$Description) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        Assert-ExistingFile $Path $Description
    }
}

function Resolve-HostFxrPath([string]$Dotnet) {
    if ($HostFxrPath) {
        $resolved = Resolve-FullPath $HostFxrPath (Get-Location).Path
        Assert-ExistingFile $resolved "hostfxr"
        return $resolved
    }

    if (-not [System.IO.Path]::IsPathRooted($Dotnet)) {
        throw "Set -HostFxrPath when -DotnetPath is not an absolute path."
    }

    $dotnetRoot = Split-Path -Parent $Dotnet
    $hostFxrRoot = Join-Path $dotnetRoot "host\fxr"
    if (-not (Test-Path -LiteralPath $hostFxrRoot -PathType Container)) {
        throw "Could not locate hostfxr root under dotnet path: $hostFxrRoot"
    }

    $hostFxr = Get-ChildItem -LiteralPath $hostFxrRoot -Recurse -Filter hostfxr.dll |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $hostFxr) {
        throw "Could not locate hostfxr.dll under: $hostFxrRoot"
    }

    return $hostFxr.FullName
}

function Find-DebugMapForScript([string]$EmitDirectory, [string]$ScriptFullPath) {
    $debugMaps = Get-ChildItem -LiteralPath $EmitDirectory -Filter "*.debugmap.json" -File
    foreach ($candidate in $debugMaps) {
        try {
            $debugMap = Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
        }
        catch {
            continue
        }

        if ($debugMap.sourceDocumentPath) {
            $sourcePath = [System.IO.Path]::GetFullPath($debugMap.sourceDocumentPath)
            if ([string]::Equals($sourcePath, $ScriptFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{
                    Path = $candidate.FullName
                    Map = $debugMap
                }
            }
        }
    }

    throw "Could not find a DebugMap in '$EmitDirectory' for script '$ScriptFullPath'."
}

function Get-BehaviorTypeParts([string]$BehaviorId) {
    $lastDot = $BehaviorId.LastIndexOf(".")
    if ($lastDot -lt 0) {
        return [pscustomobject]@{
            Namespace = ""
            TypeName = $BehaviorId
        }
    }

    return [pscustomobject]@{
        Namespace = $BehaviorId.Substring(0, $lastDot)
        TypeName = $BehaviorId.Substring($lastDot + 1)
    }
}

function Escape-Xml([string]$Value) {
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-EmitSiblingPath([string]$DebugMapPath, [string]$Extension) {
    $suffix = ".debugmap.json"
    if ($DebugMapPath.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $DebugMapPath.Substring(0, $DebugMapPath.Length - $suffix.Length) + $Extension
    }

    return [System.IO.Path]::ChangeExtension($DebugMapPath, $Extension)
}

function Write-BridgeProject(
    [string]$Directory,
    [string]$GeneratedAssemblyPath,
    [string]$BehaviorNamespace,
    [string]$BehaviorTypeName) {
    if ($InputKey -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Input key must be a C# enum member name, for example W."
    }

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    $projectPath = Join-Path $Directory "bridge.csproj"
    $programPath = Join-Path $Directory "Program.cs"
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($GeneratedAssemblyPath)
    $escapedAssembly = Escape-Xml $assemblyName
    $escapedHintPath = Escape-Xml $GeneratedAssemblyPath
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>bridge</AssemblyName>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="$escapedAssembly">
      <HintPath>$escapedHintPath</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>

</Project>
"@

    $usingBehaviorNamespace = ""
    if (-not [string]::IsNullOrWhiteSpace($BehaviorNamespace)) {
        $usingBehaviorNamespace = "using $BehaviorNamespace;`r`n"
    }

    $deltaLiteral = $Delta
    if ($deltaLiteral -notmatch 'f$') {
        $deltaLiteral = "$deltaLiteral" + "f"
    }

    $program = @"
using Asharia.Behavior;
$usingBehaviorNamespace
namespace ScriptLab.GeneratedBridge;

public static class Program
{
    public static void Main()
    {
    }
}

public static class NativeHostBridge
{
    private static readonly SimulatedWorld World = new();

    public static int Prepare(IntPtr args, int size)
    {
        World.RegisterPackage("com.asharia.core");
        World.RegisterPackage("com.asharia.scene-core");
        World.RegisterPackage("com.asharia.script-runtime");
        _ = typeof($BehaviorTypeName).Assembly.FullName;
        return World.PackageCount == 3 ? 0 : 21;
    }

    public static int Entry(IntPtr args, int size)
    {
        var entity = World.CreateEntity($EntityId);
        Input.SetKeyDown(Key.$InputKey, true);
        var system = new SimulatedScriptSystem(World);
        system.Tick(entity, $deltaLiteral);
        return World.FrameCount == 1 ? 0 : 22;
    }
}

public sealed class SimulatedWorld
{
    private readonly HashSet<string> packages = new(StringComparer.Ordinal);
    private readonly HashSet<int> entities = new();

    public int PackageCount => packages.Count;

    public int FrameCount { get; private set; }

    public void RegisterPackage(string packageName)
    {
        packages.Add(packageName);
    }

    public int CreateEntity(int entityId)
    {
        entities.Add(entityId);
        return entityId;
    }

    public void AdvanceFrame(int entityId)
    {
        if (!entities.Contains(entityId))
        {
            throw new InvalidOperationException(
                "Script tick targeted an entity outside the simulated scene world.");
        }

        FrameCount++;
    }
}

public sealed class SimulatedScriptSystem
{
    private readonly SimulatedWorld world;

    public SimulatedScriptSystem(SimulatedWorld world)
    {
        this.world = world;
    }

    public void Tick(int entityId, float delta)
    {
        var instance = new ScriptLabSmokeHost();
        instance.Tick(delta);
        world.AdvanceFrame(entityId);
    }
}

public sealed class ScriptLabSmokeHost : $BehaviorTypeName
{
    public void Tick(float delta)
    {
        base.Update(delta);
    }
}
"@

    Set-Content -LiteralPath $projectPath -Value $project -Encoding UTF8
    Set-Content -LiteralPath $programPath -Value $program -Encoding UTF8
    return $projectPath
}

$projectRootPath = Resolve-DefaultProjectRoot
$dotnet = Resolve-DefaultDotnetPath
$script = Resolve-FullPath $ScriptPath $projectRootPath
$emitDirectory = Resolve-FullPath $OutputDirectory $projectRootPath
$bridgeDirectoryPath = Resolve-FullPath $BridgeDirectory $projectRootPath
$manifestPath = Resolve-FullPath $BridgeManifestPath (Get-Location).Path
$projectPath = Join-Path $projectRootPath "ScriptLab.csproj"
$hostFxr = Resolve-HostFxrPath $dotnet

Assert-ExistingFile $projectPath "ScriptLab project"
Assert-ExistingFile $script "Script file"
Assert-ExecutableIfQualified $dotnet "dotnet executable"
New-Item -ItemType Directory -Path $emitDirectory -Force | Out-Null

& $dotnet run --project $projectPath --configuration $Configuration -- emit-debug $script $emitDirectory
if ($LASTEXITCODE -ne 0) {
    throw "emit-debug failed with exit code $LASTEXITCODE."
}

$debugMap = Find-DebugMapForScript $emitDirectory $script
$behavior = Get-BehaviorTypeParts $debugMap.Map.behaviorId
$generatedAssemblyPath = Get-EmitSiblingPath $debugMap.Path ".dll"
$pdbPath = Get-EmitSiblingPath $debugMap.Path ".pdb"
Assert-ExistingFile $generatedAssemblyPath "Generated assembly"
Assert-ExistingFile $pdbPath "Generated PDB"

$bridgeProjectPath = Write-BridgeProject $bridgeDirectoryPath $generatedAssemblyPath $behavior.Namespace $behavior.TypeName
& $dotnet build $bridgeProjectPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "bridge project build failed with exit code $LASTEXITCODE."
}

$bridgeOutputDirectory = Join-Path $bridgeDirectoryPath "bin\$Configuration\net10.0"
$bridgeAssemblyPath = Join-Path $bridgeOutputDirectory "bridge.dll"
$bridgeRuntimeConfigPath = Join-Path $bridgeOutputDirectory "bridge.runtimeconfig.json"
Assert-ExistingFile $bridgeAssemblyPath "Bridge assembly"
Assert-ExistingFile $bridgeRuntimeConfigPath "Bridge runtimeconfig"

if (Split-Path -Parent $manifestPath) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $manifestPath) -Force | Out-Null
}

$manifest = [ordered]@{
    hostfxrPath = $hostFxr
    runtimeConfigPath = $bridgeRuntimeConfigPath
    assemblyPath = $bridgeAssemblyPath
    typeName = "ScriptLab.GeneratedBridge.NativeHostBridge, bridge"
    prepareMethod = "Prepare"
    entryMethod = "Entry"
    generatedAssemblyPath = $generatedAssemblyPath
    pdbPath = $pdbPath
    debugMapPath = $debugMap.Path
    sourceDocumentPath = $script
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$result = [ordered]@{
    bridgeProjectPath = $bridgeProjectPath
    bridgeManifestPath = $manifestPath
    generatedAssemblyPath = $generatedAssemblyPath
    pdbPath = $pdbPath
    debugMapPath = $debugMap.Path
    readyFilePath = [System.IO.Path]::ChangeExtension($manifestPath, ".ready")
    goFilePath = [System.IO.Path]::ChangeExtension($manifestPath, ".go")
    hostfxrPath = $hostFxr
    bridgeAssemblyPath = $bridgeAssemblyPath
    bridgeRuntimeConfigPath = $bridgeRuntimeConfigPath
    bridgeTypeName = "ScriptLab.GeneratedBridge.NativeHostBridge, bridge"
    behaviorId = $debugMap.Map.behaviorId
}

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    Write-Host "ScriptLab engine host bridge prepared."
    Write-Host "  BridgeProjectPath: $($result.bridgeProjectPath)"
    Write-Host "  BridgeManifestPath: $($result.bridgeManifestPath)"
    Write-Host "  ReadyFilePath: $($result.readyFilePath)"
    Write-Host "  GoFilePath: $($result.goFilePath)"
    Write-Host "  BehaviorId: $($result.behaviorId)"
}
