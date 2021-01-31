using CodeSandbox.Orchestrator.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace CodeSandbox.Orchestrator.Services
{
    public class ContainerInstanceClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _subscriptionId;
        private readonly string _resourceGroup;
        private readonly string _location;
        private readonly string _imageName;
        private readonly string _imageRegistryServer;
        private readonly string _imageRegistryUsername;
        private readonly string _imageRegistryPassword;
        private readonly ServiceAccessTokenProvider _serviceAccessTokenProvider;

        public ContainerInstanceClient(
            HttpClient httpClient,
            IConfiguration configuration,
            ServiceAccessTokenProvider serviceAccessTokenProvider)
        {
            _httpClient = httpClient;
            _subscriptionId = configuration["SubscriptionId"];
            _resourceGroup = configuration["ResourceGroup"];
            _location = configuration["Location"];
            _imageName = configuration["ImageName"];
            _imageRegistryServer = configuration["ImageRegistryServer"];
            _imageRegistryUsername = configuration["ImageRegistryUsername"];
            _imageRegistryPassword = configuration["ImageRegistryPassword"];
            _serviceAccessTokenProvider = serviceAccessTokenProvider;
        }

        public async Task StartContainer(
            string containerGroupName,
            string containerName,
            string sasUrl)
        {
            var url = GetUrl(containerGroupName);
            var json = JsonConvert.SerializeObject(new
            {
                location = _location,
                properties = new
                {
                    containers = new[]
                    {
                        new
                        {
                            name = containerName,
                            properties = new
                            {
                                image = _imageName,
                                resources = new
                                {
                                    requests = new
                                    {
                                        cpu = 1,
                                        memoryInGB = 1.5
                                    }
                                },
                                environmentVariables = new[]
                                {
                                    new
                                    {
                                        name = "BLOBURI",
                                        secureValue = sasUrl
                                    }
                                },
                                command = new[]
                                {
                                    "/bin/sh", "-c", "/home/run.sh $BLOBURI"
                                }
                            }
                        }
                    },
                    restartPolicy = "Never",
                    osType = "Linux",
                    imageRegistryCredentials = new[]
                    {
                        new
                        {
                            server = _imageRegistryServer,
                            username = _imageRegistryUsername,
                            password = _imageRegistryPassword
                        }
                    }
                }
            });
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await AuthenticateRequest(request);
            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw new Exception("Something went wrong creating container instance: " + (int)response.StatusCode);
            }
        }

        public async Task<ContainerStatus> GetContainerStatus(
            string containerGroupName)
        {
            var url = GetUrl(containerGroupName);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            await AuthenticateRequest(request);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Unexpected response from status: {(int)response.StatusCode} {responseContent}");
            }

            var containerGroup = (JObject)JsonConvert.DeserializeObject(responseContent);
            var properties = (JObject)containerGroup.GetValue("properties");
            var instanceView = (JObject)properties.GetValue("instanceView");
            var state = instanceView.GetValue("state")?.Value<string>() ?? "Pending";

            return state switch
            {
                "Pending" => ContainerStatus.Running,
                "Running" => ContainerStatus.Running,
                "Succeeded" => ContainerStatus.Succeeded,
                _ => ContainerStatus.Failed
            };
        }

        public async Task<string> GetContainerLogs(
            string containerGroupName,
            string containerName)
        {
            var url = GetUrl(containerGroupName, containerName);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            await AuthenticateRequest(request);

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            var logsResults = (JObject)JsonConvert.DeserializeObject(responseContent);
            return logsResults.GetValue("content").Value<string>();
        }

        public async Task DeleteContainer(string containerGroupName)
        {
            var url = GetUrl(containerGroupName);
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            await AuthenticateRequest(request);
            await _httpClient.SendAsync(request);
        }

        private async Task AuthenticateRequest(HttpRequestMessage request)
        {
            var accessToken = await _serviceAccessTokenProvider.GetAzureManagementApiAccessTokenAsync();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        private string GetUrl(string containerGroupName)
            => $"https://management.azure.com/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroup}/providers/Microsoft.ContainerInstance/containerGroups/{containerGroupName}?api-version=2019-12-01";
        private string GetUrl(string containerGroupName, string containerName)
            => $"https://management.azure.com/subscriptions/{_subscriptionId}/resourceGroups/{_resourceGroup}/providers/Microsoft.ContainerInstance/containerGroups/{containerGroupName}/containers/{containerName}/logs?api-version=2019-12-01";
    }
}
