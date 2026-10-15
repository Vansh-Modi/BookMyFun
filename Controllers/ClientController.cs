using System.Security.Claims;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyFun.Controllers
{
    [Authorize(Roles = "Client")]
    public class ClientController : Controller
    {
        private readonly IUserRepository _userRepo;
        private readonly IBookingRepository _bookingRepo;

        public ClientController(IUserRepository userRepo, IBookingRepository bookingRepo)
        {
            _userRepo = userRepo;
            _bookingRepo = bookingRepo;
        }

        public async Task<IActionResult> Index()
        {
            var clientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var client = await _userRepo.GetByIdAsync(clientId);
            var bookings = await _bookingRepo.GetByClientIdAsync(clientId);

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
