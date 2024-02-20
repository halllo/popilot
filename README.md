# popilot
Navigating the PO experience on Azure DevOps.

## Getting started
Set up a connection to Azure DevOps by providing tenant Id, client Id, and OpenAI key in the `appsettings.json`. In order to get the client Id, you have to register an application in AAD and grant it access to the Azure DevOps resource.

Afterwards you can run the following commands:
```bash
popilot get-prio -p "MyProject" -t "MyTeam" -d
popilot get-current-sprint -p "MyProject" -t "MyTeam"
popilot get-pipelines -p "MyProject" --path "\folder"
popilot get-pipeline 50
popilot get-releasenotes 1337 -d
```
