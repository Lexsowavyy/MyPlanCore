using Microsoft.AspNetCore.Mvc;

namespace PlanCore.Web.Controllers
{
    // Port of Components\Pages\NotFound.razor ("/not-found") plus the catch-all
    // fallback for any URL that does not match a known page (same as the Blazor router).
    public class NotFoundController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }
    }
}