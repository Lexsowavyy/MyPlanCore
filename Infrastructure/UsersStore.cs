using Microsoft.EntityFrameworkCore;
using PlanCoreMAUI.Data;

namespace PlanCoreMAUI.Components.Pages.Admin
{
    // Web port of the in-memory admin user list that the Blazor Users page kept as a
    // public static field. The dashboards and archived-users page reference Users.users.
    public static class Users
    {
        public static List<User> users = new()
        {
            new User { Id = 1, FirstName = "Admin", LastName = "User", FullName = "Admin User", Username = "admin@plancore.com", Email = "admin@plancore.com", Role = "Admin", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now, IsArchived = false },
            new User { Id = 2, FirstName = "John", LastName = "Smith", FullName = "John Smith", Username = "projectmanager@plancore.com", Email = "projectmanager@plancore.com", Role = "ProjectManager", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now.AddHours(-2), IsArchived = false },
            new User { Id = 3, FirstName = "Jane", LastName = "Doe", FullName = "Jane Doe", Username = "architect@plancore.com", Email = "architect@plancore.com", Role = "Architect", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now.AddDays(-1), IsArchived = false },
            new User { Id = 4, FirstName = "Alexa", LastName = "Architect", FullName = "Alexa Architect", Username = "alexaarchitect@plancore.com", Email = "alexaarchitect@plancore.com", Role = "Architect", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now, IsArchived = false },
            new User { Id = 5, FirstName = "Mike", LastName = "Johnson", FullName = "Mike Johnson", Username = "engineer@plancore.com", Email = "engineer@plancore.com", Role = "SiteEngineer", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now.AddHours(-5), IsArchived = false },
            new User { Id = 6, FirstName = "Sarah", LastName = "Williams", FullName = "Sarah Williams", Username = "qs@plancore.com", Email = "qs@plancore.com", Role = "QuantitySurveyor", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now.AddDays(-2), IsArchived = false },
            new User { Id = 7, FirstName = "Robert", LastName = "Brown", FullName = "Robert Brown", Username = "finance@plancore.com", Email = "finance@plancore.com", Role = "FinanceOfficer", AdminEmail = "admin@plancore.com", Status = "Active", LastActive = DateTime.Now.AddHours(-8), IsArchived = false },
        };

        public static void EnsureUsersSynced()
        {
            var existingEmails = new HashSet<string>(users.Select(u => u.Email.ToLower().Trim()));
            var authUsers = PlanCoreMAUI.Services.AuthService.GetAllUsers();
            int nextId = users.Count > 0 ? users.Max(u => u.Id) + 1 : 1;
            foreach (var (email, role) in authUsers)
            {
                if (role == "SuperAdmin")
                    continue;
                if (!existingEmails.Contains(email.ToLower().Trim()))
                {
                    AddUserToList(email, role, ref nextId, existingEmails);
                }
            }
            // Also sync from database directly
            try
            {
                using var scope = MauiProgram.GetServiceProvider()?.CreateScope();
                if (scope != null)
                {
                    var db = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();
                    var dbUsers = db.UserAccounts.Where(u => u.Role != "SuperAdmin").AsNoTracking().ToList();
                    foreach (var u in dbUsers)
                    {
                        var email = u.Email.ToLower().Trim();
                        if (!existingEmails.Contains(email))
                        {
                            AddUserToList(email, u.Role, ref nextId, existingEmails, u.MfaEnabled);
                        }
                    }
                }
            }
            catch { }
        }

        private static void AddUserToList(string email, string role, ref int nextId, HashSet<string> existingEmails, bool mfaEnabled = false)
        {
            var displayName = email.Split('@')[0];
            displayName = string.Join(" ", System.Text.RegularExpressions.Regex.Split(displayName, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])"))
                .Replace(".", " ").Replace("_", " ");
            var ti = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo;
            displayName = ti.ToTitleCase(displayName.ToLower());
            users.Add(new User
            {
                Id = nextId++,
                FirstName = displayName,
                LastName = "",
                FullName = displayName,
                Username = email.ToLower().Trim(),
                Email = email.ToLower().Trim(),
                Role = role,
                AdminEmail = email,
                Status = "Active",
                LastActive = DateTime.Now,
                IsArchived = false,
                MfaEnabled = mfaEnabled,
            });
            existingEmails.Add(email.ToLower().Trim());
        }

        public class User
        {
            public int Id { get; set; }
            public string FirstName { get; set; } = "";
            public string LastName { get; set; } = "";
            public string FullName { get; set; } = "";
            public string Username { get; set; } = "";
            public string Email { get; set; } = "";
            public string Password { get; set; } = "";
            public string Role { get; set; } = "";
            public string? AdminEmail { get; set; }
            public string Status { get; set; } = "";
            public DateTime? LastActive { get; set; }
            public DateTime? PasswordChangedAt { get; set; }
            public bool IsArchived { get; set; }
            public DateTime? ArchivedOn { get; set; }
            public bool MfaEnabled { get; set; }
        }
    }
}