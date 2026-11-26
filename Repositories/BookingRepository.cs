using BookMyFun.Data;
using BookMyFun.Models.Configuration;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace BookMyFun.Repositories
{
    public interface IBookingRepository
    {
        Task<Booking?> GetByIdAsync(int bookingID);
        Task<Booking?> GetBookingByIdAsync(int bookingId);
        Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey);
        Task<List<Booking>> GetByClientIdAsync(int clientId);
        Task<List<Booking>> GetByOwnerIdAsync(int ownerId);
        Task<List<Booking>> GetAllBookingsAsync();
        Task<int> CreateBookingAsync(Booking booking);
        Task<bool> UpdateBookingStatusAsync(int bookingId, string status);
        Task<bool> CancelBookingAsync(int bookingId);
        Task<bool> UpdateOwnerPayoutAsync(int bookingId, decimal newOwnerPayout) => Task.FromResult(true);
        Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status);
        Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType);
        Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId)
            => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType);
        Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId, TimeSpan? hourlyCheckInTime, int? hourlyDuration)
            => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType, excludeBookingId);
        Task<int> RecordPaymentAsync(Payment payment);
        Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId);
        Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId);
        Task<int> AddReviewAsync(CustomerReview review);
        Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate);
        Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId) => Task.FromResult(new List<Booking>());
        Task<Booking?> FindActiveHoldAsync(int clientId, int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
            => Task.FromResult<Booking?>(null);
        Task<(bool Available, int BookingId)> CreateHoldAtomicAsync(
            Booking booking,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null)
            => CreateBookingAsync(booking).ContinueWith(t => (t.Result > 0, t.Result));
        Task<int> ExpireStaleHoldsAsync() => Task.FromResult(0);
    }

    public class BookingRepository : IBookingRepository
    {
        private readonly DBHelper _db;
        private readonly BookingTimingOptions _timingOptions;
        private readonly BookingOptions _bookingOptions;

        public BookingRepository(
            DBHelper db,
            IOptions<BookingTimingOptions>? timingOptions = null,
            IOptions<BookingOptions>? bookingOptions = null)
        {
            _db = db;
            _timingOptions = timingOptions?.Value ?? new BookingTimingOptions();
            _bookingOptions = bookingOptions?.Value ?? new BookingOptions();
        }

        public async Task<Booking?> GetByIdAsync(int bookingId)
        {
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       (SELECT TOP 1 StorageUrl FROM PropertyMedia WHERE PropertyID = p.PropertyID AND MediaType = 'photo' ORDER BY IsPrimary DESC, MediaID ASC) AS PropertyImageUrl,
                       u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.BookingID = @BookingID";

            return await _db.ExecuteReaderSingleAsync(sql, MapBooking, new SqlParameter("@BookingID", bookingId));
        }

        public Task<Booking?> GetBookingByIdAsync(int bookingId) => GetByIdAsync(bookingId);

        public async Task<Booking?> GetByIdempotencyKeyAsync(string idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.IdempotencyKey = @IdempotencyKey";

            return await _db.ExecuteReaderSingleAsync(sql, MapBooking, new SqlParameter("@IdempotencyKey", idempotencyKey));
        }

        public async Task<List<Booking>> GetByClientIdAsync(int clientId)
        {
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.ClientID = @ClientID
                  AND (b.Status IN ('Confirmed', 'Completed') OR EXISTS (SELECT 1 FROM Payments pay WHERE pay.BookingID = b.BookingID AND pay.PaymentStatus IN ('Success', 'Completed')))
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking, new SqlParameter("@ClientID", clientId));
        }

        public async Task<List<Booking>> GetByOwnerIdAsync(int ownerId)
        {
            // CRITICAL PRIVACY (Section 2B): Never return guest phone or email to owner API/UI
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, CAST(NULL AS NVARCHAR(100)) AS ClientEmail, CAST(NULL AS NVARCHAR(20)) AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE p.OwnerID = @OwnerID
                  AND (b.Status IN ('Confirmed', 'Completed') OR EXISTS (SELECT 1 FROM Payments pay WHERE pay.BookingID = b.BookingID AND pay.PaymentStatus IN ('Success', 'Completed')))
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<List<Booking>> GetAllBookingsAsync()
        {
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE (b.Status IN ('Confirmed', 'Completed') OR EXISTS (SELECT 1 FROM Payments pay WHERE pay.BookingID = b.BookingID AND pay.PaymentStatus IN ('Success', 'Completed')))
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking);
        }

        public async Task<List<Booking>> GetBookingsByPropertyIdAsync(int propertyId)
        {
            const string sql = @"
                SELECT b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.PropertyID = @PropertyID
                ORDER BY b.CheckInDate ASC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<int> CreateBookingAsync(Booking booking)
        {
            const string sqlWithHoldCol = @"
                INSERT INTO Bookings 
                (PropertyID, ClientID, OccasionID, CheckInDate, CheckOutDate, TotalAmount, Status, BookingType, SlotType, SlotStartTime, SlotEndTime,
                 FreeCancellationWindowHoursSnapshot, CancellationCutoffHoursSnapshot, CancellationFeeTypeSnapshot, CancellationFeeValueSnapshot,
                 CommissionRateSnapshot, PlatformCommissionAmount, OwnerPayoutAmount, SecurityDepositAmount, SecurityDepositStatus, IdempotencyKey,
                 IsEventBooking, EventPackageID, CustomEventNotes, EventDate, DecoratorAssignedID, SetupStatus, SetupProofMediaUrls, EventPackagePriceSnapshot,
                 EventCommissionRateSnapshot, EventPlatformCommissionAmount, EventManagerPayoutAmount, CreatedAt, HoldExpiresAt)
                OUTPUT INSERTED.BookingID
                VALUES 
                (@PropertyID, @ClientID, @OccasionID, @CheckInDate, @CheckOutDate, @TotalAmount, @Status, @BookingType, @SlotType, @SlotStartTime, @SlotEndTime,
                 @FreeCancellationWindowHoursSnapshot, @CancellationCutoffHoursSnapshot, @CancellationFeeTypeSnapshot, @CancellationFeeValueSnapshot,
                 @CommissionRateSnapshot, @PlatformCommissionAmount, @OwnerPayoutAmount, @SecurityDepositAmount, @SecurityDepositStatus, @IdempotencyKey,
                 @IsEventBooking, @EventPackageID, @CustomEventNotes, @EventDate, @DecoratorAssignedID, @SetupStatus, @SetupProofMediaUrls, @EventPackagePriceSnapshot,
                 @EventCommissionRateSnapshot, @EventPlatformCommissionAmount, @EventManagerPayoutAmount, GETUTCDATE(), @HoldExpiresAt)";

            const string sqlLegacy = @"
                INSERT INTO Bookings 
                (PropertyID, ClientID, OccasionID, CheckInDate, CheckOutDate, TotalAmount, Status, BookingType, SlotType, SlotStartTime, SlotEndTime,
                 FreeCancellationWindowHoursSnapshot, CancellationCutoffHoursSnapshot, CancellationFeeTypeSnapshot, CancellationFeeValueSnapshot,
                 CommissionRateSnapshot, PlatformCommissionAmount, OwnerPayoutAmount, SecurityDepositAmount, SecurityDepositStatus, IdempotencyKey,
                 IsEventBooking, EventPackageID, CustomEventNotes, EventDate, DecoratorAssignedID, SetupStatus, SetupProofMediaUrls, EventPackagePriceSnapshot,
                 EventCommissionRateSnapshot, EventPlatformCommissionAmount, EventManagerPayoutAmount, CreatedAt)
                OUTPUT INSERTED.BookingID
                VALUES 
                (@PropertyID, @ClientID, @OccasionID, @CheckInDate, @CheckOutDate, @TotalAmount, @Status, @BookingType, @SlotType, @SlotStartTime, @SlotEndTime,
                 @FreeCancellationWindowHoursSnapshot, @CancellationCutoffHoursSnapshot, @CancellationFeeTypeSnapshot, @CancellationFeeValueSnapshot,
                 @CommissionRateSnapshot, @PlatformCommissionAmount, @OwnerPayoutAmount, @SecurityDepositAmount, @SecurityDepositStatus, @IdempotencyKey,
                 @IsEventBooking, @EventPackageID, @CustomEventNotes, @EventDate, @DecoratorAssignedID, @SetupStatus, @SetupProofMediaUrls, @EventPackagePriceSnapshot,
                 @EventCommissionRateSnapshot, @EventPlatformCommissionAmount, @EventManagerPayoutAmount, GETUTCDATE())";

            var hasColObj = await _db.ExecuteScalarAsync("SELECT COL_LENGTH('dbo.Bookings', 'HoldExpiresAt')");
            bool hasHoldCol = hasColObj != null && hasColObj != DBNull.Value;
            string sql = hasHoldCol ? sqlWithHoldCol : sqlLegacy;

            var holdExpiresAt = booking.HoldExpiresAt;
            if (!holdExpiresAt.HasValue && string.Equals(booking.Status, "Hold", StringComparison.OrdinalIgnoreCase))
            {
                holdExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _bookingOptions.HoldMinutes));
            }

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", booking.PropertyID),
                new SqlParameter("@ClientID", booking.ClientID),
                new SqlParameter("@OccasionID", DBHelper.ToDbValue(booking.OccasionID)),
                new SqlParameter("@CheckInDate", booking.CheckInDate.Date),
                new SqlParameter("@CheckOutDate", booking.CheckOutDate.Date),
                new SqlParameter("@TotalAmount", booking.TotalAmount),
                new SqlParameter("@Status", booking.Status),
                new SqlParameter("@BookingType", booking.BookingType),
                new SqlParameter("@SlotType", DBHelper.ToDbValue(booking.SlotType)),
                new SqlParameter("@SlotStartTime", DBHelper.ToDbValue(booking.SlotStartTime)),
                new SqlParameter("@SlotEndTime", DBHelper.ToDbValue(booking.SlotEndTime)),
                new SqlParameter("@FreeCancellationWindowHoursSnapshot", booking.FreeCancellationWindowHoursSnapshot),
                new SqlParameter("@CancellationCutoffHoursSnapshot", booking.CancellationCutoffHoursSnapshot),
                new SqlParameter("@CancellationFeeTypeSnapshot", booking.CancellationFeeTypeSnapshot),
                new SqlParameter("@CancellationFeeValueSnapshot", booking.CancellationFeeValueSnapshot),
                new SqlParameter("@CommissionRateSnapshot", booking.CommissionRateSnapshot),
                new SqlParameter("@PlatformCommissionAmount", booking.PlatformCommissionAmount),
                new SqlParameter("@OwnerPayoutAmount", booking.OwnerPayoutAmount),
                new SqlParameter("@SecurityDepositAmount", booking.SecurityDepositAmount),
                new SqlParameter("@SecurityDepositStatus", booking.SecurityDepositStatus ?? "Held"),
                new SqlParameter("@IdempotencyKey", DBHelper.ToDbValue(booking.IdempotencyKey)),
                new SqlParameter("@IsEventBooking", booking.IsEventBooking),
                new SqlParameter("@EventPackageID", DBHelper.ToDbValue(booking.EventPackageID)),
                new SqlParameter("@CustomEventNotes", DBHelper.ToDbValue(booking.CustomEventNotes)),
                new SqlParameter("@EventDate", DBHelper.ToDbValue(booking.EventDate?.Date)),
                new SqlParameter("@DecoratorAssignedID", DBHelper.ToDbValue(booking.DecoratorAssignedID)),
                new SqlParameter("@SetupStatus", booking.SetupStatus ?? "Pending"),
                new SqlParameter("@SetupProofMediaUrls", DBHelper.ToDbValue(booking.SetupProofMediaUrls)),
                new SqlParameter("@EventPackagePriceSnapshot", booking.EventPackagePriceSnapshot),
                new SqlParameter("@EventCommissionRateSnapshot", booking.EventCommissionRateSnapshot),
                new SqlParameter("@EventPlatformCommissionAmount", booking.EventPlatformCommissionAmount),
                new SqlParameter("@EventManagerPayoutAmount", booking.EventManagerPayoutAmount),
                new SqlParameter("@HoldExpiresAt", DBHelper.ToDbValue(holdExpiresAt)));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<Booking?> FindActiveHoldAsync(int clientId, int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
        {
            var hasColObj = await _db.ExecuteScalarAsync("SELECT COL_LENGTH('dbo.Bookings', 'HoldExpiresAt')");
            bool hasHoldCol = hasColObj != null && hasColObj != DBNull.Value;
            string expiryClause = hasHoldCol
                ? "(b.HoldExpiresAt IS NOT NULL AND b.HoldExpiresAt > GETUTCDATE())"
                : $"b.CreatedAt >= DATEADD(MINUTE, -{Math.Max(1, _bookingOptions.HoldMinutes)}, GETUTCDATE())";

            string sql = $@"
                SELECT TOP 1 b.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, u.Phone AS ClientPhone, o.OccasionName,
                       ep.Name AS EventPackageName, dec.FullName AS DecoratorName
                FROM Bookings b
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Occasions o ON b.OccasionID = o.OccasionID
                LEFT JOIN EventPackages ep ON b.EventPackageID = ep.PackageID
                LEFT JOIN Users dec ON b.DecoratorAssignedID = dec.UserID
                WHERE b.ClientID = @ClientID
                  AND b.PropertyID = @PropertyID
                  AND b.CheckInDate = @CheckInDate
                  AND b.CheckOutDate = @CheckOutDate
                  AND ISNULL(b.BookingType, 'FullDay') = @BookingType
                  AND (b.SlotType = @SlotType OR (b.SlotType IS NULL AND @SlotType IS NULL))
                  AND b.Status = 'Hold'
                  AND {expiryClause}
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderSingleAsync(sql, MapBooking,
                new SqlParameter("@ClientID", clientId),
                new SqlParameter("@PropertyID", propertyId),
                new SqlParameter("@CheckInDate", checkInDate.Date),
                new SqlParameter("@CheckOutDate", checkOutDate.Date),
                new SqlParameter("@BookingType", string.IsNullOrWhiteSpace(bookingType) ? "FullDay" : bookingType),
                new SqlParameter("@SlotType", DBHelper.ToDbValue(slotType)));
        }

        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string status)
        {
            const string sql = "UPDATE Bookings SET Status = @Status WHERE BookingID = @BookingID";
            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@BookingID", bookingId));
            return rows > 0;
        }

        public async Task<bool> CancelBookingAsync(int bookingId)
        {
            const string sql = "UPDATE Bookings SET Status = 'Cancelled' WHERE BookingID = @BookingID";
            var rows = await _db.ExecuteNonQueryAsync(sql, new SqlParameter("@BookingID", bookingId));
            return rows > 0;
        }

        public async Task<bool> UpdateOwnerPayoutAsync(int bookingId, decimal newOwnerPayout)
        {
            const string sql = "UPDATE Bookings SET OwnerPayoutAmount = @OwnerPayoutAmount WHERE BookingID = @BookingID";
            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@OwnerPayoutAmount", newOwnerPayout),
                new SqlParameter("@BookingID", bookingId));
            return rows > 0;
        }

        public async Task<bool> UpdateSecurityDepositStatusAsync(int bookingId, string status)
        {
            const string sql = "UPDATE Bookings SET SecurityDepositStatus = @Status WHERE BookingID = @BookingID";
            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@BookingID", bookingId));
            return rows > 0;
        }

        public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType)
            => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType, null, null, null);

        public Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId)
            => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType, excludeBookingId, null, null);

        public async Task<bool> IsSlotAvailableAsync(
            int propertyId,
            DateTime checkInDate,
            DateTime checkOutDate,
            string bookingType,
            string? slotType,
            int? excludeBookingId,
            TimeSpan? hourlyCheckInTime,
            int? hourlyDuration)
        {
            if (string.Equals(bookingType, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                var startToCheck = hourlyCheckInTime ?? _timingOptions.CheckInTimeSpan;
                if (!_timingOptions.IsHourlyStartAllowed(startToCheck))
                {
                    return false;
                }
            }

            var (reqStart, reqEnd) = _timingOptions.GetBookingWindow(
                checkInDate, checkOutDate, bookingType, slotType, hourlyCheckInTime, hourlyDuration);

            var hasColObj = await _db.ExecuteScalarAsync("SELECT COL_LENGTH('dbo.Bookings', 'HoldExpiresAt')");
            bool hasHoldCol = hasColObj != null && hasColObj != DBNull.Value;
            string holdClause = hasHoldCol
                ? "(Status = 'Hold' AND HoldExpiresAt IS NOT NULL AND HoldExpiresAt > GETUTCDATE())"
                : $"(Status = 'Hold' AND CreatedAt >= DATEADD(MINUTE, -{Math.Max(1, _bookingOptions.HoldMinutes)}, GETUTCDATE()))";

            string sql = $@"
                SELECT CheckInDate, CheckOutDate, BookingType, SlotType, SlotStartTime, SlotEndTime
                FROM Bookings
                WHERE PropertyID = @PropertyID
                  AND (@ExcludeBookingID IS NULL OR BookingID <> @ExcludeBookingID)
                  AND (
                        Status IN ('Confirmed', 'Paid')
                        OR {holdClause}
                      )
                  AND CheckInDate <= @RangeEnd
                  AND CheckOutDate >= @RangeStart";

            var candidates = await _db.ExecuteReaderListAsync(sql, r => new
            {
                CheckInDate = r.GetDateTime(r.GetOrdinal("CheckInDate")),
                CheckOutDate = r.GetDateTime(r.GetOrdinal("CheckOutDate")),
                BookingType = r.IsDBNull(r.GetOrdinal("BookingType")) ? "FullDay" : r.GetString(r.GetOrdinal("BookingType")),
                SlotType = r.IsDBNull(r.GetOrdinal("SlotType")) ? null : r.GetString(r.GetOrdinal("SlotType")),
                SlotStartTime = r.IsDBNull(r.GetOrdinal("SlotStartTime")) ? (TimeSpan?)null : r.GetTimeSpan(r.GetOrdinal("SlotStartTime")),
                SlotEndTime = r.IsDBNull(r.GetOrdinal("SlotEndTime")) ? (TimeSpan?)null : r.GetTimeSpan(r.GetOrdinal("SlotEndTime"))
            },
            new SqlParameter("@PropertyID", propertyId),
            new SqlParameter("@RangeStart", checkInDate.Date.AddDays(-1)),
            new SqlParameter("@RangeEnd", checkOutDate.Date.AddDays(1)),
            new SqlParameter("@ExcludeBookingID", DBHelper.ToDbValue(excludeBookingId)));

            foreach (var existing in candidates)
            {
                int? existingHours = null;
                if (existing.SlotStartTime.HasValue && existing.SlotEndTime.HasValue)
                {
                    var diff = (existing.SlotEndTime.Value - existing.SlotStartTime.Value).TotalHours;
                    if (diff <= 0) diff += 24;
                    existingHours = Math.Max(1, (int)Math.Ceiling(diff));
                }

                var (exStart, exEnd) = _timingOptions.GetBookingWindow(
                    existing.CheckInDate,
                    existing.CheckOutDate,
                    existing.BookingType,
                    existing.SlotType,
                    existing.SlotStartTime,
                    existingHours);

                if (BookingTimingOptions.Overlaps(reqStart, reqEnd, exStart, exEnd))
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<(bool Available, int BookingId)> CreateHoldAtomicAsync(
            Booking booking,
            TimeSpan? hourlyCheckInTime = null,
            int? hourlyDuration = null)
        {
            if (string.Equals(booking.BookingType, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                var startToCheck = hourlyCheckInTime ?? _timingOptions.CheckInTimeSpan;
                if (!_timingOptions.IsHourlyStartAllowed(startToCheck))
                {
                    return (false, 0);
                }
            }

            var (reqStart, reqEnd) = _timingOptions.GetBookingWindow(
                booking.CheckInDate, booking.CheckOutDate, booking.BookingType, booking.SlotType, hourlyCheckInTime, hourlyDuration);

            var holdExpiresAt = booking.HoldExpiresAt ?? DateTime.UtcNow.AddMinutes(Math.Max(1, _bookingOptions.HoldMinutes));
            booking.HoldExpiresAt = holdExpiresAt;

            using var conn = _db.CreateConnection();
            await conn.OpenAsync();
            using var tx = (SqlTransaction)await conn.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            bool hasHoldCol;
            using (var colCmd = new SqlCommand("SELECT COL_LENGTH('dbo.Bookings', 'HoldExpiresAt')", conn, tx))
            {
                var colObj = await colCmd.ExecuteScalarAsync();
                hasHoldCol = colObj != null && colObj != DBNull.Value;
            }

            string holdClause = hasHoldCol
                ? "(Status = 'Hold' AND HoldExpiresAt IS NOT NULL AND HoldExpiresAt > GETUTCDATE())"
                : $"(Status = 'Hold' AND CreatedAt >= DATEADD(MINUTE, -{Math.Max(1, _bookingOptions.HoldMinutes)}, GETUTCDATE()))";

            string checkSql = $@"
                SELECT CheckInDate, CheckOutDate, BookingType, SlotType, SlotStartTime, SlotEndTime
                FROM Bookings WITH (UPDLOCK, HOLDLOCK)
                WHERE PropertyID = @PropertyID
                  AND (Status IN ('Confirmed', 'Paid') OR {holdClause})
                  AND CheckInDate <= @RangeEnd
                  AND CheckOutDate >= @RangeStart";

            using (var checkCmd = new SqlCommand(checkSql, conn, tx))
            {
                checkCmd.Parameters.AddWithValue("@PropertyID", booking.PropertyID);
                checkCmd.Parameters.AddWithValue("@RangeStart", booking.CheckInDate.Date.AddDays(-1));
                checkCmd.Parameters.AddWithValue("@RangeEnd", booking.CheckOutDate.Date.AddDays(1));

                using var r = await checkCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var exIn = r.GetDateTime(r.GetOrdinal("CheckInDate"));
                    var exOut = r.GetDateTime(r.GetOrdinal("CheckOutDate"));
                    var exType = r.IsDBNull(r.GetOrdinal("BookingType")) ? "FullDay" : r.GetString(r.GetOrdinal("BookingType"));
                    var exSlot = r.IsDBNull(r.GetOrdinal("SlotType")) ? null : r.GetString(r.GetOrdinal("SlotType"));
                    var exStartTs = r.IsDBNull(r.GetOrdinal("SlotStartTime")) ? (TimeSpan?)null : r.GetTimeSpan(r.GetOrdinal("SlotStartTime"));
                    var exEndTs = r.IsDBNull(r.GetOrdinal("SlotEndTime")) ? (TimeSpan?)null : r.GetTimeSpan(r.GetOrdinal("SlotEndTime"));

                    int? existingHours = null;
                    if (exStartTs.HasValue && exEndTs.HasValue)
                    {
                        var diff = (exEndTs.Value - exStartTs.Value).TotalHours;
                        if (diff <= 0) diff += 24;
                        existingHours = Math.Max(1, (int)Math.Ceiling(diff));
                    }

                    var (exStart, exEnd) = _timingOptions.GetBookingWindow(exIn, exOut, exType, exSlot, exStartTs, existingHours);
                    if (BookingTimingOptions.Overlaps(reqStart, reqEnd, exStart, exEnd))
                    {
                        await tx.RollbackAsync();
                        return (false, 0);
                    }
                }
            }

            string insertSql = hasHoldCol
                ? @"INSERT INTO Bookings
                    (PropertyID, ClientID, OccasionID, CheckInDate, CheckOutDate, TotalAmount, Status, BookingType, SlotType, SlotStartTime, SlotEndTime,
                     FreeCancellationWindowHoursSnapshot, CancellationCutoffHoursSnapshot, CancellationFeeTypeSnapshot, CancellationFeeValueSnapshot,
                     CommissionRateSnapshot, PlatformCommissionAmount, OwnerPayoutAmount, SecurityDepositAmount, SecurityDepositStatus, IdempotencyKey,
                     IsEventBooking, EventPackageID, CustomEventNotes, EventDate, DecoratorAssignedID, SetupStatus, SetupProofMediaUrls, EventPackagePriceSnapshot,
                     EventCommissionRateSnapshot, EventPlatformCommissionAmount, EventManagerPayoutAmount, CreatedAt, HoldExpiresAt)
                    OUTPUT INSERTED.BookingID
                    VALUES
                    (@PropertyID, @ClientID, @OccasionID, @CheckInDate, @CheckOutDate, @TotalAmount, @Status, @BookingType, @SlotType, @SlotStartTime, @SlotEndTime,
                     @FreeCancellationWindowHoursSnapshot, @CancellationCutoffHoursSnapshot, @CancellationFeeTypeSnapshot, @CancellationFeeValueSnapshot,
                     @CommissionRateSnapshot, @PlatformCommissionAmount, @OwnerPayoutAmount, @SecurityDepositAmount, @SecurityDepositStatus, @IdempotencyKey,
                     @IsEventBooking, @EventPackageID, @CustomEventNotes, @EventDate, @DecoratorAssignedID, @SetupStatus, @SetupProofMediaUrls, @EventPackagePriceSnapshot,
                     @EventCommissionRateSnapshot, @EventPlatformCommissionAmount, @EventManagerPayoutAmount, GETUTCDATE(), @HoldExpiresAt)"
                : @"INSERT INTO Bookings
                    (PropertyID, ClientID, OccasionID, CheckInDate, CheckOutDate, TotalAmount, Status, BookingType, SlotType, SlotStartTime, SlotEndTime,
                     FreeCancellationWindowHoursSnapshot, CancellationCutoffHoursSnapshot, CancellationFeeTypeSnapshot, CancellationFeeValueSnapshot,
                     CommissionRateSnapshot, PlatformCommissionAmount, OwnerPayoutAmount, SecurityDepositAmount, SecurityDepositStatus, IdempotencyKey,
                     IsEventBooking, EventPackageID, CustomEventNotes, EventDate, DecoratorAssignedID, SetupStatus, SetupProofMediaUrls, EventPackagePriceSnapshot,
                     EventCommissionRateSnapshot, EventPlatformCommissionAmount, EventManagerPayoutAmount, CreatedAt)
                    OUTPUT INSERTED.BookingID
                    VALUES
                    (@PropertyID, @ClientID, @OccasionID, @CheckInDate, @CheckOutDate, @TotalAmount, @Status, @BookingType, @SlotType, @SlotStartTime, @SlotEndTime,
                     @FreeCancellationWindowHoursSnapshot, @CancellationCutoffHoursSnapshot, @CancellationFeeTypeSnapshot, @CancellationFeeValueSnapshot,
                     @CommissionRateSnapshot, @PlatformCommissionAmount, @OwnerPayoutAmount, @SecurityDepositAmount, @SecurityDepositStatus, @IdempotencyKey,
                     @IsEventBooking, @EventPackageID, @CustomEventNotes, @EventDate, @DecoratorAssignedID, @SetupStatus, @SetupProofMediaUrls, @EventPackagePriceSnapshot,
                     @EventCommissionRateSnapshot, @EventPlatformCommissionAmount, @EventManagerPayoutAmount, GETUTCDATE())";

            int newId;
            using (var insertCmd = new SqlCommand(insertSql, conn, tx))
            {
                insertCmd.Parameters.AddWithValue("@PropertyID", booking.PropertyID);
                insertCmd.Parameters.AddWithValue("@ClientID", booking.ClientID);
                insertCmd.Parameters.AddWithValue("@OccasionID", DBHelper.ToDbValue(booking.OccasionID));
                insertCmd.Parameters.AddWithValue("@CheckInDate", booking.CheckInDate.Date);
                insertCmd.Parameters.AddWithValue("@CheckOutDate", booking.CheckOutDate.Date);
                insertCmd.Parameters.AddWithValue("@TotalAmount", booking.TotalAmount);
                insertCmd.Parameters.AddWithValue("@Status", booking.Status);
                insertCmd.Parameters.AddWithValue("@BookingType", booking.BookingType);
                insertCmd.Parameters.AddWithValue("@SlotType", DBHelper.ToDbValue(booking.SlotType));
                insertCmd.Parameters.AddWithValue("@SlotStartTime", DBHelper.ToDbValue(booking.SlotStartTime));
                insertCmd.Parameters.AddWithValue("@SlotEndTime", DBHelper.ToDbValue(booking.SlotEndTime));
                insertCmd.Parameters.AddWithValue("@FreeCancellationWindowHoursSnapshot", booking.FreeCancellationWindowHoursSnapshot);
                insertCmd.Parameters.AddWithValue("@CancellationCutoffHoursSnapshot", booking.CancellationCutoffHoursSnapshot);
                insertCmd.Parameters.AddWithValue("@CancellationFeeTypeSnapshot", booking.CancellationFeeTypeSnapshot);
                insertCmd.Parameters.AddWithValue("@CancellationFeeValueSnapshot", booking.CancellationFeeValueSnapshot);
                insertCmd.Parameters.AddWithValue("@CommissionRateSnapshot", booking.CommissionRateSnapshot);
                insertCmd.Parameters.AddWithValue("@PlatformCommissionAmount", booking.PlatformCommissionAmount);
                insertCmd.Parameters.AddWithValue("@OwnerPayoutAmount", booking.OwnerPayoutAmount);
                insertCmd.Parameters.AddWithValue("@SecurityDepositAmount", booking.SecurityDepositAmount);
                insertCmd.Parameters.AddWithValue("@SecurityDepositStatus", booking.SecurityDepositStatus ?? "Held");
                insertCmd.Parameters.AddWithValue("@IdempotencyKey", DBHelper.ToDbValue(booking.IdempotencyKey));
                insertCmd.Parameters.AddWithValue("@IsEventBooking", booking.IsEventBooking);
                insertCmd.Parameters.AddWithValue("@EventPackageID", DBHelper.ToDbValue(booking.EventPackageID));
                insertCmd.Parameters.AddWithValue("@CustomEventNotes", DBHelper.ToDbValue(booking.CustomEventNotes));
                insertCmd.Parameters.AddWithValue("@EventDate", DBHelper.ToDbValue(booking.EventDate?.Date));
                insertCmd.Parameters.AddWithValue("@DecoratorAssignedID", DBHelper.ToDbValue(booking.DecoratorAssignedID));
                insertCmd.Parameters.AddWithValue("@SetupStatus", booking.SetupStatus ?? "Pending");
                insertCmd.Parameters.AddWithValue("@SetupProofMediaUrls", DBHelper.ToDbValue(booking.SetupProofMediaUrls));
                insertCmd.Parameters.AddWithValue("@EventPackagePriceSnapshot", booking.EventPackagePriceSnapshot);
                insertCmd.Parameters.AddWithValue("@EventCommissionRateSnapshot", booking.EventCommissionRateSnapshot);
                insertCmd.Parameters.AddWithValue("@EventPlatformCommissionAmount", booking.EventPlatformCommissionAmount);
                insertCmd.Parameters.AddWithValue("@EventManagerPayoutAmount", booking.EventManagerPayoutAmount);
                if (hasHoldCol)
                {
                    insertCmd.Parameters.AddWithValue("@HoldExpiresAt", DBHelper.ToDbValue(holdExpiresAt));
                }

                var scalar = await insertCmd.ExecuteScalarAsync();
                newId = scalar != null ? Convert.ToInt32(scalar) : 0;
            }

            await tx.CommitAsync();
            return (newId > 0, newId);
        }

        public async Task<int> RecordPaymentAsync(Payment payment)
        {
            const string sql = @"
                INSERT INTO Payments (BookingID, GatewayTransactionID, Amount, PaymentStatus, PaymentDate)
                OUTPUT INSERTED.PaymentID
                VALUES (@BookingID, @GatewayTransactionID, @Amount, @PaymentStatus, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@BookingID", payment.BookingID),
                new SqlParameter("@GatewayTransactionID", DBHelper.ToDbValue(payment.GatewayTransactionID)),
                new SqlParameter("@Amount", payment.Amount),
                new SqlParameter("@PaymentStatus", payment.PaymentStatus));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId)
        {
            const string sql = "SELECT * FROM Payments WHERE BookingID = @BookingID ORDER BY PaymentDate DESC";
            return await _db.ExecuteReaderListAsync(sql, r => new Payment
            {
                PaymentID = r.GetInt32(r.GetOrdinal("PaymentID")),
                BookingID = r.GetInt32(r.GetOrdinal("BookingID")),
                GatewayTransactionID = r.IsDBNull(r.GetOrdinal("GatewayTransactionID")) ? null : r.GetString(r.GetOrdinal("GatewayTransactionID")),
                Amount = r.GetDecimal(r.GetOrdinal("Amount")),
                PaymentStatus = r.GetString(r.GetOrdinal("PaymentStatus")),
                PaymentDate = r.GetDateTime(r.GetOrdinal("PaymentDate"))
            }, new SqlParameter("@BookingID", bookingId));
        }

        public async Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId)
        {
            const string sql = @"
                SELECT cr.*, u.FullName AS ClientName
                FROM CustomerReviews cr
                JOIN Users u ON cr.ClientID = u.UserID
                WHERE cr.PropertyID = @PropertyID
                ORDER BY cr.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, r => new CustomerReview
            {
                ReviewID = r.GetInt32(r.GetOrdinal("ReviewID")),
                BookingID = r.GetInt32(r.GetOrdinal("BookingID")),
                ClientID = r.GetInt32(r.GetOrdinal("ClientID")),
                PropertyID = r.GetInt32(r.GetOrdinal("PropertyID")),
                Rating = r.GetInt32(r.GetOrdinal("Rating")),
                Comment = r.IsDBNull(r.GetOrdinal("Comment")) ? null : r.GetString(r.GetOrdinal("Comment")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
                ClientName = r.GetString(r.GetOrdinal("ClientName"))
            }, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<int> AddReviewAsync(CustomerReview review)
        {
            const string sql = @"
                INSERT INTO CustomerReviews (BookingID, ClientID, PropertyID, Rating, Comment, CreatedAt)
                OUTPUT INSERTED.ReviewID
                VALUES (@BookingID, @ClientID, @PropertyID, @Rating, @Comment, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@BookingID", review.BookingID),
                new SqlParameter("@ClientID", review.ClientID),
                new SqlParameter("@PropertyID", review.PropertyID),
                new SqlParameter("@Rating", review.Rating),
                new SqlParameter("@Comment", DBHelper.ToDbValue(review.Comment)));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate)
        {
            var booking = await GetByIdAsync(bookingId);
            if (booking == null || !booking.IsEventBooking || booking.EventPackagePriceSnapshot <= 0)
            {
                return false;
            }

            decimal price = booking.EventPackagePriceSnapshot;
            decimal newComm = Math.Round((price * newEventCommissionRate) / 100m, 2);
            decimal newPayout = Math.Max(0, price - newComm);

            const string sql = @"
                UPDATE Bookings
                SET EventCommissionRateSnapshot = @Rate,
                    EventPlatformCommissionAmount = @Comm,
                    EventManagerPayoutAmount = @Payout
                WHERE BookingID = @BookingID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@BookingID", bookingId),
                new SqlParameter("@Rate", newEventCommissionRate),
                new SqlParameter("@Comm", newComm),
                new SqlParameter("@Payout", newPayout));

            return rows > 0;
        }

        private static Booking MapBooking(SqlDataReader r)
        {
            var b = new Booking
            {
                BookingID = r.GetInt32(r.GetOrdinal("BookingID")),
                PropertyID = r.GetInt32(r.GetOrdinal("PropertyID")),
                ClientID = r.GetInt32(r.GetOrdinal("ClientID")),
                OccasionID = r.IsDBNull(r.GetOrdinal("OccasionID")) ? null : r.GetInt32(r.GetOrdinal("OccasionID")),
                CheckInDate = r.GetDateTime(r.GetOrdinal("CheckInDate")),
                CheckOutDate = r.GetDateTime(r.GetOrdinal("CheckOutDate")),
                TotalAmount = r.GetDecimal(r.GetOrdinal("TotalAmount")),
                Status = r.GetString(r.GetOrdinal("Status")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            };

            try
            {
                var holdOrd = r.GetOrdinal("HoldExpiresAt");
                b.HoldExpiresAt = r.IsDBNull(holdOrd) ? null : r.GetDateTime(holdOrd);
            }
            catch { }

            try
            {
                b.BookingType = r.IsDBNull(r.GetOrdinal("BookingType")) ? "FullDay" : r.GetString(r.GetOrdinal("BookingType"));
                b.SlotType = r.IsDBNull(r.GetOrdinal("SlotType")) ? null : r.GetString(r.GetOrdinal("SlotType"));
                b.SlotStartTime = r.IsDBNull(r.GetOrdinal("SlotStartTime")) ? null : r.GetTimeSpan(r.GetOrdinal("SlotStartTime"));
                b.SlotEndTime = r.IsDBNull(r.GetOrdinal("SlotEndTime")) ? null : r.GetTimeSpan(r.GetOrdinal("SlotEndTime"));
            }
            catch { }

            try
            {
                int secDepOrd = r.GetOrdinal("SecurityDepositAmount");
                if (!r.IsDBNull(secDepOrd)) b.SecurityDepositAmount = r.GetDecimal(secDepOrd);
            }
            catch { }

            try
            {
                int secDepStatusOrd = r.GetOrdinal("SecurityDepositStatus");
                if (!r.IsDBNull(secDepStatusOrd)) b.SecurityDepositStatus = r.GetString(secDepStatusOrd);
            }
            catch { }

            try
            {
                int commSnapOrd = r.GetOrdinal("CommissionRateSnapshot");
                if (!r.IsDBNull(commSnapOrd)) b.CommissionRateSnapshot = r.GetDecimal(commSnapOrd);
            }
            catch { }

            try
            {
                int platCommOrd = r.GetOrdinal("PlatformCommissionAmount");
                if (!r.IsDBNull(platCommOrd)) b.PlatformCommissionAmount = r.GetDecimal(platCommOrd);
            }
            catch { }

            try
            {
                int payoutOrd = r.GetOrdinal("OwnerPayoutAmount");
                if (!r.IsDBNull(payoutOrd)) b.OwnerPayoutAmount = r.GetDecimal(payoutOrd);
            }
            catch { }

            try
            {
                int freeWinOrd = r.GetOrdinal("FreeCancellationWindowHoursSnapshot");
                if (!r.IsDBNull(freeWinOrd)) b.FreeCancellationWindowHoursSnapshot = r.GetInt32(freeWinOrd);
            }
            catch { }

            try
            {
                int cutoffOrd = r.GetOrdinal("CancellationCutoffHoursSnapshot");
                if (!r.IsDBNull(cutoffOrd)) b.CancellationCutoffHoursSnapshot = r.GetInt32(cutoffOrd);
            }
            catch { }

            try
            {
                int feeTypeOrd = r.GetOrdinal("CancellationFeeTypeSnapshot");
                if (!r.IsDBNull(feeTypeOrd)) b.CancellationFeeTypeSnapshot = r.GetString(feeTypeOrd);
            }
            catch { }

            try
            {
                int feeValOrd = r.GetOrdinal("CancellationFeeValueSnapshot");
                if (!r.IsDBNull(feeValOrd)) b.CancellationFeeValueSnapshot = r.GetDecimal(feeValOrd);
            }
            catch { }

            try
            {
                int idemOrd = r.GetOrdinal("IdempotencyKey");
                if (!r.IsDBNull(idemOrd)) b.IdempotencyKey = r.GetString(idemOrd);
            }
            catch { }

            try { b.PropertyTitle = r.GetString(r.GetOrdinal("PropertyTitle")); } catch { }
            try { b.ClientName = r.GetString(r.GetOrdinal("ClientName")); } catch { }
            try { b.ClientEmail = r.GetString(r.GetOrdinal("ClientEmail")); } catch { }
            try { b.ClientPhone = r.IsDBNull(r.GetOrdinal("ClientPhone")) ? null : r.GetString(r.GetOrdinal("ClientPhone")); } catch { }
            try { b.OccasionName = r.IsDBNull(r.GetOrdinal("OccasionName")) ? null : r.GetString(r.GetOrdinal("OccasionName")); } catch { }
            try { b.EventPackageName = r.IsDBNull(r.GetOrdinal("EventPackageName")) ? null : r.GetString(r.GetOrdinal("EventPackageName")); } catch { }
            try { b.DecoratorName = r.IsDBNull(r.GetOrdinal("DecoratorName")) ? null : r.GetString(r.GetOrdinal("DecoratorName")); } catch { }

            try
            {
                int isEventOrd = r.GetOrdinal("IsEventBooking");
                if (!r.IsDBNull(isEventOrd)) b.IsEventBooking = r.GetBoolean(isEventOrd);
            }
            catch { }

            try
            {
                int pkgIdOrd = r.GetOrdinal("EventPackageID");
                if (!r.IsDBNull(pkgIdOrd)) b.EventPackageID = r.GetInt32(pkgIdOrd);
            }
            catch { }

            try
            {
                int notesOrd = r.GetOrdinal("CustomEventNotes");
                if (!r.IsDBNull(notesOrd)) b.CustomEventNotes = r.GetString(notesOrd);
            }
            catch { }

            try
            {
                int evtDateOrd = r.GetOrdinal("EventDate");
                if (!r.IsDBNull(evtDateOrd)) b.EventDate = r.GetDateTime(evtDateOrd);
            }
            catch { }

            try
            {
                int decOrd = r.GetOrdinal("DecoratorAssignedID");
                if (!r.IsDBNull(decOrd)) b.DecoratorAssignedID = r.GetInt32(decOrd);
            }
            catch { }

            try
            {
                int setupOrd = r.GetOrdinal("SetupStatus");
                if (!r.IsDBNull(setupOrd)) b.SetupStatus = r.GetString(setupOrd);
            }
            catch { }

            try
            {
                int proofOrd = r.GetOrdinal("SetupProofMediaUrls");
                if (!r.IsDBNull(proofOrd)) b.SetupProofMediaUrls = r.GetString(proofOrd);
            }
            catch { }

            try
            {
                int pkgPriceOrd = r.GetOrdinal("EventPackagePriceSnapshot");
                if (!r.IsDBNull(pkgPriceOrd)) b.EventPackagePriceSnapshot = r.GetDecimal(pkgPriceOrd);
            }
            catch { }

            try
            {
                int eCommRateOrd = r.GetOrdinal("EventCommissionRateSnapshot");
                if (!r.IsDBNull(eCommRateOrd)) b.EventCommissionRateSnapshot = r.GetDecimal(eCommRateOrd);
            }
            catch { }

            try
            {
                int eCommAmtOrd = r.GetOrdinal("EventPlatformCommissionAmount");
                if (!r.IsDBNull(eCommAmtOrd)) b.EventPlatformCommissionAmount = r.GetDecimal(eCommAmtOrd);
            }
            catch { }

            try
            {
                int eMgrPayoutOrd = r.GetOrdinal("EventManagerPayoutAmount");
                if (!r.IsDBNull(eMgrPayoutOrd)) b.EventManagerPayoutAmount = r.GetDecimal(eMgrPayoutOrd);
            }
            catch { }

            return b;
        }
    }
}
