# popilot
Navigating the PO experience on Azure DevOps.

## Getting started
Set up a connection to Azure DevOps by providing tenant Id, client Id, and OpenAI key in the `appsettings.json`. In order to get the client Id, you have to register an application in AAD and grant it access to the Azure DevOps resource.

Afterwards you can run the following commands:
```bash
popilot.cli.exe get-prio -p "MyProject" -t "MyTeam" -d
popilot.cli.exe get-current-sprint -p "MyProject" -t "MyTeam"
popilot.cli.exe get-pipelines -p "MyProject" --path "\folder"
popilot.cli.exe get-pipeline 50
popilot.cli.exe get-releasenotes 1337 -d
```