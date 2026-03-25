namespace LPM.Services;

/// <summary>
/// Sends SMS via GlobalSms.co.il (itnewsletter SAPI) using SOAP 1.1 over HttpClient.
/// Endpoint: https://sapi.itnewsletter.co.il/webservices/wssms.asmx
/// </summary>
public class SmsService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string EndpointUrl = "https://sapi.itnewsletter.co.il/webservices/wssms.asmx";
    private const string SoapAction  = "apiGlobalSms/sendSmsToRecipients";

    private string ApiKey     => configuration["Sms:ApiKey"]     ?? "";
    private string Originator => configuration["Sms:Originator"] ?? "";

    /// <summary>
    /// Sends an SMS. destination should be a local Israeli number (e.g. "0541234567")
    /// or an international format (e.g. "+972541234567"). Returns true on success.
    /// </summary>
    public async Task<bool> SendSmsAsync(string destination, string message)
    {
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(message))
            return false;

        // Normalize: strip leading + for the API (it accepts "972..." or "05...")
        var dest = destination.Trim();

        var soapBody = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"
                           xmlns:tns="apiGlobalSms">
              <soap:Body>
                <tns:sendSmsToRecipients>
                  <tns:ApiKey>{Escape(ApiKey)}</tns:ApiKey>
                  <tns:txtOriginator>{Escape(Originator)}</tns:txtOriginator>
                  <tns:destinations>{Escape(dest)}</tns:destinations>
                  <tns:txtSMSmessage>{Escape(message)}</tns:txtSMSmessage>
                  <tns:dteToDeliver></tns:dteToDeliver>
                  <tns:txtAddInf></tns:txtAddInf>
                </tns:sendSmsToRecipients>
              </soap:Body>
            </soap:Envelope>
            """;

        try
        {
            var client  = httpClientFactory.CreateClient("sms");
            var content = new StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", SoapAction);

            var response = await client.PostAsync(EndpointUrl, content);
            var body     = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"[SmsService] SMS to {dest}: HTTP {(int)response.StatusCode} | {body}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SmsService] Error sending SMS to {dest}: {ex.Message}");
            return false;
        }
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
