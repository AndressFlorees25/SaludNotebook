using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaludNotebook.Revisiones;

namespace SaludNotebook;

public partial class MainWindow : Window
{
    const string AppVantage = @"shell:AppsFolder\E046963F.LenovoCompanion_k1h2ywk1493x8!App";

    readonly ObservableCollection<Seccion> _secciones = new();
    readonly SemaphoreSlim _colaInstalacion = new(1, 1);
    ResultadoRevision? _resultado;
    List<ActualizacionPendiente> _pendientes = new();
    bool _buscandoActualizaciones;

    public MainWindow()
    {
        InitializeComponent();
        TarjetasResumen.ItemsSource = _secciones;
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CargarHistorial();
        _ = CargarAjustesAsync();
        await RevisarTodo();
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_juegoCts != null) TerminarJuego();
    }

    async void Revisar_Click(object sender, RoutedEventArgs e) => await RevisarTodo();

    // ==================== Revisión ====================

    async Task RevisarTodo()
    {
        BotonRevisar.IsEnabled = false;
        BotonPdf.IsEnabled = false;
        MostrarBanner(Estado.Info, "Revisando tu notebook...", "Esto toma unos segundos.");
        _secciones.Clear();

        var r = await Revisor.RevisarAsync();
        _resultado = r;
        MostrarEquipo(r.Equipo);

        var listas = new Dictionary<string, ItemsControl>
        {
            ["Componentes"] = ListaComponentes,
            ["Batería"] = ListaBateria,
            ["Disco"] = ListaDisco,
            ["Temperatura"] = ListaTemperatura,
            ["Rendimiento"] = ListaRendimiento,
            ["Drivers"] = ListaDrivers,
            ["Fallos"] = ListaFallosResumen,
        };
        foreach (var s in r.Secciones)
            Mostrar(s, listas[s.Nombre]);

        TablaDrivers.ItemsSource = r.Drivers;
        TarjetasComponentes.ItemsSource = r.Componentes;
        ListaFallos.ItemsSource = r.Fallos;
        SinFallos.Visibility = r.Fallos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ActualizarBanner();
        Historial.Guardar(r);
        CargarHistorial();
        BotonRevisar.IsEnabled = true;
        BotonPdf.IsEnabled = true;

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
        _pendientes = pendientes;
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
        var peor = Revisor.PeorEstado(_secciones);
        var revisar = string.Join(", ", _secciones.Where(s => s.Estado == peor).Select(s => s.Nombre.ToLower()));

        switch (peor)
        {
            case Estado.Bien:
                MostrarBanner(peor, "Tu notebook está en buen estado", "No se encontró nada que necesite mantenimiento ni cambio de piezas.");
                break;
            case Estado.Atencion:
                MostrarBanner(peor, "Tu notebook está bien, pero hay cosas que conviene revisar", $"Mira: {revisar}. Abre cada sección para ver qué hacer.");
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

    // ==================== Navegación ====================

    bool _programasCargados, _limpiezaCargada;

    async void Pestanas_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Pestanas) return;
        switch ((Pestanas.SelectedItem as TabItem)?.Header as string)
        {
            case "📋 Programas" when !_programasCargados:
                _programasCargados = true;
                await CargarProgramas();
                break;
            case "🧹 Liberar espacio" when !_limpiezaCargada:
                _limpiezaCargada = true;
                await AnalizarLimpieza();
                break;
        }
    }

    void IrA(string encabezado)
    {
        var pestana = Pestanas.Items.OfType<TabItem>().FirstOrDefault(t => (t.Header as string) == encabezado);
        if (pestana != null) Pestanas.SelectedItem = pestana;
    }

    void Tarjeta_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Seccion s) IrA(s.Nombre);
    }

    void IrALimpieza_Click(object sender, RoutedEventArgs e) => IrA("🧹 Liberar espacio");
    void IrAModoJuego_Click(object sender, RoutedEventArgs e) => IrA("🎮 Modo juego");
    void IrAProgramas_Click(object sender, RoutedEventArgs e) => IrA("📋 Programas");

    // ==================== Actualizaciones ====================

    async void Instalar_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ActualizacionPendiente p) return;
        p.PuedeInstalar = false;
        p.EstadoInstalacion = "En espera...";

        // Windows Update solo permite una instalación a la vez: las demás esperan su turno.
        await _colaInstalacion.WaitAsync();
        (bool ok, bool reiniciar, string mensaje) r;
        try
        {
            p.EstadoInstalacion = "Preparando...";
            r = await Task.Run(() =>
                Actualizaciones.Instalar(p, texto => Dispatcher.Invoke(() => p.EstadoInstalacion = texto)));
        }
        finally
        {
            _colaInstalacion.Release();
        }
        var (ok, reiniciar, mensaje) = r;

        p.EstadoInstalacion = mensaje;
        p.PuedeInstalar = !ok;
        if (reiniciar)
            MessageBox.Show(this, $"\"{p.Titulo}\" quedó instalada.\n\nReinicia el notebook cuando puedas para que termine de aplicarse.",
                "Reinicio pendiente", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    void WindowsUpdate_Click(object sender, RoutedEventArgs e) => Abrir("ms-settings:windowsupdate");
    void Opcionales_Click(object sender, RoutedEventArgs e) => Abrir("ms-settings:windowsupdate-optionalupdates");

    // ==================== Fallos ====================

    void DiagnosticoMemoria_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this,
            "El diagnóstico de memoria de Windows reinicia el notebook y revisa la RAM durante unos 15 minutos.\n\n" +
            "Guarda lo que estés haciendo antes de continuar. En la siguiente ventana elige cuándo hacerlo.",
            "Diagnóstico de memoria", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (r == MessageBoxResult.OK) Abrir("mdsched.exe");
    }

    // ==================== Modo juego ====================

    CancellationTokenSource? _juegoCts;
    readonly List<(double? Gpu, double? Sistema)> _muestras = new();
    DateTime _juegoInicio;

    void Juego_Click(object sender, RoutedEventArgs e)
    {
        if (_juegoCts == null) EmpezarJuego();
        else TerminarJuego();
    }

    void EmpezarJuego()
    {
        _muestras.Clear();
        CajaVeredicto.Visibility = Visibility.Collapsed;
        BotonJuego.Content = "■  Terminar y ver resultado";
        BotonJuego.Background = Colores.Problema;
        _juegoInicio = DateTime.Now;
        _juegoCts = new CancellationTokenSource();
        _ = MedirJuego(_juegoCts.Token);
    }

    async Task MedirJuego(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var (gpu, uso, sistema) = await Task.Run(() =>
            {
                var g = Temperatura.LeerGpus().FirstOrDefault();
                return ((double?)g?.Temp, g?.Uso, Temperatura.LeerSistema());
            });
            if (ct.IsCancellationRequested) break;

            _muestras.Add((gpu, sistema));
            JuegoGpu.Text = gpu is { } gg ? $"{gg:0} °C" : "-";
            JuegoSistema.Text = sistema is { } ss ? $"{ss:0} °C" : "-";
            JuegoGpuMax.Text = $"Máxima: {Max(m => m.Gpu)}";
            JuegoSistemaMax.Text = $"Máxima: {Max(m => m.Sistema)}";
            JuegoUso.Text = uso is { } u ? $"Uso de la gráfica: {u}%" : "Uso de la gráfica: -";
            var t = DateTime.Now - _juegoInicio;
            JuegoTiempo.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
            DibujarJuego();

            try
            {
                await Task.Delay(2000, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    string Max(Func<(double? Gpu, double? Sistema), double?> campo) =>
        _muestras.Select(campo).Max() is { } v ? $"{v:0} °C" : "-";

    void DibujarJuego() => Grafico.Dibujar(GraficoJuego,
        new List<IReadOnlyList<double?>> { _muestras.Select(m => m.Gpu).ToList(), _muestras.Select(m => m.Sistema).ToList() },
        new[] { Colores.Crear("#E65100"), Colores.Crear("#1565C0") }, "°");

    void GraficoJuego_SizeChanged(object sender, SizeChangedEventArgs e) => DibujarJuego();

    void TerminarJuego()
    {
        _juegoCts?.Cancel();
        _juegoCts = null;
        BotonJuego.Content = "▶  Empezar a medir";
        BotonJuego.ClearValue(BackgroundProperty);

        if (_muestras.Count < 2)
        {
            MostrarVeredicto(Estado.Info, "Mediste muy poco rato para sacar conclusiones. Prueba de nuevo jugando al menos 15 minutos.");
            return;
        }

        var minutos = (DateTime.Now - _juegoInicio).TotalMinutes;
        var maxGpu = _muestras.Max(m => m.Gpu);
        var maxSis = _muestras.Max(m => m.Sistema);
        var gpus = _muestras.Where(m => m.Gpu != null).Select(m => m.Gpu!.Value).ToList();
        double? promGpu = gpus.Count > 0 ? gpus.Average() : null;

        var (estado, texto) = (maxGpu ?? 0, maxSis ?? 0) switch
        {
            ( >= 87, _) or (_, >= 95) => (Estado.Problema,
                "Se calentó demasiado. Conviene una limpieza de ventiladores y cambio de pasta térmica. Mientras tanto, juega sobre una superficie dura (no en la cama) o con una base refrigerante."),
            ( >= 80, _) or (_, >= 90) => (Estado.Atencion,
                "Se puso bastante caliente, pero dentro de lo que aguanta. Si con los meses sube más, es señal de polvo en los ventiladores."),
            _ => (Estado.Bien, "Temperaturas normales para jugar. Tu notebook se enfría bien. 👍"),
        };
        if (minutos < 5) texto += " (Mediste menos de 5 minutos: para un resultado confiable juega al menos 15.)";

        var sesion = new SesionJuego
        {
            Inicio = _juegoInicio, Minutos = minutos, MaxGpu = maxGpu, PromGpu = promGpu, MaxSistema = maxSis,
            Veredicto = texto, Estado = estado,
        };
        Historial.GuardarSesion(sesion);
        CargarHistorial();

        var resumen = $"Máxima de la gráfica: {(maxGpu is { } g ? $"{g:0} °C" : "-")}  ·  Máxima del sistema: {(maxSis is { } s ? $"{s:0} °C" : "-")}\n{texto}";
        MostrarVeredicto(estado, resumen);
    }

    void MostrarVeredicto(Estado estado, string texto)
    {
        CajaVeredicto.BorderBrush = Colores.De(estado);
        VeredictoJuego.Text = texto;
        CajaVeredicto.Visibility = Visibility.Visible;
    }

    // ==================== Liberar espacio ====================

    List<ElementoLimpieza> _limpieza = new();

    async Task AnalizarLimpieza()
    {
        ActualizarEspacio();
        ListaLimpieza.ItemsSource = null;
        TotalLimpieza.Text = "Calculando cuánto se puede liberar...";
        BotonLimpiar.IsEnabled = false;

        _limpieza = await Task.Run(Limpieza.Analizar);
        ListaLimpieza.ItemsSource = _limpieza;
        ActualizarTotal();
        BotonLimpiar.IsEnabled = true;

        var descargas = await Task.Run(Limpieza.TamanoDescargas);
        TextoDescargas.Text = $"Ocupa {Formato.Tamano(descargas)}. Esta carpeta no se limpia sola: revisa si hay instaladores o archivos que ya no necesitas.";
    }

    void ActualizarEspacio()
    {
        try
        {
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            EspacioActual.Text = $"Disco {c.Name.TrimEnd('\\')}: {c.AvailableFreeSpace / 1e9:0} GB libres de {c.TotalSize / 1e9:0} GB " +
                                 $"({c.AvailableFreeSpace * 100.0 / c.TotalSize:0}%)";
        }
        catch
        {
            EspacioActual.Text = "";
        }
    }

    void Limpieza_Cambio(object sender, RoutedEventArgs e) => ActualizarTotal();

    void ActualizarTotal()
    {
        var total = _limpieza.Where(x => x.Seleccionado).Sum(x => x.Bytes);
        TotalLimpieza.Text = $"Se liberarán aproximadamente {Formato.Tamano(total)}.";
    }

    async void Limpiar_Click(object sender, RoutedEventArgs e)
    {
        var elegidos = _limpieza.Where(x => x.Seleccionado && x.Bytes > 0).ToList();
        if (elegidos.Count == 0) return;
        BotonLimpiar.IsEnabled = false;
        long total = 0;
        foreach (var x in elegidos)
        {
            x.Resultado = "Limpiando...";
            var liberado = await Task.Run(x.Limpiar);
            total += liberado;
            x.Bytes = Math.Max(0, x.Bytes - liberado);
            x.Resultado = liberado > 0 ? $"Liberado {Formato.Tamano(liberado)} ✔" : "Nada que borrar (archivos en uso)";
        }
        TotalLimpieza.Text = $"¡Listo! Liberaste {Formato.Tamano(total)}. Algunos archivos pueden quedar porque están en uso; se borran la próxima vez.";
        ActualizarEspacio();
        BotonLimpiar.IsEnabled = true;
    }

    async void AnalizarLimpieza_Click(object sender, RoutedEventArgs e) => await AnalizarLimpieza();

    void AbrirDescargas_Click(object sender, RoutedEventArgs e) =>
        Abrir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

    void AbrirAplicaciones_Click(object sender, RoutedEventArgs e) => Abrir("ms-settings:appsfeatures");

    // ==================== Programas ====================

    async Task CargarProgramas()
    {
        EstadoProgramas.Text = "Midiendo qué está usando cada programa...";
        ListaAbiertos.ItemsSource = null;
        var abiertos = await Task.Run(Programas.ListarAbiertos);
        ListaAbiertos.ItemsSource = abiertos;
        EstadoProgramas.Text = $"{abiertos.Count} programas abiertos, usando {abiertos.Sum(p => p.RamMb) / 1024:0.0} GB de memoria en total.";
        ListaInicio.ItemsSource = await Task.Run(Programas.ListarInicio);
    }

    async void ActualizarProgramas_Click(object sender, RoutedEventArgs e) => await CargarProgramas();

    async void CerrarProgramas_Click(object sender, RoutedEventArgs e)
    {
        if (ListaAbiertos.ItemsSource is not List<ProgramaAbierto> lista) return;
        var elegidos = lista.Where(p => p.Seleccionado).ToList();
        if (elegidos.Count == 0) return;
        BotonCerrarProgramas.IsEnabled = false;
        foreach (var p in elegidos)
        {
            p.Resultado = "Cerrando...";
            p.Resultado = await Task.Run(() => Programas.Cerrar(p));
            p.Seleccionado = false;
        }
        EstadoProgramas.Text = $"Listo. Liberaste unos {elegidos.Where(p => p.Resultado.StartsWith("Cerrado")).Sum(p => p.RamMb) / 1024:0.0} GB de memoria.";
        BotonCerrarProgramas.IsEnabled = true;
    }

    // ==================== Historial ====================

    List<RegistroHistorial> _historial = new();

    void CargarHistorial()
    {
        _historial = Historial.LeerRevisiones();
        TablaHistorial.ItemsSource = _historial.OrderByDescending(x => x.Fecha).ToList();
        ListaSesiones.ItemsSource = Historial.LeerSesiones().AsEnumerable().Reverse().ToList();
        DibujarHistorial();
    }

    void DibujarHistorial()
    {
        Grafico.Dibujar(GraficoBateria, new List<IReadOnlyList<double?>> { _historial.Select(x => x.SaludBateria).ToList() },
            new[] { Colores.Bien }, "%");
        Grafico.Dibujar(GraficoDisco, new List<IReadOnlyList<double?>> { _historial.Select(x => x.DiscoLibreGb).ToList() },
            new[] { Colores.Crear("#1565C0") }, " GB");
    }

    void Historial_SizeChanged(object sender, SizeChangedEventArgs e) => DibujarHistorial();

    // ==================== Ajustes ====================

    async Task CargarAjustesAsync()
    {
        ChkSemanal.IsChecked = await Task.Run(() => Instalacion.Existe(Instalacion.TareaSemanal));
        if (Instalacion.HayAccesoDirecto())
        {
            EstadoAcceso.Text = "✔ Ya tienes el acceso directo en el escritorio";
            BotonAcceso.Content = "Volver a crearlo";
        }
    }

    void Semanal_Click(object sender, RoutedEventArgs e)
    {
        EstadoAjustes.Text = "";
        try
        {
            if (ChkSemanal.IsChecked == true) Instalacion.CrearTareaSemanal();
            else Instalacion.Borrar(Instalacion.TareaSemanal);
        }
        catch (Exception ex)
        {
            ChkSemanal.IsChecked = !ChkSemanal.IsChecked;
            EstadoAjustes.Text = "No se pudo cambiar la revisión automática: " + ex.Message;
        }
    }

    void AccesoDirecto_Click(object sender, RoutedEventArgs e)
    {
        EstadoAjustes.Text = "";
        try
        {
            Instalacion.CrearTareaAbrir();
            Instalacion.CrearAccesosDirectos();
            EstadoAcceso.Text = "✔ Listo: usa el ícono \"Salud del Notebook\" del escritorio";
        }
        catch (Exception ex)
        {
            EstadoAjustes.Text = "No se pudo crear el acceso directo: " + ex.Message;
        }
    }

    async void Pdf_Click(object sender, RoutedEventArgs e)
    {
        if (_resultado == null) return;
        var ruta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"Informe Salud Notebook {DateTime.Now:yyyy-MM-dd HH-mm}.pdf");
        BotonPdf.IsEnabled = false;
        try
        {
            var r = _resultado;
            var pendientes = _pendientes.ToList();
            await Task.Run(() => Informe.Generar(r, pendientes, Historial.LeerSesiones(), ruta));
            Abrir(ruta);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo crear el PDF: " + ex.Message, "Informe", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BotonPdf.IsEnabled = true;
        }
    }

    // ==================== Equipo / soporte ====================

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
        if (_resultado != null) Abrir(Equipo.PaginaSoporte(_resultado.Equipo));
    }

    void CopiarSerie_Click(object sender, RoutedEventArgs e)
    {
        var serie = _resultado?.Equipo.NumeroSerie;
        if (string.IsNullOrEmpty(serie)) return;
        Clipboard.SetText(serie);
        MessageBox.Show(this, $"Número de serie copiado: {serie}\n\nPégalo en la página de soporte para ver los drivers exactos de tu modelo.",
            "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
    }

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
