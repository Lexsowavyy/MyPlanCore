using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    public class QuantitySurveyorController : AppControllerBase
    {
        private readonly QS_CostEstimateService _estimateService;
        private readonly QS_BudgetService _budgetService;
        private readonly PM_TaskService _taskService;
        private readonly SiteRequestService _siteRequestService;
        private readonly PdfExportService _pdfExport;

        public QuantitySurveyorController(AuthService auth, SubscriptionService subscription, AuditService audit,
            QS_CostEstimateService estimateService, QS_BudgetService budgetService, PM_TaskService taskService,
            SiteRequestService siteRequestService, PdfExportService pdfExport)
            : base(auth, subscription, audit)
        {
            _estimateService = estimateService;
            _budgetService = budgetService;
            _taskService = taskService;
            _siteRequestService = siteRequestService;
            _pdfExport = pdfExport;
        }

        protected override string? RequiredRole => "QuantitySurveyor";

        [HttpGet]
        public async Task<IActionResult> Index(string? filter)
        {
            var model = new QSIndexViewModel
            {
                DisplayName = Auth.CurrentDisplayName ?? "User",
                DashboardFilter = string.IsNullOrEmpty(filter) ? "All" : filter
            };

            var estimates = await LoadEstimatesAsync();
            var budgets = await LoadBudgetsAsync();
            var tasks = await LoadTasksAsync();

            var filteredEstimates = estimates.Where(e => DateMatches(e.CreatedDate, model.DashboardFilter)).ToList();
            var filteredBudgets = budgets.Where(b => DateMatches(b.CreatedAt, model.DashboardFilter)).ToList();

            model.EstimateCount = filteredEstimates.Count;
            model.BudgetCount = filteredBudgets.Count;
            model.PendingApprovalCount = filteredEstimates.Count(e => e.Status == "Pending Approval") + filteredBudgets.Count(b => b.Status == "Pending Approval");
            model.ApprovedCount = filteredEstimates.Count(e => e.Status == "Approved") + filteredBudgets.Count(b => b.Status == "Approved");
            model.OverBudgetCount = filteredBudgets.Count(b => b.Remaining < 0);
            model.WaitingPMCount = filteredEstimates.Count(e => e.Status == "Waiting for PM Approval") + filteredBudgets.Count(b => b.Status == "Waiting for PM Approval");
            model.RejectedCount = filteredEstimates.Count(e => e.Status == "Rejected") + filteredBudgets.Count(b => b.Status == "Rejected");

            model.EstimateTotalValue = filteredEstimates.Sum(e => e.TotalCost);
            model.TotalBudgetValue = filteredBudgets.Sum(b => b.TotalBudget);
            model.TotalSpentValue = filteredBudgets.Sum(b => b.TotalSpent);
            model.RemainingValue = model.TotalBudgetValue - model.TotalSpentValue;

            model.TotalLabor = filteredEstimates.Sum(e => e.LaborCost);
            model.TotalMaterials = filteredEstimates.Sum(e => e.MaterialsCost);
            model.TotalEquipment = filteredEstimates.Sum(e => e.EquipmentCost);
            model.TotalEstimateCost = filteredEstimates.Sum(e => e.TotalCost);

            model.TaskCount = tasks.Count;

            model.ChartBudgets = filteredBudgets.OrderByDescending(b => b.TotalBudget).Take(5).Select(b => new ChartBudgetItem
            {
                ProjectName = b.Project.Length > 15 ? b.Project[..15] + "..." : b.Project,
                Budget = b.TotalBudget,
                Spent = b.TotalSpent,
                MaxValue = filteredBudgets.Any() ? filteredBudgets.Max(x => x.TotalBudget) + 0.01m : 1
            }).ToList();

            var healthTotal = filteredBudgets.Count > 0 ? filteredBudgets.Count : 1;
            model.BudgetsHealth = new List<HealthCountItem>
            {
                new() { Label = "On Track", Count = filteredBudgets.Count(b => b.Health == "On Track"), Total = healthTotal },
                new() { Label = "Warning", Count = filteredBudgets.Count(b => b.Health == "Warning"), Total = healthTotal },
                new() { Label = "Critical", Count = filteredBudgets.Count(b => b.Health == "Critical"), Total = healthTotal },
                new() { Label = "Over Budget", Count = filteredBudgets.Count(b => b.Health == "Over Budget"), Total = healthTotal },
            };

            model.EstimateStatuses = new List<QsStatusCountItem>
            {
                new() { Label = "Approved", Count = filteredEstimates.Count(e => e.Status == "Approved") },
                new() { Label = "Waiting for PM Approval", Count = filteredEstimates.Count(e => e.Status == "Waiting for PM Approval") },
                new() { Label = "Pending Approval", Count = filteredEstimates.Count(e => e.Status == "Pending Approval") },
                new() { Label = "Rejected", Count = filteredEstimates.Count(e => e.Status == "Rejected") },
            };

            var recent = new List<QsActivityItem>();
            foreach (var e in filteredEstimates.OrderByDescending(e => e.CreatedDate).Take(5))
                recent.Add(new QsActivityItem { Type = "Estimate", Title = $"EST-{e.EstimateId:D3} - {e.ProjectName}", Subtitle = $"{e.TotalCost:N2} | {e.Status}", Date = e.CreatedDate.ToString("MMM d, yyyy"), SortDate = e.CreatedDate });
            foreach (var b in filteredBudgets.OrderByDescending(b => b.CreatedAt).Take(5))
                recent.Add(new QsActivityItem { Type = "Budget", Title = $"BUD-{b.Id:D3} - {b.Project}", Subtitle = $"{b.TotalBudget:N2} | {b.Status}", Date = b.CreatedAt.ToString("MMM d, yyyy"), SortDate = b.CreatedAt });
            model.RecentItems = recent.OrderByDescending(i => i.SortDate).Take(8).ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Resources(string? search, string? type, string? dateRange,
            string? from, string? to, int? view, int? archive)
        {
            var requests = await LoadApprovedRequestsAsync();
            var model = BuildResourcesModel(requests, search, type, dateRange, from, to);

            if (view.HasValue)
            {
                model.SelectedRequest = requests.FirstOrDefault(r => r.Id == view.Value);
                if (model.SelectedRequest != null)
                    model.Modal = "view";
            }
            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveRequest(int id)
        {
            await _siteRequestService.ArchiveAsync(id);
            return RedirectToAction("Resources");
        }

        [HttpGet]
        public async Task<IActionResult> Archived(string? type, string? search, string? viewType, int? viewId,
            int? restoreId, string? restoreType, int? deleteId, string? deleteType)
        {
            var estimates = await LoadArchivedEstimatesAsync();
            var budgets = await LoadArchivedBudgetsAsync();
            var model = BuildArchivedModel(estimates, budgets, type, search);

            if (!string.IsNullOrEmpty(viewType) && viewId.HasValue)
            {
                if (viewType == "estimate")
                {
                    var e = estimates.FirstOrDefault(x => x.EstimateId == viewId.Value);
                    if (e != null)
                    {
                        model.ViewEstimate = e;
                        model.ViewTitle = $"Estimate - EST-{e.EstimateId:D3}";
                        model.Modal = "view";
                    }
                }
                else if (viewType == "budget")
                {
                    var b = budgets.FirstOrDefault(x => x.Id == viewId.Value);
                    if (b != null)
                    {
                        model.ViewBudget = b;
                        model.ViewTitle = $"Budget - BDG-{b.Id:D3}";
                        model.Modal = "view";
                    }
                }
            }
            if (restoreId.HasValue && !string.IsNullOrEmpty(restoreType))
            {
                model.Modal = "restore";
                model.RestoreId = restoreId.Value;
                model.RestoreType = restoreType;
            }
            if (deleteId.HasValue && !string.IsNullOrEmpty(deleteType))
            {
                model.Modal = "delete";
                model.DeleteId = deleteId.Value;
                model.DeleteType = deleteType;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> RestoreItem(int id, string type)
        {
            if (type == "estimate")
                await _estimateService.RestoreAsync(id);
            else if (type == "budget")
                await _budgetService.RestoreAsync(id);
            return RedirectToAction("Archived");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteItem(int id, string type)
        {
            if (type == "estimate")
                await _estimateService.DeleteAsync(id);
            else if (type == "budget")
                await _budgetService.DeleteAsync(id);
            return RedirectToAction("Archived");
        }

        [HttpGet]
        public async Task<IActionResult> CostEstimates(string? search, string? status, string? dateRange,
            string? from, string? to, int? view, int? edit, int? add, int? archive)
        {
            var estimates = await LoadEstimatesAsync();
            var model = BuildCostEstimatesModel(estimates, search, status, dateRange, from, to);

            if (add == 1)
                model.Modal = "new";

            if (view.HasValue)
            {
                model.SelectedEstimate = estimates.FirstOrDefault(e => e.EstimateId == view.Value);
                if (model.SelectedEstimate != null)
                    model.Modal = "view";
            }

            if (edit.HasValue)
            {
                var target = estimates.FirstOrDefault(e => e.EstimateId == edit.Value);
                if (target != null)
                {
                    model.Modal = "edit";
                    model.EditId = target.EstimateId;
                    model.EditProjectName = target.ProjectName;
                    model.EditLocation = target.Location ?? "";
                    model.EditLaborCost = FormatCost(target.LaborCost);
                    model.EditMaterialsCost = FormatCost(target.MaterialsCost);
                    model.EditEquipmentCost = FormatCost(target.EquipmentCost);
                    model.EditNotes = target.Notes ?? "";
                }
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateEstimate(string projectName, string? location, string? scheduledDate,
            string? laborCost, string? materialsCost, string? equipmentCost, string? notes,
            string? search, string? status, string? dateRange, string? from, string? to)
        {
            var estimates = await LoadEstimatesAsync();
            var model = BuildCostEstimatesModel(estimates, search, status, dateRange, from, to);
            model.Modal = "new";
            model.NewProjectName = projectName ?? "";
            model.NewLocation = location ?? "";
            model.NewScheduledDate = scheduledDate ?? "";
            model.NewLaborCost = laborCost ?? "";
            model.NewMaterialsCost = materialsCost ?? "";
            model.NewEquipmentCost = equipmentCost ?? "";
            model.NewNotes = notes ?? "";

            if (string.IsNullOrWhiteSpace(projectName))
            {
                model.NewError = "Project Name is required.";
            }
            else if (estimates.Any(e => e.ProjectName.Equals(projectName.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                model.NewError = "An estimate for this project already exists.";
            }
            else
            {
                try
                {
                    var newEstimate = new CostEstimate
                    {
                        ProjectName = projectName,
                        Location = location,
                        Status = "Pending Approval",
                        ScheduledDate = ParseDate(scheduledDate),
                        LaborCost = ParseCostInput(laborCost),
                        MaterialsCost = ParseCostInput(materialsCost),
                        EquipmentCost = ParseCostInput(equipmentCost),
                        Notes = notes
                    };
                    var result = await _estimateService.CreateAsync(newEstimate);
                    if (result != null)
                        return RedirectToAction("CostEstimates");
                    model.NewError = "Unable to create estimate. Please try again.";
                }
                catch
                {
                    model.NewError = "An unexpected error occurred.";
                }
            }

            return View("CostEstimates", model);
        }

        [HttpPost]
        public async Task<IActionResult> SaveEstimate(int id, string projectName, string? location,
            string? laborCost, string? materialsCost, string? equipmentCost, string? notes,
            string? search, string? status, string? dateRange, string? from, string? to)
        {
            var estimates = await LoadEstimatesAsync();
            var model = BuildCostEstimatesModel(estimates, search, status, dateRange, from, to);
            model.Modal = "edit";
            model.EditId = id;
            model.EditProjectName = projectName ?? "";
            model.EditLocation = location ?? "";
            model.EditLaborCost = laborCost ?? "";
            model.EditMaterialsCost = materialsCost ?? "";
            model.EditEquipmentCost = equipmentCost ?? "";
            model.EditNotes = notes ?? "";

            if (string.IsNullOrWhiteSpace(projectName))
            {
                model.EditError = "Project Name is required.";
            }
            else
            {
                var updated = await _estimateService.UpdateAsync(new CostEstimate
                {
                    EstimateId = id,
                    ProjectName = projectName,
                    Location = location,
                    LaborCost = ParseCostInput(laborCost),
                    MaterialsCost = ParseCostInput(materialsCost),
                    EquipmentCost = ParseCostInput(equipmentCost),
                    Notes = notes
                });
                if (updated)
                    return RedirectToAction("CostEstimates");
                model.EditError = "Unable to save changes. Please try again.";
            }

            return View("CostEstimates", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveEstimate(int id)
        {
            var success = await _estimateService.ArchiveAsync(id);
            if (success)
                return RedirectToAction("Archived");
            return RedirectToAction("CostEstimates");
        }

        [HttpGet]
        public async Task<IActionResult> Budget(string? search, string? status, string? dateRange,
            string? from, string? to, int? view, int? add, int? archive)
        {
            var budgets = await LoadBudgetsAsync();
            var model = BuildBudgetModel(budgets, search, status, dateRange, from, to);

            if (add == 1)
                model.Modal = "new";

            if (view.HasValue)
            {
                model.SelectedBudget = budgets.FirstOrDefault(b => b.Id == view.Value);
                if (model.SelectedBudget != null)
                    model.Modal = "view";
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateBudget(string project, string? location, string? budgetDate,
            string? manpowerBudget, string? equipmentBudget, string? materialsBudget,
            string? manpowerSpent, string? equipmentSpent, string? materialsSpent, string? notes,
            string? search, string? status, string? dateRange, string? from, string? to)
        {
            var budgets = await LoadBudgetsAsync();
            var model = BuildBudgetModel(budgets, search, status, dateRange, from, to);
            model.Modal = "new";
            model.NewProject = project ?? "";
            model.NewLocation = location ?? "";
            model.NewBudgetDate = budgetDate ?? "";
            model.NewManpowerBudget = manpowerBudget ?? "";
            model.NewEquipmentBudget = equipmentBudget ?? "";
            model.NewMaterialsBudget = materialsBudget ?? "";
            model.NewManpowerSpent = manpowerSpent ?? "";
            model.NewEquipmentSpent = equipmentSpent ?? "";
            model.NewMaterialsSpent = materialsSpent ?? "";
            model.NewNotes = notes ?? "";

            if (string.IsNullOrWhiteSpace(project))
            {
                model.NewError = "Project Name is required.";
            }
            else if (budgets.Any(b => b.Project.Equals(project.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                model.NewError = "A budget for this project already exists.";
            }
            else
            {
                try
                {
                    var newBudget = new QS_Budget
                    {
                        Project = project,
                        Location = location ?? "",
                        BudgetDate = ParseDate(budgetDate),
                        ManpowerBudget = ParseCostInput(manpowerBudget),
                        EquipmentBudget = ParseCostInput(equipmentBudget),
                        MaterialsBudget = ParseCostInput(materialsBudget),
                        ManpowerSpent = ParseCostInput(manpowerSpent),
                        EquipmentSpent = ParseCostInput(equipmentSpent),
                        MaterialsSpent = ParseCostInput(materialsSpent),
                        Notes = notes
                    };
                    var result = await _budgetService.CreateAsync(newBudget);
                    if (result != null)
                        return RedirectToAction("Budget");
                    model.NewError = "Unable to create budget. Please try again.";
                }
                catch
                {
                    model.NewError = "An unexpected error occurred.";
                }
            }

            return View("Budget", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveBudget(int id)
        {
            var success = await _budgetService.ArchiveAsync(id);
            if (success)
                return RedirectToAction("Archived");
            return RedirectToAction("Budget");
        }

        [HttpGet]
        public async Task<IActionResult> Reports(string? section, string? status, string? search,
            string? dateRange, string? from, string? to)
        {
            var estimates = await LoadEstimatesAsync();
            var budgets = await LoadBudgetsAsync();
            var model = BuildReportsModel(estimates, budgets, section, status, search, dateRange, from, to);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf(string? section, string? status, string? search,
            string? dateRange, string? from, string? to)
        {
            var estimates = await LoadEstimatesAsync();
            var budgets = await LoadBudgetsAsync();
            var model = BuildReportsModel(estimates, budgets, section, status, search, dateRange, from, to);

            var pdfData = _pdfExport.GenerateQSReport(
                "Quantity Surveyor",
                DateTime.Now,
                model.EstimateItems,
                model.BudgetItems);

            var fileName = $"QSReports_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return File(pdfData, "application/pdf", fileName);
        }

        [HttpGet]
        public async Task<IActionResult> Tasks(string? search, string? status, string? dateRange,
            string? from, string? to, int? view, int? archive)
        {
            var tasks = await LoadTasksAsync();
            var model = BuildTasksModel(tasks, search, status, dateRange, from, to);

            if (view.HasValue)
            {
                model.SelectedTask = tasks.FirstOrDefault(t => t.Id == view.Value);
                if (model.SelectedTask != null)
                    model.Modal = "view";
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveTask(int id)
        {
            await _taskService.ArchiveTaskAsync(id);
            return RedirectToAction("Tasks");
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus(int id)
        {
            var task = await _taskService.GetTaskByIdAsync(id);
            if (task != null && task.Status != "Completed")
            {
                task.Status = task.Status == "Pending" ? "In Progress" : "Completed";
                await _taskService.UpdateTaskAsync(task);
            }
            return RedirectToAction("Tasks");
        }

        private async Task<List<CostEstimate>> LoadEstimatesAsync()
        {
            try { return await _estimateService.GetAllAsync(); }
            catch { return new List<CostEstimate>(); }
        }

        private async Task<List<CostEstimate>> LoadArchivedEstimatesAsync()
        {
            try { return await _estimateService.GetArchivedAsync(); }
            catch { return new List<CostEstimate>(); }
        }

        private async Task<List<QS_Budget>> LoadBudgetsAsync()
        {
            try { return await _budgetService.GetAllAsync(); }
            catch { return new List<QS_Budget>(); }
        }

        private async Task<List<QS_Budget>> LoadArchivedBudgetsAsync()
        {
            try { return await _budgetService.GetArchivedAsync(); }
            catch { return new List<QS_Budget>(); }
        }

        private async Task<List<PM_Tasks>> LoadTasksAsync()
        {
            try { return await _taskService.GetTasksByAssignedRoleAsync("QuantitySurveyor"); }
            catch { return new List<PM_Tasks>(); }
        }

        private async Task<List<SiteRequest>> LoadApprovedRequestsAsync()
        {
            try { return await _siteRequestService.GetApprovedAsync(); }
            catch { return new List<SiteRequest>(); }
        }

        private static QSResourcesViewModel BuildResourcesModel(List<SiteRequest> requests, string? search,
            string? type, string? dateRange, string? from, string? to)
        {
            var model = new QSResourcesViewModel
            {
                ApprovedCount = requests.Count,
                TotalMaterials = requests.Sum(r => ParseJsonList(r.MaterialsJson).Count),
                TotalEquipment = requests.Sum(r => ParseJsonList(r.EquipmentsJson).Count),
                TotalManpower = requests.Sum(r => r.Manpower),
                Search = search ?? "",
                SelectedType = type ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to)
            };

            var query = requests.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedType))
            {
                query = model.SelectedType switch
                {
                    "materials" => query.Where(r => ParseJsonList(r.MaterialsJson).Any()),
                    "equipment" => query.Where(r => ParseJsonList(r.EquipmentsJson).Any()),
                    "manpower" => query.Where(r => r.Manpower > 0),
                    _ => query
                };
            }

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(r =>
                    (r.Project ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (r.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var today = DateTime.Today;
            query = model.SelectedDateRange switch
            {
                "today" => query.Where(r => r.ReviewedAt.HasValue && r.ReviewedAt.Value.Date == today),
                "thisweek" => query.Where(r =>
                {
                    if (!r.ReviewedAt.HasValue) return false;
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    return r.ReviewedAt.Value.Date >= startOfWeek && r.ReviewedAt.Value.Date < startOfWeek.AddDays(7);
                }),
                "thismonth" => query.Where(r => r.ReviewedAt.HasValue &&
                    r.ReviewedAt.Value.Year == today.Year && r.ReviewedAt.Value.Month == today.Month),
                "thisyear" => query.Where(r => r.ReviewedAt.HasValue && r.ReviewedAt.Value.Year == today.Year),
                "customrange" => query.Where(r =>
                {
                    if (!r.ReviewedAt.HasValue) return false;
                    var rangeFrom = model.CustomStartDate ?? DateTime.MinValue;
                    var rangeTo = model.CustomEndDate ?? DateTime.MaxValue;
                    return r.ReviewedAt.Value.Date >= rangeFrom && r.ReviewedAt.Value.Date <= rangeTo;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(r => r.ReviewedAt).ToList();
            return model;
        }

        private static QSArchivedViewModel BuildArchivedModel(List<CostEstimate> estimates, List<QS_Budget> budgets,
            string? type, string? search)
        {
            var model = new QSArchivedViewModel
            {
                SelectedType = string.IsNullOrEmpty(type) ? "all" : type,
                Search = search ?? ""
            };

            var rows = new List<QSArchivedRow>();

            foreach (var e in estimates)
            {
                rows.Add(new QSArchivedRow
                {
                    Type = "Estimate",
                    DisplayId = $"EST-{e.EstimateId:D3}",
                    Project = e.ProjectName ?? "-",
                    Details = $"{e.TotalCost:N0}",
                    Status = e.Status ?? "-",
                    StatusClass = GetEstimateStatusClass(e.Status),
                    ArchivedOnDisplay = e.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = e.EstimateId,
                    OriginalType = "estimate",
                    SortDate = e.ArchivedOn ?? DateTime.MinValue
                });
            }

            foreach (var b in budgets)
            {
                rows.Add(new QSArchivedRow
                {
                    Type = "Budget",
                    DisplayId = $"BDG-{b.Id:D3}",
                    Project = b.Project ?? "-",
                    Details = $"{(b.Location ?? "-")}",
                    Status = b.Health ?? "-",
                    StatusClass = GetBudgetHealthClass(b.Health),
                    ArchivedOnDisplay = b.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = b.Id,
                    OriginalType = "budget",
                    SortDate = b.ArchivedOn ?? DateTime.MinValue
                });
            }

            var query = rows.AsEnumerable();

            if (model.SelectedType != "all")
                query = query.Where(a => a.OriginalType == model.SelectedType);

            if (!string.IsNullOrEmpty(model.Search))
                query = query.Where(a =>
                    a.Project.Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayId.Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    a.Details.Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    a.Type.Contains(model.Search, StringComparison.OrdinalIgnoreCase));

            model.Items = query.OrderByDescending(a => a.SortDate).ToList();
            return model;
        }

        private static QSCostEstimatesViewModel BuildCostEstimatesModel(List<CostEstimate> estimates, string? search,
            string? status, string? dateRange, string? from, string? to)
        {
            var model = new QSCostEstimatesViewModel
            {
                TotalCount = estimates.Count,
                PendingCount = estimates.Count(e => e.Status == "Pending Approval"),
                WaitingPMCount = estimates.Count(e => e.Status == "Waiting for PM Approval"),
                ApprovedCount = estimates.Count(e => e.Status == "Approved"),
                Search = search ?? "",
                SelectedStatus = status ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to)
            };

            var query = estimates.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(e => e.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(e =>
                    (e.ProjectName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (e.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var today = DateTime.Today;
            query = model.SelectedDateRange switch
            {
                "today" => query.Where(e => e.CreatedDate.Date == today),
                "thisweek" => query.Where(e =>
                {
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    return e.CreatedDate.Date >= startOfWeek && e.CreatedDate.Date < startOfWeek.AddDays(7);
                }),
                "thismonth" => query.Where(e =>
                    e.CreatedDate.Year == today.Year && e.CreatedDate.Month == today.Month),
                "thisyear" => query.Where(e => e.CreatedDate.Year == today.Year),
                "customrange" => query.Where(e =>
                {
                    var rangeFrom = model.CustomStartDate ?? DateTime.MinValue;
                    var rangeTo = model.CustomEndDate ?? DateTime.MaxValue;
                    return e.CreatedDate.Date >= rangeFrom && e.CreatedDate.Date <= rangeTo;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(e => e.CreatedDate).ToList();
            return model;
        }

        private static QSBudgetViewModel BuildBudgetModel(List<QS_Budget> budgets, string? search,
            string? status, string? dateRange, string? from, string? to)
        {
            var model = new QSBudgetViewModel
            {
                TotalBudget = budgets.Sum(b => b.TotalBudget),
                TotalSpent = budgets.Sum(b => b.TotalSpent),
                Search = search ?? "",
                SelectedStatus = status ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to)
            };
            model.Remaining = model.TotalBudget - model.TotalSpent;

            var query = budgets.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(b => b.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(b =>
                    (b.Project ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (b.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var today = DateTime.Today;
            query = model.SelectedDateRange switch
            {
                "today" => query.Where(b => b.CreatedAt.Date == today),
                "thisweek" => query.Where(b =>
                {
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    return b.CreatedAt.Date >= startOfWeek && b.CreatedAt.Date < startOfWeek.AddDays(7);
                }),
                "thismonth" => query.Where(b =>
                    b.CreatedAt.Year == today.Year && b.CreatedAt.Month == today.Month),
                "thisyear" => query.Where(b => b.CreatedAt.Year == today.Year),
                "customrange" => query.Where(b =>
                {
                    var rangeFrom = model.CustomStartDate ?? DateTime.MinValue;
                    var rangeTo = model.CustomEndDate ?? DateTime.MaxValue;
                    return b.CreatedAt.Date >= rangeFrom && b.CreatedAt.Date <= rangeTo;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(b => b.CreatedAt).ToList();
            return model;
        }

        private static QSReportsViewModel BuildReportsModel(List<CostEstimate> estimates, List<QS_Budget> budgets,
            string? section, string? status, string? search, string? dateRange, string? from, string? to)
        {
            var model = new QSReportsViewModel
            {
                ReportSection = string.IsNullOrEmpty(section) ? "All" : section,
                StatusFilter = string.IsNullOrEmpty(status) ? "All" : status,
                Search = search ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to),
                EstimateCount = estimates.Count,
                ApprovedCount = estimates.Count(e => e.Status == "Approved") + budgets.Count(b => b.Status == "Approved"),
                PendingCount = estimates.Count(e => e.Status == "Pending Approval") + budgets.Count(b => b.Status == "Pending Approval"),
                TotalCost = estimates.Sum(e => e.TotalCost) + budgets.Sum(b => b.TotalBudget)
            };

            bool DateInRange(DateTime date)
            {
                var today = DateTime.Today;
                return model.SelectedDateRange switch
                {
                    "today" => date.Date == today,
                    "thisweek" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                    "thismonth" => date.Year == today.Year && date.Month == today.Month,
                    "thisyear" => date.Year == today.Year,
                    "customrange" => (!model.CustomStartDate.HasValue || date >= model.CustomStartDate.Value) &&
                                     (!model.CustomEndDate.HasValue || date <= model.CustomEndDate.Value.AddDays(1)),
                    _ => true
                };
            }

            model.EstimateItems = estimates.Where(e =>
                (model.StatusFilter == "All" || e.Status == model.StatusFilter) &&
                (string.IsNullOrEmpty(model.Search) || e.ProjectName.Contains(model.Search, StringComparison.OrdinalIgnoreCase)) &&
                DateInRange(e.CreatedDate)
            ).ToList();

            model.BudgetItems = budgets.Where(b =>
                (model.StatusFilter == "All" || b.Status == model.StatusFilter) &&
                (string.IsNullOrEmpty(model.Search) || b.Project.Contains(model.Search, StringComparison.OrdinalIgnoreCase)) &&
                DateInRange(b.CreatedAt)
            ).ToList();

            model.CurrentSectionTitle = model.ReportSection switch
            {
                "Estimates" => "Cost Estimates",
                "Budgets" => "Project Budgets",
                _ => "All Reports"
            };

            model.CurrentItemCount = model.ReportSection switch
            {
                "Estimates" => model.EstimateItems.Count,
                "Budgets" => model.BudgetItems.Count,
                _ => model.EstimateItems.Count + model.BudgetItems.Count
            };

            return model;
        }

        private static QSTasksViewModel BuildTasksModel(List<PM_Tasks> tasks, string? search,
            string? status, string? dateRange, string? from, string? to)
        {
            var model = new QSTasksViewModel
            {
                TotalCount = tasks.Count,
                CompletedCount = tasks.Count(t => t.Status == "Completed"),
                InProgressCount = tasks.Count(t => t.Status == "In Progress"),
                Search = search ?? "",
                SelectedStatus = status ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to)
            };

            var query = tasks.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(t => t.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(t =>
                    (t.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.Project ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var today = DateTime.Today;
            query = model.SelectedDateRange switch
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
                    var rangeFrom = model.CustomStartDate ?? DateTime.MinValue;
                    var rangeTo = model.CustomEndDate ?? DateTime.MaxValue;
                    return t.StartDate.Value.Date >= rangeFrom && t.StartDate.Value.Date <= rangeTo;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(t => t.CreatedAt).ToList();
            return model;
        }

        private static List<string> ParseJsonList(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                try
                {
                    var s = System.Text.Json.JsonSerializer.Deserialize<string>(json);
                    return s != null ? new List<string> { s } : new List<string>();
                }
                catch
                {
                    return new List<string>();
                }
            }
        }

        private static string GetEstimateStatusClass(string? s) => s switch
        {
            "Approved" => "badge-green",
            "Rejected" => "badge-red",
            "Waiting for PM Approval" => "badge-blue",
            "Pending Approval" => "badge-yellow",
            _ => "badge-yellow"
        };

        private static string GetBudgetHealthClass(string? h) => h switch
        {
            "On Track" => "badge-green",
            "Warning" => "badge-yellow",
            "Critical" => "badge-orange",
            "Over Budget" => "badge-red",
            _ => "badge-blue"
        };

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

        private static DateTime StartOfWeek(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }

        private static string FormatCost(decimal value) => value == 0m ? "" : value.ToString("N2");

        private static decimal ParseCostInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0m;
            var cleaned = new string(input.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                return result;
            return 0m;
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                return result;
            if (DateTime.TryParse(value, out result))
                return result;
            return null;
        }
    }
}
