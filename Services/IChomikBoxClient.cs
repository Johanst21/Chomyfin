using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chomyfin.Models;

namespace Chomyfin.Services
{
    public interface IChomikBoxClient : IDisposable
    {
        bool IsAuthenticated { get; }
        long HamsterId { get; }
        string Token { get; }

        Task<bool> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
        Task LogoutAsync(CancellationToken cancellationToken = default);
        Task<List<ChomikFolderInfo>> GetSubfoldersAsync(long parentFolderId, string currentPath, CancellationToken cancellationToken = default);
        Task<FolderFilesResult> GetFilesInFolderAsync(string username, long folderId, string currentPath, CancellationToken cancellationToken = default);
    }
}
