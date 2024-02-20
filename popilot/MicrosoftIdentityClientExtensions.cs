using Microsoft.Identity.Client;

namespace popilot
{
    public static class MicrosoftIdentityClientExtensions
    {
        public static T WithTenantIdIfNotNullNorEmpty<T>(this AbstractApplicationBuilder<T> applicationBuilder, string? tenantId)
            where T : BaseAbstractApplicationBuilder<T>
        {
            if (!string.IsNullOrEmpty(tenantId))
            {
                return applicationBuilder.WithTenantId(tenantId);
            }
            else
            {
                return (applicationBuilder as T)!;
            }
        }

    }
}
