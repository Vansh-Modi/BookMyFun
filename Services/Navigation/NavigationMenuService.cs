using System.Collections.Generic;

namespace BookMyFun.Services.Navigation
{
    public class NavigationMenuItem
    {
        public string Title { get; set; } = string.Empty;
        public string Controller { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string? IconColorClass { get; set; }
        public string? Heading { get; set; }
        public bool IsDivider { get; set; }
        public bool IsLogout { get; set; }
        public object? RouteValues { get; set; }
    }

    public static class NavigationMenuDefinition
    {
        public static string GetDashboardNameForRole(string? role)
        {
            return (role ?? string.Empty).Trim() switch
            {
                "Admin" => "Admin Dashboard",
                "Employee" => "Employee Dashboard",
                "EventManager" => "Event / Decor Dashboard",
                "Owner" => "Host Dashboard",
                "Client" => "My Bookings",
                _ => "My Account"
            };
        }

        public static (string Controller, string Action) GetDashboardRouteForRole(string? role)
        {
            return (role ?? string.Empty).Trim() switch
            {
                "Admin" => ("Admin", "Index"),
                "Employee" => ("Employee", "Index"),
                "EventManager" => ("EventManager", "Index"),
                "Owner" => ("Owner", "Index"),
                "Client" => ("Client", "Index"),
                _ => ("Account", "Login")
            };
        }

        public static List<NavigationMenuItem> GetHeaderDropdownItems(string? role)
        {
            return (role ?? string.Empty).Trim() switch
            {
                "Admin" => new List<NavigationMenuItem>
                {
                    new() { Title = "Admin Dashboard", Controller = "Admin", Action = "Index", Icon = "bi-shield-lock-fill", IconColorClass = "text-warning" },
                    new() { Title = "Manage Bookings", Controller = "Admin", Action = "ManageBookings", Icon = "bi-calendar-check", IconColorClass = "text-success" },
                    new() { Title = "Manage Properties", Controller = "Admin", Action = "ManageProperties", Icon = "bi-building", IconColorClass = "text-warning" },
                    new() { Title = "My Profile", Controller = "Account", Action = "Profile", Icon = "bi-person-circle", IconColorClass = "text-primary" },
                    new() { IsDivider = true },
                    new() { Title = "Log Out", IsLogout = true, Icon = "bi-box-arrow-right", IconColorClass = "text-danger" }
                },
                "Employee" => new List<NavigationMenuItem>
                {
                    new() { Title = "Employee Dashboard", Controller = "Employee", Action = "Index", Icon = "bi-headset", IconColorClass = "text-info" },
                    new() { Title = "Property Approvals", Controller = "Employee", Action = "ManageProperties", Icon = "bi-building", IconColorClass = "text-warning" },
                    new() { Title = "Guest KYC Queue", Controller = "Employee", Action = "IdVerifications", Icon = "bi-person-vcard", IconColorClass = "text-info" },
                    new() { Title = "My Profile", Controller = "Account", Action = "Profile", Icon = "bi-person-circle", IconColorClass = "text-primary" },
                    new() { IsDivider = true },
                    new() { Title = "Log Out", IsLogout = true, Icon = "bi-box-arrow-right", IconColorClass = "text-danger" }
                },
                "Owner" => new List<NavigationMenuItem>
                {
                    new() { Title = "Host Dashboard", Controller = "Owner", Action = "Index", Icon = "bi-speedometer2", IconColorClass = "text-warning" },
                    new() { Title = "List New Farm", Controller = "Owner", Action = "CreateProperty", Icon = "bi-plus-circle-fill", IconColorClass = "text-success" },
                    new() { Title = "Deposit Claims", Controller = "Owner", Action = "DamageClaims", Icon = "bi-shield-exclamation", IconColorClass = "text-danger" },
                    new() { Title = "Subscription Plans", Controller = "Owner", Action = "Subscriptions", Icon = "bi-award", IconColorClass = "text-primary" },
                    new() { Title = "My Profile", Controller = "Account", Action = "Profile", Icon = "bi-person-circle", IconColorClass = "text-primary" },
                    new() { IsDivider = true },
                    new() { Title = "Log Out", IsLogout = true, Icon = "bi-box-arrow-right", IconColorClass = "text-danger" }
                },
                "EventManager" => new List<NavigationMenuItem>
                {
                    new() { Title = "Event / Decor Dashboard", Controller = "EventManager", Action = "Index", Icon = "bi-palette2", IconColorClass = "text-primary" },
                    new() { Title = "Décor Packages", Controller = "EventManager", Action = "Packages", Icon = "bi-box-seam", IconColorClass = "text-success" },
                    new() { Title = "Assigned Setups", Controller = "EventManager", Action = "AssignedSetups", Icon = "bi-calendar-event", IconColorClass = "text-warning" },
                    new() { Title = "My Profile", Controller = "Account", Action = "Profile", Icon = "bi-person-circle", IconColorClass = "text-primary" },
                    new() { IsDivider = true },
                    new() { Title = "Log Out", IsLogout = true, Icon = "bi-box-arrow-right", IconColorClass = "text-danger" }
                },
                "Client" => new List<NavigationMenuItem>
                {
                    new() { Title = "My Bookings", Controller = "Client", Action = "Index", Icon = "bi-calendar-check", IconColorClass = "text-success" },
                    new() { Title = "Receipts", Controller = "Client", Action = "Receipts", Icon = "bi-receipt", IconColorClass = "text-info" },
                    new() { Title = "My Profile", Controller = "Account", Action = "Profile", Icon = "bi-person-circle", IconColorClass = "text-primary" },
                    new() { IsDivider = true },
                    new() { Title = "Log Out", IsLogout = true, Icon = "bi-box-arrow-right", IconColorClass = "text-danger" }
                },
                _ => new List<NavigationMenuItem>()
            };
        }

        public static List<NavigationMenuItem> GetSidebarDrawerItems(string? role, bool isAuthenticated)
        {
            if (!isAuthenticated || string.IsNullOrWhiteSpace(role))
            {
                // Public menu for guests
                return new List<NavigationMenuItem>
                {
                    new() { Title = "Explore Stays", Controller = "Properties", Action = "Index", Icon = "bi-compass", IconColorClass = "text-success" },
                    new() { Title = "Villas & Estates", Controller = "Properties", Action = "Index", RouteValues = new { type = "Villa" }, Icon = "bi-building", IconColorClass = "text-primary" },
                    new() { Title = "Farmhouses & Grounds", Controller = "Properties", Action = "Index", RouteValues = new { type = "Farmhouse" }, Icon = "bi-tree", IconColorClass = "text-success" },
                    new() { Title = "Resorts & Venues", Controller = "Properties", Action = "Index", RouteValues = new { type = "Resort" }, Icon = "bi-geo-alt", IconColorClass = "text-danger" },
                    new() { Title = "Support & Help", Controller = "Home", Action = "Contact", Icon = "bi-headset", IconColorClass = "text-warning" },
                    new() { Title = "Sign In", Controller = "Account", Action = "Login", Icon = "bi-box-arrow-in-right", IconColorClass = "text-primary" }
                };
            }

            return role.Trim() switch
            {
                "Admin" => new List<NavigationMenuItem>
                {
                    new() { Title = "Admin Dashboard", Controller = "Admin", Action = "Index", Icon = "bi-speedometer2", IconColorClass = "text-primary" },
                    new() { Heading = "Bookings & Refunds" },
                    new() { Title = "Bookings", Controller = "Admin", Action = "ManageBookings", Icon = "bi-calendar-check", IconColorClass = "text-success" },
                    new() { Title = "Refunds Queue", Controller = "Admin", Action = "RefundsQueue", Icon = "bi-arrow-counterclockwise", IconColorClass = "text-info" },
                    new() { Title = "Damage Claims", Controller = "Admin", Action = "DamageClaims", Icon = "bi-shield-shaded", IconColorClass = "text-danger" },
                    new() { Heading = "Properties" },
                    new() { Title = "Manage Properties", Controller = "Admin", Action = "ManageProperties", Icon = "bi-building", IconColorClass = "text-warning" },
                    new() { Title = "Add Property", Controller = "Admin", Action = "CreateProperty", Icon = "bi-plus-circle", IconColorClass = "text-success" },
                    new() { Heading = "Users & Partners" },
                    new() { Title = "Event Verifications", Controller = "Admin", Action = "EventManagerVerifications", Icon = "bi-patch-check-fill", IconColorClass = "text-primary" },
                    new() { Title = "Flagged Contacts", Controller = "Admin", Action = "FlaggedContacts", Icon = "bi-shield-x", IconColorClass = "text-danger" },
                    new() { Title = "Hosts & Owners", Controller = "Admin", Action = "ManageOwners", Icon = "bi-person-badge", IconColorClass = "text-warning" },
                    new() { Title = "Staff & Employees", Controller = "Admin", Action = "ManageEmployees", Icon = "bi-person-workspace", IconColorClass = "text-primary" },
                    new() { Title = "Partner Leads", Controller = "Admin", Action = "PartnerLeads", Icon = "bi-people", IconColorClass = "text-success" },
                    new() { Heading = "KYC" },
                    new() { Title = "Guest KYC Queue", Controller = "Admin", Action = "IdVerifications", Icon = "bi-person-vcard", IconColorClass = "text-info" },
                    new() { Heading = "Finance & Settings" },
                    new() { Title = "Analytics", Controller = "Admin", Action = "DynamicAnalytics", Icon = "bi-graph-up-arrow", IconColorClass = "text-success" },
                    new() { Title = "Platform Settings", Controller = "Admin", Action = "PlatformSettings", Icon = "bi-gear-fill", IconColorClass = "text-secondary" }
                },
                "Employee" => new List<NavigationMenuItem>
                {
                    new() { Title = "Employee Dashboard", Controller = "Employee", Action = "Index", Icon = "bi-speedometer2", IconColorClass = "text-primary" },
                    new() { Heading = "Verification Queues" },
                    new() { Title = "Property Approvals", Controller = "Employee", Action = "ManageProperties", Icon = "bi-building", IconColorClass = "text-warning" },
                    new() { Title = "Guest KYC Queue", Controller = "Employee", Action = "IdVerifications", Icon = "bi-person-vcard", IconColorClass = "text-info" },
                    new() { Title = "Partner Approvals", Controller = "Employee", Action = "PartnerApprovals", Icon = "bi-patch-check", IconColorClass = "text-success" },
                    new() { Heading = "Customer & Booking Operations" },
                    new() { Title = "Contact Messages", Controller = "Employee", Action = "SupportQueries", Icon = "bi-envelope", IconColorClass = "text-primary" },
                    new() { Title = "Refunds & Disputes", Controller = "Employee", Action = "RefundRequests", Icon = "bi-arrow-counterclockwise", IconColorClass = "text-danger" },
                    new() { Title = "Damage Claims", Controller = "Employee", Action = "DamageClaims", Icon = "bi-shield-exclamation", IconColorClass = "text-warning" },
                    new() { Heading = "Platform Oversight" },
                    new() { Title = "Event Commissions", Controller = "Employee", Action = "EventCommissions", Icon = "bi-percent", IconColorClass = "text-warning" }
                },
                "Owner" => new List<NavigationMenuItem>
                {
                    new() { Title = "Host Dashboard", Controller = "Owner", Action = "Index", Icon = "bi-grid-fill", IconColorClass = "text-warning" },
                    new() { Heading = "Property & Bookings" },
                    new() { Title = "List New Farm", Controller = "Owner", Action = "CreateProperty", Icon = "bi-plus-circle-fill", IconColorClass = "text-success" },
                    new() { Title = "Deposit Claims", Controller = "Owner", Action = "DamageClaims", Icon = "bi-shield-exclamation", IconColorClass = "text-danger" },
                    new() { Title = "Subscription Plans", Controller = "Owner", Action = "Subscriptions", Icon = "bi-award", IconColorClass = "text-primary" },
                    new() { Title = "Notification Channels", Controller = "Owner", Action = "NotificationSettings", Icon = "bi-bell-fill", IconColorClass = "text-warning" }
                },
                "EventManager" => new List<NavigationMenuItem>
                {
                    new() { Title = "Event / Decor Dashboard", Controller = "EventManager", Action = "Index", Icon = "bi-speedometer2", IconColorClass = "text-primary" },
                    new() { Heading = "Services & Packages" },
                    new() { Title = "Service Areas", Controller = "EventManager", Action = "ServiceAreas", Icon = "bi-geo-alt-fill", IconColorClass = "text-primary" },
                    new() { Title = "Décor Packages", Controller = "EventManager", Action = "Packages", Icon = "bi-box-seam", IconColorClass = "text-success" },
                    new() { Title = "Assigned Setups", Controller = "EventManager", Action = "AssignedSetups", Icon = "bi-calendar-event", IconColorClass = "text-warning" },
                    new() { Title = "Inbox & Orders", Controller = "EventManager", Action = "Inbox", Icon = "bi-inbox-fill", IconColorClass = "text-primary" },
                    new() { Title = "Partner Profile", Controller = "EventManager", Action = "Profile", Icon = "bi-person-badge-fill", IconColorClass = "text-secondary" },
                    new() { Title = "Earnings & Payouts", Controller = "EventManager", Action = "Financials", Icon = "bi-wallet2", IconColorClass = "text-info" }
                },
                "Client" => new List<NavigationMenuItem>
                {
                    new() { Title = "My Bookings", Controller = "Client", Action = "Index", Icon = "bi-calendar-check", IconColorClass = "text-primary" },
                    new() { Title = "Receipts", Controller = "Client", Action = "Receipts", Icon = "bi-receipt", IconColorClass = "text-info" },
                    new() { Title = "Explore Stays", Controller = "Properties", Action = "Index", Icon = "bi-compass", IconColorClass = "text-success" },
                    new() { Title = "Profile & Settings", Controller = "Account", Action = "Profile", Icon = "bi-person", IconColorClass = "text-secondary" },
                    new() { Title = "Support Desk", Controller = "Home", Action = "Contact", Icon = "bi-headset", IconColorClass = "text-warning" }
                },
                _ => new List<NavigationMenuItem>()
            };
        }
    }
}
