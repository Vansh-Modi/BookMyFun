using System.Threading.Tasks;
using BookMyFun.Models.ViewModels;

namespace BookMyFun.Services.Staff
{
    public interface IStaffQueueService
    {
        Task<StaffQueueViewModel> GetQueueAsync(string? filter = null, int page = 1, int pageSize = 25);
        Task<int> GetTotalPendingCountAsync();
    }
}
