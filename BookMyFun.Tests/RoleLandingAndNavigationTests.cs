using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using BookMyFun.Controllers;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace BookMyFun.Tests
{
    public class RoleLandingAndNavigationTests
    {
        private class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
            public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
        }

        private class StubRepo : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                var rt = targetMethod!.ReturnType;
                if (rt == typeof(Task<List<Property>>)) return Task.FromResult(new List<Property>());
                if (rt == typeof(Task<List<PropertyType>>)) return Task.FromResult(new List<PropertyType>());
                if (rt == typeof(Task<List<Occasion>>)) return Task.FromResult(new List<Occasion>());
                if (rt == typeof(Task<List<AmenityMaster>>)) return Task.FromResult(new List<AmenityMaster>());
                if (rt == typeof(Task<(List<Property>, int)>)) return Task.FromResult((new List<Property>(), 0));
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
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };
        }

        [Theory]
        [InlineData("Owner", "Owner")]
        [InlineData("EventManager", "EventManager")]
        [InlineData("Employee", "Employee")]
        [InlineData("Admin", "Admin")]
        public async Task HomeIndex_RedirectsStaffAndPartnersToTheirDashboard(string role, string expectedController)
        {
            var controller = new HomeController(
                StubRepo.Create<IPropertyRepository>(),
                StubRepo.Create<IContactRepository>(),
                StubRepo.Create<IPartnerLeadService>(),
                StubRepo.Create<IEmailService>(),
                StubRepo.Create<IWhatsAppService>(),
                StubRepo.Create<IUserRepository>(),
                StubRepo.Create<ILegalDocumentService>());
            controller.ControllerContext = CreateRoleContext(role);
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var res = await controller.Index();
            var redirect = Assert.IsType<RedirectToActionResult>(res);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(expectedController, redirect.ControllerName);
        }

        [Theory]
        [InlineData("Client")]
        [InlineData(null)]
        public async Task HomeIndex_AllowsClientAndGuest(string? role)
        {
            var controller = new HomeController(
                StubRepo.Create<IPropertyRepository>(),
                StubRepo.Create<IContactRepository>(),
                StubRepo.Create<IPartnerLeadService>(),
                StubRepo.Create<IEmailService>(),
                StubRepo.Create<IWhatsAppService>(),
                StubRepo.Create<IUserRepository>(),
                StubRepo.Create<ILegalDocumentService>());
            controller.ControllerContext = CreateRoleContext(role);

            var res = await controller.Index();
            Assert.IsType<ViewResult>(res);
        }

        [Theory]
        [InlineData("Owner", "Owner")]
        [InlineData("EventManager", "EventManager")]
        [InlineData("Employee", "Employee")]
        [InlineData("Admin", "Admin")]
        public async Task PropertiesIndex_RedirectsStaffAndPartnersWithGuestMessage(string role, string expectedController)
        {
            var controller = new PropertiesController(StubRepo.Create<IPropertyRepository>(), StubRepo.Create<IBookingRepository>());
            controller.ControllerContext = CreateRoleContext(role);
            controller.TempData = new TempDataDictionary(controller.HttpContext, new NullTempDataProvider());

            var res = await controller.Index(null, null, null, null, null, null);
            var redirect = Assert.IsType<RedirectToActionResult>(res);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(expectedController, redirect.ControllerName);
            Assert.Equal("This page is for guests. Use your dashboard.", controller.TempData["Error"]);
        }

        [Fact]
        public void Layout_HidesPublicPropertyNavLinksForBlockedRoles()
        {
            var path = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            var html = File.ReadAllText(path);

            Assert.Contains("isStaffOrPartner", html);
            Assert.Contains("My Dashboard", html);
            Assert.Contains("Explore Stays", html);
        }
    }
}
