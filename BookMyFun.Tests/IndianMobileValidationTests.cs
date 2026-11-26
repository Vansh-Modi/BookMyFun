using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Models.Validation;
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
    public class IndianMobileValidationTests
    {
        private class StubRepo : DispatchProxy
        {
            public Property TestProp { get; } = new Property
            {
                PropertyID = 1,
                OwnerID = 5,
                Title = "Test Villa",
                PricePerNight = 5000m,
                ListingStatus = "Live"
            };
            public User TestUser { get; } = new User
            {
                UserID = 10,
                FullName = "Vansh Sharma",
                Phone = "+919876543210",
                Role = "Client"
            };

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var name = targetMethod!.Name;
                var rt = targetMethod.ReturnType;
                if (rt == typeof(Task<Property?>)) return Task.FromResult<Property?>(TestProp);
                if (rt == typeof(Task<Booking?>)) return Task.FromResult<Booking?>(null);
                if (name == nameof(IPropertyRepository.IsPropertyPubliclyVisibleAsync)) return Task.FromResult(true);
                if (rt == typeof(Task<User?>)) return Task.FromResult<User?>(TestUser);
                if (name == nameof(IIdVerificationService.IsClientVerifiedAsync)) return Task.FromResult(true);
                if (name == nameof(IIdVerificationService.GetClientVerificationAsync)) return Task.FromResult<ClientIdVerification?>(null);
                if (name == nameof(IBookingRepository.IsSlotAvailableAsync)) return Task.FromResult(true);
                if (targetMethod.ReturnType == typeof(Task<List<string>>)) return Task.FromResult(new List<string>());
                if (targetMethod.ReturnType == typeof(Task<List<Occasion>>)) return Task.FromResult(new List<Occasion>());
                if (targetMethod.ReturnType == typeof(Task<List<EventPackage>>)) return Task.FromResult(new List<EventPackage>());
                if (targetMethod.ReturnType == typeof(Task<List<EventManagerPortfolio>>)) return Task.FromResult(new List<EventManagerPortfolio>());
                if (targetMethod.ReturnType == typeof(Task<List<int>>)) return Task.FromResult(new List<int>());
                return null;
            }

            public static T Create<T>() where T : class => DispatchProxy.Create<T, StubRepo>();
        }

        [Theory]
        [InlineData("+919876543210", "9876543210")]
        [InlineData("+91 98765-43210", "9876543210")]
        [InlineData("919876543210", "9876543210")]
        [InlineData("09876543210", "9876543210")]
        [InlineData("9876543210", "9876543210")]
        public void NormalizeTo10Digits_StripsCountryCodeAndFormatting(string raw, string expected)
        {
            Assert.Equal(expected, IndianMobileHelper.NormalizeTo10Digits(raw));
        }

        [Theory]
        [InlineData("9876543210", true)]
        [InlineData("6000000000", true)]
        [InlineData("7123456789", true)]
        [InlineData("8999999999", true)]
        [InlineData("5876543210", false)] // starts with 5
        [InlineData("1234567890", false)] // starts with 1
        [InlineData("987654321", false)]  // 9 digits
        [InlineData("98765432101", false)] // 11 digits
        public void IndianMobileAttribute_Validates10DigitIndianNumbers(string phone, bool expectedValid)
        {
            var vm = new RegisterViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                Password = "Password123",
                OtpCode = "123456",
                Phone = phone
            };

            var results = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(vm, new ValidationContext(vm), results, validateAllProperties: true);
            Assert.Equal(expectedValid, isValid);
        }

        [Fact]
        public async Task CheckoutGet_Prefills10DigitMobileFromProfileWithPlus91()
        {
            var controller = new BookingController(
                StubRepo.Create<IPropertyRepository>(),
                StubRepo.Create<IBookingRepository>(),
                StubRepo.Create<IEventPackageRepository>(),
                new BookingPricingService(),
                null!, null!, null!, null!, null!, null!,
                StubRepo.Create<IUserRepository>(),
                StubRepo.Create<IIdVerificationService>(),
                null!);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "10"),
                        new Claim(ClaimTypes.Role, "Client")
                    }, "Test"))
                }
            };

            var res = await controller.Checkout(1);
            var view = Assert.IsType<ViewResult>(res);
            var vm = Assert.IsType<BookingCheckoutViewModel>(view.Model);

            Assert.Equal("9876543210", vm.GuestPhone);
        }
    }
}
