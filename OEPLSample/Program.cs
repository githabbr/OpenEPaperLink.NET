using OEPLLib;
using SixLabors.ImageSharp;

const string accessPointAddress = "http://192.168.2.178";
const string schwarz1Alias = "Schwarz1";
const string schwarz2Alias = "Schwarz2";

using var client = new OpenEpaperLinkClient(accessPointAddress);


var schwarz1 = await client.GetTagByAliasAsync(schwarz1Alias);
var schwarz2 = await client.GetTagByAliasAsync(schwarz2Alias);

if (schwarz1 is null || schwarz2 is null)
{
    throw new InvalidOperationException($"Could not resolve both sample tags by alias. Found Schwarz1: {schwarz1 is not null}, Schwarz2: {schwarz2 is not null}.");
}

var schwarz1Type = await client.GetTagTypeAsync(schwarz1.HardwareType)
    ?? throw new InvalidOperationException($"No tag type metadata was found for {schwarz1Alias}.");
var schwarz2Type = await client.GetTagTypeAsync(schwarz2.HardwareType)
    ?? throw new InvalidOperationException($"No tag type metadata was found for {schwarz2Alias}.");

await RunStepAsync("JPEG demo on Schwarz1", () => ShowJpegDemoOnSchwarz1Async(client, schwarz1, schwarz1Type));
Console.WriteLine($"Updated {schwarz1Alias} ({schwarz1.Mac}) with JPEG rendering.");


await RunStepAsync("JSON demo on Schwarz2", () => ShowJsonDemoOnSchwarz2Async(client, schwarz2, schwarz2Type));
Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with native JSON template rendering.");

static async Task RunStepAsync(string name, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"{name} failed: {ex.Message}", ex);
    }
}

static async Task ShowJpegDemoOnSchwarz1Async(OpenEpaperLinkClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    using var canvas = new OeplCanvas(tagType.Width, tagType.Height, OeplAccentColor.Red);

    canvas
        .DrawRoundedRectangle(0, 0, tagType.Width - 1, tagType.Height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
        .DrawRectangle(10, 10, 102, 32, fill: "red", outline: "red", outlineWidth: 1)
        .DrawText("Schwarz1", 61, 17, 18, "Bahnschrift", "white")
        .DrawText("JPEG pipeline", 124, 14, 20, "Segoe UI", "black")
        .DrawText(DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 124, 38, 13, "Segoe UI", "black")
        .DrawLine(10, 56, 286, 56, "black", 2)
        .DrawText("Shapes", 12, 64, 13, "Segoe UI", "black")
        .DrawCircle(40, 102, 18, fill: "red")
        .DrawRectangle(70, 84, 36, 36, fill: "black", outline: "black")
        .DrawPolygon([new PointF(126, 118), new PointF(144, 84), new PointF(162, 118)], fill: "red", outline: "black", outlineWidth: 1)
        .DrawText("Barcode + QR", 180, 64, 13, "Segoe UI", "black")
        .DrawBarcode("SW1-2026-03-19", 178, 82, 108, 26, OeplBarcodeType.Code128)
        .DrawQrCode("http://192.168.2.178", 226, 110, 56, 56)
        .QuantizeToDisplayPalette();

    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
    Directory.CreateDirectory(outputDirectory);
    var jpegPath = Path.Combine(outputDirectory, "schwarz1-upload.jpg");
    var pngPath = Path.Combine(outputDirectory, "schwarz1-preview.png");
    canvas.SaveJpeg(jpegPath, 100);
    canvas.SavePng(pngPath);
    Console.WriteLine($"Saved Schwarz1 JPEG to {jpegPath}");
    Console.WriteLine($"Saved Schwarz1 PNG preview to {pngPath}");

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22));
}

static async Task ShowJsonDemoOnSchwarz2Async(OpenEpaperLinkClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    var document = new JsonTemplateDocument()
        .Add(new JsonRotateCommand(0))
        .Add(new JsonRoundedBoxCommand(0, 0, tagType.Width - 1, tagType.Height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
        .Add(new JsonBoxCommand(12, 12, 68, 28, OeplJsonColor.Red))
        .Add(new JsonTextCommand(46, 30, "S2", "fonts/bahnschrift20", OeplJsonColor.White, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(92, 20, "Schwarz2", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(92, 42, "Native JSON template", "fonts/bahnschrift20", OeplJsonColor.Red))
        .Add(new JsonLineCommand(10, 58, 286, 58, OeplJsonColor.Black))
        .Add(new JsonBoxCommand(12, 72, 90, 28, OeplJsonColor.Red))
        .Add(new JsonTextCommand(57, 90, "JSON", "fonts/bahnschrift20", OeplJsonColor.White, OeplJsonTextAlignment.Center))
        .Add(new JsonTextBoxCommand(112, 72, 168, 34, "AP-drawn text, lines, circles and triangles.", "fonts/bahnschrift20", OeplJsonColor.Black, 1.0f))
        .Add(new JsonCircleCommand(36, 128, 11, OeplJsonColor.Black))
        .Add(new JsonTriangleCommand(62, 138, 74, 116, 86, 138, OeplJsonColor.Red))
        .Add(new JsonLineCommand(104, 118, 280, 118, OeplJsonColor.Black))
        .Add(new JsonTextCommand(104, 130, tag.Mac, "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(238, 22, DateTime.Now.ToString("HH:mm"), "fonts/bahnschrift20", OeplJsonColor.Black, OeplJsonTextAlignment.Center));

    await client.UploadJsonTemplateAsync(tag.Mac, document);
}



