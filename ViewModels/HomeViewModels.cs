using System;

namespace PlanCore.Web.ViewModels
{
    public class LoginViewModel
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RememberMe { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string WarningMessage { get; set; } = "";
        public string ResetNotifyMessage { get; set; } = "";
        public bool IsAccountLocked { get; set; }
        public int LockoutMinutesRemaining { get; set; }
        public int RemainingAttempts { get; set; } = 5;

        public bool ShowForgot { get; set; }
        public string ForgotEmail { get; set; } = "";
        public string ForgotErrorMessage { get; set; } = "";
        public string ForgotSuccessMessage { get; set; } = "";
        public string RecaptchaSiteKey { get; set; } = "";
    }

    public class MfaViewModel
    {
        public string Code { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public string DevCode { get; set; } = "";
    }
}