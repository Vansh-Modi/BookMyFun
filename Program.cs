using BookMyFun.Data;
using BookMyFun.Repositories;
using BookMyFun.Services;
using BookMyFun.Services.Configuration;
using BookMyFun.Services.Security;
using BookMyFun.Services.Checkout;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Booking.json", optional: true, reloadOnChange: true);

// Bootstrap Logger for Startup Phase Diagnostics
using var startupLoggerFactory = LoggerFactory.Create(lb =>
{
    lb.AddConfiguration(builder.Configuration.GetSection("Logging"));
    lb.AddConsole();
});
var startupLogger = startupLoggerFactory.CreateLogger("Startup");

// 1. Fail-fast validation for REQUIRED configuration
string defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
if (string.IsNullOrWhiteSpace(defaultConnectionString))
{
    startupLogger.LogCritical("FATAL: Missing REQUIRED configuration setting 'ConnectionStrings:DefaultConnection'. Application cannot start.");
    throw new InvalidOperationException("Missing REQUIRED configuration setting 'ConnectionStrings:DefaultConnection'. Application cannot start.");
}

// 2. Options Pattern Registration
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.Configure<RazorpayOptions>(builder.Configuration.GetSection(RazorpayOptions.SectionName));
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection(WhatsAppOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<BookMyFun.Models.Configuration.BookingTimingOptions>(builder.Configuration.GetSection(BookMyFun.Models.Configuration.BookingTimingOptions.SectionName));
builder.Services.Configure<BookMyFun.Models.Configuration.BookingOptions>(builder.Configuration.GetSection(BookMyFun.Models.Configuration.BookingOptions.SectionName));

var smtpOptions = builder.Configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
var razorpayOptions = builder.Configuration.GetSection(RazorpayOptions.SectionName).Get<RazorpayOptions>() ?? new RazorpayOptions();
var googleOptions = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
var whatsAppOptions = builder.Configuration.GetSection(WhatsAppOptions.SectionName).Get<WhatsAppOptions>() ?? new WhatsAppOptions();
var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

// Optional Feature Diagnostics
if (!smtpOptions.IsConfigured)
{
    startupLogger.LogWarning("Optional feature 'SMTP Email' is disabled or incomplete. Configure 'Smtp:Host', 'Smtp:Username', and 'Smtp:Password'.");
}
if (!razorpayOptions.IsConfigured)
{
    startupLogger.LogWarning("Optional feature 'Razorpay Payments' is disabled or incomplete. Configure 'Razorpay:KeyId' and 'Razorpay:KeySecret'.");
}
if (!whatsAppOptions.IsConfigured)
{
    startupLogger.LogWarning("Optional feature 'WhatsApp Cloud API' is disabled or incomplete. Configure 'WhatsApp:ApiKey' and 'WhatsApp:PhoneNumberId'.");
}

// 3. ASP.NET Core Data Protection (Keys persisted to App_Data/DataProtectionKeys)
var appDataParent = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
EnsureDirectoryWritable(appDataParent, startupLogger, "App_Data directory failed write verification.");
var appDataDir = Path.Combine(appDataParent, "DataProtectionKeys");
EnsureDirectoryWritable(appDataDir, startupLogger, "DataProtectionKeys directory in App_Data failed write verification.");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(appDataDir))
    .SetApplicationName("BookMyFun");

// 4. Controller & View Pipeline with Global Antiforgery Validation
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BookMyFun.Services.Filters.GlobalExceptionFilter>();
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// 5. Response Compression (Brotli & Gzip for HTML, CSS, JS, JSON, SVG)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "image/svg+xml" });
});

// Register HTTP Client Factory with resilience timeouts
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Razorpay", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("WhatsApp", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Data Access Layer (Raw ADO.NET)
builder.Services.AddSingleton<DBHelper>();
builder.Services.AddTransient<DatabaseInitializer>();
builder.Services.AddScoped<BookMyFun.Services.Database.ISchemaVersionService, BookMyFun.Services.Database.SchemaVersionService>();

// Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IOtpRepository, OtpRepository>();
builder.Services.AddScoped<IOwnerRequestRepository, OwnerRequestRepository>();
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<IFinancialRepository, FinancialRepository>();
builder.Services.AddScoped<IPropertyRepository, PropertyRepository>();
builder.Services.AddScoped<ISiteSettingsService, SiteSettingsService>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IConfigRepository, ConfigRepository>();
builder.Services.AddScoped<IRefundRepository, RefundRepository>();
builder.Services.AddScoped<IPartnerLeadRepository, PartnerLeadRepository>();
builder.Services.AddScoped<IDamageClaimRepository, DamageClaimRepository>();
builder.Services.AddScoped<IIdVerificationRepository, IdVerificationRepository>();
builder.Services.AddScoped<IEventPackageRepository, EventPackageRepository>();
builder.Services.AddScoped<IWebhookEventRepository, WebhookEventRepository>();
builder.Services.AddScoped<ILegalDocumentRepository, LegalDocumentRepository>();
builder.Services.AddScoped<IPhotoShootRepository, PhotoShootRepository>();

// Core Security & Domain Services
builder.Services.AddSingleton<IPrivateDocumentEncryptionService, PrivateDocumentEncryptionService>();
builder.Services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddSingleton<ILoginLockoutService, LoginLockoutService>();
builder.Services.AddSingleton<IHtmlSanitizerService, HtmlSanitizerService>();
builder.Services.AddScoped<ICustomErrorHandlerService, CustomErrorHandlerService>();
builder.Services.AddScoped<IBookingPricingService, BookingPricingService>();
builder.Services.AddScoped<ICommissionService, CommissionService>();
builder.Services.AddScoped<IPaymentGatewayService, RazorpayPaymentService>();
builder.Services.AddSingleton<IEmailQueueService, EmailQueueService>();
builder.Services.AddHostedService<EmailBackgroundDispatcher>();
builder.Services.AddScoped<CommunicationService>();
builder.Services.AddScoped<IEmailService>(sp => sp.GetRequiredService<CommunicationService>());
builder.Services.AddScoped<IWhatsAppService>(sp => sp.GetRequiredService<CommunicationService>());
builder.Services.AddScoped<IImageOptimizationService, ImageOptimizationService>();
builder.Services.AddScoped<IMediaStorageService, MediaStorageService>();
builder.Services.AddScoped<IRefundEngineService, RefundEngineService>();
builder.Services.AddScoped<IPartnerLeadService, PartnerLeadService>();
builder.Services.AddScoped<IDamageClaimService, DamageClaimService>();
builder.Services.AddScoped<IIdVerificationService, IdVerificationService>();
builder.Services.AddScoped<ILegalDocumentService, LegalDocumentService>();
builder.Services.AddScoped<IAvailabilitySuggestionService, AvailabilitySuggestionService>();
builder.Services.AddScoped<ICalendarRangeService, CalendarRangeService>();
builder.Services.AddScoped<IContactPrivacyScanner, ContactPrivacyScanner>();
builder.Services.AddScoped<IWhatsAppProvider, MetaWhatsAppProvider>();
builder.Services.AddScoped<INotificationFanoutService, NotificationFanoutService>();
builder.Services.AddScoped<BookMyFun.Repositories.IUserNotificationRepository, BookMyFun.Repositories.UserNotificationRepository>();
builder.Services.AddScoped<BookMyFun.Repositories.INotificationOutboxRepository, BookMyFun.Repositories.NotificationOutboxRepository>();
builder.Services.AddScoped<BookMyFun.Services.Notifications.INotificationDispatcherService, BookMyFun.Services.Notifications.NotificationDispatcherService>();
builder.Services.AddHostedService<BookMyFun.Services.Notifications.NotificationOutboxBackgroundService>();
builder.Services.AddHostedService<BookMyFun.Services.Notifications.CheckInReminderBackgroundService>();
builder.Services.AddHostedService<BookMyFun.Services.Notifications.SubscriptionLifecycleBackgroundService>();
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});
builder.Services.AddScoped<ICheckoutEventManagerService, CheckoutEventManagerService>();
builder.Services.AddScoped<IStayCelebrationEligibilityService, StayCelebrationEligibilityService>();
builder.Services.AddScoped<BookMyFun.Services.Staff.IEventManagerReviewService, BookMyFun.Services.Staff.EventManagerReviewService>();
builder.Services.AddScoped<BookMyFun.Services.Staff.IStaffQueueService, BookMyFun.Services.Staff.StaffQueueService>();
builder.Services.AddScoped<BookMyFun.Services.Health.IDataHealthCheckService, BookMyFun.Services.Health.DataHealthCheckService>();
builder.Services.AddMemoryCache();

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// Antiforgery Configuration
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// Authentication Configuration (Cookie & Google OAuth)
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "BookMyFun.AuthCookie";
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    options.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            // Enforce maximum absolute ticket lifetime of 24 hours from issuance
            if (context.Properties.IssuedUtc.HasValue)
            {
                var timeElapsed = DateTimeOffset.UtcNow - context.Properties.IssuedUtc.Value;
                if (timeElapsed > TimeSpan.FromHours(24))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        }
    };
});

// Policy-Based Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireEmployee", policy => policy.RequireRole("Employee", "Admin"));
    options.AddPolicy("RequireOwner", policy => policy.RequireRole("Owner", "Admin"));
    options.AddPolicy("RequireEventManager", policy => policy.RequireRole("EventManager", "Admin", "Employee"));
    options.AddPolicy("RequireClient", policy => policy.RequireRole("Client", "Admin"));
});

// Check environment variables for Google OAuth fallback
if (string.IsNullOrWhiteSpace(googleOptions.ClientId))
{
    googleOptions.ClientId = Environment.GetEnvironmentVariable("Google__ClientId")
        ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? string.Empty;
}
if (string.IsNullOrWhiteSpace(googleOptions.ClientSecret))
{
    googleOptions.ClientSecret = Environment.GetEnvironmentVariable("Google__ClientSecret")
        ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? string.Empty;
}

if (googleOptions.IsConfigured)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleOptions.ClientId;
        options.ClientSecret = googleOptions.ClientSecret;
        options.CallbackPath = "/signin-google";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.SaveTokens = true;
    });
}
else
{
    startupLogger.LogWarning("Optional feature 'Google Sign-in' is disabled. Set 'Authentication:Google:ClientId' and 'Authentication:Google:ClientSecret'.");
}

var app = builder.Build();

// Ensure mandatory runtime folders exist and are writable
var logsDirectory = Path.Combine(app.Environment.ContentRootPath, "logs");
var uploadsDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
EnsureDirectoryWritable(logsDirectory, app.Logger);
EnsureDirectoryWritable(uploadsDirectory, app.Logger);

// Phase 6 Runtime Schema Verification (No DDL at startup)
// The application must never create or alter tables at startup in any environment.
// Instead, read the highest applied migration from dbo.SchemaVersion and log a single warning if behind.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var schemaService = scope.ServiceProvider.GetRequiredService<BookMyFun.Services.Database.ISchemaVersionService>();
        var schemaStatus = await schemaService.CheckSchemaStatusAsync();
        if (schemaStatus.IsBehind)
        {
            app.Logger.LogWarning("Schema verification check: Database is at version {CurrentVersion} (missing: {MissingVersions}). Running in degraded compatibility mode.",
                schemaStatus.CurrentVersion, string.Join(", ", schemaStatus.MissingVersions));
        }
        else
        {
            app.Logger.LogInformation("Database schema verified at version {Version}.", schemaStatus.CurrentVersion);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Schema check skipped: database was unreachable or encountered an error at startup. Continuing startup in degraded mode.");
    }
}

// Health Endpoint (returns plain text OK without querying database)
app.MapGet("/health", () => Results.Text("OK", "text/plain"));

// Security & Rate Limiting Middlewares
app.UseMiddleware<BookMyFun.Services.Security.SecurityHeadersMiddleware>();
app.UseMiddleware<BookMyFun.Services.Security.RateLimitingMiddleware>();

// Response Compression Middleware
app.UseResponseCompression();

// Exception Handling & Status Codes
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

// Serve static files with modern image formats support & immutable caching
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webp"] = "image/webp";
contentTypeProvider.Mappings[".avif"] = "image/avif";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypeProvider,
    OnPrepareResponse = ctx =>
    {
        // 1 year cache for static assets (images, css, js, fonts)
        const int durationInSeconds = 60 * 60 * 24 * 365;
        ctx.Context.Response.Headers["Cache-Control"] = $"public,max-age={durationInSeconds},immutable";
    }
});

app.UseRouting();

app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// CLI diagnostic command: db-health
if (args.Contains("db-health", StringComparer.OrdinalIgnoreCase) || args.Contains("--db-health", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var healthService = scope.ServiceProvider.GetRequiredService<BookMyFun.Services.Health.IDataHealthCheckService>();
    var report = await healthService.RunDiagnosticsAsync();
    Console.WriteLine($"=== BookMyFun Data Health Check ({report.CheckedAt:yyyy-MM-dd HH:mm:ss} UTC) ===");
    Console.WriteLine($"Total Issues Found: {report.TotalIssuesCount}");
    foreach (var cat in report.CountsByCategory)
    {
        Console.WriteLine($" - {cat.Key}: {cat.Value}");
    }
    Console.WriteLine();
    foreach (var f in report.Findings)
    {
        Console.WriteLine($"[{f.Severity.ToUpperInvariant()}] [{f.Category}] Entity #{f.EntityID} ({f.Name}): {f.Issue}");
        Console.WriteLine($"       Fix URL: {f.FixUrl}");
    }
    return;
}

app.Run();

// Local Helper: Verifies that a directory exists and has write permissions
static void EnsureDirectoryWritable(string directoryPath, ILogger logger, string? failureContext = null)
{
    try
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            logger.LogInformation("Created startup directory: {Directory}", directoryPath);
        }

        string testFilePath = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(testFilePath, "write_test");
        File.Delete(testFilePath);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "CRITICAL: {Context} Directory '{Directory}' failed write permission check.", failureContext ?? "Directory write check failed.", directoryPath);
    }
}
