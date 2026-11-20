using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using BookMyFun.Data;
using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public class PhotoShootRepository : IPhotoShootRepository
    {
        private readonly DBHelper _db;

        public PhotoShootRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> CreateRequestAsync(PhotoShootRequest request)
        {
            const string sql = @"
                INSERT INTO PhotoShootRequests (PropertyID, OwnerID, ShootType, Status, AssignedToUserID, Notes, CreatedAt)
                OUTPUT INSERTED.RequestID
                VALUES (@PropertyID, @OwnerID, @ShootType, @Status, @AssignedToUserID, @Notes, SYSUTCDATETIME());";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", request.PropertyID),
                new SqlParameter("@OwnerID", request.OwnerID),
                new SqlParameter("@ShootType", request.ShootType),
                new SqlParameter("@Status", request.Status ?? "Requested"),
                new SqlParameter("@AssignedToUserID", (object?)request.AssignedToUserID ?? DBNull.Value),
                new SqlParameter("@Notes", (object?)request.Notes ?? DBNull.Value));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<PhotoShootRequest?> GetByIdAsync(int requestId)
        {
            const string sql = @"
                SELECT psr.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       u.FullName AS OwnerName, u.Email AS OwnerEmail, u.Phone AS OwnerPhone,
                       staff.FullName AS AssignedToUserName
                FROM PhotoShootRequests psr
                JOIN Properties p ON psr.PropertyID = p.PropertyID
                JOIN Users u ON psr.OwnerID = u.UserID
                LEFT JOIN Users staff ON psr.AssignedToUserID = staff.UserID
                WHERE psr.RequestID = @RequestID;";

            return await _db.ExecuteReaderSingleAsync(sql, MapRequest, new SqlParameter("@RequestID", requestId));
        }

        public async Task<List<PhotoShootRequest>> GetByOwnerIdAsync(int ownerId)
        {
            const string sql = @"
                SELECT psr.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       u.FullName AS OwnerName, u.Email AS OwnerEmail, u.Phone AS OwnerPhone,
                       staff.FullName AS AssignedToUserName
                FROM PhotoShootRequests psr
                JOIN Properties p ON psr.PropertyID = p.PropertyID
                JOIN Users u ON psr.OwnerID = u.UserID
                LEFT JOIN Users staff ON psr.AssignedToUserID = staff.UserID
                WHERE psr.OwnerID = @OwnerID
                ORDER BY psr.CreatedAt DESC;";

            return await _db.ExecuteReaderListAsync(sql, MapRequest, new SqlParameter("@OwnerID", ownerId));
        }

        public async Task<List<PhotoShootRequest>> GetByPropertyIdAsync(int propertyId)
        {
            const string sql = @"
                SELECT psr.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       u.FullName AS OwnerName, u.Email AS OwnerEmail, u.Phone AS OwnerPhone,
                       staff.FullName AS AssignedToUserName
                FROM PhotoShootRequests psr
                JOIN Properties p ON psr.PropertyID = p.PropertyID
                JOIN Users u ON psr.OwnerID = u.UserID
                LEFT JOIN Users staff ON psr.AssignedToUserID = staff.UserID
                WHERE psr.PropertyID = @PropertyID
                ORDER BY psr.CreatedAt DESC;";

            return await _db.ExecuteReaderListAsync(sql, MapRequest, new SqlParameter("@PropertyID", propertyId));
        }

        public async Task<List<PhotoShootRequest>> GetPendingRequestsAsync()
        {
            const string sql = @"
                SELECT psr.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       u.FullName AS OwnerName, u.Email AS OwnerEmail, u.Phone AS OwnerPhone,
                       staff.FullName AS AssignedToUserName
                FROM PhotoShootRequests psr
                JOIN Properties p ON psr.PropertyID = p.PropertyID
                JOIN Users u ON psr.OwnerID = u.UserID
                LEFT JOIN Users staff ON psr.AssignedToUserID = staff.UserID
                WHERE psr.Status IN ('Requested', 'Scheduled')
                ORDER BY psr.CreatedAt ASC;";

            return await _db.ExecuteReaderListAsync(sql, MapRequest);
        }

        public async Task<List<PhotoShootRequest>> GetAllRequestsAsync()
        {
            const string sql = @"
                SELECT psr.*, p.Title AS PropertyTitle, p.Location AS PropertyLocation,
                       u.FullName AS OwnerName, u.Email AS OwnerEmail, u.Phone AS OwnerPhone,
                       staff.FullName AS AssignedToUserName
                FROM PhotoShootRequests psr
                JOIN Properties p ON psr.PropertyID = p.PropertyID
                JOIN Users u ON psr.OwnerID = u.UserID
                LEFT JOIN Users staff ON psr.AssignedToUserID = staff.UserID
                ORDER BY psr.CreatedAt DESC;";

            return await _db.ExecuteReaderListAsync(sql, MapRequest);
        }

        public async Task<bool> HasOpenRequestAsync(int propertyId, string shootType)
        {
            const string sql = @"
                SELECT TOP 1 1
                FROM PhotoShootRequests
                WHERE PropertyID = @PropertyID 
                  AND ShootType = @ShootType
                  AND Status IN ('Requested', 'Scheduled');";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@PropertyID", propertyId),
                new SqlParameter("@ShootType", shootType));

            return res != null && Convert.ToInt32(res) == 1;
        }

        public async Task<bool> UpdateStatusAsync(int requestId, string status, int? assignedToUserId, string? notes)
        {
            const string sql = @"
                UPDATE PhotoShootRequests
                SET Status = @Status,
                    AssignedToUserID = COALESCE(@AssignedToUserID, AssignedToUserID),
                    Notes = COALESCE(@Notes, Notes),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE RequestID = @RequestID;";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@AssignedToUserID", (object?)assignedToUserId ?? DBNull.Value),
                new SqlParameter("@Notes", (object?)notes ?? DBNull.Value),
                new SqlParameter("@RequestID", requestId));

            return rows > 0;
        }

        private static PhotoShootRequest MapRequest(SqlDataReader r)
        {
            var req = new PhotoShootRequest
            {
                RequestID = r.GetInt32(r.GetOrdinal("RequestID")),
                PropertyID = r.GetInt32(r.GetOrdinal("PropertyID")),
                OwnerID = r.GetInt32(r.GetOrdinal("OwnerID")),
                ShootType = r.GetString(r.GetOrdinal("ShootType")),
                Status = r.GetString(r.GetOrdinal("Status")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            };

            int assignedOrd = r.GetOrdinal("AssignedToUserID");
            if (!r.IsDBNull(assignedOrd)) req.AssignedToUserID = r.GetInt32(assignedOrd);

            int notesOrd = r.GetOrdinal("Notes");
            if (!r.IsDBNull(notesOrd)) req.Notes = r.GetString(notesOrd);

            int updatedOrd = r.GetOrdinal("UpdatedAt");
            if (!r.IsDBNull(updatedOrd)) req.UpdatedAt = r.GetDateTime(updatedOrd);

            try { req.PropertyTitle = r.GetString(r.GetOrdinal("PropertyTitle")); } catch { }
            try { req.PropertyLocation = r.GetString(r.GetOrdinal("PropertyLocation")); } catch { }
            try { req.OwnerName = r.GetString(r.GetOrdinal("OwnerName")); } catch { }
            try { req.OwnerEmail = r.GetString(r.GetOrdinal("OwnerEmail")); } catch { }
            try { req.OwnerPhone = r.GetString(r.GetOrdinal("OwnerPhone")); } catch { }
            try { req.AssignedToUserName = r.GetString(r.GetOrdinal("AssignedToUserName")); } catch { }

            return req;
        }
    }
}
