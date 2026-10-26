using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PlanCoreMAUI.Data;
using PlanCoreMAUI.Models;

namespace PlanCoreMAUI.Services
{
    // Web-compatible AuthService.
    // Identical public API and logic to the MAUI desktop AuthService, except the
    // "current signed in user / pending MFA" state is stored in the HTTP session so
    // the web application works correctly for multiple concurrent users.
    public class AuthService
    {
        private static IHttpContextAccessor? _accessor;

        public static void ConfigureAccessor(IHttpContextAccessor accessor) => _accessor = accessor;

        private static ISession? Session => _accessor?.HttpContext?.Session;

        private const string RoleKey = "CurrentRole";
        private const string EmailKey = "CurrentEmail";
        private const string SavedRoleKey = "SavedRole";

        private static string? _currentRoleFallback;
        private static string? _currentEmailFallback;
        private static string? _savedRoleFallback;

        private static readonly Dictionary<string, (string HashedPassword, string Role)> Users = new();
        private static readonly Dictionary<string, DateTime?> PasswordChangedDates = new();
        private static readonly Dictionary<string, LoginAttempt> LoginAttempts = new();

        private static int MaxFailedAttempts => DotNetEnv.Env.GetInt("MAX_LOGIN_ATTEMPTS", 3);
        private static TimeSpan ShortLockoutDuration => TimeSpan.FromMinutes(DotNetEnv.Env.GetInt("SHORT_LOCKOUT_MINUTES", 3));
        private static TimeSpan LongLockoutDuration => TimeSpan.FromMinutes(DotNetEnv.Env.GetInt("LONG_LOCKOUT_MINUTES", 15));
        private readonly IServiceProvider _serviceProvider;

        private class LoginAttempt
        {
            public int FailedCount { get; set; }
            public DateTime? LockedUntil { get; set; }
            public int LockoutEscalations { get; set; }
        }

        public AuthService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private static string? ReadSession(string key)
        {
            return Session?.GetString(key) ?? _currentRoleFallback; // fallback used only when session unavailable
        }

        private static void WriteSession(string key, string keyBackup, string? value, ref string? backup)
        {
            if (Session != null)
            {
                if (value == null) Session.Remove(key);
                else Session.SetString(key, value);
            }
            else
            {
                backup = value;
            }
        }

        public string? CurrentRole
        {
            get
            {
                var v = Session?.GetString(RoleKey) ?? _currentRoleFallback;
                return v;
            }
            set
            {
                if (Session != null)
                {
                    if (value == null) Session.Remove(RoleKey);
                    else Session.SetString(RoleKey, value);
                }
                else _currentRoleFallback = value;
            }
        }

        public static string? CurrentUserRole => Session?.GetString(RoleKey) ?? _currentRoleFallback;
        public static string? CurrentEmail => Session?.GetString(EmailKey) ?? _currentEmailFallback;
        public string? CurrentDisplayName => CurrentRole != null ? FormatRoleName(CurrentRole) : null;

        public static DateTime? GetPasswordChangedAt(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || !PasswordChangedDates.TryGetValue(normalized, out var dt))
                return null;
            return dt;
        }

        private static string FormatRoleName(string role)
        {
            return role switch
            {
                "QuantitySurveyor" => "QS",
                "FinanceOfficer" => "Finance Officer",
                "SiteEngineer" => "Site Engineer",
                "ProjectManager" => "Project Manager",
                "SuperAdmin" => "Super Admin",
                _ => role
            };
        }

        public event Action? OnRoleChanged;

        public void Initialize(string? savedRole)
        {
            var args = new object?[] { savedRole };
            WriteSession(SavedRoleKey, SavedRoleKey, savedRole, ref _savedRoleFallback);
        }

        public static void LoadFromDatabase(ApplicationDbContext db)
        {
            try
            {
                var dbUsers = db.UserAccounts.AsNoTracking().ToList();
                Users.Clear();
                PasswordChangedDates.Clear();
                foreach (var u in dbUsers)
                {
                    Users[u.Email.ToLower().Trim()] = (u.PasswordHash, u.Role);
                    if (u.PasswordChangedAt.HasValue)
                        PasswordChangedDates[u.Email.ToLower().Trim()] = u.PasswordChangedAt;
                }
                System.Diagnostics.Debug.WriteLine($"LoadFromDatabase: loaded {Users.Count} users");
                foreach (var email in Users.Keys)
                    System.Diagnostics.Debug.WriteLine($"  - {email}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFromDatabase error: {ex.Message}");
            }
        }

        public static List<(string Email, string Role)> GetAllUsers()
        {
            return Users.Select(u => (u.Key, u.Value.Role)).ToList();
        }

        public static HashSet<string> GetSuperAdminEmails()
        {
            return new HashSet<string>(Users
                .Where(u => u.Value.Role == "SuperAdmin")
                .Select(u => u.Key),
                StringComparer.OrdinalIgnoreCase);
        }

        public string? Login(string email, string password)
        {
            return LoginInternal(email, password);
        }

        private string? LoginInternal(string email, string password)
        {
            var normalizedEmail = email?.ToLower().Trim();

            if (string.IsNullOrEmpty(normalizedEmail))
                return null;

            if (LoginAttempts.TryGetValue(normalizedEmail, out var attempt))
            {
                if (attempt.LockedUntil.HasValue && attempt.LockedUntil > DateTime.UtcNow)
                    return null;
                if (attempt.LockedUntil.HasValue && attempt.LockedUntil <= DateTime.UtcNow)
                {
                    attempt.FailedCount = 0;
                    attempt.LockedUntil = null;
                    attempt.LockoutEscalations = 0;
                }
            }

            if (Users.TryGetValue(normalizedEmail, out var user))
            {
                if (VerifyPassword(password, user.HashedPassword))
                {
                    if (Models.SystemSettings.MaintenanceMode && user.Role != "SuperAdmin")
                        return null;

                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
                        var tenant = db.Tenants.FirstOrDefault(t => t.AdminEmail == normalizedEmail);
                        if (tenant != null)
                        {
                            if (!tenant.IsActive && user.Role != "SuperAdmin")
                                return null;
                            if (tenant.IsExpired && user.Role != "SuperAdmin")
                                return null;
                            SubscriptionService.SetTenantPlan(tenant.Plan);
                        }
                    }
                    catch { }

                    CurrentRole = user.Role;
                    SetSessionEmail(normalizedEmail);

                    if (LoginAttempts.ContainsKey(normalizedEmail))
                        LoginAttempts.Remove(normalizedEmail);

                    OnRoleChanged?.Invoke();

                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
#pragma warning disable EF1002
                        db.Database.ExecuteSqlRaw("UPDATE [ASPNetUsers] SET [LastLoginAt] = {0} WHERE [Email] = {1}", DateTime.Now, normalizedEmail);
#pragma warning restore EF1002
                    }
                    catch { }

                    return user.Role;
                }
            }

            if (!LoginAttempts.ContainsKey(normalizedEmail))
                LoginAttempts[normalizedEmail] = new LoginAttempt();

            LoginAttempts[normalizedEmail].FailedCount++;

            if (LoginAttempts[normalizedEmail].FailedCount >= MaxFailedAttempts)
            {
                var att = LoginAttempts[normalizedEmail];
                if (att.LockedUntil == null)
                {
                    att.LockedUntil = DateTime.UtcNow + ShortLockoutDuration;
                    att.LockoutEscalations = 1;
                }
                else if (att.LockoutEscalations < 3)
                {
                    att.LockedUntil = DateTime.UtcNow + LongLockoutDuration;
                    att.LockoutEscalations++;
                }
            }

            return null;
        }

        private static void SetSessionEmail(string? email)
        {
            if (Session != null)
            {
                if (email == null) Session.Remove(EmailKey);
                else Session.SetString(EmailKey, email);
            }
            else _currentEmailFallback = email;
        }

        public async Task<string?> LoginAsync(string email, string password)
        {
            var normalizedEmail = email?.ToLower().Trim();

            if (string.IsNullOrEmpty(normalizedEmail))
                return null;

            if (LoginAttempts.TryGetValue(normalizedEmail, out var attempt))
            {
                if (attempt.LockedUntil.HasValue && attempt.LockedUntil > DateTime.UtcNow)
                    return null;
                if (attempt.LockedUntil.HasValue && attempt.LockedUntil <= DateTime.UtcNow)
                {
                    attempt.FailedCount = 0;
                    attempt.LockedUntil = null;
                    attempt.LockoutEscalations = 0;
                }
            }

            if (Users.TryGetValue(normalizedEmail, out var user))
            {
                if (VerifyPassword(password, user.HashedPassword))
                {
                    if (Models.SystemSettings.MaintenanceMode && user.Role != "SuperAdmin")
                        return null;

                    // Check tenant subscription expiry
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
                        var tenant = db.Tenants.FirstOrDefault(t => t.AdminEmail == normalizedEmail);
                        if (tenant != null)
                        {
                            if (!tenant.IsActive && user.Role != "SuperAdmin")
                                return null;
                            if (tenant.IsExpired && user.Role != "SuperAdmin")
                                return null;
                            SubscriptionService.SetTenantPlan(tenant.Plan);
                        }
                    }
                    catch { }

                    CurrentRole = user.Role;
                    SetSessionEmail(normalizedEmail);

                    if (LoginAttempts.ContainsKey(normalizedEmail))
                        LoginAttempts.Remove(normalizedEmail);

                    OnRoleChanged?.Invoke();

                    // Log login and update last login
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
#pragma warning disable EF1002
                        db.Database.ExecuteSqlRaw("UPDATE [ASPNetUsers] SET [LastLoginAt] = {0} WHERE [Email] = {1}", DateTime.Now, normalizedEmail);
#pragma warning restore EF1002
                    }
                    catch { }

                    return user.Role;
                }
            }

            if (!LoginAttempts.ContainsKey(normalizedEmail))
                LoginAttempts[normalizedEmail] = new LoginAttempt();

            LoginAttempts[normalizedEmail].FailedCount++;

            if (LoginAttempts[normalizedEmail].FailedCount >= MaxFailedAttempts)
            {
                var att = LoginAttempts[normalizedEmail];
                if (att.LockoutEscalations == 0)
                {
                    att.LockedUntil = DateTime.UtcNow.Add(ShortLockoutDuration);
                    att.LockoutEscalations = 1;
                }
                else
                {
                    att.LockedUntil = DateTime.UtcNow.Add(LongLockoutDuration);
                }
            }

            return null;
        }

        public bool IsAccountLocked(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized)) return false;
            if (LoginAttempts.TryGetValue(normalized, out var attempt))
                return attempt.LockedUntil.HasValue && attempt.LockedUntil > DateTime.UtcNow;
            return false;
        }

        public int GetRemainingAttempts(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized)) return MaxFailedAttempts;
            if (LoginAttempts.TryGetValue(normalized, out var attempt))
            {
                if (attempt.LockedUntil.HasValue && attempt.LockedUntil > DateTime.UtcNow)
                    return 0;
                return Math.Max(0, MaxFailedAttempts - attempt.FailedCount);
            }
            return MaxFailedAttempts;
        }

        public int GetLockoutRemainingMinutes(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized)) return 0;
            if (LoginAttempts.TryGetValue(normalized, out var attempt) && attempt.LockedUntil.HasValue)
            {
                var remaining = attempt.LockedUntil.Value - DateTime.UtcNow;
                return remaining.TotalSeconds > 0 ? Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) : 0;
            }
            return 0;
        }

        public void UnlockAccount(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (!string.IsNullOrEmpty(normalized) && LoginAttempts.ContainsKey(normalized))
                LoginAttempts.Remove(normalized);
        }

        public void SetRole(string role)
        {
            CurrentRole = role;
            OnRoleChanged?.Invoke();
        }

        public void ClearRole()
        {
            CurrentRole = null;
            SetSessionEmail(null);
            OnRoleChanged?.Invoke();
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(CurrentRole);

        public bool IsPasswordExpired(string email)
        {
            if (!Models.SystemSettings.PasswordExpiry)
                return false;
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || !PasswordChangedDates.TryGetValue(normalized, out var changedAt))
                return false;
            if (!changedAt.HasValue)
                return true;
            var expiryDays = Models.SystemSettings.PasswordExpiryDays;
            return changedAt.Value.AddDays(expiryDays) < DateTime.Now;
        }

        public List<string> GetAvailableEmails()
        {
            return Users.Keys.ToList();
        }

        public bool EmailExists(string email)
        {
            var normalized = email?.ToLower().Trim();
            return !string.IsNullOrEmpty(normalized) && Users.ContainsKey(normalized);
        }

        public static string HashPassword(string password)
        {
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
            byte[] hashBytes = new byte[48];
            Array.Copy(salt, 0, hashBytes, 0, 16);
            Array.Copy(hash, 0, hashBytes, 16, 32);
            return Convert.ToBase64String(hashBytes);
        }

        public static bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword))
                return false;

            try
            {
                byte[] hashBytes = Convert.FromBase64String(hashedPassword);
                if (hashBytes.Length != 48) return false;
                byte[] salt = new byte[16];
                Array.Copy(hashBytes, 0, salt, 0, 16);
                byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
                for (int i = 0; i < 32; i++)
                {
                    if (hashBytes[i + 16] != hash[i]) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool SyncUserToDb(string email, string hashedPassword, string role, bool passwordChanged = false, string? adminEmail = null)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var existing = db.UserAccounts.AsNoTracking().FirstOrDefault(u => u.Email == email);
                if (existing != null)
                {
                    var updated = new UserAccount { Email = email, PasswordHash = hashedPassword, Role = role, AdminEmail = adminEmail ?? existing.AdminEmail };
                    if (passwordChanged)
                    {
                        updated.PasswordChangedAt = DateTime.Now;
                        PasswordChangedDates[email] = updated.PasswordChangedAt;
                    }
                    db.UserAccounts.Update(updated);
                }
                else
                {
                    db.UserAccounts.Add(new UserAccount { Email = email, PasswordHash = hashedPassword, Role = role, AdminEmail = adminEmail });
                }

                db.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] SyncUserToDb FAILED for {email}: {ex}");
                return false;
            }
        }

        public bool AddUser(string email, string plainPassword, string role, string? adminEmail = null)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || Users.ContainsKey(normalized))
                return false;
            var hashed = HashPassword(plainPassword);
            Users[normalized] = (hashed, role);
            var ok = SyncUserToDb(normalized, hashed, role, adminEmail: adminEmail ?? CurrentEmail);
            if (!ok)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] AddUser: SyncUserToDb FAILED for {normalized}, user removed from memory");
                Users.Remove(normalized);
            }
            return ok;
        }

        public void AddUserToCache(string email, string hashedPassword, string role)
        {
            var normalized = email?.ToLower().Trim();
            if (!string.IsNullOrEmpty(normalized))
                Users[normalized] = (hashedPassword, role);
        }

        public bool ChangePassword(string email, string currentPassword, string newPassword)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || !Users.TryGetValue(normalized, out var user))
                return false;
            if (!VerifyPassword(currentPassword, user.HashedPassword))
                return false;
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
                return false;
            var hashed = HashPassword(newPassword);
            Users[normalized] = (hashed, user.Role);
            if (LoginAttempts.ContainsKey(normalized))
                LoginAttempts.Remove(normalized);
            SyncUserToDb(normalized, hashed, user.Role, passwordChanged: true);
            return true;
        }

        public bool ResetPassword(string email, string newPassword)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || !Users.ContainsKey(normalized))
                return false;
            var hashed = HashPassword(newPassword);
            Users[normalized] = (hashed, Users[normalized].Role);
            if (LoginAttempts.ContainsKey(normalized))
                LoginAttempts.Remove(normalized);
            SyncUserToDb(normalized, hashed, Users[normalized].Role);
            return true;
        }

        public class PasswordResetRequest
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
            public string? TempPassword { get; set; }
            public DateTime RequestedAt { get; set; } = DateTime.Now;
            public string Status { get; set; } = "Pending";
            public string? ReviewedBy { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public string? Remarks { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }

        private static List<PasswordResetRequest> _resetRequests = new();
        private static int _nextResetId = 1;

        private static readonly Dictionary<string, DateTime> _forgotRequestTimestamps = new();
        private static readonly TimeSpan ForgotRequestCooldown = TimeSpan.FromMinutes(3);

        public static TimeSpan GetForgotRequestCooldownRemaining(string email)
        {
            var normalized = email?.ToLower().Trim();
            if (string.IsNullOrEmpty(normalized) || !_forgotRequestTimestamps.TryGetValue(normalized, out var last))
                return TimeSpan.Zero;
            var elapsed = DateTime.Now - last;
            return elapsed < ForgotRequestCooldown ? ForgotRequestCooldown - elapsed : TimeSpan.Zero;
        }

        public string? SubmitResetRequest(string email)
        {
            var normalized = email.ToLower().Trim();
            var remaining = GetForgotRequestCooldownRemaining(normalized);
            if (remaining > TimeSpan.Zero)
                return null;
            _forgotRequestTimestamps[normalized] = DateTime.Now;
            _resetRequests.Add(new PasswordResetRequest
            {
                Id = _nextResetId++,
                Email = normalized,
                RequestedAt = DateTime.Now,
                Status = "Pending"
            });
            return "submitted";
        }

        public string? ApproveReset(int requestId, string reviewedBy)
        {
            var req = _resetRequests.FirstOrDefault(r => r.Id == requestId);
            if (req != null && req.Status == "Pending")
            {
                var tempPw = GenerateTempPassword();
                if (ResetPassword(req.Email, tempPw))
                {
                    req.TempPassword = tempPw;
                    req.Status = "Approved";
                    req.ReviewedBy = reviewedBy;
                    req.ReviewedAt = DateTime.Now;
                    req.ExpiresAt = DateTime.Now.AddHours(24);
                    return tempPw;
                }
            }
            return null;
        }

        public List<PasswordResetRequest> GetPendingResetRequests()
        {
            return _resetRequests.Where(r => r.Status == "Pending").OrderBy(r => r.RequestedAt).ToList();
        }

        public List<PasswordResetRequest> GetAllResetRequests()
        {
            return _resetRequests.OrderByDescending(r => r.RequestedAt).ToList();
        }

        public string? GetResetStatus(string email)
        {
            var normalized = email?.ToLower().Trim();
            var req = _resetRequests.LastOrDefault(r => r.Email == normalized);
            if (req == null) return null;
            if (req.Status == "Approved" && req.ExpiresAt.HasValue && req.ExpiresAt < DateTime.Now)
                return "Expired";
            return req.Status;
        }

        public string? GetResetTempPassword(string email)
        {
            var normalized = email?.ToLower().Trim();
            var req = _resetRequests.LastOrDefault(r => r.Email == normalized);
            if (req?.Status != "Approved") return null;
            if (req.ExpiresAt.HasValue && req.ExpiresAt < DateTime.Now) return null;
            return req.TempPassword;
        }

        public PasswordResetRequest? RejectResetRequest(int id, string reviewedBy, string? remarks = null)
        {
            var req = _resetRequests.FirstOrDefault(r => r.Id == id);
            if (req != null && req.Status == "Pending")
            {
                req.Status = "Rejected";
                req.ReviewedBy = reviewedBy;
                req.ReviewedAt = DateTime.Now;
                req.Remarks = remarks;
                return req;
            }
            return null;
        }

        private static string GenerateTempPassword()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var num = BitConverter.ToUInt32(bytes, 0) % 9000 + 1000;
            return $"Temp@{num}";
        }
    }
}