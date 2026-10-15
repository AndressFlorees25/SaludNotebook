using System.Diagnostics;
using System.Management;

namespace SaludNotebook.Revisiones;

static class Wmi
{
    public static List<ManagementObject> Consultar(string consulta, string ns = @"root\cimv2")
    {
        using var buscador = new ManagementObjectSearcher(ns, consulta);
        return buscador.Get().Cast<ManagementObject>().ToList();
    }

    public static ManagementObject? Primero(string consulta, string ns = @"root\cimv2") =>
        Consultar(consulta, ns).FirstOrDefault();

    public static string Texto(ManagementBaseObject o, string prop) =>
        (o[prop]?.ToString() ?? "").Trim();

    public static long Numero(ManagementBaseObject o, string prop) =>
        o[prop] is { } v ? Convert.ToInt64(v) : 0;

    public static DateTime? Fecha(ManagementBaseObject o, string prop)
    {
        try
        {
            return o[prop] is string s && s.Length >= 8 ? ManagementDateTimeConverter.ToDateTime(s) : null;
        }
        catch
        {
            return null;
        }
    }

    // Ejecuta un programa de consola y devuelve su salida (o null si falla).
    public static string? Ejecutar(string exe, string args, int timeoutMs = 30000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p == null) return null;
            var salida = p.StandardOutput.ReadToEnd();
            p.WaitForExit(timeoutMs);
            return p.ExitCode == 0 ? salida : null;
        }
        catch
        {
            return null;
        }
    }
}
