using OEPLLib;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

const string accessPointAddress = "http://192.168.2.178";
const string schwarz1Alias = "Schwarz1";
const string schwarz2Alias = "Schwarz2";
const double forecastLatitude = 48.311944;
const double forecastLongitude = 8.917778;
const string forecastLocationName = "Bisingen, DE";

using var client = new OpenEpaperLinkRoamingClient(
[
    new OpenEpaperLinkAccessPointRegistration("ap-1", accessPointAddress)
]);
client.DebugLog = message => Console.WriteLine(message);


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


await RunStepAsync("Portrait JPEG demo on Schwarz1", () => ShowPortraitJpegDemoOnSchwarz1Async(client, schwarz1, schwarz1Type));
Console.WriteLine($"Updated {schwarz1Alias} ({schwarz1.Mac}) with the portrait JPEG example.");

await RunStepAsync("Portrait JSON demo on Schwarz2", () => ShowPortraitJsonDemoOnSchwarz2Async(client, schwarz2, schwarz2Type));
Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the portrait JSON example.");
await PrintStateAsync(client, "State after portrait update round");

Console.WriteLine("Waiting another 1 minute after the portrait demo round...");
await Task.Delay(TimeSpan.FromMinutes(1));

return;


await RunStepAsync("Second JPEG demo on Schwarz1", () => ShowJpegDemoOnSchwarz1Async(client, schwarz1, schwarz1Type));
Console.WriteLine($"Updated {schwarz1Alias} ({schwarz1.Mac}) with JPEG rendering again.");

await RunStepAsync("JSON demo on Schwarz2", () => ShowJsonDemoOnSchwarz2Async(client, schwarz2, schwarz2Type));
Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with native JSON template rendering.");
await PrintStateAsync(client, "State after first update round");

Console.WriteLine();
Console.WriteLine("Waiting 1 minute before updating again using the known roaming state...");
await Task.Delay(TimeSpan.FromMinutes(1));
await RunStepAsync("Weather forecast demo on Schwarz1", () => ShowWeatherForecastOnSchwarz1Async(client, schwarz1, schwarz1Type));
Console.WriteLine($"Updated {schwarz1Alias} ({schwarz1.Mac}) with tomorrow's weather forecast.");

/* temporarily disabled
await RunStepAsync("Second warehouse logistics JPEG demo on Schwarz2", () => ShowWarehouseLogisticsJpegOnSchwarz2Async(client, schwarz2, schwarz2Type));
Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the warehouse logistics JPEG example again.");
await PrintStateAsync(client, "State after second update round");
*

Console.WriteLine();
Console.WriteLine("Waiting another 1 minute before the final JSON warehouse logistics update...");
await Task.Delay(TimeSpan.FromMinutes(1));
await RunStepAsync("Warehouse logistics JSON demo on Schwarz2", () => ShowWarehouseLogisticsJsonOnSchwarz2Async(client, schwarz2, schwarz2Type));
Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the warehouse logistics JSON example.");
await PrintStateAsync(client, "State after final JSON update");
*/

Console.WriteLine();

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

static async Task PrintStateAsync(OpenEpaperLinkRoamingClient client, string title)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine(new string('=', title.Length));
    Console.WriteLine(await client.FormatStateAsync());
}

static async Task ShowWeatherForecastOnSchwarz1Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    var forecast = await GetTomorrowForecastAsync(forecastLatitude, forecastLongitude);

    using var canvas = new OeplCanvas(tagType, accentColor: OeplAccentColor.Red);

    canvas
        .DrawRoundedRectangle(0, 0, tagType.Width - 1, tagType.Height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
        .DrawRectangle(10, 10, 84, 30, fill: "red", outline: "red", outlineWidth: 1)
        .DrawTextFromFile("WETTER", 18, 16, 18, OeplBundledFonts.SansBold, "white")
        .DrawTextFromFile(forecastLocationName, 106, 14, 22, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile(forecast.DateLabel, 106, 38, 13, OeplBundledFonts.SansRegular, "black")
        .DrawLine(10, 54, 286, 54, "black", 2)
        .DrawTextFromFile(forecast.ConditionLabel, 12, 66, 18, OeplBundledFonts.SansBold, forecast.AccentColor)
        .DrawTextFromFile($"Max {forecast.MaxTemperatureC} C", 12, 94, 15, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile($"Min {forecast.MinTemperatureC} C", 12, 116, 15, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile($"Regen {forecast.PrecipitationProbabilityPercent}%", 12, 138, 13, OeplBundledFonts.SansRegular, "black")
        .DrawRectangle(178, 68, 98, 54, fill: null, outline: "black", outlineWidth: 2)
        .DrawTextFromFile("Wind", 188, 78, 14, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile($"{forecast.WindSpeedKmh} km/h", 188, 100, 18, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile(forecast.IconText, 237, 116, 22, OeplBundledFonts.SansBold, "white")
        .DrawTextFromFile($"Refreshed {DateTime.Now:HH:mm}", 182, 139, 11, OeplBundledFonts.SansRegular, "black")
        .QuantizeToDisplayPalette();

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22));
}

static async Task ShowJpegDemoOnSchwarz1Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    await ShowJpegDemoOnSchwarz1CoreAsync(client, tag, tagType, portrait: false);

    /*
    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
    Directory.CreateDirectory(outputDirectory);
    var jpegPath = Path.Combine(outputDirectory, "schwarz1-upload.jpg");
    var pngPath = Path.Combine(outputDirectory, "schwarz1-preview.png");
    canvas.SaveJpeg(jpegPath, 100);
    canvas.SavePng(pngPath);
    Console.WriteLine($"Saved Schwarz1 JPEG to {jpegPath}");
    Console.WriteLine($"Saved Schwarz1 PNG preview to {pngPath}");
    */
}

static async Task ShowPortraitJpegDemoOnSchwarz1Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType) =>
    await ShowJpegDemoOnSchwarz1CoreAsync(client, tag, tagType, portrait: true);

static async Task ShowJpegDemoOnSchwarz1CoreAsync(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType, bool portrait)
{
    using var canvas = new OeplCanvas(tagType, portrait, OeplAccentColor.Red);
    var width = canvas.Width;
    var height = canvas.Height;

    if (!portrait)
    {
        canvas
            .DrawRoundedRectangle(0, 0, width - 1, height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
            .DrawRectangle(10, 10, 102, 32, fill: "red", outline: "red", outlineWidth: 1)
            .DrawTextFromFile("Schwarz1", 61, 17, 18, OeplBundledFonts.SansBold, "white")
            .DrawTextFromFile("JPEG pipeline", 124, 14, 20, OeplBundledFonts.SansRegular, "black")
            .DrawTextFromFile(DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 124, 38, 13, OeplBundledFonts.SansRegular, "black")
            .DrawLine(10, 56, 286, 56, "black", 2)
            .DrawTextFromFile("Shapes", 12, 64, 13, OeplBundledFonts.SansRegular, "black")
            .DrawCircle(40, 102, 18, fill: "red")
            .DrawRectangle(70, 84, 36, 36, fill: "black", outline: "black")
            .DrawPolygon([new PointF(126, 118), new PointF(144, 84), new PointF(162, 118)], fill: "red", outline: "black", outlineWidth: 1)
            .DrawTextFromFile("Barcode + QR", 180, 64, 13, OeplBundledFonts.SansRegular, "black")
            .DrawBarcode("SW1-2026-03-19", 178, 82, 108, 26, OeplBarcodeType.Code128)
            .DrawQrCode(accessPointAddress, 226, 110, 56, 56);
    }
    else
    {
        canvas
            .DrawRoundedRectangle(0, 0, width - 1, height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
            .DrawRectangle(10, 10, width - 20, 28, fill: "red", outline: "red", outlineWidth: 1)
            .DrawTextFromFile("Schwarz1", 22, 16, 18, OeplBundledFonts.SansBold, "white")
            .DrawTextFromFile("Portrait JPEG", 14, 54, 18, OeplBundledFonts.SansRegular, "black")
            .DrawTextFromFile(DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 14, 78, 13, OeplBundledFonts.SansRegular, "black")
            .DrawTextFromFile(DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture), 14, 96, 13, OeplBundledFonts.SansRegular, "red")
            .DrawLine(10, 118, width - 10, 118, "black", 2)
            .DrawTextFromFile("Scan for AP", 24, 128, 13, OeplBundledFonts.SansRegular, "black")
            .DrawQrCode(accessPointAddress, 18, 146, 92, 92)
            .DrawTextFromFile("Code128", 26, 248, 13, OeplBundledFonts.SansRegular, "black")
            .DrawBarcode("SW1-PORTRAIT", 14, 264, width - 28, 18, OeplBarcodeType.Code128);
    }

    canvas.QuantizeToDisplayPalette();

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22));
}

static async Task ShowWarehouseLogisticsJpegOnSchwarz2Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    using var canvas = new OeplCanvas(tagType, accentColor: OeplAccentColor.Red);

    canvas
        .DrawRoundedRectangle(0, 0, tagType.Width - 1, tagType.Height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
        .DrawTextFromFile("Siemens, Ettlingen", 12, 14, 18, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile("ABT 220-5DM", 12, 38, 20, OeplBundledFonts.SansBold, "black")
        .DrawLine(12, 62, tagType.Width - 12, 62, "black", 2)
        .DrawBarcode("231231", 12, 72, 152, 28, OeplBarcodeType.Code128)
        .DrawTextFromFile("231231", 60, 103, 15, OeplBundledFonts.SansBold, "black")
        .DrawLine(172, 72, 172, 136, "black", 2)
        .DrawTextFromFile("WX12312312", 184, 76, 13, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile("WF156112221", 184, 92, 13, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile("Kalibrierung + Eichung", 12, 124, 13, OeplBundledFonts.SansRegular, "black")
        .DrawTextFromFile("21.03.2026", 206, 124, 13, OeplBundledFonts.SansRegular, "black")
        .DrawRectangle(10, 140, tagType.Width - 20, 8, fill: "red", outline: "red", outlineWidth: 1)
        .QuantizeToDisplayPalette();

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22));
}

static async Task ShowWarehouseLogisticsJsonOnSchwarz2Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    var document = new JsonTemplateDocument()
        .Add(new JsonRotateCommand(tagType.GetJsonRotation()))
        .Add(new JsonRoundedBoxCommand(0, 0, tagType.Width - 1, tagType.Height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
        .Add(new JsonTextCommand(12, 14, "Siemens, Ettlingen", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(12, 38, "ABT 220-5DM", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonLineCommand(12, 62, tagType.Width - 12, 62, OeplJsonColor.Black))
        .Add(new JsonRoundedBoxCommand(12, 72, 152, 28, 6, OeplJsonColor.White, OeplJsonColor.Black, 2))
        .Add(new JsonTextCommand(88, 92, "231231", "fonts/bahnschrift20", OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(88, 111, "BARCODE SLOT", "fonts/bahnschrift20", OeplJsonColor.Red, OeplJsonTextAlignment.Center))
        .Add(new JsonLineCommand(172, 72, 172, 136, OeplJsonColor.Black))
        .Add(new JsonTextCommand(184, 76, "WX12312312", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(184, 92, "WF156112221", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(12, 124, "Kalibrierung + Eichung", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonTextCommand(206, 124, "21.03.2026", "fonts/bahnschrift20", OeplJsonColor.Black))
        .Add(new JsonBoxCommand(10, 140, tagType.Width - 20, 8, OeplJsonColor.Red));

    await client.UploadJsonTemplateAsync(tag.Mac, document);
}

static async Task ShowJsonDemoOnSchwarz2Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType)
{
    await ShowJsonDemoOnSchwarz2CoreAsync(client, tag, tagType, portrait: false);
}

static async Task ShowPortraitJsonDemoOnSchwarz2Async(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType) =>
    await ShowJsonDemoOnSchwarz2CoreAsync(client, tag, tagType, portrait: true);

static async Task ShowJsonDemoOnSchwarz2CoreAsync(OpenEpaperLinkRoamingClient client, OpenEpaperLinkTag tag, OpenEpaperLinkTagType tagType, bool portrait)
{
    var width = tagType.GetRenderWidth(portrait);
    var height = tagType.GetRenderHeight(portrait);
    var rotation = tagType.GetJsonRotation(portrait);

    var document = !portrait
        ? new JsonTemplateDocument()
            .Add(new JsonRotateCommand(rotation))
            .Add(new JsonRoundedBoxCommand(0, 0, width - 1, height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
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
            .Add(new JsonTextCommand(238, 22, DateTime.Now.ToString("HH:mm"), "fonts/bahnschrift20", OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        : new JsonTemplateDocument()
            .Add(new JsonRotateCommand(rotation))
            .Add(new JsonRoundedBoxCommand(0, 0, width - 1, height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
            .Add(new JsonBoxCommand(10, 10, width - 20, 28, OeplJsonColor.Red))
            .Add(new JsonTextCommand(width / 2, 30, "S2 PORTRAIT", "fonts/bahnschrift20", OeplJsonColor.White, OeplJsonTextAlignment.Center))
            .Add(new JsonTextCommand(14, 56, "Native JSON template", "fonts/bahnschrift20", OeplJsonColor.Black))
            .Add(new JsonTextCommand(14, 76, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "fonts/bahnschrift20", OeplJsonColor.Red))
            .Add(new JsonLineCommand(10, 96, width - 10, 96, OeplJsonColor.Black))
            .Add(new JsonRoundedBoxCommand(14, 110, width - 28, 70, 8, OeplJsonColor.White, OeplJsonColor.Black, 2))
            .Add(new JsonTextCommand(width / 2, 136, "JSON", "fonts/bahnschrift20", OeplJsonColor.Red, OeplJsonTextAlignment.Center))
            .Add(new JsonTextBoxCommand(20, 150, width - 40, 24, "Portrait layout using the same tag type metadata.", "fonts/bahnschrift20", OeplJsonColor.Black, 1.0f, OeplJsonTextAlignment.Center))
            .Add(new JsonTextBoxCommand(16, 196, width - 32, 44, tag.Mac, "fonts/bahnschrift20", OeplJsonColor.Black, 1.0f, OeplJsonTextAlignment.Center))
            .Add(new JsonCircleCommand(width / 2, 258, 10, OeplJsonColor.Black))
            .Add(new JsonTriangleCommand(width / 2, 284, (width / 2) - 18, 248, (width / 2) + 18, 248, OeplJsonColor.Red));

    await client.UploadJsonTemplateAsync(tag.Mac, document);
}

static async Task<TomorrowForecast> GetTomorrowForecastAsync(double latitude, double longitude)
{
    var url = string.Create(
        CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max&timezone=auto&forecast_days=2");

    using var httpClient = new HttpClient();
    using var response = await httpClient.GetAsync(url);
    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync();
    var payload = await JsonSerializer.DeserializeAsync<OpenMeteoForecastResponse>(stream)
        ?? throw new InvalidOperationException("Open-Meteo returned an empty forecast payload.");

    var daily = payload.Daily ?? throw new InvalidOperationException("Open-Meteo daily forecast data is missing.");

    if (daily.Time is null ||
        daily.WeatherCode is null ||
        daily.TemperatureMax is null ||
        daily.TemperatureMin is null ||
        daily.PrecipitationProbabilityMax is null ||
        daily.WindSpeedMax is null)
    {
        throw new InvalidOperationException("Open-Meteo did not provide all required daily forecast fields.");
    }

    if (daily.Time.Count < 2 ||
        daily.WeatherCode.Count < 2 ||
        daily.TemperatureMax.Count < 2 ||
        daily.TemperatureMin.Count < 2 ||
        daily.PrecipitationProbabilityMax.Count < 2 ||
        daily.WindSpeedMax.Count < 2)
    {
        throw new InvalidOperationException("Open-Meteo did not return a next-day forecast.");
    }

    var date = daily.Time[1];
    var weatherCode = daily.WeatherCode[1];

    return CreateTomorrowForecast(
        date,
        weatherCode,
        daily.TemperatureMax[1],
        daily.TemperatureMin[1],
        daily.PrecipitationProbabilityMax[1],
        daily.WindSpeedMax[1]);
}

static TomorrowForecast CreateTomorrowForecast(string isoDate, int weatherCode, double maxTemperatureC, double minTemperatureC, int precipitationProbabilityPercent, double windSpeedKmh)
{
    var date = DateOnly.Parse(isoDate, CultureInfo.InvariantCulture);
    var (conditionLabel, iconText, accentColor) = DescribeWeatherCode(weatherCode);

    return new TomorrowForecast(
        date.ToString("ddd, dd MMM", CultureInfo.InvariantCulture),
        conditionLabel,
        iconText,
        accentColor,
        Math.Round(maxTemperatureC).ToString("0", CultureInfo.InvariantCulture),
        Math.Round(minTemperatureC).ToString("0", CultureInfo.InvariantCulture),
        precipitationProbabilityPercent,
        Math.Round(windSpeedKmh).ToString("0", CultureInfo.InvariantCulture));
}

static (string ConditionLabel, string IconText, string AccentColor) DescribeWeatherCode(int code) =>
    code switch
    {
        0 => ("Clear sky", "SUN", "red"),
        1 or 2 => ("Partly cloudy", "SUN", "red"),
        3 => ("Overcast", "CLD", "black"),
        45 or 48 => ("Foggy", "FOG", "black"),
        51 or 53 or 55 or 56 or 57 => ("Drizzle", "DRP", "red"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("Rain", "RAN", "red"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Snow", "SNW", "black"),
        95 or 96 or 99 => ("Thunderstorm", "STM", "red"),
        _ => ("Forecast", "DAY", "black")
    };

internal sealed record TomorrowForecast(
    string DateLabel,
    string ConditionLabel,
    string IconText,
    string AccentColor,
    string MaxTemperatureC,
    string MinTemperatureC,
    int PrecipitationProbabilityPercent,
    string WindSpeedKmh);

internal sealed class OpenMeteoForecastResponse
{
    [JsonPropertyName("daily")]
    public OpenMeteoDailyForecast? Daily { get; init; }
}

internal sealed class OpenMeteoDailyForecast
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; init; }

    [JsonPropertyName("weather_code")]
    public List<int>? WeatherCode { get; init; }

    [JsonPropertyName("temperature_2m_max")]
    public List<double>? TemperatureMax { get; init; }

    [JsonPropertyName("temperature_2m_min")]
    public List<double>? TemperatureMin { get; init; }

    [JsonPropertyName("precipitation_probability_max")]
    public List<int>? PrecipitationProbabilityMax { get; init; }

    [JsonPropertyName("wind_speed_10m_max")]
    public List<double>? WindSpeedMax { get; init; }
}
