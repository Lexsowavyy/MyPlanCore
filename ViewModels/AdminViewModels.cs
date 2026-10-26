using System;
using System.Collections.Generic;
using PlanCoreMAUI.Models;
using Users = PlanCoreMAUI.Components.Pages.Admin.Users;

namespace PlanCore.Web.ViewModels
{
    public class DashboardViewModel
    {
        public string DisplayName { get; set; } = "";
        public string Filter { get; set; } = "All";
        public int TotalUsersCount { get; set; }
        public int ProjectCount { get; set; }
        public int AuditCount { get; set; }
        public int IssueCount { get; set; }
        public int RequestCount { get; set; }
        public int ActiveTaskCount { get; set; }
        public int EstimateCount { get; set; }
        public decimal TotalBudgetValue { get; set; }
        public decimal TotalExpenseValue { get; set; }
        public decimal TotalIncomeValue { get; set; }
        public int DesignCount { get; set; }
        public int CashFlowCount { get; set; }
        public List<LabelCountItem> RoleCounts { get; set; } = new();
        public List<LabelCountItem> DailyActivity { get; set; } = new();
        public List<LabelCountItem> ModuleActivity { get; set; } = new();
        public int ModuleActivityMax { get; set; } = 1;
        public List<LabelCountItem> ProjectStatuses { get; set; } = new();
        public List<ActivityItem> RecentActivity { get; set; } = new();
    }

    public class LabelCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public string Color { get; set; } = "";
    }

    public class ActivityItem
    {
        public string Action { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public class RolesViewModel
    {
        public string Search { get; set; } = "";
        public List<RoleCard> RoleCards { get; set; } = new();
        public List<RoleUser> Users { get; set; } = new();
    }

    public class RoleCard
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Color { get; set; } = "";
        public string ColorBG { get; set; } = "";
        public string Description { get; set; } = "";
        public int UserCount { get; set; }
    }

    public class RoleUser
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime AssignedDate { get; set; }
    }

    public class AuditLogsViewModel
    {
        public string TypeFilter { get; set; } = "";
        public string ActionFilter { get; set; } = "";
        public string DateFilter { get; set; } = "";
        public string Search { get; set; } = "";
        public HashSet<string> SuperAdminEmails { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AuditLog> AuditLogs { get; set; } = new();
    }

    public class ArchivedUsersViewModel
    {
        public string Search { get; set; } = "";
        public int? DeleteId { get; set; }
        public List<Users.User> Users { get; set; } = new();
    }

    public class BackupRecord
    {
        public string Name { get; set; } = "";
        public DateTime Date { get; set; }
        public string Size { get; set; } = "";
        public string Type { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class BillingViewModel
    {
        public PlanDefinition CurrentPlan { get; set; } = new();
        public int UsedProjects { get; set; }
        public int MaxProjects { get; set; }
        public int UsedUsers { get; set; }
        public int MaxUsers { get; set; }
        public string StorageUsed { get; set; } = "0 MB";
        public string StorageTotal { get; set; } = "Unlimited";
        public string BillingCycle { get; set; } = "monthly";
        public DateTime? SubscriptionStart { get; set; }
        public DateTime? SubscriptionEnd { get; set; }
        public DateTime? TrialEnd { get; set; }
        public bool NoPlan { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsExpired { get; set; }
        public bool IsOnTrial { get; set; }
        public string SaveMessage { get; set; } = "";
        public string? Modal { get; set; }
        public string SelectedPlan { get; set; } = "";
        public PlanDefinition? SelectedPlanDef { get; set; }
        public string DowngradeTargetPlan { get; set; } = "";
        public PlanDefinition? DowngradeTargetDef { get; set; }
        public BillingReceiptInfo? Receipt { get; set; }
        public List<PaymentTransaction> PaymentLedger { get; set; } = new();
    }

    public class BillingReceiptInfo
    {
        public string InvoiceNumber { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public string Plan { get; set; } = "";
        public decimal Amount { get; set; }
        public string Cycle { get; set; } = "";
        public string PeriodStart { get; set; } = "";
        public string PeriodEnd { get; set; } = "";
        public string Date { get; set; } = "";
        public string Method { get; set; } = "";
        public string Action { get; set; } = "";
    }

    public class AdminReportsViewModel
    {
        public string Search { get; set; } = "";
        public string Section { get; set; } = "All";

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
        public int AuditCount { get; set; }

        public List<AdminReportProjectRow> Projects { get; set; } = new();
        public List<AdminReportTaskRow> Tasks { get; set; } = new();
        public List<AdminReportBudgetRow> Budgets { get; set; } = new();
        public List<LabelCountItem> UsersByRole { get; set; } = new();
        public List<ActivityItem> RecentActivity { get; set; } = new();
        public List<AdminReportPMRow> ProjectManagers { get; set; } = new();
    }

    public class AdminReportPMRow
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public int ProjectCount { get; set; }
        public int TaskCount { get; set; }
        public decimal BudgetTotal { get; set; }
        public decimal SpentTotal { get; set; }
        public string Status { get; set; } = "";
    }

    public class AdminReportProjectRow
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string Manager { get; set; } = "";
        public string Location { get; set; } = "";
        public string DueDate { get; set; } = "";
    }

    public class AdminReportTaskRow
    {
        public string Name { get; set; } = "";
        public string Project { get; set; } = "";
        public string Assignee { get; set; } = "";
        public string Status { get; set; } = "";
        public string Priority { get; set; } = "";
        public int Progress { get; set; }
        public string DueDate { get; set; } = "";
    }

    public class AdminReportBudgetRow
    {
        public string Project { get; set; } = "";
        public decimal Budget { get; set; }
        public decimal Spent { get; set; }
        public decimal Remaining { get; set; }
        public string Health { get; set; } = "";
    }
}
