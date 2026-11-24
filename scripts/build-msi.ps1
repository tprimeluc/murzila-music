$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path $root 'artifacts\msi-stage'
$source = Join-Path $root 'src\MurzilaMusic.App\bin\Release\net9.0-windows\win-x64'
$out = Join-Path $root 'artifacts\MurzilaMusic-3.6.0-win-x64.msi'
$wxs = Join-Path $root 'installer\Product.generated.wxs'

dotnet build (Join-Path $root 'src\MurzilaMusic.App\MurzilaMusic.App.csproj') -c Release -v q --nologo

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $stage -Recurse -Force

function Safe-Id([string]$text) {
  $s = ($text -replace '[^A-Za-z0-9]', '_')
  if ($s -match '^[0-9]') { $s = 'D' + $s }
  return $s
}

$files = Get-ChildItem $stage -Recurse -File
$tree = @{}
$dirMap = @{}
foreach ($f in $files) {
  $relDir = $f.DirectoryName.Substring($stage.Length).TrimStart('\')
  if ([string]::IsNullOrEmpty($relDir)) { continue }
  $node = $tree
  $path = ''
  foreach ($part in ($relDir -split '\\')) {
    $path = if ($path) { $path + '\' + $part } else { $part }
    if (-not $node.ContainsKey($part)) { $node[$part] = @{} }
    $dirMap[$path] = 'Dir_' + (Safe-Id $path)
    $node = $node[$part]
  }
}

$dirXml = New-Object System.Text.StringBuilder
function Emit-Dir([hashtable]$node, [string]$prefix, [System.Text.StringBuilder]$sb) {
  foreach ($name in ($node.Keys | Sort-Object)) {
    $path = if ($prefix) { $prefix + '\' + $name } else { $name }
    $id = 'Dir_' + (Safe-Id $path)
    [void]$sb.AppendLine("        <Directory Id=`"$id`" Name=`"$name`">")
    Emit-Dir $node[$name] $path $sb
    [void]$sb.AppendLine('        </Directory>')
  }
}
Emit-Dir $tree '' $dirXml

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Package Name="Murzila Music" Manufacturer="MursikT7 Corporation" Version="3.6.0" UpgradeCode="6D2B4E5A-8C31-4F2A-9E77-0A1B2C3D4E5F" Language="1049" Codepage="65001" InstallerVersion="200" Scope="perUserOrMachine" Compressed="yes">')
[void]$sb.AppendLine('    <MediaTemplate EmbedCab="yes" />')
[void]$sb.AppendLine('    <Icon Id="AppIcon" SourceFile="$(var.SourceDir)\Assets\murzila.ico" />')
[void]$sb.AppendLine('    <StandardDirectory Id="ProgramFilesFolder">')
[void]$sb.AppendLine('      <Directory Id="INSTALLFOLDER" Name="Murzila Music">')
[void]$sb.Append($dirXml.ToString())
[void]$sb.AppendLine('      </Directory>')
[void]$sb.AppendLine('    </StandardDirectory>')
[void]$sb.AppendLine('    <StandardDirectory Id="ProgramMenuFolder">')
[void]$sb.AppendLine('      <Directory Id="MurzilaMenu" Name="Murzila Music" />')
[void]$sb.AppendLine('    </StandardDirectory>')
[void]$sb.AppendLine('    <ComponentGroup Id="AppFiles">')
foreach ($f in $files) {
  $rel = $f.FullName.Substring($stage.Length).TrimStart('\')
  $relDir = $f.DirectoryName.Substring($stage.Length).TrimStart('\')
  $dirId = if ([string]::IsNullOrEmpty($relDir)) { 'INSTALLFOLDER' } else { $dirMap[$relDir] }
  $safe = Safe-Id $rel
  $src = '$(var.SourceDir)\' + $rel
  if ($f.Name -eq 'MurzilaMusic.App.exe') {
    [void]$sb.AppendLine("      <Component Id=`"C_$safe`" Directory=`"$dirId`" Guid=`"*`"><File Id=`"F_$safe`" Source=`"$src`" KeyPath=`"yes`"><Shortcut Id=`"DesktopShortcut`" Directory=`"DesktopFolder`" Name=`"Murzila Music`" WorkingDirectory=`"INSTALLFOLDER`" Advertise=`"no`" Icon=`"AppIcon`" /><Shortcut Id=`"StartMenuShortcut`" Directory=`"MurzilaMenu`" Name=`"Murzila Music`" WorkingDirectory=`"INSTALLFOLDER`" Advertise=`"no`" Icon=`"AppIcon`" /></File></Component>")
  } else {
    [void]$sb.AppendLine("      <Component Id=`"C_$safe`" Directory=`"$dirId`" Guid=`"*`"><File Id=`"F_$safe`" Source=`"$src`" KeyPath=`"yes`" /></Component>")
  }
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('    <Feature Id="MainFeature" Title="Murzila Music" Level="1" ConfigurableDirectory="INSTALLFOLDER"><ComponentGroupRef Id="AppFiles" /></Feature>')
[void]$sb.AppendLine('  </Package>')
[void]$sb.AppendLine('</Wix>')
[System.IO.File]::WriteAllText($wxs, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))

wix build $wxs -d "SourceDir=$stage" -o $out -nologo
Copy-Item $out (Join-Path $root 'src\MurzilaMusic.Server\wwwroot\downloads\MurzilaMusic-3.6.0-win-x64.msi') -Force
Write-Host "MSI: $out"
