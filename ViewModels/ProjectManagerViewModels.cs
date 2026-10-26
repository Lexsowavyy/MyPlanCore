using System;
using System.Collections.Generic;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;

namespace PlanCore.Web.ViewModels
{
    public class PMStatusCount
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class PMHealthCount
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public int Total { get; set; }
    }

    public class PMActivityItem
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public class PMIndexViewModel
    {
        public string DisplayName { get; set; } = "User";

        public int ProjectCount { get; set; }
        public int OnTrackCount { get; set; }
        public int OpenIssueCount { get; set; }
        public int PendingApprovalCount { get; set; }
        public int TasksDueToday { get; set; }
        public int MyTaskCount { get; set; }
        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetBalance { get; set; }
        public int EstimateCount { get; set; }
        public int ApprovedBudgetCount { get; set; }
        public int ReportCount { get; set; }
        public int ArchivedCount { get; set; }

        public List<PMStatusCount> ProjectStatuses { get; set; } = new();
        public List<PMHealthCount> BudgetHealthItems { get; set; } = new();

        public int TaskCount { get; set; }
        public int PendingTaskCount { get; set; }
        public int InProgressTaskCount { get; set; }
        public int CompletedTaskCount { get; set; }

        public List<PMActivityItem> RecentItems { get; set; } = new();
    }

    public class PMPlanningViewModel
    {
        public List<PM_Projects> Items { get; set; } = new();
        public string Search { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "thismonth";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int TotalCount { get; set; }
        public int OnTrackCount { get; set; }
        public int DelayedCount { get; set; }

        public string? Modal { get; set; }
        public string NewError { get; set; } = "";
        public string EditError { get; set; } = "";
        public PM_Projects? SelectedProject { get; set; }
        public int ArchiveId { get; set; }

        public string NewProjectName { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewStartDate { get; set; } = "";
        public string NewDueDate { get; set; } = "";
        public string NewStatus { get; set; } = "Planning";
        public string NewDescription { get; set; } = "";

        public int EditId { get; set; }
        public string EditProjectName { get; set; } = "";
        public string EditLocation { get; set; } = "";
        public string EditStartDate { get; set; } = "";
        public string EditDueDate { get; set; } = "";
        public string EditStatus { get; set; } = "";
        public string EditDescription { get; set; } = "";
    }

    public class PMNewProjectViewModel
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string Status { get; set; } = "Planning";
        public string Description { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class PMTasksViewModel
    {
        public List<PM_Tasks> Items { get; set; } = new();
        public string Search { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "thismonth";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int TotalCount { get; set; }
        public int CompletedCount { get; set; }
        public int InProgressCount { get; set; }

        public string? Modal { get; set; }
        public string NewError { get; set; } = "";
        public string EditError { get; set; } = "";
        public PM_Tasks? SelectedTask { get; set; }
        public int ArchiveId { get; set; }

        public string NewTaskName { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewProject { get; set; } = "";
        public string NewAssignedTo { get; set; } = "";
        public string NewPriority { get; set; } = "Medium";
        public string NewStartDate { get; set; } = "";
        public string NewDueDate { get; set; } = "";
        public string NewDescription { get; set; } = "";

        public int EditId { get; set; }
        public string EditTaskName { get; set; } = "";
        public string EditLocation { get; set; } = "";
        public string EditProject { get; set; } = "";
        public string EditAssignedTo { get; set; } = "";
        public string EditPriority { get; set; } = "";
        public string EditStartDate { get; set; } = "";
        public string EditDueDate { get; set; } = "";
        public string EditDescription { get; set; } = "";
    }

    public class PMAssignTaskViewModel
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string Project { get; set; } = "";
        public string AssignedTo { get; set; } = "";
        public string Priority { get; set; } = "Medium";
        public string StartDate { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string Description { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class PMArchivedItem
    {
        public string Type { get; set; } = "";
        public int RawId { get; set; }
        public string DisplayId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusClass { get; set; } = "";
        public string ArchivedOnDisplay { get; set; } = "";
        public DateTime? SortDate { get; set; }
    }

    public class PMArchivedViewModel
    {
        public List<PMArchivedItem> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int ProjectCount { get; set; }
        public int TaskCount { get; set; }
        public string SelectedType { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";
        public string? Modal { get; set; }
        public PM_Projects? SelectedProject { get; set; }
        public PM_Tasks? SelectedTask { get; set; }
        public int ActionTargetId { get; set; }
        public string ActionTargetType { get; set; } = "";
    }

    public class PMMonitoringViewModel
    {
        public List<ProgressReport> Items { get; set; } = new();
        public List<PM_Projects> Projects { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedDate { get; set; } = "All";
        public string Search { get; set; } = "";

        public int TotalCount { get; set; }
        public int CompletedCount { get; set; }
        public int NearCompletionCount { get; set; }
        public int PendingCount { get; set; }

        public string? Modal { get; set; }
        public ProgressReport? SelectedItem { get; set; }
        public int ArchiveId { get; set; }
    }

    public class PMBudgetViewModel
    {
        public List<QS_Budget> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalBudget { get; set; }

        public string? Modal { get; set; }
        public QS_Budget? SelectedBudget { get; set; }
        public int RejectId { get; set; }
        public string RejectReason { get; set; } = "";
        public string RejectError { get; set; } = "";
        public int ArchiveId { get; set; }
    }

    public class PMExpenseViewModel
    {
        public List<Expense> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalAmount { get; set; }

        public string? Modal { get; set; }
        public Expense? SelectedExpense { get; set; }
        public int RejectId { get; set; }
        public string RejectReason { get; set; } = "";
        public string RejectError { get; set; } = "";
    }

    public class PMEstimateViewModel
    {
        public List<CostEstimate> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalCost { get; set; }

        public string? Modal { get; set; }
        public CostEstimate? SelectedEstimate { get; set; }
        public int RejectId { get; set; }
        public string RejectReason { get; set; } = "";
        public string RejectError { get; set; } = "";
        public int ArchiveId { get; set; }
    }

    public class PMIncomeViewModel
    {
        public List<PM_IncomeTransactions> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public string Search { get; set; } = "";

        public decimal TotalAmount { get; set; }
        public int TransactionCount { get; set; }
        public int ThisMonthCount { get; set; }
        public int PendingCount { get; set; }

        public string? Modal { get; set; }
        public PM_IncomeTransactions? SelectedItem { get; set; }
    }

    public class PMFinancialProject
    {
        public string Name { get; set; } = "";
        public decimal Income { get; set; }
        public decimal Expenses { get; set; }
        public decimal Budget { get; set; }
        public decimal Remaining { get; set; }
    }

    public class PMReportsViewModel
    {
        public List<ProgressReport> AllProgress { get; set; } = new();
        public List<SiteRequest> AllResources { get; set; } = new();
        public List<SiteIssue> AllIssues { get; set; } = new();
        public List<CostEstimate> AllEstimates { get; set; } = new();
        public List<QS_Budget> AllBudgets { get; set; } = new();
        public List<PM_IncomeTransactions> AllIncomes { get; set; } = new();
        public List<Expense> AllExpenses { get; set; } = new();
        public List<PM_Tasks> AllTasks { get; set; } = new();
        public List<PM_Projects> AllProjects { get; set; } = new();

        public List<ProgressReport> FilteredProgress { get; set; } = new();
        public List<SiteRequest> FilteredResources { get; set; } = new();
        public List<SiteIssue> FilteredIssues { get; set; } = new();
        public List<CostEstimate> FilteredEstimates { get; set; } = new();
        public List<QS_Budget> FilteredBudgets { get; set; } = new();
        public List<PM_IncomeTransactions> FilteredIncomes { get; set; } = new();
        public List<Expense> FilteredExpenses { get; set; } = new();
        public List<PM_Tasks> FilteredTasks { get; set; } = new();
        public List<PM_Projects> FilteredProjects { get; set; } = new();
        public List<PMFinancialProject> FinancialProjects { get; set; } = new();

        public List<ProjectProfitability> ProfitabilityItems { get; set; } = new();
        public List<MonthlyCashFlow> CashFlowItems { get; set; } = new();
        public List<EstimateVsActual> EstimateVsActualItems { get; set; } = new();
        public List<BudgetSummary> BudgetSummaryItems { get; set; } = new();

        public string ReportSection { get; set; } = "All";
        public string StatusFilter { get; set; } = "All";
        public string SelectedDateRange { get; set; } = "all";
        public string Search { get; set; } = "";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public string CurrentSectionTitle { get; set; } = "All Reports";
        public int CurrentItemCount { get; set; }

        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetBalance { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal TotalSpent { get; set; }
    }

    public class PMRisksViewModel
    {
        public List<SiteIssue> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedSeverity { get; set; } = "";
        public string Search { get; set; } = "";

        public int TotalCount { get; set; }
        public int OpenCount { get; set; }
        public int ResolvedCount { get; set; }

        public string? Modal { get; set; }
        public SiteIssue? SelectedItem { get; set; }
        public string PmNotes { get; set; } = "";
        public string? Error { get; set; }
        public int ConfirmId { get; set; }
    }

    public class PMSiteRequestsViewModel
    {
        public List<SiteRequest> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }

        public string? Modal { get; set; }
        public SiteRequest? SelectedItem { get; set; }
        public int ActionTargetId { get; set; }
        public string RejectRemarks { get; set; } = "";
        public string? RejectError { get; set; }
        public string? ApproveError { get; set; }
    }

    public class PMIssuesViewModel
    {
        public List<SiteIssue> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedSeverity { get; set; } = "";
        public string Search { get; set; } = "";

        public int TotalCount { get; set; }
        public int OpenCount { get; set; }
        public int ResolvedCount { get; set; }

        public string? Modal { get; set; }
        public SiteIssue? SelectedItem { get; set; }
    }

    public class PMDesignsViewModel
    {
        public List<ArchitectDesign> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public string Search { get; set; } = "";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }

        public string? Modal { get; set; }
        public ArchitectDesign? SelectedItem { get; set; }
        public int ActionTargetId { get; set; }
        public string Reason { get; set; } = "";
        public string? Error { get; set; }
    }
}