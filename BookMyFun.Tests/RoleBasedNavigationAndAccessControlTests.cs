using System.IO;
using System.Linq;
using BookMyFun.Services.Navigation;
using Xunit;

namespace BookMyFun.Tests
{
    public class RoleBasedNavigationAndAccessControlTests
    {
        [Fact]
        public void ClientMenu_NeverContainsHostDashboard_ContainsClientLinksOnly()
        {
            var headerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Client");
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems("Client", true);

            // Client must NEVER see Host Dashboard
            Assert.DoesNotContain(headerItems, i => i.Title.Contains("Host Dashboard"));
            Assert.DoesNotContain(drawerItems, i => i.Title.Contains("Host Dashboard"));

            // Client sees Client links only: My Bookings, Receipts, My Profile, Log Out
            Assert.Contains(headerItems, i => i.Title == "My Bookings" && i.Controller == "Client" && i.Action == "Index");
            Assert.Contains(headerItems, i => i.Title == "Receipts" && i.Controller == "Client" && i.Action == "Receipts");
            Assert.Contains(headerItems, i => i.Title == "My Profile" && i.Controller == "Account" && i.Action == "Profile");
            Assert.Contains(headerItems, i => i.IsLogout);

            Assert.Contains(drawerItems, i => i.Title == "My Bookings");
            Assert.Contains(drawerItems, i => i.Title == "Receipts");
        }

        [Fact]
        public void OwnerMenu_ContainsHostDashboard_AndHostLinks()
        {
            var headerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Owner");
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems("Owner", true);

            // Owner sees Host Dashboard
            Assert.Contains(headerItems, i => i.Title == "Host Dashboard" && i.Controller == "Owner" && i.Action == "Index");
            Assert.Contains(headerItems, i => i.Title == "List New Farm" && i.Controller == "Owner" && i.Action == "CreateProperty");
            Assert.Contains(headerItems, i => i.Title == "Deposit Claims" && i.Controller == "Owner" && i.Action == "DamageClaims");
            Assert.Contains(headerItems, i => i.Title == "Subscription Plans" && i.Controller == "Owner" && i.Action == "Subscriptions");

            Assert.Contains(drawerItems, i => i.Title == "Host Dashboard");
            Assert.Contains(drawerItems, i => i.Title == "List New Farm");
        }

        [Fact]
        public void EventManagerMenu_ContainsEventDashboard_NeverHostDashboard()
        {
            var headerItems = NavigationMenuDefinition.GetHeaderDropdownItems("EventManager");
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems("EventManager", true);

            // EventManager must NOT see Host Dashboard
            Assert.DoesNotContain(headerItems, i => i.Title.Contains("Host Dashboard"));
            Assert.DoesNotContain(drawerItems, i => i.Title.Contains("Host Dashboard"));

            // EventManager sees Event / Decor Dashboard and event manager links
            Assert.Contains(headerItems, i => i.Title == "Event / Decor Dashboard" && i.Controller == "EventManager" && i.Action == "Index");
            Assert.Contains(headerItems, i => i.Title == "Décor Packages" && i.Controller == "EventManager" && i.Action == "Packages");
            Assert.Contains(headerItems, i => i.Title == "Assigned Setups" && i.Controller == "EventManager" && i.Action == "AssignedSetups");

            Assert.Contains(drawerItems, i => i.Title == "Event / Decor Dashboard");
            Assert.Contains(drawerItems, i => i.Title == "Décor Packages");
        }

        [Fact]
        public void EmployeeMenu_ContainsEmployeeDashboard_NeverHostDashboard()
        {
            var headerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Employee");
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems("Employee", true);

            // Employee must NOT see Host Dashboard
            Assert.DoesNotContain(headerItems, i => i.Title.Contains("Host Dashboard"));
            Assert.DoesNotContain(drawerItems, i => i.Title.Contains("Host Dashboard"));

            // Employee sees Employee Dashboard and employee links
            Assert.Contains(headerItems, i => i.Title == "Employee Dashboard" && i.Controller == "Employee" && i.Action == "Index");
            Assert.Contains(headerItems, i => i.Title == "Property Approvals" && i.Controller == "Employee" && i.Action == "ManageProperties");
            Assert.Contains(headerItems, i => i.Title == "Guest KYC Queue" && i.Controller == "Employee" && i.Action == "IdVerifications");

            Assert.Contains(drawerItems, i => i.Title == "Employee Dashboard");
        }

        [Fact]
        public void AdminMenu_ContainsAdminDashboard_NeverHostDashboard()
        {
            var headerItems = NavigationMenuDefinition.GetHeaderDropdownItems("Admin");
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems("Admin", true);

            // Admin must NOT see Host Dashboard
            Assert.DoesNotContain(headerItems, i => i.Title.Contains("Host Dashboard"));
            Assert.DoesNotContain(drawerItems, i => i.Title.Contains("Host Dashboard"));

            // Admin sees Admin Dashboard and admin links
            Assert.Contains(headerItems, i => i.Title == "Admin Dashboard" && i.Controller == "Admin" && i.Action == "Index");
            Assert.Contains(headerItems, i => i.Title == "Manage Bookings" && i.Controller == "Admin" && i.Action == "ManageBookings");
            Assert.Contains(headerItems, i => i.Title == "Manage Properties" && i.Controller == "Admin" && i.Action == "ManageProperties");

            Assert.Contains(drawerItems, i => i.Title == "Admin Dashboard");
        }

        [Fact]
        public void GuestMenu_ShowsPublicMenuOnly()
        {
            var drawerItems = NavigationMenuDefinition.GetSidebarDrawerItems(null, false);

            Assert.Contains(drawerItems, i => i.Title == "Explore Stays");
            Assert.Contains(drawerItems, i => i.Title.Contains("Villas"));
            Assert.Contains(drawerItems, i => i.Title.Contains("Farmhouses"));
            Assert.Contains(drawerItems, i => i.Title.Contains("Resorts"));
            Assert.Contains(drawerItems, i => i.Title == "Support & Help");
            Assert.Contains(drawerItems, i => i.Title == "Sign In");

            Assert.DoesNotContain(drawerItems, i => i.Title.Contains("Dashboard"));
        }

        [Theory]
        [InlineData("Admin", "Admin", "Index")]
        [InlineData("Employee", "Employee", "Index")]
        [InlineData("EventManager", "EventManager", "Index")]
        [InlineData("Owner", "Owner", "Index")]
        [InlineData("Client", "Client", "Index")]
        [InlineData("", "Account", "Login")]
        public void DashboardRouteResolution_ResolvesToCorrectControllerAndAction(string role, string expectedController, string expectedAction)
        {
            var (controller, action) = NavigationMenuDefinition.GetDashboardRouteForRole(role);
            Assert.Equal(expectedController, controller);
            Assert.Equal(expectedAction, action);
        }

        [Fact]
        public void AccessDeniedView_Exists_AndRendersGoToMyDashboardButton()
        {
            var accessDeniedPath = TestPathHelper.GetPath("Views", "Account", "AccessDenied.cshtml");
            Assert.True(File.Exists(accessDeniedPath), $"AccessDenied.cshtml not found at {accessDeniedPath}");
            var content = File.ReadAllText(accessDeniedPath);

            // Verifies 403 heading and dynamic dashboard navigation
            Assert.Contains("Access Restricted (403)", content);
            Assert.Contains("NavigationMenuDefinition.GetDashboardRouteForRole", content);
            Assert.Contains("Go to @dashTitle", content);
        }
    }
}
