using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public class EventPackageRepository : IEventPackageRepository
    {
        private readonly DBHelper _db;

        public EventPackageRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<List<EventPackage>> GetAllPackagesAsync()
        {
            const string query = @"
                SELECT ep.*, u.FullName AS CreatedByUserName
                FROM EventPackages ep
                LEFT JOIN Users u ON ep.CreatedByUserID = u.UserID
                ORDER BY ep.PackageID DESC";

            return await _db.ExecuteReaderListAsync(query, MapPackage);
        }

        public async Task<List<EventPackage>> GetActivePackagesAsync()
        {
            const string query = @"
                SELECT ep.*, u.FullName AS CreatedByUserName
                FROM EventPackages ep
                LEFT JOIN Users u ON ep.CreatedByUserID = u.UserID
                WHERE ep.IsActive = 1
                ORDER BY ep.BasePrice ASC";

            return await _db.ExecuteReaderListAsync(query, MapPackage);
        }

        public async Task<EventPackage?> GetPackageByIdAsync(int packageId)
        {
            const string query = @"
                SELECT ep.*, u.FullName AS CreatedByUserName
                FROM EventPackages ep
                LEFT JOIN Users u ON ep.CreatedByUserID = u.UserID
                WHERE ep.PackageID = @PackageID";

            return await _db.ExecuteReaderSingleAsync(query, MapPackage, new SqlParameter("@PackageID", packageId));
        }

        public async Task<int> CreatePackageAsync(EventPackage package)
        {
            var query = @"
                INSERT INTO EventPackages (Name, Category, BasePrice, Description, MediaUrl, IncludedItems, IsActive, CreatedByUserID, CreatedAt)
                VALUES (@Name, @Category, @BasePrice, @Description, @MediaUrl, @IncludedItems, @IsActive, @CreatedByUserID, GETDATE());
                SELECT SCOPE_IDENTITY();";

            var result = await _db.ExecuteScalarAsync(query,
                new SqlParameter("@Name", package.Name),
                new SqlParameter("@Category", package.Category),
                new SqlParameter("@BasePrice", package.BasePrice),
                new SqlParameter("@Description", (object?)package.Description ?? DBNull.Value),
                new SqlParameter("@MediaUrl", (object?)package.MediaUrl ?? DBNull.Value),
                new SqlParameter("@IncludedItems", (object?)package.IncludedItems ?? DBNull.Value),
                new SqlParameter("@IsActive", package.IsActive),
                new SqlParameter("@CreatedByUserID", (object?)package.CreatedByUserID ?? DBNull.Value));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<bool> UpdatePackageAsync(EventPackage package)
        {
            var query = @"
                UPDATE EventPackages
                SET Name = @Name,
                    Category = @Category,
                    BasePrice = @BasePrice,
                    Description = @Description,
                    MediaUrl = @MediaUrl,
                    IncludedItems = @IncludedItems,
                    IsActive = @IsActive
                WHERE PackageID = @PackageID";

            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@PackageID", package.PackageID),
                new SqlParameter("@Name", package.Name),
                new SqlParameter("@Category", package.Category),
                new SqlParameter("@BasePrice", package.BasePrice),
                new SqlParameter("@Description", (object?)package.Description ?? DBNull.Value),
                new SqlParameter("@MediaUrl", (object?)package.MediaUrl ?? DBNull.Value),
                new SqlParameter("@IncludedItems", (object?)package.IncludedItems ?? DBNull.Value),
                new SqlParameter("@IsActive", package.IsActive));

            return rows > 0;
        }

        public async Task<bool> DeletePackageAsync(int packageId)
        {
            var query = "DELETE FROM EventPackages WHERE PackageID = @PackageID";
            var rows = await _db.ExecuteNonQueryAsync(query, new SqlParameter("@PackageID", packageId));
            return rows > 0;
        }

        public async Task<List<Booking>> GetAssignedEventBookingsAsync(int decoratorUserId)
        {
            const string query = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                INNER JOIN Properties p ON b.PropertyID = p.PropertyID
                INNER JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.IsEventBooking = 1 AND (b.DecoratorAssignedID = @DecoratorID OR @DecoratorID = 0)
                ORDER BY b.CheckInDate ASC";

            return await _db.ExecuteReaderListAsync(query, MapEventBooking, new SqlParameter("@DecoratorID", decoratorUserId));
        }

        public async Task<List<Booking>> GetAllEventBookingsAsync()
        {
            return await GetAssignedEventBookingsAsync(0);
        }

        public async Task<bool> UpdateSetupStatusAsync(int bookingId, string setupStatus, string? setupProofMediaUrls, int? decoratorId)
        {
            var query = @"
                UPDATE Bookings
                SET SetupStatus = @SetupStatus,
                    SetupProofMediaUrls = COALESCE(@SetupProofMediaUrls, SetupProofMediaUrls),
                    DecoratorAssignedID = COALESCE(@DecoratorID, DecoratorAssignedID)
                WHERE BookingID = @BookingID";

            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@BookingID", bookingId),
                new SqlParameter("@SetupStatus", setupStatus),
                new SqlParameter("@SetupProofMediaUrls", (object?)setupProofMediaUrls ?? DBNull.Value),
                new SqlParameter("@DecoratorID", (object?)decoratorId ?? DBNull.Value));

            return rows > 0;
        }

        public async Task<bool> AssignDecoratorAsync(int bookingId, int decoratorUserId)
        {
            var query = @"
                UPDATE Bookings
                SET DecoratorAssignedID = @DecoratorID,
                    SetupStatus = CASE WHEN SetupStatus = 'Pending' THEN 'Assigned' ELSE SetupStatus END
                WHERE BookingID = @BookingID";

            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@BookingID", bookingId),
                new SqlParameter("@DecoratorID", decoratorUserId));

            return rows > 0;
        }

        public async Task<List<User>> GetDecoratorsAsync()
        {
            const string query = "SELECT * FROM Users WHERE Role = 'EventManager' AND IsActive = 1 ORDER BY FullName";
            return await _db.ExecuteReaderListAsync(query, MapUser);
        }

        private static User MapUser(SqlDataReader reader)
        {
            return new User
            {
                UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                Role = reader.GetString(reader.GetOrdinal("Role")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
            };
        }

        private static EventPackage MapPackage(SqlDataReader reader)
        {
            return new EventPackage
            {
                PackageID = reader.GetInt32(reader.GetOrdinal("PackageID")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Category = reader.GetString(reader.GetOrdinal("Category")),
                BasePrice = reader.GetDecimal(reader.GetOrdinal("BasePrice")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                MediaUrl = reader.IsDBNull(reader.GetOrdinal("MediaUrl")) ? null : reader.GetString(reader.GetOrdinal("MediaUrl")),
                IncludedItems = reader.IsDBNull(reader.GetOrdinal("IncludedItems")) ? null : reader.GetString(reader.GetOrdinal("IncludedItems")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive")),
                CreatedByUserID = reader.IsDBNull(reader.GetOrdinal("CreatedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("CreatedByUserID")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                CreatedByUserName = reader.IsDBNull(reader.GetOrdinal("CreatedByUserName")) ? null : reader.GetString(reader.GetOrdinal("CreatedByUserName"))
            };
        }

        private static Booking MapEventBooking(SqlDataReader reader)
        {
            var b = new Booking
            {
                BookingID = reader.GetInt32(reader.GetOrdinal("BookingID")),
                PropertyID = reader.GetInt32(reader.GetOrdinal("PropertyID")),
                ClientID = reader.GetInt32(reader.GetOrdinal("ClientID")),
                CheckInDate = reader.GetDateTime(reader.GetOrdinal("CheckInDate")),
                CheckOutDate = reader.GetDateTime(reader.GetOrdinal("CheckOutDate")),
                TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                PropertyTitle = reader.IsDBNull(reader.GetOrdinal("PropertyTitle")) ? null : reader.GetString(reader.GetOrdinal("PropertyTitle")),
                ClientName = reader.IsDBNull(reader.GetOrdinal("ClientName")) ? null : reader.GetString(reader.GetOrdinal("ClientName")),
                ClientEmail = reader.IsDBNull(reader.GetOrdinal("ClientEmail")) ? null : reader.GetString(reader.GetOrdinal("ClientEmail")),
                ClientPhone = reader.IsDBNull(reader.GetOrdinal("ClientPhone")) ? null : reader.GetString(reader.GetOrdinal("ClientPhone")),
                IsEventBooking = reader.GetBoolean(reader.GetOrdinal("IsEventBooking")),
                EventPackageID = reader.IsDBNull(reader.GetOrdinal("EventPackageID")) ? null : reader.GetInt32(reader.GetOrdinal("EventPackageID")),
                CustomEventNotes = reader.IsDBNull(reader.GetOrdinal("CustomEventNotes")) ? null : reader.GetString(reader.GetOrdinal("CustomEventNotes")),
                EventDate = reader.IsDBNull(reader.GetOrdinal("EventDate")) ? null : reader.GetDateTime(reader.GetOrdinal("EventDate")),
                DecoratorAssignedID = reader.IsDBNull(reader.GetOrdinal("DecoratorAssignedID")) ? null : reader.GetInt32(reader.GetOrdinal("DecoratorAssignedID")),
                SetupStatus = reader.IsDBNull(reader.GetOrdinal("SetupStatus")) ? "Pending" : reader.GetString(reader.GetOrdinal("SetupStatus")),
                SetupProofMediaUrls = reader.IsDBNull(reader.GetOrdinal("SetupProofMediaUrls")) ? null : reader.GetString(reader.GetOrdinal("SetupProofMediaUrls")),
                EventPackagePriceSnapshot = reader.IsDBNull(reader.GetOrdinal("EventPackagePriceSnapshot")) ? 0.00m : reader.GetDecimal(reader.GetOrdinal("EventPackagePriceSnapshot")),
                EventPackageName = reader.IsDBNull(reader.GetOrdinal("EventPackageName")) ? null : reader.GetString(reader.GetOrdinal("EventPackageName")),
                DecoratorName = reader.IsDBNull(reader.GetOrdinal("DecoratorName")) ? null : reader.GetString(reader.GetOrdinal("DecoratorName"))
            };
            return b;
        }
    }
}
