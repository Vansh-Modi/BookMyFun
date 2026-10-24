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
        private readonly ILegalDocumentService _legalService;

        public HomeController(
            IPropertyRepository propRepo,
            IContactRepository contactRepo,
            IPartnerLeadService partnerLeadService,
            IEmailService emailService,
            IWhatsAppService whatsAppService,
            IUserRepository userRepo,
            ILegalDocumentService legalService)
        {
            _propRepo = propRepo;
            _contactRepo = contactRepo;
            _partnerLeadService = partnerLeadService;
            _emailService = emailService;
            _whatsAppService = whatsAppService;
            _userRepo = userRepo;
            _legalService = legalService;
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
        public IActionResult Partner(string? type)
        {
            var initialType = string.Equals(type, "eventmanager", StringComparison.OrdinalIgnoreCase) 
                ? "EventManager" 
                : "Owner";
            ViewBag.InitialType = initialType;
            return View(new BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel { PartnerType = initialType });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Partner(BookMyFun.Models.ViewModels.PartnerLeadCreateViewModel model)
        {
            if (!Request.Form.ContainsKey("PolicyConsent") || Request.Form["PolicyConsent"] != "true")
            {
                ModelState.AddModelError("", "You must accept the Terms of Service, Privacy Policy, and Refund Policy before submitting your partner application.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.InitialType = model.PartnerType ?? "Owner";
                return View(model);
            }

            int leadId = await _partnerLeadService.CreateLeadAsync(model);

            // Record policy consent acceptance
            try
            {
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                string pageUrl = Request.Path.Value ?? "/Home/Partner";
                await _legalService.RecordAllStandardConsentsAsync(null, leadId, ip, pageUrl);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
                ViewBag.InitialType = model.PartnerType ?? "Owner";
                return View(model);
            }

            bool isEventManager = string.Equals(model.PartnerType, "EventManager", StringComparison.OrdinalIgnoreCase);
            string roleLabel = isEventManager ? "Event Manager & Décor Studio" : "Farmhouse Host";

            // Notify Operations team
            var staffList = (await _userRepo.GetUsersByRoleAsync("Employee"))
                .Concat(await _userRepo.GetUsersByRoleAsync("Admin"))
                .ToList();

            string subject = $"[{roleLabel} Lead #{leadId}] New Partner Application: {model.PropertyName}";
            string body = $@"
                <h3>New {roleLabel} Application Received</h3>
                <p><strong>Lead ID:</strong> #{leadId}<br/>
                <strong>Candidate Name:</strong> {model.FullName}<br/>
                <strong>Email:</strong> {model.Email}<br/>
                <strong>Phone:</strong> {model.Phone}<br/>
                <strong>{(isEventManager ? "Studio / Brand Name" : "Property Name")}:</strong> {model.PropertyName}<br/>
                <strong>{(isEventManager ? "Operating Region" : "Location")}:</strong> {model.Location}<br/>
                <strong>{(isEventManager ? "Specialization" : "Acreage / Type")}:</strong> {model.AcreageOrType}</p>
                <p><strong>Message:</strong> {model.Message ?? "N/A"}</p>
                <p>Please review and verify this application from your Admin/Employee Dashboard.</p>";

            foreach (var staff in staffList)
            {
                await _emailService.EnqueueEmailAsync(staff.Email, subject, body, "StaffPartnerNotification");
            }

            TempData["Success"] = "Thank you for submitting your partner application! Partner verification takes standard 24 to 48 hours. You will be notified by email and message once verification is complete.";

            return RedirectToAction(nameof(Partner), new { type = isEventManager ? "eventmanager" : "owner" });
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
            if (!Request.Form.ContainsKey("PolicyConsent") || Request.Form["PolicyConsent"] != "true")
            {
                ModelState.AddModelError("", "You must read and agree to the Privacy Policy to submit an inquiry.");
            }

            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Message))
            {
                ModelState.AddModelError("", "Please fill in all mandatory fields.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Record policy consent acceptance (privacy notice only)
            try
            {
                string ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                string pageUrl = Request.Path.Value ?? "/Home/Contact";
                await _legalService.RecordConsentAsync(null, null, "PrivacyPolicy", ip, pageUrl);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", ex.Message);
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
                await _emailService.EnqueueEmailAsync(staff.Email, subject, body, "StaffPartnerNotification");
            }

            TempData["Success"] = "Thank you for reaching out! A dedicated customer support executive has received your query and will contact you shortly.";
            return RedirectToAction(nameof(Contact));
        }

        [HttpGet("/privacy-policy")]
        public async Task<IActionResult> PrivacyPolicy()
        {
            var doc = await _legalService.GetCurrentDocumentAsync("PrivacyPolicy");
            return View("LegalDocumentView", doc ?? new LegalDocument
            {
                Title = "Privacy Policy",
                DocumentType = "PrivacyPolicy",
                Body = "<p>Policy not yet published. Please contact support.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true
            });
        }

        [HttpGet("/terms")]
        public async Task<IActionResult> Terms()
        {
            var doc = await _legalService.GetCurrentDocumentAsync("TermsOfService");
            return View("LegalDocumentView", doc ?? new LegalDocument
            {
                Title = "Terms of Service",
                DocumentType = "TermsOfService",
                Body = "<p>Policy not yet published. Please contact support.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true
            });
        }

        [HttpGet("/refund-policy")]
        public async Task<IActionResult> RefundPolicy()
        {
            var doc = await _legalService.GetCurrentDocumentAsync("RefundPolicy");
            return View("LegalDocumentView", doc ?? new LegalDocument
            {
                Title = "Refund and Cancellation Policy",
                DocumentType = "RefundPolicy",
                Body = "<p>Policy not yet published. Please contact support.</p>",
                Version = "1.0",
                EffectiveDate = DateTime.UtcNow,
                IsDraft = true
            });
        }

        public IActionResult Privacy()
        {
            return RedirectToAction(nameof(PrivacyPolicy));
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
