using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    // Account controller – handles Sign Out only.
    // Change Password and MFA Setup have been removed from the system.
    public class AccountController : AppControllerBase
    {
        public AccountController(AuthService auth, SubscriptionService subscription, AuditService audit)
            : base(auth, subscription, audit)
        {
        }

        private string DashboardUrl =>
            (Auth.CurrentRole?.ToLowerInvariant() ?? "") switch
            {
                "superadmin"       => "/superadmin",
                "admin"            => "/admin",
                "projectmanager"   => "/projectmanager",
                "architect"        => "/architect",
                "siteengineer"     => "/siteengineer",
                "quantitysurveyor" => "/quantitysurveyor",
                "financeofficer"   => "/financeofficer",
                _                  => "/"
            };

        // ---- Change password (disabled – redirect to dashboard) ----------------

        [HttpGet("changepassword")]
        [HttpGet("account/changepassword")]
        public IActionResult ChangePassword() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        [HttpPost("changepassword")]
        [HttpPost("account/changepassword")]
        public IActionResult ChangePasswordPost() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        // ---- MFA setup (disabled – redirect to dashboard) ----------------------

        [HttpGet("mfa-setup")]
        [HttpGet("account/mfasetup")]
        public IActionResult MfaSetup() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        [HttpPost("Account/MfaSendCode")]
        public IActionResult MfaSendCode() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        [HttpPost("Account/MfaVerifyCode")]
        public IActionResult MfaVerifyCode() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        [HttpPost("Account/MfaDisable")]
        public IActionResult MfaDisable() =>
            Redirect(Auth.IsAuthenticated ? DashboardUrl : "/");

        // ---- Sign out ----------------------------------------------------------

        [HttpPost, HttpGet]
        public new async Task<IActionResult> SignOut()
        {
            await Audit.LogLogoutAsync(AuthService.CurrentEmail ?? "unknown");
            Auth.ClearRole();
            HttpContext.Session.Clear();
            return Redirect("/");
        }
    }
}