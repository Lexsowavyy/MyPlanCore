using System.Text.Json;
using DotNetEnv;

namespace PlanCore.Web.Services
{
    public class RecaptchaService
    {
        private readonly HttpClient _http;
        private readonly string _secretKey;

        public RecaptchaService(HttpClient http)
        {
            _http = http;
            SiteKey = FirstNonEmpty(
                Environment.GetEnvironmentVariable("RECAPTCHA_SITE_KEY"),
                TryEnv("RECAPTCHA_SITE_KEY"),
                "6LeYXwAuAAAAAFvkYU4DI1Z1ZTpcYTwbtFofd_6D");
            _secretKey = FirstNonEmpty(
                Environment.GetEnvironmentVariable("RECAPTCHA_SECRET_KEY"),
                TryEnv("RECAPTCHA_SECRET_KEY"),
                "6LeYXwAuAAAAADqEUXBKNJJRsmP8pcFQ8Oerk9JR");
        }

        public string SiteKey { get; }

        public async Task<bool> VerifyAsync(string? token, string? remoteIp)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_secretKey))
                return false;

            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _secretKey,
                    ["response"] = token,
                    ["remoteip"] = remoteIp ?? ""
                });
                // reCAPTCHA Enterprise uses the same siteverify endpoint
                using var response = await _http.PostAsync("https://www.google.com/recaptcha/api/siteverify", content);
                if (!response.IsSuccessStatusCode)
                    return false;

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                return doc.RootElement.TryGetProperty("success", out var success) && success.GetBoolean();
            }
            catch
            {
                return false;
            }
        }

        private static string? TryEnv(string key)
        {
            try { return Env.GetString(key); }
            catch { return null; }
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return "";
        }
    }
}
