#Requires -Version 3.0
Set-ExecutionPolicy Unrestricted -Scope CurrentUser

Write-Host "========================================"
Write-Host "         Emby Windows Builder"
Write-Host "========================================"
Write-Host ""

function Get-Tree($Path,$Include='*') { 
    @(Get-Item $Path -Include $Include) + 
        (Get-ChildItem $Path -Recurse -Include $Include) | 
        sort pspath -Descending -unique
} 

function Remove-Tree($Path,$Include='*') { 
    Get-Tree $Path $Include | Remove-Item -force -recurse
} 

# http://invokemsbuild.codeplex.com/documentation?referringTitle=Home
Import-Module -Name "$PSScriptRoot\Invoke-MsBuild.psm1"

$DeployPath = "$PSScriptRoot\..\..\..\Deploy"
$DeployPathServerPath = "$DeployPath\Server"
$DeployPathSystemFolderName = "System"
$DeployPathSystemPath = "$DeployPathServerPath\$DeployPathSystemFolderName"
$7za = "$PSScriptRoot\..\..\ThirdParty\7zip\7za.exe"
$7zaOptions = "a -mx9"

$WindowsBinReleasePath = "$PSScriptRoot\..\..\MediaBrowser.ServerApplication\bin\Release"

Write-Host "Building Windows version..."
$buildSucceeded = Invoke-MsBuild -Path "$PSScriptRoot\..\..\MediaBrowser.sln" -MsBuildParameters "/target:Clean;Build /property:Configuration=Release;Platform=""x64"" /verbosity:Quiet" -BuildLogDirectoryPath "$PSScriptRoot" 

if ($buildSucceeded)
{
    Write-Host "Windows Build completed successfully."
}
else
{
    Write-Host "Windows Build failed. Check the build log file for errors."
    Exit
}

Write-Host "Deleting $DeployPathServerPath"
Remove-Tree $DeployPathServerPath
Write-Host "Copying $WindowsBinReleasePath to $DeployPathSystemFolderName"

Copy-Item -Path $WindowsBinReleasePath -Destination $DeployPathSystemPath -Recurse

Remove-Tree $DeployPathServerPath "*.pdb"
Remove-Tree $DeployPathServerPath "*.xml"

$process = (Start-Process -FilePath $7za -ArgumentList "$7zaOptions ""$DeployPath\emby.windows.zip"" ""$DeployPathServerPath\*""" -Wait -Passthru -NoNewWindow -RedirectStandardOutput "$PSScriptRoot\7za.log" -RedirectStandardError "$PSScriptRoot\7zaError.log").ExitCode

if ($process -ne 0)
{
    Write-Host "Creating archive failed."
    Exit
}

$WindowsReleaseVersion = ((Get-Command "$WindowsBinReleasePath\MediaBrowser.ServerApplication.exe").FileVersionInfo).FileVersion
Write-Host "Windows Release: Copy archive to MBServer_$WindowsReleaseVersion.zip..."
Copy-Item -Path "$DeployPath\emby.windows.zip" -Destination "$DeployPath\MBServer_$WindowsReleaseVersion.zip" -Force


Write-Host "Done"