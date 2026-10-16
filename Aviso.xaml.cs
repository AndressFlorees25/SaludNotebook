using System.Windows;
using System.Windows.Threading;
using SaludNotebook.Revisiones;

namespace SaludNotebook;

// Ventanita en la esquina que muestra la revisión semanal cuando encuentra problemas.
public partial class Aviso : Window
{
    readonly DispatcherTimer _autoCierre = new() { Interval = TimeSpan.FromMinutes(10) };

    public Aviso(ResultadoRevision r)
    {
        InitializeComponent();
        var malas = r.Secciones.Where(s => s.Estado == Estado.Problema).ToList();
        Franja.Background = Colores.De(r.EstadoGeneral);
        Titulo.Text = malas.Count == 1 ? "Hay algo que necesita atención" : "Hay cosas que necesitan atención";
        Detalle.Text = string.Join("\n", malas.Select(s => $"• {s.Nombre}: {s.Resumen}"));
        _autoCierre.Tick += (_, _) => Close();
        _autoCierre.Start();
    }

    void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }

    void Abrir_Click(object sender, RoutedEventArgs e)
    {
        new MainWindow().Show();
        Close();
    }

    void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
