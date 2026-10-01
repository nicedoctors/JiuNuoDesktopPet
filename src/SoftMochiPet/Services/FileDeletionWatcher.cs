using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;

namespace SoftMochiPet.Services;

public sealed class FileDeletionWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BitmapSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _displayNameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _cacheWarmupTask;
    private string[] _foldersToWarm = [];
    private volatile bool _disposed;

    public event EventHandler<DeletedItemInfo>? ItemDeleted;

    public void Start(IEnumerable<string> configuredFolders)
    {
        if (_disposed || _watchers.Count > 0)
        {
            return;
        }

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        };
        var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrWhiteSpace(commonDesktop))
        {
            folders.Add(commonDesktop);
        }

        foreach (var folder in configuredFolders.Where(folder => !string.IsNullOrWhiteSpace(folder)))
        {
            folders.Add(Environment.ExpandEnvironmentVariables(folder));
        }

        var availableFolders = folders.Where(Directory.Exists).ToArray();
        _foldersToWarm = availableFolders;
        foreach (var folder in availableFolders)
        {
            AddFolderWatcher(folder);
        }

        AddRecycleBinWatchers();
    }

    public void BeginIconCacheWarmup() => StartIconCacheWarmup(_foldersToWarm);

    private void AddFolderWatcher(string folder)
    {
        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Attributes,
                EnableRaisingEvents = true,
            };
            watcher.Deleted += (_, args) => Publish(args.FullPath, cameFromRecycleBin: false);
            watcher.Created += (_, args) => CacheAfterSettled(args.FullPath);
            watcher.Changed += (_, args) => CacheAfterSettled(args.FullPath);
            watcher.Renamed += (sender, args) =>
            {
                _iconCache.TryRemove(args.OldFullPath, out _);
                _displayNameCache.TryRemove(args.OldFullPath, out _);
                CacheAfterSettled(args.FullPath);
            };
            _watchers.Add(watcher);
            DiagnosticsLog.WriteEvent("FolderWatcherStarted", ("Folder", folder));
        }
        catch (Exception exception)
        {
            // An inaccessible optional folder must not break other reactions.
            DiagnosticsLog.Write("Folder watcher could not be started. Folder=" + folder, exception);
        }
    }

    private void StartIconCacheWarmup(IReadOnlyCollection<string> folders)
    {
        if (folders.Count == 0 || _cacheWarmupTask is not null)
        {
            return;
        }

        DiagnosticsLog.WriteEvent("IconCacheWarmupStarted", ("FolderCount", folders.Count));
        _cacheWarmupTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var scannedEntries = 0;
            try
            {
                foreach (var folder in folders)
                {
                    _cancellation.Token.ThrowIfCancellationRequested();
                    scannedEntries += SeedIconCache(folder, _cancellation.Token);
                }

                DiagnosticsLog.WriteEvent(
                    "IconCacheWarmupCompleted",
                    ("Folders", folders.Count),
                    ("Entries", scannedEntries),
                    ("CachedIcons", _iconCache.Count),
                    ("ElapsedMs", stopwatch.Elapsed.TotalMilliseconds));
            }
            catch (OperationCanceledException)
            {
                // Closing during warm-up should be silent and immediate.
            }
            catch (Exception exception)
            {
                DiagnosticsLog.Write("Icon cache warm-up failed.", exception);
            }
        });
    }

    private int SeedIconCache(string folder, CancellationToken cancellationToken)
    {
        var scannedEntries = 0;
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CacheIcon(path);
                scannedEntries++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A folder can change while its initial icon snapshot is being built.
        }

        return scannedEntries;
    }

    private async void CacheAfterSettled(string path)
    {
        try
        {
            await Task.Delay(140, _cancellation.Token);
            if (_disposed)
            {
                return;
            }

            CacheIcon(path);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Shell icons may not be ready immediately after an item is created.
        }
    }

    private void CacheIcon(string path)
    {
        if (_disposed || _iconCache.Count >= 2048 || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return;
        }

        var icon = ShellIconProvider.GetIcon(path);
        if (!_disposed && icon is not null)
        {
            _iconCache[path] = icon;
        }

        var displayName = ShellIconProvider.GetDisplayName(path);
        if (!_disposed && !string.IsNullOrWhiteSpace(displayName))
        {
            _displayNameCache[path] = displayName;
        }
    }

    private void AddRecycleBinWatchers()
    {
        string? sid;
        try
        {
            sid = WindowsIdentity.GetCurrent().User?.Value;
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sid))
        {
            return;
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            var recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
            if (!Directory.Exists(recyclePath))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(recyclePath, "$I*")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += RecycleMetadataCreated;
                _watchers.Add(watcher);
                DiagnosticsLog.WriteEvent("RecycleWatcherStarted", ("Folder", recyclePath));
            }
            catch (Exception exception)
            {
                // Some removable/encrypted volumes expose a recycle path but deny watching it.
                DiagnosticsLog.Write("Recycle-bin watcher could not be started. Folder=" + recyclePath, exception);
            }
        }
    }

    private async void RecycleMetadataCreated(object sender, FileSystemEventArgs args)
    {
        try
        {
            for (var attempt = 0; attempt < 6 && !_cancellation.IsCancellationRequested; attempt++)
            {
                var originalPath = TryReadRecycleMetadata(args.FullPath);
                if (!string.IsNullOrWhiteSpace(originalPath))
                {
                    Publish(originalPath, cameFromRecycleBin: true);
                    return;
                }

                await Task.Delay(90, _cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // A partially-written recycle metadata file can disappear before it is read.
        }
    }

    private static string? TryReadRecycleMetadata(string metadataPath)
    {
        try
        {
            using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < 26)
            {
                return null;
            }

            using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: false);
            var version = reader.ReadInt64();
            _ = reader.ReadInt64(); // Original file size.
            _ = reader.ReadInt64(); // Deletion FILETIME.

            if (version >= 2 && stream.Length >= 28)
            {
                var characterCount = reader.ReadInt32();
                var availableCharacters = (int)((stream.Length - stream.Position) / 2);
                characterCount = Math.Clamp(characterCount, 0, availableCharacters);
                return Encoding.Unicode.GetString(reader.ReadBytes(characterCount * 2)).TrimEnd('\0');
            }

            return Encoding.Unicode.GetString(reader.ReadBytes((int)(stream.Length - stream.Position))).TrimEnd('\0');
        }
        catch
        {
            return null;
        }
    }

    private void Publish(string originalPath, bool cameFromRecycleBin)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (_recent.TryGetValue(originalPath, out var previous) && now - previous < TimeSpan.FromSeconds(4))
        {
            DiagnosticsLog.WriteEventThrottled(
                "deletion-deduplicated-" + originalPath,
                TimeSpan.FromSeconds(4),
                "DeletionDeduplicated",
                ("Path", originalPath),
                ("Source", cameFromRecycleBin ? "RecycleBin" : "FolderWatcher"));
            return;
        }

        _recent[originalPath] = now;
        if (_recent.Count > 256)
        {
            foreach (var stale in _recent.Where(pair => now - pair.Value > TimeSpan.FromMinutes(2)))
            {
                _recent.TryRemove(stale.Key, out _);
            }
        }

        _displayNameCache.TryRemove(originalPath, out var displayName);
        displayName ??= Path.GetFileName(originalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "神秘文件";
        }

        var iconCapturedBeforeDeletion = _iconCache.TryRemove(originalPath, out var cachedIcon);
        cachedIcon ??= ShellIconProvider.GetIcon(originalPath);
        DiagnosticsLog.WriteEvent(
            "DeletionPublished",
            ("Path", originalPath),
            ("DisplayName", displayName),
            ("Source", cameFromRecycleBin ? "RecycleBin" : "FolderWatcher"),
            ("IconCapturedBeforeDeletion", iconCapturedBeforeDeletion));
        ItemDeleted?.Invoke(this, new DeletedItemInfo(
            originalPath,
            displayName,
            now,
            cameFromRecycleBin,
            iconCapturedBeforeDeletion,
            cachedIcon));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _iconCache.Clear();
        _displayNameCache.Clear();
        _cancellation.Dispose();
    }
}
