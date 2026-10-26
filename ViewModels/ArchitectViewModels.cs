using System;
using System.Collections.Generic;
using System.Linq;
using PlanCoreMAUI.Models;

namespace PlanCore.Web.ViewModels
{
    public class StatusCount
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public string Color { get; set; } = "";
    }

    public class BarCount
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class DailyCount
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
    }

    public class ArchitectActivityItem
    {
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Date { get; set; } = "";
    }

    public class ArchitectDashboardViewModel
    {
        public string DisplayName { get; set; } = "";
        public string DashboardFilter { get; set; } = "All";
        public List<ArchitectDesign> Designs { get; set; } = new();
        public List<ArchitectRevision> Revisions { get; set; } = new();
        public List<PM_Tasks> Tasks { get; set; } = new();

        public List<ArchitectDesign> FilteredDesigns => Designs.Where(d =>
            DashboardFilter == "All" || DateMatches(d.UploadDate)
        ).ToList();

        public List<ArchitectRevision> FilteredRevisions => Revisions.Where(r =>
            DashboardFilter == "All" || DateMatches(r.RequestDate)
        ).ToList();

        public int DesignCount => FilteredDesigns.Count;
        public int ApprovedCount => FilteredDesigns.Count(d => d.Status == "Approved");
        public int PendingCount => FilteredDesigns.Count(d => d.Status == "Pending");
        public int RevisedCount => FilteredDesigns.Count(d => d.Status == "Revised");
        public int TaskCount => Tasks.Count(t => t.Status != "Completed");
        public int RevisionCount => FilteredRevisions.Count;

        public List<StatusCount> DesignStatuses =>
            new List<(string Label, string Color)>
            {
                ("Approved", "#16a34a"),
                ("Pending", "#f59e0b"),
                ("Revised", "#db2777"),
                ("Rejected", "#dc2626"),
            }
            .Select(s => new StatusCount
            {
                Label = s.Label,
                Count = FilteredDesigns.Count(d => d.Status == s.Label),
                Color = s.Color
            })
            .Where(s => s.Count > 0)
            .ToList();

        public List<BarCount> DesignsByProject =>
            FilteredDesigns
                .GroupBy(d => d.Project)
                .Select(g => new BarCount
                {
                    Label = g.Key.Length > 15 ? g.Key[..15] + "..." : g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(b => b.Count)
                .Take(5)
                .ToList();

        public List<StatusCount> RevisionStatuses =>
            new List<(string Label, string Color)>
            {
                ("Pending", "#f59e0b"),
                ("Accepted", "#16a34a"),
                ("Rejected", "#dc2626"),
            }
            .Select(s => new StatusCount
            {
                Label = s.Label,
                Count = FilteredRevisions.Count(r => r.Status == s.Label),
                Color = s.Color
            })
            .Where(s => s.Count > 0)
            .ToList();

        public List<DailyCount> DailyUploads
        {
            get
            {
                var sevenDays = Enumerable.Range(0, 7).Select(i => DateTime.Now.Date.AddDays(-i)).Reverse().ToList();
                return sevenDays.Select(day => new DailyCount
                {
                    Label = day.ToString("ddd"),
                    Count = FilteredDesigns.Count(d => d.UploadDate.Date == day)
                }).ToList();
            }
        }

        public List<ArchitectActivityItem> RecentActivity
        {
            get
            {
                var items = new List<ArchitectActivityItem>();
                foreach (var d in FilteredDesigns.OrderByDescending(d => d.UploadDate).Take(5))
                    items.Add(new() { Type = "Design", Title = $"{d.DesignTitle}", Subtitle = $"{d.Project} | {d.Status}", Date = d.UploadDate.ToString("MMM d, yyyy") });
                foreach (var r in FilteredRevisions.OrderByDescending(r => r.RequestDate).Take(5))
                    items.Add(new() { Type = "Revision", Title = $"Revision: {r.ChangeRequest}", Subtitle = $"{r.Project} | {r.Status}", Date = r.RequestDate.ToString("MMM d, yyyy") });
                return items.OrderByDescending(i => i.Date).Take(8).ToList();
            }
        }

        public bool DateMatches(DateTime date)
        {
            var today = DateTime.Now;
            return DashboardFilter switch
            {
                "Today" => date.Date == today.Date,
                "This Week" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                "This Month" => date.Year == today.Year && date.Month == today.Month,
                "This Year" => date.Year == today.Year,
                _ => true
            };
        }

        public static DateTime StartOfWeek(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }
    }

    public class ArchitectTasksViewModel
    {
        public string StatusFilter { get; set; } = "";
        public string SearchQuery { get; set; } = "";
        public string DateFilter { get; set; } = "All";
        public List<PM_Tasks> Tasks { get; set; } = new();
        public bool ShowViewModal { get; set; }
        public PM_Tasks? SelectedTask { get; set; }
        public bool ShowArchiveConfirm { get; set; }
        public int ArchiveTargetId { get; set; }

        public List<PM_Tasks> FilteredTasks => Tasks.Where(t =>
            (string.IsNullOrEmpty(StatusFilter) || t.Status == StatusFilter) &&
            (string.IsNullOrEmpty(SearchQuery) || t.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || (t.Project ?? "").Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) &&
            (DateFilter == "All" || CheckDateFilter(t.DueDate))
        ).ToList();

        public int TotalTasks => Tasks.Count;
        public int PendingTasks => Tasks.Count(t => t.Status == "Pending");
        public int InProgressTasks => Tasks.Count(t => t.Status == "In Progress");
        public int CompletedTasks => Tasks.Count(t => t.Status == "Completed");

        public bool CheckDateFilter(DateTime? date)
        {
            if (!date.HasValue) return false;
            var today = DateTime.Today;
            return DateFilter switch
            {
                "Today" => date.Value.Date == today,
                "This Week" => date.Value.Date >= today && date.Value.Date <= today.AddDays(7),
                "This Month" => date.Value.Month == today.Month && date.Value.Year == today.Year,
                "This Year" => date.Value.Year == today.Year,
                _ => true
            };
        }
    }

    public class ArchitectArchivedViewModel
    {
        public string ActiveTab { get; set; } = "designs";
        public List<ArchitectDesign> ArchivedDesigns { get; set; } = new();
        public List<ArchitectRevision> ArchivedRevisions { get; set; } = new();
        public List<PM_Tasks> ArchivedTasks { get; set; } = new();

        public bool ShowViewModal { get; set; }
        public ArchitectDesign? SelectedDesign { get; set; }
        public ArchitectRevision? SelectedRevision { get; set; }
        public PM_Tasks? SelectedTask { get; set; }

        public bool ShowRestoreConfirm { get; set; }
        public string RestoreTargetType { get; set; } = "";
        public int RestoreTargetId { get; set; }

        public bool ShowDeleteConfirm { get; set; }
        public string DeleteTargetType { get; set; } = "";
        public int DeleteTargetId { get; set; }
    }

    public class ArchitectDesignsViewModel
    {
        public List<ArchitectDesign> Designs { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public string SearchQuery { get; set; } = "";

        public List<ArchitectDesign> FilteredDesigns
        {
            get
            {
                var query = Designs.AsEnumerable();

                if (!string.IsNullOrEmpty(SelectedStatus))
                    query = query.Where(d => d.Status == SelectedStatus);

                if (!string.IsNullOrEmpty(SearchQuery))
                    query = query.Where(d =>
                        (d.DesignTitle ?? "").Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                        (d.Project ?? "").Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

                var today = DateTime.Today;
                query = SelectedDateRange switch
                {
                    "today" => query.Where(d => d.UploadDate.Date == today),
                    "thisweek" => query.Where(d =>
                    {
                        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                        var endOfWeek = startOfWeek.AddDays(7);
                        return d.UploadDate.Date >= startOfWeek && d.UploadDate.Date < endOfWeek;
                    }),
                    "thismonth" => query.Where(d => d.UploadDate.Year == today.Year && d.UploadDate.Month == today.Month),
                    "thisyear" => query.Where(d => d.UploadDate.Year == today.Year),
                    _ => query
                };

                return query.OrderByDescending(d => d.DesignID).ToList();
            }
        }

        public bool ShowUploadModal { get; set; }
        public bool ShowViewModal { get; set; }
        public bool ShowEditModal { get; set; }
        public bool ShowArchiveConfirm { get; set; }
        public bool ShowFullImage { get; set; }
        public string UploadError { get; set; } = "";
        public string EditError { get; set; } = "";
        public int ArchiveTargetId { get; set; }
        public ArchitectDesign? SelectedDesign { get; set; }

        public int EditId { get; set; }
        public string EditTitle { get; set; } = "";
        public string EditProject { get; set; } = "";
        public string EditLocation { get; set; } = "";
        public string EditVersion { get; set; } = "";
        public string EditNotes { get; set; } = "";
        public string EditFileName { get; set; } = "";

        public string UploadTitle { get; set; } = "";
        public string UploadProject { get; set; } = "";
        public string UploadLocation { get; set; } = "";
        public string UploadVersion { get; set; } = "v1";
        public DateTime UploadDate { get; set; } = DateTime.Now;
        public string UploadNotes { get; set; } = "";
        public string? UploadFileName { get; set; }
    }

    public class ArchitectRevisionsViewModel
    {
        public List<ArchitectRevision> Revisions { get; set; } = new();
        public string SelectedStatus { get; set; } = "";
        public string SearchQuery { get; set; } = "";
        public string SelectedDateRange { get; set; } = "all";
        public bool ShowViewModal { get; set; }
        public ArchitectRevision? SelectedRevision { get; set; }
        public ArchitectDesign? LinkedDesign { get; set; }
        public bool ShowConfirmAccept { get; set; }
        public int AcceptTargetId { get; set; }
        public bool ShowFullImage { get; set; }

        public List<ArchitectRevision> FilteredRevisions => Revisions.Where(r =>
            (string.IsNullOrEmpty(SelectedStatus) || r.Status == SelectedStatus) &&
            (string.IsNullOrEmpty(SearchQuery) || r.ChangeRequest.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) || r.Project.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedDateRange == "all" || CheckDateFilter(r.RequestDate))
        ).OrderByDescending(r => r.RevisionID).ToList();

        public int PendingRevisions => Revisions.Count(r => r.Status == "Pending");
        public int AcceptedRevisions => Revisions.Count(r => r.Status == "Accepted");

        public bool CheckDateFilter(DateTime date)
        {
            var today = DateTime.Today;
            return SelectedDateRange switch
            {
                "today" => date.Date == today,
                "thisweek" => date.Date >= today && date.Date <= today.AddDays(7),
                "thismonth" => date.Month == today.Month && date.Year == today.Year,
                "thisyear" => date.Year == today.Year,
                _ => true
            };
        }
    }
}
