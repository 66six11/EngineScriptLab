param(
    [string]$ProjectRoot,
    [string]$DotnetPath,
    [string]$DapAdapterPath,
    [string]$ScriptPath = "Samples\PlayerMove.ash.cs",
    [string]$OutputDirectory = "bin\ScriptDebug\RealEngineProbe",
    [Parameter(Mandatory = $true)]
    [string]$BridgeManifestPath,
    [string]$EnginePackageRoot,
    [int]$HostProcessId = 0,
    [int]$BreakpointLine = 14,
    [int]$BreakpointColumn = 13,
    [int]$EntityId = 101,
    [int]$TimeoutMilliseconds = 5000,
    [string]$GoFilePath,
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$ContinueAfterStop,
    [switch]$WaitForExit
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

function Resolve-DefaultDapAdapterPath {
    if ($DapAdapterPath) {
        return $DapAdapterPath
    }

    if ($env:SCRIPTLAB_DAP_ADAPTER) {
        return $env:SCRIPTLAB_DAP_ADAPTER
    }

    $wingetNetcoredbg = "C:\Users\C66\AppData\Local\Microsoft\WinGet\Packages\Samsung.NetCoreDbg_Microsoft.Winget.Source_8wekyb3d8bbwe\netcoredbg\netcoredbg.exe"
    if (Test-Path -LiteralPath $wingetNetcoredbg) {
        return $wingetNetcoredbg
    }

    throw "Set -DapAdapterPath or SCRIPTLAB_DAP_ADAPTER to a netcoredbg executable."
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

function Quote-ProcessArgument([string]$Argument) {
    if ($null -eq $Argument -or $Argument.Length -eq 0) {
        return '""'
    }

    if ($Argument -notmatch '[\s"]') {
        return $Argument
    }

    return '"' + ($Argument -replace '"', '\"') + '"'
}

function New-JsonParams([hashtable]$Pairs) {
    $params = [ordered]@{}
    foreach ($key in $Pairs.Keys) {
        $value = $Pairs[$key]
        if ($null -ne $value -and -not ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) {
            $params[$key] = $value
        }
    }

    return $params
}

function Send-JsonRpcRequest([System.Diagnostics.Process]$Server, [string]$Method, [object]$Params) {
    $id = $script:NextRequestId
    $script:NextRequestId++

    $request = [ordered]@{
        jsonrpc = "2.0"
        id = $id
        method = $Method
        params = $Params
    }
    $json = $request | ConvertTo-Json -Depth 32 -Compress

    Write-Host "-> $Method (#$id)"
    $Server.StandardInput.WriteLine($json)
    $Server.StandardInput.Flush()

    while ($true) {
        $line = $Server.StandardOutput.ReadLine()
        if ($null -eq $line) {
            throw "ScriptLab server exited before responding to $Method."
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $response = $line | ConvertFrom-Json
        }
        catch {
            Write-Warning "Ignoring non-JSON server output: $line"
            continue
        }

        if ($response.PSObject.Properties.Name -contains "error" -and $null -ne $response.error) {
            throw "JSON-RPC $Method failed: $($response.error.message)"
        }

        return $response
    }
}

function Get-CollectionCount($Value) {
    if ($null -eq $Value) {
        return 0
    }

    if ($Value -is [System.Array]) {
        return $Value.Count
    }

    return 1
}

function Format-VariableLine($Variable) {
    $type = ""
    if ($Variable.type) {
        $type = " : $($Variable.type)"
    }

    $children = ""
    if ($Variable.variablesReference) {
        $children = " children=$($Variable.variablesReference)"
    }

    return "  $($Variable.name)$type = $($Variable.displayValue)$children"
}

$projectRootPath = Resolve-DefaultProjectRoot
$dotnet = Resolve-DefaultDotnetPath
$dapAdapter = Resolve-DefaultDapAdapterPath
$script = Resolve-FullPath $ScriptPath $projectRootPath
$emitDirectory = Resolve-FullPath $OutputDirectory $projectRootPath
$manifest = Resolve-FullPath $BridgeManifestPath (Get-Location).Path
$goFile = $null
if ($GoFilePath) {
    $goFile = Resolve-FullPath $GoFilePath (Get-Location).Path
}

$projectPath = Join-Path $projectRootPath "ScriptLab.csproj"
$serverAssembly = Join-Path $projectRootPath "bin\$Configuration\net10.0\ScriptLab.dll"

Assert-ExistingFile $projectPath "ScriptLab project"
Assert-ExistingFile $script "Script file"
Assert-ExistingFile $manifest "Bridge manifest"
Assert-ExecutableIfQualified $dotnet "dotnet executable"
Assert-ExistingFile $dapAdapter "DAP adapter"

if (-not $SkipBuild) {
    Write-Host "Building ScriptLab server..."
    & $dotnet build $projectPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}

Assert-ExistingFile $serverAssembly "ScriptLab server assembly"

$dotnetDirectory = $null
if ([System.IO.Path]::IsPathRooted($dotnet)) {
    $dotnetDirectory = Split-Path -Parent $dotnet
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $dotnet
$startInfo.Arguments = (Quote-ProcessArgument $serverAssembly) + " server " + (Quote-ProcessArgument "--dap-adapter=$dapAdapter")
$startInfo.WorkingDirectory = $projectRootPath
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $false
$startInfo.CreateNoWindow = $true

if ($dotnetDirectory) {
    $startInfo.EnvironmentVariables["DOTNET_ROOT"] = $dotnetDirectory
    $startInfo.EnvironmentVariables["DOTNET_ROOT_X64"] = $dotnetDirectory
    $startInfo.EnvironmentVariables["PATH"] = $dotnetDirectory + [System.IO.Path]::PathSeparator + $startInfo.EnvironmentVariables["PATH"]
}

$server = New-Object System.Diagnostics.Process
$server.StartInfo = $startInfo

Write-Host "Starting ScriptLab JSON-RPC server..."
[void]$server.Start()
$script:NextRequestId = 1

try {
    Send-JsonRpcRequest $server "loadGraph" (New-JsonParams @{
        scriptPath = $script
        outputDirectory = $emitDirectory
    }) | Out-Null

    Send-JsonRpcRequest $server "setSourceBreakpoints" (New-JsonParams @{
        sourcePath = $script
        breakpoints = @(@{
            line = $BreakpointLine
            column = $BreakpointColumn
        })
    }) | Out-Null

    $validation = Send-JsonRpcRequest $server "validateDebugHost" (New-JsonParams @{
        hostKind = "cppClr"
        bridgeManifestPath = $manifest
        enginePackageRoot = $EnginePackageRoot
    })
    Write-Host "Validated bridge manifest."
    Write-Host "  generatedAssemblyPath: $($validation.result.generatedAssemblyPath)"
    Write-Host "  debugMapPath: $($validation.result.debugMapPath)"

    if ($HostProcessId -le 0) {
        $hostProcessText = Read-Host "Start the engine host, wait for its ready barrier, then enter process id"
        $HostProcessId = [int]$hostProcessText
    }

    Send-JsonRpcRequest $server "registerDebugHost" (New-JsonParams @{
        processId = $HostProcessId
        hostKind = "cppClr"
        bridgeManifestPath = $manifest
        enginePackageRoot = $EnginePackageRoot
    }) | Out-Null

    $attach = Send-JsonRpcRequest $server "attachDebugHost" (New-JsonParams @{})
    Write-Host "Attach status: $($attach.result.status)"
    if ($attach.result.status -ne "attached") {
        throw "attachDebugHost returned status '$($attach.result.status)'."
    }

    $breakpointDrain = Send-JsonRpcRequest $server "drainDebugEvents" (New-JsonParams @{
        timeoutMilliseconds = 1000
        entityId = $EntityId
    })
    $breakpointEventCount = Get-CollectionCount $breakpointDrain.result.breakpointEvents
    if ($breakpointEventCount -gt 0) {
        Write-Host "Observed $breakpointEventCount breakpoint event(s)."
    }

    if ($goFile) {
        $goDirectory = Split-Path -Parent $goFile
        if ($goDirectory -and -not (Test-Path -LiteralPath $goDirectory)) {
            [void](New-Item -ItemType Directory -Path $goDirectory)
        }

        Set-Content -LiteralPath $goFile -Value "go" -Encoding UTF8
        Write-Host "Wrote go file: $goFile"
    }
    else {
        [void](Read-Host "Release the engine host now, then press Enter to wait for the script breakpoint")
    }

    $drain = Send-JsonRpcRequest $server "drainDebugEvents" (New-JsonParams @{
        timeoutMilliseconds = $TimeoutMilliseconds
        entityId = $EntityId
    })
    if ($null -eq $drain.result.stoppedEvent) {
        throw "No stopped event arrived within $TimeoutMilliseconds ms."
    }

    $stopped = $drain.result.stoppedEvent
    Write-Host "Stopped at $($stopped.sourcePath):$($stopped.line):$($stopped.column)"
    Write-Host "  status: $($stopped.status)"
    Write-Host "  threadId: $($stopped.threadId)"
    Write-Host "  debugStateId: $($drain.result.debugStateId)"

    $variables = Send-JsonRpcRequest $server "readVariables" (New-JsonParams @{
        debugStateId = $drain.result.debugStateId
        count = 20
    })
    Write-Host "Variables: $($variables.result.status), total=$($variables.result.totalCount)"
    foreach ($variable in @($variables.result.variables)) {
        if ($null -ne $variable) {
            Write-Host (Format-VariableLine $variable)
        }
    }

    $shouldContinue = $ContinueAfterStop.IsPresent
    if (-not $shouldContinue) {
        $answer = Read-Host "Send continue to the stopped thread? [Y/n]"
        $shouldContinue = [string]::IsNullOrWhiteSpace($answer) -or $answer -match '^[Yy]'
    }

    if ($shouldContinue) {
        Send-JsonRpcRequest $server "continue" (New-JsonParams @{
            threadId = $stopped.threadId
        }) | Out-Null
        Write-Host "Continue sent."
    }

    if ($WaitForExit) {
        $exit = Send-JsonRpcRequest $server "waitDebugHostExit" (New-JsonParams @{
            timeoutMilliseconds = $TimeoutMilliseconds
        })
        Write-Host "Host exit status: $($exit.result.status)"
    }

    Send-JsonRpcRequest $server "disconnectDebugHost" (New-JsonParams @{
        terminateDebuggee = $false
    }) | Out-Null
    Write-Host "Disconnected."
}
finally {
    if ($null -ne $server -and -not $server.HasExited) {
        try {
            $server.StandardInput.Close()
        }
        catch {
        }

        if (-not $server.WaitForExit(2000)) {
            $server.Kill()
        }
    }
}
