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
            // CRITICAL (Section 5a): Only show packages from verified event managers
            const string query = @"
                SELECT ep.*, u.FullName AS CreatedByUserName
                FROM EventPackages ep
                LEFT JOIN Users u ON ep.CreatedByUserID = u.UserID
                WHERE ep.IsActive = 1
                  AND (u.VerificationStatus = 'Verified' OR u.VerificationStatus IS NULL OR ep.CreatedByUserID IS NULL)
                ORDER BY ep.BasePrice ASC";

            return await _db.ExecuteReaderListAsync(query, MapPackage);
        }

        public async Task<List<EventPackage>> GetPackagesByUserIdAsync(int userId)
        {
            const string query = @"
                SELECT ep.*, u.FullName AS CreatedByUserName
                FROM EventPackages ep
                LEFT JOIN Users u ON ep.CreatedByUserID = u.UserID
                WHERE ep.CreatedByUserID = @UserID
                ORDER BY ep.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(query, MapPackage, new SqlParameter("@UserID", userId));
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
                INSERT INTO EventPackages (Name, Category, BasePrice, Description, MediaUrl, IncludedItems, IsActive, CommissionRate, CreatedByUserID, CreatedAt)
                VALUES (@Name, @Category, @BasePrice, @Description, @MediaUrl, @IncludedItems, @IsActive, @CommissionRate, @CreatedByUserID, GETDATE());
                SELECT SCOPE_IDENTITY();";

            var result = await _db.ExecuteScalarAsync(query,
                new SqlParameter("@Name", package.Name),
                new SqlParameter("@Category", package.Category),
                new SqlParameter("@BasePrice", package.BasePrice),
                new SqlParameter("@Description", (object?)package.Description ?? DBNull.Value),
                new SqlParameter("@MediaUrl", (object?)package.MediaUrl ?? DBNull.Value),
                new SqlParameter("@IncludedItems", (object?)package.IncludedItems ?? DBNull.Value),
                new SqlParameter("@IsActive", package.IsActive),
                new SqlParameter("@CommissionRate", (object?)package.CommissionRate ?? DBNull.Value),
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
                    IsActive = @IsActive,
                    CommissionRate = @CommissionRate
                WHERE PackageID = @PackageID";

            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@PackageID", package.PackageID),
                new SqlParameter("@Name", package.Name),
                new SqlParameter("@Category", package.Category),
                new SqlParameter("@BasePrice", package.BasePrice),
                new SqlParameter("@Description", (object?)package.Description ?? DBNull.Value),
                new SqlParameter("@MediaUrl", (object?)package.MediaUrl ?? DBNull.Value),
                new SqlParameter("@IncludedItems", (object?)package.IncludedItems ?? DBNull.Value),
                new SqlParameter("@IsActive", package.IsActive),
                new SqlParameter("@CommissionRate", (object?)package.CommissionRate ?? DBNull.Value));

            return rows > 0;
        }

        public async Task<bool> UpdatePackageCommissionRateAsync(int packageId, decimal? commissionRate)
        {
            const string query = @"
                UPDATE EventPackages
                SET CommissionRate = @CommissionRate
                WHERE PackageID = @PackageID";

            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@PackageID", packageId),
                new SqlParameter("@CommissionRate", (object?)commissionRate ?? DBNull.Value));

            return rows > 0;
        }

        public async Task<bool> DeletePackageAsync(int packageId)
        {
            try
            {
                var query = @"
                    UPDATE Bookings SET EventPackageID = NULL WHERE EventPackageID = @PackageID;
                    DELETE FROM EventPackages WHERE PackageID = @PackageID;";
                var rows = await _db.ExecuteNonQueryAsync(query, new SqlParameter("@PackageID", packageId));
                return rows > 0;
            }
            catch
            {
                var fallback = "UPDATE EventPackages SET IsActive = 0 WHERE PackageID = @PackageID";
                var rows = await _db.ExecuteNonQueryAsync(fallback, new SqlParameter("@PackageID", packageId));
                return rows > 0;
            }
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

        public async Task<bool> CancelOrRemoveEventFromBookingAsync(int bookingId)
        {
            const string query = @"
                UPDATE Bookings 
                SET IsEventBooking = 0, 
                    SetupStatus = 'Cancelled', 
                    DecoratorAssignedID = NULL 
                WHERE BookingID = @BookingID";

            var rows = await _db.ExecuteNonQueryAsync(query, new SqlParameter("@BookingID", bookingId));
            return rows > 0;
        }

        public async Task<List<User>> GetDecoratorsAsync(bool onlyVerified = false)
        {
            var query = "SELECT * FROM Users WHERE Role = 'EventManager' AND IsActive = 1 " +
                        (onlyVerified ? " AND VerificationStatus = 'Verified' " : "") +
                        " ORDER BY FullName";
            return await _db.ExecuteReaderListAsync(query, MapUser);
        }

        public async Task<HashSet<int>> GetUserIdsWithApprovedPortfoliosAsync()
        {
            const string sql = "SELECT DISTINCT UserID FROM EventManagerPortfolios WHERE IsApproved = 1";
            var list = await _db.ExecuteReaderListAsync(sql, r => r.GetInt32(0));
            return new HashSet<int>(list);
        }

        private static User MapUser(SqlDataReader reader)
        {
            var u = new User
            {
                UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                FullName = reader.GetString(reader.GetOrdinal("FullName")),
                Email = reader.GetString(reader.GetOrdinal("Email")),
                Phone = reader.IsDBNull(reader.GetOrdinal("Phone")) ? null : reader.GetString(reader.GetOrdinal("Phone")),
                Role = reader.GetString(reader.GetOrdinal("Role")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("IsActive"))
            };

            try {
                int ord = reader.GetOrdinal("VerificationStatus");
                if (!reader.IsDBNull(ord)) u.VerificationStatus = reader.GetString(ord);
            } catch { }

            try {
                int ord = reader.GetOrdinal("ProfilePictureUrl");
                if (!reader.IsDBNull(ord)) u.ProfilePictureUrl = reader.GetString(ord);
            } catch { }

            try {
                int ord = reader.GetOrdinal("ServiceAreas");
                if (!reader.IsDBNull(ord)) u.ServiceAreas = reader.GetString(ord);
            } catch { }

            try {
                int ord = reader.GetOrdinal("Bio");
                if (!reader.IsDBNull(ord)) u.Bio = reader.GetString(ord);
            } catch { }

            try {
                int ord = reader.GetOrdinal("YearsOfExperience");
                if (!reader.IsDBNull(ord)) u.YearsOfExperience = reader.GetInt32(ord);
            } catch { }

            return u;
        }


        public async Task<int> AddPortfolioItemAsync(EventManagerPortfolio item)
        {
            const string query = @"
                INSERT INTO EventManagerPortfolios (UserID, Title, Description, MediaUrl, MediaType, IsApproved, CreatedAt)
                OUTPUT INSERTED.PortfolioID
                VALUES (@UserID, @Title, @Description, @MediaUrl, @MediaType, @IsApproved, GETUTCDATE())";

            var res = await _db.ExecuteScalarAsync(query,
                new SqlParameter("@UserID", item.UserID),
                new SqlParameter("@Title", item.Title),
                new SqlParameter("@Description", (object?)item.Description ?? DBNull.Value),
                new SqlParameter("@MediaUrl", item.MediaUrl),
                new SqlParameter("@MediaType", item.MediaType),
                new SqlParameter("@IsApproved", item.IsApproved));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<List<EventManagerPortfolio>> GetPortfoliosByUserIdAsync(int userId, bool onlyApproved = true)
        {
            var query = @"
                SELECT emp.*, u.FullName AS ManagerName
                FROM EventManagerPortfolios emp
                JOIN Users u ON emp.UserID = u.UserID
                WHERE emp.UserID = @UserID" + (onlyApproved ? " AND emp.IsApproved = 1" : "") + @"
                ORDER BY emp.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(query, MapPortfolio, new SqlParameter("@UserID", userId));
        }

        public async Task<List<EventManagerPortfolio>> GetAllPortfoliosAsync()
        {
            const string query = @"
                SELECT emp.*, u.FullName AS ManagerName
                FROM EventManagerPortfolios emp
                JOIN Users u ON emp.UserID = u.UserID
                ORDER BY emp.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(query, MapPortfolio);
        }

        public async Task<bool> ApprovePortfolioItemAsync(int portfolioId, bool isApproved)
        {
            const string query = @"UPDATE EventManagerPortfolios SET IsApproved = @IsApproved WHERE PortfolioID = @PortfolioID";
            int rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@IsApproved", isApproved),
                new SqlParameter("@PortfolioID", portfolioId));
            return rows > 0;
        }

        public async Task<bool> DeletePortfolioItemAsync(int portfolioId, int userId)
        {
            const string query = @"DELETE FROM EventManagerPortfolios WHERE PortfolioID = @PortfolioID AND (UserID = @UserID OR @UserID = 0)";
            int rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@PortfolioID", portfolioId),
                new SqlParameter("@UserID", userId));
            return rows > 0;
        }

        public async Task<List<FlaggedContactAttempt>> GetFlaggedContactAttemptsAsync()
        {
            const string query = @"
                SELECT f.*, u.FullName AS UserName
                FROM FlaggedContactAttempts f
                JOIN Users u ON f.UserID = u.UserID
                ORDER BY f.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(query, r => new FlaggedContactAttempt
            {
                FlagID = r.GetInt32(r.GetOrdinal("FlagID")),
                UserID = r.GetInt32(r.GetOrdinal("UserID")),
                ContentType = r.GetString(r.GetOrdinal("ContentType")),
                RawContent = r.GetString(r.GetOrdinal("RawContent")),
                FlaggedPatterns = r.GetString(r.GetOrdinal("FlaggedPatterns")),
                Status = r.GetString(r.GetOrdinal("Status")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                UserName = r.GetString(r.GetOrdinal("UserName"))
            });
        }

        public async Task<bool> UpdateFlaggedContactAttemptStatusAsync(int flagId, string status)
        {
            const string query = @"UPDATE FlaggedContactAttempts SET Status = @Status WHERE FlagID = @FlagID";
            int rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@Status", status),
                new SqlParameter("@FlagID", flagId));
            return rows > 0;
        }

        private static EventManagerPortfolio MapPortfolio(SqlDataReader reader)
        {
            var p = new EventManagerPortfolio
            {
                PortfolioID = reader.GetInt32(reader.GetOrdinal("PortfolioID")),
                UserID = reader.GetInt32(reader.GetOrdinal("UserID")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
                MediaUrl = reader.GetString(reader.GetOrdinal("MediaUrl")),
                MediaType = reader.GetString(reader.GetOrdinal("MediaType")),
                IsApproved = reader.GetBoolean(reader.GetOrdinal("IsApproved")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"))
            };

            try
            {
                int ord = reader.GetOrdinal("ManagerName");
                if (!reader.IsDBNull(ord)) p.ManagerName = reader.GetString(ord);
            }
            catch { }

            return p;
        }

        private static EventPackage MapPackage(SqlDataReader reader)
        {
            var pkg = new EventPackage
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

            try
            {
                int commOrd = reader.GetOrdinal("CommissionRate");
                if (!reader.IsDBNull(commOrd)) pkg.CommissionRate = reader.GetDecimal(commOrd);
            }
            catch { }

            return pkg;
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

            try
            {
                int eCommRateOrd = reader.GetOrdinal("EventCommissionRateSnapshot");
                if (!reader.IsDBNull(eCommRateOrd)) b.EventCommissionRateSnapshot = reader.GetDecimal(eCommRateOrd);
            }
            catch { }

            try
            {
                int eCommAmtOrd = reader.GetOrdinal("EventPlatformCommissionAmount");
                if (!reader.IsDBNull(eCommAmtOrd)) b.EventPlatformCommissionAmount = reader.GetDecimal(eCommAmtOrd);
            }
            catch { }

            try
            {
                int eMgrPayoutOrd = reader.GetOrdinal("EventManagerPayoutAmount");
                if (!reader.IsDBNull(eMgrPayoutOrd)) b.EventManagerPayoutAmount = reader.GetDecimal(eMgrPayoutOrd);
            }
            catch { }

            return b;
        }

        public async Task<List<string>> GetAllDistinctPropertyLocationsAsync()
        {
            const string query = @"
                SELECT DISTINCT Location 
                FROM Properties 
                WHERE Location IS NOT NULL 
                  AND LTRIM(RTRIM(Location)) <> ''
                ORDER BY Location ASC";

            return await _db.ExecuteReaderListAsync(query, reader => reader.GetString(0));
        }

        public async Task<string?> GetEventManagerServiceAreasAsync(int eventManagerUserId)
        {
            const string query = "SELECT ServiceAreas FROM Users WHERE UserID = @UserID";
            var result = await _db.ExecuteScalarAsync(query, new SqlParameter("@UserID", eventManagerUserId));
            return result != null && result != DBNull.Value ? result.ToString() : null;
        }

        public async Task<bool> UpdateEventManagerServiceAreasAsync(int eventManagerUserId, string serviceAreas)
        {
            const string query = "UPDATE Users SET ServiceAreas = @ServiceAreas WHERE UserID = @UserID";
            var rows = await _db.ExecuteNonQueryAsync(query,
                new SqlParameter("@ServiceAreas", (object?)serviceAreas ?? DBNull.Value),
                new SqlParameter("@UserID", eventManagerUserId));
            return rows > 0;
        }

        public async Task<bool> IsAreaServicedByAnyEventManagerAsync(string? propertyLocation)
        {
            if (string.IsNullOrWhiteSpace(propertyLocation)) return false;

            const string query = @"
                SELECT ServiceAreas 
                FROM Users 
                WHERE Role = 'EventManager' 
                  AND (IsActive = 1 OR IsActive IS NULL) 
                  AND ServiceAreas IS NOT NULL 
                  AND LTRIM(RTRIM(ServiceAreas)) <> ''";

            var managers = await _db.ExecuteReaderListAsync(query, r => r.GetString(0));
            if (!managers.Any()) return false;

            var cleanPropLoc = propertyLocation.Trim().ToLowerInvariant();

            foreach (var areaStr in managers)
            {
                if (string.IsNullOrWhiteSpace(areaStr)) continue;

                var areas = areaStr.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var a in areas)
                {
                    var cleanA = a.ToLowerInvariant();
                    if (cleanA == cleanPropLoc || cleanPropLoc.Contains(cleanA) || cleanA.Contains(cleanPropLoc))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
