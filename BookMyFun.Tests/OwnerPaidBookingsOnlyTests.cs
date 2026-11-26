using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BookMyFun.Tests
{
    public class OwnerPaidBookingsOnlyTests
    {
        private class StubProxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var rt = targetMethod!.ReturnType;
                if (rt == typeof(Task<List<Property>>)) return Task.FromResult(new List<Property>());
                if (rt == typeof(Task<List<SubscriptionPlan>>)) return Task.FromResult(new List<SubscriptionPlan>());
                if (rt == typeof(Task<List<PropertyType>>)) return Task.FromResult(new List<PropertyType>());
                if (rt == typeof(Task<User?>)) return Task.FromResult<User?>(new User { UserID = 5, FullName = "Owner" });
                if (rt == typeof(Task<OwnerSubscription?>)) return Task.FromResult<OwnerSubscription?>(null);
                if (rt == typeof(Task<(bool, string?, int, int)>)) return Task.FromResult((true, (string?)null, 0, 1));
                if (rt == typeof(Task<bool>)) return Task.FromResult(false);
                return null;
            }

            public static T Create<T>() where T : class => DispatchProxy.Create<T, StubProxy>();
        }

        private class MockBookingRepo : IBookingRepository
        {
            public List<Booking> Bookings { get; } = new();
            public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(Bookings.ToList());
            public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(Bookings.ToList());
            public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingID));
            public Task<Booking?> GetBookingByIdAsync(int bookingId) => Task.FromResult(Bookings.FirstOrDefault(b => b.BookingID == bookingId));
            public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
            public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
            public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(1);
            public Task<bool> UpdateBookingStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
            public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
            public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
            public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
            public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
            public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
            public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
            public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
        }

        [Fact]
        public async Task OwnerDashboard_ExcludesUnpaidPendingAndFailedBookings_IncludesConfirmed()
        {
            var bookingRepo = new MockBookingRepo();
            bookingRepo.Bookings.Add(new Booking { BookingID = 1, PropertyID = 10, TotalAmount = 10000m, Status = "Pending" });
            bookingRepo.Bookings.Add(new Booking { BookingID = 2, PropertyID = 10, TotalAmount = 12000m, Status = "PaymentFailed" });
            bookingRepo.Bookings.Add(new Booking { BookingID = 3, PropertyID = 10, TotalAmount = 10000m, CommissionRateSnapshot = 10m, Status = "Confirmed" });

            var controller = new OwnerController(
                StubProxy.Create<IPropertyRepository>(),
                StubProxy.Create<IUserRepository>(),
                bookingRepo,
                StubProxy.Create<ISubscriptionRepository>(),
                null!,
                null!,
                null!,
                null!);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "5"),
                        new Claim(ClaimTypes.Role, "Owner")
                    }, "Test"))
                }
            };

            var res = await controller.Index();
            var view = Assert.IsType<ViewResult>(res);
            var vm = Assert.IsType<OwnerDashboardViewModel>(view.Model);

            Assert.Single(vm.Bookings);
            Assert.Equal(3, vm.Bookings[0].BookingID);
            Assert.Equal(9000m, vm.TotalEarnings);
        }

        [Fact]
        public void BookingController_DoesNotDispatchStaffAlertBeforeVerifiedPayment()
        {
            var path = TestPathHelper.GetPath("Controllers", "BookingController.cs");
            var code = File.ReadAllText(path);

            var confirmIdx = code.IndexOf("CreateHoldAtomicAsync(booking", StringComparison.Ordinal);
            var verifyIdx = code.IndexOf("ConfirmBookingPaymentAsync(booking.BookingID", StringComparison.Ordinal);
            var staffAlertIdx = code.IndexOf("DispatchBookingCreatedStaffAlertAsync", StringComparison.Ordinal);

            Assert.True(confirmIdx > 0);
            Assert.True(verifyIdx > confirmIdx);
            Assert.True(staffAlertIdx > verifyIdx);
        }
    }
}
