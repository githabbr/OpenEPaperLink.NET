namespace OEPLLib;

public sealed class JsonAssetPublisher
{
    private readonly OpenEpaperLinkClient _client;

    public JsonAssetPublisher(OpenEpaperLinkClient client)
    {
        _client = client;
    }

    public async Task<JsonImageCommand> UploadCanvasAsync(string littleFsPath, OeplCanvas canvas, int x, int y, int quality = 90, CancellationToken cancellationToken = default)
    {
        var jpegBytes = canvas.ToJpegBytes(quality);
        await _client.UploadLittleFsFileAsync(littleFsPath, jpegBytes, "image/jpeg", Path.GetFileName(littleFsPath), cancellationToken).ConfigureAwait(false);
        return new JsonImageCommand(littleFsPath, x, y);
    }

    public async Task<JsonImageCommand> UploadBarcodeAsync(string littleFsPath, string value, int width, int height, int x, int y, OeplBarcodeType type = OeplBarcodeType.Code128, CancellationToken cancellationToken = default)
    {
        using var canvas = new OeplCanvas(width, height);
        canvas.DrawBarcode(value, 0, 0, width, height, type).QuantizeToDisplayPalette();
        return await UploadCanvasAsync(littleFsPath, canvas, x, y, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonImageCommand> UploadQrCodeAsync(string littleFsPath, string value, int width, int height, int x, int y, CancellationToken cancellationToken = default)
    {
        using var canvas = new OeplCanvas(width, height);
        canvas.DrawQrCode(value, 0, 0, width, height).QuantizeToDisplayPalette();
        return await UploadCanvasAsync(littleFsPath, canvas, x, y, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
