using System;
using System.Collections.Generic;

namespace Chomyfin.Models
{
    public enum StrmOverwritePolicy
    {
        SkipIfExists = 0,
        UpdateIfUrlChanged = 1
    }

    public enum StrmSyncMode
    {
        FullAccount = 0,
        SelectedFolders = 1
    }

    public class StrmGeneratorOptions
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string DestinationPath { get; set; }
        public StrmSyncMode SyncMode { get; set; } = StrmSyncMode.FullAccount;
        public List<string> SelectedFolders { get; set; } = new List<string>();
        public List<string> AllowedExtensions { get; set; } = new List<string>
        {
            ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".ts", ".mpg", ".mpeg", ".flv", ".webm"
        };
        public StrmOverwritePolicy OverwritePolicy { get; set; } = StrmOverwritePolicy.SkipIfExists;
        public bool DeleteOrphans { get; set; } = false;
        public bool OrphansDryRun { get; set; } = true;
        public int DelayBetweenRequestsMs { get; set; } = 150;
        public bool EnableSoapDiagnostics { get; set; } = false;
        public string DiagnosticsDirectory { get; set; }
    }

    public class StrmGeneratorResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int FilesDiscovered { get; set; }
        public int FilesCreated { get; set; }
        public int FilesUpdated { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public int OrphansDeleted { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public void AddError(string error)
        {
            if (Errors.Count < 50)
            {
                Errors.Add(error);
            }
        }
    }
}
