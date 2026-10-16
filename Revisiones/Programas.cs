using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SaludNotebook.Revisiones;

// Programas abiertos (para cerrar los que no se usan) y programas que arrancan con Windows.
static class Programas
{
    // Procesos que nunca se ofrecen para cerrar: Windows, antivirus, drivers y esta misma app.
    static readonly HashSet<string> Protegidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso", "svchost",
        "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "conhost", "RuntimeBroker", "SearchHost", "SearchIndexer",
        "StartMenuExperienceHost", "ShellExperienceHost", "ShellHost", "TextInputHost", "ApplicationFrameHost",
        "MsMpEng", "NisSrv", "MpDefenderCoreService", "SecurityHealthService", "SecurityHealthSystray", "smartscreen",
        "audiodg", "spoolsv", "WmiPrvSE", "dllhost", "taskhostw", "Memory Compression", "SgrmBroker", "unsecapp",
        "AggregatorHost", "WUDFHost", "dasHost", "LockApp", "UserOOBEBroker", "SystemSettingsBroker", "msedgewebview2",
        "nvcontainer", "NVDisplay.Container", "NVIDIA Web Helper", "NahimicSvc64", "NahimicSvc32", "RtkAudUService64",
        "SaludNotebook", "WindowsTerminal", "OpenConsole", "claude", "node", "bun", "powershell", "pwsh", "cmd",
        "CrossDeviceService",
    };

    public static List<ProgramaAbierto> ListarAbiertos()
    {
        var sesion = Process.GetCurrentProcess().SessionId;
        var procesos = Process.GetProcesses().Where(p => p.SessionId == sesion && !Protegidos.Contains(p.ProcessName)).ToList();

        // Uso de procesador: dos mediciones separadas por 1,5 segundos.
        var antes = new Dictionary<int, TimeSpan>();
        foreach (var p in procesos)
        {
            try { antes[p.Id] = p.TotalProcessorTime; } catch { }
        }
        Thread.Sleep(1500);

        var lista = new List<ProgramaAbierto>();
        foreach (var grupo in procesos.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            double ram = 0, cpu = 0;
            var conVentana = false;
            string? ruta = null, descripcion = null;
            foreach (var p in grupo)
            {
                try
                {
                    p.Refresh();
                    ram += p.WorkingSet64 / 1024.0 / 1024;
                    if (antes.TryGetValue(p.Id, out var t0))
                        cpu += (p.TotalProcessorTime - t0).TotalMilliseconds / 1500 / Environment.ProcessorCount * 100;
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0) conVentana = true;
                    if (ruta == null)
                    {
                        ruta = p.MainModule?.FileName;
                        descripcion = p.MainModule?.FileVersionInfo.FileDescription;
                    }
                }
                catch
                {
                }
            }

            // Solo programas del usuario: con ventana, o en segundo plano fuera de Windows y gastando memoria.
            var deWindows = ruta != null && ruta.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
            if (!conVentana && (deWindows || ram < 50)) continue;

            lista.Add(new ProgramaAbierto
            {
                Nombre = string.IsNullOrWhiteSpace(descripcion) ? grupo.Key : descripcion.Trim(),
                Proceso = grupo.Key,
                Ids = grupo.Select(p => p.Id).ToList(),
                RamMb = ram,
                Cpu = cpu,
                TieneVentana = conVentana,
                Seleccionado = cpu < 1,
            });
        }
        return lista.OrderByDescending(p => p.RamMb).ToList();
    }

    // Las ventanas se cierran "con cortesía" (si hay algo sin guardar, el programa pregunta).
    public static string Cerrar(ProgramaAbierto programa)
    {
        var procesos = programa.Ids.Select(id => { try { return Process.GetProcessById(id); } catch { return null; } })
                                   .Where(p => p != null).Cast<Process>().ToList();
        foreach (var p in procesos)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow();
                else p.Kill();
            }
            catch
            {
            }
        }

        Thread.Sleep(2500);
        var siguen = procesos.Count(p => { try { return !p.HasExited; } catch { return false; } });
        if (siguen == 0) return "Cerrado ✔";
        return programa.TieneVentana ? "Sigue abierto (puede estar preguntando si guardar)" : "No se pudo cerrar";
    }

    // ---- Programas de inicio ----

    const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string Aprobados = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    const string TareasApps = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    public static List<ProgramaInicio> ListarInicio()
    {
        var lista = new List<ProgramaInicio>();

        void DesdeRegistro(RegistryKey raiz, string ruta, string aprobado, string origen)
        {
            using var k = raiz.OpenSubKey(ruta);
            if (k == null) return;
            foreach (var nombre in k.GetValueNames().Where(n => n.Length > 0))
                lista.Add(ConAprobacion(raiz, aprobado, nombre, nombre, k.GetValue(nombre)?.ToString() ?? "", origen));
        }

        void DesdeCarpeta(string carpeta, RegistryKey raiz, string origen)
        {
            if (!Directory.Exists(carpeta)) return;
            foreach (var f in Directory.GetFiles(carpeta).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                lista.Add(ConAprobacion(raiz, "StartupFolder", Path.GetFileName(f), Path.GetFileNameWithoutExtension(f), f, origen));
        }

        try { DesdeRegistro(Registry.CurrentUser, Run, "Run", "Tu usuario"); } catch { }
        try { DesdeRegistro(Registry.LocalMachine, Run, "Run", "Todos los usuarios"); } catch { }
        try { DesdeRegistro(Registry.LocalMachine, Run32, "Run32", "Todos los usuarios"); } catch { }
        try { DesdeCarpeta(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "Carpeta Inicio"); } catch { }
        try { DesdeCarpeta(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "Carpeta Inicio (todos)"); } catch { }
        try { DesdeAppsTienda(lista); } catch { }

        return lista.OrderBy(p => p.Nombre, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Igual que el Administrador de tareas: el primer byte del valor en StartupApproved dice si está activo.
    static ProgramaInicio ConAprobacion(RegistryKey raiz, string subclave, string valor, string nombre, string comando, string origen)
    {
        var ruta = Aprobados + subclave;
        bool activo;
        using (var k = raiz.OpenSubKey(ruta))
            activo = k?.GetValue(valor) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;

        var p = new ProgramaInicio
        {
            Nombre = nombre,
            Comando = comando,
            Origen = origen,
            Aplicar = encender =>
            {
                using var k = raiz.CreateSubKey(ruta, true);
                var datos = new byte[12];
                datos[0] = encender ? (byte)0x02 : (byte)0x03;
                if (!encender) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(datos, 4);
                k.SetValue(valor, datos, RegistryValueKind.Binary);
            },
        };
        p.Cargar(activo);
        return p;
    }

    // Apps de la Tienda (ej. Enlace Móvil): State 2 = activa, 1 = desactivada por el usuario.
    static void DesdeAppsTienda(List<ProgramaInicio> lista)
    {
        using var raiz = Registry.CurrentUser.OpenSubKey(TareasApps);
        if (raiz == null) return;
        foreach (var paquete in raiz.GetSubKeyNames())
        {
            using var kp = raiz.OpenSubKey(paquete);
            if (kp == null) continue;
            var tareas = kp.GetSubKeyNames();
            foreach (var tarea in tareas)
            {
                var ruta = $@"{TareasApps}\{paquete}\{tarea}";
                using var kt = Registry.CurrentUser.OpenSubKey(ruta);
                if (kt?.GetValue("State") is not int estado) continue;
                if (estado is 3 or 4) continue; // controladas por directiva, no se pueden cambiar

                var p = new ProgramaInicio
                {
                    // Si la app tiene varias tareas de inicio, se agrega cuál es para distinguirlas.
                    Nombre = NombrePaquete(paquete) + (tareas.Length > 1 ? $" ({tarea})" : ""),
                    Comando = paquete,
                    Origen = "App de la Tienda",
                    Aplicar = encender =>
                    {
                        using var k = Registry.CurrentUser.OpenSubKey(ruta, true)
                                      ?? throw new InvalidOperationException("La app ya no existe.");
                        k.SetValue("State", encender ? 2 : 1, RegistryValueKind.DWord);
                    },
                };
                p.Cargar(estado == 2);
                lista.Add(p);
            }
        }
    }

    static string NombrePaquete(string familia)
    {
        var nombre = familia.Split('_')[0];
        var conocido = nombre switch
        {
            "Microsoft.YourPhone" => "Enlace Móvil (Phone Link)",
            "MicrosoftTeams" or "MSTeams" => "Microsoft Teams",
            "Microsoft.WindowsTerminal" => "Terminal de Windows",
            "SpotifyAB.SpotifyMusic" => "Spotify",
            "5319275A.WhatsAppDesktop" => "WhatsApp",
            "Microsoft.GamingApp" => "Xbox",
            "E046963F.LenovoCompanion" => "Lenovo Vantage",
            _ => null,
        };
        return conocido ?? nombre.Split('.').Last();
    }
}
