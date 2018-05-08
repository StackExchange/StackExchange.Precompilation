param(
    [parameter(Position=0)]
    [string] $VersionSuffix,
    [parameter(Position=1)]
    [string] $GitCommitId,
    [parameter(Position=2)]
    [string[]] $MsBuildArgs,
    [switch] $CIBuild
)

if (-not $semver)
{
    set-variable -name semver -scope global -value (get-content .\semver.txt)
}

if ($VersionSuffix -or $CIBuild)
{
    $version = "$semver$VersionSuffix"
}
else
{
    $epoch = [math]::truncate((new-timespan -start (get-date -date "01/01/1970") -end (get-date)).TotalSeconds)
    $version = "$semver-local$epoch"
}

if(-not $GitCommitId)
{
    $GitCommitId = $(git rev-parse HEAD)
}

$solutionDir = "$((Resolve-Path .).Path)\"
$defaultArgs = "/v:n", "/nologo",
    "/p:SolutionDir=$solutionDir",
    "/p:RepositoryCommit=$GitCommitId",
    "/p:Version=$version",
    "/p:Configuration=Release",
    "/p:SEPrecompilerPath=$solutionDir\StackExchange.Precompilation.Build\bin\Release\net462"
if ($MsBuildArgs)
{
    $defaultArgs += $MsBuildArgs
}
& msbuild ($defaultArgs + "/t:Restore")
& msbuild ($defaultArgs + "/t:Build,Pack")

if ($LastExitCode -ne 0)
{
    throw "MSBuild failed"
}

.\Test.ConsoleApp\bin\Release\net462\Test.ConsoleApp.exe

if ($LastExitCode -ne 0)
{
    throw "Test.ConsoleApp failed to run"
}
