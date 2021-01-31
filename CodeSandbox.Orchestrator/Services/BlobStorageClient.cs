using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CodeSandbox.Orchestrator.Services
{
    public class BlobStorageClient
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _containerName;

        public BlobStorageClient(
            BlobServiceClient blobServiceClient,
            IConfiguration configuration)
        {
            _blobServiceClient = blobServiceClient;
            _containerName = configuration["StorageContainer"];
        }

        public async Task<(string containerName, string blobName, string sasUrl)> Upload(
            Stream stream)
        {
            var blobName = $"{Guid.NewGuid()}.cs";
            var container = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blob = container.GetBlobClient(blobName);
            await blob.UploadAsync(stream);

            var sasUrl = blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(30));

            return (_containerName, blobName, sasUrl.ToString());
        }

        public async Task Delete(string containerName, string blobName)
        {
            var container = _blobServiceClient.GetBlobContainerClient(containerName);
            var blob = container.GetBlobClient(blobName);
            await blob.DeleteAsync();
        }
    }
}
