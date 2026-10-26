using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    // Port of Components\Pages\Upgrade.razor ("/upgrade") - subscription upgrade prompt.
    public class UpgradeController : AppControllerBase
    {
        public UpgradeController(AuthService auth, SubscriptionService subscription, AuditService audit)
            : base(auth, subscription, audit)
        {
        }

        [HttpGet]
        public IActionResult Index(string? module = null)
        {
            if (!Auth.IsAuthenticated)
                return Redirect("/");

            var currentPlan = Subscription.GetCurrentPlan();
            SubscriptionService.SetTenantPlan(currentPlan.Name);

            var requestedModule = module ?? "";
            var requiredPlan = SubscriptionService.GetRequiredPlan(requestedModule);
            var restrictionMessage = SubscriptionService.GetPlanRestrictionMessage(requestedModule);
            if (string.IsNullOrEmpty(restrictionMessage))
                restrictionMessage = "This feature requires a higher subscription tier.";

            return View(new UpgradeViewModel
            {
                RestrictionMessage = restrictionMessage,
                RequestedModule = requestedModule,
                RequiredPlan = requiredPlan,
                CurrentPlanName = currentPlan.Name
            });
        }
    }
}