using System.IO;
using System.Text.Json;

namespace SaludNotebook.Revisiones;

// Guarda un registro por día y las sesiones de "Modo juego", para ver la evolución.
static class Historial
{
    static readonly string Carpeta = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaludNotebook");
    static readonly string ArchivoRevisiones = Path.Combine(Carpeta, "historial.json");
    static readonly string ArchivoSesiones = Path.Combine(Carpeta, "sesiones-juego.json");

    const int MaxRevisiones = 400;
    const int MaxSesiones = 50;

    static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    public static List<RegistroHistorial> LeerRevisiones() => Leer<RegistroHistorial>(ArchivoRevisiones);
    public static List<SesionJuego> LeerSesiones() => Leer<SesionJuego>(ArchivoSesiones);

    public static void Guardar(ResultadoRevision r)
    {
        double? M(string seccion, string clave) =>
            r.Secciones.FirstOrDefault(s => s.Nombre == seccion)?.Metricas.TryGetValue(clave, out var v) == true ? v : null;

        var lista = LeerRevisiones();
        lista.RemoveAll(x => x.Fecha.Date == DateTime.Today);
        lista.Add(new RegistroHistorial
        {
            Fecha = DateTime.Now,
            SaludBateria = M("Batería", "salud"),
            CiclosBateria = M("Batería", "ciclos"),
            DiscoLibreGb = M("Disco", "libreGb"),
            DiscoLibrePct = M("Disco", "librePct"),
            DesgasteSsd = M("Disco", "desgaste"),
            TempSistema = M("Temperatura", "sistema"),
            TempGpu = M("Temperatura", "gpu"),
            RamUso = M("Rendimiento", "ram"),
            Estado = r.EstadoGeneral,
        });
        Escribir(ArchivoRevisiones, lista.OrderBy(x => x.Fecha).TakeLast(MaxRevisiones).ToList());
    }

    public static void GuardarSesion(SesionJuego sesion)
    {
        var lista = LeerSesiones();
        lista.Add(sesion);
        Escribir(ArchivoSesiones, lista.TakeLast(MaxSesiones).ToList());
    }

    static List<T> Leer<T>(string archivo)
    {
        try
        {
            return File.Exists(archivo)
                ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(archivo), Opciones) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    static void Escribir<T>(string archivo, List<T> lista)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(archivo, JsonSerializer.Serialize(lista, Opciones));
        }
        catch
        {
        }
    }
}
