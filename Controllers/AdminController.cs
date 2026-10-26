using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI;
using PlanCoreMAUI.Data;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using System.Text;
using System.Text.Json;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;
using UsersStore = PlanCoreMAUI.Components.Pages.Admin.Users;

namespace PlanCore.Web.Controllers
{
    // "/admin" role area. Ports Admin\Users.razor ("/admin/users") plus a dashboard
    // hub. Other Admin pages (billing, auditlogs, archived-users, ...) are added here.
    public class AdminController : AppControllerBase
    {
        private readonly PdfExportService _pdfExport;

        public AdminController(AuthService auth, SubscriptionService subscription, AuditService audit, PdfExportService pdfExport)
            : base(auth, subscription, audit)
        {
            _pdfExport = pdfExport;
        }

        protected override string? RequiredRole => "Admin";

        public IActionResult Index()
        {
            return Redirect("/admin/dashboard");
        }

        // ---- User Management ---------------------------------------------------

        [HttpGet]
        public IActionResult Users(string? search, string? filterRole, string? filterStatus,
            string? add, int? view, int? edit, int? archive)
        {
            UsersStore.EnsureUsersSynced();
            UsersStore.User? selected = null;
            UsersStore.User? editTarget = null;

            if (view.HasValue)
                selected = UsersStore.users.FirstOrDefault(u => u.Id == view.Value);
            if (edit.HasValue)
                editTarget = UsersStore.users.FirstOrDefault(u => u.Id == edit.Value);

            var model = new UsersViewModel
            {
                Search = search ?? "",
                SelectedRole = filterRole ?? "",
                SelectedStatus = filterStatus ?? "",
                Modal = add == "1" ? "add" : null,
                ModalId = archive,
                SelectedUser = selected,
                EditUser = editTarget != null ? CloneEditable(editTarget) : null,
                ModalError = "",
                EditErrorMessage = ""
            };

            if (selected != null)
                model.Modal = "view";
            if (editTarget != null)
                model.Modal = "edit";
            if (archive.HasValue)
                model.Modal = "archive";

            return View(model);
        }

        [HttpPost("admin/users/add")]
        public async Task<IActionResult> AddUser(string firstName, string lastName, string username,
            string password, string confirmPassword, string role, string status)
        {
            UsersStore.EnsureUsersSynced();

            var model = new UsersViewModel { Modal = "add" };

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

                if (UsersStore.users.Any(u => u.Username.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase)) ||
                    Auth.EmailExists(normalizedUsername))
                {
                    model.ModalError = "A user with this email already exists.";
                }
                else
                {
                    var (canAdd, seatMsg) = Subscription.CanAddUser(role);
                    if (!canAdd)
                    {
                        model.ModalError = seatMsg;
                    }
                    else
                    {
                        var addUser = new UsersStore.User
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Role = role,
                            Status = string.IsNullOrEmpty(status) ? "Active" : status,
                            Username = normalizedUsername,
                            Email = normalizedUsername,
                            FullName = $"{firstName} {lastName}",
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
                                        Role = addUser.Role,
                                        AdminEmail = AuthService.CurrentEmail
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
                            Auth.AddUserToCache(normalizedUsername, addUser.Password, addUser.Role);
                            Subscription.IncrementUserCount();
                            addUser.Id = UsersStore.users.Count > 0 ? UsersStore.users.Max(u => u.Id) + 1 : 1;
                            addUser.LastActive = DateTime.Now;
                            UsersStore.users.Insert(0, addUser);
                            await Audit.LogAsync("Add", "UserManagement", addUser.FullName, addUser.Id, AuthService.CurrentEmail, $"Created user account for {addUser.FullName}");
                            return RedirectToAction("Users");
                        }
                    }
                }
            }

            // Keep the modal open with the error + entered values.
            model.AddFirstName = firstName;
            model.AddLastName = lastName;
            model.AddUsername = username ?? "";
            model.AddRole = role;
            model.AddStatus = string.IsNullOrEmpty(status) ? "Active" : status;
            return View("Users", model);
        }

        [HttpPost("admin/users/save")]
        public async Task<IActionResult> SaveUser(int id, string editFirstName, string editLastName,
            string editRole, string editStatus)
        {
            UsersStore.EnsureUsersSynced();

            var model = new UsersViewModel { Modal = "edit" };

            if (string.IsNullOrWhiteSpace(editFirstName) || string.IsNullOrWhiteSpace(editLastName))
            {
                model.EditErrorMessage = "First Name and Last Name are required.";
            }
            else
            {
                var index = UsersStore.users.FindIndex(u => u.Id == id);
                if (index >= 0)
                {
                    var u = UsersStore.users[index];
                    u.FirstName = editFirstName;
                    u.LastName = editLastName;
                    u.FullName = $"{editFirstName} {editLastName}";
                    u.Role = editRole;
                    u.Status = string.IsNullOrEmpty(editStatus) ? u.Status : editStatus;

                    await Audit.LogAsync("Edit", "UserManagement", u.FullName, u.Id, AuthService.CurrentEmail, $"Updated user account for {u.FullName}");
                    return RedirectToAction("Users");
                }
                model.EditErrorMessage = "User not found.";
            }

            model.SelectedUser = UsersStore.users.FirstOrDefault(u => u.Id == id);
            return View("Users", model);
        }

        [HttpPost("admin/users/archive")]
        public async Task<IActionResult> ArchiveUser(int id)
        {
            var user = UsersStore.users.FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                user.IsArchived = true;
                user.ArchivedOn = DateTime.Now;
                await Audit.LogAsync("Archive", "UserManagement", user.FullName, user.Id, AuthService.CurrentEmail, $"Archived user account for {user.FullName}");
            }
            return RedirectToAction("Users");
        }

        public async Task<IActionResult> Dashboard(string? filter)
        {
            UsersStore.EnsureUsersSynced();

            var model = new DashboardViewModel
            {
                DisplayName = Auth.CurrentDisplayName ?? "",
                Filter = string.IsNullOrEmpty(filter) ? "All" : filter,
                TotalUsersCount = UsersStore.users.Count
            };

            List<AuditLog> auditLogs = new();
            List<PM_Projects> projects = new();
            List<PM_Tasks> tasks = new();

            using (var scope = MauiProgram.GetServiceProvider()?.CreateScope())
            {
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var adminEmail = TenantScope.OwnerKey(db);
                    try { auditLogs = await db.AuditLogs.Where(l => adminEmail == null || l.AdminEmail == adminEmail).OrderByDescending(l => l.PerformedAt).ToListAsync(); } catch { }
                    try { projects = await db.PM_Projects.Where(p => !p.IsArchived && (adminEmail == null || p.AdminEmail == adminEmail)).ToListAsync(); } catch { }
                    try { tasks = await db.PM_Tasks.Where(t => !t.IsArchived && (adminEmail == null || t.AdminEmail == adminEmail)).ToListAsync(); } catch { }
                    try { model.IssueCount = await db.SiteIssues.CountAsync(i => !i.IsArchived && i.Status == "Open" && (adminEmail == null || i.AdminEmail == adminEmail)); } catch { }
                    try { model.RequestCount = await db.SiteRequests.CountAsync(r => !r.IsArchived && r.Status == "Pending" && (adminEmail == null || r.AdminEmail == adminEmail)); } catch { }
                    try { model.EstimateCount = await db.CostEstimates.CountAsync(e => !e.IsArchived && (adminEmail == null || e.AdminEmail == adminEmail)); } catch { }
                    try
                    {
                        var budgets = await db.QS_Budgets.Where(b => !b.IsArchived && (adminEmail == null || b.AdminEmail == adminEmail)).ToListAsync();
                        model.TotalBudgetValue = budgets.Sum(b => b.TotalBudget);
                    }
                    catch { }
                    try
                    {
                        var expenses = await db.Expenses.Where(e => !e.IsArchived && (adminEmail == null || e.AdminEmail == adminEmail)).ToListAsync();
                        model.TotalExpenseValue = expenses.Sum(e => e.Amount);
                    }
                    catch { }
                    try
                    {
                        var incomes = await db.PM_IncomeTransactions.Where(i => !i.IsArchived && (adminEmail == null || i.AdminEmail == adminEmail)).ToListAsync();
                        model.TotalIncomeValue = incomes.Sum(i => i.Amount);
                    }
                    catch { }
                    try { model.DesignCount = await db.ArchitectDesigns.CountAsync(d => !d.IsArchived && (adminEmail == null || d.AdminEmail == adminEmail)); } catch { }
                    try { model.CashFlowCount = await db.CashFlows.CountAsync(c => !c.IsArchived && (adminEmail == null || c.AdminEmail == adminEmail)); } catch { }
                }
            }

            model.ProjectCount = projects.Count;
            model.ActiveTaskCount = tasks.Count(t => t.Status != "Completed");

            var filteredAuditLogs = auditLogs.Where(l => model.Filter == "All" || DateMatches(l.PerformedAt, model.Filter)).ToList();
            model.AuditCount = filteredAuditLogs.Count;

            model.RoleCounts = UsersStore.users
                .GroupBy(u => u.Role)
                .Select(g => new LabelCountItem
                {
                    Label = g.Key switch
                    {
                        "Admin" => "Admin",
                        "ProjectManager" => "Project Manager",
                        "Architect" => "Architect",
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
                .OrderByDescending(r => r.Count)
                .ToList();

            var sevenDays = Enumerable.Range(0, 7).Select(i => DateTime.UtcNow.Date.AddDays(-i)).Reverse().ToList();
            model.DailyActivity = sevenDays.Select(day => new LabelCountItem
            {
                Label = day.ToString("ddd"),
                Count = filteredAuditLogs.Count(l => l.PerformedAt.Date == day)
            }).ToList();

            var moduleColors = new Dictionary<string, string>
            {
                { "ProjectPlanning", "#2563eb" },
                { "TaskScheduling", "#7c3aed" },
                { "CostEstimation", "#f59e0b" },
                { "Budget", "#ca8a04" },
                { "Expenses", "#ef4444" },
                { "Income", "#16a34a" },
                { "SiteIssues", "#ea580c" },
                { "SiteRequests", "#06b6d4" },
                { "ArchitectDesign", "#6366f1" },
                { "ArchitectRevision", "#a855f7" },
                { "CashFlow", "#0d9488" },
                { "Authentication", "#64748b" }
            };
            model.ModuleActivity = filteredAuditLogs
                .GroupBy(l => l.Module)
                .Select(g => new LabelCountItem
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Color = moduleColors.GetValueOrDefault(g.Key, "#94a3b8")
                })
                .OrderByDescending(m => m.Count)
                .Take(8)
                .ToList();
            model.ModuleActivityMax = model.ModuleActivity.Any() ? model.ModuleActivity.Max(m => m.Count) : 1;

            model.ProjectStatuses = projects
                .GroupBy(p => p.Status ?? "Unknown")
                .Select(g => new LabelCountItem
                {
                    Label = g.Key,
                    Count = g.Count(),
                    Color = g.Key switch
                    {
                        "Planning" => "#64748b",
                        "In Progress" => "#2563eb",
                        "On Track" => "#16a34a",
                        "Delayed" => "#ea580c",
                        "Completed" => "#10b981",
                        "On Hold" => "#f59e0b",
                        _ => "#94a3b8"
                    }
                })
                .OrderByDescending(s => s.Count)
                .ToList();

            var manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            model.RecentActivity = filteredAuditLogs
                .OrderByDescending(l => l.PerformedAt)
                .Take(8)
                .Select(l => new ActivityItem
                {
                    Action = l.Action,
                    Title = $"{l.PerformedBy} - {l.Action}",
                    Subtitle = $"{l.Module} | {l.RecordName}",
                    Date = TimeZoneInfo.ConvertTimeFromUtc(l.PerformedAt, manila).ToString("MMM d, h:mm tt")
                })
                .ToList();

            return View(model);
        }

        public IActionResult Roles(string? search)
        {
            var roleUsers = new List<RoleUser>
            {
                new RoleUser { Id = 1, FullName = "Admin User", Email = "admin@plancore.com", Role = "Admin", AssignedDate = DateTime.Now.AddMonths(-6) },
                new RoleUser { Id = 2, FullName = "John Smith", Email = "pm@plancore.com", Role = "ProjectManager", AssignedDate = DateTime.Now.AddMonths(-4) },
                new RoleUser { Id = 3, FullName = "Jane Doe", Email = "architect@plancore.com", Role = "Architect", AssignedDate = DateTime.Now.AddMonths(-3) },
                new RoleUser { Id = 4, FullName = "Mike Johnson", Email = "engineer@plancore.com", Role = "SiteEngineer", AssignedDate = DateTime.Now.AddMonths(-2) },
                new RoleUser { Id = 5, FullName = "Sarah Williams", Email = "qs@plancore.com", Role = "QuantitySurveyor", AssignedDate = DateTime.Now.AddMonths(-2) },
                new RoleUser { Id = 6, FullName = "Robert Brown", Email = "finance@plancore.com", Role = "FinanceOfficer", AssignedDate = DateTime.Now.AddMonths(-1) }
            };

            var model = new RolesViewModel { Search = search ?? "" };

            int CountRole(string roleKey) => roleUsers.Count(u => u.Role == roleKey);

            model.RoleCards = new List<RoleCard>
            {
                new RoleCard { Name = "Admin", Icon = "fas fa-shield-alt", Color = "#dc2626", ColorBG = "#fee2e2", Description = "Full system access and control.", UserCount = CountRole("Admin") },
                new RoleCard { Name = "Project Manager", Icon = "fas fa-tasks", Color = "#2563eb", ColorBG = "#dbeafe", Description = "Manage projects and teams.", UserCount = CountRole("ProjectManager") },
                new RoleCard { Name = "Architect", Icon = "fas fa-drafting-compass", Color = "#8b5cf6", ColorBG = "#f5f3ff", Description = "Design management and revisions.", UserCount = CountRole("Architect") },
                new RoleCard { Name = "Site Engineer", Icon = "fas fa-hard-hat", Color = "#10b981", ColorBG = "#dcfce7", Description = "Site operations and tracking.", UserCount = CountRole("SiteEngineer") },
                new RoleCard { Name = "Quantity Surveyor", Icon = "fas fa-calculator", Color = "#f59e0b", ColorBG = "#fef3c7", Description = "Cost estimation and budget.", UserCount = CountRole("QuantitySurveyor") },
                new RoleCard { Name = "Finance Officer", Icon = "fas fa-coins", Color = "#06b6d4", ColorBG = "#cffafe", Description = "Financial management.", UserCount = CountRole("FinanceOfficer") }
            };

            var searchQuery = search ?? "";
            if (!string.IsNullOrEmpty(searchQuery))
            {
                roleUsers = roleUsers
                    .Where(u => (u.FullName ?? "").Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                                (u.Email ?? "").Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            model.Users = roleUsers;

            return View(model);
        }

        public IActionResult AllModules()
        {
            return View();
        }

        public IActionResult Billing(string? upgrade, string? downgrade, int? enterprise)
        {
            // Admin Billing module removed.
            return Redirect("/admin/users");
        }

        [HttpPost("/admin/billing/upgrade")]
        public async Task<IActionResult> BillingUpgrade(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return Redirect("/admin/billing");
            var planDef = SubscriptionPlan.GetPlan(plan);
            var (msg, receipt) = await CompleteUpgrade(plan, planDef.PriceMonthly, "monthly", "simulated", "Simulated");
            TempData["SaveMessage"] = msg;
            if (receipt != null) TempData["ReceiptJson"] = JsonSerializer.Serialize(receipt);
            return Redirect("/admin/billing");
        }

        [HttpPost("/admin/billing/downgrade")]
        public async Task<IActionResult> BillingDowngrade(string plan)
        {
            if (string.IsNullOrEmpty(plan)) return Redirect("/admin/billing");
            var (msg, receipt) = await ConfirmDowngrade(plan);
            TempData["SaveMessage"] = msg;
            if (receipt != null) TempData["ReceiptJson"] = JsonSerializer.Serialize(receipt);
            return Redirect("/admin/billing");
        }

        [HttpPost("/admin/billing/enterprise")]
        public async Task<IActionResult> BillingEnterprise()
        {
            var (msg, receipt) = await CompleteUpgrade("Enterprise", 8999, "yearly", "admin_activation", "Admin");
            TempData["SaveMessage"] = msg;
            if (receipt != null) TempData["ReceiptJson"] = JsonSerializer.Serialize(receipt);
            return Redirect("/admin/billing");
        }

        [HttpPost("/admin/billing/receipt")]
        public IActionResult BillingReceipt(BillingReceiptInfo receipt, string? mode)
        {
            var print = string.Equals(mode, "print", StringComparison.OrdinalIgnoreCase);
            var html = BuildReceiptHtml(receipt, print);
            if (print)
                return Content(html, "text/html");
            return File(Encoding.UTF8.GetBytes(html), "text/html", $"receipt_{receipt.InvoiceNumber}.html");
        }

        public async Task<IActionResult> Reports(string? search, string? section)
        {
            UsersStore.EnsureUsersSynced();
            var model = new AdminReportsViewModel
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
                    var adminEmail = TenantScope.OwnerKey(db);

                    var projects = await db.PM_Projects.Where(p => !p.IsArchived && (adminEmail == null || p.AdminEmail == adminEmail)).ToListAsync();
                    var tasks = await db.PM_Tasks.Where(t => !t.IsArchived && (adminEmail == null || t.AdminEmail == adminEmail)).ToListAsync();
                    var budgets = await db.QS_Budgets.Where(b => !b.IsArchived && (adminEmail == null || b.AdminEmail == adminEmail)).ToListAsync();
                    var expenses = await db.Expenses.Where(e => !e.IsArchived && (adminEmail == null || e.AdminEmail == adminEmail)).ToListAsync();
                    var incomes = await db.PM_IncomeTransactions.Where(i => !i.IsArchived && (adminEmail == null || i.AdminEmail == adminEmail)).ToListAsync();
                    var auditLogs = await db.AuditLogs.Where(l => adminEmail == null || l.AdminEmail == adminEmail).OrderByDescending(l => l.PerformedAt).ToListAsync();

                    model.ProjectCount = projects.Count;
                    model.TaskCount = tasks.Count;
                    model.TotalBudget = budgets.Sum(b => b.TotalBudget);
                    model.TotalSpent = budgets.Sum(b => b.TotalSpent);
                    model.TotalIncome = incomes.Sum(i => i.Amount);
                    model.TotalExpense = expenses.Sum(e => e.Amount);
                    model.NetBalance = model.TotalIncome - model.TotalExpense;
                    model.AuditCount = auditLogs.Count;

                    try { model.EstimateCount = await db.CostEstimates.CountAsync(e => !e.IsArchived && (adminEmail == null || e.AdminEmail == adminEmail)); } catch { }
                    try { model.IssueCount = await db.SiteIssues.CountAsync(i => !i.IsArchived && i.Status == "Open" && (adminEmail == null || i.AdminEmail == adminEmail)); } catch { }
                    try { model.RequestCount = await db.SiteRequests.CountAsync(r => !r.IsArchived && r.Status == "Pending" && (adminEmail == null || r.AdminEmail == adminEmail)); } catch { }

                    string Range(DateTime? d, DateTime? d2 = null)
                    {
                        var v = d ?? d2;
                        return v?.ToString("MMM d, yyyy") ?? "-";
                    }

                    model.Projects = projects
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => new AdminReportProjectRow
                        {
                            Name = p.ProjectName ?? "-",
                            Status = p.Status ?? "-",
                            Manager = !string.IsNullOrEmpty(p.Manager) ? p.Manager! : (p.AssignedTo ?? "-"),
                            Location = p.Location ?? "-",
                            DueDate = Range(p.DueDate)
                        }).ToList();

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
                        }).ToList();

                    model.Budgets = budgets
                        .OrderByDescending(b => b.CreatedAt)
                        .Select(b => new AdminReportBudgetRow
                        {
                            Project = b.Project,
                            Budget = b.TotalBudget,
                            Spent = b.TotalSpent,
                            Remaining = b.Remaining,
                            Health = b.Health
                        }).ToList();

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
                        .OrderByDescending(r => r.Count)
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
                    var adminEmail = TenantScope.OwnerKey(db);
                    var progress = await db.DailyProgress.Where(p => !p.IsArchived && (adminEmail == null || p.AdminEmail == adminEmail)).ToListAsync();
                    var requests = await db.SiteRequests.Where(r => !r.IsArchived && (adminEmail == null || r.AdminEmail == adminEmail)).ToListAsync();
                    var issues = await db.SiteIssues.Where(i => !i.IsArchived && (adminEmail == null || i.AdminEmail == adminEmail)).ToListAsync();

                    var pdf = _pdfExport.GenerateSummaryReport("Administrator", DateTime.Now, progress, requests, issues);
                    return File(pdf, "application/pdf", $"Admin_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
                }
            }
            catch { }

            return RedirectToAction("Reports");
        }

        public async Task<IActionResult> AuditLogs(string? type, [FromQuery(Name = "action")] string? action, string? date, string? search)
        {
            var model = new AuditLogsViewModel
            {
                TypeFilter = type ?? "",
                ActionFilter = action ?? "",
                DateFilter = date ?? "",
                Search = search ?? "",
                SuperAdminEmails = AuthService.GetSuperAdminEmails()
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
                model.AuditLogs = new List<AuditLog>();
            }

            return View(model);
        }

        public IActionResult Backup()
        {
            return View();
        }

        public IActionResult Maintenance()
        {
            return View();
        }

        [HttpGet("/admin/archived-users")]
        public IActionResult ArchivedUsers(string? search, int? delete)
        {
            UsersStore.EnsureUsersSynced();
            var model = new ArchivedUsersViewModel
            {
                Search = search ?? "",
                DeleteId = delete,
                Users = UsersStore.users.Where(u => u.IsArchived).OrderByDescending(u => u.ArchivedOn).ToList()
            };

            if (!string.IsNullOrEmpty(model.Search))
            {
                model.Users = model.Users.Where(u =>
                    (u.FullName ?? "").Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    (u.Email ?? "").Contains(model.Search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return View(model);
        }

        [HttpPost("/admin/archived-users/restore")]
        public async Task<IActionResult> RestoreArchivedUser(int id)
        {
            UsersStore.EnsureUsersSynced();
            var user = UsersStore.users.FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                user.IsArchived = false;
                user.ArchivedOn = null;
                await Audit.LogAsync("Restore", "UserManagement", user.FullName, user.Id, AuthService.CurrentEmail, $"Restored user account for {user.FullName}");
            }
            return Redirect("/admin/archived-users");
        }

        [HttpPost("/admin/archived-users/delete")]
        public async Task<IActionResult> DeleteArchivedUser(int id)
        {
            UsersStore.EnsureUsersSynced();
            var user = UsersStore.users.FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                UsersStore.users.Remove(user);
                await Audit.LogAsync("Delete", "UserManagement", user.FullName, user.Id, AuthService.CurrentEmail, $"Permanently deleted user account for {user.FullName}");
            }
            return Redirect("/admin/archived-users");
        }

        private BillingViewModel LoadBilling()
        {
            var model = new BillingViewModel();
            var tenant = Subscription.GetTenant();
            model.NoPlan = tenant == null || string.IsNullOrEmpty(tenant.Plan);
            if (tenant != null)
            {
                model.CurrentPlan = SubscriptionPlan.GetPlan(tenant.Plan);
                model.UsedProjects = tenant.UsedProjects;
                model.MaxProjects = model.CurrentPlan.MaxProjects;
                model.UsedUsers = tenant.UsedUsers;
                model.MaxUsers = model.CurrentPlan.MaxUsers;
                model.BillingCycle = tenant.BillingCycle;
                model.SubscriptionStart = tenant.SubscriptionStart;
                model.SubscriptionEnd = tenant.SubscriptionEnd;
                model.TrialEnd = tenant.TrialEnd;
                model.IsActive = tenant.IsActive;
                model.IsExpired = tenant.IsExpired;
                model.IsOnTrial = tenant.IsOnTrial;

                model.StorageTotal = model.CurrentPlan.StorageGB >= int.MaxValue ? "Unlimited" : $"{model.CurrentPlan.StorageGB} GB";
                var usedBytes = tenant.UsedStorageBytes;
                if (usedBytes >= 1073741824)
                    model.StorageUsed = $"{usedBytes / 1073741824.0:F1} GB";
                else if (usedBytes >= 1048576)
                    model.StorageUsed = $"{usedBytes / 1048576.0:F0} MB";
                else
                    model.StorageUsed = $"{usedBytes / 1024.0:F0} KB";

                SubscriptionService.SetTenantPlan(tenant.Plan);
            }
            else
            {
                model.CurrentPlan = new PlanDefinition { Name = "", MaxProjects = 0, MaxUsers = 0, StorageGB = 0 };
            }

            var tenantId = tenant?.Id ?? 0;
            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    model.PaymentLedger = db.PaymentTransactions
                        .Where(pt => pt.TenantId == tenantId)
                        .OrderByDescending(pt => pt.CreatedAt)
                        .ToList();
                }
            }
            catch { }

            if (model.UsedProjects == 0)
            {
                try
                {
                    using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                    if (scope != null)
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        model.UsedProjects = db.PM_Projects.Count(p => p.AdminEmail == TenantScope.OwnerKey(db));
                        if (db.Tenants.Any())
                        {
                            var t = db.Tenants.First();
                            t.UsedProjects = model.UsedProjects;
                            db.SaveChanges();
                        }
                    }
                }
                catch { }
            }

            if (model.UsedUsers == 0)
            {
                try
                {
                    using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                    if (scope != null)
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        model.UsedUsers = db.UserAccounts.Count();
                        if (db.Tenants.Any())
                        {
                            var t = db.Tenants.First();
                            t.UsedUsers = model.UsedUsers;
                            db.SaveChanges();
                        }
                    }
                }
                catch { }
            }

            return model;
        }

        private async Task<(string Message, BillingReceiptInfo? Receipt)> CompleteUpgrade(string plan, decimal amount, string cycle, string sessionId, string provider)
        {
            try
            {
                var sp = MauiProgram.GetServiceProvider();
                if (sp == null) return ("Error: Service unavailable.", null);
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var tenant = EnsureTenant(db);
                if (tenant == null) return ("Error: No tenant record.", null);

                var oldPlan = tenant.Plan;
                var planDef = SubscriptionPlan.GetPlan(plan);
                if (planDef.PriceMonthly > 0 && planDef.PriceYearly == 0)
                    cycle = "monthly";
                else if (planDef.PriceYearly > 0 && planDef.PriceMonthly == 0)
                    cycle = "yearly";

                tenant.Plan = plan;
                tenant.BillingCycle = cycle;
                tenant.SubscriptionStart = DateTime.Now;
                tenant.SubscriptionEnd = DateTime.Now.AddMonths(cycle == "yearly" ? 12 : 1);
                tenant.LastPaymentAt = DateTime.Now;
                tenant.PaymentRetryCount = 0;
                tenant.IsActive = true;

                db.PaymentTransactions.Add(new PaymentTransaction
                {
                    TenantId = tenant.Id,
                    PaymentProvider = provider,
                    PaymentProviderTransactionId = sessionId,
                    Amount = amount,
                    Currency = "PHP",
                    Status = "Success",
                    Plan = plan,
                    BillingCycle = cycle,
                    CreatedAt = DateTime.Now,
                    PaidAt = DateTime.Now,
                    Notes = $"Upgraded from {oldPlan} to {plan}"
                });

                await db.SaveChangesAsync();
                SubscriptionService.SetTenantPlan(plan);

                var action = oldPlan == plan ? "Renewed" : $"Upgraded from {oldPlan}";
                var receipt = BuildReceipt(plan, amount, cycle, sessionId, provider, action);

                await Audit.LogAsync("Upgrade", "Subscription", plan, 0, AuthService.CurrentEmail ?? "System",
                    $"{action} from previous plan");
                return ($"Successfully {action.ToLower()} to {plan}!", receipt);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return ($"Error: {inner}", null);
            }
        }

        private async Task<(string Message, BillingReceiptInfo? Receipt)> ConfirmDowngrade(string downgradeTargetPlan)
        {
            try
            {
                var sp = MauiProgram.GetServiceProvider();
                if (sp == null) return ("Error: Service unavailable.", null);
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var tenant = EnsureTenant(db);
                if (tenant == null) return ("Error: No tenant record.", null);

                var oldPlan = tenant.Plan;
                var downgradePlanDef = SubscriptionPlan.GetPlan(downgradeTargetPlan);
                var downgradeCycle = "monthly";
                if (downgradePlanDef.PriceMonthly > 0 && downgradePlanDef.PriceYearly == 0)
                    downgradeCycle = "monthly";
                else if (downgradePlanDef.PriceYearly > 0 && downgradePlanDef.PriceMonthly == 0)
                    downgradeCycle = "yearly";

                tenant.Plan = downgradeTargetPlan;
                tenant.BillingCycle = downgradeCycle;
                tenant.PendingPlan = null;
                tenant.PlanChangeEffective = null;

                db.PaymentTransactions.Add(new PaymentTransaction
                {
                    TenantId = tenant.Id,
                    PaymentProvider = "System",
                    PaymentProviderTransactionId = $"downgrade_{DateTime.Now:yyyyMMddHHmmss}",
                    Amount = 0,
                    Currency = "PHP",
                    Status = "Success",
                    Plan = downgradeTargetPlan,
                    BillingCycle = downgradeCycle,
                    CreatedAt = DateTime.Now,
                    PaidAt = DateTime.Now,
                    Notes = $"Downgraded from {oldPlan} to {downgradeTargetPlan}"
                });

                await db.SaveChangesAsync();
                SubscriptionService.SetTenantPlan(downgradeTargetPlan);

                var receipt = BuildReceipt(downgradeTargetPlan, 0, downgradeCycle, $"downgrade_{DateTime.Now:yyyyMMddHHmmss}", "System", $"Downgraded from {oldPlan}");

                await Audit.LogAsync("Downgrade", "Subscription", downgradeTargetPlan, 0, AuthService.CurrentEmail ?? "System",
                    $"Downgraded from {oldPlan} to {downgradeTargetPlan}");
                return ($"Downgraded from {oldPlan} to {downgradeTargetPlan}.", receipt);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                return ($"Error: {inner}", null);
            }
        }

        private static BillingReceiptInfo BuildReceipt(string plan, decimal amount, string cycle, string transactionId, string method, string action)
        {
            return new BillingReceiptInfo
            {
                InvoiceNumber = $"INV-{DateTime.Now:yyyyMMdd-HHmmss}",
                TransactionId = transactionId,
                Plan = plan,
                Amount = amount,
                Cycle = cycle == "yearly" ? "Annual" : "Monthly",
                PeriodStart = DateTime.Now.ToString("MMM dd, yyyy"),
                PeriodEnd = DateTime.Now.AddMonths(cycle == "yearly" ? 12 : 1).ToString("MMM dd, yyyy"),
                Date = DateTime.Now.ToString("MMM dd, yyyy hh:mm tt"),
                Method = method,
                Action = action
            };
        }

        private static string BuildReceiptHtml(BillingReceiptInfo r, bool print)
        {
            var periodLabel = r.Cycle == "Annual" ? "Yearly" : "Monthly";
            var bodyTag = print ? "<body onload='window.print()'>" : "<body>";
            return $@"<html>
<head><meta charset='utf-8'><title>Receipt {r.InvoiceNumber}</title>
<style>
body {{ font-family: Arial, sans-serif; max-width: 600px; margin: 40px auto; padding: 20px; color: #333; }}
.header {{ text-align: center; border-bottom: 2px solid #1e40af; padding-bottom: 20px; margin-bottom: 20px; }}
.header h1 {{ margin: 0; font-size: 24px; color: #1e40af; }}
.header p {{ margin: 4px 0 0; color: #666; font-size: 13px; }}
.row {{ display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #eee; }}
.label {{ font-weight: 600; color: #555; font-size: 13px; }}
.value {{ color: #111; font-size: 14px; }}
.amount {{ font-size: 22px; font-weight: 700; color: #16a34a; }}
.status {{ background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 8px; padding: 12px 16px; text-align: center; margin-top: 20px; }}
.status h3 {{ margin: 0; color: #16a34a; }}
.status p {{ margin: 4px 0 0; color: #166534; font-size: 13px; }}
.footer {{ text-align: center; margin-top: 24px; font-size: 12px; color: #999; }}
</style></head>
{bodyTag}
<div class='header'>
<h1>PlanCore PMS</h1>
<p>Payment Receipt</p>
<p style='font-size:12px;'>{r.InvoiceNumber}</p>
</div>
<div class='row'><span class='label'>Plan</span><span class='value'>{r.Plan}</span></div>
<div class='row'><span class='label'>Amount</span><span class='value amount'>{r.Amount:N0}</span></div>
<div class='row'><span class='label'>Billing Cycle</span><span class='value'>{r.Cycle}</span></div>
<div class='row'><span class='label'>Period</span><span class='value'>{periodLabel}</span></div>
<div class='row'><span class='label'>Date Range</span><span class='value'>{r.PeriodStart} — {r.PeriodEnd}</span></div>
<div class='row'><span class='label'>Payment Method</span><span class='value'>{r.Method}</span></div>
<div class='row'><span class='label'>Transaction ID</span><span class='value' style='font-size:11px;'>{r.TransactionId}</span></div>
<div class='row'><span class='label'>Date</span><span class='value'>{r.Date}</span></div>
<div class='status'><h3>✅ Payment Successful</h3><p>{r.Action}</p></div>
<div class='footer'>Generated by PlanCore PMS on {r.Date}</div>
</body></html>";
        }

        private static bool DateMatches(DateTime date, string filter)
        {
            var today = DateTime.UtcNow;
            return filter switch
            {
                "Today" => date.Date == today.Date,
                "This Week" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                "This Month" => date.Year == today.Year && date.Month == today.Month,
                "This Year" => date.Year == today.Year,
                _ => true
            };
        }

        private static DateTime StartOfWeek(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }

        private static Tenant? EnsureTenant(ApplicationDbContext db)
        {
            var email = AuthService.CurrentEmail;
            if (string.IsNullOrEmpty(email)) return null;
            var existing = db.Tenants.FirstOrDefault(t => t.AdminEmail == email);
            if (existing != null) return existing;
            db.Tenants.Add(new Tenant
            {
                AdminEmail = email,
                CompanyName = email.Split('@')[0],
                Plan = "Basic",
                BillingCycle = "monthly",
                IsActive = true,
                SubscriptionStart = DateTime.Now,
                SubscriptionEnd = DateTime.Now.AddMonths(1),
                CreatedAt = DateTime.Now,
                Address = "",
                ContactNumber = "",
                PaymentProvider = "",
                PaymentProviderCustomerId = "",
                PaymentProviderSubscriptionId = "",
                PendingPlan = ""
            });
            db.SaveChanges();
            return db.Tenants.FirstOrDefault(t => t.AdminEmail == email);
        }

        private static UsersStore.User? CloneEditable(UsersStore.User u)
        {
            if (u == null) return null;
            return new UsersStore.User
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                FullName = u.FullName,
                Username = u.Username,
                Email = u.Email,
                Role = u.Role,
                Status = u.Status,
                LastActive = u.LastActive,
                MfaEnabled = u.MfaEnabled
            };
        }

        private static bool IsValidPassword(string pw)
        {
            return new System.Text.RegularExpressions.Regex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).{8,12}$").IsMatch(pw);
        }
    }
}