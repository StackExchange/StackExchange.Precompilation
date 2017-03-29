param(
    [parameter(Position=0)]
    [string] $VersionSuffix
)

msbuild /m /p:Configuration=Release /v:q /nologo

.\PrepareForPack.ps1
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

# make sure we can compile with the assemblies from the tools dir
msbuild /m /t:Rebuild /p:Configuration=Debug /p:SECompilerPath=..\tools /v:q /nologo

new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { .nuget\nuget pack $_.FullName -version $version -o packages\obj } | 
    out-null
