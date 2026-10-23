using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Client")]
    public class ClientController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;
        private readonly IIdVerificationService _idVerificationService;

        public ClientController(
            IUserRepository userRepo,
            IBookingRepository bookingRepo,
            IIdVerificationService idVerificationService)
        {
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
            _idVerificationService = idVerificationService;
        }

        public async Task<IActionResult> Index()
        {
            var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var client = await _userRepo.GetByIdAsync(clientId);
            var bookings = await _bookingRepo.GetByClientIdAsync(clientId);
            var verification = await _idVerificationService.GetClientVerificationAsync(clientId, clientId, "Client");

            ViewBag.Verification = verification;

            var vm = new ClientDashboardViewModel
            {
                Client = client!,
                Bookings = bookings
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddReview(int bookingId, int propertyId, int rating, string? comment)
        {
            var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var booking = await _bookingRepo.GetByIdAsync(bookingId);
            if (booking == null || booking.ClientID != clientId || booking.PropertyID != propertyId)
            {
                return Forbid();
            }

            var review = new CustomerReview
            {
                BookingID = bookingId,
                PropertyID = propertyId,
                ClientID = clientId,
                Rating = Math.Clamp(rating, 1, 5),
                Comment = comment
            };

            await _bookingRepo.AddReviewAsync(review);
            TempData["Success"] = "Thank you! Your review has been submitted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
