using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    public class ProjectManagerController : AppControllerBase
    {
        private readonly PM_ProjectService _projectService;
        private readonly PM_TaskService _taskService;
        private readonly QS_BudgetService _budgetService;
        private readonly QS_CostEstimateService _estimateService;
        private readonly FO_IncomeTransactionService _incomeService;
        private readonly FO_ExpenseService _expenseService;
        private readonly SiteIssueService _issueService;
        private readonly SiteRequestService _requestService;
        private readonly DailyProgressService _progressService;
        private readonly ArchitectDesignService _designService;
        private readonly ArchitectRevisionService _revisionService;
        private readonly ProjectService _legacyProjectService;
        private readonly PdfExportService _pdfExport;

        public ProjectManagerController(AuthService auth, SubscriptionService subscription, AuditService audit,
            PM_ProjectService projectService, PM_TaskService taskService, QS_BudgetService budgetService,
            QS_CostEstimateService estimateService, FO_IncomeTransactionService incomeService,
            FO_ExpenseService expenseService, SiteIssueService issueService, SiteRequestService requestService,
            DailyProgressService progressService, ArchitectDesignService designService,
            ArchitectRevisionService revisionService, ProjectService legacyProjectService, PdfExportService pdfExport)
            : base(auth, subscription, audit)
        {
            _projectService = projectService;
            _taskService = taskService;
            _budgetService = budgetService;
            _estimateService = estimateService;
            _incomeService = incomeService;
            _expenseService = expenseService;
            _issueService = issueService;
            _requestService = requestService;
            _progressService = progressService;
            _designService = designService;
            _revisionService = revisionService;
            _legacyProjectService = legacyProjectService;
            _pdfExport = pdfExport;
        }

        protected override string? RequiredRole => "ProjectManager";

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var projectList = await LoadProjectsAsync();
            var progressList = await LoadProgressAsync();

            // Derive project status from latest progress updates
            foreach (var project in projectList)
            {
                var projectProgress = progressList
                    .Where(p => p.Project.Equals(project.ProjectName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                project.Status = DeriveProjectStatusFromProgress(projectProgress, project.Status ?? "Planning");
            }

            var taskList = await LoadTasksAsync();
            var budgetList = await LoadBudgetsAsync();
            var estimateList = await LoadEstimatesAsync();
            var incomeList = await LoadIncomeAsync();
            var expenseList = await LoadExpensesAsync();
            var issueList = await LoadIssuesAsync();

            var model = new PMIndexViewModel
            {
                ProjectCount = projectList.Count,
                OnTrackCount = budgetList.Count(b => b.Health == "On Track"),
                OpenIssueCount = issueList.Count(i => i.Status == "Open" || i.Status == "In Progress"),
                PendingApprovalCount = estimateList.Count(e => e.PMApprovalStatus == "Pending" && e.Status == "Waiting for PM Approval")
                    + budgetList.Count(b => b.Status == "Waiting for PM Approval"),
                TasksDueToday = taskList.Count(t => t.DueDate.HasValue && t.DueDate.Value.Date == DateTime.Today && t.Status != "Completed"),
                MyTaskCount = taskList.Count(t => t.Status != "Completed"),
                TotalIncome = incomeList.Sum(i => i.Amount),
                TotalExpenses = expenseList.Sum(e => e.Amount),
                EstimateCount = estimateList.Count,
                ApprovedBudgetCount = budgetList.Count(b => b.Status == "Approved"),
                ReportCount = 5,
                ArchivedCount = projectList.Count(p => p.IsArchived),
                TaskCount = taskList.Count,
                PendingTaskCount = taskList.Count(t => t.Status == "Pending"),
                InProgressTaskCount = taskList.Count(t => t.Status == "In Progress"),
                CompletedTaskCount = taskList.Count(t => t.Status == "Completed")
            };
            model.NetBalance = model.TotalIncome - model.TotalExpenses;

            model.ProjectStatuses = new List<PMStatusCount>
            {
                new() { Label = "Planning", Count = projectList.Count(p => p.Status == "Planning") },
                new() { Label = "In Progress", Count = projectList.Count(p => p.Status == "In Progress") },
                new() { Label = "Completed", Count = projectList.Count(p => p.Status == "Completed") },
                new() { Label = "On Hold", Count = projectList.Count(p => p.Status == "On Hold") },
            };

            var healthTotal = budgetList.Count > 0 ? budgetList.Count : 1;
            model.BudgetHealthItems = new List<PMHealthCount>
            {
                new() { Label = "On Track", Count = budgetList.Count(b => b.Health == "On Track"), Total = healthTotal },
                new() { Label = "Warning", Count = budgetList.Count(b => b.Health == "Warning"), Total = healthTotal },
                new() { Label = "Critical", Count = budgetList.Count(b => b.Health == "Critical"), Total = healthTotal },
                new() { Label = "Over Budget", Count = budgetList.Count(b => b.Health == "Over Budget"), Total = healthTotal },
            };

            var items = new List<PMActivityItem>();
            foreach (var e in estimateList.OrderByDescending(x => x.CreatedDate).Take(3))
                items.Add(new PMActivityItem { Type = "Estimate", Title = $"EST-{e.EstimateId:D3} - {e.ProjectName}", Subtitle = $"{e.TotalCost:N2} | {e.Status}", Date = e.CreatedDate.ToString("MMM d, yyyy") });
            foreach (var b in budgetList.OrderByDescending(x => x.Id).Take(3))
                items.Add(new PMActivityItem { Type = "Budget", Title = $"{b.Project}", Subtitle = $"Budget: {b.TotalBudget:N2} | {b.Status}", Date = DateTime.Now.ToString("MMM d, yyyy") });
            foreach (var i in issueList.OrderByDescending(x => x.DateReported).Take(3))
                items.Add(new PMActivityItem { Type = "Issue", Title = $"ISS-{i.IssueId:D3} - {i.IssueType}", Subtitle = $"{i.Project} | {i.Severity}", Date = i.DateReported.ToString("MMM d, yyyy") });
            foreach (var inc in incomeList.OrderByDescending(x => x.TransactionDate).Take(3))
                items.Add(new PMActivityItem { Type = "Income", Title = $"INC-{inc.Id:D3} - {inc.Project}", Subtitle = $"{inc.Amount:N2} | {inc.Status}", Date = inc.TransactionDate.ToString("MMM d, yyyy") });
            foreach (var ex in expenseList.OrderByDescending(x => x.ExpenseDate).Take(3))
                items.Add(new PMActivityItem { Type = "Expense", Title = $"EXP-{ex.Id:D3} - {ex.Project}", Subtitle = $"{ex.Amount:N2} | {ex.Status}", Date = ex.ExpenseDate.ToString("MMM d, yyyy") });
            model.RecentItems = items.OrderByDescending(i => i.Date).Take(10).ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Planning(string? search, string? status, string? dateRange,
            string? from, string? to, int? view, int? edit, int? archive, int? add)
        {
            var projects = await LoadProjectsAsync();
            var progressUpdates = await LoadProgressAsync();

            // Derive project status from latest progress updates
            foreach (var project in projects)
            {
                var projectProgress = progressUpdates
                    .Where(p => p.Project.Equals(project.ProjectName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                project.Status = DeriveProjectStatusFromProgress(projectProgress, project.Status ?? "Planning");
            }

            var model = BuildPlanningModel(projects, search, status, dateRange, from, to);

            if (add == 1)
            {
                model.Modal = "new";
                var manilaNow = ManilaNow().ToString("yyyy-MM-ddTHH:mm");
                model.NewStartDate = manilaNow;
                model.NewDueDate = manilaNow;
            }

            if (view.HasValue)
            {
                model.SelectedProject = projects.FirstOrDefault(p => p.Id == view.Value);
                if (model.SelectedProject != null)
                    model.Modal = "view";
            }

            if (edit.HasValue)
            {
                var target = projects.FirstOrDefault(p => p.Id == edit.Value);
                if (target != null)
                {
                    model.Modal = "edit";
                    model.EditId = target.Id;
                    model.EditProjectName = target.ProjectName ?? "";
                    model.EditLocation = target.Location ?? "";
                    model.EditStartDate = target.StartDate?.ToString("yyyy-MM-ddTHH:mm") ?? "";
                    model.EditDueDate = target.DueDate?.ToString("yyyy-MM-ddTHH:mm") ?? "";
                    model.EditStatus = target.Status;
                    model.EditDescription = target.Description ?? "";
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
        public async Task<IActionResult> CreateProject(string projectName, string? location, string? startDate,
            string? dueDate, string? status, string? description, string? search, string? filterStatus,
            string? dateRange, string? from, string? to)
        {
            var projects = await LoadProjectsAsync();
            var model = BuildPlanningModel(projects, search ?? "", filterStatus, dateRange, from, to);
            model.Modal = "new";
            model.NewProjectName = projectName ?? "";
            model.NewLocation = location ?? "";
            model.NewStartDate = startDate ?? "";
            model.NewDueDate = dueDate ?? "";
            model.NewStatus = string.IsNullOrEmpty(status) ? "Planning" : status;
            model.NewDescription = description ?? "";

            if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(location))
            {
                model.NewError = "Project Name and Location are required.";
            }
            else if (projects.Any(p => p.ProjectName != null && p.ProjectName.Equals(projectName.Trim(), StringComparison.OrdinalIgnoreCase)
                && (p.Location ?? "").Equals((location ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                model.NewError = "A project with this name and location already exists.";
            }
            else
            {
                try
                {
                    var (success, msg) = await _projectService.CreateProjectWithLimitCheckAsync(new PM_Projects
                    {
                        ProjectName = projectName,
                        Location = location,
                        Status = model.NewStatus,
                        StartDate = ParseDate(startDate),
                        DueDate = ParseDate(dueDate),
                        Description = description
                    });
                    if (success)
                        return RedirectToAction("Planning");
                    model.NewError = msg;
                }
                catch
                {
                    model.NewError = "An unexpected error occurred while creating the project.";
                }
            }

            return View("Planning", model);
        }

        [HttpPost]
        public async Task<IActionResult> SaveProject(int editId, string editProjectName, string? editLocation,
            string? editStartDate, string? editDueDate, string? editStatus, string? editDescription,
            string? search, string? filterStatus, string? dateRange, string? from, string? to)
        {
            var projects = await LoadProjectsAsync();
            var model = BuildPlanningModel(projects, search ?? "", filterStatus, dateRange, from, to);
            model.Modal = "edit";
            model.EditId = editId;
            model.EditProjectName = editProjectName ?? "";
            model.EditLocation = editLocation ?? "";
            model.EditStartDate = editStartDate ?? "";
            model.EditDueDate = editDueDate ?? "";
            model.EditStatus = string.IsNullOrEmpty(editStatus) ? "Planning" : editStatus;
            model.EditDescription = editDescription ?? "";

            if (string.IsNullOrWhiteSpace(editProjectName) || string.IsNullOrWhiteSpace(editLocation))
            {
                model.EditError = "Project Name and Location are required.";
            }
            else if (projects.Any(p => p.ProjectName != null && p.ProjectName.Equals(editProjectName.Trim(), StringComparison.OrdinalIgnoreCase)
                && p.Id != editId))
            {
                model.EditError = "Another project with this name already exists.";
            }
            else
            {
                var success = await _projectService.UpdateProjectAsync(new PM_Projects
                {
                    Id = editId,
                    ProjectName = editProjectName,
                    Location = editLocation,
                    Status = model.EditStatus,
                    StartDate = ParseDate(editStartDate),
                    DueDate = ParseDate(editDueDate),
                    Description = editDescription
                });
                if (success)
                    return RedirectToAction("Planning");
                model.EditError = "Unable to save changes. Please try again.";
            }

            return View("Planning", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveProject(int id)
        {
            var success = await _projectService.ArchiveProjectAsync(id);
            if (success)
                return RedirectToAction("Archived");
            return RedirectToAction("Planning");
        }

        [HttpGet("projectmanager/planning/new")]
        public IActionResult NewProject()
        {
            var manilaNow = ManilaNow().ToString("yyyy-MM-ddTHH:mm");
            return View(new PMNewProjectViewModel { StartDate = manilaNow, DueDate = manilaNow });
        }

        [HttpPost("projectmanager/planning/new")]
        public async Task<IActionResult> NewProject(string name, string? location, string? startDate,
            string? dueDate, string? status, string? description)
        {
            var model = new PMNewProjectViewModel
            {
                Name = name ?? "",
                Location = location ?? "",
                StartDate = startDate ?? "",
                DueDate = dueDate ?? "",
                Status = string.IsNullOrEmpty(status) ? "Planning" : status,
                Description = description ?? ""
            };

            if (string.IsNullOrWhiteSpace(name))
            {
                model.ErrorMessage = "Project Name is required.";
                return View(model);
            }
            if (string.IsNullOrWhiteSpace(location))
            {
                model.ErrorMessage = "Location is required.";
                return View(model);
            }

            var (canAdd, limitMsg) = Subscription.CanAddProject();
            if (!canAdd)
            {
                model.ErrorMessage = limitMsg;
                return View(model);
            }

            var (success, message, _, _) = await _legacyProjectService.CreateProjectAsync(
                name, description, location, model.Status, ParseDate(startDate), ParseDate(dueDate));

            if (success)
            {
                Subscription.IncrementProjectCount();
                return RedirectToAction("Planning");
            }

            model.ErrorMessage = "Failed to save project: " + message;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Tasks(string? search, string? status, string? dateRange,
            string? from, string? to, int? view, int? edit, int? archive, int? add)
        {
            var tasks = await LoadTasksAsync();
            var model = BuildTasksModel(tasks, search, status, dateRange, from, to);

            if (add == 1)
                model.Modal = "new";

            if (view.HasValue)
            {
                model.SelectedTask = tasks.FirstOrDefault(t => t.Id == view.Value);
                if (model.SelectedTask != null)
                    model.Modal = "view";
            }

            if (edit.HasValue)
            {
                var target = tasks.FirstOrDefault(t => t.Id == edit.Value);
                if (target != null && target.Status != "Completed")
                {
                    model.Modal = "edit";
                    model.EditId = target.Id;
                    model.EditTaskName = target.Name;
                    model.EditLocation = target.Location ?? "";
                    model.EditProject = target.Project;
                    model.EditAssignedTo = target.AssignedTo ?? "";
                    model.EditPriority = target.Priority ?? "Medium";
                    model.EditStartDate = target.StartDate?.ToString("yyyy-MM-ddTHH:mm") ?? "";
                    model.EditDueDate = target.DueDate?.ToString("yyyy-MM-ddTHH:mm") ?? "";
                    model.EditDescription = target.Description ?? "";
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
        public async Task<IActionResult> CreateTask(string taskName, string? taskLocation, string? project,
            string? assignedTo, string? priority, string? startDate, string? dueDate, string? description,
            string? search, string? filterStatus, string? dateRange, string? from, string? to)
        {
            var tasks = await LoadTasksAsync();
            var model = BuildTasksModel(tasks, search ?? "", filterStatus, dateRange, from, to);
            model.Modal = "new";
            model.NewTaskName = taskName ?? "";
            model.NewLocation = taskLocation ?? "";
            model.NewProject = project ?? "";
            model.NewAssignedTo = assignedTo ?? "";
            model.NewPriority = string.IsNullOrEmpty(priority) ? "Medium" : priority;
            model.NewStartDate = startDate ?? "";
            model.NewDueDate = dueDate ?? "";
            model.NewDescription = description ?? "";

            if (string.IsNullOrWhiteSpace(taskName) || string.IsNullOrWhiteSpace(project))
            {
                model.NewError = "Task Name and Project are required.";
            }
            else
            {
                try
                {
                    var success = await _taskService.CreateTaskAsync(new PM_Tasks
                    {
                        Name = taskName,
                        Location = taskLocation,
                        Project = project,
                        AssignedTo = assignedTo,
                        Priority = model.NewPriority,
                        Status = "Pending",
                        StartDate = ParseDate(startDate),
                        DueDate = ParseDate(dueDate),
                        Description = description
                    });
                    if (success)
                        return RedirectToAction("Tasks");
                    model.NewError = "Unable to create task. Please try again.";
                }
                catch
                {
                    model.NewError = "An unexpected error occurred.";
                }
            }

            return View("Tasks", model);
        }

        [HttpPost]
        public async Task<IActionResult> SaveTask(int editId, string editTaskName, string? editTaskLocation,
            string? editProject, string? editAssignedTo, string? editPriority, string? editStartDate,
            string? editDueDate, string? editDescription,
            string? search, string? filterStatus, string? dateRange, string? from, string? to)
        {
            var tasks = await LoadTasksAsync();
            var model = BuildTasksModel(tasks, search ?? "", filterStatus, dateRange, from, to);
            model.Modal = "edit";
            model.EditId = editId;
            model.EditTaskName = editTaskName ?? "";
            model.EditLocation = editTaskLocation ?? "";
            model.EditProject = editProject ?? "";
            model.EditAssignedTo = editAssignedTo ?? "";
            model.EditPriority = string.IsNullOrEmpty(editPriority) ? "Medium" : editPriority;
            model.EditStartDate = editStartDate ?? "";
            model.EditDueDate = editDueDate ?? "";
            model.EditDescription = editDescription ?? "";

            var original = tasks.FirstOrDefault(t => t.Id == editId);
            if (original?.Status == "Completed")
                return RedirectToAction("Tasks");

            if (string.IsNullOrWhiteSpace(editTaskName) || string.IsNullOrWhiteSpace(editProject))
            {
                model.EditError = "Task Name and Project are required.";
            }
            else
            {
                var success = await _taskService.UpdateTaskAsync(new PM_Tasks
                {
                    Id = editId,
                    Name = editTaskName,
                    Location = editTaskLocation,
                    Project = editProject,
                    AssignedTo = editAssignedTo,
                    Priority = model.EditPriority,
                    Status = original.Status,
                    ProgressPercentage = original.ProgressPercentage,
                    StartDate = ParseDate(editStartDate),
                    DueDate = ParseDate(editDueDate),
                    Description = editDescription
                });
                if (success)
                    return RedirectToAction("Tasks");
                model.EditError = "Unable to save changes. Please try again.";
            }

            return View("Tasks", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveTask(int id)
        {
            var success = await _taskService.ArchiveTaskAsync(id);
            if (success)
                return RedirectToAction("Archived");
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

        [HttpGet("projectmanager/tasks/new")]
        public IActionResult AssignTask()
        {
            return View(new PMAssignTaskViewModel());
        }

        [HttpPost("projectmanager/tasks/new")]
        public async Task<IActionResult> AssignTask(string taskName, string? taskLocation, string? project,
            string? assignedTo, string? priority, string? startDate, string? dueDate, string? description)
        {
            var model = new PMAssignTaskViewModel
            {
                Name = taskName ?? "",
                Location = taskLocation ?? "",
                Project = project ?? "",
                AssignedTo = assignedTo ?? "",
                Priority = string.IsNullOrEmpty(priority) ? "Medium" : priority,
                StartDate = startDate ?? "",
                DueDate = dueDate ?? "",
                Description = description ?? ""
            };

            if (string.IsNullOrWhiteSpace(taskName) || string.IsNullOrWhiteSpace(project))
            {
                model.ErrorMessage = "Task Name and Project are required.";
                return View(model);
            }

            try
            {
                var success = await _taskService.CreateTaskAsync(new PM_Tasks
                {
                    Name = taskName,
                    Location = taskLocation,
                    Project = project,
                    AssignedTo = assignedTo,
                    Priority = model.Priority,
                    StartDate = ParseDate(startDate),
                    DueDate = ParseDate(dueDate),
                    Status = "Pending",
                    Description = description
                });
                if (success)
                    return RedirectToAction("Tasks");
                model.ErrorMessage = "Unable to create task. Please try again.";
            }
            catch
            {
                model.ErrorMessage = "Unable to create task. Please try again.";
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Archived(string? type, string? status, string? search,
            int? view, int? restore, int? delete, string? actionType)
        {
            List<PM_Projects> archivedProjects;
            List<PM_Tasks> archivedTasks;
            try { archivedProjects = await _projectService.GetArchivedProjectsAsync(); } catch { archivedProjects = new(); }
            try { archivedTasks = await _taskService.GetArchivedTasksAsync(); } catch { archivedTasks = new(); }

            var model = BuildArchivedModel(archivedProjects, archivedTasks, type, status, search);

            if (view.HasValue)
            {
                var item = model.Items.FirstOrDefault(i => i.RawId == view.Value);
                if (item != null)
                {
                    model.Modal = "view";
                    if (item.Type == "Project")
                        model.SelectedProject = archivedProjects.FirstOrDefault(p => p.Id == view.Value);
                    else
                        model.SelectedTask = archivedTasks.FirstOrDefault(t => t.Id == view.Value);
                }
            }

            if (restore.HasValue)
            {
                model.Modal = "restore";
                model.ActionTargetId = restore.Value;
                model.ActionTargetType = actionType ?? "";
            }

            if (delete.HasValue)
            {
                model.Modal = "delete";
                model.ActionTargetId = delete.Value;
                model.ActionTargetType = actionType ?? "";
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> RestoreItem(int id, string type)
        {
            try
            {
                if (type == "Project")
                    await _projectService.RestoreProjectAsync(id);
                else
                    await _taskService.RestoreTaskAsync(id);
            }
            catch { }
            return RedirectToAction("Archived");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteItem(int id, string type)
        {
            try
            {
                if (type == "Project")
                    await _projectService.DeleteProjectAsync(id);
                else
                    await _taskService.DeleteTaskAsync(id);
            }
            catch { }
            return RedirectToAction("Archived");
        }

        [HttpGet]
        public async Task<IActionResult> Monitoring(string? status, string? date, string? search,
            int? view, int? archive)
        {
            var progressUpdates = await LoadProgressAsync();
            var projects = await LoadProjectsAsync();

            var model = BuildMonitoringModel(progressUpdates, projects, status, date, search);

            if (view.HasValue)
            {
                model.SelectedItem = progressUpdates.FirstOrDefault(p => p.Id == view.Value);
                if (model.SelectedItem != null)
                    model.Modal = "view";
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ProgressPhoto(int id)
        {
            var photo = await _progressService.GetByIdAsync(id);
            if (photo?.PhotoData == null || photo.PhotoData.Length == 0) return NotFound();

            Response.Headers.CacheControl = "private, max-age=86400";
            return File(photo.PhotoData, string.IsNullOrWhiteSpace(photo.PhotoContentType) ? "image/png" : photo.PhotoContentType);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveProgress(int id)
        {
            try
            {
                var result = await _progressService.ArchiveAsync(id);
                if (result) return RedirectToAction("Monitoring");
            }
            catch { }
            return RedirectToAction("Monitoring");
        }

        [HttpGet]
        public async Task<IActionResult> Budget(string? status, string? search,
            int? view, int? reject, int? archive)
        {
            var budgets = await LoadBudgetsAsync();

            var model = BuildBudgetModel(budgets, status, search);

            if (view.HasValue)
            {
                model.SelectedBudget = budgets.FirstOrDefault(b => b.Id == view.Value);
                if (model.SelectedBudget != null)
                    model.Modal = "view";
            }

            if (reject.HasValue)
            {
                model.Modal = "reject";
                model.RejectId = reject.Value;
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveBudget(int id)
        {
            var success = await _budgetService.ApprovePMAsync(id, Auth.CurrentDisplayName);
            if (success)
            {
                var budget = await _budgetService.GetByIdAsync(id);
                if (budget != null)
                    await _taskService.CompleteTaskByReferenceAsync(budget.Project, "QuantitySurveyor", "budget");
            }
            return RedirectToAction("Budget");
        }

        [HttpPost]
        public async Task<IActionResult> RejectBudget(int id, string? reason, string? status, string? search)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                var budgets = await LoadBudgetsAsync();
                var model = BuildBudgetModel(budgets, status, search);
                model.Modal = "reject";
                model.RejectId = id;
                model.RejectError = "Reason for rejection is required.";
                return View("Budget", model);
            }
            await _budgetService.RejectPMAsync(id, reason, Auth.CurrentDisplayName);
            return RedirectToAction("Budget");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveBudget(int id)
        {
            var success = await _budgetService.ArchiveAsync(id);
            if (success) return RedirectToAction("Budget");
            return RedirectToAction("Budget");
        }

        [HttpGet]
        public async Task<IActionResult> Expenses(string? status, string? search, int? view, int? reject)
        {
            var expenses = await LoadExpensesAsync();
            var model = BuildExpenseModel(expenses, status, search);

            if (view.HasValue)
            {
                model.SelectedExpense = expenses.FirstOrDefault(e => e.Id == view.Value);
                if (model.SelectedExpense != null)
                    model.Modal = "view";
            }

            if (reject.HasValue)
            {
                model.Modal = "reject";
                model.RejectId = reject.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveExpense(int id)
        {
            await _expenseService.ApprovePMAsync(id, Auth.CurrentDisplayName);
            return RedirectToAction("Expenses");
        }

        [HttpPost]
        public async Task<IActionResult> RejectExpense(int id, string? reason, string? status, string? search)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                var expenses = await LoadExpensesAsync();
                var model = BuildExpenseModel(expenses, status, search);
                model.Modal = "reject";
                model.RejectId = id;
                model.RejectError = "Reason for rejection is required.";
                return View("Expenses", model);
            }
            await _expenseService.RejectPMAsync(id, reason, Auth.CurrentDisplayName);
            return RedirectToAction("Expenses");
        }

        private static PMExpenseViewModel BuildExpenseModel(List<Expense> expenses, string? status, string? search)
        {
            var model = new PMExpenseViewModel
            {
                ApprovedCount = expenses.Count(e => e.Status == "Approved"),
                PendingCount = expenses.Count(e => e.Status == "Pending"),
                RejectedCount = expenses.Count(e => e.Status == "Rejected"),
                TotalAmount = expenses.Sum(e => e.Amount),
                SelectedStatus = status ?? "",
                Search = search ?? ""
            };

            var query = expenses.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(e => e.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(e => (e.Project ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || (e.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            model.Items = query.OrderByDescending(e => e.CreatedAt).ToList();
            return model;
        }

        [HttpGet]
        public async Task<IActionResult> CostEstimates(string? status, string? search,
            int? view, int? reject, int? archive)
        {
            var estimates = await LoadEstimatesAsync();

            var model = BuildEstimateModel(estimates, status, search);

            if (view.HasValue)
            {
                model.SelectedEstimate = estimates.FirstOrDefault(e => e.EstimateId == view.Value);
                if (model.SelectedEstimate != null)
                    model.Modal = "view";
            }

            if (reject.HasValue)
            {
                model.Modal = "reject";
                model.RejectId = reject.Value;
            }

            if (archive.HasValue)
            {
                model.Modal = "archive";
                model.ArchiveId = archive.Value;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveEstimate(int id)
        {
            var success = await _estimateService.ApprovePMAsync(id, Auth.CurrentDisplayName);
            if (success)
            {
                var estimate = await _estimateService.GetByIdAsync(id);
                if (estimate != null)
                    await _taskService.CompleteTaskByReferenceAsync(estimate.ProjectName, "QuantitySurveyor", "estimat");
            }
            return RedirectToAction("CostEstimates");
        }

        [HttpPost]
        public async Task<IActionResult> RejectEstimate(int id, string? reason, string? status, string? search)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                var estimates = await LoadEstimatesAsync();
                var model = BuildEstimateModel(estimates, status, search);
                model.Modal = "reject";
                model.RejectId = id;
                model.RejectError = "Reason for rejection is required.";
                return View("CostEstimates", model);
            }
            await _estimateService.RejectPMAsync(id, reason);
            return RedirectToAction("CostEstimates");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveEstimate(int id)
        {
            var success = await _estimateService.ArchiveAsync(id);
            if (success) return RedirectToAction("CostEstimates");
            return RedirectToAction("CostEstimates");
        }

        [HttpGet]
        public async Task<IActionResult> Income(string? status, string? dateRange, string? search, int? view)
        {
            var incomes = await LoadIncomeAsync();

            var model = BuildIncomeModel(incomes, status, dateRange, search);

            if (view.HasValue)
            {
                model.SelectedItem = incomes.FirstOrDefault(i => i.Id == view.Value);
                if (model.SelectedItem != null)
                    model.Modal = "view";
            }

            return View(model);
        }

        private static PMIncomeViewModel BuildIncomeModel(List<PM_IncomeTransactions> incomes,
            string? status, string? dateRange, string? search)
        {
            var today = DateTime.Today;
            var model = new PMIncomeViewModel
            {
                TotalAmount = incomes.Sum(i => i.Amount),
                TransactionCount = incomes.Count,
                ThisMonthCount = incomes.Count(i => i.TransactionDate.Year == today.Year && i.TransactionDate.Month == today.Month),
                PendingCount = incomes.Count(i => i.Status == "Pending"),
                SelectedStatus = status ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                Search = search ?? ""
            };

            var query = incomes.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(i => i.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(i =>
                    i.Project.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (i.Category ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            query = model.SelectedDateRange switch
            {
                "today" => query.Where(i => i.TransactionDate.Date == today),
                "thisweek" => query.Where(i =>
                {
                    var start = StartOfWeek(today);
                    return i.TransactionDate.Date >= start && i.TransactionDate.Date < start.AddDays(7);
                }),
                "thismonth" => query.Where(i => i.TransactionDate.Year == today.Year && i.TransactionDate.Month == today.Month),
                "thisyear" => query.Where(i => i.TransactionDate.Year == today.Year),
                _ => query
            };

            model.Items = query.OrderByDescending(i => i.TransactionDate).ToList();
            return model;
        }

        private static DateTime StartOfWeek(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }

        private static PMEstimateViewModel BuildEstimateModel(List<CostEstimate> estimates,
            string? status, string? search)
        {
            var model = new PMEstimateViewModel
            {
                ApprovedCount = estimates.Count(e => e.Status == "Approved"),
                PendingCount = estimates.Count(e => e.Status == "Waiting for PM Approval"),
                RejectedCount = estimates.Count(e => e.Status == "Rejected"),
                TotalCost = estimates.Sum(e => e.TotalCost),
                SelectedStatus = status ?? "",
                Search = search ?? ""
            };

            var query = estimates.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(e => e.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(e => (e.ProjectName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            model.Items = query.OrderByDescending(e => e.CreatedDate).ToList();
            return model;
        }

        private static PMBudgetViewModel BuildBudgetModel(List<QS_Budget> budgets,
            string? status, string? search)
        {
            var model = new PMBudgetViewModel
            {
                ApprovedCount = budgets.Count(b => b.Status == "Approved"),
                PendingCount = budgets.Count(b => b.Status == "Waiting for PM Approval"),
                RejectedCount = budgets.Count(b => b.Status == "Rejected"),
                TotalBudget = budgets.Sum(b => b.TotalBudget),
                SelectedStatus = status ?? "",
                Search = search ?? ""
            };

            var query = budgets.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(b => b.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(b => (b.Project ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            model.Items = query.OrderByDescending(b => b.CreatedAt).ToList();
            return model;
        }

        private static PMMonitoringViewModel BuildMonitoringModel(List<ProgressReport> updates,
            List<PM_Projects> projects, string? status, string? date, string? search)
        {
            var model = new PMMonitoringViewModel
            {
                Projects = projects,
                TotalCount = updates.Count,
                CompletedCount = updates.Count(p => p.Status == "Completed"),
                NearCompletionCount = updates.Count(p => p.Status == "Near Completion"),
                PendingCount = updates.Count(p => p.Status == "Pending"),
                SelectedStatus = status ?? "",
                SelectedDate = string.IsNullOrEmpty(date) ? "All" : date,
                Search = search ?? ""
            };

            var query = updates.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(p => p.Status == model.SelectedStatus);

            if (model.SelectedDate != "All")
            {
                var today = DateTime.Today;
                query = model.SelectedDate switch
                {
                    "Today" => query.Where(p => p.UpdateDate.Date == today),
                    "This Week" => query.Where(p => p.UpdateDate.Date >= today && p.UpdateDate.Date <= today.AddDays(7)),
                    "This Month" => query.Where(p => p.UpdateDate.Month == today.Month && p.UpdateDate.Year == today.Year),
                    "This Year" => query.Where(p => p.UpdateDate.Year == today.Year),
                    _ => query
                };
            }

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(p =>
                    p.Project.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Location?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.SubmittedBy?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            model.Items = query.ToList();
            return model;
        }

        private static PMArchivedViewModel BuildArchivedModel(List<PM_Projects> projects,
            List<PM_Tasks> tasks, string? type, string? status, string? search)
        {
            var allItems = new List<PMArchivedItem>();

            foreach (var p in projects)
            {
                allItems.Add(new PMArchivedItem
                {
                    Type = "Project",
                    RawId = p.Id,
                    DisplayId = $"PRJ {p.Id:D3}",
                    Name = p.ProjectName ?? "",
                    Detail = p.Location ?? "",
                    Status = p.Status ?? "",
                    StatusClass = GetStatusClass(p.Status),
                    ArchivedOnDisplay = FormatDateTime(p.ArchivedOn),
                    SortDate = p.ArchivedOn
                });
            }

            foreach (var t in tasks)
            {
                allItems.Add(new PMArchivedItem
                {
                    Type = "Task",
                    RawId = t.Id,
                    DisplayId = $"TSK {t.Id:D3}",
                    Name = t.Name ?? "",
                    Detail = t.Project ?? "",
                    Status = t.Status ?? "",
                    StatusClass = GetStatusClass(t.Status),
                    ArchivedOnDisplay = FormatDateTime(t.ArchivedOn),
                    SortDate = t.ArchivedOn
                });
            }

            var model = new PMArchivedViewModel
            {
                TotalCount = allItems.Count,
                ProjectCount = projects.Count,
                TaskCount = tasks.Count,
                SelectedType = type ?? "",
                SelectedStatus = status ?? "",
                Search = search ?? ""
            };

            var query = allItems.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedType))
                query = query.Where(i => i.Type == model.SelectedType);

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(i => i.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(i =>
                    i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.Detail.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.DisplayId.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            model.Items = query.OrderByDescending(i => i.SortDate).ToList();
            return model;
        }

        private static PMTasksViewModel BuildTasksModel(List<PM_Tasks> tasks, string? search,
            string? status, string? dateRange, string? from, string? to)
        {
            var model = new PMTasksViewModel
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
                    (t.AssignedTo ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
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
                    var fromDate = model.CustomStartDate ?? DateTime.MinValue;
                    var toDate = model.CustomEndDate ?? DateTime.MaxValue;
                    return t.StartDate.Value.Date >= fromDate && t.StartDate.Value.Date <= toDate;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(t => t.CreatedAt).ToList();
            return model;
        }

        private static PMPlanningViewModel BuildPlanningModel(List<PM_Projects> projects, string? search,
            string? status, string? dateRange, string? from, string? to)
        {
            var model = new PMPlanningViewModel
            {
                TotalCount = projects.Count,
                OnTrackCount = projects.Count(p => p.Status == "On Track" || p.Status == "In Progress"),
                DelayedCount = projects.Count(p => p.Status == "Delayed"),
                Search = search ?? "",
                SelectedStatus = status ?? "",
                SelectedDateRange = string.IsNullOrEmpty(dateRange) ? "all" : dateRange,
                CustomStartDate = ParseDate(from),
                CustomEndDate = ParseDate(to)
            };

            var query = projects.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(p => p.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
            {
                var q = model.Search;
                query = query.Where(p =>
                    (p.ProjectName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (p.Location ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            var today = DateTime.Today;
            query = model.SelectedDateRange switch
            {
                "today" => query.Where(p => p.StartDate.HasValue && p.StartDate.Value.Date == today),
                "thisweek" => query.Where(p =>
                {
                    if (!p.StartDate.HasValue) return false;
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    var endOfWeek = startOfWeek.AddDays(7);
                    return p.StartDate.Value.Date >= startOfWeek && p.StartDate.Value.Date < endOfWeek;
                }),
                "thismonth" => query.Where(p => p.StartDate.HasValue &&
                    p.StartDate.Value.Year == today.Year && p.StartDate.Value.Month == today.Month),
                "thisyear" => query.Where(p => p.StartDate.HasValue && p.StartDate.Value.Year == today.Year),
                "customrange" => query.Where(p =>
                {
                    if (!p.StartDate.HasValue) return false;
                    var fromDate = model.CustomStartDate ?? DateTime.MinValue;
                    var toDate = model.CustomEndDate ?? DateTime.MaxValue;
                    return p.StartDate.Value.Date >= fromDate && p.StartDate.Value.Date <= toDate;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(p => p.CreatedAt).ToList();
            return model;
        }

        private static string DeriveProjectStatusFromProgress(List<ProgressReport> progressUpdates, string currentProjectStatus)
        {
            if (!progressUpdates.Any())
                return currentProjectStatus;

            var latestUpdate = progressUpdates.OrderByDescending(p => p.UpdateDate).FirstOrDefault();
            if (latestUpdate == null)
                return currentProjectStatus;

            // Map progress update status to project status
            return latestUpdate.Status switch
            {
                "Completed" => "Completed",
                "Near Completion" => "On Track",
                "In Progress" => "In Progress",
                "Pending" => currentProjectStatus == "Planning" ? "Planning" : "In Progress",
                _ => currentProjectStatus
            };
        }

        private static readonly TimeZoneInfo ManilaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

        private static DateTime ManilaNow()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaTimeZone);
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result))
                return result;
            if (DateTime.TryParse(value, out result))
                return result;
            return null;
        }

        private static string FormatDateTime(DateTime? date)
        {
            return date?.ToString("MMM dd, yyyy HH:mm") ?? "-";
        }

        private static string GetStatusClass(string? status)
        {
            return status switch
            {
                "In Progress" => "badge-blue",
                "On Track" => "badge-green",
                "Delayed" => "badge-red",
                "Completed" => "badge-teal",
                "Planning" => "badge-orange",
                _ => "badge-yellow"
            };
        }

        private async Task<List<PM_Projects>> LoadProjectsAsync()
        {
            try { return await _projectService.GetAllProjectsAsync(); }
            catch { return new List<PM_Projects>(); }
        }

        private async Task<List<PM_Tasks>> LoadTasksAsync()
        {
            try { return await _taskService.GetAllTasksAsync(); }
            catch { return new List<PM_Tasks>(); }
        }

        private async Task<List<QS_Budget>> LoadBudgetsAsync()
        {
            try { return await _budgetService.GetAllAsync(); }
            catch { return new List<QS_Budget>(); }
        }

        private async Task<List<CostEstimate>> LoadEstimatesAsync()
        {
            try { return await _estimateService.GetAllAsync(); }
            catch { return new List<CostEstimate>(); }
        }

        private async Task<List<PM_IncomeTransactions>> LoadIncomeAsync()
        {
            try { return await _incomeService.GetAllAsync(); }
            catch { return new List<PM_IncomeTransactions>(); }
        }

        private async Task<List<Expense>> LoadExpensesAsync()
        {
            try { return await _expenseService.GetAllAsync(); }
            catch { return new List<Expense>(); }
        }

        private async Task<List<SiteIssue>> LoadIssuesAsync()
        {
            try { return await _issueService.GetAllAsync(); }
            catch { return new List<SiteIssue>(); }
        }

        private async Task<List<SiteRequest>> LoadRequestsAsync()
        {
            try { return await _requestService.GetAllAsync(); }
            catch { return new List<SiteRequest>(); }
        }

        private async Task<List<ProgressReport>> LoadProgressAsync()
        {
            try { return await _progressService.GetAllAsync(); }
            catch { return new List<ProgressReport>(); }
        }

        [HttpGet]
        public async Task<IActionResult> Reports(string? section, string? status, string? dateRange, string? search,
            DateTime? startDate, DateTime? endDate)
        {
            var model = await BuildReportsModelAsync(section, status, dateRange, search, startDate, endDate);
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ExportPdf(string? section, string? status, string? dateRange, string? search,
            DateTime? startDate, DateTime? endDate)
        {
            var model = await BuildReportsModelAsync(section, status, dateRange, search, startDate, endDate);

            var prog = (model.ReportSection == "All" || model.ReportSection == "Progress") ? model.FilteredProgress : new List<ProgressReport>();
            var res = (model.ReportSection == "All" || model.ReportSection == "Resources") ? model.FilteredResources : new List<SiteRequest>();
            var iss = (model.ReportSection == "All" || model.ReportSection == "Issues") ? model.FilteredIssues : new List<SiteIssue>();
            var est = (model.ReportSection == "All" || model.ReportSection == "Estimates") ? model.FilteredEstimates : new List<CostEstimate>();
            var bud = (model.ReportSection == "All" || model.ReportSection == "Budget" || model.ReportSection == "BudgetVsActual") ? model.FilteredBudgets : new List<QS_Budget>();
            var inc = (model.ReportSection == "All" || model.ReportSection == "Income" || model.ReportSection == "Financial") ? model.FilteredIncomes : new List<PM_IncomeTransactions>();
            var exp = (model.ReportSection == "All" || model.ReportSection == "Expenses" || model.ReportSection == "Financial") ? model.FilteredExpenses : new List<Expense>();

            byte[] pdfData;
            if (model.ReportSection == "Progress" || model.ReportSection == "Resources" || model.ReportSection == "Issues" || model.ReportSection == "All")
            {
                pdfData = _pdfExport.GenerateSummaryReport("Project Manager", DateTime.Now, prog, res, iss);
            }
            else if (model.ReportSection == "Estimates" || model.ReportSection == "Budget")
            {
                pdfData = _pdfExport.GenerateQSReport("Project Manager", DateTime.Now, est, bud);
            }
            else if (model.ReportSection == "Income" || model.ReportSection == "Expenses" || model.ReportSection == "BudgetVsActual" || model.ReportSection == "Financial")
            {
                var b = bud.Select(x => new BudgetSummary
                {
                    Project = x.Project, Budget = x.TotalBudget, Spent = x.TotalSpent,
                    Remaining = x.Remaining, Health = x.Health, Status = x.Status
                }).ToList();
                var p = model.FinancialProjects.Select(x => new ProjectProfitability
                {
                    Project = x.Name, Income = x.Income, Expenses = x.Expenses
                }).ToList();
                var byMonth = new Dictionary<string, MonthlyCashFlow>();
                foreach (var i in inc)
                {
                    var key = i.TransactionDate.ToString("MMM yyyy");
                    if (!byMonth.ContainsKey(key)) byMonth[key] = new MonthlyCashFlow { Period = key };
                    byMonth[key].Inflow += i.Amount;
                }
                foreach (var e in exp)
                {
                    var key = e.ExpenseDate.ToString("MMM yyyy");
                    if (!byMonth.ContainsKey(key)) byMonth[key] = new MonthlyCashFlow { Period = key };
                    byMonth[key].Outflow += e.Amount;
                }
                var cf = byMonth.Values.OrderBy(m => m.Period).ToList();
                var ev = model.AllEstimates.Select(e => new EstimateVsActual
                {
                    Project = e.ProjectName, Estimated = e.TotalCost,
                    Actual = e.AmountSpent, Status = e.Status
                }).ToList();
                pdfData = _pdfExport.GenerateFOReport("Project Manager", DateTime.Now, inc, exp, b, p, cf, ev);
            }
            else
            {
                pdfData = _pdfExport.GenerateSummaryReport("Project Manager", DateTime.Now,
                    new List<ProgressReport>(), new List<SiteRequest>(), new List<SiteIssue>());
            }

            var fileName = $"PM_{model.ReportSection}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return File(pdfData, "application/pdf", fileName);
        }

        private async Task<PMReportsViewModel> BuildReportsModelAsync(string? section, string? status, string? dateRange,
            string? search, DateTime? startDate, DateTime? endDate)
        {
            var progressList = await LoadProgressAsync();
            var resourceList = await LoadRequestsAsync();
            var issueList = await LoadIssuesAsync();
            var estimateList = await LoadEstimatesAsync();
            var budgetList = await LoadBudgetsAsync();
            var incomeList = await LoadIncomeAsync();
            var expenseList = await LoadExpensesAsync();
            var taskList = await LoadTasksAsync();
            var projectList = await LoadProjectsAsync();

            string sectionKey = string.IsNullOrEmpty(section) ? "All" : section;
            string statusKey = string.IsNullOrEmpty(status) ? "All" : status;
            string rangeKey = string.IsNullOrEmpty(dateRange) ? "all" : dateRange;
            var searchKey = search ?? "";

            var model = new PMReportsViewModel
            {
                AllProgress = progressList,
                AllResources = resourceList,
                AllIssues = issueList,
                AllEstimates = estimateList,
                AllBudgets = budgetList,
                AllIncomes = incomeList,
                AllExpenses = expenseList,
                AllTasks = taskList,
                AllProjects = projectList,
                ReportSection = sectionKey,
                StatusFilter = statusKey,
                SelectedDateRange = rangeKey,
                Search = searchKey,
                CustomStartDate = startDate,
                CustomEndDate = endDate,
                TotalIncome = incomeList.Sum(i => i.Amount),
                TotalExpenses = expenseList.Sum(e => e.Amount),
                NetBalance = incomeList.Sum(i => i.Amount) - expenseList.Sum(e => e.Amount),
                TotalBudget = budgetList.Sum(b => b.TotalBudget),
                TotalSpent = budgetList.Sum(b => b.TotalSpent),
                FilteredProgress = FilterReports(progressList, searchKey, rangeKey, startDate, endDate),
                FilteredResources = FilterRequests(resourceList, searchKey, rangeKey, startDate, endDate),
                FilteredIssues = FilterIssues(issueList, searchKey, rangeKey, startDate, endDate),
                FilteredEstimates = FilterEstimates(estimateList, searchKey, statusKey),
                FilteredBudgets = FilterBudgets(budgetList, searchKey, statusKey),
                FilteredIncomes = FilterIncomes(incomeList, searchKey, statusKey, rangeKey, startDate, endDate),
                FilteredExpenses = FilterExpenses(expenseList, searchKey, statusKey, rangeKey, startDate, endDate),
                FilteredTasks = FilterTasks(taskList, searchKey),
                FilteredProjects = FilterProjects(projectList, searchKey),
                FinancialProjects = BuildFinancialProjects(incomeList, expenseList, budgetList)
            };

            model.BudgetSummaryItems = budgetList
                .Where(b => string.IsNullOrEmpty(searchKey) || (b.Project ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase))
                .Select(b => new BudgetSummary
                {
                    Project = b.Project ?? "",
                    Budget = b.TotalBudget,
                    Spent = b.TotalSpent,
                    Remaining = b.Remaining,
                    Health = b.Health ?? "",
                    Status = b.Status ?? ""
                }).ToList();

            var incomeByProject = incomeList
                .Where(i => string.IsNullOrEmpty(searchKey) || (i.Project ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => i.Project ?? "")
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));
            var expenseByProject = expenseList
                .Where(e => string.IsNullOrEmpty(searchKey) || (e.Project ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Project ?? "")
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
            var allProjectNames = incomeByProject.Keys.Union(expenseByProject.Keys).OrderBy(p => p);
            model.ProfitabilityItems = allProjectNames.Select(p => new ProjectProfitability
            {
                Project = p,
                Income = incomeByProject.GetValueOrDefault(p, 0),
                Expenses = expenseByProject.GetValueOrDefault(p, 0)
            }).ToList();

            var cashFlowDict = new Dictionary<string, MonthlyCashFlow>();
            foreach (var inc in incomeList.Where(i => string.IsNullOrEmpty(searchKey) || (i.Project ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase)))
            {
                var key = inc.TransactionDate.ToString("yyyy-MM");
                if (!cashFlowDict.ContainsKey(key))
                    cashFlowDict[key] = new MonthlyCashFlow { Period = inc.TransactionDate.ToString("MMM yyyy") };
                cashFlowDict[key].Inflow += inc.Amount;
            }
            foreach (var exp in expenseList.Where(e => string.IsNullOrEmpty(searchKey) || (e.Project ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase)))
            {
                var key = exp.ExpenseDate.ToString("yyyy-MM");
                if (!cashFlowDict.ContainsKey(key))
                    cashFlowDict[key] = new MonthlyCashFlow { Period = exp.ExpenseDate.ToString("MMM yyyy") };
                cashFlowDict[key].Outflow += exp.Amount;
            }
            model.CashFlowItems = cashFlowDict.Values.OrderBy(m => m.Period).ToList();

            var expenseSumsAll = expenseList.GroupBy(e => e.Project ?? "").ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
            model.EstimateVsActualItems = estimateList
                .Where(e => string.IsNullOrEmpty(searchKey) || (e.ProjectName ?? "").Contains(searchKey, StringComparison.OrdinalIgnoreCase))
                .Select(e => new EstimateVsActual
                {
                    Project = e.ProjectName ?? "",
                    Estimated = e.TotalCost,
                    Actual = expenseSumsAll.GetValueOrDefault(e.ProjectName ?? "", 0),
                    Status = e.Status ?? ""
                }).ToList();

            model.CurrentSectionTitle = GetPMReportSectionTitle(sectionKey);
            model.CurrentItemCount = sectionKey switch
            {
                "Projects" => model.FilteredProjects.Count,
                "Tasks" => model.FilteredTasks.Count,
                "Budget" => model.BudgetSummaryItems.Count,
                "Income" => model.FilteredIncomes.Count,
                "Expenses" => model.FilteredExpenses.Count,
                "Profitability" => model.ProfitabilityItems.Count,
                "CashFlow" => model.CashFlowItems.Count,
                "Estimates" => model.EstimateVsActualItems.Count,
                "CostEstimates" => model.FilteredEstimates.Count,
                "ProjectBudgets" => model.FilteredBudgets.Count,
                "Progress" => model.FilteredProgress.Count,
                "Issues" => model.FilteredIssues.Count,
                "Resources" => model.FilteredResources.Count,
                _ => model.FilteredProjects.Count + model.FilteredTasks.Count + model.BudgetSummaryItems.Count
                     + model.FilteredIncomes.Count + model.FilteredExpenses.Count + model.ProfitabilityItems.Count
                     + model.CashFlowItems.Count + model.EstimateVsActualItems.Count + model.FilteredEstimates.Count
                     + model.FilteredBudgets.Count + model.FilteredProgress.Count
                     + model.FilteredIssues.Count + model.FilteredResources.Count
            };

            return model;
        }

        private static string GetPMReportSectionTitle(string section)
        {
            return section switch
            {
                "Projects" => "Project Report",
                "Tasks" => "Task Report",
                "Budget" => "Budget vs Actual",
                "Income" => "Income Transactions",
                "Expenses" => "Expense Transactions",
                "Profitability" => "Project Profitability",
                "CashFlow" => "Cash Flow Trends",
                "Estimates" => "Estimate vs Actual",
                "CostEstimates" => "Cost Estimates",
                "ProjectBudgets" => "Project Budgets",
                "Progress" => "Progress Report",
                "Issues" => "Issues Report",
                "Resources" => "Resource Requests",
                _ => "All Reports"
            };
        }

        private static bool DateMatches(DateTime date, string range, DateTime? start, DateTime? end)
        {
            var today = DateTime.Today;
            return range switch
            {
                "today" => date.Date == today,
                "thisweek" => date.Date >= StartOfWeek(today) && date.Date < StartOfWeek(today).AddDays(7),
                "thismonth" => date.Year == today.Year && date.Month == today.Month,
                "thisyear" => date.Year == today.Year,
                "customrange" => (!start.HasValue || date.Date >= start.Value.Date) && (!end.HasValue || date.Date <= end.Value.Date),
                _ => true
            };
        }

        private static List<ProgressReport> FilterReports(List<ProgressReport> source, string? search,
            string range, DateTime? start, DateTime? end)
        {
            return source.Where(p =>
                (string.IsNullOrEmpty(search) || p.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                DateMatches(p.UpdateDate, range, start, end)).ToList();
        }

        private static List<SiteRequest> FilterRequests(List<SiteRequest> source, string? search,
            string range, DateTime? start, DateTime? end)
        {
            return source.Where(r =>
                (string.IsNullOrEmpty(search) || r.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                DateMatches(r.NeededBy, range, start, end)).ToList();
        }

        private static List<SiteIssue> FilterIssues(List<SiteIssue> source, string? search,
            string range, DateTime? start, DateTime? end)
        {
            return source.Where(i =>
                (string.IsNullOrEmpty(search) || i.Project.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    i.IssueType.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                DateMatches(i.DateReported, range, start, end)).ToList();
        }

        private static List<CostEstimate> FilterEstimates(List<CostEstimate> source, string? search, string? status)
        {
            return source.Where(e =>
                (string.IsNullOrEmpty(search) || e.ProjectName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(status) || status == "All" || (e.Status ?? "") == status)).ToList();
        }

        private static List<QS_Budget> FilterBudgets(List<QS_Budget> source, string? search, string? status)
        {
            return source.Where(b =>
                (string.IsNullOrEmpty(search) || b.Project.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(status) || status == "All" || (b.Status ?? "") == status)).ToList();
        }

        private static List<PM_IncomeTransactions> FilterIncomes(List<PM_IncomeTransactions> source, string? search,
            string status, string range, DateTime? start, DateTime? end)
        {
            return source.Where(i =>
                (string.IsNullOrEmpty(search) || i.Project.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (i.Category ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (status == "All" || (i.Status ?? "") == status) &&
                DateMatches(i.TransactionDate, range, start, end)).ToList();
        }

        private static List<Expense> FilterExpenses(List<Expense> source, string? search,
            string status, string range, DateTime? start, DateTime? end)
        {
            return source.Where(e =>
                (string.IsNullOrEmpty(search) || e.Project.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Category ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (status == "All" || (e.Status ?? "") == status) &&
                DateMatches(e.ExpenseDate, range, start, end)).ToList();
        }

        private static List<PM_Tasks> FilterTasks(List<PM_Tasks> source, string? search)
        {
            return source.Where(t =>
                string.IsNullOrEmpty(search) || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (t.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<PM_Projects> FilterProjects(List<PM_Projects> source, string? search)
        {
            return source.Where(p =>
                string.IsNullOrEmpty(search) || (p.ProjectName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (p.Location ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static List<PMFinancialProject> BuildFinancialProjects(List<PM_IncomeTransactions> incomeList,
            List<Expense> expenseList, List<QS_Budget> budgetList)
        {
            var results = new List<PMFinancialProject>();
            var allProjects = incomeList.Select(i => i.Project)
                .Union(expenseList.Select(e => e.Project))
                .Union(budgetList.Select(b => b.Project))
                .Distinct();
            foreach (var name in allProjects)
            {
                var inc = incomeList.Where(i => i.Project == name).Sum(i => i.Amount);
                var exp = expenseList.Where(e => e.Project == name).Sum(e => e.Amount);
                var budget = budgetList.Where(b => b.Project == name).Select(b => (decimal?)b.TotalBudget).FirstOrDefault() ?? 0;
                var remaining = budgetList.Where(b => b.Project == name).Select(b => (decimal?)b.Remaining).FirstOrDefault() ?? 0;
                results.Add(new PMFinancialProject { Name = name, Income = inc, Expenses = exp, Budget = budget, Remaining = remaining });
            }
            return results.OrderBy(p => p.Name).ToList();
        }

        private static List<string> GetMaterials(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        private static List<string> GetEquipment(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        [HttpGet]
        public async Task<IActionResult> Risks(string? status, string? severity, string? search, int? view,
            string? modal, string? err, string? notes)
        {
            var issues = await LoadIssuesAsync();

            var model = BuildRisksModel(issues, status, severity, search);

            if (view.HasValue)
            {
                var item = issues.FirstOrDefault(i => i.Id == view.Value);
                if (item != null)
                {
                    model.SelectedItem = item;
                    if (modal == "archive")
                    {
                        model.Modal = "confirm";
                        model.ConfirmId = item.Id;
                    }
                    else
                    {
                        model.Modal = "view";
                        model.PmNotes = notes ?? item.PMNotes ?? "";
                        model.Error = err;
                    }
                }
            }

            return View(model);
        }

        private static PMRisksViewModel BuildRisksModel(List<SiteIssue> issues, string? status, string? severity, string? search)
        {
            var model = new PMRisksViewModel
            {
                SelectedStatus = status ?? "",
                SelectedSeverity = severity ?? "",
                Search = search ?? "",
                TotalCount = issues.Count,
                OpenCount = issues.Count(r => r.Status == "Open"),
                ResolvedCount = issues.Count(r => r.Status == "Resolved")
            };

            var query = issues.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(r => r.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.SelectedSeverity))
                query = query.Where(r => r.Severity == model.SelectedSeverity);

            if (!string.IsNullOrEmpty(model.Search))
                query = query.Where(r => r.Project.Contains(model.Search, StringComparison.OrdinalIgnoreCase));

            model.Items = query.ToList();
            return model;
        }

        [HttpPost]
        public async Task<IActionResult> SubmitResolution(int issueId, string pmNotes)
        {
            if (string.IsNullOrWhiteSpace(pmNotes))
            {
                var query = new Dictionary<string, string?>
                {
                    ["view"] = issueId.ToString(),
                    ["err"] = "Please enter a resolution plan before submitting.",
                    ["notes"] = pmNotes
                };
                return RedirectToAction("Risks", query);
            }

            try { await _issueService.SubmitResolutionAsync(issueId, pmNotes.Trim()); }
            catch { }

            return RedirectToAction("Risks");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveIssue(int id)
        {
            try { await _issueService.ArchiveAsync(id); }
            catch { }

            return RedirectToAction("Risks");
        }

        [HttpGet]
        public async Task<IActionResult> Resources(string? status, string? search, int? view, string? modal)
        {
            var requests = await LoadRequestsAsync();

            var model = BuildSiteRequestsModel(requests, status, search);

            if (view.HasValue)
            {
                var item = requests.FirstOrDefault(r => r.Id == view.Value);
                if (item != null)
                {
                    model.SelectedItem = item;
                    model.ActionTargetId = item.Id;
                    model.Modal = modal == "reject" ? "reject" : modal == "confirm" ? "confirm" : "view";
                    if (modal == "reject") model.RejectRemarks = "";
                }
            }

            return View(model);
        }

        private static PMSiteRequestsViewModel BuildSiteRequestsModel(List<SiteRequest> requests, string? status, string? search)
        {
            var model = new PMSiteRequestsViewModel
            {
                SelectedStatus = status ?? "",
                Search = search ?? "",
                TotalCount = requests.Count,
                PendingCount = requests.Count(r => r.Status == "Pending"),
                ApprovedCount = requests.Count(r => r.Status == "Approved"),
                RejectedCount = requests.Count(r => r.Status == "Rejected")
            };

            var query = requests.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(r => r.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
                query = query.Where(r => r.Project.Contains(model.Search, StringComparison.OrdinalIgnoreCase));

            model.Items = query.ToList();
            return model;
        }

        [HttpPost]
        public async Task<IActionResult> ApproveRequest(int id, string? status, string? search)
        {
            var requests = await LoadRequestsAsync();
            var item = requests.FirstOrDefault(r => r.Id == id);

            if (item == null)
                return RedirectToAction("Resources", new { status, search });

            if (item.Status != "Pending")
                return await RenderResourcesError(id, status, search, "view", null,
                    $"This request has already been {item.Status.ToLowerInvariant()} and can no longer be approved.", null);

            var success = false;
            try { success = await _requestService.ApproveAsync(id, Auth.CurrentDisplayName ?? "ProjectManager"); }
            catch { }

            if (!success)
                return await RenderResourcesError(id, status, search, "view", null,
                    "Approval failed. Please try again.", null);

            return RedirectToAction("Resources", new { status, search });
        }

        [HttpPost]
        public async Task<IActionResult> RejectRequest(int id, string? remarks, string? status, string? search)
        {
            var requests = await LoadRequestsAsync();
            var item = requests.FirstOrDefault(r => r.Id == id);

            if (item == null)
                return RedirectToAction("Resources", new { status, search });

            if (item.Status != "Pending")
                return await RenderResourcesError(id, status, search, "reject",
                    $"This request has already been {item.Status.ToLowerInvariant()} and can no longer be rejected.", null, remarks);

            if (string.IsNullOrWhiteSpace(remarks))
                return await RenderResourcesError(id, status, search, "reject",
                    "Reason for rejection is required.", null, remarks);

            var success = false;
            try { success = await _requestService.RejectAsync(id, Auth.CurrentDisplayName ?? "ProjectManager", remarks.Trim()); }
            catch { }

            if (!success)
                return await RenderResourcesError(id, status, search, "reject",
                    "Rejection failed. Please try again.", null, remarks);

            return RedirectToAction("Resources", new { status, search });
        }

        private async Task<IActionResult> RenderResourcesError(int id, string? status, string? search,
            string modal, string? rejectError, string? approveError, string? remarks)
        {
            var requests = await LoadRequestsAsync();
            var model = BuildSiteRequestsModel(requests, status, search);

            var item = requests.FirstOrDefault(r => r.Id == id);
            if (item == null) return RedirectToAction("Resources", new { status, search });

            model.SelectedItem = item;
            model.ActionTargetId = item.Id;
            model.Modal = modal;
            model.RejectRemarks = remarks ?? "";
            model.RejectError = rejectError;
            model.ApproveError = approveError;

            return View("Resources", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveRequest(int id)
        {
            try { await _requestService.ArchiveAsync(id); }
            catch { }

            return RedirectToAction("Resources");
        }

        [HttpGet]
        public async Task<IActionResult> Map(string? statusFilter, string? search, string? dateFilterLabel)
        {
            var projects = await LoadProjectsAsync();
            var progressList = await LoadProgressAsync();

            // Derive project status from latest progress updates
            foreach (var project in projects)
            {
                var projectProgress = progressList
                    .Where(p => p.Project.Equals(project.ProjectName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                project.Status = DeriveProjectStatusFromProgress(projectProgress, project.Status ?? "Planning");
            }

            var model = new MapViewModel
            {
                StatusFilter = statusFilter ?? "",
                Search = search ?? "",
                DateFilterLabel = string.IsNullOrEmpty(dateFilterLabel) ? "All" : dateFilterLabel,
                Projects = projects.Where(p =>
                    (string.IsNullOrEmpty(statusFilter) || p.Status == statusFilter) &&
                    (string.IsNullOrEmpty(search) || (p.ProjectName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) || (p.Location ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
                ).Select(p => new ProjectMapItem
                {
                    Name = p.ProjectName ?? "",
                    Location = p.Location ?? "",
                    Status = p.Status ?? "Planning",
                    Progress = DeriveProgressPercentage(p.Status)
                }).ToList(),
                ActiveProjectCount = projects.Count(p => p.Status == "In Progress"),
                LocationCount = projects.Where(p => !string.IsNullOrEmpty(p.Location)).Select(p => p.Location).Distinct().Count(),
                CompletedCount = projects.Count(p => p.Status == "Completed")
            };

            return View(model);
        }

        private static int DeriveProgressPercentage(string? status)
        {
            return status switch
            {
                "Planning" => 0,
                "In Progress" => 50,
                "Completed" => 100,
                "On Hold" => 30,
                _ => 0
            };
        }

        [HttpGet]
        public async Task<IActionResult> Issues(string? status, string? severity, string? search, int? view)
        {
            var issues = await LoadIssuesAsync();

            var model = BuildIssuesModel(issues, status, severity, search);

            if (view.HasValue)
            {
                var item = issues.FirstOrDefault(i => i.Id == view.Value);
                if (item != null)
                {
                    model.SelectedItem = item;
                    model.Modal = "view";
                }
            }

            return View(model);
        }

        private static PMIssuesViewModel BuildIssuesModel(List<SiteIssue> issues, string? status, string? severity, string? search)
        {
            var model = new PMIssuesViewModel
            {
                SelectedStatus = status ?? "",
                SelectedSeverity = severity ?? "",
                Search = search ?? "",
                TotalCount = issues.Count,
                OpenCount = issues.Count(r => r.Status == "Open"),
                ResolvedCount = issues.Count(r => r.Status == "Resolved")
            };

            var query = issues.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(r => r.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.SelectedSeverity))
                query = query.Where(r => r.Severity == model.SelectedSeverity);

            if (!string.IsNullOrEmpty(model.Search))
                query = query.Where(r => r.Project.Contains(model.Search, StringComparison.OrdinalIgnoreCase));

            model.Items = query.ToList();
            return model;
        }

        [HttpGet]
        public async Task<IActionResult> Designs(string? status, string? dateRange, string? search,
            DateTime? start, DateTime? end, int? view, string? modal, string? reason, string? err)
        {
            var designs = await LoadDesignsAsync();

            var model = BuildDesignsModel(designs, status, dateRange, search, start, end);

            if (view.HasValue)
            {
                var item = designs.FirstOrDefault(d => d.DesignID == view.Value);
                if (item != null)
                {
                    model.SelectedItem = item;
                    model.ActionTargetId = item.DesignID;
                    model.Reason = reason ?? "";
                    model.Error = err;
                    model.Modal = modal switch
                    {
                        "reject" => "reject",
                        "revised" => "revised",
                        "archive" => "archive",
                        "fullimage" => "fullimage",
                        _ => "view"
                    };
                }
            }

            return View(model);
        }

        private async Task<List<ArchitectDesign>> LoadDesignsAsync()
        {
            try { return await _designService.GetAllDesignsAsync(); }
            catch { return new List<ArchitectDesign>(); }
        }

        private static PMDesignsViewModel BuildDesignsModel(List<ArchitectDesign> designs, string? status,
            string? dateRange, string? search, DateTime? start, DateTime? end)
        {
            string rangeKey = string.IsNullOrEmpty(dateRange) ? "all" : dateRange;

            var model = new PMDesignsViewModel
            {
                SelectedStatus = status ?? "",
                SelectedDateRange = rangeKey,
                Search = search ?? "",
                CustomStart = start,
                CustomEnd = end,
                TotalCount = designs.Count,
                PendingCount = designs.Count(d => d.Status == "Pending"),
                ApprovedCount = designs.Count(d => d.Status == "Approved"),
                RejectedCount = designs.Count(d => d.Status == "Rejected")
            };

            var query = designs.AsEnumerable();

            if (!string.IsNullOrEmpty(model.SelectedStatus))
                query = query.Where(d => d.Status == model.SelectedStatus);

            if (!string.IsNullOrEmpty(model.Search))
                query = query.Where(d =>
                    (d.DesignTitle ?? "").Contains(model.Search, StringComparison.OrdinalIgnoreCase) ||
                    (d.Project ?? "").Contains(model.Search, StringComparison.OrdinalIgnoreCase));

            var today = DateTime.Today;
            query = rangeKey switch
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
                "custom" => query.Where(d =>
                {
                    if (start.HasValue && end.HasValue)
                        return d.UploadDate >= start.Value && d.UploadDate <= end.Value;
                    return true;
                }),
                _ => query
            };

            model.Items = query.OrderByDescending(d => d.DesignID).ToList();
            return model;
        }

        [HttpPost]
        public async Task<IActionResult> ApproveDesign(int id)
        {
            try { await _designService.ReviewDesignAsync(id, "Approved", "Design has been approved.", null, Auth.CurrentRole ?? "ProjectManager"); }
            catch { }

            return RedirectToAction("Designs");
        }

        [HttpPost]
        public async Task<IActionResult> RejectDesign(int id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return RedirectToAction("Designs", new { view = id, modal = "reject", err = "Please provide a reason for rejection." });

            try { await _designService.ReviewDesignAsync(id, "Rejected", "", reason.Trim(), Auth.CurrentRole ?? "ProjectManager"); }
            catch { }

            return RedirectToAction("Designs");
        }

        [HttpPost]
        public async Task<IActionResult> RequestRevision(int id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return RedirectToAction("Designs", new { view = id, modal = "revised", err = "Please provide a reason for revision." });

            try
            {
                var design = await _designService.GetDesignByIdAsync(id);
                if (design != null)
                {
                    var revision = new ArchitectRevision
                    {
                        DesignID = design.DesignID,
                        ChangeRequest = design.DesignTitle,
                        RevisionNotes = reason.Trim(),
                        Project = design.Project,
                        RequestedBy = Auth.CurrentRole ?? "ProjectManager",
                        RequestDate = DateTime.Now
                    };
                    var created = await _revisionService.CreateAsync(revision);
                    if (created)
                        await _designService.MarkAsRevisedAsync(design.DesignID, reason.Trim());
                }
            }
            catch { }

            return RedirectToAction("Designs");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveDesign(int id)
        {
            try { await _designService.ArchiveDesignAsync(id); }
            catch { }

            return RedirectToAction("Designs");
        }
    }
}