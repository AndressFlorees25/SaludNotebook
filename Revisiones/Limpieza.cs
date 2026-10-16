using System.IO;
using System.Runtime.InteropServices;

namespace SaludNotebook.Revisiones;

// Busca archivos que se pueden borrar sin riesgo y los elimina.
static class Limpieza
{
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<ElementoLimpieza> Analizar()
    {
        var lista = new List<ElementoLimpieza>
        {
            Carpetas("Archivos temporales", "Archivos que los programas dejan mientras trabajan. Se pueden borrar sin problema.",
                true, Path.GetTempPath(), Path.Combine(Windows, "Temp")),
            Papelera(),
            Carpetas("Descargas de Windows Update", "Instaladores de actualizaciones que ya se aplicaron.",
                true, Path.Combine(Windows, "SoftwareDistribution", "Download")),
            Carpetas("Caché de Optimización de distribución", "Pedazos de actualizaciones guardados para compartir con otros PCs.",
                true, Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")),
            Carpetas("Informes de errores", "Informes que Windows guarda cuando un programa se cierra solo.",
                true, @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue",
                Path.Combine(Local, "CrashDumps")),
            CacheGraficos(),
            Carpetas("Archivos de pantallazos azules", "La pestaña \"Fallos\" los usa para saber qué causó cada pantallazo. Bórralos solo si ya no te interesan.",
                false, Path.Combine(Windows, "Minidump"), Path.Combine(Windows, "LiveKernelReports")),
        };
        return lista;
    }

    // Normalmente pesa unos pocos GB; si creció demasiado, conviene borrarla aunque los juegos
    // tengan que volver a prepararla.
    const long CacheExagerada = 10L << 30;

    static ElementoLimpieza CacheGraficos()
    {
        var c = Carpetas("Caché de gráficos de juegos", "", false,
            Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"),
            Path.Combine(Local, @"AMD\DxCache"), Path.Combine(Local, @"AMD\DxcCache"));
        var exagerada = c.Bytes > CacheExagerada;
        return new ElementoLimpieza
        {
            Nombre = c.Nombre,
            Bytes = c.Bytes,
            Limpiar = c.Limpiar,
            Seleccionado = exagerada,
            Descripcion = exagerada
                ? "¡Está muy grande! Normalmente pesa unos pocos GB. Se vuelve a crear sola: los juegos pueden ir un poco a tirones la primera vez que los abras. " +
                  "Para que no vuelva a crecer tanto: Panel de control de NVIDIA > Configuración 3D > Tamaño de caché del sombreador > 10 GB."
                : "Se vuelve a crear sola, pero los juegos pueden ir un poco a tirones la primera vez. Bórrala solo si te falta mucho espacio.",
        };
    }

    public static long TamanoDescargas()
    {
        var descargas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Tamano(descargas);
    }

    static ElementoLimpieza Carpetas(string nombre, string descripcion, bool seleccionado, params string[] rutas) => new()
    {
        Nombre = nombre,
        Descripcion = descripcion,
        Seleccionado = seleccionado,
        Bytes = rutas.Sum(Tamano),
        Limpiar = () => rutas.Sum(Vaciar),
    };

    static readonly EnumerationOptions Todo = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    static long Tamano(string ruta)
    {
        try
        {
            if (!Directory.Exists(ruta)) return 0;
            return new DirectoryInfo(ruta).EnumerateFiles("*", Todo).Sum(f =>
            {
                try { return f.Length; } catch { return 0L; }
            });
        }
        catch
        {
            return 0;
        }
    }

    // Borra el contenido de la carpeta (no la carpeta). Los archivos en uso se saltan.
    static long Vaciar(string ruta)
    {
        if (!Directory.Exists(ruta)) return 0;
        long liberado = 0;
        var dir = new DirectoryInfo(ruta);
        foreach (var f in dir.EnumerateFiles("*", Todo))
        {
            try
            {
                var largo = f.Length;
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
                liberado += largo;
            }
            catch
            {
            }
        }
        foreach (var d in dir.EnumerateDirectories("*", Todo).OrderByDescending(d => d.FullName.Length))
        {
            try { d.Delete(false); } catch { }
        }
        return liberado;
    }

    // Papelera de reciclaje, a través de la API de Windows.
    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    const uint SinConfirmar = 0x1, SinProgreso = 0x2, SinSonido = 0x4;

    static long TamanoPapelera()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    static ElementoLimpieza Papelera() => new()
    {
        Nombre = "Papelera de reciclaje",
        Descripcion = "Archivos que ya borraste. Al vaciarla no se pueden recuperar.",
        Seleccionado = true,
        Bytes = TamanoPapelera(),
        Limpiar = () =>
        {
            var antes = TamanoPapelera();
            SHEmptyRecycleBin(IntPtr.Zero, null, SinConfirmar | SinProgreso | SinSonido);
            return antes - TamanoPapelera();
        },
    };
}
