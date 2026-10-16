using System.Windows;
using SaludNotebook.Revisiones;

namespace SaludNotebook;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Revisión semanal (desde la tarea programada): sin ventana salvo que haya problemas.
        if (e.Args.Contains(Instalacion.ArgSemanal))
        {
            var r = await Revisor.RevisarAsync();
            Historial.Guardar(r);
            if (r.Secciones.Any(s => s.Estado == Estado.Problema))
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                new Aviso(r).Show();
            }
            else
            {
                Shutdown();
            }
            return;
        }

        // Generar el informe PDF sin abrir la ventana: SaludNotebook.exe --informe "C:\ruta\informe.pdf"
        var iInforme = Array.IndexOf(e.Args, Instalacion.ArgInforme);
        if (iInforme >= 0 && iInforme + 1 < e.Args.Length)
        {
            var r = await Revisor.RevisarAsync();
            Informe.Generar(r, new List<ActualizacionPendiente>(), Historial.LeerSesiones(), e.Args[iInforme + 1]);
            Shutdown();
            return;
        }

        // Primera instalación: acceso directo sin permisos + revisión semanal.
        if (e.Args.Contains(Instalacion.ArgConfigurar))
        {
            try
            {
                Instalacion.CrearTareaAbrir();
                Instalacion.CrearAccesosDirectos();
                Instalacion.CrearTareaSemanal();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo terminar la configuración: " + ex.Message, "Salud del Notebook");
            }
        }

        // Versión portátil abierta en un PC nuevo: ofrecer instalarla.
        else if (Instalacion.ConvieneOfrecerInstalar())
        {
            var r = MessageBox.Show(
                "¿Quieres instalar Salud del Notebook en este PC?\n\n" +
                "Se crea un acceso directo en el escritorio que la abre sin pedir permiso, y una revisión automática cada lunes.\n\n" +
                "Si eliges No, igual puedes usarla ahora desde este archivo.",
                "Salud del Notebook", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                try
                {
                    Instalacion.Instalar();
                    MessageBox.Show("¡Listo! Desde ahora ábrela con el ícono \"Salud del Notebook\" del escritorio.",
                        "Salud del Notebook", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("No se pudo instalar: " + ex.Message, "Salud del Notebook");
                }
            }
            else
            {
                Instalacion.NoVolverAPreguntar();
            }
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        new MainWindow().Show();
    }
}
