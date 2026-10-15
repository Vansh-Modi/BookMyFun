using BookMyFun.Controllers;
using BookMyFun.Data;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Data
{
    public static class DatabaseSeeder
    {
        public static async Task SeedInitialDataAsync(DBHelper db)
        {
            // 1. Seed Default Users across all 5 roles if empty
            var countResult = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Users");
            int userCount = countResult != null ? Convert.ToInt32(countResult) : 0;

            if (userCount == 0)
            {
                string adminHash = AccountController.HashPassword("Admin@123");
                string empHash = AccountController.HashPassword("Emp@123");
                string ownerHash = AccountController.HashPassword("Owner@123");
                string clientHash = AccountController.HashPassword("Client@123");
                string eventHash = AccountController.HashPassword("Event@123");

                var seedUsersSql = @"
                    INSERT INTO Users (FullName, Email, PasswordHash, Phone, Role) VALUES
                    ('Super Admin', 'admin@bookmyfun.com', @AdminHash, '+919999900001', 'Admin'),
                    ('Operations Staff', 'employee@bookmyfun.com', @EmpHash, '+919999900002', 'Employee'),
                    ('Rajesh Farmhouse Owner', 'owner@bookmyfun.com', @OwnerHash, '+919999900003', 'Owner'),
                    ('Vipul Guest', 'client@bookmyfun.com', @ClientHash, '+919999900004', 'Client'),
                    ('Creative Event Decorator', 'eventmanager@bookmyfun.com', @EventHash, '+919999900005', 'EventManager');";

                await db.ExecuteNonQueryAsync(seedUsersSql,
                    new SqlParameter("@AdminHash", adminHash),
                    new SqlParameter("@EmpHash", empHash),
                    new SqlParameter("@OwnerHash", ownerHash),
                    new SqlParameter("@ClientHash", clientHash),
                    new SqlParameter("@EventHash", eventHash));
            }

            // 2. Seed PropertyTypes
            var typeCount = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM PropertyTypes");
            if (Convert.ToInt32(typeCount) == 0)
            {
                var seedTypesSql = @"
                    INSERT INTO PropertyTypes (TypeName, Description) VALUES
                    ('Villa', 'Luxury private villa with exclusive pool and lawns'),
                    ('Farmhouse', 'Spacious countryside farmhouse with greenery and outdoor games'),
                    ('Resort', 'Full-service resort property with club amenities'),
                    ('Cottage', 'Cozy aesthetic cottage for intimate weekend getaways');";
                await db.ExecuteNonQueryAsync(seedTypesSql);
            }

            // 3. Seed Occasions
            var occCount = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Occasions");
            if (Convert.ToInt32(occCount) == 0)
            {
                var seedOccSql = @"
                    INSERT INTO Occasions (OccasionName, Description) VALUES
                    ('One-day Outing', 'Day outing with friends, pool parties and celebrations'),
                    ('Couple Stay', 'Private retreat for romantic getaways'),
                    ('Corporate Retreat', 'Team offsite with conference and gathering spaces'),
                    ('Family Gathering', 'Reunions and family celebrations with spacious lawns');";
                await db.ExecuteNonQueryAsync(seedOccSql);
            }

            // 4. Seed Subscription Plans
            var planCount = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM SubscriptionPlans");
            if (Convert.ToInt32(planCount) == 0)
            {
                var seedPlansSql = @"
                    INSERT INTO SubscriptionPlans (PlanName, Price, DurationInDays, MaxListings, IsActive) VALUES
                    ('Starter Partner', 1999.00, 30, 2, 1),
                    ('Gold Partner', 4999.00, 90, 5, 1),
                    ('Platinum Enterprise', 9999.00, 180, 15, 1);";
                await db.ExecuteNonQueryAsync(seedPlansSql);
            }

            // 5. Seed sample active property
            var propCount = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM Properties");
            if (Convert.ToInt32(propCount) == 0)
            {
                var seedPropSql = @"
                    DECLARE @OwnerId INT = (SELECT TOP 1 UserID FROM Users WHERE Role = 'Owner');
                    DECLARE @TypeId INT = (SELECT TOP 1 PropertyTypeID FROM PropertyTypes WHERE TypeName = 'Villa');

                    IF @OwnerId IS NOT NULL AND @TypeId IS NOT NULL
                    BEGIN
                        INSERT INTO Properties (OwnerID, PropertyTypeID, Title, Description, Location, FormattedAddress, Latitude, Longitude, PricePerNight, SecurityDepositAmount, CommissionRate, FreeCancellationWindowHours, CancellationCutoffHours, CancellationFeeType, CancellationFeeValue, MaxGuests, ListingStatus, IsApproved)
                        VALUES (@OwnerId, @TypeId, 'Serene Hills Private Villa & Pool', 'Stunning luxury 4BHK villa featuring a private infinity pool, sprawling manicured lawn, barbecue area, and panoramic mountain views.', 'Lonavala, Maharashtra', 'Tungarli Lake Rd, Lonavala, MH 410401', 18.761234, 73.415678, 12500.00, 5000.00, 12.00, 48, 24, 'percentage', 10.00, 15, 'Active', 1);

                        DECLARE @NewPropId INT = SCOPE_IDENTITY();
                        INSERT INTO PropertyImages (PropertyID, ImageURL, IsPrimary)
                        VALUES (@NewPropId, 'https://images.unsplash.com/photo-1580587771525-78b9dba3b914?auto=format&fit=crop&w=1200&q=80', 1);

                        INSERT INTO PropertyMedia (PropertyID, MediaType, SourceType, StorageUrl, IsPrimary, SortOrder)
                        VALUES (@NewPropId, 'photo', 'url', 'https://images.unsplash.com/photo-1580587771525-78b9dba3b914?auto=format&fit=crop&w=1200&q=80', 1, 1);
                    END";
                await db.ExecuteNonQueryAsync(seedPropSql);
            }
        }
    }
}
