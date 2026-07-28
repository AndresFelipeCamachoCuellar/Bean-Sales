using Microsoft.AspNetCore.Mvc;
using Web.Filters;

namespace Web.Attributes;

public class HasPermissionAttribute : TypeFilterAttribute
{
    public HasPermissionAttribute(string module, string permission) : base(typeof(PermissionFilter))
    {
        Arguments = new object[] { module, permission };
    }
}
