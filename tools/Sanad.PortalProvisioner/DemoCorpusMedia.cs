using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Infrastructure.Storage;

namespace Sanad.PortalProvisioner;

/// <summary>
/// Demo media writer. Every key is produced by the real
/// <see cref="LocalDiskFileStorage"/> into the target's configured upload
/// root using the folder the real uploader uses; nothing is invented and
/// no bytes are written directly. The same generated files are reused for
/// every field, and each produced key is re-opened through the same
/// abstraction before it is allowed into a row.
/// <para>
/// It is a top-level type (not a private nested one) so the media workflow can
/// be exercised offline against an ordinary temporary upload root with the same
/// writer the run uses.
/// </para>
/// <para>
/// Reuse rule (hardened after the 2026-10-26 review): the sidecar is scoped
/// to one target root and one set of source files by fingerprints. Under a
/// matching scope the recorded key is the key the demo rows already reference,
/// so it must re-validate through the real storage implementation before it is
/// served, and its length metadata is repopulated for the fresh process. A
/// recorded key that no longer validates — missing, empty, altered bytes,
/// wrong type, or resolving outside the upload root — REFUSES the run: it is
/// never "solved" by a fresh replacement file the rows do not reference while
/// the database keeps pointing at the broken key. A deliberately fresh media
/// establishment is an operator decision: remove the sidecar file.
/// </para>
/// </summary>
public sealed class DemoCorpusMedia
{
    /// <summary>The one public image, reused by every public image field.</summary>
    public const string ImageFileName = "sanad-demo.jpg";

    /// <summary>The one document, reused by every document field.</summary>
    public const string DocumentFileName = "sanad-demo-document.pdf";

    /// <summary>The one private video, reused by every video-capable field.</summary>
    public const string VideoFileName = "sanad-demo.mp4";

    /// <summary>The public writer's single-file ceiling, mirrored from the app.</summary>
    public const long MaxBytes = 5_242_880;

    private readonly LocalDiskFileStorage _storage;
    private readonly string _storageRoot;
    private readonly string _directory;
    private string? _publicImageKey;
    private string? _privateImageKey;
    private string? _documentKey;
    private string? _videoKey;

    // Replays reuse the keys a previous run already produced and that the demo
    // rows already reference. Without this sidecar the per-process cache dies
    // with the process, every replay writes a fresh GUID file the rows never
    // reference, and the target steadily accumulates orphan media. The sidecar
    // is scoped by fingerprints (target root + source media files) so a
    // manifest from another target or other source bytes can never seed a
    // reference here.
    private sealed record ProducedKeysManifest
    {
        public string? StorageRootFingerprint { get; init; }

        public string? MediaSourceFingerprint { get; init; }

        public string? PublicImageKey { get; init; }

        public string? PrivateImageKey { get; init; }

        public string? DocumentKey { get; init; }

        public string? VideoKey { get; init; }
    }

    private const string ManifestFileName = "produced-keys.json";

    private const string KeyPublicImage = "publicImage";
    private const string KeyPrivateImage = "privateImage";
    private const string KeyDocument = "document";
    private const string KeyVideo = "video";

    private readonly string _manifestPath;

    private ProducedKeysManifest? _manifest;
    private string? _storageRootFingerprint;
    private string? _mediaSourceFingerprint;

    /// <summary>The length metadata the demo rows carry for images/documents.</summary>
    public long ImageLength { get; private set; }

    public long DocumentLength { get; private set; }

    /// <summary>
    /// The same real writer every produced key came from, exposed so a
    /// workflow that owns its own media (the subscription-invoice PDF)
    /// persists through the identical implementation into the same root.
    /// </summary>
    public IFileStorage Storage => _storage;

    public DemoCorpusMedia(string storageRoot, string directory)
    {
        _storage = new LocalDiskFileStorage(
            Options.Create(new LocalStorageOptions { RootPath = storageRoot, MaxBytes = MaxBytes }));
        _storageRoot = storageRoot;
        _directory = directory;
        _manifestPath = Path.Combine(directory, ManifestFileName);
    }

    public async Task<string> ImagePublicKeyAsync(string folder)
    {
        if (_publicImageKey is not null)
        {
            return _publicImageKey;
        }

        _publicImageKey = await ReuseOrProduceAsync(
            KeyPublicImage,
            () => SaveAsync(ImageFileName, "image/jpeg", folder, isPrivate: false));
        return _publicImageKey;
    }

    public async Task<string> ImagePrivateKeyAsync(string folder, long maxBytes)
    {
        if (_privateImageKey is not null)
        {
            return _privateImageKey;
        }

        _privateImageKey = await ReuseOrProduceAsync(
            KeyPrivateImage,
            () => SaveAsync(ImageFileName, "image/jpeg", folder, isPrivate: true, maxBytes));
        return _privateImageKey;
    }

    public async Task<string> DocumentKeyAsync(string folder)
    {
        if (_documentKey is not null)
        {
            return _documentKey;
        }

        _documentKey = await ReuseOrProduceAsync(
            KeyDocument,
            () => SaveAsync(DocumentFileName, "application/pdf", folder, isPrivate: true));
        return _documentKey;
    }

    public async Task<string> VideoKeyAsync(string folder)
    {
        if (_videoKey is not null)
        {
            return _videoKey;
        }

        _videoKey = await ReuseOrProduceAsync(
            KeyVideo,
            () => SaveAsync(VideoFileName, "video/mp4", folder, isPrivate: true));
        return _videoKey;
    }

    private async Task<string> ReuseOrProduceAsync(
        string manifestProperty,
        Func<Task<string>> produce)
    {
        ProducedKeysManifest manifest = LoadManifest();
        string? recorded = manifestProperty switch
        {
            KeyPublicImage => manifest.PublicImageKey,
            KeyPrivateImage => manifest.PrivateImageKey,
            KeyDocument => manifest.DocumentKey,
            _ => manifest.VideoKey
        };

        // The sidecar matched this target and these source files, so the
        // recorded key is the key the demo rows already reference: reuse it
        // only after it re-validates through the real storage implementation,
        // repopulating the length metadata this fresh process would otherwise
        // leave at zero. A key that no longer validates fails the run — see
        // AssertReadableAsync for the reasons this must not fall through to a
        // fresh replacement write.
        if (!string.IsNullOrWhiteSpace(recorded))
        {
            long length = await AssertReadableAsync(recorded, SlotContentType(manifestProperty));
            if (manifestProperty is KeyPublicImage or KeyPrivateImage)
            {
                ImageLength = length;
            }
            else if (manifestProperty == KeyDocument)
            {
                DocumentLength = length;
            }

            return recorded;
        }

        string produced = await produce();
        SaveManifest(manifest with
        {
            StorageRootFingerprint = StorageRootFingerprint(),
            MediaSourceFingerprint = MediaSourceFingerprint(),
            PublicImageKey = manifestProperty == KeyPublicImage ? produced : manifest.PublicImageKey,
            PrivateImageKey = manifestProperty == KeyPrivateImage ? produced : manifest.PrivateImageKey,
            DocumentKey = manifestProperty == KeyDocument ? produced : manifest.DocumentKey,
            VideoKey = manifestProperty == KeyVideo ? produced : manifest.VideoKey
        });
        return produced;
    }

    private static string SlotContentType(string manifestProperty) => manifestProperty switch
    {
        KeyPublicImage or KeyPrivateImage => "image/jpeg",
        KeyDocument => "application/pdf",
        _ => "video/mp4"
    };

    /// <summary>The source file the slot's bytes come from, by content type.</summary>
    private static string SourceFileNameFor(string contentType) => contentType switch
    {
        "image/jpeg" => ImageFileName,
        "application/pdf" => DocumentFileName,
        _ => VideoFileName
    };

    private ProducedKeysManifest LoadManifest()
    {
        if (_manifest is not null)
        {
            return _manifest;
        }

        if (File.Exists(_manifestPath))
        {
            try
            {
                _manifest = System.Text.Json.JsonSerializer.Deserialize<ProducedKeysManifest>(
                    File.ReadAllText(_manifestPath));

                if (_manifest is not null && !SidecarScopeMatches(_manifest))
                {
                    // A sidecar from another target root or other source bytes
                    // is ignored, never adopted: its keys do not exist here and
                    // must not be validated against the wrong root.
                    Console.Error.WriteLine(
                        $"demo media sidecar '{ManifestFileName}' was produced for a different target root "
                            + "or different source media; it is ignored and a fresh establishment is planned.");
                    _manifest = new ProducedKeysManifest();
                }

                return _manifest ?? new ProducedKeysManifest();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException
                    or UnauthorizedAccessException)
            {
                // An unreadable manifest is treated as absent: fresh writes only.
                _manifest = new ProducedKeysManifest();
                return _manifest;
            }
        }

        _manifest = new ProducedKeysManifest();
        return _manifest;
    }

    /// <summary>
    /// The sidecar binds to one target (the effective upload root and its
    /// derived private sibling) and one source-media set (the generated
    /// JPEG/PDF/MP4 bytes). A missing source file makes the scope unverifiable,
    /// so the sidecar is ignored — the run refuses on the missing source if it
    /// needs to produce, rather than reusing keys under an unverified identity.
    /// </summary>
    private bool SidecarScopeMatches(ProducedKeysManifest manifest) =>
        string.Equals(manifest.StorageRootFingerprint, StorageRootFingerprint(), StringComparison.Ordinal)
        && string.Equals(manifest.MediaSourceFingerprint, MediaSourceFingerprint(), StringComparison.Ordinal);

    private string StorageRootFingerprint()
    {
        if (_storageRootFingerprint is not null)
        {
            return _storageRootFingerprint;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_storageRoot));
        string privateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root + "-private"));
        _storageRootFingerprint = Sha256Hex(
            Encoding.UTF8.GetBytes($"sanad-demo-media-scope:v1|root:{root}|private:{privateRoot}"));
        return _storageRootFingerprint;
    }

    private string? MediaSourceFingerprint()
    {
        if (_mediaSourceFingerprint is not null)
        {
            return _mediaSourceFingerprint;
        }

        var combined = new MemoryStream();
        using (var writer = new StreamWriter(combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            foreach (string fileName in new[] { ImageFileName, DocumentFileName, VideoFileName })
            {
                string path = Path.Combine(_directory, fileName);
                if (!File.Exists(path))
                {
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(path);
                writer.Write(fileName);
                writer.Write('|');
                writer.Write(bytes.LongLength.ToString(CultureInfo.InvariantCulture));
                writer.Write('|');
                writer.Write(Sha256Hex(bytes));
                writer.Write('\n');
            }
        }

        _mediaSourceFingerprint = Sha256Hex(combined.ToArray());
        return _mediaSourceFingerprint;
    }

    private void SaveManifest(ProducedKeysManifest manifest)
    {
        _manifest = manifest;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            _manifestPath,
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    /// <summary>
    /// The on-disk location the SAME storage implementation resolves a key to
    /// (LocalDiskFileStorage.ResolveFullPath): a public key lives at
    /// root + key; a private key lives under the sibling '-private' root with
    /// its FULL key — the 'private/ ' prefix included — appended, exactly as
    /// the writer composes it. The resolved path is contained canonically: a
    /// key that escapes the root (path traversal, absolute path) is refused
    /// rather than probed on disk. Mirrored here only for validation; every
    /// byte is still written through the real abstraction.
    /// </summary>
    private string ResolveDiskPath(string key)
    {
        string normalized = key.Trim().Replace('\\', '/').Trim('/');
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_storageRoot));
        bool isPrivate = normalized.StartsWith("private/", StringComparison.Ordinal);
        string baseRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(isPrivate ? root + "-private" : root));
        string combined = Path.GetFullPath(
            Path.Combine(baseRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));

        bool contained = combined.StartsWith(
            baseRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!contained && !string.Equals(combined, baseRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"demo media key '{key}' resolves outside the upload root '{baseRoot}'; the sidecar is "
                    + "refused rather than probed on disk.");
        }

        return combined;
    }

    private async Task<string> SaveAsync(
        string fileName,
        string contentType,
        string folder,
        bool isPrivate,
        long maxBytes = MaxBytes)
    {
        string path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"demo media source '{fileName}' is missing from the media directory; generate it first "
                    + "(docs/internal/tools/New-DemoMedia.ps1). The run refuses to seed a reference to a file "
                    + "that does not exist.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException($"demo media source '{fileName}' is empty.");
        }

        using var buffer = new MemoryStream(bytes, writable: false);
        var saved = isPrivate
            ? await _storage.SavePrivateAsync(buffer, contentType, bytes.LongLength, folder, maxBytes)
            : await _storage.SaveAsync(buffer, contentType, bytes.LongLength, folder);

        if (saved.IsFailure)
        {
            throw new InvalidOperationException(
                $"the storage writer refused '{fileName}' for folder '{folder}': {saved.Error.Code}");
        }

        if (contentType == "image/jpeg")
        {
            ImageLength = bytes.LongLength;
        }
        else if (contentType == "application/pdf")
        {
            DocumentLength = bytes.LongLength;
        }

        await AssertReadableAsync(saved.Value.Key, contentType);
        return saved.Value.Key;
    }

    /// <summary>
    /// Full validation of a media key against the REAL storage implementation.
    /// A private key is re-opened through the identical abstraction that wrote
    /// it (the only read the abstraction offers); a public key is confirmed at
    /// the canonical path the same writer composed (root + key, exactly what
    /// UseStaticFiles serves at <c>/files/&lt;key&gt;</c>), because no public
    /// read exists on IFileStorage. In both routes the stored bytes are
    /// compared against the current source media: a missing, empty, altered or
    /// wrong-typed file REFUSES the run with the exact reason. The refused run
    /// never writes a fresh replacement file, because the sidecar's key is the
    /// key the demo rows already reference — a silent replacement would leave
    /// those rows pointing at the broken key while the upload root gains an
    /// orphan write. Repair or deliberately re-establish instead (remove the
    /// sidecar, only as an operator decision).
    /// </summary>
    /// <returns>The validated length of the stored bytes.</returns>
    private async Task<long> AssertReadableAsync(string key, string expectedContentType)
    {
        string sourceFileName = SourceFileNameFor(expectedContentType);
        string sourcePath = Path.Combine(_directory, sourceFileName);
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException(
                $"recorded demo media key '{key}' cannot be validated: source '{sourceFileName}' is missing "
                    + "from the media directory. Refusing the run rather than reusing keys under unverified "
                    + "identity or writing unreferenced replacements.");
        }

        byte[] expectedBytes = await File.ReadAllBytesAsync(sourcePath, CancellationToken.None);
        string expectedSha256 = ExpectedSha256(expectedContentType, expectedBytes);

        if (key.StartsWith("private/", StringComparison.Ordinal))
        {
            var opened = await _storage.OpenReadAsync(key);
            if (opened.IsFailure)
            {
                throw Refuse(key,
                    $"it cannot be opened through the storage abstraction (attempt: {opened.Error.Code})");
            }

            PrivateFileContent content = opened.Value;
            if (!string.Equals(content.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            {
                throw Refuse(key,
                    $"it reports '{content.ContentType}' but '{expectedContentType}' was written");
            }

            // The stream is disposed promptly: an open read handle would block
            // a later repair of the stored file (File.OpenRead shares read
            // access only).
            await using (content.Content)
            {
                using var readback = new MemoryStream();
                await content.Content.CopyToAsync(readback, CancellationToken.None);
                byte[] storedBytes = readback.ToArray();
                if (storedBytes.Length == 0)
                {
                    throw Refuse(key, "the stored file is empty");
                }

                if (!BytesMatchSource(storedBytes, expectedBytes, expectedSha256))
                {
                    throw Refuse(key,
                        $"its stored bytes do not match source '{sourceFileName}' (the file behind the referenced "
                            + "key was replaced, truncated or corrupted)");
                }

                return storedBytes.LongLength;
            }
        }

        string path = ResolveDiskPath(key);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw Refuse(key, $"no file exists at the location the storage writer resolves it to ({path})");
        }

        byte[] publicBytes = await File.ReadAllBytesAsync(path, CancellationToken.None);
        if (publicBytes.Length == 0)
        {
            throw Refuse(key, "the stored file is empty");
        }

        if (!BytesMatchSource(publicBytes, expectedBytes, expectedSha256))
        {
            throw Refuse(key,
                $"its stored bytes do not match source '{sourceFileName}' (the file behind the referenced "
                    + "key was replaced, truncated or corrupted)");
        }

        return publicBytes.LongLength;
    }

    private static string ExpectedSha256(string contentType, byte[] sourceBytes)
    {
        // The source bytes ARE the bytes the writer persisted, so the expected
        // digest is the source digest itself. The parameter exists to keep the
        // call sites explicit about which source guards which key.
        _ = contentType;
        return Sha256Hex(sourceBytes);
    }

    private static bool BytesMatchSource(byte[] storedBytes, byte[] expectedBytes, string expectedSha256) =>
        storedBytes.AsSpan().SequenceEqual(expectedBytes)
        || string.Equals(Sha256Hex(storedBytes), expectedSha256, StringComparison.Ordinal);

    private static InvalidOperationException Refuse(string key, string reason) =>
        new(
            $"recorded demo media key '{key}' failed real-storage validation: {reason}. The run refuses "
                + "instead of writing a fresh replacement the rows do not reference — the sidecar key is the "
                + "key the demo rows point at, so repair the stored file or, as a deliberate operator "
                + $"decision, remove the sidecar '{ManifestFileName}' to re-establish all demo media.");

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
