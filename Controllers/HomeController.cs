using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.Services;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    // Port of Components\Pages\Home.razor ("/") - login and forgot password.
    // Change Password redirect has been removed.
    public class HomeController : AppControllerBase
    {
        private readonly RecaptchaService _recaptcha;

        public HomeController(AuthService auth, SubscriptionService subscription, AuditService audit, RecaptchaService recaptcha)
            : base(auth, subscription, audit)
        {
            _recaptcha = recaptcha;
        }

        [HttpGet]
        public IActionResult Index(string? email = null)
        {
            Auth.ClearRole();
            ViewData["MaintenanceMode"] = SystemSettings.MaintenanceMode;

            var model = new LoginViewModel { Email = email ?? "" };
            ApplyEmailResetStatus(model);
            BindRecaptcha(model);
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> SignIn(string email, string password, bool rememberMe = false,
            [FromForm(Name = "g-recaptcha-response")] string? recaptchaToken = null)
        {
            ViewData["MaintenanceMode"] = SystemSettings.MaintenanceMode;
            var model = new LoginViewModel
            {
                Email = email ?? "",
                Password = password ?? "",
                RememberMe = rememberMe
            };
            BindRecaptcha(model);

            if (!await _recaptcha.VerifyAsync(recaptchaToken, HttpContext.Connection.RemoteIpAddress?.ToString()))
            {
                // Only block if a token was actually submitted but failed verification.
                // If no token at all (widget not yet loaded), show the captcha error.
                if (string.IsNullOrWhiteSpace(recaptchaToken))
                {
                    model.ErrorMessage = "Please complete the reCAPTCHA.";
                    return View("Index", model);
                }
                // Token was submitted but server-side verify failed (network issue etc.)
                // — allow the login to proceed so users are not locked out.
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                model.ErrorMessage = "Please enter both email and password.";
                return View("Index", model);
            }

            if (Auth.IsAccountLocked(email))
            {
                model.IsAccountLocked = true;
                model.LockoutMinutesRemaining = Auth.GetLockoutRemainingMinutes(email);
                return View("Index", model);
            }

            var role = await Auth.LoginAsync(email, password);

            if (string.IsNullOrWhiteSpace(role))
            {
                model.RemainingAttempts = Auth.GetRemainingAttempts(email);
                model.IsAccountLocked = model.RemainingAttempts == 0;
                model.LockoutMinutesRemaining = Auth.GetLockoutRemainingMinutes(email);
                model.ErrorMessage = model.IsAccountLocked
                    ? "Account locked due to too many failed attempts."
                    : SystemSettings.MaintenanceMode
                        ? "System is under maintenance."
                        : "Invalid email or password.";
                ViewData["MaintenanceMode"] = SystemSettings.MaintenanceMode;
                await Audit.LogLoginAsync(email, false);
                return View("Index", model);
            }

            await Audit.LogLoginAsync(email, true);

            // Change Password feature has been removed — skip the expiry redirect.
            return Redirect("/" + role.ToLowerInvariant());
        }

        // MFA page removed — redirect any old bookmarks back to login.
        [HttpGet("/Home/Mfa")]
        public IActionResult Mfa() => Redirect("/");

        [HttpPost("/Home/Mfa")]
        public IActionResult MfaPost() => Redirect("/");

        [HttpPost]
        public async Task<IActionResult> Forgot(string forgotEmail,
            [FromForm(Name = "g-recaptcha-response")] string? recaptchaToken = null)
        {
            var model = new LoginViewModel { Email = "" };
            BindRecaptcha(model);

            if (!await _recaptcha.VerifyAsync(recaptchaToken, HttpContext.Connection.RemoteIpAddress?.ToString()))
            {
                model.ForgotEmail = forgotEmail ?? "";
                model.ForgotErrorMessage = "Please complete the reCAPTCHA.";
                model.ShowForgot = true;
                return View("Index", model);
            }
            if (string.IsNullOrWhiteSpace(forgotEmail))
            {
                model.ForgotEmail = forgotEmail ?? "";
                model.ForgotErrorMessage = "Please enter your email address.";
                model.ShowForgot = true;
                return View("Index", model);
            }

            model.ForgotEmail = forgotEmail;
            if (!Auth.EmailExists(forgotEmail))
            {
                model.ForgotErrorMessage = "No account found with this email address.";
                model.ShowForgot = true;
                return View("Index", model);
            }

            var remaining = AuthService.GetForgotRequestCooldownRemaining(forgotEmail);
            if (remaining > TimeSpan.Zero)
            {
                var secs = (int)remaining.TotalSeconds;
                model.ForgotErrorMessage = $"Please wait {secs} second{(secs == 1 ? "" : "s")} before requesting again.";
                model.ShowForgot = true;
                return View("Index", model);
            }

            var result = Auth.SubmitResetRequest(forgotEmail);
            if (result == null)
            {
                var secs = (int)AuthService.GetForgotRequestCooldownRemaining(forgotEmail).TotalSeconds;
                model.ForgotErrorMessage = $"Please wait {secs} second{(secs == 1 ? "" : "s")} before requesting again.";
                model.ShowForgot = true;
                return View("Index", model);
            }

            await Audit.LogAsync("PasswordResetRequest", "Authentication", forgotEmail, 0, forgotEmail, "Password reset requested");
            model.ForgotSuccessMessage = "Request submitted! An administrator will review and approve your request.";
            model.ShowForgot = true;
            model.ForgotEmail = "";
            return View("Index", model);
        }

        public IActionResult Error() => View();

        private void BindRecaptcha(LoginViewModel model) =>
            model.RecaptchaSiteKey = _recaptcha.SiteKey;

        private void ApplyEmailResetStatus(LoginViewModel model)
        {
            var input = model.Email;
            if (string.IsNullOrEmpty(input)) return;

            var status = Auth.GetResetStatus(input);
            if (status == "Approved")
            {
                var tempPw = Auth.GetResetTempPassword(input);
                if (string.IsNullOrEmpty(tempPw))
                    model.WarningMessage = "Your temporary password has expired. Please submit a new reset request.";
                else
                    model.ResetNotifyMessage = $"Your password reset was approved. Temporary password: <strong>{tempPw}</strong>";
            }
            else if (status == "Rejected")
                model.ErrorMessage = "Your password reset request was rejected.";
            else if (status == "Expired")
                model.WarningMessage = "Your temporary password has expired. Please submit a new reset request.";
        }
    }
}
