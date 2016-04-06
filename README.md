# Tether Installer
This is the Installation agent for the [Tether](https://github.com/surgicalcoder/Tether) project. The agent will communicate with the SD API, and either A) provision a new machine or B) if a machine exists with the same name, grab that machine's key.

If Tether already exists in the specified download location, it will preserve the existing configuration file, and perform an upgrade.

## Requirements

You need to be running the .NET 4 framework (minimum), have a Server Density account, and an SD API Key. If you don't have an API key, you can obtain one by following [instructions on the SD API page](https://apidocs.serverdensity.com/#authentication). 

## Usage - Self Build

For this to work, you can either use the Tether AppVeyor build, or build Tether yourself and put it on a web server accessible  from the machine you are attempting to install on.

Download code, build, distribute EXE as required, then use the following parameters:

    D:\>SDInstaller.exe
    The argument 'SDAPIKey' is required
    Usage - SDInstaller <SDAPIKey> <TetherLocation> <InstallLocation> -options
    
    GlobalOptionDescription
    
    SDAPIKey* (-API)
    TetherLocation*
    InstallLocation*
    ManifestLocation
    ServerDensityPostLocation    [Default='https://{account}.agent.serverdensity.io']
    TempPath (-T)
    PreserveConfigFile           [Default='True']
    SDAccountName
    SDAgentKey
    
ServerDensityPostLocation can either be provided as a proper url, or have the placeholder of {account}, which will be automatically replaced. You can also provide overrides for AccountName, and/or AgentKeys, so if these are provided, the installation process will not contact SD's API to obtain these.

Example usage:

	SDInstaller.exe SDKeyGoesHere https://ci.appveyor.com/api/buildjobs/otc78a4jwi4pm03y/artifacts/Tether/bin/Build.zip c:\apps\Tether

## Usage - hosted on AppVeyor

You can also run this straight off AppVeyor, by using (and adapting!) this powershell script:

    $source = "((Installer build on AppVeyor))" 
    $destination = "c:\SDInstaller.exe"
    $WebClient = New-Object System.Net.WebClient
    $WebClient.DownloadFile( $source, $destination )
    Start-Process -FilePath $destination -ArgumentList '((APIKEY))','((Tether.ZIP build on AppVeyor))','((Installation Location))' -NoNewWindow -Wait
    Remove-Item $destination

This will download the Installation EXE, and run it, with parameters to install it.

## Changelog

* Request from SD team to change default postback url.