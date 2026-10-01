using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;

internal static class CharacterProductionAssetsTests
{
    public static void EveryCharacterFrameDecodesAtRuntimeSizeWithTransparency()
    {
        foreach (var character in PetCharacterProfile.All)
        {
            var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, character.RuntimeRelativeDirectory);
            var folders = SpriteAnimator.RequiredAssetFoldersFor(character);
            var expectedFolderCount = character.UsesMischief ? 40 : 23;
            Assert(folders.Count == expectedFolderCount && SpriteAnimator.HasCompleteAssets(runtimeDirectory, character),
                $"The production character must contain all {expectedFolderCount} × 16 actual frames: {character.Id}.");
            var decodedCount = 0;
            foreach (var folder in folders)
            {
                for (var index = 0; index < 16; index++)
                {
                    var path = Path.Combine(runtimeDirectory, folder, $"frame_{index:00}.png");
                    var frame = Decode(path).Frames.Single();
                    Assert(frame.PixelWidth == 384 && frame.PixelHeight == 384,
                        $"Every production frame must use the normalized 384 × 384 canvas: {character.Id}/{folder}/{index:00}.");
                    var pixels = ReadBgraPixels(frame);
                    var hasVisiblePixel = false;
                    var hasTransparentPixel = false;
                    var firstVisibleRow = 384;
                    var lastVisibleRow = -1;
                    for (var offset = 3; offset < pixels.Length; offset += 4)
                    {
                        hasVisiblePixel |= pixels[offset] > 0;
                        hasTransparentPixel |= pixels[offset] < 255;
                        if (pixels[offset] > 12)
                        {
                            var row = offset / (384 * 4);
                            firstVisibleRow = Math.Min(firstVisibleRow, row);
                            lastVisibleRow = Math.Max(lastVisibleRow, row);
                        }
                    }

                    Assert(hasVisiblePixel && hasTransparentPixel,
                        $"Every production sprite needs visible character art and a transparent background: {character.Id}/{folder}/{index:00}.");
                    if (folder.StartsWith("pair_", StringComparison.Ordinal) || folder is "pinch" or "pinch_right")
                        Assert(firstVisibleRow >= 8 && lastVisibleRow <= 376,
                            $"Paired-action sprites must keep head and feet inside a safe margin: {character.Id}/{folder}/{index:00}.");
                    decodedCount++;
                }
            }

            Assert(decodedCount == expectedFolderCount * 16,
                $"Every required real production frame must be decoded: {character.Id}, expected {expectedFolderCount * 16}.");
        }
    }

    public static void EveryCharacterIconDecodesFromItsOwnPackage()
    {
        foreach (var character in PetCharacterProfile.All)
        {
            var path = Path.Combine(AppContext.BaseDirectory, character.IconRelativePath);
            var decoder = Decode(path);
            Assert(decoder is IconBitmapDecoder && decoder.Frames.Count > 0,
                $"Each character must package its own readable ICO file: {character.Id}.");
            foreach (var frame in decoder.Frames)
            {
                Assert(frame.PixelWidth > 0 && frame.PixelHeight > 0 && ReadBgraPixels(frame).Length > 0,
                    $"Every ICO size must contain decodable pixels: {character.Id}.");
            }
        }
    }

    public static void EachAnimatorBindsItsOwnProductionIdleArtwork()
    {
        var actualIdlePixels = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var character in PetCharacterProfile.All)
        {
            var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, character.RuntimeRelativeDirectory);
            Assert(SpriteAnimator.HasCompleteAssets(runtimeDirectory, character),
                $"An animator binding test must use the complete real character package: {character.Id}.");
            var animator = new SpriteAnimator(runtimeDirectory, character);
            var expected = Decode(Path.Combine(runtimeDirectory, "idle", "frame_00.png")).Frames.Single();
            var actual = ReadBgraPixels(animator.CurrentFrame);
            Assert(animator.CurrentClip == "idle" && animator.CurrentFrameIndex == 0 &&
                animator.LoadedAssetFolderCount == 1 && actual.SequenceEqual(ReadBgraPixels(expected)),
                $"The first displayed frame must exactly match this character's own idle asset: {character.Id}.");
            actualIdlePixels.Add(character.Id, actual);
        }

        Assert(!actualIdlePixels[PetCharacterProfile.Nuonuo.Id]
                .SequenceEqual(actualIdlePixels[PetCharacterProfile.FeibiJiubi.Id]),
            "Nuonuo and Feibi Jiubi must display different production artwork, never a borrowed idle sprite.");
    }

    public static void FeibiPrankContactsAndHatMasksMatchActualProductionPixels()
    {
        var character = PetCharacterProfile.FeibiJiubi;
        var directory = Path.Combine(AppContext.BaseDirectory, character.RuntimeRelativeDirectory);
        var characterDirectory = Path.GetDirectoryName(directory)!;
        var manifestPath = Path.Combine(characterDirectory, "prank-animations.json");
        Assert(File.Exists(manifestPath),
            "The measured production prank manifest is required; missing artwork cannot be skipped or replaced by fixtures.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;
        Assert(manifest.GetProperty("originalSha256").GetString() ==
            "6b991b530a6c9ab038c762f56a22328d53b8615da1e59f7eace6e3e64139f815",
            "All prank geometry must retain the original Feibi reference lineage.");
        var clipMetadata = manifest.GetProperty("clips");
        Assert(clipMetadata.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(MischiefAnimationCatalog.All.Select(clip => clip.Name)),
            "The production manifest must describe every required prank clip exactly once.");
        var artwork = new PrankArtwork(characterDirectory);
        var contactActions = new HashSet<string> { "kick", "punch", "charge", "bare_kick", "bare_tear" };
        var hatActions = new HashSet<string> { "hat_open", "hat_store", "hat_hold", "hat_return" };
        foreach (var definition in MischiefAnimationCatalog.All)
        {
            var clip = artwork.Clip(definition.Name);
            var metadata = clipMetadata.GetProperty(definition.Name);
            Assert(metadata.GetProperty("frameCount").GetInt32() == 16 &&
                metadata.GetProperty("loop").GetBoolean() == definition.Loop &&
                Math.Abs(clip.FrameSeconds - definition.FrameSeconds) < 0.0000001,
                $"Production previews, metadata and runtime cadence must agree: {definition.Name}.");
            var frameHashes = metadata.GetProperty("runtimeSha256").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            Assert(frameHashes.Length == 16,
                $"Measured geometry must bind to all sixteen exact production frames: {definition.Name}.");

            for (var index = 0; index < 16; index++)
            {
                var framePath = Path.Combine(directory, definition.Name, $"frame_{index:00}.png");
                Assert(HashMatches(framePath, frameHashes[index]),
                    $"The packaged frame no longer matches its measured anchors: {definition.Name}/{index:00}.");
                var frame = Decode(framePath).Frames.Single();
                var pixels = ReadBgraPixels(frame);
                if (contactActions.Contains(definition.Name))
                {
                    Assert(clip.Contact is { Length: 16 } && clip.ContactFrame is >= 0 and < 16,
                        $"Every interaction frame needs measured contact coordinates: {definition.Name}.");
                    var contact = clip.ContactAt(index);
                    Assert(IsCanvasPoint(contact.X, contact.Y) &&
                        HasOpaquePixelNear(frame, pixels, contact.X, contact.Y, 12),
                        $"The contact point must remain on the actual visible character: {definition.Name}/{index:00}.");
                }

                if (hatActions.Contains(definition.Name))
                {
                    Assert(clip.HatMouth is { Length: 16 } && clip.HatWidth is { Length: 16 } &&
                        !string.IsNullOrWhiteSpace(clip.ForegroundMaskFolder),
                        $"Every hat-storage frame needs an opening, width and foreground brim: {definition.Name}.");
                    var mouth = clip.HatMouthAt(index);
                    var width = clip.HatWidth![index];
                    Assert(IsCanvasPoint(mouth.X, mouth.Y) && double.IsFinite(width) && width is >= 5 and <= 512 &&
                        HasOpaquePixelNear(frame, pixels, mouth.X, mouth.Y, (int)Math.Ceiling(width * 0.75 / 2 + 3)),
                        $"A measured hat opening must lie next to the actual hat pixels: {definition.Name}/{index:00}.");
                    var foreground = artwork.Foreground(definition.Name, index);
                    Assert(foreground is not null && foreground.PixelWidth == frame.PixelWidth &&
                        foreground.PixelHeight == frame.PixelHeight,
                        $"Each foreground brim must preserve the sprite canvas: {definition.Name}/{index:00}.");
                    var mask = ReadBgraPixels(foreground!);
                    var sourceVisible = 0;
                    var maskVisible = 0;
                    for (var offset = 0; offset < pixels.Length; offset += 4)
                    {
                        if (pixels[offset + 3] > 0) sourceVisible++;
                        if (mask[offset + 3] == 0) continue;
                        maskVisible++;
                        Assert(mask[offset] == pixels[offset] && mask[offset + 1] == pixels[offset + 1] &&
                            mask[offset + 2] == pixels[offset + 2] && mask[offset + 3] == pixels[offset + 3],
                            $"Hat foreground must be an exact pixel subset of this native frame: {definition.Name}/{index:00}.");
                    }
                    Assert(maskVisible > 0 && maskVisible < sourceVisible,
                        $"The front brim must be nonempty but not the entire character: {definition.Name}/{index:00}.");
                }
            }

            if (contactActions.Contains(definition.Name))
            {
                var markerName = definition.Name == "bare_tear" ? "tear" : "contact";
                Assert(definition.Markers.Single(marker => marker.Name == markerName).FrameIndex == clip.ContactFrame,
                    $"The measured contact must coincide with the runtime one-shot marker: {definition.Name}.");
            }

            if (definition.Name is "hat_throw" or "hat_pickup")
            {
                var expectedTransfer = definition.Name == "hat_throw" ? 7 : 4;
                var transferFrame = metadata.GetProperty("hatTransferFrame").GetInt32();
                var transfer = metadata.GetProperty("hatTransferPoint");
                var x = transfer.GetProperty("x").GetDouble();
                var y = transfer.GetProperty("y").GetDouble();
                var sourceFrame = metadata.GetProperty("hatTransferSourceFrame").GetInt32();
                var expectedSourceFrame = definition.Name == "hat_throw" ? 6 : 4;
                var frame = Decode(Path.Combine(directory, definition.Name, $"frame_{sourceFrame:00}.png")).Frames.Single();
                Assert(transferFrame == expectedTransfer && definition.Markers.Single().FrameIndex == expectedTransfer &&
                    sourceFrame == expectedSourceFrame && double.IsFinite(clip.HatTransferWidth) && clip.HatTransferWidth > 0 &&
                    double.IsFinite(clip.HatTransferRotation) &&
                    IsCanvasPoint(x, y) && HasOpaquePixelNear(frame, ReadBgraPixels(frame), x, y, 12),
                    $"The independent hat must transfer at the measured native hand/hat pixels: {definition.Name}.");
            }
        }

        var hat = artwork.Hat;
        var hatPixels = ReadBgraPixels(hat);
        Assert(hat.PixelWidth == 384 && hat.PixelHeight == 384 &&
            Enumerable.Range(0, hatPixels.Length / 4).Any(index => hatPixels[index * 4 + 3] > 220) &&
            Enumerable.Range(0, hatPixels.Length / 4).Any(index => hatPixels[index * 4 + 3] == 0) &&
            HashMatches(Path.Combine(characterDirectory, "props", "hat.png"), manifest.GetProperty("hatPropSha256").GetString()),
            "The independent hat must be a readable transparent production prop bound to the approved source hash.");
    }

    private static bool IsCanvasPoint(double x, double y) =>
        double.IsFinite(x) && double.IsFinite(y) && x is >= 0 and <= 512 && y is >= 0 and <= 512;

    private static bool HasOpaquePixelNear(BitmapSource frame, byte[] pixels, double canvasX, double canvasY, int radius)
    {
        var x = (int)Math.Round(canvasX * frame.PixelWidth / 512);
        var y = (int)Math.Round(canvasY * frame.PixelHeight / 512);
        for (var row = Math.Max(0, y - radius); row <= Math.Min(frame.PixelHeight - 1, y + radius); row++)
        {
            for (var column = Math.Max(0, x - radius); column <= Math.Min(frame.PixelWidth - 1, x + radius); column++)
            {
                if (pixels[(row * frame.PixelWidth + column) * 4 + 3] > 192) return true;
            }
        }
        return false;
    }

    private static bool HashMatches(string path, string? expected) => File.Exists(path) && expected?.Length == 64 &&
        string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), expected, StringComparison.OrdinalIgnoreCase);

    private static BitmapDecoder Decode(string path)
    {
        Assert(File.Exists(path), $"Required production artwork is missing: {path}.");
        using var stream = File.OpenRead(path);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }

    private static byte[] ReadBgraPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
