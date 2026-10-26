using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    public class SiteEngineerController : AppControllerBase
    {
        private readonly DailyProgressService _progress;
        private readonly SiteRequestService _requests;
        private readonly SiteIssueService _issues;
        private readonly PM_TaskService _tasks;
        private readonly PdfExportService _pdf;

        public SiteEngineerController(AuthService auth, SubscriptionService subscription, AuditService audit,
            DailyProgressService progress, SiteRequestService requests, SiteIssueService issues, PM_TaskService tasks, PdfExportService pdf)
            : base(auth, subscription, audit)
        {
            _progress = progress;
            _requests = requests;
            _issues = issues;
            _tasks = tasks;
            _pdf = pdf;
        }

        protected override string? RequiredRole => "SiteEngineer";

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            ViewData["CurrentDisplayName"] = Auth.CurrentDisplayName;
        }

        // ---- Dashboard --------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Index(string? filter)
        {
            var dashboardFilter = NormalizeDashboardFilter(filter);

            var allTasks = new List<PM_Tasks>();
            var allProgress = new List<ProgressReport>();
            var allIssues = new List<SiteIssue>();
            var allRequests = new List<SiteRequest>();
            try { allTasks = await _tasks.GetTasksByAssignedRoleAsync("SiteEngineer"); } catch { }
            try { allProgress = await _progress.GetAllAsync(); } catch { }
            try { allIssues = await _issues.GetAllAsync(); } catch { }
            try { allRequests = await _requests.GetAllAsync(); } catch { }

            var model = new SiteEngineerDashboardViewModel
            {
                DashboardFilter = dashboardFilter,
                FilteredTasks = allTasks.Where(t => dashboardFilter == "All" || DateMatches(t.CreatedAt, dashboardFilter)).ToList(),
                FilteredProgress = allProgress.Where(p => dashboardFilter == "All" || DateMatches(p.UpdateDate, dashboardFilter)).ToList(),
                FilteredIssues = allIssues.Where(i => dashboardFilter == "All" || DateMatches(i.DateReported, dashboardFilter)).ToList(),
                FilteredRequests = allRequests.Where(r => dashboardFilter == "All" || DateMatches(r.CreatedAt, dashboardFilter)).ToList()
            };

            var items = new List<SiteEngineerActivityFeedItem>();
            foreach (var p in model.FilteredProgress.Take(3))
                items.Add(new SiteEngineerActivityFeedItem { Title = $"Progress Update — {p.Project}", Subtitle = $"{p.ProgressPercent}% complete | {p.Status}", Color = "#10b981", Date = FormatRelTime(p.UpdateDate), SortDate = p.UpdateDate });
            foreach (var r in model.FilteredRequests.Take(3))
                items.Add(new SiteEngineerActivityFeedItem { Title = $"Request: {r.MaterialType}", Subtitle = $"{r.Project} | {r.Status}", Color = "#f59e0b", Date = FormatRelTime(r.CreatedAt), SortDate = r.CreatedAt });
            foreach (var i in model.FilteredIssues.Take(3))
                items.Add(new SiteEngineerActivityFeedItem { Title = $"{i.IssueType} — {i.Severity}", Subtitle = $"{i.Project} | {i.Status}", Color = i.Severity == "Critical" ? "#ef4444" : i.Severity == "High" ? "#f59e0b" : "#3b82f6", Date = FormatRelTime(i.DateReported), SortDate = i.DateReported });
            model.Activity = items.OrderByDescending(a => a.SortDate).Take(10).ToList();

            return View(model);
        }

        [HttpGet]
        public IActionResult Clear()
        {
            return RedirectToAction("Index");
        }

        // ---- Archived ---------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Archived(string? type, string? search, string? view, int? restore, int? delete, string? restored, string? deleted)
        {
            var selectedType = string.IsNullOrEmpty(type) ? "all" : type;
            var searchQuery = search ?? "";
            var model = new ArchivedViewModel
            {
                SelectedType = selectedType,
                SearchQuery = searchQuery,
                Error = restored == "error" ? "Could not restore item." : deleted == "error" ? "Could not delete item." : (string?)null
            };

            model.Items = await BuildArchivedItemsAsync();
            model.Items = FilterArchivedItems(model.Items, selectedType, searchQuery);
            model.ViewItem = ResolveArchivedItem(model.Items, view);

            if (restore.HasValue)
                model.RestoreItem = model.Items.FirstOrDefault(i => i.OriginalId == restore.Value);
            if (delete.HasValue)
                model.DeleteItem = model.Items.FirstOrDefault(i => i.OriginalId == delete.Value);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(int id, string type)
        {
            try
            {
                switch (type)
                {
                    case "progress": await _progress.RestoreAsync(id); break;
                    case "resource": await _requests.RestoreAsync(id); break;
                    case "issue": await _issues.RestoreAsync(id); break;
                    case "task": await _tasks.RestoreTaskAsync(id); break;
                }
                return RedirectToAction("Archived");
            }
            catch
            {
                return RedirectToAction("Archived", new { restored = "error" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, string type)
        {
            try
            {
                switch (type)
                {
                    case "progress": await _progress.DeleteAsync(id); break;
                    case "resource": await _requests.DeleteAsync(id); break;
                    case "issue": await _issues.DeleteAsync(id); break;
                    case "task": await _tasks.DeleteTaskAsync(id); break;
                }
                return RedirectToAction("Archived");
            }
            catch
            {
                return RedirectToAction("Archived", new { deleted = "error" });
            }
        }

        // ---- Issues & Risks --------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Issues(string? statusFilter, string? severityFilter, string? dateFilter, string? search, string? add, int? view)
        {
            var all = new List<SiteIssue>();
            try { all = await _issues.GetAllAsync(); } catch { }

            var model = new IssuesViewModel
            {
                StatusFilter = statusFilter ?? "",
                SeverityFilter = severityFilter ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                Search = search ?? "",
                Issues = FilterIssues(all, statusFilter ?? "", severityFilter ?? "", string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter, search ?? ""),
                ShowAddModal = add == "1",
                ViewItem = null
            };
            if (view.HasValue)
            {
                model.ViewItem = all.FirstOrDefault(i => i.Id == view.Value);
                model.ShowViewModal = model.ViewItem != null;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ReportIssue(string statusFilter, string severityFilter, string dateFilter, string search,
            string? issueType, string? issueTypeOther, string? severity, string? project, string? location,
            string? issueDate, string? description, string? suggestedAction)
        {
            var model = new IssuesViewModel
            {
                StatusFilter = statusFilter ?? "",
                SeverityFilter = severityFilter ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                Search = search ?? "",
                ShowAddModal = true,
                NewIssueType = issueType ?? "",
                NewIssueTypeOther = issueTypeOther,
                NewSeverity = string.IsNullOrEmpty(severity) ? "Medium" : severity,
                NewProject = project ?? "",
                NewLocation = location ?? "",
                NewIssueDate = issueDate ?? "",
                NewDescription = description ?? "",
                NewSuggestedAction = suggestedAction ?? ""
            };

            var all = new List<SiteIssue>();
            try { all = await _issues.GetAllAsync(); } catch { }
            model.Issues = FilterIssues(all, model.StatusFilter, model.SeverityFilter, model.DateFilter, model.Search);

            if (string.IsNullOrWhiteSpace(model.NewIssueType))
                model.SubmitError = "Issue type is required.";
            else if (model.NewIssueType == "Other" && string.IsNullOrWhiteSpace(model.NewIssueTypeOther))
                model.SubmitError = "Please specify the issue type.";
            else if (string.IsNullOrWhiteSpace(model.NewProject))
                model.SubmitError = "Project name is required.";
            else if (string.IsNullOrWhiteSpace(model.NewIssueDate))
                model.SubmitError = "Date & time reported is required.";
            else if (!DateTime.TryParse(model.NewIssueDate, out _))
                model.SubmitError = "Invalid date format.";

            SiteIssue? issue = null;
            if (string.IsNullOrEmpty(model.SubmitError))
            {
                if (all.Any(i => i.Project.Equals(model.NewProject.Trim(), StringComparison.OrdinalIgnoreCase)
                              && i.Description.Equals(model.NewDescription.Trim(), StringComparison.OrdinalIgnoreCase)
                              && i.Status != "Resolved"))
                {
                    model.SubmitError = "An unresolved issue with this description already exists for this project.";
                }
                else
                {
                    issue = new SiteIssue
                    {
                        UserId = "SE-001",
                        ReportedByName = Auth.CurrentDisplayName ?? "SiteEngineer",
                        IssueType = model.NewIssueType == "Other" ? model.NewIssueTypeOther!.Trim() : model.NewIssueType.Trim(),
                        IssueTypeOther = model.NewIssueType == "Other" ? model.NewIssueTypeOther?.Trim() : null,
                        Severity = model.NewSeverity,
                        Project = model.NewProject.Trim(),
                        Location = string.IsNullOrWhiteSpace(model.NewLocation) ? null : model.NewLocation.Trim(),
                        Description = model.NewDescription.Trim(),
                        SuggestedAction = string.IsNullOrWhiteSpace(model.NewSuggestedAction) ? null : model.NewSuggestedAction.Trim(),
                        Status = "Open",
                        DateReported = DateTime.TryParse(model.NewIssueDate, out var dtReported) ? dtReported : DateTime.Now,
                        ResolvedDate = null
                    };
                }
            }

            if (issue != null)
            {
                try
                {
                    var (saved, error) = await _issues.CreateAsync(issue);
                    if (saved)
                        return RedirectToAction("Issues", RouteValues(model.StatusFilter, model.SeverityFilter, model.DateFilter, model.Search));
                    model.SubmitError = error;
                }
                catch (Exception ex)
                {
                    model.SubmitError = $"Unexpected error: {ex.Message}";
                }
            }

            return View("Issues", model);
        }

        [HttpPost]
        public async Task<IActionResult> ResolveIssue(int id, string statusFilter, string severityFilter, string dateFilter, string search)
        {
            try { await _issues.ResolveAsync(id); } catch { }
            return RedirectToAction("Issues", RouteValues(statusFilter, severityFilter, dateFilter, search));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveIssue(int id, string statusFilter, string severityFilter, string dateFilter, string search)
        {
            try { await _issues.ArchiveAsync(id); } catch { }
            return RedirectToAction("Issues", RouteValues(statusFilter, severityFilter, dateFilter, search));
        }

        // ---- Daily Progress ---------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Progress(string? statusFilter, string? dateFilter, string? add, int? view, int? edit)
        {
            var all = new List<ProgressReport>();
            try { all = await _progress.GetAllAsync(); } catch { }

            var model = new ProgressViewModel
            {
                StatusFilter = statusFilter ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                Progress = FilterProgress(all, statusFilter ?? "", string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter),
                ShowAddModal = add == "1",
                NewDateTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm"),
                NewStatus = "Pending"
            };

            if (view.HasValue)
            {
                model.ViewItem = all.FirstOrDefault(p => p.Id == view.Value);
                model.ShowViewModal = model.ViewItem != null;
            }
            if (edit.HasValue)
            {
                var src = all.FirstOrDefault(p => p.Id == edit.Value);
                if (src != null && src.Status != "Completed")
                {
                    model.EditItem = src;
                    model.ShowEditModal = true;
                }
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProgress(string statusFilter, string dateFilter,
            string? project, string? location, string? dateTime, string? status, int? progress, string? notes, Microsoft.AspNetCore.Http.IFormFile? photo)
        {
            var model = new ProgressViewModel
            {
                StatusFilter = statusFilter ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                ShowAddModal = true,
                NewProject = project ?? "",
                NewLocation = location ?? "",
                NewStatus = string.IsNullOrEmpty(status) ? "Pending" : status,
                NewProgressPercent = progress ?? 0,
                NewNotes = notes
            };

            var all = new List<ProgressReport>();
            try { all = await _progress.GetAllAsync(); } catch { }
            model.Progress = FilterProgress(all, model.StatusFilter, model.DateFilter);

            if (string.IsNullOrWhiteSpace(model.NewProject))
            {
                model.SubmitError = "Project is required.";
            }
            else
            {
                model.NewDateTime = dateTime ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm");
                if (!DateTime.TryParse(model.NewDateTime, out var dt))
                    dt = DateTime.Now;

                if (all.Any(p => p.Project.Equals(model.NewProject.Trim(), StringComparison.OrdinalIgnoreCase)
                              && p.UpdateDate.Date == dt.Date))
                {
                    model.SubmitError = "Progress for this project on this date has already been submitted.";
                }
                else
                {
                    byte[]? photoBytes = null;
                    string? photoContentType = null;
                    if (photo != null && photo.Length > 0)
                    {
                        using var ms = new MemoryStream();
                        await photo.CopyToAsync(ms);
                        photoBytes = ms.ToArray();
                        photoContentType = photo.ContentType;
                    }

                    var item = new ProgressReport
                    {
                        Project = model.NewProject.Trim(),
                        Location = string.IsNullOrWhiteSpace(model.NewLocation) ? null : model.NewLocation.Trim(),
                        UpdateDate = dt,
                        ProgressPercent = model.NewProgressPercent,
                        Status = model.NewStatus,
                        Notes = string.IsNullOrWhiteSpace(model.NewNotes) ? null : model.NewNotes.Trim(),
                        PhotoData = photoBytes,
                        PhotoContentType = photoContentType,
                        SubmittedBy = Auth.CurrentRole ?? "Site Engineer"
                    };

                    try
                    {
                        var result = await _progress.CreateAsync(item);
                        if (result)
                            return RedirectToAction("Progress", RouteValues(model.StatusFilter, model.DateFilter));
                        model.SubmitError = "Unable to submit progress. Please try again.";
                    }
                    catch (Exception ex)
                    {
                        model.SubmitError = $"Error: {ex.InnerException?.Message ?? ex.Message}";
                    }
                }
            }

            return View("Progress", model);
        }

        [HttpPost]
        public async Task<IActionResult> EditProgress(int id, string statusFilter, string dateFilter,
            string? project, string? location, string? dateTime, string? status, int? progress, string? notes, Microsoft.AspNetCore.Http.IFormFile? editPhoto)
        {
            var model = new ProgressViewModel
            {
                StatusFilter = statusFilter ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                Progress = new List<ProgressReport>(),
                ShowEditModal = true
            };

            ProgressReport? src = null;
            try { src = (await _progress.GetAllAsync()).FirstOrDefault(p => p.Id == id); } catch { }
            if (src == null || src.Status == "Completed")
                return RedirectToAction("Progress", RouteValues(model.StatusFilter, model.DateFilter));

            if (!DateTime.TryParse(dateTime, out var dt)) dt = src.UpdateDate;

            src.Project = project ?? src.Project;
            src.Location = string.IsNullOrWhiteSpace(location) ? null : location;
            src.UpdateDate = dt;
            src.Status = string.IsNullOrEmpty(status) ? src.Status : status;
            src.ProgressPercent = progress ?? src.ProgressPercent;
            src.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            if (editPhoto != null && editPhoto.Length > 0)
            {
                using var ms = new MemoryStream();
                await editPhoto.CopyToAsync(ms);
                src.PhotoData = ms.ToArray();
                src.PhotoContentType = editPhoto.ContentType;
            }

            try
            {
                var result = await _progress.UpdateAsync(src);
                if (result)
                    return RedirectToAction("Progress", RouteValues(model.StatusFilter, model.DateFilter));
                model.EditError = "Unable to update progress.";
            }
            catch (Exception ex)
            {
                model.EditError = $"Error: {ex.InnerException?.Message ?? ex.Message}";
            }

            model.EditItem = src;
            return View("Progress", model);
        }

        [HttpGet]
        public async Task<IActionResult> ProgressPhoto(int id)
        {
            var photo = await _progress.GetByIdAsync(id);
            if (photo?.PhotoData == null || photo.PhotoData.Length == 0) return NotFound();

            Response.Headers.CacheControl = "private, max-age=86400";
            return File(photo.PhotoData, string.IsNullOrWhiteSpace(photo.PhotoContentType) ? "image/png" : photo.PhotoContentType);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveProgress(int id, string statusFilter, string dateFilter)
        {
            try { await _progress.ArchiveAsync(id); } catch { }
            return RedirectToAction("Progress", RouteValues(statusFilter, dateFilter));
        }

        // ---- Project Map ------------------------------------------------------

        [HttpGet]
        public IActionResult Map(string? statusFilter, string? search, string? dateFilterLabel)
        {
            var projects = new List<ProjectMapItem>
            {
                new ProjectMapItem { Name = "Skyline Tower", Location = "Makati City", Status = "Active", Progress = 65 },
                new ProjectMapItem { Name = "Office Complex", Location = "BGC, Taguig", Status = "Active", Progress = 42 },
                new ProjectMapItem { Name = "Megaworld Office Park", Location = "Ortigas", Status = "Active", Progress = 78 },
                new ProjectMapItem { Name = "Rockwell Center", Location = "Mandaluyong", Status = "Active", Progress = 55 },
                new ProjectMapItem { Name = "Ayala Tower", Location = "Makati", Status = "Completed", Progress = 100 },
                new ProjectMapItem { Name = "SM Arena", Location = "Mall of Asia", Status = "Active", Progress = 30 }
            };

            var model = new MapViewModel
            {
                StatusFilter = statusFilter ?? "",
                Search = search ?? "",
                DateFilterLabel = string.IsNullOrEmpty(dateFilterLabel) ? "All" : dateFilterLabel,
                Projects = projects.Where(p =>
                    (string.IsNullOrEmpty(statusFilter) || p.Status == statusFilter) &&
                    (string.IsNullOrEmpty(search) || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Location.Contains(search, StringComparison.OrdinalIgnoreCase))
                ).ToList(),
                ActiveProjectCount = 4,
                LocationCount = 6,
                CompletedCount = 2
            };

            return View(model);
        }

        // ---- My Tasks ---------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Tasks(string? selectedStatus, string? selectedDateRange, string? search,
            DateTime? customStartDate, DateTime? customEndDate, int? view, int? archive)
        {
            var all = new List<PM_Tasks>();
            try { all = await _tasks.GetTasksByAssignedRoleAsync("SiteEngineer"); } catch { }

            var model = new TasksViewModel
            {
                SelectedStatus = selectedStatus ?? "",
                SelectedDateRange = string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange,
                Search = search ?? "",
                CustomStartDate = customStartDate,
                CustomEndDate = customEndDate,
                Tasks = FilterTasks(all, selectedStatus ?? "", string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange, search ?? "", customStartDate, customEndDate)
            };

            if (view.HasValue)
            {
                model.ViewTask = all.FirstOrDefault(t => t.Id == view.Value);
                model.ShowViewModal = model.ViewTask != null;
            }
            if (archive.HasValue)
            {
                model.ArchiveTask = all.FirstOrDefault(t => t.Id == archive.Value);
                model.ShowArchiveConfirm = model.ArchiveTask != null;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus(int id, string selectedStatus, string selectedDateRange, string search, DateTime? customStartDate, DateTime? customEndDate)
        {
            try
            {
                var all = await _tasks.GetTasksByAssignedRoleAsync("SiteEngineer");
                var task = all.FirstOrDefault(t => t.Id == id);
                if (task != null && task.Status != "Completed")
                {
                    task.Status = task.Status == "Pending" ? "In Progress" : "Completed";
                    await _tasks.UpdateTaskAsync(task);
                }
            }
            catch { }
            return RedirectToAction("Tasks", RouteValues(selectedStatus, string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange, search, customStartDate, customEndDate));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveTask(int id, string selectedStatus, string selectedDateRange, string search, DateTime? customStartDate, DateTime? customEndDate)
        {
            try { await _tasks.ArchiveTaskAsync(id); } catch { }
            return RedirectToAction("Tasks", RouteValues(selectedStatus, string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange, search, customStartDate, customEndDate));
        }

        // ---- Reports ----------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Reports(string? reportSection, string? statusFilter, string? search,
            string? selectedDateRange, DateTime? customStartDate, DateTime? customEndDate)
        {
            var progress = new List<ProgressReport>();
            var resources = new List<SiteRequest>();
            var issues = new List<SiteIssue>();
            try { progress = await _progress.GetAllAsync(); } catch { }
            try { resources = await _requests.GetAllAsync(); } catch { }
            try { issues = await _issues.GetAllAsync(); } catch { }

            return BuildReportsModel(reportSection ?? "All", statusFilter ?? "All", search ?? "",
                string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange, customStartDate, customEndDate,
                progress, resources, issues);
        }

        [HttpGet]
        public async Task<IActionResult> ExportToPdf(string? reportSection, string? statusFilter, string? search,
            string? selectedDateRange, DateTime? customStartDate, DateTime? customEndDate)
        {
            var progress = new List<ProgressReport>();
            var resources = new List<SiteRequest>();
            var issues = new List<SiteIssue>();
            try { progress = await _progress.GetAllAsync(); } catch { }
            try { resources = await _requests.GetAllAsync(); } catch { }
            try { issues = await _issues.GetAllAsync(); } catch { }

            var model = BuildReportsModelAux(reportSection ?? "All", statusFilter ?? "All", search ?? "",
                string.IsNullOrEmpty(selectedDateRange) ? "all" : selectedDateRange, customStartDate, customEndDate,
                progress, resources, issues);

            var pdfData = _pdf.GenerateSummaryReport(
                Auth.CurrentDisplayName ?? "Site Engineer",
                DateTime.Now,
                model.FilteredProgress,
                model.FilteredResources,
                model.FilteredIssues);

            var fileName = $"SummaryReports_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return File(pdfData, "application/pdf", fileName);
        }

        // ---- Resource Allocation ----------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Resources(string? statusFilter, string? search, string? dateFilter, string? add, int? view, int? edit)
        {
            var all = new List<SiteRequest>();
            try { all = await _requests.GetAllAsync(); } catch { }

            var model = new ResourcesViewModel
            {
                StatusFilter = statusFilter ?? "",
                Search = search ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                Requests = FilterRequests(all, statusFilter ?? "", search ?? "", string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter),
                ShowAddModal = add == "1",
                NeededByDate = DateTime.Now.AddDays(7).ToString("yyyy-MM-ddTHH:mm")
            };

            if (view.HasValue)
            {
                model.ViewItem = all.FirstOrDefault(r => r.Id == view.Value);
                model.ShowViewModal = model.ViewItem != null;
            }
            if (edit.HasValue)
            {
                var src = all.FirstOrDefault(r => r.Id == edit.Value);
                if (src != null && src.Status != "Approved" && src.Status != "Fulfilled")
                {
                    model.EditItem = src;
                    model.EditManpower = src.Manpower;
                    model.EditNeededByDate = src.NeededBy.ToString("yyyy-MM-ddTHH:mm");
                    model.EditNotes = src.Notes ?? "";
                    model.ShowEditModal = true;
                }
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateRequest(string statusFilter, string search, string dateFilter,
            string? project, string? location, string? notes, string? neededBy, int? manpower,
            string? materials, string? equipment)
        {
            var model = new ResourcesViewModel
            {
                StatusFilter = statusFilter ?? "",
                Search = search ?? "",
                DateFilter = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter,
                ShowAddModal = true,
                NewProject = project ?? "",
                NewLocation = location ?? "",
                NewNotes = notes ?? "",
                NeededByDate = neededBy ?? "",
                NewManpower = manpower ?? 0
            };

            var all = new List<SiteRequest>();
            try { all = await _requests.GetAllAsync(); } catch { }
            model.Requests = FilterRequests(all, model.StatusFilter, model.Search, model.DateFilter);

            if (string.IsNullOrWhiteSpace(model.NewProject))
            {
                model.SubmitError = "Project name is required.";
            }
            else if (all.Any(r => r.Project.Equals(model.NewProject.Trim(), StringComparison.OrdinalIgnoreCase)
                               && (r.Location ?? "").Equals((model.NewLocation ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                               && r.Status == "Pending"))
            {
                model.SubmitError = "A pending request for this project and location already exists.";
            }
            else
            {
                var now = DateTime.Now;
                var dtNeeded = DateTime.TryParse(model.NeededByDate, out var parsed) ? parsed : now.AddDays(7);
                var materialList = SplitLines(materials);
                var equipmentList = SplitLines(equipment);

                var newReq = new SiteRequest
                {
                    RequestId = "",
                    UserId = "SE-001",
                    Project = model.NewProject.Trim(),
                    Location = string.IsNullOrWhiteSpace(model.NewLocation) ? null : model.NewLocation.Trim(),
                    MaterialsJson = materialList.Any() ? JsonSerializer.Serialize(materialList) : null,
                    EquipmentsJson = equipmentList.Any() ? JsonSerializer.Serialize(equipmentList) : null,
                    Manpower = model.NewManpower,
                    WorkerQuantity = model.NewManpower.ToString(),
                    Notes = string.IsNullOrWhiteSpace(model.NewNotes) ? null : model.NewNotes.Trim(),
                    EngineerName = "SiteEngineer",
                    Status = "Pending",
                    NeededBy = dtNeeded,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsArchived = false
                };

                try
                {
                    var (saved, error) = await _requests.CreateAsync(newReq);
                    if (saved)
                        return RedirectToAction("Resources", RouteValues(model.StatusFilter, model.DateFilter, model.Search));
                    model.SubmitError = error;
                }
                catch (Exception ex)
                {
                    model.SubmitError = $"Unexpected error: {ex.Message}";
                }
            }

            return View("Resources", model);
        }

        [HttpPost]
        public async Task<IActionResult> EditRequest(int id, string statusFilter, string search, string dateFilter,
            int? manpower, string? neededBy, string? notes)
        {
            var all = new List<SiteRequest>();
            try { all = await _requests.GetAllAsync(); } catch { }

            var statusF = statusFilter ?? "";
            var srchF = search ?? "";
            var dateF = string.IsNullOrEmpty(dateFilter) ? "All" : dateFilter;

            var model = new ResourcesViewModel
            {
                StatusFilter = statusF,
                Search = srchF,
                DateFilter = dateF,
                Requests = FilterRequests(all, statusF, srchF, dateF),
                ShowEditModal = true,
                EditManpower = manpower ?? 0,
                EditNeededByDate = neededBy ?? "",
                EditNotes = notes ?? ""
            };

            var src = all.FirstOrDefault(r => r.Id == id);
            if (src == null || src.Status == "Approved" || src.Status == "Fulfilled")
                return RedirectToAction("Resources", RouteValues(model.StatusFilter, model.DateFilter, model.Search));

            var dtNeeded = DateTime.TryParse(neededBy, out var parsed2) ? parsed2 : src.NeededBy;
            src.Manpower = manpower ?? src.Manpower;
            src.WorkerQuantity = (manpower ?? src.Manpower).ToString();
            src.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            src.NeededBy = dtNeeded;
            src.UpdatedAt = DateTime.Now;

            try
            {
                var updated = await _requests.UpdateAsync(src);
                if (updated)
                    return RedirectToAction("Resources", RouteValues(model.StatusFilter, model.DateFilter, model.Search));
                model.EditError = "Failed to update request in database.";
            }
            catch (Exception ex)
            {
                model.EditError = $"Unexpected error: {ex.Message}";
            }

            model.EditItem = src;
            return View("Resources", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveRequest(int id, string statusFilter, string search, string dateFilter)
        {
            try { await _requests.ArchiveAsync(id); } catch { }
            return RedirectToAction("Resources", RouteValues(statusFilter, dateFilter, search));
        }

        // ---- Shared helpers ---------------------------------------------------

        private async Task<List<ArchivedItem>> BuildArchivedItemsAsync()
        {
            var progressList = new List<ProgressReport>();
            var resourceList = new List<SiteRequest>();
            var issueList = new List<SiteIssue>();
            var taskList = new List<PM_Tasks>();
            try { progressList = await _progress.GetArchivedAsync(); } catch { }
            try { resourceList = await _requests.GetArchivedAsync(); } catch { }
            try { issueList = await _issues.GetArchivedAsync(); } catch { }
            try { taskList = await _tasks.GetArchivedTasksAsync(); } catch { }

            var items = new List<ArchivedItem>();
            foreach (var p in progressList)
                items.Add(new ArchivedItem
                {
                    Type = "Progress",
                    DisplayId = p.ProgressId ?? $"PRG-{p.Id:D3}",
                    Project = p.Project ?? "-",
                    Details = $"{p.ProgressPercent}%",
                    Status = p.Status ?? "-",
                    StatusClass = GetProgressStatusClass(p.Status),
                    ArchivedOnDisplay = p.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = p.Id,
                    OriginalType = "progress",
                    RefProgress = p,
                    SortOn = p.ArchivedOn ?? DateTime.MinValue
                });
            foreach (var r in resourceList)
                items.Add(new ArchivedItem
                {
                    Type = "Request",
                    DisplayId = r.RequestId ?? $"REQ-{r.Id:D3}",
                    Project = r.Project ?? "-",
                    Details = $"Manpower: {r.Manpower}",
                    Status = r.Status ?? "-",
                    StatusClass = GetRequestStatusClass(r.Status),
                    ArchivedOnDisplay = r.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = r.Id,
                    OriginalType = "resource",
                    RefResource = r,
                    SortOn = r.ArchivedOn ?? DateTime.MinValue
                });
            foreach (var i in issueList)
                items.Add(new ArchivedItem
                {
                    Type = "Issue",
                    DisplayId = i.IssueId ?? $"ISS-{i.Id:D3}",
                    Project = i.Project ?? "-",
                    Details = $"{i.IssueType} - {i.Severity}",
                    Status = i.Status ?? "-",
                    StatusClass = GetIssueStatusClass(i.Status),
                    ArchivedOnDisplay = i.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = i.Id,
                    OriginalType = "issue",
                    RefIssue = i,
                    SortOn = i.ArchivedOn ?? DateTime.MinValue
                });
            foreach (var t in taskList)
                items.Add(new ArchivedItem
                {
                    Type = "Task",
                    DisplayId = $"TSK {t.Id:D3}",
                    Project = t.Project ?? "-",
                    Details = t.Name ?? "-",
                    Status = t.Status ?? "-",
                    StatusClass = GetProgressStatusClass(t.Status),
                    ArchivedOnDisplay = t.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = t.Id,
                    OriginalType = "task",
                    RefTask = t,
                    SortOn = t.ArchivedOn ?? DateTime.MinValue
                });
            return items.OrderByDescending(a => a.SortOn).ToList();
        }

        private static List<ArchivedItem> FilterArchivedItems(List<ArchivedItem> items, string selectedType, string searchQuery)
        {
            var query = items.AsEnumerable();
            if (selectedType != "all")
                query = query.Where(a => a.OriginalType == selectedType);
            if (!string.IsNullOrEmpty(searchQuery))
                query = query.Where(a =>
                    a.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayId.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    a.Details.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                    a.Type.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
            return query.ToList();
        }

        private static ArchivedItem? ResolveArchivedItem(List<ArchivedItem> items, string? view)
        {
            if (string.IsNullOrEmpty(view)) return null;
            var parts = view.Split('=', 2);
            if (parts.Length != 2) return null;
            if (!int.TryParse(parts[1], out var id)) return null;
            return items.FirstOrDefault(i => i.OriginalType == parts[0] && i.OriginalId == id);
        }

        private static List<SiteIssue> FilterIssues(List<SiteIssue> issues, string statusFilter, string severityFilter, string dateFilter, string search)
        {
            return issues.Where(i =>
                (string.IsNullOrEmpty(statusFilter) || i.Status == statusFilter) &&
                (string.IsNullOrEmpty(severityFilter) || i.Severity == severityFilter) &&
                (string.IsNullOrEmpty(search) || i.Project.Contains(search, StringComparison.OrdinalIgnoreCase) || i.IssueType.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (dateFilter == "All" || CheckDateFilter(i.DateReported, dateFilter)) &&
                !i.IsArchived
            ).ToList();
        }

        private static List<ProgressReport> FilterProgress(List<ProgressReport> progress, string statusFilter, string dateFilter)
        {
            return progress.Where(p =>
                (string.IsNullOrEmpty(statusFilter) || p.Status == statusFilter) &&
                (dateFilter == "All" || CheckDateFilter(p.UpdateDate, dateFilter))
            ).ToList();
        }

        private static List<SiteRequest> FilterRequests(List<SiteRequest> requests, string statusFilter, string search, string dateFilter)
        {
            return requests.Where(m =>
                (string.IsNullOrEmpty(statusFilter) || m.Status == statusFilter) &&
                (string.IsNullOrEmpty(search) || m.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (dateFilter == "All" || CheckDateFilter(m.NeededBy, dateFilter))
            ).ToList();
        }

        private static List<PM_Tasks> FilterTasks(List<PM_Tasks> tasks, string selectedStatus, string selectedDateRange, string search,
            DateTime? customStartDate, DateTime? customEndDate)
        {
            var query = tasks.AsEnumerable();
            if (!string.IsNullOrEmpty(selectedStatus))
                query = query.Where(t => t.Status == selectedStatus);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(t =>
                    (t.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (t.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (t.Location ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));

            var today = DateTime.Today;
            query = selectedDateRange switch
            {
                "today" => query.Where(t => t.StartDate.HasValue && t.StartDate.Value.Date == today),
                "thisweek" => query.Where(t =>
                {
                    if (!t.StartDate.HasValue) return false;
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    return t.StartDate.Value.Date >= startOfWeek && t.StartDate.Value.Date < startOfWeek.AddDays(7);
                }),
                "thismonth" => query.Where(t => t.StartDate.HasValue &&
                    t.StartDate.Value.Year == today.Year && t.StartDate.Value.Month == today.Month),
                "thisyear" => query.Where(t => t.StartDate.HasValue && t.StartDate.Value.Year == today.Year),
                "customrange" => query.Where(t =>
                {
                    if (!t.StartDate.HasValue) return false;
                    var from = customStartDate ?? DateTime.MinValue;
                    var to = customEndDate ?? DateTime.MaxValue;
                    return t.StartDate.Value.Date >= from && t.StartDate.Value.Date <= to;
                }),
                _ => query
            };
            return query.OrderByDescending(t => t.CreatedAt).ToList();
        }

        private ActionResult BuildReportsModel(string section, string status, string search, string range,
            DateTime? start, DateTime? end, List<ProgressReport> progress, List<SiteRequest> resources, List<SiteIssue> issues)
        {
            var model = BuildReportsModelAux(section, status, search, range, start, end, progress, resources, issues);
            return View("Reports", model);
        }

        private static ReportsViewModel BuildReportsModelAux(string section, string status, string search, string range,
            DateTime? start, DateTime? end, List<ProgressReport> progress, List<SiteRequest> resources, List<SiteIssue> issues)
        {
            var model = new ReportsViewModel
            {
                ReportSection = section,
                StatusFilter = status,
                Search = search,
                SelectedDateRange = range,
                CustomStartDate = start,
                CustomEndDate = end,
                Progress = progress,
                Resources = resources,
                Issues = issues
            };
            model.FilteredProgress = progress.Where(p =>
                (status == "All" || p.Status == status) &&
                (string.IsNullOrEmpty(search) || p.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                ReportsDateInRange(p.UpdateDate, range, start, end)
            ).ToList();
            model.FilteredResources = resources.Where(r =>
                (status == "All" || r.Status == status) &&
                (string.IsNullOrEmpty(search) || r.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                ReportsDateInRange(r.NeededBy, range, start, end)
            ).ToList();
            model.FilteredIssues = issues.Where(i =>
                (status == "All" || i.Status == status) &&
                (string.IsNullOrEmpty(search) || i.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                ReportsDateInRange(i.DateReported, range, start, end)
            ).ToList();
            return model;
        }

        private static bool DateMatches(DateTime date, string filter)
        {
            var today = DateTime.Today;
            return filter switch
            {
                "Today" => date.Date == today,
                "This Week" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                "This Month" => date.Year == today.Year && date.Month == today.Month,
                "This Year" => date.Year == today.Year,
                _ => true
            };
        }

        private static bool CheckDateFilter(DateTime date, string dateFilter)
        {
            var today = DateTime.Today;
            return dateFilter switch
            {
                "Today" => date.Date == today,
                "This Week" => date.Date >= today && date.Date <= today.AddDays(7),
                "This Month" => date.Month == today.Month && date.Year == today.Year,
                "This Year" => date.Year == today.Year,
                _ => true
            };
        }

        private static bool ReportsDateInRange(DateTime date, string range, DateTime? start, DateTime? end)
        {
            var today = DateTime.Today;
            return range switch
            {
                "today" => date.Date == today,
                "thisweek" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                "thismonth" => date.Year == today.Year && date.Month == today.Month,
                "thisyear" => date.Year == today.Year,
                "customrange" => (!start.HasValue || date >= start.Value) &&
                                 (!end.HasValue || date <= end.Value.AddDays(1)),
                _ => true
            };
        }

        private static DateTime StartOfWeek(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }

        private static string NormalizeDashboardFilter(string? filter)
        {
            return filter is "Today" or "This Week" or "This Month" or "This Year" or "All" ? filter : "All";
        }

        private static string FormatRelTime(DateTime dt)
        {
            var diff = DateTime.Now - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return dt.ToString("MMM dd");
        }

        private static string GetProgressStatusClass(string? s) => s switch
        {
            "Completed" => "badge-green",
            "In Progress" => "badge-blue",
            "Reviewed" => "badge-purple",
            _ => "badge-yellow"
        };

        private static string GetRequestStatusClass(string? s) => s switch
        {
            "Approved" => "badge-green",
            "Pending" => "badge-yellow",
            "Rejected" => "badge-red",
            "Fulfilled" => "badge-teal",
            _ => "badge-yellow"
        };

        private static string GetIssueStatusClass(string? s) => s switch
        {
            "Resolved" => "badge-green",
            "In Progress" => "badge-blue",
            _ => "badge-yellow"
        };

        private static List<string> SplitLines(string? value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return result;
            foreach (var line in value.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        private static RouteValueDictionary RouteValues(string statusFilter, string severityFilter, string dateFilter, string search)
        {
            return new RouteValueDictionary
            {
                ["statusFilter"] = statusFilter,
                ["severityFilter"] = severityFilter,
                ["dateFilter"] = dateFilter,
                ["search"] = search
            };
        }

        private static RouteValueDictionary RouteValues(string statusFilter, string dateFilter)
        {
            return new RouteValueDictionary
            {
                ["statusFilter"] = statusFilter,
                ["dateFilter"] = dateFilter
            };
        }

        private static RouteValueDictionary RouteValues(string statusFilter, string dateFilter, string search)
        {
            return new RouteValueDictionary
            {
                ["statusFilter"] = statusFilter,
                ["dateFilter"] = dateFilter,
                ["search"] = search
            };
        }

        private static RouteValueDictionary RouteValues(string selectedStatus, string selectedDateRange, string search, DateTime? customStartDate, DateTime? customEndDate)
        {
            return new RouteValueDictionary
            {
                ["selectedStatus"] = selectedStatus,
                ["selectedDateRange"] = selectedDateRange,
                ["search"] = search,
                ["customStartDate"] = customStartDate?.ToString("yyyy-MM-dd"),
                ["customEndDate"] = customEndDate?.ToString("yyyy-MM-dd")
            };
        }
    }
}