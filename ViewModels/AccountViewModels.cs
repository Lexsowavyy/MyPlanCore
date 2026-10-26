using System;

namespace PlanCore.Web.ViewModels
{
    public class ChangePasswordViewModel
    {
        public string ErrorMessage { get; set; } = "";
        public string SuccessMessage { get; set; } = "";
        public bool PasswordChanged { get; set; }
    }

    public class MfaSetupViewModel
    {
        public string Email { get; set; } = "";
        public string Step { get; set; } = "idle";
        public string Code { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string SuccessMessage { get; set; } = "";
        public bool IsMfaEnabled { get; set; }
        public string DevCode { get; set; } = "";
    }
}