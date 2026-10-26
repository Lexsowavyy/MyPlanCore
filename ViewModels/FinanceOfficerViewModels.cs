using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;

namespace PlanCore.Web.ViewModels
{
    public class FODashboardBarItem
    {
        public string Label { get; set; } = "";
        public decimal Inflow { get; set; }
        public decimal Outflow { get; set; }
        public double InflowPct { get; set; }
        public double OutflowPct { get; set; }
    }

    public class FODashboardHealthItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public int Total { get; set; }
    }

    public class FODashboardStatusItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class FODashboardActivityItem
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public class FODashboardViewModel
    {
        public string SelectedDateRange { get; set; } = "All";

        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetBalance { get; set; }
        public decimal TotalCombined { get; set; }
        public int RecordCount { get; set; }
        public int PendingIncomeCount { get; set; }
        public int PendingExpenseCount { get; set; }
        public int ApprovedBudgetCount { get; set; }
        public int WaitingPMCount { get; set; }
        public int TaskCount { get; set; }

        public List<FODashboardBarItem> MonthlyBars { get; set; } = new();
        public List<FODashboardHealthItem> BudgetsHealth { get; set; } = new();
        public List<FODashboardStatusItem> EstimateStatuses { get; set; } = new();
        public List<FODashboardActivityItem> RecentItems { get; set; } = new();
    }

    public class FOBudgetViewModel
    {
        public List<QS_Budget> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalBudget { get; set; }

        public string? Modal { get; set; }
        public QS_Budget? SelectedItem { get; set; }
        public int ActionTargetId { get; set; }
        public string Reason { get; set; } = "";
        public string? Error { get; set; }
    }

    public class FOCostEstimateViewModel
    {
        public List<CostEstimate> Items { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string Search { get; set; } = "";

        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public decimal TotalCost { get; set; }

        public string? Modal { get; set; }
        public CostEstimate? SelectedItem { get; set; }
        public int ActionTargetId { get; set; }
        public string Reason { get; set; } = "";
        public string? Error { get; set; }
    }

    public class FOIncomeViewModel
    {
        public List<PM_IncomeTransactions> Items { get; set; } = new();
        public List<PM_IncomeTransactions> AllIncomes { get; set; } = new();

        public string SelectedCategory { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public string Search { get; set; } = "";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public decimal TotalAmount { get; set; }
        public int TotalRecords { get; set; }
        public int ThisMonthCount { get; set; }

        public string? Modal { get; set; }
        public PM_IncomeTransactions? ViewItem { get; set; }
        public int ActionTargetId { get; set; }

        public int? FormEditId { get; set; }
        public string FormProject { get; set; } = "";
        public string FormLocation { get; set; } = "";
        public string FormDescription { get; set; } = "";
        public string FormAmount { get; set; } = "";
        public DateTime? FormDate { get; set; }
        public string FormPayment { get; set; } = "";
        public string FormReference { get; set; } = "";
        public string FormNotes { get; set; } = "";
        public List<string> FormCategories { get; set; } = new();
        public string FormOther { get; set; } = "";
        public string? FormError { get; set; }
    }

    public class FOExpenseViewModel
    {
        public List<Expense> Items { get; set; } = new();
        public List<Expense> AllExpenses { get; set; } = new();

        public string SelectedCategory { get; set; } = "";
        public string SelectedDateRange { get; set; } = "thismonth";
        public string Search { get; set; } = "";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public decimal TotalAmount { get; set; }
        public int TotalRecords { get; set; }
        public int ThisMonthCount { get; set; }

        public string? Modal { get; set; }
        public Expense? ViewItem { get; set; }
        public string SubmitterName { get; set; } = "-";
        public int ActionTargetId { get; set; }

        public int? FormEditId { get; set; }
        public string FormProject { get; set; } = "";
        public string FormDescription { get; set; } = "";
        public string FormAmount { get; set; } = "";
        public DateTime? FormDate { get; set; }
        public List<string> FormCategories { get; set; } = new();
        public string FormOther { get; set; } = "";
        public string FormLocation { get; set; } = "";
        public string? FormError { get; set; }
    }

    public class FOTransactionEntry
    {
        public int SourceId { get; set; }
        public bool IsIncome { get; set; }
        public DateTime Date { get; set; }
        public string Project { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
        public string? Location { get; set; }
        public string Status { get; set; } = "";
        public string? Notes { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceNumber { get; set; }

        public string IdDisplay => IsIncome ? $"INC-{SourceId:D3}" : $"EXP-{SourceId:D3}";
        public string PaymentRefDisplay => IsIncome
            ? string.Join(", ", new[] { PaymentMethod, ReferenceNumber }.Where(s => !string.IsNullOrEmpty(s)))
            : "-";
    }

    public class FOTransactionViewModel
    {
        public List<FOTransactionEntry> Items { get; set; } = new();
        public string SelectedType { get; set; } = "";
        public string SelectedCategory { get; set; } = "";
        public string SelectedDateRange { get; set; } = "thismonth";
        public string Search { get; set; } = "";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public decimal TotalInflow { get; set; }
        public decimal TotalOutflow { get; set; }
        public decimal NetBalance { get; set; }
        public int RecordCount { get; set; }

        public string? Modal { get; set; }
        public FOTransactionEntry? ViewItem { get; set; }
        public int ArchiveId { get; set; }
        public string ArchiveType { get; set; } = "";
        public string ArchiveLabel { get; set; } = "";
    }

    public class FOCashFlowEntry
    {
        public int SourceId { get; set; }
        public string Source { get; set; } = "";
        public bool IsInflow { get; set; }
        public DateTime Date { get; set; }
        public string Project { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Amount { get; set; }
        public string ReferenceNumber { get; set; } = "";
        public string Notes { get; set; } = "";
        public string IdDisplay { get; set; } = "";
    }

    public class FOCashFlowBudgetSummary
    {
        public string Project { get; set; } = "";
        public decimal TotalBudget { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal Remaining { get; set; }
        public string Health { get; set; } = "";
    }

    public class FOCashFlowProjectSummary
    {
        public string ProjectName { get; set; } = "";
        public decimal TotalInflow { get; set; }
        public decimal TotalOutflow { get; set; }
        public decimal Net => TotalInflow - TotalOutflow;
        public FOCashFlowBudgetSummary? Budget { get; set; }
        public bool HasBudget => Budget != null && Budget.TotalBudget > 0;
        public decimal TotalBudget => Budget?.TotalBudget ?? 0;
        public decimal BudgetRemaining => Budget?.Remaining ?? 0;
        public string Health => Budget?.Health ?? "";
        public string HealthColor => Health switch
        {
            "Over Budget" => "#dc2626",
            "Critical" => "#f59e0b",
            "Warning" => "#f97316",
            "On Track" => "#16a34a",
            _ => "#94a3b8"
        };
    }

    public class FOCashFlowMonthlyTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = "";
        public decimal Inflow { get; set; }
        public decimal Outflow { get; set; }
        public decimal Net => Inflow - Outflow;
    }

    public class FOCashFlowViewModel
    {
        public List<FOCashFlowEntry> Entries { get; set; } = new();
        public List<FOCashFlowEntry> FilteredEntries { get; set; } = new();
        public List<string> DistinctCategories { get; set; } = new();
        public string SelectedType { get; set; } = "All";
        public string SelectedCategory { get; set; } = "All";
        public string SelectedDateRange { get; set; } = "";
        public string Search { get; set; } = "";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public decimal TotalInflow { get; set; }
        public decimal TotalOutflow { get; set; }
        public decimal NetBalance { get; set; }
        public int RecordCount { get; set; }

        public List<FOCashFlowProjectSummary> ProjectSummaries { get; set; } = new();
        public List<FOCashFlowMonthlyTrend> MonthlyTrends { get; set; } = new();

        public string? Modal { get; set; }
        public FOCashFlowEntry? ViewItem { get; set; }
        public int ArchiveId { get; set; }
        public string ArchiveSource { get; set; } = "";
        public string ArchiveDescription { get; set; } = "";
        public string? AddError { get; set; }

        public int? FormEditId { get; set; }
        public string FormProject { get; set; } = "";
        public string FormType { get; set; } = "Inflow";
        public string FormCategory { get; set; } = "";
        public string FormAmount { get; set; } = "";
        public DateTime? FormDate { get; set; }
        public string FormDescription { get; set; } = "";
        public string FormReference { get; set; } = "";
        public string FormNotes { get; set; } = "";
    }

    public class FOReportsViewModel
    {
        public string Section { get; set; } = "All";
        public string StatusFilter { get; set; } = "All";
        public string Search { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStart { get; set; }
        public DateTime? CustomEnd { get; set; }

        public decimal TotalIncome { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetBalance { get; set; }
        public int RecordCount { get; set; }

        public string CurrentSectionTitle { get; set; } = "All Financial Reports";
        public int CurrentItemCount { get; set; }

        public List<PM_IncomeTransactions> IncomeItems { get; set; } = new();
        public List<Expense> ExpenseItems { get; set; } = new();
        public List<BudgetSummary> BudgetItems { get; set; } = new();
        public List<ProjectProfitability> ProfitabilityItems { get; set; } = new();
        public List<MonthlyCashFlow> CashFlowItems { get; set; } = new();
        public List<EstimateVsActual> EstimateItems { get; set; } = new();
    }

    public class FOMyTasksViewModel
    {
        public List<PM_Tasks> Items { get; set; } = new();
        public List<PM_Tasks> AllTasks { get; set; } = new();
        public string DateFilter { get; set; } = "All";
        public string StatusFilter { get; set; } = "";
        public string Search { get; set; } = "";

        public int Total { get; set; }
        public int PendingCount { get; set; }
        public int InProgressCount { get; set; }
        public int CompletedCount { get; set; }

        public string? Modal { get; set; }
        public PM_Tasks? SelectedItem { get; set; }
        public int ActionTargetId { get; set; }
        public string ActionTargetName { get; set; } = "";
    }

    public class FOArchivedItem
    {
        public string Type { get; set; } = "";
        public string DisplayId { get; set; } = "";
        public string Project { get; set; } = "";
        public string Details { get; set; } = "";
        public string AmountDisplay { get; set; } = "";
        public string AmountClass { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusClass { get; set; } = "";
        public string ArchivedOnDisplay { get; set; } = "";
        public DateTime? ArchivedOn { get; set; }
        public int OriginalId { get; set; }
        public string OriginalType { get; set; } = "";

        public Expense? RefExpense { get; set; }
        public PM_IncomeTransactions? RefIncome { get; set; }
    }

    public class FOArchivedViewModel
    {
        public List<FOArchivedItem> Items { get; set; } = new();
        public string SelectedType { get; set; } = "all";
        public string Search { get; set; } = "";

        public string? Modal { get; set; }
        public string Title { get; set; } = "";
        public Expense? ViewExpense { get; set; }
        public PM_IncomeTransactions? ViewIncome { get; set; }
        public int RestoreId { get; set; }
        public string RestoreType { get; set; } = "";
        public int DeleteId { get; set; }
        public string DeleteType { get; set; } = "";
    }
}