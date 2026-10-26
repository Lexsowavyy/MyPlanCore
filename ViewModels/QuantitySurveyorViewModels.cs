using System;
using System.Collections.Generic;
using PlanCoreMAUI.Models;

namespace PlanCore.Web.ViewModels
{
    public class ChartBudgetItem
    {
        public string ProjectName { get; set; } = "";
        public decimal Budget { get; set; }
        public decimal Spent { get; set; }
        public decimal MaxValue { get; set; }
    }

    public class HealthCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public int Total { get; set; }
    }

    public class QsStatusCountItem
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class QsActivityItem
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
        public DateTime SortDate { get; set; }
    }

    public class QSIndexViewModel
    {
        public string DisplayName { get; set; } = "User";
        public string DashboardFilter { get; set; } = "All";

        public int EstimateCount { get; set; }
        public int BudgetCount { get; set; }
        public int PendingApprovalCount { get; set; }
        public int ApprovedCount { get; set; }
        public int OverBudgetCount { get; set; }
        public int WaitingPMCount { get; set; }
        public int RejectedCount { get; set; }
        public int TaskCount { get; set; }

        public decimal EstimateTotalValue { get; set; }
        public decimal TotalBudgetValue { get; set; }
        public decimal TotalSpentValue { get; set; }
        public decimal RemainingValue { get; set; }

        public decimal TotalLabor { get; set; }
        public decimal TotalMaterials { get; set; }
        public decimal TotalEquipment { get; set; }
        public decimal TotalEstimateCost { get; set; }

        public List<ChartBudgetItem> ChartBudgets { get; set; } = new();
        public List<HealthCountItem> BudgetsHealth { get; set; } = new();
        public List<QsStatusCountItem> EstimateStatuses { get; set; } = new();
        public List<QsActivityItem> RecentItems { get; set; } = new();
    }

    public class QSResourcesViewModel
    {
        public List<SiteRequest> Items { get; set; } = new();
        public string Search { get; set; } = "";
        public string SelectedType { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int ApprovedCount { get; set; }
        public int TotalMaterials { get; set; }
        public int TotalEquipment { get; set; }
        public int TotalManpower { get; set; }

        public string? Modal { get; set; }
        public SiteRequest? SelectedRequest { get; set; }
        public int ArchiveId { get; set; }
    }

    public class QSArchivedRow
    {
        public string Type { get; set; } = "";
        public string DisplayId { get; set; } = "";
        public string Project { get; set; } = "";
        public string Details { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusClass { get; set; } = "";
        public string ArchivedOnDisplay { get; set; } = "";
        public int OriginalId { get; set; }
        public string OriginalType { get; set; } = "";
        public DateTime SortDate { get; set; }
    }

    public class QSArchivedViewModel
    {
        public List<QSArchivedRow> Items { get; set; } = new();
        public string SelectedType { get; set; } = "all";
        public string Search { get; set; } = "";

        public string? Modal { get; set; }
        public string ViewTitle { get; set; } = "";
        public CostEstimate? ViewEstimate { get; set; }
        public QS_Budget? ViewBudget { get; set; }
        public int RestoreId { get; set; }
        public string RestoreType { get; set; } = "";
        public int DeleteId { get; set; }
        public string DeleteType { get; set; } = "";
    }

    public class QSCostEstimatesViewModel
    {
        public List<CostEstimate> Items { get; set; } = new();
        public string Search { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int TotalCount { get; set; }
        public int PendingCount { get; set; }
        public int WaitingPMCount { get; set; }
        public int ApprovedCount { get; set; }

        public string? Modal { get; set; }
        public string NewError { get; set; } = "";
        public string EditError { get; set; } = "";

        public CostEstimate? SelectedEstimate { get; set; }
        public int ArchiveId { get; set; }

        public string NewProjectName { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewScheduledDate { get; set; } = "";
        public string NewLaborCost { get; set; } = "";
        public string NewMaterialsCost { get; set; } = "";
        public string NewEquipmentCost { get; set; } = "";
        public string NewNotes { get; set; } = "";

        public int EditId { get; set; }
        public string EditProjectName { get; set; } = "";
        public string EditLocation { get; set; } = "";
        public string EditLaborCost { get; set; } = "";
        public string EditMaterialsCost { get; set; } = "";
        public string EditEquipmentCost { get; set; } = "";
        public string EditNotes { get; set; } = "";
    }

    public class QSBudgetViewModel
    {
        public List<QS_Budget> Items { get; set; } = new();
        public string Search { get; set; } = "";
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public decimal TotalBudget { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal Remaining { get; set; }

        public string? Modal { get; set; }
        public string NewError { get; set; } = "";
        public QS_Budget? SelectedBudget { get; set; }
        public int ArchiveId { get; set; }

        public string NewProject { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewBudgetDate { get; set; } = "";
        public string NewManpowerBudget { get; set; } = "";
        public string NewEquipmentBudget { get; set; } = "";
        public string NewMaterialsBudget { get; set; } = "";
        public string NewManpowerSpent { get; set; } = "";
        public string NewEquipmentSpent { get; set; } = "";
        public string NewMaterialsSpent { get; set; } = "";
        public string NewNotes { get; set; } = "";
    }

    public class QSReportsViewModel
    {
        public List<CostEstimate> EstimateItems { get; set; } = new();
        public List<QS_Budget> BudgetItems { get; set; } = new();
        public string ReportSection { get; set; } = "All";
        public string StatusFilter { get; set; } = "All";
        public string Search { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }

        public int EstimateCount { get; set; }
        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public decimal TotalCost { get; set; }
        public string CurrentSectionTitle { get; set; } = "All Reports";
        public int CurrentItemCount { get; set; }
    }

    public class QSTasksViewModel
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
        public PM_Tasks? SelectedTask { get; set; }
        public int ArchiveId { get; set; }
    }
}
