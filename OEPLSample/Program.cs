using OEPLLib;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

var testMode = false;

const string accessPointAddress = "http://192.168.2.178";
const string mealieBaseAddress = "https://mealie.turino.de";
const string supermarktShoppingListId = "f344e66e-6115-4336-a8c5-38bb49ec6515";
const string schwarz1Alias = "Schwarz1";
const string schwarz2Alias = "Schwarz2";
const double forecastLatitude = 48.311944;
const double forecastLongitude = 8.917778;
const string forecastLocationName = "Bisingen, DE";
const int mealPlanDayCount = 8;
var shoppingListPollInterval = TimeSpan.FromMinutes(1);
var shoppingListRenderMode = LoadRenderModeSetting("SHOPPING_LIST_RENDER_MODE", DisplayRenderMode.Jpeg);
var mealPlanRenderMode = LoadRenderModeSetting("MEAL_PLAN_RENDER_MODE", DisplayRenderMode.Jpeg);

using var client = new OpenEpaperLinkRoamingClient(
[
    new OpenEpaperLinkAccessPointRegistration("ap-1", accessPointAddress)
]);
client.DebugLog = message => Console.WriteLine(message);


var schwarz1 = await client.GetTagByAliasAsync(schwarz1Alias);
if (schwarz1 is null)
{
    throw new InvalidOperationException($"Could not resolve sample tag '{schwarz1Alias}'.");
}

var schwarz1Type = await client.GetTagTypeAsync(schwarz1.HardwareType)
    ?? throw new InvalidOperationException($"No tag type metadata was found for {schwarz1Alias}.");


if (testMode)
{
    var schwarz2 = await client.GetTagByAliasAsync(schwarz2Alias)
        ?? throw new InvalidOperationException($"Could not resolve sample tag '{schwarz2Alias}'.");
    var schwarz2Type = await client.GetTagTypeAsync(schwarz2.HardwareType)
        ?? throw new InvalidOperationException($"No tag type metadata was found for {schwarz2Alias}.");

    await RunStepAsync("Portrait JPEG demo on Schwarz1", () => ShowPortraitJpegDemoOnSchwarz1Async(client, schwarz1, schwarz1Type));
    Console.WriteLine($"Updated {schwarz1Alias} ({schwarz1.Mac}) with the portrait JPEG example.");

    await RunStepAsync("Portrait JSON demo on Schwarz2", () => ShowPortraitJsonDemoOnSchwarz2Async(client, schwarz2, schwarz2Type));
    Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the portrait JSON example.");
    await PrintStateAsync(client, "State after portrait update round");

    Console.WriteLine("Waiting another 1 minute after the portrait demo round...");
    await Task.Delay(TimeSpan.FromMinutes(1));

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

    // temporarily disabled
    await RunStepAsync("Second warehouse logistics JPEG demo on Schwarz2", () => ShowWarehouseLogisticsJpegOnSchwarz2Async(client, schwarz2, schwarz2Type));
    Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the warehouse logistics JPEG example again.");
    await PrintStateAsync(client, "State after second update round");

    Console.WriteLine();
    Console.WriteLine("Waiting another 1 minute before the final JSON warehouse logistics update...");
    await Task.Delay(TimeSpan.FromMinutes(1));
    await RunStepAsync("Warehouse logistics JSON demo on Schwarz2", () => ShowWarehouseLogisticsJsonOnSchwarz2Async(client, schwarz2, schwarz2Type));
    Console.WriteLine($"Updated {schwarz2Alias} ({schwarz2.Mac}) with the warehouse logistics JSON example.");
    await PrintStateAsync(client, "State after final JSON update");
}
else
{
    var schwarz2 = await client.GetTagByAliasAsync(schwarz2Alias)
        ?? throw new InvalidOperationException($"Could not resolve sample tag '{schwarz2Alias}'.");
    var schwarz2Type = await client.GetTagTypeAsync(schwarz2.HardwareType)
        ?? throw new InvalidOperationException($"No tag type metadata was found for {schwarz2Alias}.");

    var mealieToken = LoadRequiredSetting("MEALIE_TOKEN");
    using var mealieClient = CreateMealieClient(mealieToken);
    using var cancellationTokenSource = new CancellationTokenSource();

    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationTokenSource.Cancel();
    };

    Console.WriteLine($"Starting Mealie sync for list '{supermarktShoppingListId}' to {schwarz1Alias} in portrait mode using {shoppingListRenderMode} rendering.");
    Console.WriteLine($"Starting Mealie sync for the meal plan on {schwarz2Alias} for today plus the next {mealPlanDayCount - 1} days using {mealPlanRenderMode} rendering.");
    Console.WriteLine("Only unchecked shopping-list items are shown on Schwarz1. Press Ctrl+C to stop.");

    await Task.WhenAll(
        RunShoppingListSyncLoopAsync(
            mealieClient,
            client,
            schwarz1,
            schwarz1Type,
            supermarktShoppingListId,
            shoppingListPollInterval,
            shoppingListRenderMode,
            cancellationTokenSource.Token),
        RunMealPlanSyncLoopAsync(
            mealieClient,
            client,
            schwarz2,
            schwarz2Type,
            shoppingListPollInterval,
            mealPlanDayCount,
            mealPlanRenderMode,
            cancellationTokenSource.Token));

}

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

static HttpClient CreateMealieClient(string mealieToken)
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(mealieBaseAddress.TrimEnd('/') + "/", UriKind.Absolute)
    };
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mealieToken);
    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return httpClient;
}

static string? LoadOptionalSetting(string key)
{
    var value = Environment.GetEnvironmentVariable(key);
    if (!string.IsNullOrWhiteSpace(value))
    {
        return value.Trim();
    }

    foreach (var envFilePath in GetCandidateEnvFilePaths())
    {
        var configuredValue = TryReadEnvValue(envFilePath, key);
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue;
        }
    }

    return null;
}

static string LoadRequiredSetting(string key)
{
    return LoadOptionalSetting(key)
        ?? throw new InvalidOperationException(
            $"Missing required setting '{key}'. Set it as an environment variable or add it to OEPLSample/.env.");
}

static DisplayRenderMode LoadRenderModeSetting(string key, DisplayRenderMode defaultValue)
{
    var value = LoadOptionalSetting(key) ?? LoadOptionalSetting("OEPL_RENDER_MODE");
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "json" => DisplayRenderMode.Json,
        "jpeg" or "jpg" or "image" => DisplayRenderMode.Jpeg,
        _ => throw new InvalidOperationException(
            $"Unsupported render mode '{value}' for '{key}'. Use 'json' or 'jpeg'.")
    };
}

static IEnumerable<string> GetCandidateEnvFilePaths()
{
    var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var candidatePath = Path.Combine(directory.FullName, ".env");
            if (seenPaths.Add(candidatePath))
            {
                yield return candidatePath;
            }

            directory = directory.Parent;
        }
    }
}

static string? TryReadEnvValue(string envFilePath, string key)
{
    if (!File.Exists(envFilePath))
    {
        return null;
    }

    foreach (var rawLine in File.ReadLines(envFilePath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var candidateKey = line[..separatorIndex].Trim();
        if (!string.Equals(candidateKey, key, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var value = line[(separatorIndex + 1)..].Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            value = value[1..^1];
        }

        return value;
    }

    return null;
}

static async Task RunShoppingListSyncLoopAsync(
    HttpClient mealieClient,
    OpenEpaperLinkRoamingClient oeplClient,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    string shoppingListId,
    TimeSpan pollInterval,
    DisplayRenderMode renderMode,
    CancellationToken cancellationToken)
{
    string? latestKnownState = null;
    DateTimeOffset? lastDisplayUpdateUtc = null;
    var periodicRefreshInterval = TimeSpan.FromHours(12);

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var snapshot = await GetShoppingListSnapshotAsync(mealieClient, shoppingListId, cancellationToken);
            var stateChanged = !string.Equals(snapshot.StateSignature, latestKnownState, StringComparison.Ordinal);
            var periodicRefreshDue = lastDisplayUpdateUtc is null || (DateTimeOffset.UtcNow - lastDisplayUpdateUtc.Value) >= periodicRefreshInterval;

            if (!stateChanged && !periodicRefreshDue)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Shopping list unchanged ({snapshot.Items.Count} open item(s)).");
            }
            else
            {
                await ShowShoppingListOnSchwarz1Async(oeplClient, tag, tagType, snapshot, renderMode, cancellationToken);
                latestKnownState = snapshot.StateSignature;
                lastDisplayUpdateUtc = DateTimeOffset.UtcNow;

                if (stateChanged)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Synced {snapshot.Items.Count} open item(s) from '{snapshot.Name}' to {tag.Alias ?? tag.Mac} via {renderMode}.");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Refreshed unchanged shopping list '{snapshot.Name}' on {tag.Alias ?? tag.Mac} after {periodicRefreshInterval.TotalHours:0}h via {renderMode}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Shopping list sync failed: {ex.Message}");
        }

        try
        {
            await Task.Delay(pollInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
    }
}

static async Task RunMealPlanSyncLoopAsync(
    HttpClient mealieClient,
    OpenEpaperLinkRoamingClient oeplClient,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    TimeSpan pollInterval,
    int dayCount,
    DisplayRenderMode renderMode,
    CancellationToken cancellationToken)
{
    string? latestKnownState = null;
    DateTimeOffset? lastDisplayUpdateUtc = null;
    var periodicRefreshInterval = TimeSpan.FromHours(12);

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var startDate = DateOnly.FromDateTime(DateTime.Now);
            var snapshot = await GetMealPlanSnapshotAsync(mealieClient, startDate, dayCount, cancellationToken);
            var stateChanged = !string.Equals(snapshot.StateSignature, latestKnownState, StringComparison.Ordinal);
            var periodicRefreshDue = lastDisplayUpdateUtc is null || (DateTimeOffset.UtcNow - lastDisplayUpdateUtc.Value) >= periodicRefreshInterval;

            if (!stateChanged && !periodicRefreshDue)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Meal plan unchanged ({snapshot.TotalMealCount} planned meal(s)).");
            }
            else
            {
                await ShowMealPlanOnSchwarz2Async(oeplClient, tag, tagType, snapshot, renderMode, cancellationToken);
                latestKnownState = snapshot.StateSignature;
                lastDisplayUpdateUtc = DateTimeOffset.UtcNow;

                if (stateChanged)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Synced {snapshot.TotalMealCount} planned meal(s) from {snapshot.StartDate:yyyy-MM-dd} to {snapshot.EndDate:yyyy-MM-dd} to {tag.Alias ?? tag.Mac} via {renderMode}.");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Refreshed unchanged meal plan for {snapshot.StartDate:yyyy-MM-dd} to {snapshot.EndDate:yyyy-MM-dd} on {tag.Alias ?? tag.Mac} after {periodicRefreshInterval.TotalHours:0}h via {renderMode}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Meal plan sync failed: {ex.Message}");
        }

        try
        {
            await Task.Delay(pollInterval, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
    }
}

static async Task<ShoppingListSnapshot> GetShoppingListSnapshotAsync(HttpClient mealieClient, string shoppingListId, CancellationToken cancellationToken)
{
    using var response = await mealieClient.GetAsync($"api/households/shopping/lists/{shoppingListId}", cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    var shoppingList = await JsonSerializer.DeserializeAsync<MealieShoppingListResponse>(stream, cancellationToken: cancellationToken)
        ?? throw new InvalidOperationException("Mealie returned an empty shopping-list payload.");

    var recipeNamesById = (shoppingList.RecipeReferences ?? [])
        .Where(reference => !string.IsNullOrWhiteSpace(reference.RecipeId))
        .GroupBy(reference => reference.RecipeId!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => NormalizeWhitespace(group
                .Select(reference => reference.Recipe?.Name)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))),
            StringComparer.OrdinalIgnoreCase);

    var items = (shoppingList.ListItems ?? [])
        .Where(item => !item.Checked)
        .Select(item => CreateShoppingListEntry(item, recipeNamesById))
        .Where(entry => !string.IsNullOrWhiteSpace(entry.DisplayText))
        .OrderBy(entry => entry.HasRecipe ? 1 : 0)
        .ThenBy(entry => entry.RecipeSortKey, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(entry => entry.IngredientSortKey, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(entry => entry.DisplayText, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    return new ShoppingListSnapshot(
        shoppingList.Name?.Trim() is { Length: > 0 } name ? name : "Shopping list",
        items,
        string.Join('\n', items.Select(item => $"{item.RecipeSortKey}|{item.IngredientSortKey}|{item.DisplayText}")));
}

static async Task<MealPlanSnapshot> GetMealPlanSnapshotAsync(
    HttpClient mealieClient,
    DateOnly startDate,
    int dayCount,
    CancellationToken cancellationToken)
{
    var endDate = startDate.AddDays(dayCount - 1);
    var requestUri =
        $"api/households/mealplans?page=1&perPage=-1&start_date={startDate:yyyy-MM-dd}&end_date={endDate:yyyy-MM-dd}";

    using var response = await mealieClient.GetAsync(requestUri, cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
    var mealPlan = await JsonSerializer.DeserializeAsync<MealieMealPlanResponse>(stream, cancellationToken: cancellationToken)
        ?? throw new InvalidOperationException("Mealie returned an empty meal-plan payload.");

    var entriesByDate = new Dictionary<DateOnly, Dictionary<string, List<string>>>();
    foreach (var item in mealPlan.Items ?? [])
    {
        if (!TryCreateMealPlanEntry(item, out var date, out var mealType, out var recipeName))
        {
            continue;
        }

        if (!entriesByDate.TryGetValue(date, out var mealsByType))
        {
            mealsByType = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            entriesByDate[date] = mealsByType;
        }

        if (!mealsByType.TryGetValue(mealType, out var recipes))
        {
            recipes = [];
            mealsByType[mealType] = recipes;
        }

        recipes.Add(recipeName);
    }

    var days = Enumerable.Range(0, dayCount)
        .Select(offset =>
        {
            var date = startDate.AddDays(offset);
            entriesByDate.TryGetValue(date, out var mealsByType);

            return new MealPlanDay(
                date,
                BuildMealPlanDayLabel(date),
                FormatMealPlanCellText(mealsByType, "breakfast"),
                FormatMealPlanCellText(mealsByType, "lunch"),
                FormatMealPlanCellText(mealsByType, "dinner"));
        })
        .ToList();

    return new MealPlanSnapshot(
        startDate,
        endDate,
        days,
        days.Sum(day => day.PlannedMealCount),
        string.Join('\n', days.Select(day => $"{day.Date:yyyy-MM-dd}|{day.Breakfast}|{day.Lunch}|{day.Dinner}")));
}

static ShoppingListEntry CreateShoppingListEntry(
    MealieShoppingListItem item,
    IReadOnlyDictionary<string, string?> recipeNamesById)
{
    var display = BuildIngredientDisplayText(item);
    if (string.IsNullOrWhiteSpace(display))
    {
        return new ShoppingListEntry(string.Empty, string.Empty, string.Empty);
    }

    var recipeNames = GetRecipeNames(item, recipeNamesById);
    var recipeLabel = recipeNames.Count == 0
        ? null
        : string.Join(" + ", recipeNames);

    var ingredientSortKey =
        NormalizeWhitespace(item.Food?.Name) ??
        NormalizeWhitespace(item.Display) ??
        NormalizeWhitespace(item.Note) ??
        display;

    return new ShoppingListEntry(
        display,
        ingredientSortKey,
        recipeLabel);
}

static string BuildIngredientDisplayText(MealieShoppingListItem item)
{
    var display = NormalizeWhitespace(item.Display);
    if (!string.IsNullOrWhiteSpace(display))
    {
        return display;
    }

    var parts = new List<string>();

    if (item.Quantity is > 0)
    {
        parts.Add(item.Quantity.Value.ToString("0.##", CultureInfo.InvariantCulture));
    }

    var unit = NormalizeWhitespace(item.Unit?.Abbreviation)
        ?? NormalizeWhitespace(item.Unit?.Name);
    if(unit == "Gramm")
        unit = "g";
    else if(unit == "Kilogramm")
        unit = "kg";


    if (!string.IsNullOrWhiteSpace(unit))
    {
        parts.Add(unit);
    }

    var food = NormalizeWhitespace(item.Food?.Name);
    if (!string.IsNullOrWhiteSpace(food))
    {
        parts.Add(food);
    }

    var note = NormalizeWhitespace(item.Note);
    if (!string.IsNullOrWhiteSpace(note))
    {
        parts.Add(note);
    }

    return string.Join(' ', parts);
}

static IReadOnlyList<string> GetRecipeNames(
    MealieShoppingListItem item,
    IReadOnlyDictionary<string, string?> recipeNamesById)
{
    var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

    var referencedRecipeName = NormalizeWhitespace(item.ReferencedRecipe?.Name);
    if (!string.IsNullOrWhiteSpace(referencedRecipeName))
    {
        names.Add(referencedRecipeName);
    }

    foreach (var recipeReference in item.RecipeReferences ?? [])
    {
        var recipeName =
            NormalizeWhitespace(recipeReference.Recipe?.Name) ??
            (recipeReference.RecipeId is { Length: > 0 } recipeId && recipeNamesById.TryGetValue(recipeId, out var mappedName)
                ? NormalizeWhitespace(mappedName)
                : null);

        if (!string.IsNullOrWhiteSpace(recipeName))
        {
            names.Add(recipeName);
        }
    }

    return names
        .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();
}

static string? NormalizeWhitespace(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

static bool TryCreateMealPlanEntry(
    MealieMealPlanItem item,
    out DateOnly date,
    out string mealType,
    out string recipeName)
{
    date = default;
    mealType = string.Empty;
    recipeName = string.Empty;

    if (string.IsNullOrWhiteSpace(item.Date) ||
        !DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
    {
        return false;
    }

    mealType = NormalizeMealPlanEntryType(item.EntryType) ?? string.Empty;
    if (mealType.Length == 0)
    {
        return false;
    }

    recipeName = BuildMealPlanRecipeName(item) ?? string.Empty;
    return recipeName.Length > 0;
}

static string? NormalizeMealPlanEntryType(string? value) =>
    NormalizeWhitespace(value)?.ToLowerInvariant() switch
    {
        "breakfast" => "breakfast",
        "lunch" => "lunch",
        "dinner" => "dinner",
        _ => null
    };

static string? BuildMealPlanRecipeName(MealieMealPlanItem item) =>
    NormalizeWhitespace(item.Recipe?.Name) ??
    NormalizeWhitespace(item.Title) ??
    NormalizeWhitespace(item.Text);

static string FormatMealPlanCellText(
    IReadOnlyDictionary<string, List<string>>? mealsByType,
    string mealType)
{
    if (mealsByType is null || !mealsByType.TryGetValue(mealType, out var values) || values.Count == 0)
    {
        return "-";
    }

    return string.Join(
        " + ",
        values
            .Select(NormalizeWhitespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)!);
}

static string BuildMealPlanDayLabel(DateOnly date) =>
    $"{GetGermanWeekdayAbbreviation(date)} {date:dd}";

static string GetGermanWeekdayAbbreviation(DateOnly date) =>
    date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Mo",
        DayOfWeek.Tuesday => "Di",
        DayOfWeek.Wednesday => "Mi",
        DayOfWeek.Thursday => "Do",
        DayOfWeek.Friday => "Fr",
        DayOfWeek.Saturday => "Sa",
        DayOfWeek.Sunday => "So",
        _ => "--"
    };

static async Task ShowShoppingListOnSchwarz1Async(
    OpenEpaperLinkRoamingClient client,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    ShoppingListSnapshot snapshot,
    DisplayRenderMode renderMode,
    CancellationToken cancellationToken)
{
    if (renderMode == DisplayRenderMode.Json)
    {
        await ShowShoppingListOnSchwarz1AsJsonAsync(client, tag, tagType, snapshot, cancellationToken);
        return;
    }

    using var canvas = new OeplCanvas(tagType, portrait: true, OeplAccentColor.Red);
    var width = canvas.Width;
    var height = canvas.Height;
    const int padding = 5;
    const int headerHeight = 24;
    const int infoTop = 30;
    const int listTop = 50;
    const int footerHeight = 14;
    const int lineHeight = 15;
    const float titleFontSize = 13;
    const float infoFontSize = 11;
    const float itemFontSize = 11;
    var availableListHeight = height - listTop - footerHeight - padding;
    var maxVisibleLines = Math.Max(1, availableListHeight / lineHeight);
    var standardLineMaxCharacters = Math.Max(12, (width - 28) / 6);
    var recipeLineMaxCharacters = Math.Max(10, (width - 26) / 6);
    var indentedLineMaxCharacters = Math.Max(8, (width - 42) / 6);

    var visibleLines = BuildShoppingListDisplayLines(snapshot.Items)
        .Select(line => line with
        {
            Text = TruncateWithEllipsis(
                line.Text,
                line.Kind switch
                {
                    ShoppingListDisplayLineKind.RecipeHeader => recipeLineMaxCharacters,
                    ShoppingListDisplayLineKind.RecipeIngredient => indentedLineMaxCharacters,
                    _ => standardLineMaxCharacters
                })
        })
        .ToList();

    if (visibleLines.Count > maxVisibleLines)
    {
        visibleLines = visibleLines.Take(maxVisibleLines).ToList();
        visibleLines[^1] = new ShoppingListDisplayLine("...", ShoppingListDisplayLineKind.Truncation);
    }

    canvas
        .DrawRoundedRectangle(0, 0, width - 1, height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
        .DrawRectangle(padding, padding, width - (padding * 2), headerHeight, fill: "red", outline: "red", outlineWidth: 1)
        .DrawTextFromFile(TruncateWithEllipsis(snapshot.Name, standardLineMaxCharacters - 1), padding + 5, padding + 3, titleFontSize, OeplBundledFonts.SansBold, "white")
        .DrawTextFromFile($"{snapshot.Items.Count} offen", padding, infoTop, infoFontSize, OeplBundledFonts.SansBold, "black")
        .DrawLine(padding, listTop - 8, width - padding, listTop - 8, "black", 2);

    if (visibleLines.Count == 0)
    {
        canvas
            .DrawTextFromFile("Alles erledigt", padding, listTop, 14, OeplBundledFonts.SansBold, "black")
            .DrawTextFromFile("Keine offenen Einträge.", padding, listTop + 20, itemFontSize, OeplBundledFonts.SansRegular, "black");
    }
    else
    {
        for (var i = 0; i < visibleLines.Count; i++)
        {
            var y = listTop + (i * lineHeight);
            var line = visibleLines[i];

            switch (line.Kind)
            {
                case ShoppingListDisplayLineKind.RecipeHeader:
                    canvas
                        .DrawPolygon(
                            [
                                new PointF(padding + 1, y + 4),
                                new PointF(padding + 7, y + 7),
                                new PointF(padding + 1, y + 10)
                            ],
                            fill: "red",
                            outline: "red",
                            outlineWidth: 1)
                        .DrawTextFromFile(line.Text, padding + 12, y, itemFontSize, OeplBundledFonts.SansBold, "black");
                    break;
                case ShoppingListDisplayLineKind.RecipeIngredient:
                    canvas
                        .DrawLine(padding + 10, y + 7, padding + 14, y + 7, "black", 1)
                        .DrawTextFromFile(line.Text, padding + 18, y, itemFontSize, OeplBundledFonts.SansRegular, "black");
                    break;
                case ShoppingListDisplayLineKind.Truncation:
                    canvas
                        .DrawCircle(padding + 3, y + 7, 1.2f, fill: "red", outline: "red", outlineWidth: 1)
                        .DrawCircle(padding + 7, y + 7, 1.2f, fill: "red", outline: "red", outlineWidth: 1)
                        .DrawCircle(padding + 11, y + 7, 1.2f, fill: "red", outline: "red", outlineWidth: 1)
                        .DrawTextFromFile(line.Text, padding + 18, y, itemFontSize, OeplBundledFonts.SansRegular, "black");
                    break;
                default:
                    canvas
                        .DrawRectangle(padding, y + 2, 4, 4, fill: "black", outline: "black", outlineWidth: 1)
                        .DrawTextFromFile(line.Text, padding + 10, y, itemFontSize, OeplBundledFonts.SansRegular, "black");
                    break;
            }
        }
    }

    canvas.QuantizeToDisplayPalette();

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22),
        cancellationToken);
}

static async Task ShowMealPlanOnSchwarz2Async(
    OpenEpaperLinkRoamingClient client,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    MealPlanSnapshot snapshot,
    DisplayRenderMode renderMode,
    CancellationToken cancellationToken)
{
    if (renderMode == DisplayRenderMode.Json)
    {
        await ShowMealPlanOnSchwarz2AsJsonAsync(client, tag, tagType, snapshot, cancellationToken);
        return;
    }

    using var canvas = new OeplCanvas(tagType, accentColor: OeplAccentColor.Red);
    var width = canvas.Width;
    var height = canvas.Height;
    const int padding = 5;
    const int tableTop = 9;
    const float tableHeaderFontSize = 11;
    const float rowFontSize = 11;
    const int dayColumnWidth = 34;
    var mealColumnsWidth = width - (padding * 2) - dayColumnWidth;
    var breakfastColumnWidth = mealColumnsWidth / 3;
    var lunchColumnWidth = mealColumnsWidth / 3;
    var dinnerColumnWidth = mealColumnsWidth - breakfastColumnWidth - lunchColumnWidth;
    var breakfastX = padding + dayColumnWidth;
    var lunchX = breakfastX + breakfastColumnWidth;
    var dinnerX = lunchX + lunchColumnWidth;
    var rowHeight = Math.Max(10, (height - tableTop - padding) / (snapshot.Days.Count + 1));
    var maxDayCharacters = EstimateMealPlanColumnCharacters(dayColumnWidth);
    var maxBreakfastCharacters = EstimateMealPlanColumnCharacters(breakfastColumnWidth);
    var maxLunchCharacters = EstimateMealPlanColumnCharacters(lunchColumnWidth);
    var maxDinnerCharacters = EstimateMealPlanColumnCharacters(dinnerColumnWidth);
    var tableBottom = Math.Min(height - padding, tableTop + ((snapshot.Days.Count + 1) * rowHeight));

    canvas
        .DrawRoundedRectangle(0, 0, width - 1, height - 1, 10, fill: "white", outline: "black", outlineWidth: 2)
        .DrawLine(padding, tableTop - 3, width - padding, tableTop - 3, "black", 1)
        .DrawLine(breakfastX, tableTop - 2, breakfastX, tableBottom - 1, "black", 1)
        .DrawLine(lunchX, tableTop - 2, lunchX, tableBottom - 1, "black", 1)
        .DrawLine(dinnerX, tableTop - 2, dinnerX, tableBottom - 1, "black", 1)
        .DrawTextFromFile("Tag", padding + 2, tableTop, tableHeaderFontSize, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile("Frühstück", breakfastX + 2, tableTop, tableHeaderFontSize, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile("Mittag", lunchX + 2, tableTop, tableHeaderFontSize, OeplBundledFonts.SansBold, "black")
        .DrawTextFromFile("Abends", dinnerX + 2, tableTop, tableHeaderFontSize, OeplBundledFonts.SansBold, "black")
        .DrawLine(padding, tableTop + rowHeight - 1, width - padding, tableTop + rowHeight - 1, "black", 1);

    for (var i = 0; i < snapshot.Days.Count; i++)
    {
        var day = snapshot.Days[i];
        var y = tableTop + ((i + 1) * rowHeight);
        var rowTop = y - 1;
        var rowFillHeight = Math.Max(1, rowHeight - 1);
        var cellFillHeight = Math.Max(1, rowFillHeight - 2);
        var lunchFillColor = day.HasMissingMainMeal ? "red" : "white";
        var dinnerFillColor = day.HasMissingMainMeal ? "red" : "white";

        canvas.DrawTextFromFile(TruncateWithEllipsis(day.Label, maxDayCharacters), padding + 2, y, rowFontSize, OeplBundledFonts.SansBold, day.HasMissingMainMeal ? "red" : "black");
        canvas.DrawRectangle(padding + 1, rowTop + 1, dayColumnWidth - 1, cellFillHeight, fill: "white", outline: "white", outlineWidth: 0);

        // canvas.DrawRectangle(breakfastX + 1, rowTop + 1, breakfastColumnWidth - 1, cellFillHeight, fill: "white", outline: "white", outlineWidth: 0);
        canvas.DrawTextFromFile(TruncateWithEllipsis(day.Breakfast, maxBreakfastCharacters), breakfastX + 2, y, rowFontSize, OeplBundledFonts.SansRegular, "black");

        canvas.DrawRectangle(lunchX + 1, rowTop + 1, lunchColumnWidth - 1, cellFillHeight, fill: lunchFillColor, outline: lunchFillColor, outlineWidth: 0);
        canvas.DrawTextFromFile(TruncateWithEllipsis(day.Lunch, maxLunchCharacters), lunchX + 2, y, rowFontSize, OeplBundledFonts.SansRegular, day.HasMissingMainMeal ? "white" : "black");

        canvas.DrawRectangle(dinnerX + 1, rowTop + 1, dinnerColumnWidth - 1, cellFillHeight, fill: dinnerFillColor, outline: dinnerFillColor, outlineWidth: 0);
        canvas.DrawTextFromFile(TruncateWithEllipsis(day.Dinner, maxDinnerCharacters), dinnerX + 2, y, rowFontSize, OeplBundledFonts.SansRegular, day.HasMissingMainMeal ? "white" : "black");

        if (i < snapshot.Days.Count - 1)
        {
            canvas.DrawLine(padding, y + rowHeight - 1, width - padding, y + rowHeight - 1, "black", 1);
        }
    }

    canvas.QuantizeToDisplayPalette();

    await client.UploadRenderedImageAsync(
        tag.Mac,
        canvas,
        new OpenEpaperLinkImageUploadOptions(
            OpenEpaperLinkDitherMode.None,
            90,
            22),
        cancellationToken);
}

static async Task ShowShoppingListOnSchwarz1AsJsonAsync(
    OpenEpaperLinkRoamingClient client,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    ShoppingListSnapshot snapshot,
    CancellationToken cancellationToken)
{
    const string font = "fonts/bahnschrift20";
    var width = tagType.GetRenderWidth(portrait: true);
    var height = tagType.GetRenderHeight(portrait: true);
    const int padding = 5;
    const int headerHeight = 24;
    const int infoTop = 34;
    const int listTop = 52;
    const int footerHeight = 14;
    const int lineHeight = 16;
    var availableListHeight = height - listTop - footerHeight - padding;
    var maxVisibleLines = Math.Max(1, availableListHeight / lineHeight);
    var standardLineMaxCharacters = Math.Max(12, (width - 28) / 6);
    var recipeLineMaxCharacters = Math.Max(10, (width - 26) / 6);
    var indentedLineMaxCharacters = Math.Max(8, (width - 42) / 6);

    var visibleLines = BuildShoppingListDisplayLines(snapshot.Items)
        .Select(line => line with
        {
            Text = TruncateWithEllipsis(
                line.Text,
                line.Kind switch
                {
                    ShoppingListDisplayLineKind.RecipeHeader => recipeLineMaxCharacters,
                    ShoppingListDisplayLineKind.RecipeIngredient => indentedLineMaxCharacters,
                    _ => standardLineMaxCharacters
                })
        })
        .ToList();

    if (visibleLines.Count > maxVisibleLines)
    {
        visibleLines = visibleLines.Take(maxVisibleLines).ToList();
        visibleLines[^1] = new ShoppingListDisplayLine("...", ShoppingListDisplayLineKind.Truncation);
    }

    var document = new JsonTemplateDocument()
        .Add(new JsonRotateCommand(tagType.GetJsonRotation(portrait: true)))
        .Add(new JsonRoundedBoxCommand(0, 0, width - 1, height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
        .Add(new JsonBoxCommand(padding, padding, width - (padding * 2), headerHeight, OeplJsonColor.Red))
        .Add(new JsonTextCommand(width / 2, padding + 17, TruncateWithEllipsis(snapshot.Name, standardLineMaxCharacters - 1), font, OeplJsonColor.White, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(padding, infoTop, $"{snapshot.Items.Count} offen", font, OeplJsonColor.Black))
        .Add(new JsonLineCommand(padding, listTop - 8, width - padding, listTop - 8, OeplJsonColor.Black));

    if (visibleLines.Count == 0)
    {
        document
            .Add(new JsonTextCommand(padding, listTop, "Alles erledigt", font, OeplJsonColor.Black))
            .Add(new JsonTextCommand(padding, listTop + 20, "Keine offenen Eintraege.", font, OeplJsonColor.Black));
    }
    else
    {
        for (var i = 0; i < visibleLines.Count; i++)
        {
            var y = listTop + (i * lineHeight);
            var line = visibleLines[i];

            switch (line.Kind)
            {
                case ShoppingListDisplayLineKind.RecipeHeader:
                    document
                        .Add(new JsonBoxCommand(padding, y + 3, 5, 5, OeplJsonColor.Red))
                        .Add(new JsonTextCommand(padding + 12, y, line.Text, font, OeplJsonColor.Black));
                    break;
                case ShoppingListDisplayLineKind.RecipeIngredient:
                    document
                        .Add(new JsonLineCommand(padding + 10, y + 7, padding + 14, y + 7, OeplJsonColor.Black))
                        .Add(new JsonTextCommand(padding + 18, y, line.Text, font, OeplJsonColor.Black));
                    break;
                case ShoppingListDisplayLineKind.Truncation:
                    document.Add(new JsonTextCommand(padding, y, line.Text, font, OeplJsonColor.Red));
                    break;
                default:
                    document
                        .Add(new JsonBoxCommand(padding, y + 3, 4, 4, OeplJsonColor.Black))
                        .Add(new JsonTextCommand(padding + 10, y, line.Text, font, OeplJsonColor.Black));
                    break;
            }
        }
    }

    await client.UploadJsonTemplateAsync(tag.Mac, document, cancellationToken);
}

static async Task ShowMealPlanOnSchwarz2AsJsonAsync(
    OpenEpaperLinkRoamingClient client,
    OpenEpaperLinkTag tag,
    OpenEpaperLinkTagType tagType,
    MealPlanSnapshot snapshot,
    CancellationToken cancellationToken)
{
    const string font = "fonts/bahnschrift20";
    var width = tagType.GetRenderWidth();
    var height = tagType.GetRenderHeight();
    const int padding = 5;
    const int tableTop = 9;
    const int dayColumnWidth = 34;
    var mealColumnsWidth = width - (padding * 2) - dayColumnWidth;
    var breakfastColumnWidth = mealColumnsWidth / 3;
    var lunchColumnWidth = mealColumnsWidth / 3;
    var dinnerColumnWidth = mealColumnsWidth - breakfastColumnWidth - lunchColumnWidth;
    var breakfastX = padding + dayColumnWidth;
    var lunchX = breakfastX + breakfastColumnWidth;
    var dinnerX = lunchX + lunchColumnWidth;
    var rowHeight = Math.Max(10, (height - tableTop - padding) / (snapshot.Days.Count + 1));
    var maxDayCharacters = EstimateMealPlanColumnCharacters(dayColumnWidth);
    var maxBreakfastCharacters = EstimateMealPlanColumnCharacters(breakfastColumnWidth);
    var maxLunchCharacters = EstimateMealPlanColumnCharacters(lunchColumnWidth);
    var maxDinnerCharacters = EstimateMealPlanColumnCharacters(dinnerColumnWidth);
    var tableBottom = Math.Min(height - padding, tableTop + ((snapshot.Days.Count + 1) * rowHeight));
    var headerBaseline = tableTop + 10;

    var document = new JsonTemplateDocument()
        .Add(new JsonRotateCommand(tagType.GetJsonRotation()))
        .Add(new JsonRoundedBoxCommand(0, 0, width - 1, height - 1, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
        .Add(new JsonLineCommand(padding, tableTop - 3, width - padding, tableTop - 3, OeplJsonColor.Black))
        .Add(new JsonLineCommand(breakfastX, tableTop - 2, breakfastX, tableBottom - 1, OeplJsonColor.Black))
        .Add(new JsonLineCommand(lunchX, tableTop - 2, lunchX, tableBottom - 1, OeplJsonColor.Black))
        .Add(new JsonLineCommand(dinnerX, tableTop - 2, dinnerX, tableBottom - 1, OeplJsonColor.Black))
        .Add(new JsonTextCommand(padding + (dayColumnWidth / 2), headerBaseline, "Tag", font, OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(breakfastX + (breakfastColumnWidth / 2), headerBaseline, "Frueh", font, OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(lunchX + (lunchColumnWidth / 2), headerBaseline, "Mittag", font, OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        .Add(new JsonTextCommand(dinnerX + (dinnerColumnWidth / 2), headerBaseline, "Abend", font, OeplJsonColor.Black, OeplJsonTextAlignment.Center))
        .Add(new JsonLineCommand(padding, tableTop + rowHeight - 1, width - padding, tableTop + rowHeight - 1, OeplJsonColor.Black));

    for (var i = 0; i < snapshot.Days.Count; i++)
    {
        var day = snapshot.Days[i];
        var y = tableTop + ((i + 1) * rowHeight);
        var rowTop = y - 1;
        var rowFillHeight = Math.Max(1, rowHeight - 1);
        var cellFillHeight = Math.Max(1, rowFillHeight - 2);
        var textBaseline = y + 10;

        document
            .Add(new JsonBoxCommand(padding + 1, rowTop + 1, dayColumnWidth - 1, cellFillHeight, OeplJsonColor.White))
            .Add(new JsonBoxCommand(breakfastX + 1, rowTop + 1, breakfastColumnWidth - 1, cellFillHeight, OeplJsonColor.White))
            .Add(new JsonBoxCommand(lunchX + 1, rowTop + 1, lunchColumnWidth - 1, cellFillHeight, day.HasMissingMainMeal ? OeplJsonColor.Red : OeplJsonColor.White))
            .Add(new JsonBoxCommand(dinnerX + 1, rowTop + 1, dinnerColumnWidth - 1, cellFillHeight, day.HasMissingMainMeal ? OeplJsonColor.Red : OeplJsonColor.White));

        document
            .Add(new JsonTextCommand(padding + (dayColumnWidth / 2), textBaseline, TruncateWithEllipsis(day.Label, maxDayCharacters), font, day.HasMissingMainMeal ? OeplJsonColor.Red : OeplJsonColor.Black, OeplJsonTextAlignment.Center))
            .Add(new JsonTextCommand(breakfastX + (breakfastColumnWidth / 2), textBaseline, TruncateWithEllipsis(day.Breakfast, maxBreakfastCharacters), font, OeplJsonColor.Black, OeplJsonTextAlignment.Center))
            .Add(new JsonTextCommand(lunchX + (lunchColumnWidth / 2), textBaseline, TruncateWithEllipsis(day.Lunch, maxLunchCharacters), font, day.HasMissingMainMeal ? OeplJsonColor.White : OeplJsonColor.Black, OeplJsonTextAlignment.Center))
            .Add(new JsonTextCommand(dinnerX + (dinnerColumnWidth / 2), textBaseline, TruncateWithEllipsis(day.Dinner, maxDinnerCharacters), font, day.HasMissingMainMeal ? OeplJsonColor.White : OeplJsonColor.Black, OeplJsonTextAlignment.Center));

        if (i < snapshot.Days.Count - 1)
        {
            document.Add(new JsonLineCommand(padding, y + rowHeight - 1, width - padding, y + rowHeight - 1, OeplJsonColor.Black));
        }
    }

    await client.UploadJsonTemplateAsync(tag.Mac, document, cancellationToken);
}

static IReadOnlyList<ShoppingListDisplayLine> BuildShoppingListDisplayLines(IReadOnlyList<ShoppingListEntry> items)
{
    var lines = new List<ShoppingListDisplayLine>();
    string? currentRecipe = null;

    foreach (var item in items)
    {
        if (!item.HasRecipe)
        {
            currentRecipe = null;
            lines.Add(new ShoppingListDisplayLine(item.IngredientDisplayText, ShoppingListDisplayLineKind.StandaloneIngredient));
            continue;
        }

        if (!string.Equals(currentRecipe, item.RecipeName, StringComparison.CurrentCultureIgnoreCase))
        {
            currentRecipe = item.RecipeName;
            lines.Add(new ShoppingListDisplayLine(item.RecipeName!, ShoppingListDisplayLineKind.RecipeHeader));
        }

        lines.Add(new ShoppingListDisplayLine(item.IngredientDisplayText, ShoppingListDisplayLineKind.RecipeIngredient));
    }

    return lines;
}

static string TruncateWithEllipsis(string value, int maxLength)
{
    if (maxLength <= 3 || value.Length <= maxLength)
    {
        return value;
    }

    return value[..(maxLength - 3)].TrimEnd() + "...";
}

static int EstimateMealPlanColumnCharacters(int columnWidth)
{
    const int horizontalPadding = 6;
    const int averageCharacterWidth = 3;

    return Math.Max(4, (columnWidth - horizontalPadding) / averageCharacterWidth);
}

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

internal sealed record ShoppingListSnapshot(
    string Name,
    IReadOnlyList<ShoppingListEntry> Items,
    string StateSignature);

internal sealed record MealPlanSnapshot(
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<MealPlanDay> Days,
    int TotalMealCount,
    string StateSignature);

internal sealed record MealPlanDay(
    DateOnly Date,
    string Label,
    string Breakfast,
    string Lunch,
    string Dinner)
{
    public bool HasMissingMainMeal => Lunch == "-" && Dinner == "-";

    public int PlannedMealCount =>
        (Breakfast == "-" ? 0 : 1) +
        (Lunch == "-" ? 0 : 1) +
        (Dinner == "-" ? 0 : 1);
}

internal sealed record ShoppingListEntry(
    string IngredientDisplayText,
    string IngredientSortKey,
    string? RecipeName)
{
    public bool HasRecipe => !string.IsNullOrWhiteSpace(RecipeName);

    public string RecipeSortKey => RecipeName ?? string.Empty;

    public string DisplayText => HasRecipe
        ? $"{RecipeName}: {IngredientDisplayText}"
        : IngredientDisplayText;
}

internal sealed record ShoppingListDisplayLine(
    string Text,
    ShoppingListDisplayLineKind Kind);

internal enum ShoppingListDisplayLineKind
{
    StandaloneIngredient,
    RecipeHeader,
    RecipeIngredient,
    Truncation
}

internal enum DisplayRenderMode
{
    Json,
    Jpeg
}

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

internal sealed class MealieShoppingListResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("listItems")]
    public List<MealieShoppingListItem>? ListItems { get; init; }

    [JsonPropertyName("recipeReferences")]
    public List<MealieShoppingListRecipeReference>? RecipeReferences { get; init; }
}

internal sealed class MealieMealPlanResponse
{
    [JsonPropertyName("items")]
    public List<MealieMealPlanItem>? Items { get; init; }
}

internal sealed class MealieMealPlanItem
{
    [JsonPropertyName("date")]
    public string? Date { get; init; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("recipe")]
    public MealieRecipeSummary? Recipe { get; init; }
}

internal sealed class MealieShoppingListItem
{
    [JsonPropertyName("quantity")]
    public double? Quantity { get; init; }

    [JsonPropertyName("display")]
    public string? Display { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("checked")]
    public bool Checked { get; init; }

    [JsonPropertyName("position")]
    public int Position { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("unit")]
    public MealieUnit? Unit { get; init; }

    [JsonPropertyName("food")]
    public MealieFood? Food { get; init; }

    [JsonPropertyName("referencedRecipe")]
    public MealieRecipeSummary? ReferencedRecipe { get; init; }

    [JsonPropertyName("recipeReferences")]
    public List<MealieShoppingListItemRecipeReference>? RecipeReferences { get; init; }
}

internal sealed class MealieUnit
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("abbreviation")]
    public string? Abbreviation { get; init; }
}

internal sealed class MealieFood
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class MealieShoppingListRecipeReference
{
    [JsonPropertyName("recipeId")]
    public string? RecipeId { get; init; }

    [JsonPropertyName("recipe")]
    public MealieRecipeSummary? Recipe { get; init; }
}

internal sealed class MealieShoppingListItemRecipeReference
{
    [JsonPropertyName("recipeId")]
    public string? RecipeId { get; init; }

    [JsonPropertyName("recipe")]
    public MealieRecipeSummary? Recipe { get; init; }
}

internal sealed class MealieRecipeSummary
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
