using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IRefundRepository
    {
        Task<int> InsertRefundAsync(BookingRefund refund);
        Task<BookingRefund?> GetRefundByIdAsync(int refundId);
        Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId);
        Task<List<BookingRefund>> GetRefundsAsync(string? status = null);
        Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes);
    }

    public class RefundRepository : IRefundRepository
    {
        private readonly DBHelper _db;

        public RefundRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> InsertRefundAsync(BookingRefund refund)
        {
            const string sql = @"
                INSERT INTO BookingRefunds 
                (BookingID, GrossBookingAmount, CancellationFeeDeducted, GatewayFeeDeducted, NetRefundAmount, RefundReason, Status, GatewayRefundID, ProcessedByUserID, ProcessedAt, CreatedAt)
                OUTPUT INSERTED.RefundID
                VALUES 
                (@BookingID, @GrossBookingAmount, @CancellationFeeDeducted, @GatewayFeeDeducted, @NetRefundAmount, @RefundReason, @Status, @GatewayRefundID, @ProcessedByUserID, @ProcessedAt, GETDATE())";

            var result = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@BookingID", refund.BookingID),
                new SqlParameter("@GrossBookingAmount", refund.GrossBookingAmount),
                new SqlParameter("@CancellationFeeDeducted", refund.CancellationFeeDeducted),
                new SqlParameter("@GatewayFeeDeducted", refund.GatewayFeeDeducted),
                new SqlParameter("@NetRefundAmount", refund.NetRefundAmount),
                new SqlParameter("@RefundReason", DBHelper.ToDbValue(refund.RefundReason)),
                new SqlParameter("@Status", refund.Status),
                new SqlParameter("@GatewayRefundID", DBHelper.ToDbValue(refund.GatewayRefundID)),
                new SqlParameter("@ProcessedByUserID", DBHelper.ToDbValue(refund.ProcessedByUserID)),
                new SqlParameter("@ProcessedAt", DBHelper.ToDbValue(refund.ProcessedAt)));

            return result != null ? Convert.ToInt32(result) : 0;
        }

        public async Task<BookingRefund?> GetRefundByIdAsync(int refundId)
        {
            const string sql = @"
                SELECT r.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS ProcessedByUserName
                FROM BookingRefunds r
                JOIN Bookings b ON r.BookingID = b.BookingID
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Users staff ON r.ProcessedByUserID = staff.UserID
                WHERE r.RefundID = @RefundID";

            return await _db.ExecuteReaderSingleAsync(sql, MapRefundWithDetails, new SqlParameter("@RefundID", refundId));
        }

        public async Task<BookingRefund?> GetRefundByBookingIdAsync(int bookingId)
        {
            const string sql = @"
                SELECT r.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS ProcessedByUserName
                FROM BookingRefunds r
                JOIN Bookings b ON r.BookingID = b.BookingID
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Users staff ON r.ProcessedByUserID = staff.UserID
                WHERE r.BookingID = @BookingID";

            return await _db.ExecuteReaderSingleAsync(sql, MapRefundWithDetails, new SqlParameter("@BookingID", bookingId));
        }

        public async Task<List<BookingRefund>> GetRefundsAsync(string? status = null)
        {
            string sql = @"
                SELECT r.*, p.Title AS PropertyTitle, u.FullName AS ClientName, u.Email AS ClientEmail, staff.FullName AS ProcessedByUserName
                FROM BookingRefunds r
                JOIN Bookings b ON r.BookingID = b.BookingID
                JOIN Properties p ON b.PropertyID = p.PropertyID
                JOIN Users u ON b.ClientID = u.UserID
                LEFT JOIN Users staff ON r.ProcessedByUserID = staff.UserID";

            if (!string.IsNullOrEmpty(status))
            {
                sql += " WHERE r.Status = @Status ORDER BY r.CreatedAt DESC";
                return await _db.ExecuteReaderListAsync(sql, MapRefundWithDetails, new SqlParameter("@Status", status));
            }

            sql += " ORDER BY r.CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapRefundWithDetails);
        }

        public async Task<bool> UpdateRefundStatusAsync(int refundId, string status, int processedByUserId, string? notes)
        {
            const string sql = @"
                UPDATE BookingRefunds
                SET Status = @Status,
                    ProcessedByUserID = @ProcessedByUserID,
                    ProcessedAt = GETDATE(),
                    RefundReason = CASE WHEN @Notes IS NOT NULL THEN ISNULL(RefundReason, '') + ' [Staff Note: ' + @Notes + ']' ELSE RefundReason END
                WHERE RefundID = @RefundID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@ProcessedByUserID", processedByUserId),
                new SqlParameter("@Notes", DBHelper.ToDbValue(notes)),
                new SqlParameter("@RefundID", refundId));

            return rows > 0;
        }

        private static BookingRefund MapRefundWithDetails(SqlDataReader reader)
        {
            var refund = new BookingRefund
            {
                RefundID = reader.GetInt32(reader.GetOrdinal("RefundID")),
                BookingID = reader.GetInt32(reader.GetOrdinal("BookingID")),
                GrossBookingAmount = reader.GetDecimal(reader.GetOrdinal("GrossBookingAmount")),
                CancellationFeeDeducted = reader.GetDecimal(reader.GetOrdinal("CancellationFeeDeducted")),
                GatewayFeeDeducted = reader.GetDecimal(reader.GetOrdinal("GatewayFeeDeducted")),
                NetRefundAmount = reader.GetDecimal(reader.GetOrdinal("NetRefundAmount")),
                RefundReason = reader.IsDBNull(reader.GetOrdinal("RefundReason")) ? null : reader.GetString(reader.GetOrdinal("RefundReason")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                GatewayRefundID = reader.IsDBNull(reader.GetOrdinal("GatewayRefundID")) ? null : reader.GetString(reader.GetOrdinal("GatewayRefundID")),
                ProcessedByUserID = reader.IsDBNull(reader.GetOrdinal("ProcessedByUserID")) ? null : reader.GetInt32(reader.GetOrdinal("ProcessedByUserID")),
                ProcessedAt = reader.IsDBNull(reader.GetOrdinal("ProcessedAt")) ? null : reader.GetDateTime(reader.GetOrdinal("ProcessedAt")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PropertyTitle = reader.IsDBNull(reader.GetOrdinal("PropertyTitle")) ? null : reader.GetString(reader.GetOrdinal("PropertyTitle")),
                ClientName = reader.IsDBNull(reader.GetOrdinal("ClientName")) ? null : reader.GetString(reader.GetOrdinal("ClientName")),
                ClientEmail = reader.IsDBNull(reader.GetOrdinal("ClientEmail")) ? null : reader.GetString(reader.GetOrdinal("ClientEmail")),
                ProcessedByUserName = reader.IsDBNull(reader.GetOrdinal("ProcessedByUserName")) ? null : reader.GetString(reader.GetOrdinal("ProcessedByUserName"))
            };

            return refund;
        }
    }
}
