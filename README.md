# C# code sandbox with Azure Durable Functions + Container Instances

The idea of this small app is to take a C# file as input,
and start a container instance to run that file.
The logs from that container are then given as the result.
Something like what .NET Fiddle does.

The flow at high level:

1. HTTP POST request to the HTTP triggered function (C# file in body)
1. Function uploads the file to Azure Storage, generates a SAS token
1. Durable Function orchestration is started
1. Container instance started with the file SAS URL as input
1. Container script runs, file downloaded as Program.cs next to a csproj file already in the container
1. Container runs dotnet build and dotnet run
1. Orchestrator monitors the progress and waits for the container to terminate
1. Logs from the container are downloaded after the container has terminated or the process has taken too long
1. The container instance is deleted and the C# file in Storage is deleted
1. The logs are set as the orchestrator result

This process takes around 1 minute for a hello world C# file,
so it isn't _quite_ at .NET Fiddle's level :D

This app was made for fun and does not include e.g. the validations I might put into a production app.