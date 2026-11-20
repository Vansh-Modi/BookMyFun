using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Policy = "RequireClient")]
    public class ClientController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IIdVerificationService _idVerificationService;
        private readonly IRefundRepository? _refundRepo;
        private readonly IHtmlSanitizerService? _sanitizer;

        public ClientController(
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            IIdVerificationService idVerificationService,
            IRefundRepository? refundRepo = null,
            IHtmlSanitizerService? sanitizer = null)
        {
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _idVerificationService = idVerificationService;
            _refundRepo = refundRepo;
            _sanitizer = sanitizer;
        }

        public async Task<IActionResult> Index()
        {
            var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var client = await _userRepo.GetByIdAsync(clientId);
            var bookings = await _bookingRepo.GetByClientIdAsync(clientId);
            var verification = await _idVerificationService.GetClientVerificationAsync(clientId, clientId, "Client");

            TimeZoneInfo istZone;
            try
            {
                istZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            catch
            {
                istZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
            var todayInIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone).Date;

            var refundMap = new Dictionary<int, BookingRefund>();
            if (_refundRepo != null)
            {
                foreach (var b in bookings.Where(b => b.Status == "Cancelled"))
                {
                    var r = await _refundRepo.GetRefundByBookingIdAsync(b.BookingID);
                    if (r != null)
                    {
                        refundMap[b.BookingID] = r;
                    }
                }
            }

            ViewBag.Verification = verification;
            ViewBag.TodayInIst = todayInIst;
            ViewBag.RefundMap = refundMap;

            var vm = new ClientDashboardViewModel
            {
                Client = client!,
                Bookings = bookings
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "RequireClient")]
        public async Task<IActionResult> AddReview(int bookingId, int propertyId, int rating, string? comment)
        {
            var clientIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(clientIdClaim) || !int.TryParse(clientIdClaim, out int clientId))
            {
                return Challenge();
            }

            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null || booking.ClientID != clientId || booking.PropertyID != propertyId)
            {
                TempData["Error"] = "Booking not found or you are not authorized to review this stay.";
                return Forbid();
            }

            if (!string.Equals(booking.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Reviews can only be submitted for completed stays.";
                return RedirectToAction(nameof(Index));
            }

            if (await _bookingRepo.HasReviewForBookingAsync(bookingId))
            {
                TempData["Error"] = "You have already submitted a review for this booking. Only one review per booking is permitted.";
                return RedirectToAction(nameof(Index));
            }

            string? cleanComment = _sanitizer != null ? _sanitizer.Sanitize(comment) : comment;

            var review = new CustomerReview
            {
                BookingID = bookingId,
                PropertyID = propertyId,
                ClientID = clientId,
                Rating = Math.Clamp(rating, 1, 5),
                Comment = cleanComment
            };

            await _bookingRepo.AddReviewAsync(review);
            TempData["Success"] = "Thank you! Your review has been submitted.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Receipts()
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
