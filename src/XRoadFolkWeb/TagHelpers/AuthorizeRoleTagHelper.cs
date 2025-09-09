using Microsoft.AspNetCore.Razor.TagHelpers;
using System.Security.Claims;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.TagHelpers;

/// <summary>
/// Conditionally renders content based on a required role (Admin or User) using ClaimTypes.Role.
/// Usage: <admin-only>...</admin-only> or <role-required role="Admin">...</role-required>
/// </summary>
[HtmlTargetElement("admin-only")]
[HtmlTargetElement("role-required", Attributes = RoleAttributeName)]
public class AuthorizeRoleTagHelper : TagHelper
{
    private const string RoleAttributeName = "role";
    public string? Role { get; set; }
    private readonly IHttpContextAccessor _http;
    public AuthorizeRoleTagHelper(IHttpContextAccessor http) => _http = http;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            output.SuppressOutput();
            return;
        }
        string required = context.TagName.Equals("admin-only", StringComparison.OrdinalIgnoreCase)
            ? AppRoles.Admin
            : (Role ?? AppRoles.Admin);
        bool has = user.Claims.Any(c => c.Type == ClaimTypes.Role && string.Equals(c.Value, required, StringComparison.OrdinalIgnoreCase));
        if (!has)
        {
            output.SuppressOutput();
        }
        else
        {
            // remove wrapper tag but keep children
            output.TagName = null;
        }
    }
}
