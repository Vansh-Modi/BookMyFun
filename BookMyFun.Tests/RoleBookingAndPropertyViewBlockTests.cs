using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.ViewModels;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class RoleBookingAndPropertyViewBlockTests
    {
        private class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class StubRepo : DispatchProxy
        {
            public Property TestProp { get; } = new Property
            {
                PropertyID = 10,
                OwnerID = 5,
                Title = "Public Villa",
                PricePerNight = 5000m,
                ListingStatus = "Live"
            };

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var rt = targetMethod!.ReturnType;
                if (targetMethod.Name == nameof(IPropertyRepository.GetByIdAsync))
                    return Task.FromResult<Property?>(TestProp);
                if (targetMethod.Name == nameof(IPropertyRepository.IsPropertyPubliclyVisibleAsync))
                    return Task.FromResult(true);
                if (rt == typeof(Task<List<CustomerReview>>))
                    return Task.FromResult(new List<CustomerReview>());
                if (rt == typeof(Task<List<Booking>>))
                    return Task.FromResult(new List<Booking>());
                return null;
            }

            public static T Create<T>() where T : class => DispatchProxy.Create<T, StubRepo>();
        }

        private static ControllerContext CreateRoleContext(string? role)
        {
            var identity = string.IsNullOrEmpty(role)
                ? new ClaimsIdentity()
                : new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "5"),
                    new Claim(ClaimTypes.Role, role)
                }, "Test");

            return new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };
        }

        [Theory]
        [InlineData("Owner", "Owner")]
        [InlineData("Employee", "Employee")]
        [InlineData("EventManager", "EventManager")]
        [InlineData("Admin", "Admin")]
        public async Task PropertiesDetails_BlocksStaffAndOwners_RedirectsToDashboard(string role, string expectedController)
        {
            var controller = new PropertiesController(StubRepo.Create<IPropertyRepository>(), StubRepo.Create<IBookingRepository>());
            controller.ControllerContext = CreateRoleContext(role);
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var res = await controller.Details(10);
            var redirect = Assert.IsType<RedirectToActionResult>(res);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(expectedController, redirect.ControllerName);
        }

        [Theory]
        [InlineData("Client")]
        [InlineData(null)]
        public async Task PropertiesDetails_AllowsClientAndAnonymous(string? role)
        {
            var controller = new PropertiesController(StubRepo.Create<IPropertyRepository>(), StubRepo.Create<IBookingRepository>());
            controller.ControllerContext = CreateRoleContext(role);
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var res = await controller.Details(10);
            Assert.IsType<ViewResult>(res);
        }

        [Theory]
        [InlineData("Owner")]
        [InlineData("Employee")]
        [InlineData("EventManager")]
        [InlineData("Admin")]
        public async Task PropertyApis_Return403ForBlockedRoles(string role)
        {
            var controller = new PropertiesController(StubRepo.Create<IPropertyRepository>(), StubRepo.Create<IBookingRepository>());
            controller.ControllerContext = CreateRoleContext(role);

            var bookedRes = await controller.GetBookedDates(10);
            var obj1 = Assert.IsType<ObjectResult>(bookedRes);
            Assert.Equal(403, obj1.StatusCode);

            var suggRes = await controller.GetAvailabilitySuggestions(10, System.DateTime.Today.AddDays(1), System.DateTime.Today.AddDays(3));
            var obj2 = Assert.IsType<ObjectResult>(suggRes);
            Assert.Equal(403, obj2.StatusCode);
        }

        [Theory]
        [InlineData("Owner", "Owner")]
        [InlineData("Employee", "Employee")]
        [InlineData("EventManager", "EventManager")]
        [InlineData("Admin", "Admin")]
        public async Task BookingRoutes_BlockStaffAndOwners_RedirectToDashboard(string role, string expectedController)
        {
            var controller = new BookingController(
                StubRepo.Create<IPropertyRepository>(),
                StubRepo.Create<IBookingRepository>(),
                StubRepo.Create<IEventPackageRepository>(),
                new BookingPricingService(),
                null!, null!, null!, null!, null!, null!,
                StubRepo.Create<IUserRepository>(),
                null!, null!);
            controller.ControllerContext = CreateRoleContext(role);
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var checkoutRes = await controller.Checkout(10);
            var r1 = Assert.IsType<RedirectToActionResult>(checkoutRes);
            Assert.Equal(expectedController, r1.ControllerName);

            var confirmRes = await controller.ConfirmBooking(new BookingCheckoutViewModel { PropertyID = 10 });
            var r2 = Assert.IsType<RedirectToActionResult>(confirmRes);
            Assert.Equal(expectedController, r2.ControllerName);

            var payRes = await controller.Payment(1);
            var r3 = Assert.IsType<RedirectToActionResult>(payRes);
            Assert.Equal(expectedController, r3.ControllerName);
        }
    }
}
