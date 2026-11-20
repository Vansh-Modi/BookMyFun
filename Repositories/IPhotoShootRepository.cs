using System.Collections.Generic;
using System.Threading.Tasks;
using BookMyFun.Models.Entities;

namespace BookMyFun.Repositories
{
    public interface IPhotoShootRepository
    {
        Task<int> CreateRequestAsync(PhotoShootRequest request);
        Task<PhotoShootRequest?> GetByIdAsync(int requestId);
        Task<List<PhotoShootRequest>> GetByOwnerIdAsync(int ownerId);
        Task<List<PhotoShootRequest>> GetByPropertyIdAsync(int propertyId);
        Task<List<PhotoShootRequest>> GetPendingRequestsAsync();
        Task<List<PhotoShootRequest>> GetAllRequestsAsync();
        Task<bool> HasOpenRequestAsync(int propertyId, string shootType);
        Task<bool> UpdateStatusAsync(int requestId, string status, int? assignedToUserId, string? notes);
    }
}
