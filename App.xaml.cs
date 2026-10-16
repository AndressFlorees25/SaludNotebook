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

        ShutdownMode = ShutdownMode.OnLastWindowClose;
        new MainWindow().Show();
    }
}
