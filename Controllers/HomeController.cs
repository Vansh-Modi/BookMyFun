using BookMyFun.Models;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BookMyFun.Controllers
{
    public class HomeController : Controller
    {
        private readonly IPropertyRepository _propRepo;
        private readonly IContactRepository _contactRepo;
        private readonly IPartnerLeadService _partnerLeadService;
        private readonly IEmailService _emailService;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IUserRepository _userRepo;

        public HomeController(
            IPropertyRepository propRepo,
            IContactRepository contactRepo,
            IPartnerLeadService partnerLeadService,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            IUserRepository userRepo)
        {
            _propRepo = propRepo;
            _contactRepo = contactRepo;
            _partnerLeadService = partnerLeadService;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _userRepo = userRepo;
        }

        public async Task<IActionResult> Index()
        {
            var properties = await _propRepo.GetAllPropertiesAsync(onlyApproved: true);
            var types = await _propRepo.GetPropertyTypesAsync();
            var occasions = await _propRepo.GetOccasionsAsync();

            ViewBag.FeaturedProperties = properties.Take(6).ToList();
            ViewBag.PropertyTypes = types;
            ViewBag.Occasions = occasions;

            return View();
        }

        [HttpGet]
        public IActionResult Partner()
        {
            return View(new BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Partner(BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            int leadId = await _partnerLeadService.CreateLeadAsync(model);

            // Notify Operations team
            var staffList = (await _userRepo.GetUsersByRoleAsync("Employee"))
                .Concat(await _userRepo.GetUsersByRoleAsync("Admin"))
                .ToList();

            string subject = $"[Host Lead #{leadId}] New Farm Host Partner Application: {model.PropertyName}";
            string body = $@"
                <h3>New Farm Host Application Received</h3>
                <p><strong>Lead ID:</strong> #{leadId}<br/>
                <strong>Owner Name:</strong> {model.FullName}<br/>
                <strong>Email:</strong> {model.Email}<br/>
                <strong>Phone:</strong> {model.Phone}<br/>
                <strong>Property Name:</strong> {model.PropertyName}<br/>
                <strong>Location:</strong> {model.Location}<br/>
                <strong>Acreage / Type:</strong> {model.AcreageOrType}</p>
                <p><strong>Message:</strong> {model.Message ?? "N/A"}</p>
                <p>Please review and verify this host application from your Admin/Employee Dashboard.</p>";

            foreach (var staff in staffList)
            {
                _ = _emailService.SendEmailAsync(staff.Email, subject, body);
            }

            TempData["Success"] = "Thank you for applying to partner with BookMyFun! Our onboarding team will inspect your property details and contact you with your activation link shortly.";
            return RedirectToAction(nameof(Partner));
        }

        [HttpGet]
        public IActionResult Contact()
        {
            return View(new ContactUs());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contact(ContactUs model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Message))
            {
                ModelState.AddModelError("", "Please fill in all mandatory fields.");
                return View(model);
            }

            int contactId = await _contactRepo.CreateContactAsync(model);

            // Notify Customer Support Executives and Staff
            var staffList = (await _userRepo.GetUsersByRoleAsync("Employee"))
                .Concat(await _userRepo.GetUsersByRoleAsync("Admin"))
                .ToList();

            string subject = $"[Customer Query #{contactId}] From {model.Name}: {model.Subject ?? "Help Needed"}";
            string body = $@"
                <h3>New Customer Support Query Received</h3>
                <p><strong>Ticket ID:</strong> #{contactId}<br/>
                <strong>Customer Name:</strong> {model.Name}<br/>
                <strong>Email:</strong> {model.Email}<br/>
                <strong>Phone:</strong> {model.Phone ?? "N/A"}<br/>
                <strong>Subject:</strong> {model.Subject ?? "General Inquiry"}</p>
                <p><strong>Message:</strong><br/>{model.Message}</p>
                <p>Please reach out to the customer promptly to assist with their booking.</p>";

            foreach (var staff in staffList)
            {
                _ = _emailService.SendEmailAsync(staff.Email, subject, body);
            }

            TempData["Success"] = "Thank you for reaching out! A dedicated customer support executive has received your query and will contact you shortly.";
            return RedirectToAction(nameof(Contact));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
