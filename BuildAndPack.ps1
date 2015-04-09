msbuild /m /p:Configuration=Release /v:q /nologo
new-item tools -type directory -force

get-childitem -file -recurse -include ("*.dll","*.exe","*.pdb","*.exe.config") | 
    where { $_.Directory -match "bin\\Release" -and $_.Directory -notmatch "Test" -and $_.Directory -notmatch "StackExchange.Precompilation" } |
    foreach { ($_).CopyTo((get-item -Path ".\" -verbose).FullName + "\tools\" + $_.name, $true) }

new-item packages\obj -type directory -force
get-childitem *.nuspec -Recurse | 
    where { $_.FullName -notmatch '\\packages\\' } | 
    foreach { nuget pack $_.FullName -version 0.0-beta -o packages/obj -tool }
