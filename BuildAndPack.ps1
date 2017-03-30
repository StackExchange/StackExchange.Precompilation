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

$defaultArgs = "/m", "/nologo", "/p:SolutionDir=$((Resolve-Path .))\"
if ($MsBuildArgs)
{
    $defaultArgs += $MsBuildArgs
}


$buildArgs =
    ".\StackExchange.Precompilation.Tests\StackExchange.Precompilation.Tests.csproj",
    "/t:Build",
    "/p:Configuration=Release",
    "/p:GitCommitId=$GitCommitId",
    "/p:PdbGitEnabled=true;PdbGitSkipVerify=true;PdbGitGitRemoteUrl=https://raw.githubusercontent.com/StackExchange/StackExchange.Precompilation"
& "msbuild" $($buildArgs + $defaultArgs)

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

new-item tools -type directory -force -ea stop | out-null
get-childitem -file -recurse -include ("StackExchange.Precompiler.*") |
     where { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test" } |
     select -ExpandProperty FullName |
     copy-item -destination ((get-item -Path ".\" -verbose).FullName + "\tools\") -verbose -ea stop


# make sure we can compile with the assemblies from the tools dir
"Test.WebApp", "Test.ConsoleApp" | foreach-object{
    Write-Host "Testing $_"
    $testBuildArgs =
        "$_\$_.csproj",
        "/t:Build",
        "/v:q",
        "/p:SECompilerPath=..\tools",
        "/p:Configuration=Release"
    & "msbuild" $($testBuildArgs + $defaultArgs)

    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Write-Host "Packing"
new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { .nuget\nuget pack $_.FullName -version $version -o packages\obj } | 
    out-null
