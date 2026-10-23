using System;
using System.IO;
using Xunit;

namespace BookMyFun.Tests;

public class ResponsiveLayoutTests
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
    public void Test100_SiteCss_DocumentAndViewportContainment_DeclaresOverflowClipAndImageFluidMaxBounds()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        Assert.True(File.Exists(siteCssPath), $"site.css not found at: {siteCssPath}");

        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Verify html and body declare overflow-x: clip for horizontal scroll containment
        Assert.Contains("overflow-x: clip;", cssContent);
        Assert.Contains("box-sizing: border-box;", cssContent);
        Assert.Contains("-webkit-text-size-adjust: 100%;", cssContent);

        // 2. Verify fallback for browsers that do not support overflow-x: clip
        Assert.Contains("@supports not (overflow-x: clip)", cssContent);
        Assert.Contains("overflow-x: hidden;", cssContent);

        // 3. Verify media fluidity rule preventing unconstrained image/video overflow
        Assert.Contains("img, svg, video, canvas, audio, iframe, embed, object", cssContent);
        Assert.Contains("max-width: 100%;", cssContent);

        // 4. Verify word-break / overflow-wrap defense on key headings and titles
        Assert.Contains("overflow-wrap: break-word;", cssContent);
        Assert.Contains("word-break: break-word;", cssContent);
    }

    [Fact]
    public void Test101_SiteCss_MobileFormInputs_PreventSafariAutoZoom_DeclaresMinimum16pxFontSize()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // Verify mobile breakpoint (< 768px)
        Assert.Contains("@media (max-width: 767.98px)", cssContent);

        // Verify input selectors including text, email, number, tel, search, url, select, textarea, .form-control, .form-select, .luxe-control
        Assert.Contains("input[type=\"search\"]", cssContent);
        Assert.Contains("input[type=\"url\"]", cssContent);
        Assert.Contains(".form-control", cssContent);
        Assert.Contains(".form-select", cssContent);
        Assert.Contains(".luxe-control", cssContent);

        // Verify 16px font-size to prevent iOS Safari auto-zoom
        Assert.Contains("font-size: 16px !important;", cssContent);
    }

    [Fact]
    public void Test102_SiteCss_TouchTargetStandards_AndTableContainment_DeclaresComfortableTapAreas()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Verify touch target min-height standards for mobile buttons and interactive elements
        Assert.Contains(".btn,", cssContent);
        Assert.Contains(".dash-btn,", cssContent);
        Assert.Contains(".dash-filter-pill,", cssContent);
        Assert.Contains(".luxe-category-pill,", cssContent);
        Assert.Contains(".mobile-nav-link", cssContent);
        Assert.Contains("min-height: 48px;", cssContent);

        // 2. Verify table touch scrolling containment
        Assert.Contains("-webkit-overflow-scrolling: touch;", cssContent);
        Assert.Contains("white-space: nowrap;", cssContent);

        // 3. Verify modal dialog viewport fitting on mobile
        Assert.Contains(".modal-dialog", cssContent);
        Assert.Contains("max-width: calc(100vw - 1rem) !important;", cssContent);
        Assert.Contains("max-height: calc(100vh - 160px);", cssContent);
    }

    [Fact]
    public void Test103_LuxeCatalog_ResponsiveLayoutAndCardActionsStacking_PreservesFluidGridAcrossBreakpoints()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var catalogViewPath = Path.Combine(repoRoot, "Views", "Properties", "Index.cshtml");

        Assert.True(File.Exists(catalogViewPath), $"Index.cshtml not found at: {catalogViewPath}");

        var siteCss = File.ReadAllText(siteCssPath);
        var catalogView = File.ReadAllText(catalogViewPath);

        // 1. Verify card action buttons stack on mobile (< 576px or < 480px) in site.css and view
        Assert.Contains(".luxe-card-actions", siteCss);
        Assert.Contains("grid-template-columns: 1fr !important;", siteCss);
        Assert.Contains(".luxe-btn-secondary,", siteCss);
        Assert.Contains("min-height: 42px !important;", siteCss);

        // View also declares responsive stacking for card actions
        Assert.Contains(".luxe-card-actions", catalogView);
        Assert.Contains("grid-template-columns: 1fr;", catalogView);

        // 2. Verify catalog search card padding scales down on mobile
        Assert.Contains(".luxe-search-card", siteCss);
        Assert.Contains("padding: 1.25rem 1rem !important;", siteCss);

        // 3. Verify catalog category pill bar touch scroller
        Assert.Contains(".luxe-category-bar", siteCss);
        Assert.Contains("overflow-x: auto !important;", siteCss);
    }

    [Fact]
    public void Test104_DashboardAndHero_StatGridResponsiveness_CollapsesGracefullyFromDesktopToMobile()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Desktop (>= 992px): 4 columns
        Assert.Contains("@media (min-width: 992px) and (max-width: 1199.98px)", cssContent);
        Assert.Contains("grid-template-columns: repeat(4, 1fr) !important;", cssContent);

        // 2. Tablet (768px - 991.98px): 3 columns
        Assert.Contains("@media (min-width: 768px) and (max-width: 991.98px)", cssContent);
        Assert.Contains("grid-template-columns: repeat(3, 1fr) !important;", cssContent);

        // 3. Mobile (< 576px): 2 columns
        Assert.Contains("@media (max-width: 575.98px)", cssContent);
        Assert.Contains("grid-template-columns: repeat(2, 1fr) !important;", cssContent);

        // 4. Ultra-compact phones (< 380px): 1 column
        Assert.Contains("@media (max-width: 379.98px)", cssContent);
        Assert.Contains("grid-template-columns: 1fr !important;", cssContent);

        // 5. Landscape mobile orientation (< 500px height)
        Assert.Contains("@media (max-height: 500px) and (orientation: landscape)", cssContent);
        Assert.Contains("height: 48px !important;", cssContent);
    }

    [Fact]
    public void Test105_ReceiptAndConfirmation_ResponsiveLayout_ScalesPaddingAndActionsForSmallViewports()
    {
        var repoRoot = FindRepoRoot();
        var siteCssPath = Path.Combine(repoRoot, "wwwroot", "css", "site.css");
        var cssContent = File.ReadAllText(siteCssPath);

        // 1. Receipt container & header padding scales on mobile
        Assert.Contains(".receipt-container", cssContent);
        Assert.Contains("padding: 0 0.5rem !important;", cssContent);
        Assert.Contains(".receipt-header,", cssContent);
        Assert.Contains("padding: 1.25rem 1rem !important;", cssContent);

        // 2. Receipt action bar stacks vertically on mobile
        Assert.Contains(".receipt-action-bar", cssContent);
        Assert.Contains("flex-direction: column !important;", cssContent);

        // 3. Receipt table horizontal touch scroller
        Assert.Contains(".receipt-table", cssContent);
        Assert.Contains("display: block;", cssContent);

        // 4. Confirmation hero banner and next-steps roadmap responsiveness
        Assert.Contains(".confirm-hero-banner", cssContent);
        Assert.Contains("padding: 1.5rem 1rem !important;", cssContent);
        Assert.Contains(".next-steps-grid", cssContent);
        Assert.Contains("grid-template-columns: 1fr !important;", cssContent);
        Assert.Contains("grid-template-columns: repeat(2, 1fr) !important;", cssContent);
        Assert.Contains("grid-template-columns: repeat(4, 1fr) !important;", cssContent);
    }
}
