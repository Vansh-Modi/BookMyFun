using BookMyFun.Data;
using BookMyFun.Repositories;
using BookMyFun.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<BookMyFun.Services.Filters.GlobalExceptionFilter>();
});

// Register Data Access Layer (Raw ADO.NET)
builder.Services.AddSingleton<DBHelper>();
builder.Services.AddTransient<DatabaseInitializer>();

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

// Services
builder.Services.AddScoped<IBookingPricingService, BookingPricingService>();
builder.Services.AddScoped<ICommissionService, CommissionService>();
builder.Services.AddScoped<IPaymentGatewayService, RazorpayPaymentService>();
builder.Services.AddScoped<CommunicationService>();
builder.Services.AddScoped<IEmailService>(sp => sp.GetRequiredService<CommunicationService>());
builder.Services.AddScoped<IWhatsAppService>(sp => sp.GetRequiredService<CommunicationService>());
builder.Services.AddScoped<IMediaStorageService, MediaStorageService>();
builder.Services.AddScoped<IRefundEngineService, RefundEngineService>();
builder.Services.AddScoped<IPartnerLeadService, PartnerLeadService>();
builder.Services.AddScoped<IDamageClaimService, DamageClaimService>();
builder.Services.AddScoped<IIdVerificationService, IdVerificationService>();

// Cookie & External Authentication (Google, etc.)
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
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Conditionally configure Google OAuth if client credentials are provided
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
bool isGoogleConfigured = !string.IsNullOrWhiteSpace(googleClientId)
    && !string.IsNullOrWhiteSpace(googleClientSecret)
    && !googleClientId.Contains("YOUR_GOOGLE_CLIENT_ID")
    && !googleClientId.Contains("your-google-client-id");

if (isGoogleConfigured)
{
    authBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId!;
        options.ClientSecret = googleClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.SaveTokens = true;
    });
}

var app = builder.Build();

// Run Database Initializer & Seeder
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();

    var dbHelper = scope.ServiceProvider.GetRequiredService<DBHelper>();
    try
    {
        await DatabaseSeeder.SeedInitialDataAsync(dbHelper);
    }
    catch { }
}

// Security & Rate Limiting Middlewares
app.UseMiddleware<BookMyFun.Services.Security.SecurityHeadersMiddleware>();
app.UseMiddleware<BookMyFun.Services.Security.RateLimitingMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
