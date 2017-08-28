set-variable -name semver -scope global -value (get-content .\semver.txt)

git tag -a "releases/$semver" -m "creating $semver release"

git push --tags