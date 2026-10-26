using System;
using System.Collections.Generic;
using Users = PlanCoreMAUI.Components.Pages.Admin.Users;

namespace PlanCore.Web.ViewModels
{
    public class UsersViewModel
    {
        public string Search { get; set; } = "";
        public string SelectedRole { get; set; } = "";
        public string SelectedStatus { get; set; } = "";

        public string? Modal { get; set; }
        public string? ModalError { get; set; }
        public string EditErrorMessage { get; set; } = "";
        public string PasswordErrorMessage { get; set; } = "";
        public int? ModalId { get; set; }

        public Users.User? SelectedUser { get; set; }
        public Users.User? EditUser { get; set; }

        public string AddFirstName { get; set; } = "";
        public string AddLastName { get; set; } = "";
        public string AddUsername { get; set; } = "";
        public string AddRole { get; set; } = "ProjectManager";
        public string AddStatus { get; set; } = "Active";
    }
}