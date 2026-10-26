using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;

namespace PlanCore.Web.Infrastructure
{
    // Shared guard used by all secured controllers. Mirrors the checks that were
    // performed by MainLayout.razor + the login page in the Blazor app:
    //  - page-level role requirement
    //  - maintenance-mode sign-out of non-SuperAdmin users
    //  - subscription route access for module roles (redirect to /upgrade)
    public abstract class AppControllerBase : Controller
    {
        protected AuthService Auth { get; }
        protected SubscriptionService Subscription { get; }
        protected AuditService Audit { get; }

        protected AppControllerBase(AuthService auth, SubscriptionService subscription, AuditService audit)
        {
            Auth = auth;
            Subscription = subscription;
            Audit = audit;
        }

        protected virtual string? RequiredRole => null;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            var role = Auth.CurrentRole;
            ViewData["CurrentRole"] = role;
            ViewData["CurrentEmail"] = AuthService.CurrentEmail;
            ViewData["RoleDisplay"] = RoleDisplayName(role);

            if (RequiredRole != null)
            {
                // SuperAdmin can access every area except nothing is blocked for them.
                var superAdminCanAccess = role == "SuperAdmin";
                // Admin is allowed to open every module area (used by the Admin
                // "All Modules" page); it may not enter the SuperAdmin area.
                var adminCanAccessModules = role == "Admin" && RequiredRole != "SuperAdmin";
                if (string.IsNullOrEmpty(role) || (role != RequiredRole && !adminCanAccessModules && !superAdminCanAccess))
                {
                    context.Result = Redirect("/");
                    return;
                }
            }

            // Maintenance mode: any non-SuperAdmin signed-in user is signed out and
            // sent back to the login screen (same behaviour as MainLayout.razor).
            if (SystemSettings.MaintenanceMode && !string.IsNullOrEmpty(role) && role != "SuperAdmin")
            {
                Auth.ClearRole();
                context.Result = Redirect("/");
                return;
            }

            // Subscription route gating for module roles (SuperAdmin and Admin bypass).
            if (role is not null and not "SuperAdmin" and not "Admin")
            {
                var path = HttpContext.Request.Path.Value?.TrimStart('/').ToLowerInvariant() ?? "";
                if (!SubscriptionService.CheckRouteAccess(path))
                {
                    var moduleKey = SubscriptionService.GetModuleForRoute(path);
                    context.Result = Redirect($"/upgrade?module={Uri.EscapeDataString(moduleKey)}");
                    return;
                }
            }
        }

        protected static string RoleDisplayName(string? role)
        {
            return role switch
            {
                "Admin" => "Administrator",
                "ProjectManager" => "Project Manager",
                "Architect" => "Architect",
                "SiteEngineer" => "Site Engineer",
                "QuantitySurveyor" => "Quantity Surveyor",
                "FinanceOfficer" => "Finance Officer",
                "SuperAdmin" => "Super Admin",
                _ => "User"
            };
        }
    }
}