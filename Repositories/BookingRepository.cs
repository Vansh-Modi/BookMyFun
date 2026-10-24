using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

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
        Task<int> RecordPaymentAsync(Payment payment);
        Task<List<Payment>> GetPaymentsByBookingIdAsync(int bookingId);
        Task<List<CustomerReview>> GetReviewsByPropertyIdAsync(int propertyId);
        Task<int> AddReviewAsync(CustomerReview review);
        Task<bool> UpdateBookingEventCommissionAsync(int bookingId, decimal newEventCommissionRate);
    }

    public class BookingRepository : IBookingRepository
    {
        private readonly DBHelper _db;

        public BookingRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<Booking?> GetByIdAsync(int bookingId)
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
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking, new SqlParameter("@ClientID", clientId));
        }

        public async Task<List<Booking>> GetByOwnerIdAsync(int ownerId)
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
                WHERE p.OwnerID = @OwnerID
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
                ORDER BY b.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, MapBooking);
        }

        public async Task<int> CreateBookingAsync(Booking booking)
        {
            const string sql = @"
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
                 @EventCommissionRateSnapshot, @EventPlatformCommissionAmount, @EventManagerPayoutAmount, GETDATE())";

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
                new SqlParameter("@EventManagerPayoutAmount", booking.EventManagerPayoutAmount));

            return result != null ? Convert.ToInt32(result) : 0;
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
            => IsSlotAvailableAsync(propertyId, checkInDate, checkOutDate, bookingType, slotType, null);

        public async Task<bool> IsSlotAvailableAsync(int propertyId, DateTime checkInDate, DateTime checkOutDate, string bookingType, string? slotType, int? excludeBookingId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM Bookings
                WHERE PropertyID = @PropertyID
                  AND (@ExcludeBookingID IS NULL OR BookingID <> @ExcludeBookingID)
                  AND (
                        Status = 'Confirmed'
                        OR (Status = 'Pending' AND CreatedAt >= DATEADD(MINUTE, -15, GETDATE()))
                      )
                  AND (
                        (CheckInDate <= @CheckOutDate AND CheckOutDate >= @CheckInDate)
                      )
                  AND (
                        -- If any is FullDay, overlap is guaranteed
                        BookingType = 'FullDay' 
                        OR @BookingType = 'FullDay'
                        -- If slot matching
                        OR (SlotType IS NOT NULL AND @SlotType IS NOT NULL AND SlotType = @SlotType)
                      )";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", propertyId),
                new SqlParameter("@CheckInDate", checkInDate.Date),
                new SqlParameter("@CheckOutDate", checkOutDate.Date),
                new SqlParameter("@BookingType", bookingType),
                new SqlParameter("@SlotType", DBHelper.ToDbValue(slotType)),
                new SqlParameter("@ExcludeBookingID", DBHelper.ToDbValue(excludeBookingId)));

            int count = result != null ? Convert.ToInt32(result) : 0;
            return count == 0;
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
