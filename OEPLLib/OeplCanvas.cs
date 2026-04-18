using QRCoder;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.Common;

namespace OEPLLib;

public sealed class OeplCanvas : IDisposable
{
    private static readonly DrawingOptions PixelPerfectDrawingOptions = new()
    {
        GraphicsOptions = new GraphicsOptions
        {
            Antialias = false,
            AntialiasSubpixelDepth = 1
        }
    };

    private readonly Image<Rgba32> _image;

    public OeplCanvas(OpenEpaperLinkTagType tagType, bool portrait = false, OeplAccentColor accentColor = OeplAccentColor.Red, string background = "white")
        : this(tagType.GetRenderWidth(portrait), tagType.GetRenderHeight(portrait), accentColor, background)
    {
        RotationQuarterTurns = tagType.GetRotationQuarterTurns(portrait);
    }

    public OeplCanvas(int width, int height, OeplAccentColor accentColor = OeplAccentColor.Red, string background = "white")
    {
        Width = width;
        Height = height;
        AccentColor = accentColor;
        _image = new Image<Rgba32>(width, height, OeplColors.Resolve(background, accentColor));
    }

    public int Width { get; }

    public int Height { get; }

    public int RotationQuarterTurns { get; }

    public OeplAccentColor AccentColor { get; }

    public OeplCanvas Clear(string color = "white")
    {
        _image.Mutate(context => context.Clear(OeplColors.Resolve(color, AccentColor)));
        return this;
    }

    public OeplCanvas DrawText(string text, float x, float y, float size, string fontFamily, string color = "black", FontStyle style = FontStyle.Regular, float? maxWidth = null)
    {
        var font = ResolveSystemFont(fontFamily, size, style);
        var options = new RichTextOptions(font)
        {
            Origin = new PointF(x, y),
            WrappingLength = maxWidth ?? -1
        };

        _image.Mutate(context => context.DrawText(
            PixelPerfectDrawingOptions,
            options,
            text,
            new SolidBrush(OeplColors.Resolve(color, AccentColor)),
            null));
        return this;
    }

    public OeplCanvas DrawTextFromFile(string text, float x, float y, float size, string fontPath, string color = "black")
    {
        var collection = new FontCollection();
        var family = collection.Add(fontPath);
        var font = family.CreateFont(size);
        var options = new RichTextOptions(font) { Origin = new PointF(x, y) };
        _image.Mutate(context => context.DrawText(
            PixelPerfectDrawingOptions,
            options,
            text,
            new SolidBrush(OeplColors.Resolve(color, AccentColor)),
            null));
        return this;
    }

    public OeplCanvas DrawLine(float x1, float y1, float x2, float y2, string color = "black", float width = 1)
    {
        _image.Mutate(context => context.DrawLine(
            PixelPerfectDrawingOptions,
            OeplColors.Resolve(color, AccentColor),
            width,
            new PointF(x1, y1),
            new PointF(x2, y2)));
        return this;
    }

    public OeplCanvas DrawRectangle(float x, float y, float width, float height, string? fill = null, string outline = "black", float outlineWidth = 1)
    {
        var rect = new RectangularPolygon(x, y, width, height);
        _image.Mutate(context =>
        {
            if (!string.IsNullOrWhiteSpace(fill))
            {
                context.Fill(PixelPerfectDrawingOptions, OeplColors.Resolve(fill, AccentColor), rect);
            }

            context.Draw(PixelPerfectDrawingOptions, OeplColors.Resolve(outline, AccentColor), outlineWidth, rect);
        });
        return this;
    }

    public OeplCanvas DrawRoundedRectangle(float x, float y, float width, float height, float radius, string? fill = null, string outline = "black", float outlineWidth = 1)
    {
        var rounded = BuildRoundedRectangle(x, y, width, height, radius);
        _image.Mutate(context =>
        {
            if (!string.IsNullOrWhiteSpace(fill))
            {
                context.Fill(PixelPerfectDrawingOptions, OeplColors.Resolve(fill, AccentColor), rounded);
            }

            context.Draw(PixelPerfectDrawingOptions, OeplColors.Resolve(outline, AccentColor), outlineWidth, rounded);
        });
        return this;
    }

    public OeplCanvas DrawCircle(float x, float y, float radius, string? fill = null, string outline = "black", float outlineWidth = 1)
    {
        var circle = new EllipsePolygon(x, y, radius);
        _image.Mutate(context =>
        {
            if (!string.IsNullOrWhiteSpace(fill))
            {
                context.Fill(PixelPerfectDrawingOptions, OeplColors.Resolve(fill, AccentColor), circle);
            }

            context.Draw(PixelPerfectDrawingOptions, OeplColors.Resolve(outline, AccentColor), outlineWidth, circle);
        });
        return this;
    }

    public OeplCanvas DrawEllipse(float x, float y, float width, float height, string? fill = null, string outline = "black", float outlineWidth = 1)
    {
        var ellipse = new EllipsePolygon(x + (width / 2f), y + (height / 2f), width / 2f, height / 2f);
        _image.Mutate(context =>
        {
            if (!string.IsNullOrWhiteSpace(fill))
            {
                context.Fill(PixelPerfectDrawingOptions, OeplColors.Resolve(fill, AccentColor), ellipse);
            }

            context.Draw(PixelPerfectDrawingOptions, OeplColors.Resolve(outline, AccentColor), outlineWidth, ellipse);
        });
        return this;
    }

    public OeplCanvas DrawPolygon(IEnumerable<PointF> points, string? fill = null, string outline = "black", float outlineWidth = 1)
    {
        var polygon = new Polygon(new LinearLineSegment(points.ToArray()));
        _image.Mutate(context =>
        {
            if (!string.IsNullOrWhiteSpace(fill))
            {
                context.Fill(PixelPerfectDrawingOptions, OeplColors.Resolve(fill, AccentColor), polygon);
            }

            context.Draw(PixelPerfectDrawingOptions, OeplColors.Resolve(outline, AccentColor), outlineWidth, polygon);
        });
        return this;
    }

    public OeplCanvas DrawImage(string filePath, float x, float y, int? width = null, int? height = null)
    {
        using var image = Image.Load<Rgba32>(filePath);
        return DrawImage(image, x, y, width, height);
    }

    public OeplCanvas DrawBarcode(string value, float x, float y, int width, int height, OeplBarcodeType barcodeType = OeplBarcodeType.Code128)
    {
        if (barcodeType == OeplBarcodeType.QrCode)
        {
            return DrawQrCode(value, x, y, width, height);
        }

        var writer = new BarcodeWriterPixelData
        {
            Format = barcodeType switch
            {
                OeplBarcodeType.Code39 => BarcodeFormat.CODE_39,
                OeplBarcodeType.Ean13 => BarcodeFormat.EAN_13,
                OeplBarcodeType.Ean8 => BarcodeFormat.EAN_8,
                OeplBarcodeType.UpcA => BarcodeFormat.UPC_A,
                _ => BarcodeFormat.CODE_128
            },
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 0,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(value);
        using var barcode = Image.LoadPixelData<Bgra32>(pixelData.Pixels, pixelData.Width, pixelData.Height);
        using var barcodeRgba = barcode.CloneAs<Rgba32>();
        return DrawImage(barcodeRgba, x, y, width, height);
    }

    public OeplCanvas DrawQrCode(string value, float x, float y, int width, int height, int pixelsPerModule = 20)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var pngBytes = new PngByteQRCode(data).GetGraphic(pixelsPerModule, [0, 0, 0], [255, 255, 255], true);
        using var qrImage = Image.Load<Rgba32>(pngBytes);
        return DrawImage(qrImage, x, y, width, height);
    }

    public OeplCanvas QuantizeToDisplayPalette()
    {
        _image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = OeplColors.FindClosestDisplayColor(row[x], AccentColor);
                }
            }
        });

        return this;
    }

    public byte[] ToJpegBytes(int quality = 100)
    {
        using var stream = new MemoryStream();
        using var exportImage = CreateExportImage();
        exportImage.Save(stream, CreateJpegEncoder(quality));
        return stream.ToArray();
    }

    public void SaveJpeg(string filePath, int quality = 100)
    {
        using var exportImage = CreateExportImage();
        exportImage.Save(filePath, CreateJpegEncoder(quality));
    }

    public void SavePng(string filePath)
    {
        using var exportImage = CreateExportImage();
        exportImage.Save(filePath, new PngEncoder());
    }

    public void Dispose() => _image.Dispose();

    private OeplCanvas DrawImage(Image<Rgba32> image, float x, float y, int? width = null, int? height = null)
    {
        using var clone = image.Clone(context =>
        {
            if (width is > 0 || height is > 0)
            {
                context.Resize(new ResizeOptions
                {
                    Size = new Size(width ?? image.Width, height ?? image.Height),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.NearestNeighbor
                });
            }
        });

        _image.Mutate(context => context.DrawImage(clone, new Point((int)x, (int)y), 1f));
        return this;
    }

    private Image<Rgb24> CreateExportImage()
    {
        var exportImage = _image.Clone();
        if (RotationQuarterTurns != 0)
        {
            exportImage.Mutate(context => context.Rotate(ToInverseRotateMode(RotationQuarterTurns)));
        }

        exportImage.Metadata.ExifProfile = null;
        exportImage.Metadata.IccProfile = null;
        exportImage.Metadata.XmpProfile = null;
        exportImage.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = OeplColors.FindClosestDisplayColor(row[x], AccentColor);
                }
            }
        });

        return exportImage.CloneAs<Rgb24>();
    }

    private static RotateMode ToInverseRotateMode(int quarterTurns) => (quarterTurns % 4) switch
    {
        1 => RotateMode.Rotate90,
        2 => RotateMode.Rotate180,
        3 => RotateMode.Rotate270,
        _ => RotateMode.None
    };

    private static JpegEncoder CreateJpegEncoder(int quality) => new()
    {
        Quality = quality,
        ColorType = JpegEncodingColor.YCbCrRatio444,
        Interleaved = true
    };

    private static Font ResolveSystemFont(string familyName, float size, FontStyle style)
    {
        if (!SystemFonts.TryGet(familyName, out var family))
        {
            throw new InvalidOperationException($"System font '{familyName}' was not found.");
        }

        return family.CreateFont(size, style);
    }

    private static IPath BuildRoundedRectangle(float x, float y, float width, float height, float radius)
    {
        var builder = new PathBuilder();
        builder.AddLine(new PointF(x + radius, y), new PointF(x + width - radius, y));
        builder.AddArc(new PointF(x + width - radius, y + radius), radius, radius, 270, 90, 0);
        builder.AddLine(new PointF(x + width, y + radius), new PointF(x + width, y + height - radius));
        builder.AddArc(new PointF(x + width - radius, y + height - radius), radius, radius, 0, 90, 0);
        builder.AddLine(new PointF(x + width - radius, y + height), new PointF(x + radius, y + height));
        builder.AddArc(new PointF(x + radius, y + height - radius), radius, radius, 90, 90, 0);
        builder.AddLine(new PointF(x, y + height - radius), new PointF(x, y + radius));
        builder.AddArc(new PointF(x + radius, y + radius), radius, radius, 180, 90, 0);
        builder.CloseFigure();
        return builder.Build();
    }
}
