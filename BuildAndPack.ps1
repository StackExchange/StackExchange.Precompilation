msbuild /m /p:Configuration=Release /v:q /nologo
new-item tools -type directory -force

get-childitem -file -recurse -include ("*.dll","*.exe","*.pdb","*.exe.config") | 
    where { $_.Directory -match "bin\\Release" -and $_.Directory -notmatch "Test" -and $_.Directory -notmatch "StackExchange.Precompilation" } |
    foreach { ($_).CopyTo((get-item -Path ".\" -verbose).FullName + "\tools\" + $_.name, $true) }

$date = Get-Date
$version = "$($date.Year).$($date.Month).$($date.Day).$(($date.Hour * 60 + $date.Minute) * 60 + $date.Second)-alpha"
new-item packages\obj -type directory -force
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { nuget pack $_.FullName -version $version -o packages\obj }
