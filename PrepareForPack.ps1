set -name semver -scope global -value (get-content .\semver.txt)

write-host "##teamcity[setParameter name='semver' value='$semver']"

new-item tools -type directory -force -ea stop

get-childitem -file -recurse -include ("StackExchange.Precompiler.*") |
     where { $_.Directory -match "bin\\Release" -and $_.FullName -notmatch "Test" } |
     select -ExpandProperty FullName |
     copy-item -destination ((get-item -Path ".\" -verbose).FullName + "\tools\") -verbose -ea stop