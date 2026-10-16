using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace SaludNotebook;

// El orden importa: se usa Max() para obtener el peor estado de una sección.
public enum Estado { Bien, Atencion, Problema, Info }

public static class Colores
{
    public static readonly Brush Bien = Crear("#2E7D32");
    public static readonly Brush Atencion = Crear("#E09A00");
    public static readonly Brush Problema = Crear("#C62828");
    public static readonly Brush Info = Crear("#607D8B");

    public static Brush De(Estado e) => e switch
    {
        Estado.Bien => Bien,
        Estado.Atencion => Atencion,
        Estado.Problema => Problema,
        _ => Info,
    };

    public static string Hex(Estado e) => e switch
    {
        Estado.Bien => "#2E7D32",
        Estado.Atencion => "#E09A00",
        Estado.Problema => "#C62828",
        _ => "#607D8B",
    };

    public static string Etiqueta(Estado e) => e switch
    {
        Estado.Bien => "Todo en orden",
        Estado.Atencion => "Conviene revisar",
        Estado.Problema => "Necesita atención",
        _ => "Solo información",
    };

    public static Brush Crear(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

// Base para los objetos que la ventana tiene que ver cambiar.
public abstract class Notificador : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Cambiar<T>(ref T campo, T valor, [CallerMemberName] string prop = "")
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return;
        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}

public class Hallazgo
{
    public string Titulo { get; init; } = "";
    public string Valor { get; init; } = "";
    public string Explicacion { get; init; } = "";
    public Estado Estado { get; init; }

    public Brush Color => Colores.De(Estado);
    public string Icono => Estado switch
    {
        Estado.Bien => "✔",
        Estado.Atencion => "!",
        Estado.Problema => "✖",
        _ => "i",
    };
}

public class Seccion
{
    public string Nombre { get; init; } = "";
    public string Emoji { get; init; } = "";
    public List<Hallazgo> Hallazgos { get; } = new();

    // Valores numéricos que se guardan en el historial (ej. "salud" de la batería).
    public Dictionary<string, double> Metricas { get; } = new();

    public Estado Estado
    {
        get
        {
            var evaluados = Hallazgos.Where(h => h.Estado != Estado.Info).ToList();
            return evaluados.Count == 0 ? Estado.Info : evaluados.Max(h => h.Estado);
        }
    }

    public Brush Color => Colores.De(Estado);
    public string EstadoTexto => Colores.Etiqueta(Estado);

    // Frase corta para la tarjeta del resumen: el hallazgo más importante.
    public string Resumen
    {
        get
        {
            var peor = Hallazgos.Where(h => h.Estado != Estado.Info)
                                .OrderByDescending(h => h.Estado)
                                .FirstOrDefault();
            if (peor == null) return Hallazgos.FirstOrDefault()?.Titulo ?? "";
            return Estado == Estado.Bien ? "Sin problemas detectados" : $"{peor.Titulo}: {peor.Valor}";
        }
    }

    public void Agregar(string titulo, string valor, Estado estado, string explicacion) =>
        Hallazgos.Add(new Hallazgo { Titulo = titulo, Valor = valor, Estado = estado, Explicacion = explicacion });
}

public class DriverInfo
{
    public string Dispositivo { get; init; } = "";
    public string Tipo { get; init; } = "";
    public string Proveedor { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? Fecha { get; init; }
    public bool Antiguo { get; init; }

    public string FechaTexto => Fecha?.ToString("dd-MM-yyyy") ?? "-";
    public string Comentario => Antiguo ? "Antiguo" : "";
}

public class ActualizacionPendiente : Notificador
{
    public string Titulo { get; init; } = "";
    public string Tipo { get; init; } = "";
    public bool Opcional { get; init; }

    // Objeto IUpdate de Windows Update, necesario para instalarla.
    public object? Com { get; init; }

    string _estadoInstalacion = "";
    public string EstadoInstalacion { get => _estadoInstalacion; set => Cambiar(ref _estadoInstalacion, value); }

    bool _puedeInstalar = true;
    public bool PuedeInstalar { get => _puedeInstalar; set => Cambiar(ref _puedeInstalar, value); }
}

public class Componente
{
    public string Emoji { get; init; } = "";
    public string Categoria { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string Detalle { get; init; } = "";
    public Estado Estado { get; init; } = Estado.Bien;
    public string EstadoTexto { get; init; } = "Funciona bien";

    public Brush Color => Colores.De(Estado);
}

public class Fallo
{
    public DateTime Fecha { get; init; }
    public string Tipo { get; init; } = "";
    public string Causa { get; init; } = "";
    public string Explicacion { get; init; } = "";
    public string Codigo { get; init; } = "";
    public Estado Estado { get; init; }

    public Brush Color => Colores.De(Estado);
    public string FechaTexto => Fecha.ToString("dd-MM-yyyy HH:mm");
}

public class ElementoLimpieza : Notificador
{
    public string Nombre { get; init; } = "";
    public string Descripcion { get; init; } = "";
    public Func<long> Limpiar { get; init; } = () => 0;

    long _bytes;
    public long Bytes
    {
        get => _bytes;
        set { Cambiar(ref _bytes, value); Cambiar(ref _tamano, Formato.Tamano(value), nameof(TamanoTexto)); }
    }

    string _tamano = "";
    public string TamanoTexto => _tamano;

    bool _seleccionado;
    public bool Seleccionado { get => _seleccionado; set => Cambiar(ref _seleccionado, value); }

    string _resultado = "";
    public string Resultado { get => _resultado; set => Cambiar(ref _resultado, value); }
}

public class ProgramaAbierto : Notificador
{
    public string Nombre { get; init; } = "";
    public string Proceso { get; init; } = "";
    public List<int> Ids { get; init; } = new();
    public double RamMb { get; init; }
    public double Cpu { get; init; }
    public bool TieneVentana { get; init; }

    public string RamTexto => RamMb >= 1024 ? $"{RamMb / 1024:0.0} GB" : $"{RamMb:0} MB";
    public string CpuTexto => $"{Cpu:0.#}%";
    public string TipoTexto => TieneVentana ? "Ventana abierta" : "Segundo plano";

    bool _seleccionado;
    public bool Seleccionado { get => _seleccionado; set => Cambiar(ref _seleccionado, value); }

    string _resultado = "";
    public string Resultado { get => _resultado; set => Cambiar(ref _resultado, value); }
}

public class ProgramaInicio : Notificador
{
    public string Nombre { get; init; } = "";
    public string Comando { get; init; } = "";
    public string Origen { get; init; } = "";
    public Action<bool> Aplicar { get; init; } = _ => { };

    bool _activo;
    public bool Activo
    {
        get => _activo;
        set
        {
            if (_activo == value) return;
            try
            {
                Aplicar(value);
                Cambiar(ref _activo, value);
                Error = "";
            }
            catch (Exception ex)
            {
                Error = "No se pudo cambiar: " + ex.Message;
            }
        }
    }

    // Para cargar el estado inicial sin escribir en el registro.
    public void Cargar(bool activo) => _activo = activo;

    string _error = "";
    public string Error { get => _error; set => Cambiar(ref _error, value); }
}

public class RegistroHistorial
{
    public DateTime Fecha { get; set; }
    public double? SaludBateria { get; set; }
    public double? CiclosBateria { get; set; }
    public double? DiscoLibreGb { get; set; }
    public double? DiscoLibrePct { get; set; }
    public double? DesgasteSsd { get; set; }
    public double? TempSistema { get; set; }
    public double? TempGpu { get; set; }
    public double? RamUso { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Estado Estado { get; set; }

    [JsonIgnore] public string FechaTexto => Fecha.ToString("dd-MM-yyyy");
    [JsonIgnore] public string EstadoTexto => Colores.Etiqueta(Estado);
    [JsonIgnore] public Brush Color => Colores.De(Estado);
}

public class SesionJuego
{
    public DateTime Inicio { get; set; }
    public double Minutos { get; set; }
    public double? MaxGpu { get; set; }
    public double? PromGpu { get; set; }
    public double? MaxSistema { get; set; }
    public string Veredicto { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Estado Estado { get; set; }

    [JsonIgnore] public string FechaTexto => Inicio.ToString("dd-MM-yyyy HH:mm");
    [JsonIgnore] public string DuracionTexto => Minutos < 60 ? $"{Minutos:0} min" : $"{Minutos / 60:0.0} h";
    [JsonIgnore] public string GpuTexto => MaxGpu is { } g ? $"{g:0} °C" : "-";
    [JsonIgnore] public string SistemaTexto => MaxSistema is { } s ? $"{s:0} °C" : "-";
    [JsonIgnore] public Brush Color => Colores.De(Estado);
}

public class InfoEquipo
{
    public string Fabricante { get; init; } = "";
    public string Modelo { get; init; } = "";
    public string NumeroSerie { get; init; } = "";
    public string Procesador { get; init; } = "";
    public string Sistema { get; init; } = "";
    public string Bios { get; init; } = "";

    public bool EsLenovo => Fabricante.Contains("lenovo", StringComparison.OrdinalIgnoreCase);
}

public static class Formato
{
    public static string Tamano(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };
}
