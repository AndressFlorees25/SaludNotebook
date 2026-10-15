using System.ComponentModel;
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

    public static string Etiqueta(Estado e) => e switch
    {
        Estado.Bien => "Todo en orden",
        Estado.Atencion => "Conviene revisar",
        Estado.Problema => "Necesita atención",
        _ => "Solo información",
    };

    static Brush Crear(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
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

public class ActualizacionPendiente : INotifyPropertyChanged
{
    public string Titulo { get; init; } = "";
    public string Tipo { get; init; } = "";
    public bool Opcional { get; init; }

    // Objeto IUpdate de Windows Update, necesario para instalarla.
    public object? Com { get; init; }

    string _estadoInstalacion = "";
    public string EstadoInstalacion
    {
        get => _estadoInstalacion;
        set { _estadoInstalacion = value; Avisar(nameof(EstadoInstalacion)); }
    }

    bool _puedeInstalar = true;
    public bool PuedeInstalar
    {
        get => _puedeInstalar;
        set { _puedeInstalar = value; Avisar(nameof(PuedeInstalar)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Avisar(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
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
