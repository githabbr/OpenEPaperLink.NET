using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace OEPLLib;

public sealed class OpenEpaperLinkClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;

    public Action<string>? DebugLog { get; set; }

    public OpenEpaperLinkClient(string baseAddress)
        : this(new HttpClient { BaseAddress = NormalizeBaseAddress(baseAddress) }, true)
    {
    }

    public OpenEpaperLinkClient(Uri baseAddress)
        : this(new HttpClient { BaseAddress = NormalizeBaseAddress(baseAddress) }, true)
    {
    }

    public OpenEpaperLinkClient(HttpClient httpClient, bool disposeHttpClient = false)
    {
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<OpenEpaperLinkSystemInfo?> GetSystemInfoAsync(CancellationToken cancellationToken = default) =>
        await GetFromJsonAsync<OpenEpaperLinkSystemInfo>("sysinfo", cancellationToken).ConfigureAwait(false);

    public async Task<OpenEpaperLinkAccessPointConfig?> GetAccessPointConfigAsync(CancellationToken cancellationToken = default) =>
        await GetFromJsonAsync<OpenEpaperLinkAccessPointConfig>("get_ap_config", cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<OpenEpaperLinkTag>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<OpenEpaperLinkTag>();
        var position = 0;

        while (true)
        {
            var page = await GetFromJsonAsync<OpenEpaperLinkTagPage>($"get_db?pos={position}", cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                break;
            }

            results.AddRange(page.Tags);

            if (page.ContinuationPosition is null || page.ContinuationPosition <= position)
            {
                break;
            }

            position = page.ContinuationPosition.Value;
        }

        return results;
    }

    public async Task<OpenEpaperLinkTag?> GetTagByMacAsync(string mac, CancellationToken cancellationToken = default)
    {
        var tags = await GetAllTagsAsync(cancellationToken).ConfigureAwait(false);
        return tags.FirstOrDefault(tag => string.Equals(tag.Mac, mac, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OpenEpaperLinkTag?> GetTagByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        var tags = await GetAllTagsAsync(cancellationToken).ConfigureAwait(false);
        return tags.FirstOrDefault(tag => string.Equals(tag.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OpenEpaperLinkTagDebugSnapshot?> GetTagDebugSnapshotAsync(string mac, CancellationToken cancellationToken = default)
    {
        var tag = await GetTagByMacAsync(mac, cancellationToken).ConfigureAwait(false);
        return tag is null ? null : CreateTagDebugSnapshot(tag);
    }

    public async Task<OpenEpaperLinkTagDebugSnapshot?> WaitForTagStateChangeAsync(
        string mac,
        OpenEpaperLinkTagDebugSnapshot baseline,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            var current = await GetTagDebugSnapshotAsync(mac, cancellationToken).ConfigureAwait(false);
            if (current is not null && HasTagStateChanged(baseline, current))
            {
                Debug($"[{DateTime.Now:HH:mm:ss}] Observed tag state change for {mac} after {attempt} poll(s): {current}");
                return current;
            }
        }

        Debug($"[{DateTime.Now:HH:mm:ss}] No tag state change observed for {mac} within {timeout.TotalSeconds:0} seconds.");
        return null;
    }

    public async Task<OpenEpaperLinkTagType?> GetTagTypeAsync(int hardwareType, CancellationToken cancellationToken = default) =>
        await GetFromJsonAsync<OpenEpaperLinkTagType>($"tagtypes/{hardwareType:X2}.json", cancellationToken).ConfigureAwait(false);

    public async Task UploadJpegAsync(string mac, byte[] fileBytes, OpenEpaperLinkImageUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        var effectiveOptions = NormalizeImageUploadOptions(options);
        var dither = effectiveOptions.Dither ?? OpenEpaperLinkDitherMode.None;
        var contentMode = effectiveOptions.ContentMode;
        var rotate = effectiveOptions.Rotate;
        var multipart = BuildBrowserLikeImageUploadMultipart(mac, ((int)dither).ToString(), contentMode, rotate, fileBytes);
        using var content = new ByteArrayContent(multipart.BodyBytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={multipart.Boundary}");
        LogMultipartBody("POST", "imgupload", content.Headers.ContentType, multipart.BodyBytes);

        using var response = await SendAsync(
            "POST",
            "imgupload",
            $"mac={mac}, fileBytes={fileBytes.Length}, dither={(int)dither}, contentMode={contentMode?.ToString() ?? "<none>"}, rotate={rotate?.ToString() ?? "<none>"}",
            () => _httpClient.PostAsync("imgupload", content, cancellationToken)).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await LogResponseBodyAsync("POST", "imgupload", response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadRenderedImageAsync(string mac, OeplCanvas canvas, OpenEpaperLinkImageUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        var effectiveOptions = NormalizeImageUploadOptions(options);
        var jpeg = canvas.ToJpegBytes(effectiveOptions.JpegQuality);
        await UploadJpegAsync(mac, jpeg, effectiveOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadJsonTemplateAsync(string mac, JsonTemplateDocument document, CancellationToken cancellationToken = default) =>
        await UploadJsonTemplateAsync(mac, document.ToJson(), cancellationToken).ConfigureAwait(false);

    public async Task UploadJsonTemplateAsync(string mac, string json, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("mac", mac),
            KeyValuePair.Create("json", json)
        ]);

        using var response = await SendAsync(
            "POST",
            "jsonupload",
            $"mac={mac}, jsonLength={json.Length}, jsonPreview={CreatePreview(json)}",
            () => _httpClient.PostAsync("jsonupload", content, cancellationToken)).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadLittleFsFileAsync(string littleFsPath, byte[] fileBytes, string contentType = "application/octet-stream", string? fileName = null, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(littleFsPath, Encoding.UTF8), "path" }
        };

        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(fileContent, "file", fileName ?? Path.GetFileName(littleFsPath));

        using var response = await SendAsync(
            "POST",
            "littlefs_put",
            $"path={littleFsPath}, bytes={fileBytes.Length}, contentType={contentType}",
            () => _httpClient.PostAsync("littlefs_put", content, cancellationToken)).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveTagConfigurationAsync(OpenEpaperLinkTagConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(configuration.Mac, Encoding.UTF8), "mac" },
            { new StringContent(configuration.Alias ?? string.Empty, Encoding.UTF8), "alias" },
            { new StringContent(configuration.ContentMode.ToString(), Encoding.UTF8), "contentmode" },
            { new StringContent(configuration.ModeConfigurationJson, Encoding.UTF8), "modecfgjson" },
            { new StringContent(configuration.Rotate.ToString(), Encoding.UTF8), "rotate" },
            { new StringContent(configuration.Lut.ToString(), Encoding.UTF8), "lut" },
            { new StringContent(configuration.Invert.ToString(), Encoding.UTF8), "invert" }
        };

        using var response = await SendAsync(
            "POST",
            "save_cfg",
            $"mac={configuration.Mac}, contentMode={configuration.ContentMode}, rotate={configuration.Rotate}, lut={configuration.Lut}, invert={configuration.Invert}, modecfgjson={CreatePreview(configuration.ModeConfigurationJson)}",
            () => _httpClient.PostAsync("save_cfg", content, cancellationToken)).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<T?> GetFromJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            "GET",
            path,
            "json request",
            () => _httpClient.GetAsync(path, cancellationToken)).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(responseStream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string details, Func<Task<HttpResponseMessage>> sender)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await sender().ConfigureAwait(false);
            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                Debug($"[{DateTime.Now:HH:mm:ss}] {method} {path} completed in {stopwatch.ElapsedMilliseconds} ms with {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return response;
        }
        catch (TaskCanceledException ex)
        {
            Debug($"[{DateTime.Now:HH:mm:ss}] {method} {path} canceled after {stopwatch.ElapsedMilliseconds} ms. HttpClient.Timeout={_httpClient.Timeout}. Details: {details}");
            throw new TimeoutException($"OpenEPaperLink request {method} {path} timed out after {stopwatch.ElapsedMilliseconds} ms. HttpClient.Timeout={_httpClient.Timeout}. Details: {details}", ex);
        }
        catch (Exception ex)
        {
            Debug($"[{DateTime.Now:HH:mm:ss}] {method} {path} failed after {stopwatch.ElapsedMilliseconds} ms. {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var requestUri = response.RequestMessage?.RequestUri?.ToString() ?? "<unknown>";
        throw new HttpRequestException($"OpenEPaperLink request to {requestUri} failed with {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
    }

    private async Task LogResponseBodyAsync(string method, string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (DebugLog is null || response.Content is null)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        Debug($"[{DateTime.Now:HH:mm:ss}] {method} {path} response body: {CreatePreview(body)}");
    }

    private void LogMultipartBody(string method, string path, MediaTypeHeaderValue? contentType, byte[] bodyBytes)
    {
        if (DebugLog is null)
        {
            return;
        }

        var bodyText = Encoding.UTF8.GetString(bodyBytes);
        Debug($"[{DateTime.Now:HH:mm:ss}] {method} {path} multipart payload prepared ({bodyBytes.Length} bytes, {contentType}). Preview: {CreatePreview(bodyText)}");
    }

    private void Debug(string message) => DebugLog?.Invoke(message);

    private static OpenEpaperLinkImageUploadOptions NormalizeImageUploadOptions(OpenEpaperLinkImageUploadOptions? options) =>
        (options ?? new OpenEpaperLinkImageUploadOptions()) with
        {
            ContentMode = options?.ContentMode ?? 22,
            Rotate = options?.Rotate ?? 0
        };

    private static MultipartPayload BuildBrowserLikeImageUploadMultipart(string mac, string dither, int? contentMode, int? rotate, byte[] fileBytes)
    {
        var boundary = "----WebKitFormBoundary" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        using var stream = new MemoryStream();

        WriteFormField(stream, boundary, "mac", mac);
        WriteFormField(stream, boundary, "dither", dither);
        if (contentMode is not null)
        {
            WriteFormField(stream, boundary, "contentmode", contentMode.Value.ToString());
        }

        if (rotate is not null)
        {
            WriteFormField(stream, boundary, "rotate", rotate.Value.ToString());
        }

        WriteAscii(stream, $"--{boundary}\r\n");
        WriteAscii(stream, "Content-Disposition: form-data; name=\"file\"; filename=\"image.jpg\"\r\n");
        WriteAscii(stream, "Content-Type: image/jpeg\r\n\r\n");
        stream.Write(fileBytes, 0, fileBytes.Length);
        WriteAscii(stream, "\r\n");
        WriteAscii(stream, $"--{boundary}--\r\n");

        return new MultipartPayload(boundary, stream.ToArray());
    }

    private static void WriteFormField(Stream stream, string boundary, string name, string value)
    {
        WriteAscii(stream, $"--{boundary}\r\n");
        WriteAscii(stream, $"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
        WriteUtf8(stream, value);
        WriteAscii(stream, "\r\n");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUtf8(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private sealed record MultipartPayload(string Boundary, byte[] BodyBytes);

    private static OpenEpaperLinkTagDebugSnapshot CreateTagDebugSnapshot(OpenEpaperLinkTag tag) =>
        new(tag.Mac, tag.ContentMode, tag.Pending, tag.Hash, tag.Rotate, tag.Lut, tag.Invert);

    private static bool HasTagStateChanged(OpenEpaperLinkTagDebugSnapshot baseline, OpenEpaperLinkTagDebugSnapshot current) =>
        baseline.Pending != current.Pending ||
        !string.Equals(baseline.Hash, current.Hash, StringComparison.OrdinalIgnoreCase) ||
        baseline.ContentMode != current.ContentMode ||
        baseline.Rotate != current.Rotate ||
        baseline.Lut != current.Lut ||
        baseline.Invert != current.Invert;

    private static Uri NormalizeBaseAddress(string baseAddress) =>
        NormalizeBaseAddress(new Uri(baseAddress, UriKind.Absolute));

    private static string CreatePreview(string value)
    {
        const int maxLength = 180;
        var compact = value.Replace("\r", "\\r").Replace("\n", "\\n");
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static Uri NormalizeBaseAddress(Uri baseAddress)
    {
        var normalized = baseAddress.ToString().TrimEnd('/') + "/";
        return new Uri(normalized, UriKind.Absolute);
    }
}

