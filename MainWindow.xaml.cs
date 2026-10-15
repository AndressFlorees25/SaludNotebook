using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaludNotebook.Revisiones;

namespace SaludNotebook;

public partial class MainWindow : Window
{
    const string AppVantage = @"shell:AppsFolder\E046963F.LenovoCompanion_k1h2ywk1493x8!App";

    readonly ObservableCollection<Seccion> _secciones = new();
    InfoEquipo? _equipo;
    bool _buscandoActualizaciones;

    public MainWindow()
    {
        InitializeComponent();
        TarjetasResumen.ItemsSource = _secciones;
    }

    async void Window_Loaded(object sender, RoutedEventArgs e) => await RevisarTodo();

    async void Revisar_Click(object sender, RoutedEventArgs e) => await RevisarTodo();

    async Task RevisarTodo()
    {
        BotonRevisar.IsEnabled = false;
        MostrarBanner(Estado.Info, "Revisando tu notebook...", "Esto toma unos segundos.");
        _secciones.Clear();

        _equipo = await Task.Run(Equipo.Leer);
        MostrarEquipo(_equipo);

        // Las revisiones rápidas corren en paralelo.
        var bateria = Task.Run(Bateria.Revisar);
        var disco = Task.Run(Disco.Revisar);
        var temperatura = Task.Run(Temperatura.Revisar);
        var rendimiento = Task.Run(Rendimiento.Revisar);
        var drivers = Task.Run(Drivers.Revisar);

        Mostrar(await bateria, ListaBateria);
        Mostrar(await disco, ListaDisco);
        Mostrar(await temperatura, ListaTemperatura);
        Mostrar(await rendimiento, ListaRendimiento);
        var (secDrivers, listaDrivers) = await drivers;
        Mostrar(secDrivers, ListaDrivers);
        TablaDrivers.ItemsSource = listaDrivers;

        ActualizarBanner();
        BotonRevisar.IsEnabled = true;

        // Windows Update es lento: va aparte y se suma al resumen cuando termina.
        await RevisarActualizaciones();
    }

    async Task RevisarActualizaciones()
    {
        if (_buscandoActualizaciones) return;
        _buscandoActualizaciones = true;
        EstadoActualizaciones.Text = "Buscando actualizaciones en Windows Update (puede tardar un par de minutos)...";
        ListaActualizaciones.ItemsSource = null;
        ListaPendientes.ItemsSource = null;

        var (sec, pendientes) = await Task.Run(Actualizaciones.Revisar);
        Mostrar(sec, ListaActualizaciones);
        ListaPendientes.ItemsSource = pendientes;
        EstadoActualizaciones.Text = $"Última búsqueda: {DateTime.Now:HH:mm}";
        ActualizarBanner();
        _buscandoActualizaciones = false;
    }

    void Mostrar(Seccion s, ItemsControl lista)
    {
        lista.ItemsSource = s.Hallazgos;
        var anterior = _secciones.FirstOrDefault(x => x.Nombre == s.Nombre);
        if (anterior != null) _secciones.Remove(anterior);
        _secciones.Add(s);
    }

    void ActualizarBanner()
    {
        var evaluadas = _secciones.Where(s => s.Estado != Estado.Info).ToList();
        var peor = evaluadas.Count == 0 ? Estado.Info : evaluadas.Max(s => s.Estado);
        var revisar = string.Join(", ", evaluadas.Where(s => s.Estado == peor).Select(s => s.Nombre.ToLower()));

        switch (peor)
        {
            case Estado.Bien:
                MostrarBanner(peor, "Tu notebook está en buen estado", "No se encontró nada que necesite mantenimiento ni cambio de piezas.");
                break;
            case Estado.Atencion:
                MostrarBanner(peor, "Tu notebook está bien, pero hay cosas que conviene revisar", $"Mira: {revisar}. Abre cada pestaña para ver qué hacer.");
                break;
            case Estado.Problema:
                MostrarBanner(peor, "Hay algo que necesita atención", $"Revisa: {revisar}. Puede necesitar mantenimiento o un cambio de pieza.");
                break;
            default:
                MostrarBanner(peor, "No se pudo evaluar el equipo", "Prueba volver a revisar.");
                break;
        }
    }

    void MostrarBanner(Estado estado, string titulo, string detalle)
    {
        Banner.Background = Colores.De(estado);
        TextoBanner.Text = titulo;
        SubtextoBanner.Text = detalle;
    }

    void MostrarEquipo(InfoEquipo e)
    {
        TextoEquipo.Text = $"{e.Fabricante} {e.Modelo}  ·  {e.Procesador}";

        var filas = new (string, string)[]
        {
            ("Marca", e.Fabricante),
            ("Modelo", e.Modelo),
            ("Número de serie", e.NumeroSerie),
            ("Procesador", e.Procesador),
            ("Sistema", e.Sistema),
            ("BIOS", e.Bios),
        };

        DatosEquipo.Children.Clear();
        DatosEquipo.RowDefinitions.Clear();
        for (int i = 0; i < filas.Length; i++)
        {
            DatosEquipo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var etiqueta = new TextBlock { Text = filas[i].Item1, Foreground = Colores.Info, Margin = new Thickness(0, 3, 0, 3) };
            var valor = new TextBlock { Text = filas[i].Item2, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(etiqueta, i);
            Grid.SetRow(valor, i);
            Grid.SetColumn(valor, 1);
            DatosEquipo.Children.Add(etiqueta);
            DatosEquipo.Children.Add(valor);
        }

        var vantage = e.EsLenovo ? Visibility.Visible : Visibility.Collapsed;
        BotonVantage.Visibility = vantage;
        BotonVantage2.Visibility = vantage;
    }

    void Tarjeta_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Seccion s) return;
        var pestana = Pestanas.Items.OfType<TabItem>().FirstOrDefault(t => (t.Header as string) == s.Nombre);
        if (pestana != null) Pestanas.SelectedItem = pestana;
    }

    void Vantage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", AppVantage));
        }
        catch
        {
            Abrir("https://www.lenovo.com/vantage");
        }
    }

    void Soporte_Click(object sender, RoutedEventArgs e)
    {
        if (_equipo != null) Abrir(Equipo.PaginaSoporte(_equipo));
    }

    void CopiarSerie_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_equipo?.NumeroSerie)) return;
        Clipboard.SetText(_equipo.NumeroSerie);
        MessageBox.Show(this, $"Número de serie copiado: {_equipo.NumeroSerie}\n\nPégalo en la página de soporte para ver los drivers exactos de tu modelo.",
            "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void WindowsUpdate_Click(object sender, RoutedEventArgs e) => Abrir("ms-settings:windowsupdate");

    static void Abrir(string destino)
    {
        try
        {
            Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
