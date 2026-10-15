using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IContactRepository
    {
        Task<int> CreateContactAsync(ContactUs contact);
        Task<List<ContactUs>> GetAllContactsAsync();
        Task<bool> UpdateStatusAsync(int contactId, string status);
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
            const string sql = "SELECT * FROM ContactUs ORDER BY CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, r => new ContactUs
            {
                ContactID = r.GetInt32(r.GetOrdinal("ContactID")),
                Name = r.GetString(r.GetOrdinal("Name")),
                Email = r.GetString(r.GetOrdinal("Email")),
                Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
                Subject = r.IsDBNull(r.GetOrdinal("Subject")) ? null : r.GetString(r.GetOrdinal("Subject")),
                Message = r.GetString(r.GetOrdinal("Message")),
                Status = r.GetString(r.GetOrdinal("Status")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            });
        }

        public async Task<bool> UpdateStatusAsync(int contactId, string status)
        {
            const string sql = "UPDATE ContactUs SET Status = @Status WHERE ContactID = @ContactID";
            var rows = await _db.ExecuteNonQueryAsync(sql,
                new SqlParameter("@Status", status),
                new SqlParameter("@ContactID", contactId));
            return rows > 0;
        }
    }
}
