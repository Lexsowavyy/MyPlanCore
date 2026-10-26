using System;
using System.Collections.Generic;
using System.Linq;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using UsersStore = PlanCoreMAUI.Components.Pages.Admin.Users;

namespace PlanCore.Web.ViewModels
{
    public class SuperAdminDashboardViewModel
    {
        public string DashboardFilter { get; set; } = "All";
        public List<AuditLog> AuditLogs { get; set; } = new();
        public List<PM_Projects> Projects { get; set; } = new();
        public string LastBackupTime { get; set; } = "";
        public int TotalBackupCount { get; set; } = 24;
        public string SystemHealth { get; set; } = "Healthy";
        public string BackupStatus { get; set; } = "Healthy";
        public string DisplayName { get; set; } = "";

        public List<AuditLog> FilteredAuditLogs => AuditLogs.Where(l => DashboardFilter == "All" || DateMatches(l.PerformedAt)).ToList();

        public int TotalUsersCount => UsersStore.users.Count;
        public int AuditCount => FilteredAuditLogs.Count;
        public int FailedLoginCount => FilteredAuditLogs.Count(l => l.Action == "Failed Login" || l.Action == "LoginFailed");
        public int ActiveAdminCount => UsersStore.users.Count(u => u.Role == "Admin" && u.Status == "Active");
        public int SecurityEventCount => FilteredAuditLogs.Count(l => l.Module == "Authentication" || l.Action == "Failed Login" || l.Action == "SecurityEvent");
        public int SettingsChangeCount => FilteredAuditLogs.Count(l => l.Module == "SystemSettings" || l.Module == "Settings");
        public int ProjectCount => Projects.Count;

        public List<RoleCountItem> RoleCounts => UsersStore.users
            .GroupBy(u => u.Role)
            .Select(g => new RoleCountItem
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

        public List<DailyCountItem> DailyActivity
        {
            get
            {
                var sevenDays = Enumerable.Range(0, 7).Select(i => DateTime.UtcNow.Date.AddDays(-i)).Reverse().ToList();
                return sevenDays.Select(day => new DailyCountItem
                {
                    Label = day.ToString("ddd"),
                    Count = FilteredAuditLogs.Count(l => l.PerformedAt.Date == day)
                }).ToList();
            }
        }

        public List<ModuleCountItem> ModuleActivity
        {
            get
            {
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
                    { "Authentication", "#64748b" },
                };
                return FilteredAuditLogs
                    .GroupBy(l => l.Module)
                    .Select(g => new ModuleCountItem
                    {
                        Label = g.Key,
                        Count = g.Count(),
                        Color = moduleColors.GetValueOrDefault(g.Key, "#94a3b8")
                    })
                    .OrderByDescending(m => m.Count)
                    .Take(8)
                    .ToList();
            }
        }

        public int ModuleActivityMax => ModuleActivity.Any() ? ModuleActivity.Max(m => m.Count) : 1;

        public List<StatusCountItem> ProjectStatuses => Projects
            .GroupBy(p => p.Status ?? "Unknown")
            .Select(g => new StatusCountItem
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

        public List<ActivityFeedItem> RecentActivity
        {
            get
            {
                var manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
                return FilteredAuditLogs
                    .OrderByDescending(l => l.PerformedAt)
                    .Take(8)
                    .Select(l => new ActivityFeedItem
                    {
                        Action = l.Action,
                        Title = $"{l.PerformedBy} - {l.Action}",
                        Subtitle = $"{l.Module} | {l.RecordName}",
                        Date = TimeZoneInfo.ConvertTimeFromUtc(l.PerformedAt, manila).ToString("MMM d, h:mm tt")
                    })
                    .ToList();
            }
        }

        private bool DateMatches(DateTime date)
        {
            var today = DateTime.UtcNow;
            return DashboardFilter switch
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
    }

    public class SuperAdminUsersViewModel
    {
        public string Search { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string? Modal { get; set; }
        public string ModalError { get; set; } = "";
        public string EditErrorMessage { get; set; } = "";
        public string ResetPasswordErrorMessage { get; set; } = "";
        public int? ModalId { get; set; }
        public string ModalEmail { get; set; } = "";
        public string RejectReason { get; set; } = "";

        public List<UsersStore.User> Users { get; set; } = new();
        public List<AuthService.PasswordResetRequest> PendingRequests { get; set; } = new();
        public UsersStore.User? SelectedUser { get; set; }
        public UsersStore.User? EditUser { get; set; }

        public string AddFirstName { get; set; } = "";
        public string AddLastName { get; set; } = "";
        public string AddUsername { get; set; } = "";

        public List<UsersStore.User> FilteredUsers { get; set; } = new();

        public int TotalAdmins => Users.Count(u => u.Role == "Admin" && !u.IsArchived);
        public int ActiveAdmins => Users.Count(u => u.Role == "Admin" && GetEffectiveStatus(u.LastActive) == "Active" && !u.IsArchived);
        public int InactiveAdmins => Users.Count(u => u.Role == "Admin" && GetEffectiveStatus(u.LastActive) == "Inactive" && !u.IsArchived);
        public bool HasPendingRequests => PendingRequests.Any();

        public string GetEffectiveStatus(DateTime? lastActive)
        {
            if (lastActive == null) return "Inactive";
            return (DateTime.Now - lastActive.Value).TotalDays > 90 ? "Inactive" : "Active";
        }

        public string FormatDateTime(DateTime? date)
        {
            return date?.ToString("MMM d, yyyy h:mm tt") ?? "-";
        }
    }

    public class SuperAdminSettingsViewModel
    {
        public bool PasswordExpiry { get; set; }
        public int PasswordExpiryDays { get; set; }
        public int SessionTimeout { get; set; }
        public bool MaintenanceMode { get; set; }
        public string? Message { get; set; }
    }

    public class SuperAdminAuditLogsViewModel
    {
        public string TypeFilter { get; set; } = "";
        public string ActionFilter { get; set; } = "";
        public string DateFilter { get; set; } = "";
        public string Search { get; set; } = "";

        public List<AuditLog> AuditLogs { get; set; } = new();
        public TimeZoneInfo ManilaTz { get; set; } = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

        public int FailedLogins => AuditLogs.Count(l => l.Action == "LoginFailed");
        public int DataChanges => AuditLogs.Count(l => l.Action == "Add" || l.Action == "Edit" || l.Action == "Archive");
        public int SuccessfulLogins => AuditLogs.Count(l => l.Action == "LoginSuccess");

        public List<AuditLog> FilteredLogs
        {
            get
            {
                var query = AuditLogs.AsEnumerable();

                if (!string.IsNullOrEmpty(TypeFilter))
                    query = query.Where(l => GetLogType(l.Action, l.Module) == TypeFilter);

                if (!string.IsNullOrEmpty(ActionFilter))
                    query = query.Where(l => l.Action == ActionFilter);

                if (!string.IsNullOrEmpty(DateFilter))
                {
                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaTz);
                    var start = DateFilter switch
                    {
                        "Today" => now.Date,
                        "ThisWeek" => now.Date.AddDays(-(int)now.DayOfWeek),
                        "ThisMonth" => new DateTime(now.Year, now.Month, 1, 0, 0, 0),
                        "ThisYear" => new DateTime(now.Year, 1, 1, 0, 0, 0),
                        _ => DateTime.MinValue
                    };
                    var end = DateFilter switch
                    {
                        "Today" => start.AddDays(1),
                        "ThisWeek" => start.AddDays(7),
                        "ThisMonth" => start.AddMonths(1),
                        "ThisYear" => start.AddYears(1),
                        _ => DateTime.MaxValue
                    };
                    query = query.Where(l => l.PerformedAt >= start && l.PerformedAt < end);
                }

                if (!string.IsNullOrEmpty(Search))
                    query = query.Where(l =>
                        (l.PerformedBy ?? "").Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                        (l.Module ?? "").Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                        (l.Details ?? "").Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                        (l.RecordName ?? "").Contains(Search, StringComparison.OrdinalIgnoreCase));

                return query.OrderByDescending(l => l.PerformedAt).ToList();
            }
        }

        public string GetActionColor(string action)
        {
            return action switch
            {
                "LoginSuccess" => "#10b981",
                "LoginFailed" => "#dc2626",
                "Add" => "#2563eb",
                "Edit" => "#f59e0b",
                "Archive" => "#8b5cf6",
                _ => "#64748b"
            };
        }

        public string FormatAction(string action)
        {
            return action switch
            {
                "LoginSuccess" => "Login",
                "LoginFailed" => "Failed Login",
                "Add" => "Add",
                "Edit" => "Edit",
                "Archive" => "Archive",
                _ => action
            };
        }

        public string GetActionIcon(string action)
        {
            return action switch
            {
                "LoginSuccess" => "<i class=\"fas fa-sign-in-alt\"></i>",
                "LoginFailed" => "<i class=\"fas fa-exclamation-triangle\"></i>",
                "Add" => "<i class=\"fas fa-plus-circle\"></i>",
                "Edit" => "<i class=\"fas fa-edit\"></i>",
                "Archive" => "<i class=\"fas fa-archive\"></i>",
                _ => "<i class=\"fas fa-circle\"></i>"
            };
        }

        public string GetLogType(string action, string module)
        {
            if (action == "LoginSuccess" || action == "LoginFailed" || module == "Authentication")
                return "System Event";
            return "User Activity";
        }
    }

    public class SuperAdminBackupViewModel
    {
        public bool ShowSchedule { get; set; }
        public string ToastMessage { get; set; } = "";
        public bool ToastIsError { get; set; }

        public string LastBackup { get; set; } = "";
        public int TotalBackups { get; set; }
        public string StorageUsed { get; set; } = "8.2 GB";
        public string NextScheduled { get; set; } = "";

        public string ScheduleFrequency { get; set; } = "Every 6 Hours";
        public string ScheduleTime { get; set; } = "14:00";
        public string RetentionPolicy { get; set; } = "Keep last 30 days";
        public string BackupType { get; set; } = "Full + Incremental";

        public string RestoreName { get; set; } = "";
        public bool ShowRestoreConfirm { get; set; }

        public List<SuperAdminBackupItem> Backups { get; set; } = new();
    }

    public class SuperAdminBackupItem
    {
        public string Name { get; set; } = "";
        public string Date { get; set; } = "";
        public string Size { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class RoleCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public string Color { get; set; } = "";
    }

    public class DailyCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class ModuleCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public string Color { get; set; } = "";
    }

    public class StatusCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public string Color { get; set; } = "";
    }

    public class ActivityFeedItem
    {
        public string Action { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public class SuperAdminReportsViewModel
    {
        public string Search { get; set; } = "";
        public string Section { get; set; } = "All";

        public int TenantCount { get; set; }
        public int ActiveTenantCount { get; set; }
        public int ExpiredTenantCount { get; set; }
        public int UserCount { get; set; }
        public int ProjectCount { get; set; }
        public int TaskCount { get; set; }
        public int EstimateCount { get; set; }
        public int IssueCount { get; set; }
        public int RequestCount { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal NetBalance { get; set; }
        public int PaymentTransactionCount { get; set; }
        public int AuditCount { get; set; }
        public int FailedLoginCount { get; set; }
        public decimal TotalRevenue { get; set; }

        public List<TenantReportRow> Tenants { get; set; } = new();
        public List<PaymentTransaction> Transactions { get; set; } = new();
        public List<AdminReportProjectRow> Projects { get; set; } = new();
        public List<AdminReportTaskRow> Tasks { get; set; } = new();
        public List<AdminReportBudgetRow> Budgets { get; set; } = new();
        public List<LabelCountItem> UsersByRole { get; set; } = new();
        public List<ActivityItem> RecentActivity { get; set; } = new();
    }

    public class TenantReportRow
    {
        public string Company { get; set; } = "";
        public string AdminEmail { get; set; } = "";
        public string Plan { get; set; } = "";
        public string Status { get; set; } = "";
        public int UsedUsers { get; set; }
        public int UsedProjects { get; set; }
        public string SubscriptionEnd { get; set; } = "";
    }
}