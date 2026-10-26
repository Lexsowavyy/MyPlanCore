using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlanCoreMAUI;
using PlanCoreMAUI.Data;
using PlanCoreMAUI.Models;
using PlanCoreMAUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Shared environment / connection-string setup (same logic as the MAUI bootstrap).
var (connStr, _) = MauiProgram.LoadEnvironment();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(120);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// NOTE: same ServiceLifetime.Transient used by the MAUI app.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(connStr);
}, ServiceLifetime.Transient);

builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddSingleton<SubscriptionService>();
builder.Services.AddSingleton<PayMongoService>();
builder.Services.AddTransient<ProjectService>();
builder.Services.AddTransient<DesktopDataService>();
builder.Services.AddTransient<PM_ProjectService>();
builder.Services.AddTransient<PM_TaskService>();
builder.Services.AddTransient<ArchitectDesignService>();
builder.Services.AddTransient<ArchitectRevisionService>();
builder.Services.AddTransient<DailyProgressService>();
builder.Services.AddTransient<SiteRequestService>();
builder.Services.AddTransient<SiteIssueService>();
builder.Services.AddTransient<CloudinaryService>();
builder.Services.AddTransient<PdfExportService>(sp =>
{
    var pdfService = new PdfExportService();
    var cloudinaryService = sp.GetRequiredService<CloudinaryService>();
    pdfService.SetCloudinaryService(cloudinaryService);
    return pdfService;
});
builder.Services.AddTransient<QS_CostEstimateService>();
builder.Services.AddTransient<QS_BudgetService>();
builder.Services.AddTransient<FO_ExpenseService>();
builder.Services.AddTransient<FO_IncomeTransactionService>();
builder.Services.AddTransient<CashFlowService>();
builder.Services.AddTransient<AuditService>();
builder.Services.AddSingleton<EmailOtpService>();
builder.Services.AddHttpClient<PlanCore.Web.Services.RecaptchaService>();

var app = builder.Build();

var accessor = app.Services.GetRequiredService<IHttpContextAccessor>();
AuthService.ConfigureAccessor(accessor);
MauiProgram.SetServiceProvider(app.Services);

// Auto-create / verify SQL Server database and seed data (same routine as the MAUI app startup).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        System.Diagnostics.Debug.WriteLine("=== SQL Server Database Initialization Started ===");

        // A fresh hosting database sometimes still contains tables created by
        // unrelated tooling, which makes EF's EnsureCreated() a no-op and leaves
        // the application schema missing. When our core table is absent, drop
        // every existing user table so EnsureCreated() can build a clean schema.
        var needsRebuild = true;
        try
        {
            var connection = db.Database.GetDbConnection();
            var wasClosed = connection.State != System.Data.ConnectionState.Open;
            if (wasClosed) connection.Open();
            try
            {
                using var probe = connection.CreateCommand();
                probe.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PM_Projects') THEN 1 ELSE 0 END";
                needsRebuild = Convert.ToInt32(probe.ExecuteScalar()) == 0;
            }
            finally { if (wasClosed) connection.Close(); }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Schema probe failed: {ex.Message}"); }

        if (needsRebuild)
        {
            System.Diagnostics.Debug.WriteLine("Core schema missing - rebuilding a clean database schema.");
            RunSql(db, @"DECLARE @sql NVARCHAR(MAX) = N'';
                         SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
                         FROM sys.foreign_keys;
                         IF @sql <> N'' EXEC sp_executesql @sql;
                         SET @sql = N'';
                         SELECT @sql += N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + N';'
                         FROM sys.tables;
                         IF @sql <> N'' EXEC sp_executesql @sql;");
        }

        // -- Tenant columns -------------------------------------------------
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tenants' AND COLUMN_NAME = 'PendingPlan') ALTER TABLE Tenants ADD PendingPlan NVARCHAR(50) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tenants' AND COLUMN_NAME = 'PlanChangeEffective') ALTER TABLE Tenants ADD PlanChangeEffective DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Tenants' AND COLUMN_NAME = 'PaymentRetryCount') ALTER TABLE Tenants ADD PaymentRetryCount INT NOT NULL DEFAULT 0");

        var created = db.Database.EnsureCreated();
        System.Diagnostics.Debug.WriteLine($"EnsureCreated result: {created}");

        // -- ArchitectDesigns -------------------------------------------------
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ArchitectDesigns' AND COLUMN_NAME = 'FileData') ALTER TABLE ArchitectDesigns ADD FileData VARBINARY(MAX) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ArchitectDesigns' AND COLUMN_NAME = 'ApprovedOn') ALTER TABLE ArchitectDesigns ADD ApprovedOn DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ArchitectDesigns' AND COLUMN_NAME = 'RejectedOn') ALTER TABLE ArchitectDesigns ADD RejectedOn DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ArchitectDesigns' AND COLUMN_NAME = 'RevisedOn') ALTER TABLE ArchitectDesigns ADD RevisedOn DATETIME2 NULL");

        RunSql(db, @"IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ArchitectDesigns')
                           AND NOT EXISTS (SELECT 1 FROM sys.columns 
                                           WHERE object_id = OBJECT_ID('ArchitectDesigns') 
                                           AND name = 'DesignID' 
                                           AND is_identity = 1)
                        BEGIN
                            SELECT *, DesignID AS OldID INTO #TempAD FROM ArchitectDesigns;
                            DROP TABLE ArchitectDesigns;
                            CREATE TABLE ArchitectDesigns (
                                DesignID INT IDENTITY(1,1) NOT NULL,
                                DesignTitle NVARCHAR(200) NOT NULL,
                                Project NVARCHAR(200) NOT NULL,
                                Location NVARCHAR(MAX) NULL,
                                Version NVARCHAR(MAX) NOT NULL DEFAULT '',
                                UploadDate DATETIME2 NOT NULL DEFAULT GETDATE(),
                                Notes NVARCHAR(MAX) NULL,
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                                FilePath NVARCHAR(MAX) NULL,
                                FileName NVARCHAR(MAX) NULL,
                                FileData VARBINARY(MAX) NULL,
                                UploadedBy NVARCHAR(MAX) NULL,
                                IsArchived BIT NOT NULL DEFAULT 0,
                                ArchivedOn DATETIME2 NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                ApprovedOn DATETIME2 NULL,
                                RejectedOn DATETIME2 NULL,
                                RevisedOn DATETIME2 NULL
                            );
                            SET IDENTITY_INSERT ArchitectDesigns ON;
                            INSERT INTO ArchitectDesigns (DesignID, DesignTitle, Project, Location, Version, UploadDate, Notes, Status, FilePath, FileName, FileData, UploadedBy, IsArchived, ArchivedOn, CreatedAt, ApprovedOn, RejectedOn, RevisedOn)
                            SELECT OldID, DesignTitle, Project, Location, Version, UploadDate, Notes, Status, FilePath, FileName, FileData, UploadedBy, IsArchived, ArchivedOn, CreatedAt, NULL, NULL, NULL
                            FROM #TempAD ORDER BY OldID;
                            SET IDENTITY_INSERT ArchitectDesigns OFF;
                            DROP TABLE #TempAD;
                        END");

        // UploadDate was originally created as a DATE column, which truncated every
        // timestamp to midnight, so the Designs table always rendered 12:00 AM. The
        // default constraint blocks the type change, so drop it and restore it after.
        RunSql(db, @"IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                                     WHERE TABLE_NAME = 'ArchitectDesigns'
                                       AND COLUMN_NAME = 'UploadDate'
                                       AND DATA_TYPE = 'date')
                     BEGIN
                         DECLARE @defName NVARCHAR(200) =
                             (SELECT dc.name
                              FROM sys.default_constraints dc
                              JOIN sys.columns c
                                ON c.object_id = dc.parent_object_id
                               AND c.column_id = dc.parent_column_id
                              WHERE dc.parent_object_id = OBJECT_ID('ArchitectDesigns')
                                AND c.name = 'UploadDate');
                         IF @defName IS NOT NULL
                             EXEC('ALTER TABLE ArchitectDesigns DROP CONSTRAINT [' + @defName + ']');
                     END");
        RunSql(db, @"IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                                     WHERE TABLE_NAME = 'ArchitectDesigns'
                                       AND COLUMN_NAME = 'UploadDate'
                                       AND DATA_TYPE = 'date')
                         ALTER TABLE ArchitectDesigns ALTER COLUMN UploadDate DATETIME2 NOT NULL");
        RunSql(db, @"IF NOT EXISTS (SELECT 1 FROM sys.default_constraints
                                  WHERE parent_object_id = OBJECT_ID('ArchitectDesigns')
                                    AND name = 'DF_ArchitectDesigns_UploadDate')
                     ALTER TABLE ArchitectDesigns
                         ADD CONSTRAINT DF_ArchitectDesigns_UploadDate DEFAULT (GETDATE()) FOR UploadDate");
        // Restore the time of day that the DATE column discarded, keeping whatever
        // date the architect actually selected.
        RunSql(db, @"UPDATE ArchitectDesigns
                     SET UploadDate = CASE
                            WHEN CONVERT(date, UploadDate) = CONVERT(date, CreatedAt) THEN CreatedAt
                            ELSE DATEADD(ms,
                                        DATEDIFF(ms, CAST('00:00:00.000' AS time), CONVERT(time, CreatedAt)),
                                        CAST(CONVERT(date, UploadDate) AS datetime2))
                          END
                     WHERE DATEPART(HOUR, UploadDate) = 0
                       AND DATEPART(MINUTE, UploadDate) = 0
                       AND DATEPART(SECOND, UploadDate) = 0");

        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ArchitectRevisions')
                        BEGIN
                            CREATE TABLE ArchitectRevisions (
                                RevisionID INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                DesignID INT NULL,
                                ChangeRequest NVARCHAR(MAX) NOT NULL,
                                RevisionNotes NVARCHAR(MAX) NOT NULL DEFAULT '',
                                Project NVARCHAR(200) NOT NULL,
                                RequestedBy NVARCHAR(MAX) NOT NULL,
                                RequestDate DATETIME2 NOT NULL DEFAULT GETDATE(),
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                                IsArchived BIT NOT NULL DEFAULT 0,
                                ArchivedOn DATETIME2 NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                            )
                        END");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ArchitectRevisions' AND COLUMN_NAME = 'DesignID') ALTER TABLE ArchitectRevisions ADD DesignID INT NULL");

        // -- PM_Tasks ---------------------------------------------------------
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_Tasks' AND COLUMN_NAME = 'StartedOn') ALTER TABLE PM_Tasks ADD StartedOn DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_Tasks' AND COLUMN_NAME = 'CompletedDate') ALTER TABLE PM_Tasks ADD CompletedDate DATETIME2 NULL");
        RunSql(db, "IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_Tasks' AND COLUMN_NAME = 'DueDate' AND DATA_TYPE = 'date') ALTER TABLE PM_Tasks ALTER COLUMN DueDate DATETIME2 NULL");
        RunSql(db, "IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_Tasks' AND COLUMN_NAME = 'StartDate' AND DATA_TYPE = 'date') ALTER TABLE PM_Tasks ALTER COLUMN StartDate DATETIME2 NULL");

        // -- SE_ProgressUpdates ----------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SE_ProgressUpdates')
                        BEGIN
                            CREATE TABLE SE_ProgressUpdates (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                ProgressId NVARCHAR(20) NOT NULL,
                                Project NVARCHAR(200) NOT NULL,
                                Location NVARCHAR(200) NULL,
                                UpdateDate DATETIME2 NOT NULL DEFAULT GETDATE(),
                                ProgressPercent INT NOT NULL DEFAULT 0,
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                                Notes NVARCHAR(MAX) NULL,
                                PhotoData VARBINARY(MAX) NULL,
                                PhotoContentType NVARCHAR(100) NULL,
                                SubmittedBy NVARCHAR(200) NULL,
                                IsArchived BIT NOT NULL DEFAULT 0,
                                ArchivedOn DATETIME2 NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                            )
                        END");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_ProgressUpdates' AND COLUMN_NAME = 'SubmittedBy') ALTER TABLE SE_ProgressUpdates ADD SubmittedBy NVARCHAR(200) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_ProgressUpdates' AND COLUMN_NAME = 'Location') ALTER TABLE SE_ProgressUpdates ADD Location NVARCHAR(200) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_ProgressUpdates' AND COLUMN_NAME = 'PhotoData') ALTER TABLE SE_ProgressUpdates ADD PhotoData VARBINARY(MAX) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_ProgressUpdates' AND COLUMN_NAME = 'PhotoContentType') ALTER TABLE SE_ProgressUpdates ADD PhotoContentType NVARCHAR(100) NULL");
        RunSql(db, "IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_ProgressUpdates' AND COLUMN_NAME = 'UserId' AND IS_NULLABLE = 'NO') ALTER TABLE SE_ProgressUpdates ALTER COLUMN UserId NVARCHAR(450) NULL");

        // -- SE_SiteRequests ---------------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SE_SiteRequests')
                        BEGIN
                            CREATE TABLE SE_SiteRequests (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                RequestId NVARCHAR(20) NOT NULL,
                                UserId NVARCHAR(450) NOT NULL,
                                EngineerName NVARCHAR(256) NULL,
                                Project NVARCHAR(200) NOT NULL,
                                Location NVARCHAR(200) NULL,
                                MaterialType NVARCHAR(200) NULL,
                                Quantity NVARCHAR(50) NULL,
                                Unit NVARCHAR(50) NULL,
                                MaterialsJson NVARCHAR(4000) NULL,
                                EquipmentType NVARCHAR(100) NULL,
                                EquipmentQuantity NVARCHAR(50) NULL,
                                EquipmentUnit NVARCHAR(50) NULL,
                                EquipmentsJson NVARCHAR(4000) NULL,
                                WorkerType NVARCHAR(100) NULL,
                                WorkerQuantity NVARCHAR(50) NULL,
                                Manpower INT NOT NULL DEFAULT 0,
                                NeededBy DATETIME2 NOT NULL DEFAULT GETDATE(),
                                Notes NVARCHAR(MAX) NULL,
                                Urgency NVARCHAR(50) NULL DEFAULT 'Medium',
                                QSAdjustedQuantity NVARCHAR(50) NULL,
                                QSRecommendation NVARCHAR(1000) NULL,
                                QSFlagged BIT NULL,
                                QSRemarks NVARCHAR(MAX) NULL,
                                PMRemarks NVARCHAR(2000) NULL,
                                ReviewedByManagerId NVARCHAR(450) NULL,
                                ReviewedAt DATETIME2 NULL,
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                                IsArchived BIT NOT NULL DEFAULT 0,
                                ArchivedOn DATETIME2 NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                            )
                        END");
        RunSql(db, @"IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SE_SiteRequests')
                        BEGIN
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'Manpower') ALTER TABLE SE_SiteRequests ADD Manpower INT NOT NULL DEFAULT 0;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'MaterialsJson') ALTER TABLE SE_SiteRequests ADD MaterialsJson NVARCHAR(4000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'EquipmentsJson') ALTER TABLE SE_SiteRequests ADD EquipmentsJson NVARCHAR(4000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'Location') ALTER TABLE SE_SiteRequests ADD Location NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'EngineerName') ALTER TABLE SE_SiteRequests ADD EngineerName NVARCHAR(256) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteRequests' AND COLUMN_NAME = 'Notes') ALTER TABLE SE_SiteRequests ADD Notes NVARCHAR(MAX) NULL;
                        END");

        // -- SE_SiteIssues -----------------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SE_SiteIssues')
                        BEGIN
                            CREATE TABLE SE_SiteIssues (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                IssueId NVARCHAR(20) NOT NULL,
                                UserId NVARCHAR(450) NOT NULL,
                                ReportedByName NVARCHAR(256) NULL,
                                Project NVARCHAR(200) NOT NULL,
                                Location NVARCHAR(500) NULL,
                                IssueType NVARCHAR(50) NOT NULL,
                                IssueTypeOther NVARCHAR(100) NULL,
                                Severity NVARCHAR(20) NOT NULL,
                                Description NVARCHAR(MAX) NOT NULL,
                                SuggestedAction NVARCHAR(1000) NULL,
                                PMNotes NVARCHAR(2000) NULL,
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Open',
                                IsArchived BIT NOT NULL DEFAULT 0,
                                ArchivedOn DATETIME2 NULL,
                                DateReported DATETIME2 NOT NULL DEFAULT GETDATE(),
                                ResolvedDate DATETIME2 NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                            )
                        END");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteIssues' AND COLUMN_NAME = 'ArchivedOn') ALTER TABLE SE_SiteIssues ADD ArchivedOn DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteIssues' AND COLUMN_NAME = 'ResolvedDate') ALTER TABLE SE_SiteIssues ADD ResolvedDate DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'SE_SiteIssues' AND COLUMN_NAME = 'PMNotes') ALTER TABLE SE_SiteIssues ADD PMNotes NVARCHAR(2000) NULL");

        // -- CostEstimates -------------------------------------------------------
        RunSql(db, @"IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CostEstimates')
                        BEGIN
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'FinanceApprovalStatus') ALTER TABLE CostEstimates ADD FinanceApprovalStatus NVARCHAR(50) NOT NULL DEFAULT 'Pending';
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'PMApprovalStatus') ALTER TABLE CostEstimates ADD PMApprovalStatus NVARCHAR(50) NOT NULL DEFAULT 'Pending';
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'FinanceRejectReason') ALTER TABLE CostEstimates ADD FinanceRejectReason NVARCHAR(1000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'PMRejectReason') ALTER TABLE CostEstimates ADD PMRejectReason NVARCHAR(1000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'RejectReason') ALTER TABLE CostEstimates ADD RejectReason NVARCHAR(1000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'FinanceApprovedDate') ALTER TABLE CostEstimates ADD FinanceApprovedDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'FinanceApprovedBy') ALTER TABLE CostEstimates ADD FinanceApprovedBy NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'PMApprovedDate') ALTER TABLE CostEstimates ADD PMApprovedDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'PMApprovedBy') ALTER TABLE CostEstimates ADD PMApprovedBy NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'ScheduledDate') ALTER TABLE CostEstimates ADD ScheduledDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'UpdatedDate') ALTER TABLE CostEstimates ADD UpdatedDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'IsArchived') ALTER TABLE CostEstimates ADD IsArchived BIT NOT NULL DEFAULT 0;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'ArchivedOn') ALTER TABLE CostEstimates ADD ArchivedOn DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CostEstimates' AND COLUMN_NAME = 'AmountSpent') ALTER TABLE CostEstimates ADD AmountSpent DECIMAL(18,2) NOT NULL DEFAULT 0;
                        END");

        // -- QS_Budgets ----------------------------------------------------------
        RunSql(db, @"IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'QS_Budgets')
                        BEGIN
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'ManpowerSpent') ALTER TABLE QS_Budgets ADD ManpowerSpent DECIMAL(18,2) NOT NULL DEFAULT 0;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'EquipmentSpent') ALTER TABLE QS_Budgets ADD EquipmentSpent DECIMAL(18,2) NOT NULL DEFAULT 0;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'MaterialsSpent') ALTER TABLE QS_Budgets ADD MaterialsSpent DECIMAL(18,2) NOT NULL DEFAULT 0;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'BudgetDate') ALTER TABLE QS_Budgets ADD BudgetDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'Notes') ALTER TABLE QS_Budgets ADD Notes NVARCHAR(1000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'Location') ALTER TABLE QS_Budgets ADD Location NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'PMApprovalStatus') ALTER TABLE QS_Budgets ADD PMApprovalStatus NVARCHAR(50) NOT NULL DEFAULT 'Pending';
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'PMApprovalDate') ALTER TABLE QS_Budgets ADD PMApprovalDate DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'FinanceApprovedBy') ALTER TABLE QS_Budgets ADD FinanceApprovedBy NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'PMApprovedBy') ALTER TABLE QS_Budgets ADD PMApprovedBy NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'RejectedOn') ALTER TABLE QS_Budgets ADD RejectedOn DATETIME2 NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'PMRejectReason') ALTER TABLE QS_Budgets ADD PMRejectReason NVARCHAR(1000) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'FinanceRejectedBy') ALTER TABLE QS_Budgets ADD FinanceRejectedBy NVARCHAR(200) NULL;
                            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'QS_Budgets' AND COLUMN_NAME = 'PMRejectedBy') ALTER TABLE QS_Budgets ADD PMRejectedBy NVARCHAR(200) NULL;
                        END");

        // -- PM_IncomeTransactions / Expenses / CashFlows ---------------------------
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'TransactionDate') ALTER TABLE PM_IncomeTransactions ADD TransactionDate DATETIME2 NOT NULL DEFAULT GETDATE()");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'PaymentMethod') ALTER TABLE PM_IncomeTransactions ADD PaymentMethod NVARCHAR(100) NOT NULL DEFAULT ''");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'ReferenceNumber') ALTER TABLE PM_IncomeTransactions ADD ReferenceNumber NVARCHAR(200) NOT NULL DEFAULT ''");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'Category') ALTER TABLE PM_IncomeTransactions ADD Category NVARCHAR(100) NOT NULL DEFAULT ''");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'Notes') ALTER TABLE PM_IncomeTransactions ADD Notes NVARCHAR(1000) NOT NULL DEFAULT ''");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'PM_IncomeTransactions' AND COLUMN_NAME = 'CreatedAt') ALTER TABLE PM_IncomeTransactions ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Expenses' AND COLUMN_NAME = 'CreatedAt') ALTER TABLE Expenses ADD CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()");
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CashFlows')
                        BEGIN
                            CREATE TABLE CashFlows (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                TransactionDate DATETIME2 NOT NULL DEFAULT GETDATE(),
                                Description NVARCHAR(500) NOT NULL DEFAULT '',
                                Type NVARCHAR(50) NOT NULL DEFAULT 'Inflow',
                                Amount DECIMAL(18,2) NOT NULL DEFAULT 0,
                                Category NVARCHAR(100) NOT NULL DEFAULT '',
                                Project NVARCHAR(200) NOT NULL DEFAULT '',
                                ReferenceNumber NVARCHAR(200) NOT NULL DEFAULT '',
                                Notes NVARCHAR(1000) NOT NULL DEFAULT '',
                                IsArchived BIT NOT NULL DEFAULT 0,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
                            );
                        END");

        // -- AdminEmail columns (multi-tenant isolation) ---------------------------
        AddAdminEmailColumn(db, "PM_Projects");
        AddAdminEmailColumn(db, "Expenses");
        AddAdminEmailColumn(db, "PM_IncomeTransactions");
        AddAdminEmailColumn(db, "PM_Tasks");
        AddAdminEmailColumn(db, "SE_SiteIssues");
        AddAdminEmailColumn(db, "SE_ProgressUpdates");
        AddAdminEmailColumn(db, "SE_SiteRequests");
        AddAdminEmailColumn(db, "QS_Budgets");
        AddAdminEmailColumn(db, "ArchitectDesigns");
        AddAdminEmailColumn(db, "CashFlows");
        AddAdminEmailColumn(db, "CostEstimates");
        AddAdminEmailColumn(db, "AuditLogs");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AuditLogs' AND COLUMN_NAME = 'IpAddress') ALTER TABLE AuditLogs ADD IpAddress NVARCHAR(45) NULL");

        RunSql(db, "UPDATE PM_Projects SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE Expenses SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE PM_IncomeTransactions SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE PM_Tasks SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE SE_SiteIssues SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE SE_ProgressUpdates SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE SE_SiteRequests SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE QS_Budgets SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE ArchitectDesigns SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE CashFlows SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE CostEstimates SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");
        RunSql(db, "UPDATE AuditLogs SET AdminEmail = 'admin@plancore.com' WHERE AdminEmail IS NULL");

        // -- Normalize PM_Tasks.AssignedTo to the role keys the modules query --------
        // Legacy rows stored display labels ("Site Engineer") while every module
        // queries the role key ("SiteEngineer"), hiding those tasks from My Tasks.
        RunSql(db, "UPDATE PM_Tasks SET AssignedTo = 'SiteEngineer' WHERE AssignedTo IN ('Site Engineer', 'site engineer', 'SITE ENGINEER')");
        RunSql(db, "UPDATE PM_Tasks SET AssignedTo = 'QuantitySurveyor' WHERE AssignedTo IN ('Quantity Surveyor', 'quantity surveyor', 'QUANTITY SURVEYOR')");
        RunSql(db, "UPDATE PM_Tasks SET AssignedTo = 'ProjectManager' WHERE AssignedTo IN ('Project Manager', 'project manager', 'PROJECT MANAGER')");

        // -- UserAccounts ---------------------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserAccounts')
                        BEGIN
                            CREATE TABLE UserAccounts (
                                Email NVARCHAR(256) NOT NULL PRIMARY KEY,
                                PasswordHash NVARCHAR(MAX) NOT NULL,
                                Role NVARCHAR(50) NOT NULL,
                                PasswordChangedAt DATETIME2 NULL,
                                AdminEmail NVARCHAR(256) NULL
                            )
                        END");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'PasswordChangedAt') ALTER TABLE UserAccounts ADD PasswordChangedAt DATETIME2 NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'AdminEmail') ALTER TABLE UserAccounts ADD AdminEmail NVARCHAR(256) NULL");
        RunSql(db, "UPDATE UserAccounts SET AdminEmail = Email WHERE AdminEmail IS NULL");

        // -- Group self-tenanted module accounts under the default tenant ---------
        // Cross-role work has to be visible: a task a Project Manager assigns to
        // "Architect" only reaches the Architect when both sit in the same tenant.
        // Only accounts never explicitly linked to a tenant (AdminEmail still equal
        // to their own email) move, so accounts already owned by another admin stay
        // put. Runs after UserAccounts exists so a fresh database is safe.
        var defaultTenantAdmin = "admin@plancore.com";
        var moduleRoles = new[] { "ProjectManager", "Architect", "SiteEngineer", "QuantitySurveyor", "FinanceOfficer" };
        var tenantOwnedTables = new[]
        {
            "PM_Projects", "PM_Tasks", "Expenses", "PM_IncomeTransactions", "SE_SiteIssues",
            "SE_ProgressUpdates", "SE_SiteRequests", "QS_Budgets", "ArchitectDesigns",
            "CashFlows", "CostEstimates", "AuditLogs"
        };

        var selfTenantedEmails = new List<string>();
        try
        {
            selfTenantedEmails = db.UserAccounts.AsNoTracking()
                .Where(u => moduleRoles.Contains(u.Role))
                .Where(u => u.AdminEmail == null || u.AdminEmail == u.Email)
                .Select(u => u.Email)
                .ToList();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Note: tenant regroup lookup failed: {ex.Message}"); }

        if (selfTenantedEmails.Count > 0
            && db.UserAccounts.AsNoTracking().Any(u => u.Email == defaultTenantAdmin && u.Role == "Admin"))
        {
            foreach (var table in tenantOwnedTables)
                ReassignTenantRows(db, table, defaultTenantAdmin, selfTenantedEmails);

            foreach (var email in selfTenantedEmails)
            {
                try
                {
                    db.Database.ExecuteSqlRaw(
                        "UPDATE UserAccounts SET AdminEmail = @admin WHERE Email = @email",
                        new SqlParameter("@admin", defaultTenantAdmin),
                        new SqlParameter("@email", email));
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Note: tenant regroup for {email}: {ex.Message}"); }
            }
        }

        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'PhoneNumber') ALTER TABLE UserAccounts ADD PhoneNumber NVARCHAR(20) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'MfaEnabled') ALTER TABLE UserAccounts ADD MfaEnabled BIT NOT NULL DEFAULT 0");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'OtpCode') ALTER TABLE UserAccounts ADD OtpCode NVARCHAR(6) NULL");
        RunSql(db, "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'UserAccounts' AND COLUMN_NAME = 'OtpExpiry') ALTER TABLE UserAccounts ADD OtpExpiry DATETIME2 NULL");

        // -- ASPNetUsers / ASPNetUserLogins ------------------------------------------
        // Note: "ASPNetUsers" resolves case-insensitively to the existing AspNetUsers table,
        // which is referenced by FKs (ProjectAssignments, TaskSchedulings, etc.) - do NOT drop it.
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ASPNetUsers')
                        BEGIN
                            CREATE TABLE ASPNetUsers (
                            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                            Email NVARCHAR(256) NOT NULL,
                            PasswordHash NVARCHAR(MAX) NOT NULL,
                            Role NVARCHAR(50) NOT NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                            LastLoginAt DATETIME2 NULL
                        )
                        END");
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ASPNetUserLogins')
                        BEGIN
                            CREATE TABLE ASPNetUserLogins (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                Email NVARCHAR(256) NOT NULL,
                                LoginTime DATETIME2 NOT NULL DEFAULT GETDATE()
                            )
                        END");

        // -- Data-import marker -----------------------------------------------------
        // Records that the one-time local->remote data migration has completed, so
        // later redeploys (which re-copy import_data.json) do not wipe live data.
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '_DataImportLog')
                        BEGIN
                            CREATE TABLE _DataImportLog (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                CompletedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                Detail NVARCHAR(500) NULL
                            )
                        END");

        // -- Tenants ------------------------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Tenants')
                        BEGIN
                            CREATE TABLE Tenants (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                CompanyName NVARCHAR(200) NOT NULL,
                                AdminEmail NVARCHAR(256) NOT NULL,
                                ContactNumber NVARCHAR(50) NULL,
                                Address NVARCHAR(500) NULL,
                                [Plan] NVARCHAR(50) NOT NULL DEFAULT 'Basic',
                                IsActive BIT NOT NULL DEFAULT 1,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                SubscriptionStart DATETIME2 NULL,
                                SubscriptionEnd DATETIME2 NULL,
                                LastPaymentAt DATETIME2 NULL,
                                PaymentProvider NVARCHAR(100) NULL,
                                PaymentProviderCustomerId NVARCHAR(200) NULL,
                                PaymentProviderSubscriptionId NVARCHAR(200) NULL,
                                UsedProjects INT NOT NULL DEFAULT 0,
                                UsedUsers INT NOT NULL DEFAULT 0,
                                UsedStorageBytes BIGINT NOT NULL DEFAULT 0,
                                BillingCycle NVARCHAR(20) NOT NULL DEFAULT 'monthly',
                                TrialEnd DATETIME2 NULL,
                                PendingPlan NVARCHAR(50) NULL,
                                PlanChangeEffective DATETIME2 NULL,
                                PaymentRetryCount INT NOT NULL DEFAULT 0
                            )
                        END");
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN Address NVARCHAR(500) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN ContactNumber NVARCHAR(50) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN PaymentProvider NVARCHAR(100) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN PaymentProviderCustomerId NVARCHAR(200) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN PaymentProviderSubscriptionId NVARCHAR(200) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN TrialEnd DATETIME2 NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN PendingPlan NVARCHAR(50) NULL"); } catch { }
        try { RunSql(db, "ALTER TABLE Tenants ALTER COLUMN PlanChangeEffective DATETIME2 NULL"); } catch { }

        // -- PaymentTransactions ----------------------------------------------------
        RunSql(db, @"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PaymentTransactions')
                        BEGIN
                            CREATE TABLE PaymentTransactions (
                                Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                                TenantId INT NOT NULL,
                                PaymentProvider NVARCHAR(100) NOT NULL,
                                PaymentProviderTransactionId NVARCHAR(200) NULL,
                                Amount DECIMAL(18,2) NOT NULL DEFAULT 0,
                                Currency NVARCHAR(10) NOT NULL DEFAULT 'PHP',
                                Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
                                [Plan] NVARCHAR(50) NULL,
                                BillingCycle NVARCHAR(20) NULL,
                                CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                PaidAt DATETIME2 NULL,
                                Notes NVARCHAR(500) NULL
                            )
                        END");

        // -- One-time data migration -----------------------------------------------
        // If an export from the local database is present next to the app, load it
        // (replacing any seeded rows) before any seed/auth logic runs.
        RunDataImport(db);

        // -- Always ensure known accounts exist with correct passwords -------------
        // This runs after every deploy so the import_data.json can never leave
        // accounts with stale/unknown hashed passwords from a different machine.
        {
            var knownUsers = new[]
            {
                (Email: "superadmin@plancore.com", Password: "SuperAdmin@123", Role: "SuperAdmin", AdminEmail: "superadmin@plancore.com"),
                (Email: "admin@plancore.com",       Password: "Admin@123",      Role: "Admin",       AdminEmail: "admin@plancore.com"),
                (Email: "projectmanager@plancore.com", Password: "Manager@123", Role: "ProjectManager", AdminEmail: "admin@plancore.com"),
                (Email: "architect@plancore.com",    Password: "Architect@123", Role: "Architect",    AdminEmail: "admin@plancore.com"),
                (Email: "alexaarchitect@plancore.com", Password: "Architect@123", Role: "Architect",  AdminEmail: "admin@plancore.com"),
                (Email: "engineer@plancore.com",     Password: "Engineer@123",  Role: "SiteEngineer", AdminEmail: "admin@plancore.com"),
                (Email: "qs@plancore.com",           Password: "Surveyor@123",  Role: "QuantitySurveyor", AdminEmail: "admin@plancore.com"),
                (Email: "finance@plancore.com",      Password: "Finance@123",   Role: "FinanceOfficer", AdminEmail: "admin@plancore.com"),
            };
            foreach (var ku in knownUsers)
            {
                var existing = db.UserAccounts.FirstOrDefault(u => u.Email == ku.Email);
                var newHash = AuthService.HashPassword(ku.Password);
                if (existing == null)
                {
                    db.UserAccounts.Add(new UserAccount
                    {
                        Email = ku.Email,
                        PasswordHash = newHash,
                        Role = ku.Role,
                        AdminEmail = ku.AdminEmail
                    });
                }
                else
                {
                    existing.PasswordHash = newHash;
                    existing.Role = ku.Role;
                    db.UserAccounts.Update(existing);
                }
            }
            db.SaveChanges();
            System.Diagnostics.Debug.WriteLine("=== Known user passwords reset/ensured ===");
        }

        // -- Seed default tenant ----------------------------------------------------
        if (!db.Tenants.Any())
        {
            db.Tenants.Add(new PlanCoreMAUI.Models.Tenant
            {
                CompanyName = "PlanCore Demo Corp",
                AdminEmail = "admin@plancore.com",
                Plan = PlanCoreMAUI.Models.SubscriptionPlan.Enterprise,
                IsActive = true,
                SubscriptionStart = DateTime.Now,
                SubscriptionEnd = DateTime.Now.AddYears(10),
                BillingCycle = "yearly",
                CreatedAt = DateTime.Now,
                Address = "",
                ContactNumber = "",
                PaymentProvider = "",
                PaymentProviderCustomerId = "",
                PaymentProviderSubscriptionId = "",
                PendingPlan = ""
            });
            db.SaveChanges();
        }

        var tenantSeed = db.Tenants.FirstOrDefault();
        if (tenantSeed != null)
        {
            tenantSeed.UsedProjects = db.PM_Projects.Count();
            tenantSeed.UsedUsers = db.UserAccounts.Count();
            db.SaveChanges();
            SubscriptionService.SetTenantPlan(tenantSeed.Plan);
        }

        SubscriptionService.RunPeriodicChecks(scope.ServiceProvider);

        // -- Seed default users -----------------------------------------------------
        if (!db.UserAccounts.Any())
        {
            var seedUsers = new[]
            {
                new UserAccount { Email = "admin@plancore.com", PasswordHash = AuthService.HashPassword("Admin@123"), Role = "Admin" },
                new UserAccount { Email = "projectmanager@plancore.com", PasswordHash = AuthService.HashPassword("Manager@123"), Role = "ProjectManager" },
                new UserAccount { Email = "architect@plancore.com", PasswordHash = AuthService.HashPassword("Architect@123"), Role = "Architect" },
                new UserAccount { Email = "alexaarchitect@plancore.com", PasswordHash = AuthService.HashPassword("Architect@123"), Role = "Architect" },
                new UserAccount { Email = "engineer@plancore.com", PasswordHash = AuthService.HashPassword("Engineer@123"), Role = "SiteEngineer" },
                new UserAccount { Email = "qs@plancore.com", PasswordHash = AuthService.HashPassword("Surveyor@123"), Role = "QuantitySurveyor" },
                new UserAccount { Email = "finance@plancore.com", PasswordHash = AuthService.HashPassword("Finance@123"), Role = "FinanceOfficer" },
                new UserAccount { Email = "superadmin@plancore.com", PasswordHash = AuthService.HashPassword("SuperAdmin@123"), Role = "SuperAdmin" }
            };
            db.UserAccounts.AddRange(seedUsers);
            db.ASPNetUsers.AddRange(seedUsers.Select(u => new ASPNetUser
            {
                Email = u.Email,
                PasswordHash = u.PasswordHash,
                Role = u.Role,
                CreatedAt = DateTime.Now
            }).ToArray());
            db.SaveChanges();
        }

        // Load accounts into the in-memory auth store immediately so login works
        // even if the optional sample-data seeding below fails on a fresh schema.
        AuthService.LoadFromDatabase(db);

        // -- Seed sample project / expenses / income -----------------------------------
        try
        {
            if (!db.PM_Projects.Any())
            {
                db.PM_Projects.Add(new PM_Projects
                {
                    ProjectName = "Highway Overpass Construction",
                    Description = "Sample infrastructure project",
                    Location = "Manila",
                    Manager = "Project Manager",
                    AssignedTo = "ProjectManager",
                    ProjectManagerId = "projectmanager@plancore.com",
                    Status = "In Progress",
                    StartDate = DateTime.Now.AddMonths(-2),
                    DueDate = DateTime.Now.AddMonths(4),
                    AdminEmail = "admin@plancore.com",
                    CreatedAt = DateTime.Now.AddMonths(-2)
                });
                db.SaveChanges();
            }

            if (!db.Expenses.Any())
            {
                var now = DateTime.Now;
                db.Expenses.AddRange(
                    new Expense { UserId = "finance@plancore.com", Project = "Highway Overpass Construction", Category = "Materials", Amount = 250000, ExpenseDate = now.AddDays(-45), Description = "Steel reinforcement bars", Status = "Approved", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-45) },
                    new Expense { UserId = "finance@plancore.com", Project = "Highway Overpass Construction", Category = "Labor", Amount = 180000, ExpenseDate = now.AddDays(-30), Description = "Construction crew wages (2 weeks)", Status = "Approved", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-30) },
                    new Expense { UserId = "finance@plancore.com", Project = "Highway Overpass Construction", Category = "Equipment", Amount = 95000, ExpenseDate = now.AddDays(-20), Description = "Heavy machinery rental", Status = "Approved", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-20) },
                    new Expense { UserId = "finance@plancore.com", Project = "Highway Overpass Construction", Category = "Materials", Amount = 120000, ExpenseDate = now.AddDays(-10), Description = "Cement and aggregates", Status = "Pending", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-10) },
                    new Expense { UserId = "finance@plancore.com", Project = "Highway Overpass Construction", Category = "Other", Amount = 45000, ExpenseDate = now.AddDays(-5), Description = "Permit and compliance fees", Status = "Pending", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-5) }
                );
                db.SaveChanges();
            }

            if (!db.PM_IncomeTransactions.Any())
            {
                var now = DateTime.Now;
                db.PM_IncomeTransactions.AddRange(
                    new PM_IncomeTransactions { Description = "Client advance payment - Phase 1", Project = "Highway Overpass Construction", Amount = 800000, Status = "Received", TransactionDate = now.AddDays(-50), PaymentMethod = "Bank Transfer", ReferenceNumber = "REF-001", Category = "Advance", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-50) },
                    new PM_IncomeTransactions { Description = "Progress billing - January", Project = "Highway Overpass Construction", Amount = 600000, Status = "Received", TransactionDate = now.AddDays(-25), PaymentMethod = "Check", ReferenceNumber = "REF-002", Category = "Progress Billing", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-25) },
                    new PM_IncomeTransactions { Description = "Progress billing - February", Project = "Highway Overpass Construction", Amount = 550000, Status = "Pending", TransactionDate = now.AddDays(-2), PaymentMethod = "Bank Transfer", ReferenceNumber = "REF-003", Category = "Progress Billing", AdminEmail = "admin@plancore.com", CreatedAt = now.AddDays(-2) }
                );
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sample data seed skipped: {ex.Message}");
        }

        AuthService.LoadFromDatabase(db);

        // Purge audit logs older than retention period
        var retentionMonths = DotNetEnv.Env.GetInt("AUDIT_LOG_RETENTION_MONTHS", 6);
        var cutoffDate = DateTime.Now.AddMonths(-retentionMonths);
        var oldLogs = db.AuditLogs.Where(a => a.PerformedAt < cutoffDate).ToList();
        if (oldLogs.Any())
        {
            db.AuditLogs.RemoveRange(oldLogs);
            db.SaveChanges();
        }

        System.Diagnostics.Debug.WriteLine("=== SQL Server Database Initialization Completed ===");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"=== Database Initialization ERROR ===");
        System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        System.Diagnostics.Debug.WriteLine($"Inner: {ex.InnerException?.Message}");
        System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
    }
}

// Background jobs: recurring subscription checks + overdue-task auto-completion.
var schedulerScope = app.Services.CreateScope();
var schedulerServices = schedulerScope.ServiceProvider;
MauiProgram.SetServiceProvider(app.Services);

_ = Task.Run(async () =>
{
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                SubscriptionService.RunPeriodicChecks(app.Services);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timer] Subscription check error: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Timer] Fatal error: {ex.Message}"); }
});

_ = Task.Run(async () =>
{
    try
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var taskService = scope.ServiceProvider.GetRequiredService<PM_TaskService>();
                await taskService.AutoCompleteOverdueTasksAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Timer] Overdue tasks error: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Timer] Overdue tasks fatal error: {ex.Message}"); }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();

// HTML responses are never cacheable (the browser otherwise keeps serving a
// stale page after a rebuild, since Razor views have no ETag/Last-Modified).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var contentType = context.Response.ContentType ?? "";
        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        return Task.CompletedTask;
    });

    await next();
});

app.UseRouting();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

app.MapFallbackToController("Index", "NotFound");

app.Run();

static void RunSql(ApplicationDbContext db, string sql)
{
    try { db.Database.ExecuteSqlRaw(sql); }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Note: {ex.Message}"); }
}

static void AddAdminEmailColumn(ApplicationDbContext db, string table)
{
    try
    {
#pragma warning disable EF1002 // table name cannot be parameterized; value is a fixed internal constant
        db.Database.ExecuteSqlRaw($"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = 'AdminEmail') ALTER TABLE {table} ADD AdminEmail NVARCHAR(256) NULL");
#pragma warning restore EF1002
    }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Note: AdminEmail on {table}: {ex.Message}"); }
}

static void ReassignTenantRows(ApplicationDbContext db, string table, string tenantAdmin, IReadOnlyList<string> emails)
{
    try
    {
        var parameterNames = new List<string>();
        var parameters = new List<SqlParameter> { new("@admin", tenantAdmin) };
        for (var i = 0; i < emails.Count; i++)
        {
            parameterNames.Add($"@email{i}");
            parameters.Add(new SqlParameter($"@email{i}", emails[i]));
        }

#pragma warning disable EF1002 // table name cannot be parameterized; email values are parameterized below
        db.Database.ExecuteSqlRaw(
            $"UPDATE {table} SET AdminEmail = @admin WHERE AdminEmail IN ({string.Join(", ", parameterNames)})",
            parameters.ToArray());
#pragma warning restore EF1002
    }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Note: tenant reassign on {table}: {ex.Message}"); }
}

// ---------------------------------------------------------------------------
// One-time data migration: loads import_data.json (exported from the local
// database) into the current database, replacing existing rows. Runs once;
// the file is renamed to import_data.done.json afterwards so it does not run
// again on subsequent restarts.
// ---------------------------------------------------------------------------
static void RunDataImport(ApplicationDbContext db)
{
    var baseDir = AppContext.BaseDirectory;
    var dataFile = Path.Combine(baseDir, "import_data.json");
    var doneFile = Path.Combine(baseDir, "import_data.done.json");
    var logFile = Path.Combine(baseDir, "import_result.log");

    var log = new System.Text.StringBuilder();
    void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        log.AppendLine(line);
        System.Diagnostics.Debug.WriteLine("[IMPORT] " + line);
    }
    void Flush()
    {
        try { File.WriteAllText(logFile, log.ToString()); } catch { }
    }

    try
    {
        if (!File.Exists(dataFile))
        {
            Log("import_data.json not found; nothing to import.");
            Flush();
            return;
        }

        Log("=== DATA IMPORT STARTED ===");
        var json = File.ReadAllText(dataFile);
        Log($"Read {json.Length:N0} characters from import_data.json.");

        using var doc = JsonDocument.Parse(json);
        var tables = doc.RootElement.GetProperty("tables");

        var conn = (SqlConnection)db.Database.GetDbConnection();
        var openedHere = conn.State != System.Data.ConnectionState.Open;
        if (openedHere) conn.Open();

        // If a previous run already completed the import, do nothing (protects live
        // data on redeploys that re-copy import_data.json).
        using (var marker = conn.CreateCommand())
        {
            marker.CommandText = "SELECT COUNT(*) FROM _DataImportLog";
            try
            {
                var done = Convert.ToInt32(marker.ExecuteScalar());
                if (done > 0)
                {
                    Log("Data import already completed previously; skipping.");
                    if (File.Exists(dataFile))
                    {
                        try { File.Move(dataFile, doneFile, overwrite: true); } catch { }
                    }
                    if (openedHere) conn.Close();
                    Flush();
                    return;
                }
            }
            catch { /* marker table missing -> treat as not-yet-imported */ }
        }

        ExecSql(conn, @"DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' NOCHECK CONSTRAINT ALL;'
            FROM sys.tables;
            IF @sql <> N'' EXEC sp_executesql @sql;");
        Log("Disabled all FK/check constraints.");

        var totalRows = 0;
        var tableCount = 0;
        var failures = 0;

        foreach (var table in tables.EnumerateArray())
        {
            var name = table.GetProperty("name").GetString() ?? "";
            var remote = GetRemoteColumns(conn, name);
            if (remote.Count == 0)
            {
                Log($"SKIP {name}: table not present in target schema.");
                continue;
            }

            var identityCol = GetRemoteIdentity(conn, name);
            var jsonCols = table.GetProperty("columns").EnumerateArray().ToList();
            var keep = new List<int>();
            var colNames = new List<string>();
            var colTypes = new List<string>();
            for (var i = 0; i < jsonCols.Count; i++)
            {
                var cn = jsonCols[i].GetProperty("n").GetString() ?? "";
                if (!remote.TryGetValue(cn, out var remoteType)) continue;
                var localType = jsonCols[i].GetProperty("t").GetString() ?? "";
                if (!CompatibleType(localType, remoteType))
                {
                    Log($"  drop {name}.{cn}: local '{localType}' incompatible with remote '{remoteType}'.");
                    continue;
                }
                keep.Add(i);
                colNames.Add(cn);
                colTypes.Add(localType);
            }
            if (keep.Count == 0)
            {
                Log($"SKIP {name}: no matching columns.");
                continue;
            }

            var rows = table.GetProperty("rows").EnumerateArray().ToList();

            try
            {
                ExecSql(conn, $"DELETE FROM {QuoteName(name)};");
                var idInsert = identityCol != null &&
                    colNames.Any(c => string.Equals(c, identityCol, StringComparison.OrdinalIgnoreCase));
                if (idInsert) ExecSql(conn, $"SET IDENTITY_INSERT {QuoteName(name)} ON;");
                try
                {
                    var inserted = InsertRows(conn, name, colNames, colTypes, rows, keep);
                    totalRows += inserted;
                    tableCount++;
                    Log($"imported {name}: {inserted} rows");
                }
                finally
                {
                    // Always turn identity insert back off, even if the insert threw,
                    // otherwise it stays ON for the session and blocks later tables.
                    if (idInsert) ExecSql(conn, $"SET IDENTITY_INSERT {QuoteName(name)} OFF;");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Log($"FAILED {name}: {ex.Message}");
            }
        }

        ExecSql(conn, @"DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + N'.' + QUOTENAME(name) + N' WITH NOCHECK CHECK CONSTRAINT ALL;'
            FROM sys.tables;
            IF @sql <> N'' EXEC sp_executesql @sql;");
        Log("Re-enabled constraints.");

        if (failures == 0)
        {
            try
            {
                using var mark = conn.CreateCommand();
                mark.CommandText = "INSERT INTO _DataImportLog (Detail) VALUES (@d)";
                mark.Parameters.AddWithValue("@d", $"tables={tableCount}, rows={totalRows}");
                mark.ExecuteNonQuery();
                Log("Recorded completion marker (import will be skipped on future deploys).");
            }
            catch (Exception ex)
            {
                Log($"WARNING: could not write completion marker: {ex.Message}");
            }
        }

        if (openedHere) conn.Close();

        Log($"=== IMPORT FINISHED: {tableCount} tables, {totalRows} rows, {failures} failures ===");

        if (failures == 0)
        {
            try
            {
                File.Move(dataFile, doneFile, overwrite: true);
                Log("Renamed import_data.json -> import_data.done.json (import will not run again).");
            }
            catch (Exception ex)
            {
                Log($"WARNING: could not rename import file: {ex.Message}");
            }
        }
        else
        {
            Log("WARNING: import had failures; leaving import_data.json in place to retry on next start.");
        }

        Flush();
    }
    catch (Exception ex)
    {
        Log($"IMPORT ERROR: {ex.Message}");
        Log(ex.StackTrace ?? "");
        Flush();
    }
}

static void ExecSql(SqlConnection conn, string sql)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    cmd.CommandTimeout = 300;
    cmd.ExecuteNonQuery();
}

static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";

// Groups SQL types so a local column is only mapped to a remote column of a
// compatible family (prevents e.g. nvarchar GUID -> int identity conversion).
static bool CompatibleType(string local, string remote)
{
    static string Group(string t) => (t ?? "").ToLowerInvariant() switch
    {
        "int" or "bigint" or "smallint" or "tinyint" => "int",
        "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => "num",
        "nvarchar" or "nchar" or "varchar" or "char" or "text" or "ntext" or "xml" => "str",
        "varbinary" or "binary" or "image" => "bin",
        "datetime" or "datetime2" or "smalldatetime" or "date" or "time" or "datetimeoffset" => "dt",
        "bit" => "bit",
        "uniqueidentifier" => "uid",
        var other => other
    };
    return Group(local) == Group(remote);
}

static Dictionary<string, string> GetRemoteColumns(SqlConnection conn, string table)
{
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t";
    cmd.Parameters.AddWithValue("@t", table);
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) dict[reader.GetString(0)] = reader.GetString(1);
    return dict;
}

static string? GetRemoteIdentity(SqlConnection conn, string table)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"SELECT TOP 1 c.name FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        WHERE t.name = @t AND c.is_identity = 1";
    cmd.Parameters.AddWithValue("@t", table);
    var result = cmd.ExecuteScalar();
    return result is null || result is DBNull ? null : (string)result;
}

static int InsertRows(SqlConnection conn, string table, List<string> colNames, List<string> colTypes, List<JsonElement> rows, List<int> keep)
{
    if (rows.Count == 0) return 0;

    var width = colNames.Count;
    var colList = string.Join(",", colNames.Select(QuoteName));
    var maxRowsPerBatch = Math.Max(1, 2000 / Math.Max(1, width));
    var inserted = 0;

    for (var start = 0; start < rows.Count; start += maxRowsPerBatch)
    {
        var batch = rows.GetRange(start, Math.Min(maxRowsPerBatch, rows.Count - start));
        var sb = new System.Text.StringBuilder();
        sb.Append("INSERT INTO ").Append(QuoteName(table)).Append(" (").Append(colList).Append(") VALUES ");

        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 300;

        for (var ri = 0; ri < batch.Count; ri++)
        {
            if (ri > 0) sb.Append(',');
            sb.Append('(');
            var rowValues = batch[ri].EnumerateArray().ToList();
            for (var ci = 0; ci < width; ci++)
            {
                if (ci > 0) sb.Append(',');
                var pname = "@p" + ri + "_" + ci;
                sb.Append(pname);
                var param = cmd.CreateParameter();
                param.ParameterName = pname;
                BindParameter(param, rowValues[keep[ci]], colTypes[ci]);
                cmd.Parameters.Add(param);
            }
            sb.Append(')');
        }

        cmd.CommandText = sb.ToString();
        inserted += cmd.ExecuteNonQuery();
    }

    return inserted;
}

static void BindParameter(SqlParameter param, JsonElement value, string type)
{
    var t = type.ToLowerInvariant();
    if (value.ValueKind == JsonValueKind.Null)
    {
        param.SqlDbType = MapSqlType(t);
        param.Value = DBNull.Value;
        return;
    }

    var s = value.GetString() ?? "";
    switch (t)
    {
        case "int": param.SqlDbType = SqlDbType.Int; param.Value = int.Parse(s, CultureInfo.InvariantCulture); break;
        case "bigint": param.SqlDbType = SqlDbType.BigInt; param.Value = long.Parse(s, CultureInfo.InvariantCulture); break;
        case "smallint": param.SqlDbType = SqlDbType.SmallInt; param.Value = short.Parse(s, CultureInfo.InvariantCulture); break;
        case "tinyint": param.SqlDbType = SqlDbType.TinyInt; param.Value = byte.Parse(s, CultureInfo.InvariantCulture); break;
        case "bit": param.SqlDbType = SqlDbType.Bit; param.Value = s is "true" or "1"; break;
        case "decimal": case "numeric": param.SqlDbType = SqlDbType.Decimal; param.Value = decimal.Parse(s, CultureInfo.InvariantCulture); break;
        case "money": param.SqlDbType = SqlDbType.Money; param.Value = decimal.Parse(s, CultureInfo.InvariantCulture); break;
        case "smallmoney": param.SqlDbType = SqlDbType.SmallMoney; param.Value = decimal.Parse(s, CultureInfo.InvariantCulture); break;
        case "float": param.SqlDbType = SqlDbType.Float; param.Value = double.Parse(s, CultureInfo.InvariantCulture); break;
        case "real": param.SqlDbType = SqlDbType.Real; param.Value = float.Parse(s, CultureInfo.InvariantCulture); break;
        case "datetime": param.SqlDbType = SqlDbType.DateTime; param.Value = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
        case "smalldatetime": param.SqlDbType = SqlDbType.SmallDateTime; param.Value = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
        case "datetime2": param.SqlDbType = SqlDbType.DateTime2; param.Value = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
        case "date": param.SqlDbType = SqlDbType.Date; param.Value = DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).Date; break;
        case "time": param.SqlDbType = SqlDbType.Time; param.Value = TimeSpan.Parse(s, CultureInfo.InvariantCulture); break;
        case "datetimeoffset": param.SqlDbType = SqlDbType.DateTimeOffset; param.Value = DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
        case "uniqueidentifier": param.SqlDbType = SqlDbType.UniqueIdentifier; param.Value = Guid.Parse(s); break;
        case "varbinary": case "binary": case "image": param.SqlDbType = SqlDbType.VarBinary; param.Size = -1; param.Value = Convert.FromBase64String(s); break;
        default: param.SqlDbType = SqlDbType.NVarChar; if (s.Length > 4000) param.Size = -1; param.Value = s; break;
    }
}

static SqlDbType MapSqlType(string t)
{
    switch (t)
    {
        case "int": return SqlDbType.Int;
        case "bigint": return SqlDbType.BigInt;
        case "smallint": return SqlDbType.SmallInt;
        case "tinyint": return SqlDbType.TinyInt;
        case "bit": return SqlDbType.Bit;
        case "decimal": case "numeric": return SqlDbType.Decimal;
        case "money": return SqlDbType.Money;
        case "smallmoney": return SqlDbType.SmallMoney;
        case "float": return SqlDbType.Float;
        case "real": return SqlDbType.Real;
        case "datetime": return SqlDbType.DateTime;
        case "smalldatetime": return SqlDbType.SmallDateTime;
        case "datetime2": return SqlDbType.DateTime2;
        case "date": return SqlDbType.Date;
        case "time": return SqlDbType.Time;
        case "datetimeoffset": return SqlDbType.DateTimeOffset;
        case "uniqueidentifier": return SqlDbType.UniqueIdentifier;
        case "varbinary": case "binary": case "image": return SqlDbType.VarBinary;
        default: return SqlDbType.NVarChar;
    }
}