using System.Threading.Tasks;
using BookMyFun.Models.ViewModels;

namespace BookMyFun.Services.Health
{
    public interface IDataHealthCheckService
    {
        Task<DataHealthCheckViewModel> RunDiagnosticsAsync();
    }
}
