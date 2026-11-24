$ErrorActionPreference='Stop'
$refs=@('GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll','GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll','GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll','GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll') | ForEach-Object { '/r:'+(Join-Path "$env:WINDIR\Microsoft.NET\assembly" $_) }
$out=Join-Path $PSScriptRoot 'bin\Tests.exe'
New-Item -ItemType Directory (Split-Path $out) -Force | Out-Null
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /platform:x64 /main:NvidiaCrashDetective.Tests "/out:$out" @refs "$PSScriptRoot\native\NativeMonitor.cs" "$PSScriptRoot\native\Tests.cs"
if($LASTEXITCODE -ne 0){throw 'Test build failed'}
& $out
if($LASTEXITCODE -ne 0){throw 'Tests failed'}
