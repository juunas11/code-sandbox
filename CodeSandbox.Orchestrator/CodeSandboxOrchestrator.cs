using System;
using System.Net.Http;
using System.Threading.Tasks;
using CodeSandbox.Orchestrator.Models;
using CodeSandbox.Orchestrator.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace CodeSandbox.Orchestrator
{
    public class CodeSandboxOrchestrator
    {
        // Function names
        private const string FunctionNameBase = nameof(CodeSandboxOrchestrator);
        private const string OrchestratorName = FunctionNameBase;
        private const string HttpStarterName = FunctionNameBase + "_HttpStart";
        private const string StartContainerActivityName = FunctionNameBase + "_StartContainer";
        private const string WaitForContainerDoneActivityName = FunctionNameBase + "_WaitForContainerDone";
        private const string GetContainerLogsActivityName = FunctionNameBase + "_GetContainerLogs";
        private const string DeleteContainerActivityName = FunctionNameBase + "_DeleteContainer";
        private const string DeleteBlobActivityName = FunctionNameBase + "_DeleteBlob";

        // 20 checks at max with 5 seconds in between = around 95 seconds max time
        private const int StatusCheckAmount = 20;
        private static readonly TimeSpan StatusCheckRetryInterval = TimeSpan.FromSeconds(5);

        private readonly ContainerInstanceClient _containerInstanceClient;
        private readonly BlobStorageClient _blobStorageClient;

        public CodeSandboxOrchestrator(
            ContainerInstanceClient containerInstanceClient,
            BlobStorageClient blobStorageClient)
        {
            _containerInstanceClient = containerInstanceClient;
            _blobStorageClient = blobStorageClient;
        }

        [FunctionName(HttpStarterName)]
        public async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            await using var stream = await req.Content.ReadAsStreamAsync();
            var (containerName, blobName, sasUrl) = await _blobStorageClient.Upload(stream);

            string instanceId = await starter.StartNewAsync(OrchestratorName, new OrchestratorInput
            {
                SasUrl = sasUrl,
                ContainerName = containerName,
                BlobName = blobName
            });

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(OrchestratorName)]
        public async Task<OrchestratorOutput> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var input = context.GetInput<OrchestratorInput>();

            var containerName = $"csharp-{context.NewGuid()}";
            var containerGroupName = containerName;

            await context.CallActivityAsync(
                StartContainerActivityName,
                new StartContainerInput
                {
                    SasUrl = input.SasUrl,
                    ContainerGroupName = containerGroupName,
                    ContainerName = containerName
                });

            try
            {
                ContainerStatus status = await context.CallActivityAsync<ContainerStatus>(
                    WaitForContainerDoneActivityName,
                    containerGroupName);

                var logs = await context.CallActivityAsync<string>(
                    GetContainerLogsActivityName,
                    new GetContainerLogsInput
                    {
                        ContainerGroupName = containerGroupName,
                        ContainerName = containerName
                    });
                return new OrchestratorOutput
                {
                    Result = status == ContainerStatus.Running ? "TimedOut" : status.ToString(),
                    Logs = logs
                };
            }
            finally
            {
                await context.CallActivityAsync(
                    DeleteContainerActivityName,
                    containerGroupName);
                await context.CallActivityAsync(
                    DeleteBlobActivityName,
                    new DeleteBlobInput
                    {
                        ContainerName = input.ContainerName,
                        BlobName = input.BlobName
                    });
            }
        }

        [FunctionName(StartContainerActivityName)]
        public async Task StartContainer(
            [ActivityTrigger] StartContainerInput input,
            ILogger logger)
        {
            await _containerInstanceClient.StartContainer(
                input.ContainerGroupName,
                input.ContainerName,
                input.SasUrl);
            logger.LogInformation("Container {ContainerName} started", input.ContainerName);
        }

        [FunctionName(WaitForContainerDoneActivityName)]
        public async Task<ContainerStatus> WaitForContainerDone(
            [ActivityTrigger] string containerGroupName,
            ILogger logger)
        {
            ContainerStatus status = ContainerStatus.Running;
            for (int i = 0; i < StatusCheckAmount; i++)
            {
                try
                {
                    status = await _containerInstanceClient.GetContainerStatus(
                                containerGroupName);
                    logger.LogInformation("Container group {Group} status {Status}", containerGroupName, status.ToString());
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to get status for container group {Group}", containerGroupName);
                    status = ContainerStatus.Running;
                }

                if (status != ContainerStatus.Running)
                {
                    break;
                }

                await Task.Delay(StatusCheckRetryInterval);
            }

            if (status == ContainerStatus.Running)
            {
                logger.LogError("Container group {Group} timed out", containerGroupName);
            }

            return status;
        }

        [FunctionName(GetContainerLogsActivityName)]
        public async Task<string> GetContainerLogs(
            [ActivityTrigger] GetContainerLogsInput input,
            ILogger logger)
        {
            try
            {
                var logs = await _containerInstanceClient.GetContainerLogs(
                    input.ContainerGroupName,
                    input.ContainerName);
                logger.LogInformation("Got logs for container {Container}, {LogCharacters} chars", input.ContainerName, logs.Length);
                return logs;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to get logs");
                return "";
            }
        }

        [FunctionName(DeleteContainerActivityName)]
        public async Task DeleteContainer(
            [ActivityTrigger] string containerGroupName,
            ILogger logger)
        {
            await _containerInstanceClient.DeleteContainer(containerGroupName);
            logger.LogInformation("Container group {Group} deleted", containerGroupName);
        }

        [FunctionName(DeleteBlobActivityName)]
        public async Task DeleteBlob(
            [ActivityTrigger] DeleteBlobInput input,
            ILogger logger)
        {
            await _blobStorageClient.Delete(input.ContainerName, input.BlobName);
            logger.LogInformation("Blob {Container}/{Blob} deleted", input.ContainerName, input.BlobName);
        }
    }
}