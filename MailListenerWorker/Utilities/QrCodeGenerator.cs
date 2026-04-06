using System.Diagnostics;
using System.Text;

namespace MailListenerWorker.Utilities;

/// <summary>
/// Generates QR codes for ticket URLs using external qrcode.com API
/// Returns base64 encoded image data for embedding in HTML emails
/// </summary>
public static class QrCodeGenerator
{
    /// <summary>
    /// Generates a QR code for a ticket and returns it as a base64 data URI
    /// </summary>
    public static async Task<string> GenerateQrCodeBase64Async(string ticketUrl, int size = 200)
    {
        try
        {
            // Use qrcode.com API (simpler than qrserver for reliability)
            // Encode URL to be safe in query string
            var encodedUrl = Uri.EscapeDataString(ticketUrl);
            var qrApiUrl = $"https://api.qrserver.com/v1/create-qr-code/?size={size}x{size}&data={encodedUrl}";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetAsync(qrApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                return string.Empty; // Fail silently if QR generation fails
            }

            var imageBytes = await response.Content.ReadAsByteArrayAsync();
            var base64 = Convert.ToBase64String(imageBytes);
            return $"data:image/png;base64,{base64}";
        }
        catch
        {
            // If QR generation fails for any reason, return empty string
            // Email will still work without QR code
            return string.Empty;
        }
    }
}
