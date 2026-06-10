using Microsoft.AspNetCore.Authorization;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace RipplePreview.Controllers;

/// <summary>
/// Route attribute for versioned Ripple Preview API endpoints:
/// /umbraco/ripple-preview/api/v{version}/...
/// </summary>
public class RippleVersionedRouteAttribute : BackOfficeRouteAttribute
{
    public RippleVersionedRouteAttribute(string template)
        : base($"{RippleConstants.Configuration.ApiPath}/v{{version:apiVersion}}/{template.TrimStart('/')}")
    {
    }
}

/// <summary>
/// Base controller for Ripple Preview API endpoints. Backoffice users only.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[RippleVersionedRoute("")]
[MapToApi(RippleConstants.Configuration.ApiName)]
public class RipplePreviewControllerBase : ManagementApiControllerBase
{
}
