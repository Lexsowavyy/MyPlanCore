using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlanCoreMAUI;
using PlanCoreMAUI.Data;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    public class ArchitectController : AppControllerBase
    {
        private readonly ArchitectDesignService _designService;
        private readonly ArchitectRevisionService _revisionService;
        private readonly PM_TaskService _taskService;

        public ArchitectController(AuthService auth, SubscriptionService subscription, AuditService audit,
            ArchitectDesignService designService, ArchitectRevisionService revisionService, PM_TaskService taskService)
            : base(auth, subscription, audit)
        {
            _designService = designService;
            _revisionService = revisionService;
            _taskService = taskService;
        }

        protected override string? RequiredRole => "Architect";

        [HttpGet]
        public async Task<IActionResult> Index(string? filter)
        {
            var model = new ArchitectDashboardViewModel
            {
                DisplayName = Auth.CurrentDisplayName ?? "",
                DashboardFilter = filter ?? "All"
            };

            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    try { model.Designs = await db.ArchitectDesigns.Where(d => !d.IsArchived).OrderByDescending(d => d.DesignID).ToListAsync(); } catch { }
                    try { model.Revisions = await db.ArchitectRevisions.Where(r => !r.IsArchived).OrderByDescending(r => r.RevisionID).ToListAsync(); } catch { }
                    try { model.Tasks = await _taskService.GetTasksByAssignedRoleAsync("Architect"); } catch { }
                }
            }
            catch { }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Tasks(string? status, string? search, string? date, int? view, int? archive)
        {
            var model = new ArchitectTasksViewModel
            {
                StatusFilter = status ?? "",
                SearchQuery = search ?? "",
                DateFilter = date ?? "All",
                Tasks = await _taskService.GetTasksByAssignedRoleAsync("Architect")
            };

            if (view.HasValue)
            {
                model.SelectedTask = model.Tasks.FirstOrDefault(t => t.Id == view.Value);
                model.ShowViewModal = model.SelectedTask != null;
            }

            if (archive.HasValue)
            {
                model.ArchiveTargetId = archive.Value;
                model.ShowArchiveConfirm = true;
            }

            return View(model);
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

        [HttpPost]
        public async Task<IActionResult> ArchiveTask(int id)
        {
            await _taskService.ArchiveTaskAsync(id);
            return RedirectToAction("Tasks");
        }

        [HttpGet]
        public async Task<IActionResult> Archived(string? tab, string? viewType, int? viewId,
            string? restoreType, int? restoreId, string? deleteType, int? deleteId)
        {
            var model = new ArchitectArchivedViewModel
            {
                ActiveTab = tab ?? "designs",
                ArchivedDesigns = await _designService.GetArchivedDesignsAsync(),
                ArchivedRevisions = await _revisionService.GetArchivedAsync(),
                ArchivedTasks = await _taskService.GetArchivedTasksAsync()
            };

            if (!string.IsNullOrEmpty(viewType) && viewId.HasValue)
            {
                switch (viewType)
                {
                    case "design":
                        model.SelectedDesign = model.ArchivedDesigns.FirstOrDefault(d => d.DesignID == viewId.Value);
                        model.ShowViewModal = model.SelectedDesign != null;
                        break;
                    case "revision":
                        model.SelectedRevision = model.ArchivedRevisions.FirstOrDefault(r => r.RevisionID == viewId.Value);
                        model.ShowViewModal = model.SelectedRevision != null;
                        break;
                    case "task":
                        model.SelectedTask = model.ArchivedTasks.FirstOrDefault(t => t.Id == viewId.Value);
                        model.ShowViewModal = model.SelectedTask != null;
                        break;
                }
            }

            if (!string.IsNullOrEmpty(restoreType) && restoreId.HasValue)
            {
                model.RestoreTargetType = restoreType;
                model.RestoreTargetId = restoreId.Value;
                model.ShowRestoreConfirm = true;
            }

            if (!string.IsNullOrEmpty(deleteType) && deleteId.HasValue)
            {
                model.DeleteTargetType = deleteType;
                model.DeleteTargetId = deleteId.Value;
                model.ShowDeleteConfirm = true;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Restore(string type, int id)
        {
            switch (type)
            {
                case "design":
                    await _designService.RestoreDesignAsync(id);
                    break;
                case "revision":
                    await _revisionService.RestoreAsync(id);
                    break;
                case "task":
                    await _taskService.RestoreTaskAsync(id);
                    break;
            }
            return RedirectToAction("Archived", new { tab = TabForType(type) });
        }

        [HttpPost]
        public async Task<IActionResult> Delete(string type, int id)
        {
            switch (type)
            {
                case "design":
                    await _designService.DeleteDesignAsync(id);
                    break;
                case "revision":
                    await _revisionService.DeleteAsync(id);
                    break;
                case "task":
                    await _taskService.DeleteTaskAsync(id);
                    break;
            }
            return RedirectToAction("Archived", new { tab = TabForType(type) });
        }

        private static string TabForType(string? type) => type switch
        {
            "design" => "designs",
            "revision" => "revisions",
            "task" => "tasks",
            _ => "designs"
        };

        [HttpGet]
        public async Task<IActionResult> Designs(string? status, string? range, string? search,
            string? add, int? view, int? edit, int? archive, int? full)
        {
            var model = await NewDesignsModelAsync();
            model.SelectedStatus = status ?? "";
            model.SelectedDateRange = range ?? "all";
            model.SearchQuery = search ?? "";

            if (add == "1")
                model.ShowUploadModal = true;

            if (view.HasValue)
            {
                model.SelectedDesign = model.Designs.FirstOrDefault(d => d.DesignID == view.Value);
                model.ShowViewModal = model.SelectedDesign != null;
            }

            if (edit.HasValue)
            {
                var d = model.Designs.FirstOrDefault(x => x.DesignID == edit.Value);
                if (d != null)
                {
                    model.EditId = d.DesignID;
                    model.EditTitle = d.DesignTitle;
                    model.EditProject = d.Project;
                    model.EditLocation = d.Location ?? "";
                    model.EditVersion = d.Version;
                    model.EditNotes = d.Notes ?? "";
                    model.EditFileName = d.FileName ?? "";
                    model.ShowEditModal = true;
                }
            }

            if (archive.HasValue)
            {
                model.ArchiveTargetId = archive.Value;
                model.ShowArchiveConfirm = true;
            }

            if (full.HasValue)
            {
                model.SelectedDesign = model.Designs.FirstOrDefault(d => d.DesignID == full.Value);
                model.ShowFullImage = model.SelectedDesign?.FileData != null && model.SelectedDesign.FileData.Length > 0;
                if (model.ShowFullImage)
                    model.ShowViewModal = true;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> UploadDesign(string designTitle, string designProject, string? designLocation,
            string? designVersion, DateTime? uploadDate, string? designNotes, IFormFile? designFile)
        {
            var model = await NewDesignsModelAsync();
            model.ShowUploadModal = true;
            model.UploadTitle = designTitle ?? "";
            model.UploadProject = designProject ?? "";
            model.UploadLocation = designLocation ?? "";
            model.UploadVersion = string.IsNullOrEmpty(designVersion) ? "v1" : designVersion;
            model.UploadDate = uploadDate ?? DateTime.Now;
            model.UploadNotes = designNotes ?? "";
            model.UploadFileName = designFile?.FileName;

            if (string.IsNullOrWhiteSpace(designTitle) || string.IsNullOrWhiteSpace(designProject))
            {
                model.UploadError = "Design Title and Project are required.";
                return View("Designs", model);
            }

            if (model.Designs.Any(d => d.DesignTitle.Equals(designTitle.Trim(), StringComparison.OrdinalIgnoreCase)
                                  && d.Project.Equals(designProject.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                model.UploadError = "A design with this title already exists for this project.";
                return View("Designs", model);
            }

            var newDesign = new ArchitectDesign
            {
                DesignTitle = designTitle.Trim(),
                Project = designProject.Trim(),
                Location = designLocation,
                Version = string.IsNullOrEmpty(designVersion) ? "v1" : designVersion,
                UploadDate = uploadDate ?? DateTime.Now,
                Notes = designNotes,
                Status = "Pending",
                UploadedBy = Auth.CurrentRole
            };

            if (designFile != null && designFile.Length > 0)
            {
                newDesign.FileName = designFile.FileName;
                using var stream = new MemoryStream();
                await designFile.CopyToAsync(stream);
                newDesign.FileData = stream.ToArray();
            }

            var success = await _designService.CreateDesignAsync(newDesign);
            if (success)
                return RedirectToAction("Designs");

            model.UploadError = "Unable to upload design. Please try again.";
            return View("Designs", model);
        }

        [HttpPost]
        public async Task<IActionResult> SaveEditDesign(int id, string editDesignTitle, string editDesignVersion,
            IFormFile? editDesignFile)
        {
            var model = await NewDesignsModelAsync();
            model.ShowEditModal = true;
            model.EditId = id;
            model.EditTitle = editDesignTitle ?? "";

            var existing = model.Designs.FirstOrDefault(d => d.DesignID == id);
            if (existing == null)
            {
                model.EditError = "Design not found.";
                return View("Designs", model);
            }

            // Project, Location and Notes are never read back from the request: they are
            // always the values already stored on the design.
            model.EditProject = existing.Project;
            model.EditLocation = existing.Location ?? "";
            model.EditNotes = existing.Notes ?? "";
            model.EditFileName = existing.FileName ?? "";
            model.EditVersion = editDesignVersion ?? "";

            if (string.IsNullOrWhiteSpace(editDesignTitle))
            {
                model.EditError = "Design Title is required.";
                return View("Designs", model);
            }

            byte[]? newFileData = null;
            string? newFileName = null;
            if (editDesignFile != null && editDesignFile.Length > 0)
            {
                using var stream = new MemoryStream();
                await editDesignFile.CopyToAsync(stream);
                newFileData = stream.ToArray();
                newFileName = editDesignFile.FileName;
            }

            var editDesign = new ArchitectDesign
            {
                DesignID = id,
                DesignTitle = editDesignTitle.Trim(),
                Project = existing.Project,
                Location = existing.Location,
                Version = string.IsNullOrWhiteSpace(editDesignVersion) ? existing.Version : editDesignVersion.Trim(),
                Notes = existing.Notes,
                UploadDate = existing.UploadDate,
                FileName = newFileName ?? existing.FileName,
                FileData = newFileData
            };

            var success = await _designService.UpdateDesignAsync(editDesign);
            if (success)
                return RedirectToAction("Designs");

            model.EditError = "Unable to save changes. Please try again.";
            return View("Designs", model);
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveDesign(int id)
        {
            await _designService.ArchiveDesignAsync(id);
            return RedirectToAction("Designs");
        }

        private async Task<ArchitectDesignsViewModel> NewDesignsModelAsync()
        {
            var model = new ArchitectDesignsViewModel();
            try
            {
                model.Designs = await _designService.GetAllDesignsAsync();
            }
            catch
            {
                model.Designs = new();
            }
            return model;
        }

        [HttpGet]
        public async Task<IActionResult> Revisions(string? status, string? search, string? range, int? view, int? accept, bool full)
        {
            var model = new ArchitectRevisionsViewModel
            {
                SelectedStatus = status ?? "",
                SearchQuery = search ?? "",
                SelectedDateRange = range ?? "all",
                Revisions = await _revisionService.GetAllAsync()
            };

            if (view.HasValue)
            {
                model.SelectedRevision = model.Revisions.FirstOrDefault(r => r.RevisionID == view.Value);
                model.ShowViewModal = model.SelectedRevision != null;
                if (model.SelectedRevision != null && model.SelectedRevision.DesignID.HasValue)
                    model.LinkedDesign = await _designService.GetDesignByIdAsync(model.SelectedRevision.DesignID.Value);
                if (full)
                    model.ShowFullImage = model.LinkedDesign?.FileData != null && model.LinkedDesign.FileData.Length > 0;
            }

            if (accept.HasValue)
            {
                model.AcceptTargetId = accept.Value;
                model.ShowConfirmAccept = true;
            }

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AcceptRevision(int id)
        {
            var revisions = await _revisionService.GetAllAsync();
            var revision = revisions.FirstOrDefault(r => r.RevisionID == id);
            if (revision != null)
            {
                await _revisionService.UpdateStatusAsync(id, "Accepted");
                if (revision.DesignID.HasValue)
                    await _designService.UpdateDesignStatusAsync(revision.DesignID.Value, "Accepted");
            }
            return RedirectToAction("Revisions");
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveRevision(int id)
        {
            await _revisionService.ArchiveAsync(id);
            return RedirectToAction("Revisions");
        }
    }
}
