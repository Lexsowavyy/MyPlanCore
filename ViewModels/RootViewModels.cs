using System;

namespace PlanCore.Web.ViewModels
{
    public class SetupViewModel
    {
        public string SelectedPlan { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string SuccessMessage { get; set; } = "";
        public bool RedirectAfterSuccess { get; set; }
    }

    public class UpgradeViewModel
    {
        public string RestrictionMessage { get; set; } = "";
        public string RequestedModule { get; set; } = "";
        public string RequiredPlan { get; set; } = "";
        public string CurrentPlanName { get; set; } = "";
    }
}