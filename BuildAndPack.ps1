param(
    [parameter(Position=0)]
    [string] $VersionSuffix,
    [parameter(Position=1)]
    [string] $GitCommitId,    
    [parameter(Position=2)]
    [string[]] $MsBuildArgs
)

if (-not $semver)
{
    set-variable -name semver -scope global -value (get-content .\semver.txt)
}

if ($VersionSuffix)
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
if (-not $MsBuildArgs)
{
    $MsBuildArgs = @()
}

$MsBuildArgs += "/m", "/nologo"

$buildArgs =
    "/t:Build",
    "/p:Configuration=Release",
    "/p:GitCommitId=$GitCommitId",
    "/p:PdbGitEnabled=true;PdbGitSkipVerify=true;PdbGitGitRemoteUrl=https://raw.githubusercontent.com/StackExchange/StackExchange.Precompilation"
& "msbuild" $($buildArgs + $MsBuildArgs)

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

new-item tools -type directory -force -ea stop
get-childitem -file -recurse -include ("StackExchange.Precompiler.*") |
     where { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test" } |
     select -ExpandProperty FullName |
     copy-item -destination ((get-item -Path ".\" -verbose).FullName + "\tools\") -verbose -ea stop


# make sure we can compile with the assemblies from the tools dir

$testBuildArgs =
    "/t:Rebuild",
    "/p:SECompilerPath=..\tools",
    "/p:Configuration=Debug"
& "msbuild" $($testBuildArgs + $MsBuildArgs)
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { .nuget\nuget pack $_.FullName -version $version -o packages\obj } | 
    out-null
