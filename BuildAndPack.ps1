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
$defaultArgs = "/v:q", "/m", "/nologo", "/p:SolutionDir=$solutionDir", "/t:Build", "/p:Configuration=Release"
if ($MsBuildArgs)
{
    $defaultArgs += $MsBuildArgs
}

$buildArgs =
    ".\StackExchange.Precompilation.Tests\StackExchange.Precompilation.Tests.csproj",
	 "/p:PathMap=$solutionDir=https://raw.githubusercontent.com/StackExchange/StackExchange.Precompilation/$GitCommitId/"
& "msbuild" $($buildArgs + $defaultArgs)

if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

new-item tools -type directory -force -ea stop | out-null
get-childitem -file -recurse -include ("StackExchange.Precompiler.*") |
     where-object { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test" } |
     select-object -ExpandProperty FullName |
     copy-item -destination ((get-item -Path ".\" -verbose).FullName + "\tools\") -verbose -ea stop


# make sure we can compile with the assemblies from the tools dir
"Test.WebApp", "Test.ConsoleApp" | foreach-object{
    Write-Host "Testing $_"
    $testBuildArgs =
        "$_\$_.csproj",
        "/p:SECompilerPath=..\tools"
    & "msbuild" $($testBuildArgs + $defaultArgs)

    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Write-Host "Packing"
new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where-object { $_.FullName -notmatch '\\packages\\' } |
    foreach-object { .nuget\nuget pack $_.FullName -version $version -o packages\obj } |
    out-null
