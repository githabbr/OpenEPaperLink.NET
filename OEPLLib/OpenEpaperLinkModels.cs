using System.Text.Json.Serialization;

namespace OEPLLib;

public sealed record OpenEpaperLinkSystemInfo
{
    [JsonPropertyName("alias")]
    public string? Alias { get; init; }

    [JsonPropertyName("env")]
    public string? Environment { get; init; }

    [JsonPropertyName("buildtime")]
    public string? BuildTimeRaw { get; init; }

    [JsonPropertyName("buildversion")]
    public string? BuildVersion { get; init; }

    [JsonPropertyName("sha")]
    public string? Sha { get; init; }

    [JsonPropertyName("psramsize")]
    public long? PsRamSize { get; init; }

    [JsonPropertyName("flashsize")]
    public long? FlashSize { get; init; }

    [JsonPropertyName("ap_version")]
    public int? AccessPointVersion { get; init; }

    [JsonPropertyName("hasC6")]
    public int? HasC6 { get; init; }

    [JsonPropertyName("hasH2")]
    public int? HasH2 { get; init; }

    [JsonPropertyName("hasTslr")]
    public int? HasTslr { get; init; }
}

public sealed record OpenEpaperLinkAccessPointConfig
{
    [JsonPropertyName("alias")]
    public string? Alias { get; init; }

    [JsonPropertyName("env")]
    public string? Environment { get; init; }

    [JsonPropertyName("channel")]
    public int Channel { get; init; }

    [JsonPropertyName("ble")]
    public int Ble { get; init; }

    [JsonPropertyName("preview")]
    public int Preview { get; init; }

    [JsonPropertyName("showtimestamp")]
    public int ShowTimestamp { get; init; }
}

public sealed record OpenEpaperLinkTag
{
    [JsonPropertyName("mac")]
    public string Mac { get; init; } = string.Empty;

    [JsonPropertyName("alias")]
    public string? Alias { get; init; }

    [JsonPropertyName("hwType")]
    public int HardwareType { get; init; }

    [JsonPropertyName("contentMode")]
    public int ContentMode { get; init; }

    [JsonPropertyName("pending")]
    public int Pending { get; init; }

    [JsonPropertyName("hash")]
    public string? Hash { get; init; }

    [JsonPropertyName("rotate")]
    public int Rotate { get; init; }

    [JsonPropertyName("lut")]
    public int Lut { get; init; }

    [JsonPropertyName("invert")]
    public int Invert { get; init; }

    [JsonPropertyName("modecfgjson")]
    public string? ModeConfigurationJson { get; init; }
}

public sealed record OpenEpaperLinkTagConfiguration(
    string Mac,
    string? Alias,
    int ContentMode,
    string ModeConfigurationJson,
    int Rotate = 0,
    int Lut = 0,
    int Invert = 0);

public sealed record OpenEpaperLinkTagPage
{
    [JsonPropertyName("tags")]
    public IReadOnlyList<OpenEpaperLinkTag> Tags { get; init; } = Array.Empty<OpenEpaperLinkTag>();

    [JsonPropertyName("continu")]
    public int? ContinuationPosition { get; init; }
}

public sealed record OpenEpaperLinkTagType
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("width")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Height { get; init; }

    [JsonPropertyName("bpp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int BitsPerPixel { get; init; }

    public int GetRenderWidth(bool portrait = false) => portrait ? Height : Width;

    public int GetRenderHeight(bool portrait = false) => portrait ? Width : Height;

    public int GetJsonRotation(bool portrait = false) => portrait ? 90 : 0;
}

public sealed record OpenEpaperLinkTagDebugSnapshot(
    string Mac,
    int ContentMode,
    int Pending,
    string? Hash,
    int Rotate,
    int Lut,
    int Invert)
{
    public override string ToString() =>
        $"mac={Mac}, contentMode={ContentMode}, pending={Pending}, hash={Hash ?? "<null>"}, rotate={Rotate}, lut={Lut}, invert={Invert}";
}

public enum OpenEpaperLinkDitherMode
{
    None = 0,
    FloydSteinberg = 1,
    Ordered = 2
}

public sealed record OpenEpaperLinkImageUploadOptions(
    OpenEpaperLinkDitherMode? Dither = null,
    int JpegQuality = 100,
    int? ContentMode = null);

public enum OeplAccentColor
{
    Red,
    Yellow
}

public enum OeplBarcodeType
{
    Code128,
    Code39,
    Ean13,
    Ean8,
    UpcA,
    QrCode
}
