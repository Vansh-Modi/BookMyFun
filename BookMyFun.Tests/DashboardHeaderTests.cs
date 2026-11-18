using System;
using System.IO;
using System.Linq;
using BookMyFun.Services.Navigation;
using Xunit;

namespace BookMyFun.Tests
{
    public class DashboardHeaderTests
    {
        [Fact]
        public void Layout_HeaderExcludesDepositClaimsSubscriptionPlansNotificationChannels()
        {
            var layoutFile = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            Assert.True(File.Exists(layoutFile), $"Expected _Layout.cshtml to exist at {layoutFile}");
            var content = File.ReadAllText(layoutFile);

            // Isolate header section between <header> and </header>
            var headerStart = content.IndexOf("<header");
            var headerEnd = content.IndexOf("</header>", headerStart);
            Assert.True(headerStart > 0 && headerEnd > headerStart, "Header must be present");
            var headerContent = content.Substring(headerStart, headerEnd - headerStart);

            // Verify Deposit Claims, Subscription Plans, and Notification Channels are NOT in header
            Assert.DoesNotContain("Deposit Claims", headerContent);
            Assert.DoesNotContain("Subscription Plans", headerContent);
            Assert.DoesNotContain("Notification Channels", headerContent);

            // Notification Bell icon dropdown item is preserved
            Assert.Contains("id=\"mainHeaderNotifDropdownItem\"", headerContent);
            Assert.Contains("id=\"mainHeaderNotifBell\"", headerContent);
        }

        [Fact]
        public void Layout_ContainsHamburgerMenuBesideLogo_AndSideDrawerWithRequiredItems()
        {
            var layoutFile = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            var content = File.ReadAllText(layoutFile);

            // 1. Hamburger button beside logo with accessibility attributes
            Assert.Contains("id=\"mainNavDrawerToggle\"", content);
            Assert.Contains("aria-label=\"Open menu\"", content);
            Assert.Contains("aria-controls=\"mainNavDrawer\"", content);
            Assert.Contains("aria-expanded=\"false\"", content);

            // 2. Side drawer container with accessibility attributes
            Assert.Contains("id=\"mainNavDrawer\"", content);
            Assert.Contains("class=\"main-nav-drawer\"", content);
            Assert.Contains("role=\"dialog\"", content);
            Assert.Contains("aria-modal=\"true\"", content);

            // 3. Side drawer uses NavigationMenuDefinition to render role-based links
            Assert.Contains("NavigationMenuDefinition.GetSidebarDrawerItems", content);

            // Public guest items verified via NavigationMenuDefinition
            var guestItems = NavigationMenuDefinition.GetSidebarDrawerItems(null, false);
            Assert.Contains(guestItems, i => i.Title == "Explore Stays");
            Assert.Contains(guestItems, i => i.Title.Contains("Villas"));
            Assert.Contains(guestItems, i => i.Title.Contains("Farmhouses"));
            Assert.Contains(guestItems, i => i.Title.Contains("Resorts"));
            Assert.Contains(guestItems, i => i.Title == "Support & Help");

            // Host Dashboard is present for Owner drawer, but NEVER for guest or client
            var ownerItems = NavigationMenuDefinition.GetSidebarDrawerItems("Owner", true);
            Assert.Contains(ownerItems, i => i.Title == "Host Dashboard");

            var clientItems = NavigationMenuDefinition.GetSidebarDrawerItems("Client", true);
            Assert.DoesNotContain(clientItems, i => i.Title.Contains("Host Dashboard"));
        }

        [Fact]
        public void Layout_ContainsCompactAccountControl_WithInitial()
        {
            var layoutFile = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            var content = File.ReadAllText(layoutFile);

            // Avatar initial calculation and elements
            Assert.Contains("header-avatar-circle", content);
            Assert.Contains("userInitial", content);
            Assert.Contains("header-compact-signin", content);

            // Menu definition is wired into the account dropdown
            Assert.Contains("NavigationMenuDefinition.GetHeaderDropdownItems", content);

            // Host Dashboard is reachable for Owner, but never Client
            var ownerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Owner");
            Assert.Contains(ownerItems, i => i.Title == "Host Dashboard");

            var clientItems = NavigationMenuDefinition.GetHeaderDropdownItems("Client");
            Assert.DoesNotContain(clientItems, i => i.Title.Contains("Host Dashboard"));
        }

        [Fact]
        public void SiteCss_ContainsDrawerAndHeaderControlStyles()
        {
            var cssFile = TestPathHelper.GetPath("wwwroot", "css", "site.css");
            Assert.True(File.Exists(cssFile), $"Expected site.css to exist at {cssFile}");
            var css = File.ReadAllText(cssFile);

            Assert.Contains(".main-nav-drawer", css);
            Assert.Contains(".main-nav-drawer.open", css);
            Assert.Contains(".main-nav-drawer-backdrop", css);
            Assert.Contains(".header-compact-signin", css);
        }

        [Fact]
        public void DashboardSidebarNav_RetainsAllItems()
        {
            var navFile = TestPathHelper.GetPath("Views", "Shared", "_DashboardSidebarNav.cshtml");
            Assert.True(File.Exists(navFile), $"Expected _DashboardSidebarNav.cshtml to exist at {navFile}");
            var content = File.ReadAllText(navFile);

            // Owner sidebar items preserved
            Assert.Contains("Host Dashboard", content);
            Assert.Contains("List New Farm", content);
            Assert.Contains("Deposit Claims", content);
            Assert.Contains("Subscription Plans", content);
            Assert.Contains("Notification Channels", content);

            // Admin sidebar items preserved
            Assert.Contains("Overview", content);
            Assert.Contains("Damage Claims", content);
            Assert.Contains("ManageProperties", content);

            // Employee sidebar items preserved
            Assert.Contains("Staff Dashboard", content);
            Assert.Contains("Damage Claims", content);
        }

        [Fact]
        public void DropdownMenu_SimplifiedInHeaderForDashboardRoles()
        {
            var layoutFile = TestPathHelper.GetPath("Views", "Shared", "_Layout.cshtml");
            var content = File.ReadAllText(layoutFile);

            // Layout delegates dropdown items to NavigationMenuDefinition
            Assert.Contains("NavigationMenuDefinition.GetHeaderDropdownItems", content);

            // Admin dropdown has Admin Dashboard, My Profile, Log Out, never Host Dashboard
            var adminItems = NavigationMenuDefinition.GetHeaderDropdownItems("Admin");
            Assert.Contains(adminItems, i => i.Title == "Admin Dashboard");
            Assert.Contains(adminItems, i => i.Title == "My Profile");
            Assert.Contains(adminItems, i => i.IsLogout);
            Assert.DoesNotContain(adminItems, i => i.Title == "Host Dashboard");

            // Owner dropdown has Host Dashboard, My Profile, Log Out
            var ownerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Owner");
            Assert.Contains(ownerItems, i => i.Title == "Host Dashboard");
            Assert.Contains(ownerItems, i => i.Title == "My Profile");
            Assert.Contains(ownerItems, i => i.IsLogout);

            // Client dropdown has My Bookings, Receipts, My Profile, Log Out, NEVER Host Dashboard
            var clientItems = NavigationMenuDefinition.GetHeaderDropdownItems("Client");
            Assert.Contains(clientItems, i => i.Title == "My Bookings");
            Assert.Contains(clientItems, i => i.Title == "Receipts");
            Assert.Contains(clientItems, i => i.Title == "My Profile");
            Assert.Contains(clientItems, i => i.IsLogout);
            Assert.DoesNotContain(clientItems, i => i.Title.Contains("Host Dashboard"));
        }
    }
}
