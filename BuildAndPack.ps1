param(
    [parameter(Position=0)]
    [string] $VersionSuffix
)
set -name semver -scope global -value (get-content .\semver.txt)

$epoch = [math]::truncate((new-timespan -start (get-date -date "01/01/1970") -end (get-date)).TotalSeconds)
if ($VersionSuffix)
{
    $version = "$semver$VersionSuffix"
    Write-Host "Packing explicit version $version"
}
else
{
    $version = "$semver-local$epoch"
    Write-Host "Packing local package $version"
}

if ($env:AppVeyor)
{
    Update-AppveyorBuild -Version $version
}

write-host "##teamcity[setParameter name='semver' value='$semver']"

msbuild /m /p:Configuration=Release /v:q /nologo

new-item tools -type directory -force -ea stop

get-childitem -file -recurse -include ("StackExchange.Precompiler.*") |
     where { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test" } |
     select -ExpandProperty FullName |
     copy-item -destination ((get-item -Path ".\" -verbose).FullName + "\tools\") -verbose -ea stop


# make sure we can compile with the assemblies from the tools dir
msbuild /m /t:Rebuild /p:Configuration=Debug /p:SECompilerPath=..\tools /v:q /nologo

new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { .nuget\nuget pack $_.FullName -version $version -o packages\obj } | 
    out-null
