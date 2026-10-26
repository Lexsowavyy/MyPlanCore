using Microsoft.AspNetCore.Mvc;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;
using PlanCore.Web.Infrastructure;
using PlanCore.Web.ViewModels;

namespace PlanCore.Web.Controllers
{
    public class FinanceOfficerController : AppControllerBase
    {
        private readonly FO_IncomeTransactionService _incomeService;
        private readonly FO_ExpenseService _expenseService;
        private readonly CashFlowService _cashFlowService;
        private readonly QS_BudgetService _budgetService;
        private readonly QS_CostEstimateService _estimateService;
        private readonly PM_TaskService _taskService;
        private readonly PdfExportService _pdfExport;

        public FinanceOfficerController(AuthService auth, SubscriptionService subscription, AuditService audit,
            FO_IncomeTransactionService incomeService, FO_ExpenseService expenseService, CashFlowService cashFlowService,
            QS_BudgetService budgetService, QS_CostEstimateService estimateService, PM_TaskService taskService,
            PdfExportService pdfExport)
            : base(auth, subscription, audit)
        {
            _incomeService = incomeService;
            _expenseService = expenseService;
            _cashFlowService = cashFlowService;
            _budgetService = budgetService;
            _estimateService = estimateService;
            _taskService = taskService;
            _pdfExport = pdfExport;
        }

        protected override string? RequiredRole => "FinanceOfficer";

        private static readonly string[] IncomeCategories = new[] { "Client Payment", "Retainer", "Design Fee", "Consultation", "Reimbursement", "Other" };
        private static readonly string[] ExpenseCategories = new[] { "Labor", "Materials", "Equipment", "Transportation", "Utilities", "Other" };
        private static readonly string[] CashFlowCategories = new[] { "Opening Balance", "Bank Transfer", "Adjustment", "Other" };

        // ============================================================ Dashboard
        public async Task<IActionResult> Index(string? range)
        {
            var model = await BuildDashboardModelAsync(range ?? "All");
            return View(model);
        }

        private async Task<FODashboardViewModel> BuildDashboardModelAsync(string range)
        {
            var model = new FODashboardViewModel { SelectedDateRange = range };

            var incomes = await LoadIncomeAsync();
            var expenses = await LoadExpensesAsync();
            var budgets = await LoadBudgetsAsync();
            var estimates = await LoadEstimatesAsync();
            var tasks = await LoadTasksAsync();

            var filteredIncome = incomes.Where(i => DashboardDateMatches(i.TransactionDate, range)).ToList();
            var filteredExpense = expenses.Where(e => DashboardDateMatches(e.ExpenseDate, range)).ToList();

            model.TotalIncome = filteredIncome.Sum(i => i.Amount);
            model.TotalExpenses = filteredExpense.Sum(e => e.Amount);
            model.NetBalance = model.TotalIncome - model.TotalExpenses;
            model.TotalCombined = model.TotalIncome + model.TotalExpenses;
            model.RecordCount = filteredIncome.Count + filteredExpense.Count;
            model.PendingIncomeCount = incomes.Count(i => i.Status == "Pending");
            model.PendingExpenseCount = expenses.Count(e => e.Status == "Pending");
            model.ApprovedBudgetCount = budgets.Count(b => b.Status == "Approved");
            model.WaitingPMCount = estimates.Count(e => e.Status == "Waiting for PM Approval") + budgets.Count(b => b.Status == "Waiting for PM Approval");
            model.TaskCount = tasks.Count;

            var byMonth = new Dictionary<string, (decimal Inflow, decimal Outflow)>();
            foreach (var inc in filteredIncome)
            {
                var key = inc.TransactionDate.ToString("yyyy-MM");
                if (!byMonth.ContainsKey(key)) byMonth[key] = (0, 0);
                byMonth[key] = (byMonth[key].Inflow + inc.Amount, byMonth[key].Outflow);
            }
            foreach (var exp in filteredExpense)
            {
                var key = exp.ExpenseDate.ToString("yyyy-MM");
                if (!byMonth.ContainsKey(key)) byMonth[key] = (0, 0);
                byMonth[key] = (byMonth[key].Inflow, byMonth[key].Outflow + exp.Amount);
            }
            var sorted = byMonth.OrderBy(k => k.Key).Take(6).ToList();
            var maxVal = sorted.Any() ? sorted.Max(m => Math.Max(m.Value.Inflow, m.Value.Outflow)) : 1;
            model.MonthlyBars = sorted.Select(m => new FODashboardBarItem
            {
                Label = DateTime.ParseExact(m.Key + "-01", "yyyy-MM-dd", null).ToString("MMM"),
                Inflow = m.Value.Inflow,
                Outflow = m.Value.Outflow,
                InflowPct = maxVal > 0 ? (double)(m.Value.Inflow / maxVal) * 100.0 : 0,
                OutflowPct = maxVal > 0 ? (double)(m.Value.Outflow / maxVal) * 100.0 : 0
            }).ToList();

            var totalCount = budgets.Count > 0 ? budgets.Count : 1;
            model.BudgetsHealth = new List<FODashboardHealthItem>
            {
                new() { Label = "On Track", Count = budgets.Count(b => b.Health == "On Track"), Total = totalCount },
                new() { Label = "Warning", Count = budgets.Count(b => b.Health == "Warning"), Total = totalCount },
                new() { Label = "Critical", Count = budgets.Count(b => b.Health == "Critical"), Total = totalCount },
                new() { Label = "Over Budget", Count = budgets.Count(b => b.Health == "Over Budget"), Total = totalCount }
            };

            model.EstimateStatuses = new List<FODashboardStatusItem>
            {
                new() { Label = "Approved", Count = estimates.Count(e => e.Status == "Approved") },
                new() { Label = "Waiting for PM Approval", Count = estimates.Count(e => e.Status == "Waiting for PM Approval") },
                new() { Label = "Pending Approval", Count = estimates.Count(e => e.Status == "Pending Approval") },
                new() { Label = "Rejected", Count = estimates.Count(e => e.Status == "Rejected") }
            };

            var recent = new List<FODashboardActivityItem>();
            foreach (var inc in incomes.Where(i => DashboardDateMatches(i.TransactionDate, range)).OrderByDescending(i => i.TransactionDate).Take(5))
                recent.Add(new FODashboardActivityItem { Type = "Income", Title = $"INC-{inc.Id:D3} - {inc.Project}", Subtitle = $"{inc.Amount:N0} | {inc.Status}", Date = inc.TransactionDate.ToString("MMM d, yyyy") });
            foreach (var exp in expenses.Where(e => DashboardDateMatches(e.ExpenseDate, range)).OrderByDescending(e => e.ExpenseDate).Take(5))
                recent.Add(new FODashboardActivityItem { Type = "Expense", Title = $"EXP-{exp.Id:D3} - {exp.Project}", Subtitle = $"{exp.Amount:N0} | {exp.Status}", Date = exp.ExpenseDate.ToString("MMM d, yyyy") });
            model.RecentItems = recent.OrderByDescending(i => RecentActivityKey(i)).Take(8).ToList();

            return model;
        }

        private string RecentActivityKey(FODashboardActivityItem item) => item.Date;

        private static bool DashboardDateMatches(DateTime date, string filter)
        {
            var today = DateTime.Today;
            return filter switch
            {
                "Today" => date.Date == today,
                "This Week" => date.Date >= StartOfWeekMonday(today) && date.Date < StartOfWeekMonday(today).AddDays(7),
                "This Month" => date.Year == today.Year && date.Month == today.Month,
                "This Year" => date.Year == today.Year,
                _ => true
            };
        }

        // ===================================================== Project Budget
        public async Task<IActionResult> ProjectBudget(string? status, string? search, int? view, string? modal, int? id, int? err)
        {
            var budgets = await LoadBudgetsAsync();
            var model = BuildBudgetModel(budgets, status, search);

            model.Modal = view.HasValue ? "view" : modal;
            model.Reason = "";
            model.Error = err.HasValue ? "Please provide a reason for rejection." : null;

            if (view.HasValue)
                model.SelectedItem = budgets.FirstOrDefault(b => b.Id == view.Value);
            else if (id.HasValue)
                model.SelectedItem = budgets.FirstOrDefault(b => b.Id == id.Value);

            if (!string.IsNullOrEmpty(modal))
                model.ActionTargetId = id ?? 0;

            return View(model);
        }

        private static FOBudgetViewModel BuildBudgetModel(List<QS_Budget> budgets, string? status, string? search)
        {
            var filtered = budgets.Where(b =>
                (string.IsNullOrEmpty(status) || b.Status == status) &&
                (string.IsNullOrEmpty(search) || (b.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            ).OrderByDescending(b => b.CreatedAt).ToList();

            return new FOBudgetViewModel
            {
                Items = filtered,
                SelectedStatus = status ?? "",
                Search = search ?? "",
                PendingCount = budgets.Count(b => b.Status == "Pending Approval"),
                ApprovedCount = budgets.Count(b => b.Status == "Waiting for PM Approval" || b.Status == "Approved"),
                RejectedCount = budgets.Count(b => b.Status == "Rejected"),
                TotalBudget = budgets.Sum(b => b.TotalBudget)
            };
        }

        [HttpPost]
        public async Task<IActionResult> ApproveBudget(int id)
        {
            await _budgetService.ApproveFinanceAsync(id, Auth.CurrentDisplayName);
            return RedirectToAction(nameof(ProjectBudget));
        }

        [HttpPost]
        public async Task<IActionResult> RejectBudget(int id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return RedirectToAction(nameof(ProjectBudget), new { modal = "reject", id });

            await _budgetService.RejectFinanceAsync(id, reason, Auth.CurrentDisplayName);
            return RedirectToAction(nameof(ProjectBudget));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveBudget(int id)
        {
            await _budgetService.ArchiveAsync(id);
            return RedirectToAction(nameof(ProjectBudget));
        }

        // ==================================================== Cost Estimates
        public async Task<IActionResult> CostEstimates(string? status, string? search, int? view, string? modal, int? id, int? err)
        {
            var estimates = await LoadEstimatesAsync();
            var model = BuildEstimateModel(estimates, status, search);

            model.Modal = view.HasValue ? "view" : modal;
            model.Reason = "";
            model.Error = err.HasValue ? "Please provide a reason for rejection." : null;

            if (view.HasValue)
                model.SelectedItem = estimates.FirstOrDefault(e => e.EstimateId == view.Value);
            else if (id.HasValue)
                model.SelectedItem = estimates.FirstOrDefault(e => e.EstimateId == id.Value);

            if (!string.IsNullOrEmpty(modal))
                model.ActionTargetId = id ?? 0;

            return View(model);
        }

        private static FOCostEstimateViewModel BuildEstimateModel(List<CostEstimate> estimates, string? status, string? search)
        {
            var filtered = estimates.Where(e =>
                (string.IsNullOrEmpty(status) || e.Status == status) &&
                (string.IsNullOrEmpty(search) || (e.ProjectName ?? "").Contains(search, StringComparison.OrdinalIgnoreCase))
            ).OrderByDescending(e => e.CreatedDate).ToList();

            return new FOCostEstimateViewModel
            {
                Items = filtered,
                SelectedStatus = status ?? "",
                Search = search ?? "",
                PendingCount = estimates.Count(e => e.Status == "Pending Approval"),
                ApprovedCount = estimates.Count(e => e.Status == "Waiting for PM Approval" || e.Status == "Approved"),
                RejectedCount = estimates.Count(e => e.Status == "Rejected"),
                TotalCost = estimates.Sum(e => e.TotalCost)
            };
        }

        [HttpPost]
        public async Task<IActionResult> ApproveEstimate(int id)
        {
            await _estimateService.ApproveFinanceAsync(id, Auth.CurrentDisplayName);
            return RedirectToAction(nameof(CostEstimates));
        }

        [HttpPost]
        public async Task<IActionResult> RejectEstimate(int id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return RedirectToAction(nameof(CostEstimates), new { modal = "reject", id });

            await _estimateService.RejectFinanceAsync(id, reason);
            return RedirectToAction(nameof(CostEstimates));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveEstimate(int id)
        {
            await _estimateService.ArchiveAsync(id);
            return RedirectToAction(nameof(CostEstimates));
        }

        // ============================================================= Income
        public async Task<IActionResult> Income(string? category, string? status, string? dateRange, string? search,
            string? start, string? end, int? view, string? modal, int? id,
            string? fproject, string? fdesc, string? famount, string? fdate, string? fpayment, string? fref, string? fnotes, string? floc, string? cats, string? fother, string? ferr)
        {
            var incomes = await LoadIncomeAsync();
            var model = BuildIncomeModel(incomes, category, status, dateRange, search, start, end);

            model.Modal = modal;
            model.FormError = ferr;
            model.FormProject = fproject ?? "";
            model.FormDescription = fdesc ?? "";
            model.FormAmount = famount ?? "";
            model.FormDate = ParseDate(fdate);
            model.FormLocation = floc ?? "";
            model.FormPayment = fpayment ?? "";
            model.FormReference = fref ?? "";
            model.FormNotes = fnotes ?? "";
            model.FormOther = fother ?? "";
            model.FormCategories = SplitCategories(cats);

            if (modal == "view" && view.HasValue)
                model.ViewItem = incomes.FirstOrDefault(i => i.Id == view.Value);

            if (modal == "archive" && id.HasValue)
                model.ActionTargetId = id.Value;

            if (modal == "edit")
            {
                model.FormEditId = id;
                var item = incomes.FirstOrDefault(i => i.Id == id);
                if (item != null && item.Status != "Received")
                {
                    model.FormProject = item.Project;
                    model.FormLocation = item.Location;
                    model.FormDescription = item.Description;
                    model.FormAmount = FormatCost(item.Amount);
                    model.FormDate = item.TransactionDate;
                    model.FormPayment = item.PaymentMethod;
                    model.FormReference = item.ReferenceNumber;
                    model.FormNotes = item.Notes;
                    model.FormCategories = item.Category.Split(", ").Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                }
            }

            return View(model);
        }

        private static FOIncomeViewModel BuildIncomeModel(List<PM_IncomeTransactions> incomes, string? category, string? status,
            string? dateRange, string? search, string? start, string? end)
        {
            var range = string.IsNullOrEmpty(dateRange) ? "all" : dateRange;
            var customStart = ParseDate(start);
            var customEnd = ParseDate(end);

            var query = incomes.AsEnumerable();
            if (!string.IsNullOrEmpty(category)) query = query.Where(i => i.Category == category);
            if (!string.IsNullOrEmpty(status)) query = query.Where(i => i.Status == status);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(i =>
                    (i.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (i.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (i.ReferenceNumber ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
            query = query.Where(i => MatchesDateRange(i.TransactionDate, range, customStart, customEnd));

            var filtered = query.OrderByDescending(i => i.CreatedAt).ToList();

            return new FOIncomeViewModel
            {
                Items = filtered,
                AllIncomes = incomes,
                SelectedCategory = category ?? "",
                SelectedStatus = status ?? "",
                SelectedDateRange = range,
                Search = search ?? "",
                CustomStart = customStart,
                CustomEnd = customEnd,
                TotalAmount = filtered.Sum(i => i.Amount),
                TotalRecords = incomes.Count,
                ThisMonthCount = incomes.Count(i => i.TransactionDate.Year == DateTime.Today.Year && i.TransactionDate.Month == DateTime.Today.Month)
            };
        }

        [HttpPost]
        public async Task<IActionResult> CreateIncome(string? project, string? location, string? desc, string? amount, string? date,
            string? payment, string? refno, string? notes, string[]? cats, string? other)
        {
            var category = BuildCategory(cats, other);
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            var error = ValidateIncomeFields(project, category, desc, parsedAmount, parsedDate);
            if (error != null)
                return RedirectToIncomeError("new", null, error, project, location, desc, amount, date, payment, refno, notes, cats, other);

            var incomes = await LoadIncomeAsync();
            if (!string.IsNullOrWhiteSpace(refno) &&
                incomes.Any(i => !string.IsNullOrWhiteSpace(i.ReferenceNumber) && i.ReferenceNumber.Equals(refno.Trim(), StringComparison.OrdinalIgnoreCase)))
                return RedirectToIncomeError("new", null, "An income record with this reference number already exists.", project, location, desc, amount, date, payment, refno, notes, cats, other);

            var item = new PM_IncomeTransactions
            {
                Project = project ?? "",
                Location = location ?? "",
                Category = category,
                Description = desc ?? "",
                Amount = parsedAmount,
                Status = "Pending",
                TransactionDate = parsedDate ?? DateTime.Now,
                PaymentMethod = payment ?? "",
                ReferenceNumber = refno ?? "",
                Notes = notes ?? ""
            };

            _ = await _incomeService.CreateAsync(item);
            return RedirectToAction(nameof(Income));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateIncome(int id, string? project, string? location, string? desc, string? amount, string? date,
            string? payment, string? refno, string? notes, string[]? cats, string? other)
        {
            var existing = (await LoadIncomeAsync()).FirstOrDefault(i => i.Id == id);
            if (existing == null || existing.Status == "Received")
                return RedirectToAction(nameof(Income));

            var category = BuildCategory(cats, other);
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            var error = ValidateIncomeFields(project, category, desc, parsedAmount, parsedDate);
            if (error != null)
                return RedirectToIncomeError("edit", id, error, project, location, desc, amount, date, payment, refno, notes, cats, other);

            existing.Project = project ?? "";
            existing.Location = location ?? "";
            existing.Category = category;
            existing.Description = desc ?? "";
            existing.Amount = parsedAmount;
            existing.TransactionDate = parsedDate ?? existing.TransactionDate;
            existing.PaymentMethod = payment ?? "";
            existing.ReferenceNumber = refno ?? "";
            existing.Notes = notes ?? "";

            _ = await _incomeService.UpdateAsync(existing);
            return RedirectToAction(nameof(Income));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveIncome(int id)
        {
            await _incomeService.ArchiveAsync(id);
            return RedirectToAction(nameof(Income));
        }

        private static string? ValidateIncomeFields(string? project, string category, string? desc, decimal amount, DateTime? date)
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(desc) || amount <= 0 || date == null)
                return "Project, Category, Description, Amount, and Date are required.";
            return null;
        }

        private RedirectToActionResult RedirectToIncomeError(string modal, int? editId, string error, string? project, string? location, string? desc,
            string? amount, string? date, string? payment, string? refno, string? notes, string[]? cats, string? other)
        {
            var values = new Dictionary<string, string?>
            {
                ["modal"] = modal,
                ["ferr"] = error,
                ["fproject"] = project,
                ["floc"] = location,
                ["fdesc"] = desc,
                ["famount"] = amount,
                ["fdate"] = NormalizeDate(date),
                ["fpayment"] = payment,
                ["fref"] = refno,
                ["fnotes"] = notes,
                ["cats"] = cats != null ? string.Join(",", cats) : null,
                ["fother"] = other
            };
            if (editId.HasValue) values["id"] = editId.Value.ToString();

            var route = new Dictionary<string, string?>();
            foreach (var kv in values)
                if (!string.IsNullOrEmpty(kv.Value))
                    route[kv.Key] = kv.Value;

            return RedirectToAction(nameof(Income), route);
        }

        // ============================================================ Expenses
        public async Task<IActionResult> Expenses(string? category, string? dateRange, string? search,
            string? start, string? end, int? view, string? modal, int? id,
            string? fproject, string? fdesc, string? famount, string? fdate, string? floc, string? cats, string? fother, string? ferr)
        {
            var expenses = await LoadExpensesAsync();
            var model = BuildExpenseModel(expenses, category, dateRange, search, start, end);

            model.Modal = modal;
            model.FormError = ferr;
            model.FormProject = fproject ?? "";
            model.FormDescription = fdesc ?? "";
            model.FormAmount = famount ?? "";
            model.FormDate = ParseDate(fdate);
            model.FormLocation = floc ?? "";
            model.FormOther = fother ?? "";
            model.FormCategories = SplitCategories(cats);

            if (modal == "view" && view.HasValue)
            {
                model.ViewItem = expenses.FirstOrDefault(e => e.Id == view.Value);
                if (model.ViewItem != null)
                    model.SubmitterName = ResolveDisplayName(await _expenseService.ResolveSubmitterEmailAsync(model.ViewItem.UserId, model.ViewItem.AdminEmail));
            }

            if (modal == "archive" && id.HasValue)
                model.ActionTargetId = id.Value;

            if (modal == "edit")
            {
                model.FormEditId = id;
                var item = expenses.FirstOrDefault(e => e.Id == id);
                if (item != null && item.Status != "Approved" && item.Status != "Rejected")
                {
                    model.FormProject = item.Project;
                    model.FormDescription = item.Description;
                    model.FormAmount = FormatCost(item.Amount);
                    model.FormDate = item.ExpenseDate;
                    model.FormCategories = item.Category.Split(", ").Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                }
            }

            return View(model);
        }

        private static FOExpenseViewModel BuildExpenseModel(List<Expense> expenses, string? category, string? dateRange,
            string? search, string? start, string? end)
        {
            var range = string.IsNullOrEmpty(dateRange) ? "all" : dateRange;
            var customStart = ParseDate(start);
            var customEnd = ParseDate(end);

            var query = expenses.AsEnumerable();
            if (!string.IsNullOrEmpty(category)) query = query.Where(e => e.Category == category);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(e =>
                    (e.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Category ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
            query = query.Where(e => MatchesDateRange(e.ExpenseDate, range, customStart, customEnd));

            var filtered = query.OrderByDescending(e => e.CreatedAt).ToList();

            return new FOExpenseViewModel
            {
                Items = filtered,
                AllExpenses = expenses,
                SelectedCategory = category ?? "",
                SelectedDateRange = range,
                Search = search ?? "",
                CustomStart = customStart,
                CustomEnd = customEnd,
                TotalAmount = filtered.Sum(e => e.Amount),
                TotalRecords = expenses.Count,
                ThisMonthCount = expenses.Count(e => e.ExpenseDate.Year == DateTime.Today.Year && e.ExpenseDate.Month == DateTime.Today.Month)
            };
        }

        [HttpPost]
        public async Task<IActionResult> CreateExpense(string? project, string? desc, string? amount, string? date, string? location, string[]? cats, string? other)
        {
            var category = BuildCategory(cats, other);
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(category) || parsedAmount <= 0 || parsedDate == null)
                return RedirectToExpenseError("new", null, "Project, Category, Amount, and Date are required.", project, desc, amount, date, cats, other, location);

            var expenses = await LoadExpensesAsync();
            if (!string.IsNullOrWhiteSpace(desc) &&
                expenses.Any(e => e.Project.Equals(project.Trim(), StringComparison.OrdinalIgnoreCase)
                               && e.Description.Equals(desc.Trim(), StringComparison.OrdinalIgnoreCase)
                               && e.Amount == parsedAmount))
                return RedirectToExpenseError("new", null, "An expense with the same description, project, and amount already exists.", project, desc, amount, date, cats, other, location);

            var item = new Expense
            {
                Project = project ?? "",
                Location = location ?? "",
                Category = category,
                Description = desc ?? "",
                Amount = parsedAmount,
                Status = "Pending",
                ExpenseDate = parsedDate ?? DateTime.Now
            };

            _ = await _expenseService.CreateAsync(item);
            return RedirectToAction(nameof(Expenses));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateExpense(int id, string? project, string? desc, string? amount, string? date, string[]? cats, string? other)
        {
            var existing = (await LoadExpensesAsync()).FirstOrDefault(e => e.Id == id);
            if (existing == null || existing.Status == "Approved" || existing.Status == "Rejected")
                return RedirectToAction(nameof(Expenses));

            var category = BuildCategory(cats, other);
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(category) || parsedAmount <= 0 || parsedDate == null)
                return RedirectToExpenseError("edit", id, "Project, Category, Amount, and Date are required.", project, desc, amount, date, cats, other);

            existing.Project = project ?? "";
            existing.Category = category;
            existing.Description = desc ?? "";
            existing.Amount = parsedAmount;
            existing.ExpenseDate = parsedDate ?? existing.ExpenseDate;

            _ = await _expenseService.UpdateAsync(existing);
            return RedirectToAction(nameof(Expenses));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveExpense(int id)
        {
            await _expenseService.ArchiveAsync(id);
            return RedirectToAction(nameof(Expenses));
        }

        private static string ResolveDisplayName(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return "-";
            try
            {
                var user = PlanCoreMAUI.Components.Pages.Admin.Users.users
                    .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Email) && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
                if (user != null && !string.IsNullOrWhiteSpace(user.FullName)) return user.FullName;
            }
            catch { }
            return email!;
        }

        private RedirectToActionResult RedirectToExpenseError(string modal, int? editId, string error, string? project, string? desc,
            string? amount, string? date, string[]? cats, string? other, string? location = null)
        {
            var route = new Dictionary<string, string?>
            {
                ["modal"] = modal,
                ["ferr"] = error,
                ["fproject"] = project,
                ["fdesc"] = desc,
                ["famount"] = amount,
                ["fdate"] = NormalizeDate(date),
                ["floc"] = location,
                ["cats"] = cats != null ? string.Join(",", cats) : null,
                ["fother"] = other
            };
            if (editId.HasValue) route["id"] = editId.Value.ToString();

            return RedirectToAction(nameof(Expenses), route);
        }

        // ======================================================== Transactions
        public async Task<IActionResult> Transactions(string? type, string? category, string? dateRange, string? search,
            string? start, string? end, int? view, string? vtype, string? modal, int? id, string? atype)
        {
            var entries = BuildTransactionEntries(await LoadExpensesAsync(), await LoadIncomeAsync());
            var model = BuildTransactionModel(entries, type, category, dateRange, search, start, end);

            if (modal == "view" && view.HasValue)
            {
                var kind = vtype == "income";
                model.ViewItem = entries.FirstOrDefault(t => t.IsIncome == kind && t.SourceId == view.Value);
            }

            if (modal == "archive" && id.HasValue)
            {
                var kind = atype == "income";
                model.ArchiveId = id.Value;
                model.ArchiveType = kind ? "income" : "expense";
                model.ArchiveLabel = kind ? "income" : "expense";
            }

            return View(model);
        }

        private static List<FOTransactionEntry> BuildTransactionEntries(List<Expense> expenses, List<PM_IncomeTransactions> incomes)
        {
            var list = new List<FOTransactionEntry>();
            foreach (var e in expenses)
            {
                list.Add(new FOTransactionEntry
                {
                    SourceId = e.Id,
                    IsIncome = false,
                    Date = e.ExpenseDate,
                    Project = e.Project,
                    Description = e.Description,
                    Category = e.Category,
                    Amount = e.Amount,
                    Location = e.Location,
                    Status = e.Status,
                    Notes = e.Notes,
                    PaymentMethod = null,
                    ReferenceNumber = null
                });
            }
            foreach (var i in incomes)
            {
                list.Add(new FOTransactionEntry
                {
                    SourceId = i.Id,
                    IsIncome = true,
                    Date = i.TransactionDate,
                    Project = i.Project,
                    Description = i.Description,
                    Category = i.Category,
                    Amount = i.Amount,
                    Location = null,
                    Status = i.Status,
                    Notes = i.Notes,
                    PaymentMethod = i.PaymentMethod,
                    ReferenceNumber = i.ReferenceNumber
                });
            }
            return list;
        }

        private static FOTransactionViewModel BuildTransactionModel(List<FOTransactionEntry> entries, string? type, string? category,
            string? dateRange, string? search, string? start, string? end)
        {
            var range = dateRange ?? "thismonth";
            var customStart = ParseDate(start);
            var customEnd = ParseDate(end);

            var query = entries.AsEnumerable();
            if (!string.IsNullOrEmpty(type))
                query = query.Where(t => type == "income" ? t.IsIncome : !t.IsIncome);
            if (!string.IsNullOrEmpty(category))
                query = query.Where(t => t.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(search))
                query = query.Where(t =>
                    (t.Project ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (t.Description ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (t.IdDisplay ?? "").Contains(search, StringComparison.OrdinalIgnoreCase));
            query = query.Where(t => MatchesDateRange(t.Date, range, customStart, customEnd));

            var filtered = query.OrderByDescending(t => t.Date).ToList();

            return new FOTransactionViewModel
            {
                Items = filtered,
                SelectedType = type ?? "",
                SelectedCategory = category ?? "",
                SelectedDateRange = range,
                Search = search ?? "",
                CustomStart = customStart,
                CustomEnd = customEnd,
                TotalInflow = filtered.Where(t => t.IsIncome).Sum(t => t.Amount),
                TotalOutflow = filtered.Where(t => !t.IsIncome).Sum(t => t.Amount),
                NetBalance = filtered.Where(t => t.IsIncome).Sum(t => t.Amount) - filtered.Where(t => !t.IsIncome).Sum(t => t.Amount),
                RecordCount = filtered.Count
            };
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveTransaction(int id, string type)
        {
            if (type == "income")
                await _incomeService.ArchiveAsync(id);
            else
                await _expenseService.ArchiveAsync(id);
            return RedirectToAction(nameof(Transactions));
        }

        // ============================================================ CashFlow
        public async Task<IActionResult> CashFlow(string? type, string? category, string? dateRange, string? search,
            string? start, string? end, int? view, string? modal, int? id, string? source, string? err,
            string? fproject, string? ftype, string? fcategory, string? famount, string? fdate, string? fdesc, string? fref, string? fnotes)
        {
            var budgets = await LoadBudgetsAsync();
            var entries = BuildCashFlowEntries(
                await LoadIncomeAsync(), await LoadExpensesAsync(), await LoadCashFlowsAsync(), budgets);
            var model = BuildCashFlowModel(entries, budgets, type, category, dateRange, search, start, end);

            model.Modal = modal;
            model.AddError = err;

            if (modal == "view" && view.HasValue)
                model.ViewItem = entries.FirstOrDefault(e => e.SourceId == view.Value);

            if (modal == "archive")
            {
                model.ArchiveId = id ?? 0;
                model.ArchiveSource = source ?? "";
                if (id.HasValue && !string.IsNullOrEmpty(source))
                {
                    var t = entries.FirstOrDefault(e => e.Source == source && e.SourceId == id.Value);
                    model.ArchiveDescription = t?.Description ?? "";
                }
            }

            if (modal == "add" || modal == "edit")
            {
                model.FormProject = fproject ?? "";
                model.FormType = string.IsNullOrEmpty(ftype) ? "Inflow" : ftype;
                model.FormCategory = fcategory ?? "";
                model.FormAmount = famount ?? "";
                model.FormDate = ParseDate(fdate);
                model.FormDescription = fdesc ?? "";
                model.FormReference = fref ?? "";
                model.FormNotes = fnotes ?? "";
                if (modal == "edit")
                {
                    model.FormEditId = id;
                    var target = entries.FirstOrDefault(e => e.Source == "CashFlow" && e.SourceId == id);
                    if (target != null)
                    {
                        model.FormProject = target.Project;
                        model.FormType = target.IsInflow ? "Inflow" : "Outflow";
                        model.FormCategory = target.Category;
                        model.FormAmount = target.Amount.ToString("N0");
                        model.FormDate = target.Date;
                        model.FormDescription = target.Description;
                        model.FormReference = target.ReferenceNumber;
                        model.FormNotes = target.Notes;
                    }
                }
            }

            return View(model);
        }

        private static List<FOCashFlowEntry> BuildCashFlowEntries(List<PM_IncomeTransactions> incomes, List<Expense> expenses,
            List<CashFlow> cashFlows, List<QS_Budget> budgets)
        {
            var entries = new List<FOCashFlowEntry>();
            foreach (var inc in incomes)
                entries.Add(new FOCashFlowEntry
                {
                    SourceId = inc.Id,
                    Source = "Income",
                    IsInflow = true,
                    Date = inc.TransactionDate,
                    Project = inc.Project,
                    Description = inc.Description,
                    Category = inc.Category,
                    Amount = inc.Amount,
                    ReferenceNumber = inc.ReferenceNumber,
                    Notes = inc.Notes,
                    IdDisplay = $"INC-{inc.Id:D3}"
                });
            foreach (var exp in expenses)
                entries.Add(new FOCashFlowEntry
                {
                    SourceId = exp.Id,
                    Source = "Expense",
                    IsInflow = false,
                    Date = exp.ExpenseDate,
                    Project = exp.Project,
                    Description = exp.Description,
                    Category = exp.Category,
                    Amount = exp.Amount,
                    ReferenceNumber = "",
                    Notes = exp.Notes ?? "",
                    IdDisplay = $"EXP-{exp.Id:D3}"
                });
            foreach (var cf in cashFlows)
                entries.Add(new FOCashFlowEntry
                {
                    SourceId = cf.Id,
                    Source = "CashFlow",
                    IsInflow = cf.Type == "Inflow",
                    Date = cf.TransactionDate,
                    Project = cf.Project,
                    Description = cf.Description,
                    Category = cf.Category,
                    Amount = cf.Amount,
                    ReferenceNumber = cf.ReferenceNumber,
                    Notes = cf.Notes,
                    IdDisplay = $"CF-{cf.Id:D3}"
                });
            return entries.OrderByDescending(e => e.Date).ToList();
        }

        private static FOCashFlowViewModel BuildCashFlowModel(List<FOCashFlowEntry> entries, List<QS_Budget> budgets, string? type, string? category,
            string? dateRange, string? search, string? start, string? end)
        {
            var range = dateRange ?? "";
            var customStart = ParseDate(start);
            var customEnd = ParseDate(end);

            var query = entries.AsEnumerable();
            if (type == "Inflow") query = query.Where(e => e.IsInflow);
            else if (type == "Outflow") query = query.Where(e => !e.IsInflow);
            if (!string.IsNullOrEmpty(category) && category != "All") query = query.Where(e => e.Category == category);
            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLowerInvariant();
                query = query.Where(e =>
                    (e.Description ?? "").ToLowerInvariant().Contains(s) ||
                    (e.Project ?? "").ToLowerInvariant().Contains(s) ||
                    (e.Category ?? "").ToLowerInvariant().Contains(s) ||
                    (e.IdDisplay ?? "").ToLowerInvariant().Contains(s));
            }
            query = query.Where(e => MatchesDateRange(e.Date, range, customStart, customEnd));

            var filtered = query.OrderByDescending(e => e.Date).ToList();

            var model = new FOCashFlowViewModel
            {
                Entries = entries,
                FilteredEntries = filtered,
                DistinctCategories = entries.Select(e => e.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList(),
                SelectedType = string.IsNullOrEmpty(type) ? "All" : type,
                SelectedCategory = string.IsNullOrEmpty(category) ? "All" : category,
                SelectedDateRange = range,
                Search = search ?? "",
                CustomStart = customStart,
                CustomEnd = customEnd,
                TotalInflow = entries.Where(e => e.IsInflow).Sum(e => e.Amount),
                TotalOutflow = entries.Where(e => !e.IsInflow).Sum(e => e.Amount),
                NetBalance = entries.Where(e => e.IsInflow).Sum(e => e.Amount) - entries.Where(e => !e.IsInflow).Sum(e => e.Amount),
                RecordCount = entries.Count
            };

            var budgetLookup = BuildBudgetLookup(budgets);
            model.ProjectSummaries = entries
                .GroupBy(e => e.Project)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new FOCashFlowProjectSummary
                {
                    ProjectName = g.Key,
                    TotalInflow = g.Where(x => x.IsInflow).Sum(x => x.Amount),
                    TotalOutflow = g.Where(x => !x.IsInflow).Sum(x => x.Amount),
                    Budget = budgetLookup.FirstOrDefault(b => b.Project.Equals(g.Key, StringComparison.OrdinalIgnoreCase)) is var b && b != null ? b : null
                })
                .OrderByDescending(p => p.Net)
                .ToList();

            model.MonthlyTrends = entries
                .GroupBy(e => new { e.Date.Year, e.Date.Month })
                .Select(g => new FOCashFlowMonthlyTrend
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM"),
                    Inflow = g.Where(x => x.IsInflow).Sum(x => x.Amount),
                    Outflow = g.Where(x => !x.IsInflow).Sum(x => x.Amount)
                })
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .ToList();

            return model;
        }

        private static List<FOCashFlowBudgetSummary> BuildBudgetLookup(List<QS_Budget> budgets)
        {
            return budgets.Select(b => new FOCashFlowBudgetSummary
            {
                Project = b.Project,
                TotalBudget = b.TotalBudget,
                TotalSpent = b.TotalSpent,
                Remaining = b.Remaining,
                Health = b.Health
            }).ToList();
        }

        [HttpPost]
        public async Task<IActionResult> AddCashFlow(string? project, string? type, string? category, string? amount,
            string? date, string? desc, string? refno, string? notes)
        {
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            if (string.IsNullOrWhiteSpace(desc) || parsedAmount <= 0)
                return RedirectToCashFlowError("add", null, "Description and a valid amount are required.", project, type, category, amount, date, desc, refno, notes);

            var entries = BuildCashFlowEntries(
                await LoadIncomeAsync(), await LoadExpensesAsync(), await LoadCashFlowsAsync(), await LoadBudgetsAsync());

            if (entries.Any(e => e.Description.Equals(desc.Trim(), StringComparison.OrdinalIgnoreCase)
                              && e.Amount == parsedAmount
                              && e.Project.Equals((project ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                return RedirectToCashFlowError("add", null, "A cash flow entry with the same description, amount, and project already exists.", project, type, category, amount, date, desc, refno, notes);

            var item = new CashFlow
            {
                Project = project ?? "",
                Type = type == "Outflow" ? "Outflow" : "Inflow",
                Category = category ?? "",
                Amount = parsedAmount,
                TransactionDate = parsedDate ?? DateTime.Now,
                Description = desc ?? "",
                ReferenceNumber = refno ?? "",
                Notes = notes ?? ""
            };

            _ = await _cashFlowService.CreateAsync(item);
            return RedirectToAction(nameof(CashFlow));
        }

        [HttpPost]
        public async Task<IActionResult> EditCashFlow(int id, string? project, string? type, string? category, string? amount,
            string? date, string? desc, string? refno, string? notes)
        {
            var parsedAmount = ParseCostInput(amount);
            var parsedDate = ParseDate(date);

            if (string.IsNullOrWhiteSpace(desc) || parsedAmount <= 0 || id <= 0)
                return RedirectToCashFlowError("edit", id, "Description and a valid amount are required.", project, type, category, amount, date, desc, refno, notes);

            var existing = (await LoadCashFlowsAsync()).FirstOrDefault(c => c.Id == id);
            if (existing == null)
                return RedirectToAction(nameof(CashFlow));

            existing.Project = project ?? "";
            existing.Type = type == "Outflow" ? "Outflow" : "Inflow";
            existing.Category = category ?? "";
            existing.Amount = parsedAmount;
            existing.TransactionDate = parsedDate ?? existing.TransactionDate;
            existing.Description = desc ?? "";
            existing.ReferenceNumber = refno ?? "";
            existing.Notes = notes ?? "";

            _ = await _cashFlowService.UpdateAsync(existing);
            return RedirectToAction(nameof(CashFlow));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveCashFlow(int id, string source)
        {
            if (source == "Income") await _incomeService.ArchiveAsync(id);
            else if (source == "Expense") await _expenseService.ArchiveAsync(id);
            else await _cashFlowService.ArchiveAsync(id);
            return RedirectToAction(nameof(CashFlow));
        }

        private RedirectToActionResult RedirectToCashFlowError(string modal, int? editId, string error, string? project, string? type,
            string? category, string? amount, string? date, string? desc, string? refno, string? notes)
        {
            var route = new Dictionary<string, string?>
            {
                ["modal"] = modal,
                ["err"] = error,
                ["fproject"] = project,
                ["ftype"] = type,
                ["fcategory"] = category,
                ["famount"] = amount,
                ["fdate"] = NormalizeDate(date),
                ["fdesc"] = desc,
                ["fref"] = refno,
                ["fnotes"] = notes
            };
            if (editId.HasValue) route["id"] = editId.Value.ToString();

            return RedirectToAction(nameof(CashFlow), route);
        }

        // ============================================================= Reports
        public async Task<IActionResult> Reports(string? section, string? status, string? dateRange, string? search, string? start, string? end)
        {
            var model = await BuildReportsModelAsync(section, status, dateRange, search, start, end);
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> ExportPdf(string? section, string? status, string? dateRange, string? search, string? start, string? end)
        {
            var model = await BuildReportsModelAsync(section, status, dateRange, search, start, end);

            var pdfData = _pdfExport.GenerateFOReport(
                "Finance Officer",
                DateTime.Now,
                model.IncomeItems,
                model.ExpenseItems,
                model.BudgetItems,
                model.ProfitabilityItems,
                model.CashFlowItems,
                model.EstimateItems);

            var fileName = $"FOReports_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            return File(pdfData, "application/pdf", fileName);
        }

        private async Task<FOReportsViewModel> BuildReportsModelAsync(string? section, string? status, string? dateRange,
            string? search, string? start, string? end)
        {
            var sec = section ?? "All";
            var statusFilter = status ?? "All";
            var range = dateRange ?? "all";
            var searchQuery = search ?? "";
            var customStart = ParseDate(start);
            var customEnd = ParseDate(end);

            var expenses = await LoadExpensesAsync();
            var incomes = await LoadIncomeAsync();
            var budgets = await LoadBudgetsAsync();
            var estimates = await LoadEstimatesAsync();

            var filteredExpenses = expenses.Where(e =>
                (statusFilter == "All" || e.Status == statusFilter) &&
                (string.IsNullOrEmpty(searchQuery) || e.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) &&
                ReportsDateInRange(e.ExpenseDate, range, customStart, customEnd)
            ).ToList();

            var filteredIncome = incomes.Where(i =>
                (statusFilter == "All" || i.Status == statusFilter) &&
                (string.IsNullOrEmpty(searchQuery) || i.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) &&
                ReportsDateInRange(i.TransactionDate, range, customStart, customEnd)
            ).ToList();

            var expenseSearch = expenses.Where(e => string.IsNullOrEmpty(searchQuery) || e.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();
            var expenseSums = expenseSearch.GroupBy(e => e.Project).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

            var budgetItems = budgets
                .Where(b => string.IsNullOrEmpty(searchQuery) || b.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .Select(b => new BudgetSummary
                {
                    Project = b.Project,
                    Budget = b.TotalBudget,
                    Spent = expenseSums.GetValueOrDefault(b.Project, 0),
                    Remaining = b.TotalBudget - expenseSums.GetValueOrDefault(b.Project, 0),
                    Health = b.Health,
                    Status = b.Status
                }).ToList();

            var incomeSums = incomes.Where(i => string.IsNullOrEmpty(searchQuery) || i.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .GroupBy(i => i.Project).ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));

            var allProjects = incomeSums.Keys.Union(expenseSums.Keys);
            var profitabilityItems = allProjects.Select(p => new ProjectProfitability
            {
                Project = p,
                Income = incomeSums.GetValueOrDefault(p, 0),
                Expenses = expenseSums.GetValueOrDefault(p, 0)
            }).OrderBy(p => p.Project).ToList();

            var cashFlowItems = new Dictionary<string, MonthlyCashFlow>();
            foreach (var inc in incomes.Where(i => string.IsNullOrEmpty(searchQuery) || i.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)))
            {
                var key = inc.TransactionDate.ToString("yyyy-MM");
                if (!cashFlowItems.ContainsKey(key))
                    cashFlowItems[key] = new MonthlyCashFlow { Period = inc.TransactionDate.ToString("MMM yyyy") };
                cashFlowItems[key].Inflow += inc.Amount;
            }
            foreach (var exp in expenses.Where(e => string.IsNullOrEmpty(searchQuery) || e.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)))
            {
                var key = exp.ExpenseDate.ToString("yyyy-MM");
                if (!cashFlowItems.ContainsKey(key))
                    cashFlowItems[key] = new MonthlyCashFlow { Period = exp.ExpenseDate.ToString("MMM yyyy") };
                cashFlowItems[key].Outflow += exp.Amount;
            }
            var monthlyCashFlow = cashFlowItems.Values.OrderBy(m => m.Period).ToList();

            var allExpenseSums = expenses.GroupBy(e => e.Project).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
            var estimateItems = estimates
                .Where(e => string.IsNullOrEmpty(searchQuery) || e.ProjectName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .Select(e => new EstimateVsActual
                {
                    Project = e.ProjectName,
                    Estimated = e.TotalCost,
                    Actual = allExpenseSums.GetValueOrDefault(e.ProjectName, 0),
                    Status = e.Status
                }).ToList();

            var model = new FOReportsViewModel
            {
                Section = sec,
                StatusFilter = statusFilter,
                Search = searchQuery,
                SelectedDateRange = range,
                CustomStart = customStart,
                CustomEnd = customEnd,
                TotalIncome = incomes.Sum(i => i.Amount),
                TotalExpenses = expenses.Sum(e => e.Amount),
                NetBalance = incomes.Sum(i => i.Amount) - expenses.Sum(e => e.Amount),
                RecordCount = incomes.Count + expenses.Count,
                CurrentSectionTitle = GetReportSectionTitle(sec),
                IncomeItems = filteredIncome,
                ExpenseItems = filteredExpenses,
                BudgetItems = budgetItems,
                ProfitabilityItems = profitabilityItems,
                CashFlowItems = monthlyCashFlow,
                EstimateItems = estimateItems
            };

            model.CurrentItemCount = sec switch
            {
                "Income" => filteredIncome.Count,
                "Expenses" => filteredExpenses.Count,
                "Budget" => budgetItems.Count,
                "Profitability" => profitabilityItems.Count,
                "CashFlow" => monthlyCashFlow.Count,
                "Estimates" => estimateItems.Count,
                _ => filteredIncome.Count + filteredExpenses.Count + budgetItems.Count + profitabilityItems.Count + monthlyCashFlow.Count + estimateItems.Count
            };

            return model;
        }

        private static string GetReportSectionTitle(string section)
        {
            return section switch
            {
                "Income" => "Income Transactions",
                "Expenses" => "Expense Transactions",
                "Budget" => "Budget vs Actual",
                "Profitability" => "Project Profitability",
                "CashFlow" => "Cash Flow Trends",
                "Estimates" => "Estimate vs Actual",
                _ => "All Financial Reports"
            };
        }

        private static bool ReportsDateInRange(DateTime date, string range, DateTime? customStart, DateTime? customEnd)
        {
            var today = DateTime.Today;
            return range switch
            {
                "today" => date.Date == today,
                "thisweek" => date.Date >= StartOfWeekMonday(today) && date.Date < StartOfWeekMonday(today).AddDays(7),
                "thismonth" => date.Year == today.Year && date.Month == today.Month,
                "thisyear" => date.Year == today.Year,
                "customrange" => (!customStart.HasValue || date >= customStart.Value) &&
                                 (!customEnd.HasValue || date <= customEnd.Value.AddDays(1)),
                _ => true
            };
        }

        // ============================================================= MyTasks
        public async Task<IActionResult> Tasks(string? date, string? status, string? search, int? view, string? modal, int? id, string? name)
        {
            var tasks = await LoadTasksAsync();
            var model = BuildTasksModel(tasks, date, status, search);

            model.Modal = modal;
            if (modal == "view" && view.HasValue)
                model.SelectedItem = tasks.FirstOrDefault(t => t.Id == view.Value);
            if (modal == "archive" && id.HasValue)
            {
                model.ActionTargetId = id.Value;
                model.ActionTargetName = name ?? tasks.FirstOrDefault(t => t.Id == id.Value)?.Name ?? "";
            }

            return View(model);
        }

        private static FOMyTasksViewModel BuildTasksModel(List<PM_Tasks> tasks, string? date, string? status, string? search)
        {
            var dateFilter = date ?? "All";
            var statusFilter = status ?? "";
            var searchQuery = search ?? "";

            var filtered = tasks.Where(t =>
                (string.IsNullOrEmpty(statusFilter) || t.Status == statusFilter) &&
                (string.IsNullOrEmpty(searchQuery) || t.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) || t.Project.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) &&
                (dateFilter == "All" || TaskDateMatches(t.DueDate, dateFilter))
            ).ToList();

            return new FOMyTasksViewModel
            {
                Items = filtered,
                AllTasks = tasks,
                DateFilter = dateFilter,
                StatusFilter = statusFilter,
                Search = searchQuery,
                Total = tasks.Count,
                PendingCount = tasks.Count(t => t.Status == "Pending"),
                InProgressCount = tasks.Count(t => t.Status == "In Progress"),
                CompletedCount = tasks.Count(t => t.Status == "Completed")
            };
        }

        private static bool TaskDateMatches(DateTime? date, string dateFilter)
        {
            if (!date.HasValue) return false;
            var today = DateTime.Today;
            return dateFilter switch
            {
                "Today" => date.Value.Date == today,
                "This Week" => date.Value.Date >= today && date.Value.Date <= today.AddDays(7),
                "This Month" => date.Value.Month == today.Month && date.Value.Year == today.Year,
                "This Year" => date.Value.Year == today.Year,
                _ => true
            };
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus(int id)
        {
            var task = (await LoadTasksAsync()).FirstOrDefault(t => t.Id == id);
            if (task != null && task.Status != "Completed")
            {
                task.Status = task.Status == "Pending" ? "In Progress" : "Completed";
                _ = await _taskService.UpdateTaskAsync(task);
            }
            return RedirectToAction(nameof(Tasks));
        }

        [HttpPost]
        public async Task<IActionResult> ArchiveTask(int id)
        {
            _ = await _taskService.ArchiveTaskAsync(id);
            return RedirectToAction(nameof(Tasks));
        }

        // ============================================================ Archived
        public async Task<IActionResult> Archived(string? type, string? search, int? view, string? vtype, string? modal, int? id, string? rtype, string? dtype)
        {
            var items = BuildArchivedItems(await LoadArchivedExpensesAsync(), await LoadArchivedIncomeAsync());
            var model = BuildArchivedModel(items, type, search);

            model.Modal = modal;
            if (modal == "view" && view.HasValue)
            {
                var target = items.FirstOrDefault(a => a.OriginalId == view.Value && a.OriginalType == (vtype ?? ""));
                if (target != null)
                {
                    model.Title = $"{target.Type} - {target.DisplayId}";
                    model.ViewExpense = target.RefExpense;
                    model.ViewIncome = target.RefIncome;
                }
            }
            if (modal == "restore" && id.HasValue)
            {
                model.RestoreId = id.Value;
                model.RestoreType = rtype ?? "";
            }
            if (modal == "delete" && id.HasValue)
            {
                model.DeleteId = id.Value;
                model.DeleteType = dtype ?? "";
            }

            return View(model);
        }

        private static List<FOArchivedItem> BuildArchivedItems(List<Expense> expenses, List<PM_IncomeTransactions> incomes)
        {
            var items = new List<FOArchivedItem>();
            foreach (var e in expenses)
            {
                items.Add(new FOArchivedItem
                {
                    Type = "Expense",
                    DisplayId = $"EXP-{e.Id:D3}",
                    Project = e.Project ?? "-",
                    Details = e.Category ?? "-",
                    AmountDisplay = $"-{e.Amount:N0}",
                    AmountClass = "text-red",
                    Status = e.Status ?? "-",
                    StatusClass = GetExpenseStatusClass(e.Status),
                    ArchivedOnDisplay = e.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = e.Id,
                    OriginalType = "expense",
                    RefExpense = e
                });
            }
            foreach (var i in incomes)
            {
                items.Add(new FOArchivedItem
                {
                    Type = "Income",
                    DisplayId = $"INC-{i.Id:D3}",
                    Project = i.Project ?? "-",
                    Details = i.Description ?? "-",
                    AmountDisplay = $"+{i.Amount:N0}",
                    AmountClass = "text-green",
                    Status = i.Status ?? "-",
                    StatusClass = GetIncomeStatusClass(i.Status),
                    ArchivedOnDisplay = i.ArchivedOn?.ToString("MMM d, yyyy") ?? "-",
                    OriginalId = i.Id,
                    OriginalType = "income",
                    RefIncome = i
                });
            }
            return items.OrderByDescending(a => a.ArchivedOnDisplay).ToList();
        }

        private static FOArchivedViewModel BuildArchivedModel(List<FOArchivedItem> items, string? type, string? search)
        {
            var selectedType = type ?? "all";
            var query = items.AsEnumerable();
            if (selectedType != "all")
                query = query.Where(a => a.OriginalType == selectedType);
            if (!string.IsNullOrEmpty(search))
                query = query.Where(a =>
                    a.Project.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    a.DisplayId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    a.Details.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    a.Type.Contains(search, StringComparison.OrdinalIgnoreCase));

            return new FOArchivedViewModel
            {
                Items = query.ToList(),
                SelectedType = selectedType,
                Search = search ?? ""
            };
        }

        [HttpPost]
        public async Task<IActionResult> RestoreItem(int id, string type)
        {
            if (type == "expense") await _expenseService.RestoreAsync(id);
            else if (type == "income") await _incomeService.RestoreAsync(id);
            return RedirectToAction(nameof(Archived));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteItem(int id, string type)
        {
            if (type == "expense") await _expenseService.DeleteAsync(id);
            else if (type == "income") await _incomeService.DeleteAsync(id);
            return RedirectToAction(nameof(Archived));
        }

        // ============================================================ Helpers
        private async Task<List<PM_IncomeTransactions>> LoadIncomeAsync()
        {
            try { return await _incomeService.GetAllAsync(); }
            catch { return new List<PM_IncomeTransactions>(); }
        }

        private async Task<List<PM_IncomeTransactions>> LoadArchivedIncomeAsync()
        {
            try { return await _incomeService.GetArchivedAsync(); }
            catch { return new List<PM_IncomeTransactions>(); }
        }

        private async Task<List<Expense>> LoadExpensesAsync()
        {
            try { return await _expenseService.GetAllAsync(); }
            catch { return new List<Expense>(); }
        }

        private async Task<List<Expense>> LoadArchivedExpensesAsync()
        {
            try { return await _expenseService.GetArchivedAsync(); }
            catch { return new List<Expense>(); }
        }

        private async Task<List<CashFlow>> LoadCashFlowsAsync()
        {
            try { return await _cashFlowService.GetAllAsync(); }
            catch { return new List<CashFlow>(); }
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

        private async Task<List<PM_Tasks>> LoadTasksAsync()
        {
            try { return await _taskService.GetTasksByAssignedRoleAsync("Finance"); }
            catch { return new List<PM_Tasks>(); }
        }

        private static List<string> SplitCategories(string? cats)
        {
            if (string.IsNullOrWhiteSpace(cats)) return new List<string>();
            return cats.Split(',').Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        }

        private static string BuildCategory(string[]? cats, string? other)
        {
            var joined = cats?.Where(c => c != "Other").Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
            if (cats != null && cats.Contains("Other") && !string.IsNullOrWhiteSpace(other))
                joined.Add(other.Trim());
            return string.Join(", ", joined);
        }

        private static bool MatchesDateRange(DateTime date, string range, DateTime? start, DateTime? end)
        {
            var today = DateTime.Today;
            switch (range)
            {
                case "today":
                    return date.Date == today;
                case "thisweek":
                    var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                    return date.Date >= startOfWeek && date.Date < startOfWeek.AddDays(7);
                case "thismonth":
                    return date.Year == today.Year && date.Month == today.Month;
                case "thisyear":
                    return date.Year == today.Year;
                case "customrange":
                    var from = start ?? DateTime.MinValue;
                    var to = end ?? DateTime.MaxValue;
                    return date.Date >= from && date.Date <= to;
                default:
                    return true;
            }
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

        private static string NormalizeDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return DateTime.TryParse(value, out var parsed) ? parsed.ToString("yyyy-MM-ddTHH:mm") : value;
        }

        private static decimal ParseCostInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0m;
            var cleaned = new string(input.Where(c => char.IsDigit(c) || c == '.').ToArray());
            return decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0m;
        }

        private static string FormatCost(decimal value) => value == 0m ? "" : value.ToString("N2");

        private static DateTime StartOfWeekMonday(DateTime dt)
        {
            var diff = dt.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;
            return dt.AddDays(-diff).Date;
        }

        private static string GetExpenseStatusClass(string? status) => status switch
        {
            "Approved" => "badge-green",
            "Rejected" => "badge-red",
            "Pending" => "badge-yellow",
            _ => "badge-yellow"
        };

        private static string GetIncomeStatusClass(string? status) => status switch
        {
            "Received" => "badge-green",
            "Pending" => "badge-yellow",
            "Cancelled" => "badge-red",
            _ => "badge-yellow"
        };
    }
}