param(
    [parameter(Position=0)]
    [string] $VersionSuffix,
    [parameter(Position=0)]
    [string] $GitCommitId
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

msbuild /m /p:Configuration=Release /v:q /nologo "/p:GitCommitId=$GitCommitId" "/p:PdbGitEnabled=true;PdbGitSkipVerify=true;PdbGitGitRemoteUrl=https://raw.githubusercontent.com/StackExchange/StackExchange.Precompilation"

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
