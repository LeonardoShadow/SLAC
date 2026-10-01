using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var supabaseUrl = "https://dvrwtqgicjtfahqxyldh.supabase.co";
        var serviceKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_KEY") 
            ?? "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImR2cnd0cWdpY2p0ZmFocXh5bGRoIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc3MjU4NjkxMCwiZXhwIjoyMDg4MTYyOTEwfQ.dM-359X8d9zH0k8F4q1A1x1y5X7_3u713Lg-1F9Qv18"; // fallback if in appsettings

        // Let's read appsettings.json to get the exact key
        var jsonText = await System.IO.File.ReadAllTextAsync("SLAC/appsettings.json");
        using var doc = JsonDocument.Parse(jsonText);
        var sbSection = doc.RootElement.GetProperty("Supabase");
        var url = sbSection.GetProperty("Url").GetString();
        var key = sbSection.GetProperty("Key").GetString();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("apikey", key);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        Console.WriteLine($"Conectando a Supabase: {url}...");

        // 1. Materias
        var resMaterias = await client.GetStringAsync($"{url}/rest/v1/materia?select=id,nombre,codigo,docente_id,institucion_id,activo");
        Console.WriteLine($"[MATERIAS]:\n{resMaterias}\n");

        // 2. Docentes
        var resDocentes = await client.GetStringAsync($"{url}/rest/v1/docente?select=id,nombre,email,codigo_docente,activo");
        Console.WriteLine($"[DOCENTES]:\n{resDocentes}\n");

        // 3. Estudiantes suscritos
        var resEstudiantes = await client.GetStringAsync($"{url}/rest/v1/suscripcion?select=count", new HttpCompletionOption());
        Console.WriteLine($"[SUSCRIPCIONES]: {resEstudiantes}\n");

        // 4. Sesiones recientes o futuras
        var resSesiones = await client.GetStringAsync($"{url}/rest/v1/sesion_asistencia?select=id,materia_id,estado,fecha,hora_inicio,hora_fin&order=fecha.desc&limit=5");
        Console.WriteLine($"[SESIONES RECIENTES]:\n{resSesiones}\n");
    }
}
