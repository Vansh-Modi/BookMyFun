using BookMyFun.Controllers;
using BookMyFun.Models;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookMyFun.Tests;

public class Phase5ErrorHandlingTests
{
    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "BookMyFun";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private class FakeEmailService : IEmailService
    {
        public List<(string To, string Subject, string Body, string Type)> EnqueuedEmails = new();

        public Task<bool> SendEmailAsync(string toEmail, string subject, string bodyHtml) => Task.FromResult(true);
        public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purpose, string? recipientName = null) => Task.FromResult(true);
        public Task EnqueueEmailAsync(string toEmail, string subject, string bodyHtml, string messageType = "Notification")
        {
            EnqueuedEmails.Add((toEmail, subject, bodyHtml, messageType));
            return Task.CompletedTask;
        }
    }

    private class FakeBookingRepository : IBookingRepository
    {
        public int UpdatedStatusBookingId = 0;
        public string UpdatedStatus = "";
        public Booking? BookingToReturn;

        public Task<Booking?> GetByIdAsync(int bookingID) => Task.FromResult(BookingToReturn);
        public Task<Booking?> GetBookingByIdAsync(int bookingId) => Task.FromResult(BookingToReturn);
        public Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey) => Task.FromResult<Booking?>(null);
        public Task<List<Booking>> GetByClientIdAsync(int clientId) => Task.FromResult(new List<Booking>());
        public Task<List<Booking>> GetByOwnerIdAsync(int ownerId) => Task.FromResult(new List<Booking>());
        public Task<List<Booking>> GetAllBookingsAsync() => Task.FromResult(new List<Booking>());
        public Task<int> CreateBookingAsync(Booking booking) => Task.FromResult(1);
        public Task<bool> UpdateBookingStatusAsync(int bookingId, string status)
        {
            UpdatedStatusBookingId = bookingId;
            UpdatedStatus = status;
            return Task.FromResult(true);
        }
        public Task<bool> CancelBookingAsync(int bookingId) => Task.FromResult(true);
        public Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status) => Task.FromResult(true);
        public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType) => Task.FromResult(true);
        public Task<int> RecordPaymentAsync(Payment payment) => Task.FromResult(1);
        public Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId) => Task.FromResult(new List<Payment>());
        public Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<CustomerReview>());
        public Task<int> AddReviewAsync(CustomerReview review) => Task.FromResult(1);
        public Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate) => Task.FromResult(true);
    }

    private class FakeUserRepository : IUserRepository
    {
        public User? UserToReturn;

        public Task<User?> GetByIdAsync(int userId) => Task.FromResult(UserToReturn);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(UserToReturn);
        public Task<User?> GetByAuthProviderKeyAsync(string provider, string providerKey) => Task.FromResult(UserToReturn);
        public Task<List<User>> GetUsersByRoleAsync(string role) => Task.FromResult(new List<User>());
        public Task<List<User>> GetAllUsersAsync() => Task.FromResult(new List<User>());
        public Task<int> CreateUserAsync(User user) => Task.FromResult(1);
        public Task<bool> UpdateUserAsync(User user) => Task.FromResult(true);
        public Task<bool> DeleteUserAsync(int userId) => Task.FromResult(true);
        public Task<bool> ResetPasswordAsync(string email, string newPasswordHash) => Task.FromResult(true);
        public Task<bool> UpdateUserTierAndCommissionAsync(int userId, string subscriptionTier, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> UpdateUserCommissionRateAsync(int userId, decimal? customCommissionRate) => Task.FromResult(true);
        public Task<bool> LinkAuthProviderAsync(int userId, string provider, string providerKey, string? profilePic = null) => Task.FromResult(true);
    }

    [Fact]
    public void OnNotFound_ReturnsJson_ForAjaxRequest()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        httpContext.TraceIdentifier = "trace-404-ajax";

        var result = service.OnNotFound(httpContext, "/properties/missing-villa");

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Equal(404, jsonResult.StatusCode);

        dynamic payload = jsonResult.Value!;
        var successProp = payload.GetType().GetProperty("success")?.GetValue(payload);
        var correlationIdProp = payload.GetType().GetProperty("correlationId")?.GetValue(payload);

        Assert.Equal(false, successProp);
        Assert.Equal("trace-404-ajax", correlationIdProp);
    }

    [Fact]
    public void OnNotFound_ReturnsView_ForStandardBrowserRequest()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-404-browser";

        var result = service.OnNotFound(httpContext, "/missing-page");

        var viewResult = Assert.IsType<ViewResult>(result);
        Assert.Equal(404, viewResult.StatusCode);
        var model = Assert.IsType<ErrorViewModel>(viewResult.Model);
        Assert.True(model.IsNotFound);
        Assert.Equal("trace-404-browser", model.RequestId);
    }

    [Fact]
    public void OnAccessDenied_ReturnsJson_ForApiRequest()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Accept"] = "application/json";
        httpContext.TraceIdentifier = "trace-403-api";

        var result = service.OnAccessDenied(httpContext, "/admin/super-secret");

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Equal(403, jsonResult.StatusCode);

        dynamic payload = jsonResult.Value!;
        var successProp = payload.GetType().GetProperty("success")?.GetValue(payload);
        var correlationIdProp = payload.GetType().GetProperty("correlationId")?.GetValue(payload);

        Assert.Equal(false, successProp);
        Assert.Equal("trace-403-api", correlationIdProp);
    }

    [Fact]
    public void OnUnhandledException_ReturnsJsonWithCorrelationId_ForAjaxRequest()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Requested-With"] = "XMLHttpRequest";
        httpContext.TraceIdentifier = "trace-500-test";

        var exception = new InvalidOperationException("Unexpected fatal crash");
        var result = service.OnUnhandledException(httpContext, exception);

        var jsonResult = Assert.IsType<JsonResult>(result);
        Assert.Equal(500, jsonResult.StatusCode);

        dynamic payload = jsonResult.Value!;
        var successProp = payload.GetType().GetProperty("success")?.GetValue(payload);
        var correlationIdProp = payload.GetType().GetProperty("correlationId")?.GetValue(payload);

        Assert.Equal(false, successProp);
        Assert.Equal("trace-500-test", correlationIdProp);
    }

    [Fact]
    public void OnLoginRequired_PreservesTargetUrlAndQueryParameters()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/Booking/Checkout";
        httpContext.Request.QueryString = new QueryString("?propertyId=12&guests=4");

        var result = service.OnLoginRequired(httpContext);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
        Assert.Equal("/Booking/Checkout?propertyId=12&guests=4", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task OnPaymentFailedAsync_UpdatesBookingStatusAndNotifiesClient()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();

        int testBookingId = 88;
        fakeBooking.BookingToReturn = new Booking
        {
            BookingID = testBookingId,
            ClientID = 42,
            Status = "Pending"
        };
        fakeUser.UserToReturn = new User
        {
            UserID = 42,
            FullName = "Alice Client",
            Email = "alice@example.com"
        };

        var service = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        await service.OnPaymentFailedAsync(testBookingId, "Card declined by issuing bank.");

        Assert.Equal(testBookingId, fakeBooking.UpdatedStatusBookingId);
        Assert.Equal("PaymentFailed", fakeBooking.UpdatedStatus);

        Assert.Single(fakeEmail.EnqueuedEmails);
        var sent = fakeEmail.EnqueuedEmails[0];
        Assert.Equal("alice@example.com", sent.To);
        Assert.Contains("Payment Action Required", sent.Subject);
        Assert.Contains("/Booking/Payment/88", sent.Body);
    }

    [Fact]
    public void HomeController_Error_RoutesStatusCodesCorrectly()
    {
        var fakeBooking = new FakeBookingRepository();
        var fakeUser = new FakeUserRepository();
        var fakeEmail = new FakeEmailService();
        var errorHandler = new CustomErrorHandlerService(
            NullLogger<CustomErrorHandlerService>.Instance,
            new FakeHostEnvironment(),
            fakeBooking,
            fakeUser,
            fakeEmail);

        var db = TestHelper.GetTestDb();
        var propRepo = new PropertyRepository(db);
        var contactRepo = new ContactRepository(db);
        var configRepo = new ConfigRepository(db);
        var config = TestHelper.GetTestConfig();
        var comms = new CommunicationService(configRepo, config, NullLogger<CommunicationService>.Instance);
        var leadService = new PartnerLeadService(
            new PartnerLeadRepository(db),
            new UserRepository(db),
            new OwnerRequestRepository(db),
            comms,
            comms,
            NullLogger<PartnerLeadService>.Instance);
        var legalService = new LegalDocumentService(new LegalDocumentRepository(db));

        var controller = new HomeController(
            propRepo,
            contactRepo,
            leadService,
            comms,
            comms,
            new UserRepository(db),
            legalService,
            errorHandler);

        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var notFoundResult = controller.Error(404);
        var nfView = Assert.IsType<ViewResult>(notFoundResult);
        Assert.Equal(404, nfView.StatusCode);

        var forbiddenResult = controller.Error(403);
        var fView = Assert.IsType<ViewResult>(forbiddenResult);
        Assert.Equal(403, fView.StatusCode);

        var serverErrorResult = controller.Error(500);
        var seView = Assert.IsType<ViewResult>(serverErrorResult);
        Assert.Equal(500, seView.StatusCode);
    }
}
