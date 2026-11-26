using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Notifications;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BookMyFun.Tests
{
    public class BookingHoldLifecycleTests
    {
        private class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class InMemoryBookingRepo : IBookingRepository
        {
            private int _nextId = 1;
            public List<Booking> Bookings { get; } = new();
            public List<Payment> Payments { get; } = new();
            public int ExpireCalls { get; private set; }

            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingID));
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingId));
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult(Bookings.FirstOrDefault(b => b.IdempotencyKey == idempotencyKey));
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(Bookings.Where(b => b.ClientID == clientId && (b.Status == "Confirmed" || b.Status == "Paid")).ToList());
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(Bookings.Where(b => b.Status == "Confirmed" || b.Status == "Paid").ToList());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.ToList());
            public Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId) => Task.FromResult(Bookings.Where(b => b.PropertyID == propertyId).ToList());

            public Task<int> CreateBookingAsync(Booking booking)
            {
                booking.BookingID = _nextId++;
                Bookings.Add(booking);
                return Task.FromResult(booking.BookingID);
            }

            public Task<(bool Available, int BookingId)> CreateHoldAtomicAsync(Booking booking, TimeSpan? hourlyCheckInTime = null, int? hourlyDuration = null)
            {
                var svc = new AvailabilityService(this);
                var ok = svc.IsAvailableAsync(booking.PropertyID, booking.CheckInDate, booking.CheckOutDate, null, booking.BookingType, booking.SlotType, hourlyCheckInTime, hourlyDuration).GetAwaiter().GetResult();
                if (!ok) return Task.FromResult((false, 0));
                booking.BookingID = _nextId++;
                Bookings.Add(booking);
                return Task.FromResult((true, booking.BookingID));
            }

            public Task<Booking?> FindActiveHoldAsync(int clientId, int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
            {
                var utcNow = DateTime.UtcNow;
                var match = Bookings.FirstOrDefault(b =>
                    b.ClientID == clientId &&
                    b.PropertyID == propertyId &&
                    b.CheckInDate.Date == checkInDate.Date &&
                    b.CheckOutDate.Date == checkOutDate.Date &&
                    string.Equals(b.BookingType, bookingType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(b.SlotType, slotType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(b.Status, "Hold", StringComparison.OrdinalIgnoreCase) &&
                    b.HoldExpiresAt.HasValue && b.HoldExpiresAt.Value > utcNow);
                return Task.FromResult(match);
            }

            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status)
            {
                var b = Bookings.FirstOrDefault(x => x.BookingID == bookingId);
                if (b == null) return Task.FromResult(false);
                b.Status = status;
                return Task.FromResult(true);
            }

            public Task<int> ExpireStaleHoldsAsync()
            {
                ExpireCalls++;
                int count = 0;
                var utcNow = DateTime.UtcNow;
                foreach (var b in Bookings.Where(x => x.Status == "Hold" && x.HoldExpiresAt.HasValue && x.HoldExpiresAt.Value <= utcNow))
                {
                    b.Status = "Expired";
                    count++;
                }
                return Task.FromResult(count);
            }

            public Task<bool> CancelBookingAsync(int bookingId) => UpdateBookingStatusAsync(bookingId, "Cancelled");
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
                => new AvailabilityService(this).IsAvailableAsync(propertyId, checkInDate, checkOutDate, null, bookingType, slotType);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId)
                => new AvailabilityService(this).IsAvailableAsync(propertyId, checkInDate, checkOutDate, excludeBookingId, bookingType, slotType);
            public Task<int> RecordPaymentAsync(Payment payment)
            {
                Payments.Add(payment);
                return Task.FromResult(Payments.Count);
            }
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(Payments.Where(p => p.BookingID == bookingId).ToList());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        private class StubRepo : DispatchProxy
        {
            public Property TestProp { get; } = new Property
            {
                PropertyID = 10,
                OwnerID = 5,
                Title = "Lakefront Villa",
                PricePerNight = 10000m,
                MinStayNights = 1,
                MaxStayNights = 30,
                ListingStatus = "Live"
            };

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var rt = targetMethod!.ReturnType;
                if (rt == typeof(Task<Property?>)) return Task.FromResult<Property?>(TestProp);
                if (targetMethod.Name == nameof(IPropertyRepository.IsPropertyPubliclyVisibleAsync)) return Task.FromResult(true);
                if (rt == typeof(Task<User?>)) return Task.FromResult<User?>(new User { UserID = 20, FullName = "Guest User", Phone = "9876543210", Email = "guest@example.com", Role = "Client" });
                if (rt == typeof(Task<bool>)) return Task.FromResult(true);
                if (rt == typeof(Task<ClientIdVerification?>)) return Task.FromResult<ClientIdVerification?>(null);
                if (rt == typeof(Task<(decimal, decimal, decimal)>)) return Task.FromResult((10m, 1000m, 9000m));
                if (rt == typeof(Task<List<Occasion>>)) return Task.FromResult(new List<Occasion>());
                if (rt == typeof(Task<List<EventPackage>>)) return Task.FromResult(new List<EventPackage>());
                if (rt == typeof(Task<List<EventManagerPortfolio>>)) return Task.FromResult(new List<EventManagerPortfolio>());
                if (rt == typeof(Task<List<int>>)) return Task.FromResult(new List<int>());
                return null;
            }

            public static T Create<T>() where T : class => DispatchProxy.Create<T, StubRepo>();
        }

        private static BookingController CreateBookingController(InMemoryBookingRepo bookingRepo, int clientId = 20)
        {
            var controller = new BookingController(
                StubRepo.Create<IPropertyRepository>(),
                bookingRepo,
                StubRepo.Create<IEventPackageRepository>(),
                new BookingPricingService(),
                StubRepo.Create<ICommissionService>(),
                null!, null!, null!, null!, null!,
                StubRepo.Create<IUserRepository>(),
                StubRepo.Create<IIdVerificationService>(),
                null!,
                availabilityService: new AvailabilityService(bookingRepo));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, clientId.ToString()),
                        new Claim(ClaimTypes.Role, "Client")
                    }, "Test"))
                }
            };
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());
            return controller;
        }

        [Fact]
        public async Task GuestWhoAbandonsPaymentAndReturnsWithinHoldWindow_ReusesSameHoldAndIsNotBlocked()
        {
            var repo = new InMemoryBookingRepo();
            var controller = CreateBookingController(repo, clientId: 20);
            var checkIn = new DateTime(2026, 11, 26);
            var checkOut = new DateTime(2026, 11, 27);

            // First submit creates Hold
            var model = new BookingCheckoutViewModel
            {
                PropertyID = 10,
                BookingType = "FullDay",
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                GuestFullName = "Guest User",
                GuestPhone = "9876543210",
                IsClientAlreadyVerified = true
            };

            var res1 = await controller.ConfirmBooking(model);
            var r1 = Assert.IsType<RedirectToActionResult>(res1);
            Assert.Equal("Payment", r1.ActionName);
            Assert.Single(repo.Bookings);
            Assert.Equal("Hold", repo.Bookings[0].Status);
            int firstHoldId = repo.Bookings[0].BookingID;

            // Guest returns to Checkout for the same dates -> still available because own hold is excluded
            var checkoutRes = await controller.Checkout(10, "FullDay", null, 1, checkIn, checkOut);
            Assert.IsType<ViewResult>(checkoutRes);

            // Guest submits again within 15 min -> reuses same hold, no duplicate row
            var res2 = await controller.ConfirmBooking(model);
            var r2 = Assert.IsType<RedirectToActionResult>(res2);
            Assert.Equal("Payment", r2.ActionName);
            Assert.Equal(firstHoldId, r2.RouteValues!["id"]);
            Assert.Single(repo.Bookings);
        }

        [Fact]
        public async Task ExpiredHold_ShowsAvailableToOtherGuest_AndPaymentRedirectsWithExpiryMessage()
        {
            var repo = new InMemoryBookingRepo();
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = 10,
                ClientID = 20,
                CheckInDate = new DateTime(2026, 11, 26),
                CheckOutDate = new DateTime(2026, 11, 27),
                BookingType = "FullDay",
                Status = "Hold",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(-1) // Expired
            });

            var availSvc = new AvailabilityService(repo);
            bool availableForOtherGuest = await availSvc.IsAvailableAsync(10, new DateTime(2026, 11, 26), new DateTime(2026, 11, 27));
            Assert.True(availableForOtherGuest);

            var controller = CreateBookingController(repo, clientId: 20);
            var payRes = await controller.Payment(1);
            var redirect = Assert.IsType<RedirectToActionResult>(payRes);
            Assert.Equal("Checkout", redirect.ActionName);
            Assert.Equal("Your hold on these dates has expired. Please select your dates again.", controller.TempData["Error"]);
            Assert.Equal("Expired", repo.Bookings[0].Status);
        }

        [Fact]
        public async Task ConcurrentOrSecondGuest_CannotTakeActiveHoldDates()
        {
            var repo = new InMemoryBookingRepo();
            var (ok1, id1) = await repo.CreateHoldAtomicAsync(new Booking
            {
                PropertyID = 10,
                ClientID = 20,
                CheckInDate = new DateTime(2026, 11, 26),
                CheckOutDate = new DateTime(2026, 11, 27),
                BookingType = "FullDay",
                Status = "Hold",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            Assert.True(ok1);
            Assert.True(id1 > 0);

            var (ok2, id2) = await repo.CreateHoldAtomicAsync(new Booking
            {
                PropertyID = 10,
                ClientID = 21, // Different guest
                CheckInDate = new DateTime(2026, 11, 26),
                CheckOutDate = new DateTime(2026, 11, 27),
                BookingType = "FullDay",
                Status = "Hold",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
            Assert.False(ok2);
            Assert.Equal(0, id2);
            Assert.Single(repo.Bookings);
        }

        [Fact]
        public async Task Calendar_Search_And_AvailabilityService_AgreeOnAllStatuses()
        {
            var repo = new InMemoryBookingRepo();
            // Active Hold: Nov 10-12 (blocks Nov 10, 11)
            repo.Bookings.Add(new Booking { BookingID = 1, PropertyID = 10, CheckInDate = new DateTime(2026, 11, 10), CheckOutDate = new DateTime(2026, 11, 12), BookingType = "FullDay", Status = "Hold", HoldExpiresAt = DateTime.UtcNow.AddMinutes(10) });
            // Expired Hold: Nov 15-17 (does NOT block)
            repo.Bookings.Add(new Booking { BookingID = 2, PropertyID = 10, CheckInDate = new DateTime(2026, 11, 15), CheckOutDate = new DateTime(2026, 11, 17), BookingType = "FullDay", Status = "Hold", HoldExpiresAt = DateTime.UtcNow.AddMinutes(-2) });
            // Expired / Failed / Cancelled: Nov 18-20 (does NOT block)
            repo.Bookings.Add(new Booking { BookingID = 3, PropertyID = 10, CheckInDate = new DateTime(2026, 11, 18), CheckOutDate = new DateTime(2026, 11, 19), BookingType = "FullDay", Status = "Expired" });
            repo.Bookings.Add(new Booking { BookingID = 4, PropertyID = 10, CheckInDate = new DateTime(2026, 11, 19), CheckOutDate = new DateTime(2026, 11, 20), BookingType = "FullDay", Status = "Failed" });
            // Paid: Nov 22-24 (blocks Nov 22, 23)
            repo.Bookings.Add(new Booking { BookingID = 5, PropertyID = 10, CheckInDate = new DateTime(2026, 11, 22), CheckOutDate = new DateTime(2026, 11, 24), BookingType = "FullDay", Status = "Paid" });

            var availSvc = new AvailabilityService(repo);
            var calSvc = new CalendarRangeService(repo, availSvc);
            var suggSvc = new AvailabilitySuggestionService(repo, Options.Create(new BookingTimingOptions()), availSvc);

            var blocked = await calSvc.GetBlockedDatesForPropertyAsync(10);
            Assert.Contains("2026-11-10", blocked);
            Assert.Contains("2026-11-11", blocked);
            Assert.DoesNotContain("2026-11-15", blocked);
            Assert.DoesNotContain("2026-11-18", blocked);
            Assert.DoesNotContain("2026-11-19", blocked);
            Assert.Contains("2026-11-22", blocked);
            Assert.Contains("2026-11-23", blocked);

            Assert.False(await availSvc.IsAvailableAsync(10, new DateTime(2026, 11, 10), new DateTime(2026, 11, 12)));
            Assert.True(await availSvc.IsAvailableAsync(10, new DateTime(2026, 11, 15), new DateTime(2026, 11, 17)));
            Assert.True(await availSvc.IsAvailableAsync(10, new DateTime(2026, 11, 18), new DateTime(2026, 11, 20)));
            Assert.False(await availSvc.IsAvailableAsync(10, new DateTime(2026, 11, 22), new DateTime(2026, 11, 24)));

            var sugg15 = await suggSvc.GetNearestSuggestionsAsync(10, new DateTime(2026, 11, 15), new DateTime(2026, 11, 17), "FullDay", null, new DateTime(2026, 11, 1));
            Assert.True(sugg15.IsRequestedAvailable);
        }

        [Fact]
        public async Task BackgroundExpiryService_MarksExpiredHoldsWithoutDeletingRows()
        {
            var repo = new InMemoryBookingRepo();
            repo.Bookings.Add(new Booking
            {
                BookingID = 1,
                PropertyID = 10,
                Status = "Hold",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(-5)
            });

            var services = new ServiceCollection();
            services.AddSingleton<IBookingRepository>(repo);
            var sp = services.BuildServiceProvider();

            var job = new BookingHoldExpiryBackgroundService(sp, NullLogger<BookingHoldExpiryBackgroundService>.Instance);
            int expired = await job.RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, expired);
            Assert.Single(repo.Bookings); // Row is NOT deleted
            Assert.Equal("Expired", repo.Bookings[0].Status);
        }
    }
}
