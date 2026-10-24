using System;
using System.IO;
using Xunit;

namespace BookMyFun.Tests
{
    public class DashboardSidebarTests
    {
        [Theory]
        [InlineData("Admin", "Overview", "ManageBookings", "ManageProperties", "Settings")]
        [InlineData("Employee", "Staff Dashboard", "RefundsQueue", "IdVerifications", "ManageProperties")]
        [InlineData("Owner", "Host Dashboard", "CreateProperty", "DamageClaims", "Subscriptions")]
        [InlineData("EventManager", "Event Dashboard", "ServiceAreas", "Packages", "Financials")]
        [InlineData("Client", "My Trips", "Explore Stays", "Profile &amp; Settings", "Support Desk")]
        public void DashboardSidebarNav_ContainsRequiredRoleSpecificLinks(string role, params string[] expectedKeywords)
        {
            var navFile = TestPathHelper.GetPath("Views", "Shared", "_DashboardSidebarNav.cshtml");
            Assert.True(File.Exists(navFile), $"Expected _DashboardSidebarNav.cshtml to exist at {navFile}");
            var content = File.ReadAllText(navFile);

            Assert.Contains($"role == \"{role}\"", content);
            foreach (var keyword in expectedKeywords)
            {
                Assert.Contains(keyword, content);
            }
        }

        [Fact]
        public void DashboardLayout_ContainsResponsiveSidebarAndTopBarComponents()
        {
            var layoutFile = TestPathHelper.GetPath("Views", "Shared", "_DashboardLayout.cshtml");
            Assert.True(File.Exists(layoutFile), $"Expected _DashboardLayout.cshtml to exist at {layoutFile}");
            var content = File.ReadAllText(layoutFile);

            // Responsive components
            Assert.Contains("dashboard-sidebar-container", content);
            Assert.Contains("mobileDashboardDrawer", content);
            Assert.Contains("offcanvas", content);

            // Top bar requirements
            Assert.Contains("dashboard-topbar", content);
            Assert.Contains("notifBellDropdown", content);
            Assert.Contains("theme-switch-btn", content);
            Assert.Contains("userProfileDropdown", content);
        }

        [Fact]
        public void SiteCss_ContainsTabletIconRailAndMobileDrawerRules()
        {
            var cssFile = TestPathHelper.GetPath("wwwroot", "css", "site.css");
            Assert.True(File.Exists(cssFile), $"Expected site.css to exist at {cssFile}");
            var css = File.ReadAllText(cssFile);

            Assert.Contains(".dashboard-sidebar-container", css);
            Assert.Contains("@media (min-width: 768px) and (max-width: 991.98px)", css);
            Assert.Contains("width: 72px;", css);
            Assert.Contains("@media (max-width: 767.98px)", css);
        }
    }
}
