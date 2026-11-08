using System;
using System.Collections.Generic;
using System.Globalization;
using Chomyfin.Models;

namespace Chomyfin.Services
{
    /// <summary>
    /// Almacén en memoria seguro entre hilos para consultar el estado de
    /// la última sincronización de archivos .strm y si hay una ejecución en curso.
    /// </summary>
    public class SyncStatusStore
    {
        private readonly object _syncLock = new object();
        private bool _isRunning;

        public bool IsRunning
        {
            get
            {
                lock (_syncLock)
                {
                    return _isRunning;
                }
            }
            private set
            {
                lock (_syncLock)
                {
                    _isRunning = value;
                }
            }
        }
        public DateTime? LastStartTimeUtc { get; private set; }
        public DateTime? LastEndTimeUtc { get; private set; }
        public bool? LastSuccess { get; private set; }
        public string LastMessage { get; private set; }
        public int FilesDiscovered { get; private set; }
        public int FilesCreated { get; private set; }
        public int FilesUpdated { get; private set; }
        public int FilesSkipped { get; private set; }
        public int FilesFailed { get; private set; }
        public int OrphansDeleted { get; private set; }
        public List<string> Errors { get; private set; } = new List<string>();

        public void RecordStart()
        {
            lock (_syncLock)
            {
                IsRunning = true;
                LastStartTimeUtc = DateTime.UtcNow;
                LastMessage = "Sincronización en curso...";
            }
        }

        public void RecordCompletion(StrmGeneratorResult result)
        {
            lock (_syncLock)
            {
                IsRunning = false;
                LastEndTimeUtc = DateTime.UtcNow;
                if (result != null)
                {
                    LastSuccess = result.Success;
                    LastMessage = result.Message;
                    FilesDiscovered = result.FilesDiscovered;
                    FilesCreated = result.FilesCreated;
                    FilesUpdated = result.FilesUpdated;
                    FilesSkipped = result.FilesSkipped;
                    FilesFailed = result.FilesFailed;
                    OrphansDeleted = result.OrphansDeleted;
                    Errors = new List<string>(result.Errors ?? new List<string>());
                }
            }
        }

        internal void ResetForTests()
        {
            lock (_syncLock)
            {
                IsRunning = false;
                LastStartTimeUtc = null;
                LastEndTimeUtc = null;
                LastSuccess = null;
                LastMessage = null;
                FilesDiscovered = 0;
                FilesCreated = 0;
                FilesUpdated = 0;
                FilesSkipped = 0;
                FilesFailed = 0;
                OrphansDeleted = 0;
                Errors.Clear();
            }
        }

        public string GetSummaryString()
        {
            lock (_syncLock)
            {
                if (IsRunning)
                {
                    var duration = LastStartTimeUtc.HasValue ? (DateTime.UtcNow - LastStartTimeUtc.Value).TotalSeconds : 0;
                    return $"Sync in progress (running for {(int)duration}s)...";
                }

                if (!LastStartTimeUtc.HasValue)
                {
                    return "No synchronization has been run yet.";
                }

                var statusText = LastSuccess == true ? "Completed" : "Failed";
                var localTime = (LastEndTimeUtc ?? LastStartTimeUtc.Value).ToLocalTime().ToString("g", CultureInfo.InvariantCulture);

                return $"{statusText} ({localTime}) - Created: {FilesCreated}, Updated: {FilesUpdated}, Skipped: {FilesSkipped}, Errors: {FilesFailed}" +
                       (OrphansDeleted > 0 ? $", Orphans: {OrphansDeleted}" : "");
            }
        }
    }
}
