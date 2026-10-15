using BookMyFun.Data;
using BookMyFun.Models.Entities;
using Microsoft.Data.SqlClient;

namespace BookMyFun.Repositories
{
    public interface IFinancialRepository
    {
        Task<int> AddEntryAsync(FinancialEntry entry);
        Task<List<FinancialEntry>> GetAllEntriesAsync();
        Task<(decimal totalIncome, decimal totalExpense, decimal netProfit)> GetFinancialSummaryAsync();
        Task<List<(string Month, decimal Income, decimal Expense)>> GetMonthlyFinancialTrendsAsync();
    }

    public class FinancialRepository : IFinancialRepository
    {
        private readonly DBHelper _db;

        public FinancialRepository(DBHelper db)
        {
            _db = db;
        }

        public async Task<int> AddEntryAsync(FinancialEntry entry)
        {
            const string sql = @"
                INSERT INTO FinancialLedger (TransactionType, Category, Amount, Title, Details, TransactionDate, LoggedByUserID, CreatedAt)
                OUTPUT INSERTED.LedgerID
                VALUES (@TransactionType, @Category, @Amount, @Title, @Details, @TransactionDate, @LoggedByUserID, GETDATE())";

            var res = await _db.ExecuteScalarAsync(sql,
                new SqlParameter("@TransactionType", entry.TransactionType),
                new SqlParameter("@Category", entry.Category),
                new SqlParameter("@Amount", entry.Amount),
                new SqlParameter("@Title", entry.Title),
                new SqlParameter("@Details", DBHelper.ToDbValue(entry.Details)),
                new SqlParameter("@TransactionDate", entry.TransactionDate.Date),
                new SqlParameter("@LoggedByUserID", DBHelper.ToDbValue(entry.LoggedByUserID)));

            return res != null ? Convert.ToInt32(res) : 0;
        }

        public async Task<List<FinancialEntry>> GetAllEntriesAsync()
        {
            const string sql = "SELECT * FROM FinancialLedger ORDER BY TransactionDate DESC, CreatedAt DESC";
            return await _db.ExecuteReaderListAsync(sql, MapEntry);
        }

        public async Task<(decimal totalIncome, decimal totalExpense, decimal netProfit)> GetFinancialSummaryAsync()
        {
            const string sql = @"
                SELECT 
                    ISNULL(SUM(CASE WHEN TransactionType = 'Income' THEN Amount ELSE 0 END), 0) AS TotalIncome,
                    ISNULL(SUM(CASE WHEN TransactionType = 'Expense' THEN Amount ELSE 0 END), 0) AS TotalExpense
                FROM FinancialLedger";

            decimal inc = 0, exp = 0;
            await _db.ExecuteReaderSingleAsync(sql, r =>
            {
                inc = r.GetDecimal(r.GetOrdinal("TotalIncome"));
                exp = r.GetDecimal(r.GetOrdinal("TotalExpense"));
                return true;
            });

            return (inc, exp, inc - exp);
        }

        public async Task<List<(string Month, decimal Income, decimal Expense)>> GetMonthlyFinancialTrendsAsync()
        {
            const string sql = @"
                SELECT 
                    FORMAT(TransactionDate, 'MMM yyyy') AS MonthLabel,
                    YEAR(TransactionDate) AS Yr,
                    MONTH(TransactionDate) AS Mo,
                    ISNULL(SUM(CASE WHEN TransactionType = 'Income' THEN Amount ELSE 0 END), 0) AS Income,
                    ISNULL(SUM(CASE WHEN TransactionType = 'Expense' THEN Amount ELSE 0 END), 0) AS Expense
                FROM FinancialLedger
                GROUP BY FORMAT(TransactionDate, 'MMM yyyy'), YEAR(TransactionDate), MONTH(TransactionDate)
                ORDER BY Yr ASC, Mo ASC";

            return await _db.ExecuteReaderListAsync(sql, r => (
                r.GetString(r.GetOrdinal("MonthLabel")),
                r.GetDecimal(r.GetOrdinal("Income")),
                r.GetDecimal(r.GetOrdinal("Expense"))
            ));
        }

        private static FinancialEntry MapEntry(SqlDataReader r)
        {
            return new FinancialEntry
            {
                LedgerID = r.GetInt32(r.GetOrdinal("LedgerID")),
                TransactionType = r.GetString(r.GetOrdinal("TransactionType")),
                Category = r.GetString(r.GetOrdinal("Category")),
                Amount = r.GetDecimal(r.GetOrdinal("Amount")),
                Title = r.GetString(r.GetOrdinal("Title")),
                Details = r.IsDBNull(r.GetOrdinal("Details")) ? null : r.GetString(r.GetOrdinal("Details")),
                TransactionDate = r.GetDateTime(r.GetOrdinal("TransactionDate")),
                LoggedByUserID = r.IsDBNull(r.GetOrdinal("LoggedByUserID")) ? null : r.GetInt32(r.GetOrdinal("LoggedByUserID")),
                CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt"))
            };
        }
    }
}
