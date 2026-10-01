using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var baseUrl = "http://slac-asistencia.runasp.net";
        Console.WriteLine($"[1] Testing Live Site Reachability: {baseUrl}...");

        try
        {
            var homeRes = await client.GetAsync(baseUrl);
            Console.WriteLine($"[HOME] Status: {(int)homeRes.StatusCode} {homeRes.StatusCode}");
            var homeHtml = await homeRes.Content.ReadAsStringAsync();
            Console.WriteLine($"[HOME] HTML Length: {homeHtml.Length} chars");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HOME ERROR] {ex.Message}");
        }

        try
        {
            var loginRes = await client.GetAsync($"{baseUrl}/login");
            Console.WriteLine($"[LOGIN] Status: {(int)loginRes.StatusCode} {loginRes.StatusCode}");
            var loginHtml = await loginRes.Content.ReadAsStringAsync();
            Console.WriteLine($"[LOGIN] Contains 'Docente': {loginHtml.Contains("Docente")}");
            Console.WriteLine($"[LOGIN] Contains 'Estudiante': {loginHtml.Contains("Estudiante")}");
            Console.WriteLine($"[LOGIN] Contains 'Administrador': {loginHtml.Contains("Administrador")}");
            Console.WriteLine($"[LOGIN] Contains 'mud-': {loginHtml.Contains("mud-")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOGIN ERROR] {ex.Message}");
        }

        // Test student scan invalid or test session
        try
        {
            var fakeSessionId = Guid.NewGuid();
            var scanRes = await client.GetAsync($"{baseUrl}/a/{fakeSessionId}");
            Console.WriteLine($"[STUDENT SCAN /a/id] Status: {(int)scanRes.StatusCode} {scanRes.StatusCode}");
            var scanHtml = await scanRes.Content.ReadAsStringAsync();
            Console.WriteLine($"[STUDENT SCAN] Contains error or registration: {scanHtml.Length} chars");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCAN ERROR] {ex.Message}");
        }
    }
}
