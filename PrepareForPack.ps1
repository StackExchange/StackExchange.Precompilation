set -name semver -scope global -value (get-content .\semver.txt)

write-host "##teamcity[setParameter name='semver' value='$semver']"

new-item tools -type directory -force | out-null

get-childitem -file -recurse -include ("*.dll","*.exe","*.pdb","*.exe.config") |
     where { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test|StackExchange.Precompiler" } |
     foreach { copy-item ($_).FullName ((get-item -Path ".\" -verbose).FullName + "\tools\" + $_.name) -Force }