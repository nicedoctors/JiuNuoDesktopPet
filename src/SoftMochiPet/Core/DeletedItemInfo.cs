using System.Windows.Media.Imaging;

namespace SoftMochiPet.Core;

public sealed record DeletedItemInfo(
    string OriginalPath,
    string DisplayName,
    DateTimeOffset DetectedAt,
    bool CameFromRecycleBin,
    bool IconCapturedBeforeDeletion = false,
    BitmapSource? CachedIcon = null,
    int? ScreenX = null,
    int? ScreenY = null,
    int? IconPixelWidth = null,
    int? IconPixelHeight = null,
    int? LabelPixelWidth = null,
    int? LabelPixelHeight = null,
    int? LabelOffsetX = null,
    int? LabelOffsetY = null,
    long QueueId = 0,
    string AnchorSource = "Unknown",
    bool ExactDesktopAnchor = false);
