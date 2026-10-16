using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace SaludNotebook.Revisiones;

// Tareas programadas de Windows: una para abrir la app sin pedir permiso (vía acceso directo)
// y otra para la revisión semanal automática.
static class Instalacion
{
    public const string TareaAbrir = @"SaludNotebook\Abrir";
    public const string TareaSemanal = @"SaludNotebook\Revision semanal";
    public const string ArgSemanal = "--semanal";
    public const string ArgConfigurar = "--configurar";
    public const string ArgInforme = "--informe";

    static readonly string CarpetaInstalada = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SaludNotebook");
    static readonly string ExeInstalado = Path.Combine(CarpetaInstalada, "SaludNotebook.exe");
    static readonly string MarcaNoInstalar = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaludNotebook", "no-instalar");

    static string ExeActual => Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró la app.");

    // Las tareas y accesos directos apuntan a la copia instalada si existe; si no, a la que está corriendo.
    static string Exe => File.Exists(ExeInstalado) ? ExeInstalado : ExeActual;
    static string Schtasks => Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    // Versión portátil: un solo .exe (single-file), que se puede copiar a otro PC.
    static bool EsPortatil => string.IsNullOrEmpty(typeof(Instalacion).Assembly.Location);

    // Se ofrece instalar cuando se abre el .exe portátil desde fuera de la carpeta de instalación
    // (ej. desde un pendrive o Descargas), salvo que el usuario ya haya dicho que no.
    public static bool ConvieneOfrecerInstalar() =>
        EsPortatil
        && !string.Equals(Path.GetFullPath(ExeActual), Path.GetFullPath(ExeInstalado), StringComparison.OrdinalIgnoreCase)
        && !File.Exists(MarcaNoInstalar);

    public static void NoVolverAPreguntar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarcaNoInstalar)!);
        File.WriteAllText(MarcaNoInstalar, "");
    }

    // Copia el .exe a %LOCALAPPDATA%\Programs\SaludNotebook y crea acceso directo + revisión semanal.
    public static void Instalar()
    {
        Directory.CreateDirectory(CarpetaInstalada);
        foreach (var p in Process.GetProcessesByName("SaludNotebook").Where(p => p.Id != Environment.ProcessId))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, ExeInstalado, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch
            {
            }
        }
        File.Copy(ExeActual, ExeInstalado, true);
        CrearTareaAbrir();
        CrearAccesosDirectos();
        CrearTareaSemanal();
    }

    public static bool Existe(string tarea) =>
        Ejecutar(Schtasks, $"/Query /TN \"{tarea}\"") == 0;

    public static void CrearTareaAbrir() => Crear(TareaAbrir, "Abre Salud del Notebook con permisos de administrador sin preguntar.", "", "");

    public static void CrearTareaSemanal()
    {
        // Próximo lunes a las 13:00; si el notebook estaba apagado, corre apenas se pueda.
        var dias = ((int)DayOfWeek.Monday - (int)DateTime.Today.DayOfWeek + 7) % 7;
        var inicio = DateTime.Today.AddDays(dias == 0 ? 7 : dias).AddHours(13);
        var disparador = $"""
            <Triggers>
              <CalendarTrigger>
                <StartBoundary>{inicio:yyyy-MM-ddTHH:mm:ss}</StartBoundary>
                <Enabled>true</Enabled>
                <ScheduleByWeek><DaysOfWeek><Monday /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>
              </CalendarTrigger>
            </Triggers>
            """;
        Crear(TareaSemanal, "Revisa la salud del notebook una vez a la semana y avisa si algo está mal.", disparador, ArgSemanal);
    }

    public static void Borrar(string tarea) => Ejecutar(Schtasks, $"/Delete /TN \"{tarea}\" /F");

    // Acceso directo en el escritorio y en el menú Inicio que abre la app a través de la tarea.
    public static void CrearAccesosDirectos()
    {
        if (!Existe(TareaAbrir)) CrearTareaAbrir();
        var destinos = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };
        var tipo = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript no disponible.");
        dynamic shell = Activator.CreateInstance(tipo)!;
        foreach (var carpeta in destinos)
        {
            dynamic acceso = shell.CreateShortcut(Path.Combine(carpeta, "Salud del Notebook.lnk"));
            acceso.TargetPath = Schtasks;
            acceso.Arguments = $"/Run /TN \"{TareaAbrir}\"";
            acceso.IconLocation = Exe + ",0";
            acceso.WindowStyle = 7; // minimizado: la consola de schtasks no se ve
            acceso.Description = "Revisa la salud de tu notebook";
            acceso.Save();
        }
    }

    public static bool HayAccesoDirecto() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Salud del Notebook.lnk"));

    static void Crear(string nombre, string descripcion, string disparadores, string argumentos)
    {
        var usuario = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{SecurityElement.Escape(descripcion)}</Description></RegistrationInfo>
              {disparadores}
              <Principals>
                <Principal id="Author">
                  <UserId>{usuario}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <StartWhenAvailable>true</StartWhenAvailable>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(Exe)}</Command>
                  <Arguments>{SecurityElement.Escape(argumentos)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
        var archivo = Path.Combine(Path.GetTempPath(), "salud-notebook-tarea.xml");
        File.WriteAllText(archivo, xml, Encoding.Unicode);
        try
        {
            if (Ejecutar(Schtasks, $"/Create /TN \"{nombre}\" /XML \"{archivo}\" /F") != 0)
                throw new InvalidOperationException("Windows no aceptó crear la tarea programada.");
        }
        finally
        {
            File.Delete(archivo);
        }
    }

    static int Ejecutar(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        return p.ExitCode;
    }
}
