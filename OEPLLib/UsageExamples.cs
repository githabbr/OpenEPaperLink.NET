namespace OEPLLib;

public static class UsageExamples
{
    public static OpenEpaperLinkRoamingClient CreateRoamingClient(params string[] accessPointAddresses) =>
        new(
            accessPointAddresses.Select((address, index) =>
                new OpenEpaperLinkAccessPointRegistration($"ap-{index + 1}", address)));

    public static async Task RenderAndUploadJpegAsync(string apAddress, string mac, CancellationToken cancellationToken = default)
    {
        using var client = new OpenEpaperLinkClient(apAddress);
        await RenderAndUploadJpegAsync(client, mac, cancellationToken).ConfigureAwait(false);
    }

    public static async Task RenderUploadAndTraceJpegAsync(string apAddress, string mac, CancellationToken cancellationToken = default)
    {
        using var client = new OpenEpaperLinkClient(apAddress);

        var before = await client.GetTagDebugSnapshotAsync(mac, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tag '{mac}' was not found.");
        Console.WriteLine($"Before imgupload: {before}");

        await RenderAndUploadJpegAsync(client, mac, cancellationToken).ConfigureAwait(false);

        var after = await client.GetTagDebugSnapshotAsync(mac, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(after is null
            ? $"Immediate post-upload lookup could not find tag '{mac}'."
            : $"Immediately after imgupload: {after}");

        var changed = await client.WaitForTagStateChangeAsync(mac, before, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(changed is null
            ? "No pending/hash/config change observed during the debug polling window."
            : $"Observed tag state change: {changed}");
    }

    public static async Task RenderUploadAndTraceWhiteJpegAsync(string apAddress, string mac, CancellationToken cancellationToken = default)
    {
        using var client = new OpenEpaperLinkClient(apAddress);

        var before = await client.GetTagDebugSnapshotAsync(mac, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tag '{mac}' was not found.");
        Console.WriteLine($"Before white imgupload: {before}");

        await RenderAndUploadWhiteJpegAsync(client, mac, cancellationToken).ConfigureAwait(false);

        var after = await client.GetTagDebugSnapshotAsync(mac, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(after is null
            ? $"Immediate post-upload lookup could not find tag '{mac}'."
            : $"Immediately after white imgupload: {after}");

        var changed = await client.WaitForTagStateChangeAsync(mac, before, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        Console.WriteLine(changed is null
            ? "No pending/hash/config change observed during the white-image polling window."
            : $"Observed tag state change after white imgupload: {changed}");
    }

    private static async Task RenderAndUploadJpegAsync(OpenEpaperLinkClient client, string mac, CancellationToken cancellationToken)
    {
        var tag = await client.GetTagByMacAsync(mac, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tag '{mac}' was not found.");
        var tagType = await client.GetTagTypeAsync(tag.HardwareType, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No tag type metadata was found for hardware type {tag.HardwareType:X2}.");

        using var canvas = new OeplCanvas(tagType.Width, tagType.Height, OeplAccentColor.Red);
        canvas
            .DrawRectangle(0, 0, tagType.Width, tagType.Height, fill: "white", outline: "black", outlineWidth: 2)
            .DrawText("OpenEPaperLink", 10, 10, 22, "Bahnschrift", "black")
            .DrawText("System font + shapes + QR + barcode", 10, 40, 14, "Segoe UI", "red")
            .DrawLine(10, 62, tagType.Width - 10, 62, "black", 2)
            .DrawBarcode("1234567890", 10, 75, 180, 40)
            .DrawQrCode("https://openepaperlink.de", tagType.Width - 92, tagType.Height - 92, 82, 82)
            .QuantizeToDisplayPalette();

        await client.UploadRenderedImageAsync(mac, canvas, new OpenEpaperLinkImageUploadOptions(OpenEpaperLinkDitherMode.None), cancellationToken).ConfigureAwait(false);
    }

    private static async Task RenderAndUploadWhiteJpegAsync(OpenEpaperLinkClient client, string mac, CancellationToken cancellationToken)
    {
        var tag = await client.GetTagByMacAsync(mac, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tag '{mac}' was not found.");
        var tagType = await client.GetTagTypeAsync(tag.HardwareType, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No tag type metadata was found for hardware type {tag.HardwareType:X2}.");

        using var canvas = new OeplCanvas(tagType.Width, tagType.Height, OeplAccentColor.Red);
        canvas
            .DrawRectangle(0, 0, tagType.Width, tagType.Height, fill: "white", outline: "white", outlineWidth: 1)
            .QuantizeToDisplayPalette();

        await client.UploadRenderedImageAsync(
            mac,
            canvas,
            new OpenEpaperLinkImageUploadOptions(OpenEpaperLinkDitherMode.None, 100, 22),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task BuildAndUploadJsonAsync(string apAddress, string mac, CancellationToken cancellationToken = default)
    {
        using var client = new OpenEpaperLinkClient(apAddress);
        var assets = new JsonAssetPublisher(client);

        var document = new JsonTemplateDocument()
            .Add(new JsonRotateCommand(0))
            .Add(new JsonTextCommand(8, 24, "JSON Template", "fonts/bahnschrift20", OeplJsonColor.Black))
            .Add(new JsonBoxCommand(8, 34, 120, 24, OeplJsonColor.Red))
            .Add(new JsonTextCommand(68, 52, "Live", "fonts/bahnschrift20", OeplJsonColor.White, OeplJsonTextAlignment.Center))
            .Add(new JsonRoundedBoxCommand(140, 34, 148, 78, 10, OeplJsonColor.White, OeplJsonColor.Black, 2))
            .Add(new JsonTextBoxCommand(148, 44, 132, 58, "Native AP JSON for text and vector primitives.", "fonts/bahnschrift20", OeplJsonColor.Black, 1.15f));

        var qrAsset = await assets.UploadQrCodeAsync("/temp/example-qr.jpg", "https://openepaperlink.de", 72, 72, 8, 72, cancellationToken).ConfigureAwait(false);
        var barcodeAsset = await assets.UploadBarcodeAsync("/temp/example-barcode.jpg", "1234567890", 160, 40, 128, 120, OeplBarcodeType.Code128, cancellationToken).ConfigureAwait(false);

        document.Add(qrAsset).Add(barcodeAsset);

        await client.UploadJsonTemplateAsync(mac, document, cancellationToken).ConfigureAwait(false);
    }
}

