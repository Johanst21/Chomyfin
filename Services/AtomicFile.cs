using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chomyfin.Services
{
    /// <summary>
    /// Provee operaciones de escritura atómica en disco y gestión centralizada de cerrojos por fichero.
    /// </summary>
    internal static class AtomicFile
    {
        internal sealed class LockEntry
        {
            public SemaphoreSlim Sem { get; } = new SemaphoreSlim(1, 1);
            public int RefCount;
        }

        private static readonly ConcurrentDictionary<string, LockEntry> _locks =
            new ConcurrentDictionary<string, LockEntry>(StringComparer.OrdinalIgnoreCase);

        private static int _lockAcquisitions = 0;

        /// <summary>
        /// Adquiere asíncronamente el cerrojo asociado a una ruta de archivo.
        /// Incrementa el contador de referencias antes de esperar y devuelve un IDisposable que libera y decrementa.
        /// </summary>
        public static async Task<IDisposable> AcquireLockAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            var key = Path.GetFullPath(path);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MaybeCleanupLocks();

                var entry = _locks.GetOrAdd(key, _ => new LockEntry());
                Interlocked.Increment(ref entry.RefCount);

                try
                {
                    await entry.Sem.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    Interlocked.Decrement(ref entry.RefCount);
                    throw;
                }

                // Verificar si la entrada registrada sigue siendo exactamente esta instancia
                if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    return new LockReleaser(key, entry);
                }

                // Si fue retirada por MaybeCleanupLocks durante la espera, liberamos y reintentamos con la actual
                entry.Sem.Release();
                Interlocked.Decrement(ref entry.RefCount);
            }
        }

        private sealed class LockReleaser : IDisposable
        {
            private readonly string _key;
            private readonly LockEntry _entry;
            private int _disposed;

            public LockReleaser(string key, LockEntry entry)
            {
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    try
                    {
                        _entry.Sem.Release();
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _entry.RefCount);
                    }
                }
            }
        }

        /// <summary>
        /// Purga periódicamente cerrojos inactivos con RefCount == 0.
        /// </summary>
        internal static void MaybeCleanupLocks(bool force = false)
        {
            if (force || (Interlocked.Increment(ref _lockAcquisitions) % 100 == 0 && _locks.Count > 50))
            {
                foreach (var kvp in _locks)
                {
                    var entry = kvp.Value;
                    if (Volatile.Read(ref entry.RefCount) == 0)
                    {
                        // Solo eliminamos si coincide la misma instancia y su RefCount sigue en 0
                        var col = (System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, LockEntry>>)_locks;
                        col.Remove(kvp);
                    }
                }
            }
        }

        /// <summary>
        /// Obtiene el número actual de entradas en el diccionario de cerrojos (para diagnósticos y pruebas).
        /// </summary>
        internal static int LockCount => _locks.Count;

        /// <summary>
        /// Intenta obtener el RefCount de una clave (para pruebas).
        /// </summary>
        internal static bool TryGetRefCount(string path, out int refCount)
        {
            var key = Path.GetFullPath(path);
            if (_locks.TryGetValue(key, out var entry))
            {
                refCount = Volatile.Read(ref entry.RefCount);
                return true;
            }
            refCount = 0;
            return false;
        }

        /// <summary>
        /// Escribe el contenido en un archivo de forma atómica usando un archivo temporal .tmp.{guid}.
        /// Si el archivo de destino existe, utiliza File.Replace. Si no existe, utiliza File.Move.
        /// </summary>
        public static void WriteAllText(string path, string content, Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            var enc = encoding ?? Encoding.UTF8;
            var fullPath = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = Path.Combine(dir ?? string.Empty, Path.GetFileName(fullPath) + ".tmp." + Guid.NewGuid().ToString("N"));

            try
            {
                File.WriteAllText(tmpPath, content ?? string.Empty, enc);

                if (File.Exists(fullPath))
                {
                    File.Replace(tmpPath, fullPath, null);
                }
                else
                {
                    File.Move(tmpPath, fullPath);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(tmpPath))
                    {
                        File.Delete(tmpPath);
                    }
                }
                catch
                {
                    // Ignore cleanup error on exception
                }
                throw;
            }
        }
    }
}
