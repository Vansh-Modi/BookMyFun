using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IContactRepository
    {
        Task<int> CreateContactAsync(ContactUs contact);
        Task<List<ContactUs>> GetAllContactsAsync();
        Task<bool> UpdateStatusAsync(int contactId, string status, int? respondedByUserId = null, string? responseNotes = null);
    }

    public class ContactRepository : IContactRepository
    {
        private readonly DBHelper _db;

        public ContactRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> CreateContactAsync(ContactUs contact)
        {
            const string sql = @"
                INSERT INTO ContactUs (Name, Email, Phone, Subject, Message, Status, CreatedAt)
                OUTPUT INSERTED.ContactID
                VALUES (@Name, @Email, @Phone, @Subject, @Message, 'New', GETDATE())";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@Name", contact.Name),
                new SqlParameter("@Email", contact.Email),
                new SqlParameter("@Phone", DBHelper.ToDbValue(contact.Phone)),
                new SqlParameter("@Subject", DBHelper.ToDbValue(contact.Subject)),
                new SqlParameter("@Message", contact.Message));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<List<ContactUs>> GetAllContactsAsync()
        {
            const string sql = @"
                SELECT c.*, u.FullName AS RespondedByUserName, u.Role AS RespondedByUserRole
                FROM ContactUs c
                LEFT JOIN Users u ON c.RespondedByUserID = u.UserID
                ORDER BY c.CreatedAt DESC";

            return await _db.ExecuteReaderListAsync(sql, r =>
            {
                var item = new ContactUs
                {
                    ContactID = r.GetInt32(r.GetOrdinal("ContactID")),
                    Name = r.GetString(r.GetOrdinal("Name")),
                    Email = r.GetString(r.GetOrdinal("Email")),
                    Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
                    Subject = r.IsDBNull(r.GetOrdinal("Subject")) ? null : r.GetString(r.GetOrdinal("Subject")),
                    Message = r.GetString(r.GetOrdinal("Message")),
                    Status = r.GetString(r.GetOrdinal("Status")),
                    CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
                };

                try
                {
                    int respIdOrd = r.GetOrdinal("RespondedByUserID");
                    if (!r.IsDBNull(respIdOrd)) item.RespondedByUserID = r.GetInt32(respIdOrd);
                }
                catch { }

                try
                {
                    int respNameOrd = r.GetOrdinal("RespondedByUserName");
                    if (!r.IsDBNull(respNameOrd)) item.RespondedByUserName = r.GetString(respNameOrd);
                }
                catch { }

                try
                {
                    int respRoleOrd = r.GetOrdinal("RespondedByUserRole");
                    if (!r.IsDBNull(respRoleOrd)) item.RespondedByUserRole = r.GetString(respRoleOrd);
                }
                catch { }

                try
                {
                    int respNotesOrd = r.GetOrdinal("ResponseNotes");
                    if (!r.IsDBNull(respNotesOrd)) item.ResponseNotes = r.GetString(respNotesOrd);
                }
                catch { }

                try
                {
                    int respAtOrd = r.GetOrdinal("RespondedAt");
                    if (!r.IsDBNull(respAtOrd)) item.RespondedAt = r.GetDateTime(respAtOrd);
                }
                catch { }

                return item;
            });
        }

        public async Task<bool> UpdateStatusAsync(int contactId, string status, int? respondedByUserId = null, string? responseNotes = null)
        {
            const string sql = @"
                UPDATE ContactUs 
                SET Status = @Status,
                    RespondedByUserID = COALESCE(@RespondedByUserID, RespondedByUserID),
                    ResponseNotes = COALESCE(@ResponseNotes, ResponseNotes),
                    RespondedAt = CASE WHEN @Status = 'Responded' THEN GETDATE() ELSE RespondedAt END
                WHERE ContactID = @ContactID";

            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@RespondedByUserID", DBHelper.ToDbValue(respondedByUserId)),
                new SqlParameter("@ResponseNotes", DBHelper.ToDbValue(responseNotes)),
                new SqlParameter("@ContactID", contactId));

            return rows > 0;
        }
    }
}
