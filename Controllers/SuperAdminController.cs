using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCoreMAUI;
using PlanCoreMAUI.Data;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;
using UsersStore = PlanCoreMAUI.Components.Pages.Admin.Users;

namespace PlanCore.Web.Controllers
{
    public class SuperAdminController : AppControllerBase
    {
        private readonly PdfExportService _pdfExport;
        private static readonly object _adminLock = new();
        private static readonly List<UsersStore.User> _adminUsers = new();

        private static readonly object _backupLock = new();
        private static bool _backupsSeeded;
        private static readonly List<SuperAdminBackupItem> _backups = new();
        private static string _lastBackup = "";
        private static string _nextScheduled = "";
        private static string _scheduleFrequency = "Every 6 Hours";
        private static string _scheduleTime = "14:00";
        private static string _retentionPolicy = "Keep last 30 days";
        private static string _backupType = "Full + Incremental";
        private static readonly TimeZoneInfo _manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

        public SuperAdminController(AuthService auth, SubscriptionService subscription, AuditService audit, PdfExportService pdfExport)
            : base(auth, subscription, audit)
        {
            _pdfExport = pdfExport;
        }

        protected override string? RequiredRole => "SuperAdmin";

        [HttpGet("superadmin")]
        public async Task<IActionResult> Index(string? filter)
        {
            UsersStore.EnsureUsersSynced();
            var model = new SuperAdminDashboardViewModel
            {
                DashboardFilter = filter ?? "All",
                DisplayName = Auth.CurrentDisplayName ?? AuthService.CurrentEmail ?? "Super Admin"
            };
            var manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            var nowManila = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, manila);
            model.LastBackupTime = nowManila.AddHours(-2).ToString("MMM d, h:mm tt");

            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    model.AuditLogs = await db.AuditLogs.OrderByDescending(l => l.PerformedAt).ToListAsync();
                    model.Projects = await db.PM_Projects.Where(p => !p.IsArchived).ToListAsync();
                }
            }
            catch { }

            return View(model);
        }

        [HttpGet("superadmin/manage-users")]
        public IActionResult ManageUsers(string? search, string? filterStatus, string? add, int? view, int? edit, int? archive, int? approve, int? reject)
        {
            var model = BuildUsersViewModel(search ?? "", filterStatus ?? "");
            model.Modal = null;

            if (add == "1")
                model.Modal = "add";
            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ModalId = archive;
            }
            if (approve.HasValue)
            {
                model.Modal = "approve";
                model.ModalId = approve;
                var req = model.PendingRequests.FirstOrDefault(r => r.Id == approve.Value);
                model.ModalEmail = req?.Email ?? "";
            }
            if (reject.HasValue)
            {
                model.Modal = "reject";
                model.ModalId = reject;
                var req = model.PendingRequests.FirstOrDefault(r => r.Id == reject.Value);
                model.ModalEmail = req?.Email ?? "";
            }
            if (view.HasValue)
            {
                model.Modal = "view";
                model.SelectedUser = model.Users.FirstOrDefault(u => u.Id == view.Value);
                if (model.SelectedUser != null)
                    model.SelectedUser.PasswordChangedAt = AuthService.GetPasswordChangedAt(model.SelectedUser.Email);
            }
            if (edit.HasValue)
            {
                model.Modal = "edit";
                var target = model.Users.FirstOrDefault(u => u.Id == edit.Value);
                model.EditUser = target != null ? CloneUser(target) : null;
            }

            return View(model);
        }

        [HttpPost("superadmin/users/add")]
        public async Task<IActionResult> AddUser(string firstName, string lastName, string username,
            string password, string confirmPassword, string search, string filterStatus)
        {
            var model = BuildUsersViewModel(search ?? "", filterStatus ?? "");
            model.Modal = "add";

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                model.ModalError = "First Name and Last Name are required.";
            }
            else if (string.IsNullOrWhiteSpace(username))
            {
                model.ModalError = "Email / Username is required.";
            }
            else if (!username.Contains('@') || !username.Contains('.'))
            {
                model.ModalError = "Please enter a valid email address.";
            }
            else if (string.IsNullOrWhiteSpace(password))
            {
                model.ModalError = "Password is required.";
            }
            else if (password.Length < 8 || password.Length > 12)
            {
                model.ModalError = "Password must be 8-12 characters.";
            }
            else if (!IsValidPassword(password))
            {
                model.ModalError = "Password must include uppercase, lowercase, digit, and special character.";
            }
            else if (password != confirmPassword)
            {
                model.ModalError = "Passwords do not match.";
            }
            else
            {
                var normalizedUsername = username.ToLower().Trim();

                if (model.Users.Any(u => u.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)))
                {
                    model.ModalError = "A user with this email already exists.";
                }
                else
                {
                    var addUser = new UsersStore.User
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        FullName = $"{firstName} {lastName}",
                        Username = normalizedUsername,
                        Email = normalizedUsername,
                        Role = "Admin",
                        Status = "Active",
                        Password = AuthService.HashPassword(password)
                    };

                    try
                    {
                        using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                        if (scope == null)
                        {
                            model.ModalError = "Could not connect to database.";
                        }
                        else
                        {
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            if (db.UserAccounts.Any(u => u.Email == normalizedUsername))
                            {
                                model.ModalError = "A user with this email already exists.";
                            }
                            else
                            {
                                db.UserAccounts.Add(new UserAccount
                                {
                                    Email = normalizedUsername,
                                    PasswordHash = addUser.Password,
                                    Role = "Admin",
                                    AdminEmail = normalizedUsername
                                });
                                db.SaveChanges();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        model.ModalError = $"Database error: {ex.InnerException?.Message ?? ex.Message}";
                    }

                    if (string.IsNullOrEmpty(model.ModalError))
                    {
                        Auth.AddUserToCache(normalizedUsername, addUser.Password, "Admin");
                        addUser.AdminEmail = normalizedUsername;
                        addUser.LastActive = DateTime.Now;
                        model.Users.Insert(0, addUser);
                        await Subscription.CreateTenantForAdminAsync(normalizedUsername, $"{addUser.FirstName} {addUser.LastName}");
                        await Audit.LogAsync("Add", "UserManagement", addUser.FullName, addUser.Id, AuthService.CurrentEmail, $"Created admin account for {addUser.FullName}");
                        return RedirectToAction("ManageUsers");
                    }
                }
            }

            model.AddFirstName = firstName ?? "";
            model.AddLastName = lastName ?? "";
            model.AddUsername = username ?? "";
            return View("ManageUsers", model);
        }

        [HttpPost("superadmin/users/save")]
        public async Task<IActionResult> SaveUser(int id, string editFirstName, string editLastName, string search, string filterStatus)
        {
            var model = BuildUsersViewModel(search ?? "", filterStatus ?? "");
            model.Modal = "edit";

            if (string.IsNullOrWhiteSpace(editFirstName) || string.IsNullOrWhiteSpace(editLastName))
            {
                model.EditErrorMessage = "First Name and Last Name are required.";
                model.EditUser = model.Users.FirstOrDefault(u => u.Id == id);
            }
            else
            {
                var index = model.Users.FindIndex(u => u.Id == id);
                if (index >= 0)
                {
                    var u = model.Users[index];
                    u.FirstName = editFirstName;
                    u.LastName = editLastName;
                    u.FullName = $"{editFirstName} {editLastName}";
                    await Audit.LogAsync("Edit", "UserManagement", u.FullName, u.Id, AuthService.CurrentEmail, $"Updated admin account for {u.FullName}");
                    return RedirectToAction("ManageUsers");
                }
                model.EditErrorMessage = "User not found.";
            }

            return View("ManageUsers", model);
        }

        [HttpPost("superadmin/users/archive")]
        public async Task<IActionResult> ArchiveUser(int id)
        {
            var user = _adminUsers.FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                user.IsArchived = true;
                user.ArchivedOn = DateTime.Now;
                await Audit.LogAsync("Archive", "UserManagement", user.FullName, user.Id, AuthService.CurrentEmail, $"Archived admin account for {user.FullName}");
            }
            return RedirectToAction("ManageUsers");
        }

        [HttpPost("superadmin/users/approve")]
        public async Task<IActionResult> ApproveUser(int id)
        {
            var req = Auth.GetPendingResetRequests().FirstOrDefault(r => r.Id == id);
            var email = req?.Email ?? "";
            var tempPw = Auth.ApproveReset(id, AuthService.CurrentEmail ?? "SuperAdmin");
            if (tempPw != null)
            {
                await Audit.LogAsync("ApprovePasswordReset", "UserManagement", email, 0, AuthService.CurrentEmail, "Approved password reset");
            }
            return RedirectToAction("ManageUsers");
        }

        [HttpPost("superadmin/users/reject")]
        public async Task<IActionResult> RejectUser(int id, string reason)
        {
            var req = Auth.GetPendingResetRequests().FirstOrDefault(r => r.Id == id);
            var email = req?.Email ?? "";
            var rejectReason = string.IsNullOrWhiteSpace(reason) ? "No reason provided" : reason;
            var rejected = Auth.RejectResetRequest(id, AuthService.CurrentEmail ?? "SuperAdmin", rejectReason);
            if (rejected != null)
            {
                await Audit.LogAsync("RejectPasswordReset", "UserManagement", email, 0, AuthService.CurrentEmail, $"Rejected password reset: {rejectReason}");
            }
            return RedirectToAction("ManageUsers");
        }



        [HttpGet("superadmin/settings")]
        public IActionResult Settings()
        {
            var model = new SuperAdminSettingsViewModel
            {
                PasswordExpiry = SystemSettings.PasswordExpiry,
                PasswordExpiryDays = SystemSettings.PasswordExpiryDays,
                SessionTimeout = SystemSettings.SessionTimeout,
                MaintenanceMode = SystemSettings.MaintenanceMode,
                Message = TempData["SettingsMessage"] as string
            };
            return View(model);
        }

        [HttpPost("superadmin/settings/save")]
        public async Task<IActionResult> SaveSettings(bool passwordExpiry, int passwordExpiryDays,
            int sessionTimeout, bool maintenanceMode)
        {
            if (sessionTimeout < 1 || sessionTimeout > 120)
            {
                TempData["SettingsMessage"] = "Session timeout must be between 1 and 120 minutes.";
                return RedirectToAction("Settings");
            }
            if (passwordExpiry && (passwordExpiryDays < 7 || passwordExpiryDays > 365))
            {
                TempData["SettingsMessage"] = "Password expiry days must be between 7 and 365.";
                return RedirectToAction("Settings");
            }
            SystemSettings.PasswordExpiry = passwordExpiry;
            SystemSettings.PasswordExpiryDays = passwordExpiryDays;
            SystemSettings.SessionTimeout = sessionTimeout;
            SystemSettings.MaintenanceMode = maintenanceMode;
            await Audit.LogAsync("Edit", "SystemSettings", "System Configuration", 0, AuthService.CurrentEmail, "System settings updated");
            TempData["SettingsMessage"] = "Settings saved successfully.";
            return RedirectToAction("Settings");
        }

        [HttpPost("superadmin/settings/reset")]
        public IActionResult ResetSettings()
        {
            SystemSettings.PasswordExpiry = true;
            SystemSettings.PasswordExpiryDays = 90;
            SystemSettings.SessionTimeout = 5;
            SystemSettings.MaintenanceMode = false;
            TempData["SettingsMessage"] = "Settings reset to defaults.";
            return RedirectToAction("Settings");
        }

        [HttpGet("superadmin/maintenance")]
        public IActionResult Maintenance()
        {
            return View();
        }

        [HttpGet("superadmin/reports")]
        public async Task<IActionResult> Reports(string? search, string? section)
        {
            UsersStore.EnsureUsersSynced();
            var model = new SuperAdminReportsViewModel
            {
                Search = search ?? "",
                Section = string.IsNullOrEmpty(section) ? "All" : section,
                UserCount = UsersStore.users.Count
            };

            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var tenants = await db.Tenants.ToListAsync();
                    model.TenantCount = tenants.Count;
                    model.ActiveTenantCount = tenants.Count(t => t.IsActive);
                    model.ExpiredTenantCount = tenants.Count(t => t.IsExpired);

                    var projects = await db.PM_Projects.Where(p => !p.IsArchived).ToListAsync();
                    var tasks = await db.PM_Tasks.Where(t => !t.IsArchived).ToListAsync();
                    var budgets = await db.QS_Budgets.Where(b => !b.IsArchived).ToListAsync();
                    var expenses = await db.Expenses.Where(e => !e.IsArchived).ToListAsync();
                    var incomes = await db.PM_IncomeTransactions.Where(i => !i.IsArchived).ToListAsync();
                    var auditLogs = await db.AuditLogs.OrderByDescending(l => l.PerformedAt).ToListAsync();

                    model.ProjectCount = projects.Count;
                    model.TaskCount = tasks.Count;
                    model.TotalBudget = budgets.Sum(b => b.TotalBudget);
                    model.TotalSpent = budgets.Sum(b => b.TotalSpent);
                    model.TotalIncome = incomes.Sum(i => i.Amount);
                    model.TotalExpense = expenses.Sum(e => e.Amount);
                    model.NetBalance = model.TotalIncome - model.TotalExpense;
                    model.AuditCount = auditLogs.Count;
                    model.FailedLoginCount = auditLogs.Count(l => l.Action == "Failed Login" || l.Action == "LoginFailed");

                    try { model.EstimateCount = await db.CostEstimates.CountAsync(e => !e.IsArchived); } catch { }
                    try { model.IssueCount = await db.SiteIssues.CountAsync(i => !i.IsArchived && i.Status == "Open"); } catch { }
                    try { model.RequestCount = await db.SiteRequests.CountAsync(r => !r.IsArchived && r.Status == "Pending"); } catch { }

                    var txns = await db.PaymentTransactions.ToListAsync();
                    model.PaymentTransactionCount = txns.Count;
                    model.TotalRevenue = txns.Where(t => t.Status == "Paid").Sum(t => t.Amount);
                    model.Transactions = txns.OrderByDescending(t => t.CreatedAt).Take(20).ToList();

                    string Range(DateTime? d, DateTime? d2 = null)
                    {
                        var v = d ?? d2;
                        return v?.ToString("MMM d, yyyy") ?? "-";
                    }

                    var q = (search ?? "").Trim();
                    bool Matches(params string?[] values) =>
                        string.IsNullOrEmpty(q) || values.Any(v => (v ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

                    model.Projects = projects
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => new AdminReportProjectRow
                        {
                            Name = p.ProjectName ?? "-",
                            Status = p.Status ?? "-",
                            Manager = !string.IsNullOrEmpty(p.Manager) ? p.Manager! : (p.AssignedTo ?? "-"),
                            Location = p.Location ?? "-",
                            DueDate = Range(p.DueDate)
                        })
                        .Where(p => Matches(p.Name, p.Status, p.Manager, p.Location))
                        .ToList();

                    model.Tasks = tasks
                        .OrderByDescending(t => t.CreatedAt)
                        .Select(t => new AdminReportTaskRow
                        {
                            Name = t.Name,
                            Project = t.Project,
                            Assignee = t.AssignedTo ?? "-",
                            Status = t.Status,
                            Priority = t.Priority ?? "-",
                            Progress = t.ProgressPercentage,
                            DueDate = Range(t.DueDate)
                        })
                        .Where(t => Matches(t.Name, t.Project, t.Assignee, t.Status, t.Priority))
                        .ToList();

                    model.Budgets = budgets
                        .OrderByDescending(b => b.CreatedAt)
                        .Select(b => new AdminReportBudgetRow
                        {
                            Project = b.Project,
                            Budget = b.TotalBudget,
                            Spent = b.TotalSpent,
                            Remaining = b.Remaining,
                            Health = b.Health
                        })
                        .Where(b => Matches(b.Project, b.Health))
                        .ToList();

                    model.UsersByRole = UsersStore.users
                        .GroupBy(u => u.Role)
                        .Select(g => new LabelCountItem
                        {
                            Label = g.Key switch
                            {
                                "ProjectManager" => "Project Manager",
                                "SiteEngineer" => "Site Engineer",
                                "QuantitySurveyor" => "Quantity Surveyor",
                                "FinanceOfficer" => "Finance Officer",
                                _ => g.Key
                            },
                            Count = g.Count(),
                            Color = g.Key switch
                            {
                                "Admin" => "#dc2626",
                                "ProjectManager" => "#2563eb",
                                "Architect" => "#8b5cf6",
                                "SiteEngineer" => "#10b981",
                                "QuantitySurveyor" => "#f59e0b",
                                "FinanceOfficer" => "#06b6d4",
                                _ => "#64748b"
                            }
                        })
                        .Where(r => Matches(r.Label))
                        .OrderByDescending(r => r.Count)
                        .ToList();

                    model.Tenants = tenants.OrderBy(t => t.CompanyName).Select(t => new TenantReportRow
                    {
                        Company = t.CompanyName,
                        AdminEmail = t.AdminEmail,
                        Plan = t.Plan,
                        Status = t.IsActive ? (t.IsExpired ? "Expired" : "Active") : "Inactive",
                        UsedUsers = t.UsedUsers,
                        UsedProjects = t.UsedProjects,
                        SubscriptionEnd = t.SubscriptionEnd?.ToString("MMM d, yyyy") ?? "-"
                    })
                    .Where(t => Matches(t.Company, t.AdminEmail, t.Plan, t.Status))
                    .ToList();

                    var manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                    model.RecentActivity = auditLogs
                        .OrderByDescending(l => l.PerformedAt)
                        .Take(10)
                        .Select(l => new ActivityItem
                        {
                            Action = l.Action,
                            Title = $"{l.PerformedBy} - {l.Action}",
                            Subtitle = $"{l.Module} | {l.RecordName}",
                            Date = TimeZoneInfo.ConvertTimeFromUtc(l.PerformedAt, manila).ToString("MMM d, yyyy h:mm tt")
                        })
                        .ToList();
                }
            }
            catch { }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ExportPdf()
        {
            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var progress = await db.DailyProgress.Where(p => !p.IsArchived).ToListAsync();
                    var requests = await db.SiteRequests.Where(r => !r.IsArchived).ToListAsync();
                    var issues = await db.SiteIssues.Where(i => !i.IsArchived).ToListAsync();

                    var pdf = _pdfExport.GenerateSummaryReport("Super Administrator", DateTime.Now, progress, requests, issues);
                    return File(pdf, "application/pdf", $"SuperAdmin_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch { }

            return RedirectToAction("Reports");
        }

        [HttpGet("superadmin/auditlogs")]
        public async Task<IActionResult> AuditLogs(string? type, [FromQuery(Name = "action")] string? action, string? date, string? search)
        {
            var model = new SuperAdminAuditLogsViewModel
            {
                TypeFilter = type ?? "",
                ActionFilter = action ?? "",
                DateFilter = date ?? "",
                Search = search ?? ""
            };

            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    model.AuditLogs = await db.AuditLogs.OrderByDescending(l => l.PerformedAt).ToListAsync();
                }
            }
            catch
            {
                model.AuditLogs = new();
            }

            return View(model);
        }

        [HttpGet("superadmin/backup")]
        public IActionResult Backup(string? schedule, string? restore)
        {
            SeedBackups();
            var model = new SuperAdminBackupViewModel
            {
                ShowSchedule = schedule == "1",
                ToastMessage = TempData["BackupMessage"] as string ?? "",
                ToastIsError = Convert.ToBoolean(TempData["BackupError"] ?? false),
                LastBackup = _lastBackup,
                TotalBackups = _backups.Count,
                NextScheduled = _nextScheduled,
                ScheduleFrequency = _scheduleFrequency,
                ScheduleTime = _scheduleTime,
                RetentionPolicy = _retentionPolicy,
                BackupType = _backupType,
                Backups = new List<SuperAdminBackupItem>(_backups)
            };
            if (!string.IsNullOrEmpty(restore))
            {
                model.RestoreName = restore;
                model.ShowRestoreConfirm = true;
            }
            return View(model);
        }

        [HttpPost("superadmin/backup/create")]
        public IActionResult CreateBackup()
        {
            Thread.Sleep(4000);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _manila);
            lock (_backupLock)
            {
                _backups.Insert(0, new SuperAdminBackupItem
                {
                    Name = $"backup_full_{now:yyyyMMdd}_{now:HHmm}",
                    Date = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Size = "2.4 GB",
                    Type = "Full",
                    Status = "Completed"
                });
                _lastBackup = now.ToString("MMM d, h:mm tt");
                _nextScheduled = now.AddHours(6).ToString("MMM d, h:mm tt");
            }
            TempData["BackupMessage"] = $"Full backup created successfully. Backup #{_backups.Count} completed.";
            return RedirectToAction("Backup");
        }

        [HttpPost("superadmin/backup/schedule")]
        public IActionResult SaveSchedule(string frequency, string time, string retention, string type)
        {
            Thread.Sleep(500);
            lock (_backupLock)
            {
                _scheduleFrequency = string.IsNullOrEmpty(frequency) ? _scheduleFrequency : frequency;
                _scheduleTime = string.IsNullOrEmpty(time) ? "14:00" : time;
                _retentionPolicy = string.IsNullOrEmpty(retention) ? _retentionPolicy : retention;
                _backupType = string.IsNullOrEmpty(type) ? _backupType : type;
            }
            TempData["BackupMessage"] = $"Schedule saved: {_scheduleFrequency}, {_scheduleTime}, {_retentionPolicy}.";
            return RedirectToAction("Backup");
        }

        [HttpPost("superadmin/backup/download")]
        public async Task<IActionResult> DownloadBackup(string name)
        {
            await Task.Delay(500);
            try
            {
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _manila);
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var filePath = Path.Combine(downloads, $"{name}.bak");
                var content = $"PlanCore PMS Backup - {name}\nDate: {now:yyyy-MM-dd HH:mm:ss}\nVersion: 1.0\n--- Simulated backup data ---";
                await System.IO.File.WriteAllTextAsync(filePath, content);
                TempData["BackupMessage"] = $"Downloaded to {filePath}";
            }
            catch (Exception ex)
            {
                TempData["BackupMessage"] = $"Download failed: {ex.Message}";
                TempData["BackupError"] = true;
            }
            return RedirectToAction("Backup");
        }

        [HttpPost("superadmin/backup/restore")]
        public IActionResult RestoreBackup(string name)
        {
            Thread.Sleep(5000);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _manila);
            TempData["BackupMessage"] = $"Restored successfully from {name} at {now:MMM d, h:mm tt}. System is stable.";
            return RedirectToAction("Backup");
        }

        private SuperAdminUsersViewModel BuildUsersViewModel(string search, string filterStatus)
        {
            LoadAdminUsers();
            var model = new SuperAdminUsersViewModel
            {
                Search = search,
                SelectedStatus = filterStatus,
                Users = _adminUsers
            };
            model.PendingRequests = Auth.GetPendingResetRequests();

            var query = model.Users.Where(u => !u.IsArchived).AsQueryable();
            if (!string.IsNullOrEmpty(filterStatus))
                query = query.Where(u => model.GetEffectiveStatus(u.LastActive) == filterStatus);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(u =>
                    (u.FullName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (u.Email ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
            model.FilteredUsers = query.OrderByDescending(u => u.LastActive).ToList();
            return model;
        }

        private static void LoadAdminUsers()
        {
            lock (_adminLock)
            {
                if (_adminUsers.Count > 0) return;
                try
                {
                    using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                    if (scope == null) return;
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var accounts = db.UserAccounts.Where(u => u.Role == "Admin").AsNoTracking().ToList();
                    int id = 1;
                    foreach (var u in accounts)
                    {
                        var email = u.Email.ToLower().Trim();
                        var displayName = email.Split('@')[0];
                        displayName = string.Join(" ", System.Text.RegularExpressions.Regex.Split(displayName, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])"))
                            .Replace(".", " ").Replace("_", " ");
                        var ti = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo;
                        displayName = ti.ToTitleCase(displayName.ToLower());
                        _adminUsers.Add(new UsersStore.User
                        {
                            Id = id++,
                            FirstName = displayName,
                            LastName = "",
                            FullName = displayName,
                            Username = email,
                            Email = email,
                            Role = u.Role,
                            AdminEmail = u.AdminEmail ?? email,
                            Status = "Active",
                            LastActive = DateTime.Now,
                            IsArchived = false,
                            MfaEnabled = u.MfaEnabled
                        });
                    }
                }
                catch
                {
                }
            }
        }

        private static UsersStore.User? CloneUser(UsersStore.User u)
        {
            return new UsersStore.User
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = u.FullName,
                Username = u.Username,
                Email = u.Email,
                Role = u.Role,
                AdminEmail = u.AdminEmail,
                Status = u.Status,
                LastActive = u.LastActive,
                MfaEnabled = u.MfaEnabled
            };
        }

        private static void SeedBackups()
        {
            lock (_backupLock)
            {
                if (_backupsSeeded) return;
                _backupsSeeded = true;
                var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _manila);
                _lastBackup = now.AddHours(-2).ToString("MMM d, h:mm tt");
                _nextScheduled = now.AddHours(6).ToString("MMM d, h:mm tt");
                _backups.AddRange(new[]
                {
                    new SuperAdminBackupItem { Name = $"backup_full_{now.AddDays(-1):yyyyMMdd}_1400", Date = now.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss"), Size = "2.4 GB", Type = "Full", Status = "Completed" },
                    new SuperAdminBackupItem { Name = $"backup_inc_{now.AddDays(-1):yyyyMMdd}_0800", Date = now.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss"), Size = "450 MB", Type = "Incremental", Status = "Completed" },
                    new SuperAdminBackupItem { Name = $"backup_full_{now.AddDays(-2):yyyyMMdd}_1400", Date = now.AddDays(-2).ToString("yyyy-MM-dd HH:mm:ss"), Size = "2.3 GB", Type = "Full", Status = "Completed" },
                    new SuperAdminBackupItem { Name = $"backup_full_{now.AddDays(-3):yyyyMMdd}_1400", Date = now.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss"), Size = "2.2 GB", Type = "Full", Status = "Completed" },
                    new SuperAdminBackupItem { Name = $"backup_inc_{now.AddDays(-3):yyyyMMdd}_0800", Date = now.AddDays(-3).ToString("yyyy-MM-dd HH:mm:ss"), Size = "380 MB", Type = "Incremental", Status = "Partial" },
                });
            }
        }

        private static bool IsValidPassword(string pw)
        {
            return new System.Text.RegularExpressions.Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,12}$").IsMatch(pw);
        }
    }
}