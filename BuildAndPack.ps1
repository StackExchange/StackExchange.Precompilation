msbuild /m /p:Configuration=Release /v:q /nologo

.\PrepareForPack.ps1

$epoch = [math]::truncate((new-timespan -start (get-date -date "01/01/1970") -end (get-date)).TotalSeconds)
$version = "$semver-alpha$epoch"
new-item .\packages\obj -type directory -force | out-null
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { nuget pack $_.FullName -version $version -o packages\obj } | 
    out-null