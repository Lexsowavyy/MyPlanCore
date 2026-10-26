using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    // Port of Components\Pages\Setup.razor ("/setup") - first-run plan selection.
    public class SetupController : AppControllerBase
    {
        public SetupController(AuthService auth, SubscriptionService subscription, AuditService audit)
            : base(auth, subscription, audit)
        {
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? plan = null)
        {
            var email = AuthService.CurrentEmail;
            if (string.IsNullOrEmpty(email))
                return Redirect("/");

            var tenant = Subscription.GetTenant();
            if (tenant == null)
            {
                await Subscription.CreateTenantForAdminAsync(email, $"{email}'s Company");
                tenant = Subscription.GetTenant();
                if (tenant == null)
                    return Redirect("/");
            }

            if (!string.IsNullOrEmpty(tenant.Plan))
                return Redirect("/admin");

            return View(new SetupViewModel { SelectedPlan = plan ?? "" });
        }

        [HttpPost]
        public async Task<IActionResult> Confirm(string plan)
        {
            var email = AuthService.CurrentEmail;
            if (string.IsNullOrEmpty(email))
                return Redirect("/");

            var tenant = Subscription.GetTenant();
            if (tenant == null)
                return Redirect("/setup");

            var model = new SetupViewModel { SelectedPlan = plan ?? "" };

            var result = SubscriptionService.SelectInitialPlan(tenant, plan);
            if (!string.IsNullOrEmpty(result))
            {
                model.ErrorMessage = result;
                return View("Index", model);
            }

            model.SuccessMessage = $"Welcome to {plan}! Redirecting to your dashboard...";
            model.RedirectAfterSuccess = true;
            return View("Index", model);
        }
    }
}