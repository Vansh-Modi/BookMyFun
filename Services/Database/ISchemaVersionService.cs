using System.Collections.Generic;
using System.Threading.Tasks;

namespace BookMyFun.Services.Database
{
    public class SchemaVersionStatus
    {
        public int CurrentVersion { get; set; } = 0;
        public int RequiredVersion { get; set; } = 6;
        public List<int> MissingVersions { get; set; } = new();
        public bool IsBehind => MissingVersions.Count > 0;
        public string? StatusMessage { get; set; }
    }

    public interface ISchemaVersionService
    {
        Task<SchemaVersionStatus> CheckSchemaStatusAsync();
    }
}
