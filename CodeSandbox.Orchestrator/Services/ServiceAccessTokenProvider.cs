using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace CodeSandbox.Orchestrator.Services
{
    public class ServiceAccessTokenProvider
    {
        private readonly TokenCredential _tokenCredential;
        private AccessToken _cachedToken;

        public ServiceAccessTokenProvider(IConfiguration configuration)
        {
            var tenantId = configuration["TenantId"];
            var options = new DefaultAzureCredentialOptions
            {
                SharedTokenCacheTenantId = tenantId,
                VisualStudioCodeTenantId = tenantId,
                VisualStudioTenantId = tenantId,
            };
            _tokenCredential = new DefaultAzureCredential(options);
        }

        public async Task<string> GetAzureManagementApiAccessTokenAsync()
        {
            var tokenRefreshTime = _cachedToken.ExpiresOn == default
                ? _cachedToken.ExpiresOn
                : _cachedToken.ExpiresOn.Subtract(TimeSpan.FromMinutes(10));
            if (DateTimeOffset.UtcNow < tokenRefreshTime)
            {
                return _cachedToken.Token;
            }

            var scope = "https://management.core.windows.net/.default";
            var accessToken = await _tokenCredential.GetTokenAsync(
                new TokenRequestContext(new[] { scope }), default);
            _cachedToken = accessToken;
            return accessToken.Token;
        }
    }
}
