using QRCoder;

namespace Doctor.Web.Services;

public interface IBarcodeService
{
    string GenerateQrCodeBase64(string payload);
}

public class BarcodeService : IBarcodeService
{
    public string GenerateQrCodeBase64(string payload)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeAsPngByteArr = qrCode.GetGraphic(20);
            return $"data:image/png;base64,{Convert.ToBase64String(qrCodeAsPngByteArr)}";
        }
        catch
        {
            return string.Empty;
        }
    }
}
