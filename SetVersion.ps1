$timestamp = Get-Date -Format "yyyy.MM.dd.HHmmss"

$csprojPath = "$PSScriptRoot\NcsScheduler\NcsScheduler.csproj"
$xml = [xml](Get-Content $csprojPath)

$ns = $xml.DocumentElement.NamespaceURI
$pg = $xml.Project.PropertyGroup | Where-Object { $_.Version -ne $null } | Select-Object -First 1

if ($pg -eq $null) {
    $pg = $xml.Project.PropertyGroup[0]
}

# Read current release number and increment
$releaseFile = "$PSScriptRoot\release.txt"
$releaseNum = 1
if (Test-Path $releaseFile) {
    $releaseNum = [int](Get-Content $releaseFile) + 1
}
Set-Content $releaseFile $releaseNum

$version = "$timestamp.$releaseNum"

# Update or create Version node
$versionNode = $pg.SelectSingleNode("Version")
if ($versionNode -eq $null) {
    $versionNode = $xml.CreateElement("Version")
    $pg.AppendChild($versionNode) | Out-Null
}
$versionNode.InnerText = $version

$xml.Save($csprojPath)
Write-Host "Version set to: $version"