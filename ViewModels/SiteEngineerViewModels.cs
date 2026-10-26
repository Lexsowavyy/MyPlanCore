using System;
using System.Collections.Generic;
using PlanCoreMAUI.Models;

namespace PlanCore.Web.ViewModels
{
    public class SiteEngineerDashboardViewModel
    {
        public string DashboardFilter { get; set; } = "All";
        public List<PM_Tasks> FilteredTasks { get; set; } = new();
        public List<ProgressReport> FilteredProgress { get; set; } = new();
        public List<SiteIssue> FilteredIssues { get; set; } = new();
        public List<SiteRequest> FilteredRequests { get; set; } = new();
        public List<SiteEngineerActivityFeedItem> Activity { get; set; } = new();
    }

    public class SiteEngineerActivityFeedItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Color { get; set; } = "";
        public string Date { get; set; } = "";
        public DateTime SortDate { get; set; }
    }

    public class ArchivedViewModel
    {
        public string SelectedType { get; set; } = "all";
        public string SearchQuery { get; set; } = "";
        public List<ArchivedItem> Items { get; set; } = new();
        public ArchivedItem? ViewItem { get; set; }
        public ArchivedItem? RestoreItem { get; set; }
        public ArchivedItem? DeleteItem { get; set; }
        public string? Error { get; set; }
    }

    public class ArchivedItem
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
        public DateTime SortOn { get; set; }

        public ProgressReport? RefProgress { get; set; }
        public SiteRequest? RefResource { get; set; }
        public SiteIssue? RefIssue { get; set; }
        public PM_Tasks? RefTask { get; set; }
    }

    public class IssuesViewModel
    {
        public string StatusFilter { get; set; } = "";
        public string SeverityFilter { get; set; } = "";
        public string DateFilter { get; set; } = "All";
        public string Search { get; set; } = "";
        public List<SiteIssue> Issues { get; set; } = new();
        public bool ShowAddModal { get; set; }
        public bool ShowViewModal { get; set; }
        public SiteIssue? ViewItem { get; set; }
        public string? SubmitError { get; set; }
        public string NewIssueType { get; set; } = "";
        public string? NewIssueTypeOther { get; set; }
        public string NewSeverity { get; set; } = "Medium";
        public string NewProject { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewIssueDate { get; set; } = "";
        public string NewDescription { get; set; } = "";
        public string NewSuggestedAction { get; set; } = "";
    }

    public class ProgressViewModel
    {
        public string StatusFilter { get; set; } = "";
        public string DateFilter { get; set; } = "All";
        public List<ProgressReport> Progress { get; set; } = new();
        public bool ShowAddModal { get; set; }
        public bool ShowViewModal { get; set; }
        public bool ShowEditModal { get; set; }
        public ProgressReport? ViewItem { get; set; }
        public ProgressReport? EditItem { get; set; }
        public string? SubmitError { get; set; }
        public string? EditError { get; set; }
        public string NewProject { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewDateTime { get; set; } = "";
        public string NewStatus { get; set; } = "Pending";
        public int NewProgressPercent { get; set; }
        public string? NewNotes { get; set; }
    }

    public class MapViewModel
    {
        public string StatusFilter { get; set; } = "";
        public string Search { get; set; } = "";
        public string DateFilterLabel { get; set; } = "All";
        public List<ProjectMapItem> Projects { get; set; } = new();
        public int ActiveProjectCount { get; set; }
        public int LocationCount { get; set; }
        public int CompletedCount { get; set; }
    }

    public class ProjectMapItem
    {
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
        public int Progress { get; set; }
    }

    public class TasksViewModel
    {
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "thismonth";
        public string Search { get; set; } = "";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }
        public List<PM_Tasks> Tasks { get; set; } = new();
        public bool ShowViewModal { get; set; }
        public PM_Tasks? ViewTask { get; set; }
        public bool ShowArchiveConfirm { get; set; }
        public PM_Tasks? ArchiveTask { get; set; }
    }

    public class ReportsViewModel
    {
        public string ReportSection { get; set; } = "All";
        public string StatusFilter { get; set; } = "All";
        public string Search { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public DateTime? CustomStartDate { get; set; }
        public DateTime? CustomEndDate { get; set; }
        public List<ProgressReport> Progress { get; set; } = new();
        public List<SiteRequest> Resources { get; set; } = new();
        public List<SiteIssue> Issues { get; set; } = new();
        public List<ProgressReport> FilteredProgress { get; set; } = new();
        public List<SiteRequest> FilteredResources { get; set; } = new();
        public List<SiteIssue> FilteredIssues { get; set; } = new();
    }

    public class ResourcesViewModel
    {
        public string StatusFilter { get; set; } = "";
        public string Search { get; set; } = "";
        public string DateFilter { get; set; } = "All";
        public List<SiteRequest> Requests { get; set; } = new();
        public bool ShowAddModal { get; set; }
        public bool ShowViewModal { get; set; }
        public bool ShowEditModal { get; set; }
        public SiteRequest? ViewItem { get; set; }
        public SiteRequest? EditItem { get; set; }
        public string? SubmitError { get; set; }
        public string? EditError { get; set; }
        public string NewProject { get; set; } = "";
        public string NewLocation { get; set; } = "";
        public string NewNotes { get; set; } = "";
        public string NeededByDate { get; set; } = "";
        public int NewManpower { get; set; }
        public int EditManpower { get; set; }
        public string EditNeededByDate { get; set; } = "";
        public string EditNotes { get; set; } = "";
    }
}