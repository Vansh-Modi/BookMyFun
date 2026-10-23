using System;
using System.IO;
using Xunit;

namespace BookMyFun.Tests;

public class ThemeGlitchResolutionTests
{
    private static string FindRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "BookMyFun.csproj")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void Test94_LayoutAndSiteJs_ThemeAttributeSynchronization_SetsBothDataThemeAndDataBsTheme()
    {
        var repoRoot = FindRepoRoot();
        var layoutPath = Path.Combine(repoRoot, "Views", "Shared", "_Layout.cshtml");
        var siteJsPath = Path.Combine(repoRoot, "wwwroot", "js", "site.js");

        Assert.True(File.Exists(layoutPath), $"Layout view not found at: {layoutPath}");
        Assert.True(File.Exists(siteJsPath), $"site.js not found at: {siteJsPath}");

        var layoutContent = File.ReadAllText(layoutPath);
        var jsContent = File.ReadAllText(siteJsPath);

        // 1. Verify <html> element declares both data-theme and data-bs-theme
        Assert.Contains("data-theme=\"light\"", layoutContent);
        Assert.Contains("data-bs-theme=\"light\"", layoutContent);

        // 2. Verify Anti-FOUC script synchronizes both attributes before first paint
        Assert.Contains("document.documentElement.setAttribute('data-theme', saved);", layoutContent);
        Assert.Contains("document.documentElement.setAttribute('data-bs-theme', saved);", layoutContent);

        // 3. Verify site.js applyTheme updates both attributes
        Assert.Contains("document.documentElement.setAttribute('data-theme', theme);", jsContent);
        Assert.Contains("document.documentElement.setAttribute('data-bs-theme', theme);", jsContent);
    }

    [Fact]
    public void Test95_SiteCss_TokensDeclaredInLightAndDarkMode_UniformTokenInheritance()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        Assert.True(File.Exists(siteCssPath), $"site.css not found at: {siteCssPath}");

        var cssContent = File.ReadAllText(siteCssPath);

        // Verify tokens in light mode (:root, [data-theme="light"], [data-bs-theme="light"])
        Assert.Contains("--bg-primary: #f8fafc", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--border-subtle: #e2e8f0", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--brand-navy: #0b4f6c", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-main: #0f172a", cssContent, StringComparison.OrdinalIgnoreCase);

        // Verify tokens in dark mode ([data-theme="dark"], [data-bs-theme="dark"])
        Assert.Contains("--bg-primary: #0f172a", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--border-subtle: #334155", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--brand-navy: #38bdf8", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--text-main: #f1f5f9", cssContent, StringComparison.OrdinalIgnoreCase);

        // Verify Bootstrap 5 link colors in dark mode
        Assert.Contains("--bs-link-color: #60a5fa", cssContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--bs-link-hover-color: #93c5fd", cssContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test96_SiteCss_BootstrapNativeOverridesAndUtilityEnhancements()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Verify .btn-light dark mode contrast override
        Assert.Contains(".btn-light", cssContent);
        Assert.Contains("background-color: #334155 !important;", cssContent);

        // 2. Verify .text-secondary dark mode contrast override
        Assert.Contains(".text-secondary", cssContent);
        Assert.Contains("color: var(--text-muted) !important;", cssContent);

        // 3. Verify .border-secondary dark mode override
        Assert.Contains(".border-secondary", cssContent);
        Assert.Contains("border-color: #334155 !important;", cssContent);

        // 4. Verify .btn-primary .badge.bg-white contrast preservation
        Assert.Contains(".btn-primary .badge.bg-white", cssContent);
        Assert.Contains("color: #1d4ed8 !important;", cssContent);
    }

    [Fact]
    public void Test97_SiteCss_InputAutofillAndFormControls_DarkContrastIntegrity()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Verify -webkit-autofill dark mode rules preventing yellow/white browser autofill glare
        Assert.Contains("input:-webkit-autofill", cssContent);
        Assert.Contains("-webkit-box-shadow: 0 0 0px 1000px #1e293b inset !important;", cssContent);

        // 2. Verify .table-bordered dark mode rules
        Assert.Contains(".table-bordered", cssContent);

        // 3. Verify breadcrumb dark mode rules
        Assert.Contains(".breadcrumb-item", cssContent);
    }

    [Fact]
    public void Test98_PropertiesIndexView_NoHardcodedRootTokens_AndDeclaresLuxeDarkOverrides()
    {
        var repoRoot = FindRepoRoot();
        var propertiesIndexPath = Path.Combine(repoRoot, "Views", "Properties", "Index.cshtml");
        Assert.True(File.Exists(propertiesIndexPath), $"Index.cshtml not found at: {propertiesIndexPath}");

        var viewContent = File.ReadAllText(propertiesIndexPath);

        // Verify the hardcoded :root token block has been eliminated
        Assert.DoesNotContain(":root {", viewContent);

        // Verify dark mode overrides for Luxe catalog components exist
        Assert.Contains("[data-theme=\"dark\"] .luxe-control", viewContent);
        Assert.Contains("[data-theme=\"dark\"] .luxe-category-pill", viewContent);
        Assert.Contains("[data-theme=\"dark\"] .luxe-card-rating", viewContent);
        Assert.Contains("[data-theme=\"dark\"] .luxe-btn-secondary", viewContent);
        Assert.Contains("[data-theme=\"dark\"] .luxe-empty-icon", viewContent);
    }

    [Fact]
    public void Test99_PaymentHeroCard_DeclaresThemeAwareGradient_AndViewsUseClass()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var bookingPaymentPath = Path.Combine(repoRoot, "Views", "Booking", "Payment.cshtml");
        var ownerPaymentPath = Path.Combine(repoRoot, "Views", "Owner", "SubscriptionPayment.cshtml");
        var clientIndexPath = Path.Combine(repoRoot, "Views", "Client", "Index.cshtml");

        var cssContent = File.ReadAllText(siteCssPath);
        var bookingContent = File.ReadAllText(bookingPaymentPath);
        var ownerContent = File.ReadAllText(ownerPaymentPath);
        var clientContent = File.ReadAllText(clientIndexPath);

        // 1. Verify .payment-hero-card is declared in site.css with dark mode gradient
        Assert.Contains(".payment-hero-card", cssContent);
        Assert.Contains("linear-gradient(135deg, #064e3b 0%, #022c22 100%) !important;", cssContent);

        // 2. Verify payment views use .payment-hero-card instead of hardcoded inline light green background
        Assert.Contains("payment-hero-card", bookingContent);
        Assert.DoesNotContain("style=\"background: linear-gradient(135deg, #f0fdf4 0%, #ecfdf5 100%);\"", bookingContent);

        Assert.Contains("payment-hero-card", ownerContent);
        Assert.DoesNotContain("style=\"background: linear-gradient(135deg, #f0fdf4 0%, #ecfdf5 100%);\"", ownerContent);

        // 3. Verify .dash-badge-danger and .dash-badge-secondary are declared and used in client dashboard
        Assert.Contains(".dash-badge-danger", cssContent);
        Assert.Contains(".dash-badge-secondary", cssContent);
        Assert.Contains("dash-badge-danger", clientContent);
    }
}
