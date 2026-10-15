using System.Diagnostics.Eventing.Reader;

namespace SaludNotebook.Revisiones;

// Inventario de las piezas internas y si Windows las ve funcionando bien.
static class Componentes
{
    const int DiasHistorial = 30;

    // Errores de hardware registrados por Windows (WHEA) en los últimos días, por tipo.
    record ErroresHardware(int Procesador, int Memoria, int PciExpress, int Fatales, int Otros)
    {
        public int Total => Procesador + Memoria + PciExpress + Fatales + Otros;
    }

    public static (Seccion, List<Componente>) Revisar()
    {
        var s = new Seccion { Nombre = "Componentes", Emoji = "🧩" };
        var lista = new List<Componente>();

        var whea = LeerErroresHardware();
        var pnp = LeerEstadosPnp();

        void Agregar(string emoji, string categoria, string nombre, string detalle, Estado estado = Estado.Bien, string estadoTexto = "Funciona bien") =>
            lista.Add(new Componente
            {
                Emoji = emoji, Categoria = categoria, Nombre = nombre, Detalle = detalle,
                Estado = estado, EstadoTexto = estadoTexto,
            });

        // Estado según el Administrador de dispositivos.
        (Estado, string) SegunPnp(string idDispositivo)
        {
            if (pnp.TryGetValue(idDispositivo, out var codigo) && codigo != 0)
                return codigo == 22
                    ? (Estado.Atencion, "Está deshabilitado")
                    : (Estado.Problema, $"Windows reporta un error (código {codigo})");
            return (Estado.Bien, "Funciona bien");
        }

        // Cada bloque va protegido: si una consulta falla, el resto se sigue mostrando.
        void Intentar(Action accion)
        {
            try { accion(); } catch { }
        }

        Intentar(() =>
        {
            var placa = Wmi.Primero("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
            var bios = Wmi.Primero("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            if (placa == null) return;
            var detalle = $"Versión de la placa: {Wmi.Texto(placa, "Version")}";
            if (bios != null)
                detalle += $"\nBIOS: {Wmi.Texto(bios, "SMBIOSBIOSVersion")}" +
                           (Wmi.Fecha(bios, "ReleaseDate") is { } f ? $" del {f:dd-MM-yyyy}" : "");
            var erroresPlaca = whea.PciExpress + whea.Otros;
            var (e, t) = whea.Fatales > 0
                ? (Estado.Problema, $"{whea.Fatales} error(es) grave(s) de hardware")
                : erroresPlaca > 0
                    ? (Estado.Atencion, $"{erroresPlaca} error(es) corregido(s) en {DiasHistorial} días")
                    : (Estado.Bien, "Sin errores registrados");
            Agregar("🧩", "Placa madre", $"{Capitalizar(Wmi.Texto(placa, "Manufacturer"))} {Wmi.Texto(placa, "Product")}", detalle, e, t);
        });

        Intentar(() =>
        {
            foreach (var cpu in Wmi.Consultar("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor"))
                Agregar("🧠", "Procesador", Wmi.Texto(cpu, "Name"),
                    $"{Wmi.Numero(cpu, "NumberOfCores")} núcleos, {Wmi.Numero(cpu, "NumberOfLogicalProcessors")} hilos · " +
                    $"{Wmi.Numero(cpu, "MaxClockSpeed") / 1000.0:0.0} GHz base · caché L3 {Wmi.Numero(cpu, "L3CacheSize") / 1024} MB",
                    whea.Procesador == 0 ? Estado.Bien : Estado.Atencion,
                    whea.Procesador == 0 ? "Sin errores registrados" : $"{whea.Procesador} error(es) corregido(s) en {DiasHistorial} días");
        });

        Intentar(() => LeerRam(Agregar, whea.Memoria));

        Intentar(() =>
        {
            // AdapterRAM se queda en 4 GB como máximo; para NVIDIA se pregunta a nvidia-smi.
            var vramNvidia = Wmi.Ejecutar("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits", 10000)?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            foreach (var v in Wmi.Consultar("SELECT Name, AdapterRAM, PNPDeviceID FROM Win32_VideoController"))
            {
                var nombre = Wmi.Texto(v, "Name");
                var vram = nombre.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && double.TryParse(vramNvidia, out var mb)
                    ? mb / 1024
                    : Wmi.Numero(v, "AdapterRAM") / 1024.0 / 1024 / 1024;
                var (e, t) = SegunPnp(Wmi.Texto(v, "PNPDeviceID"));
                Agregar("🎮", "Tarjeta gráfica", nombre, vram >= 0.5 ? $"Memoria de video: {vram:0} GB" : "", e, t);
            }
        });

        Intentar(() =>
        {
            foreach (var d in Wmi.Consultar("SELECT FriendlyName, Size, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"))
            {
                var tipo = Wmi.Numero(d, "MediaType") switch { 3 => "HDD", 4 => "SSD", _ => "Disco" };
                var bus = Wmi.Numero(d, "BusType") switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", _ => "" };
                var (e, t) = Wmi.Numero(d, "HealthStatus") switch
                {
                    0 => (Estado.Bien, "Funciona bien"),
                    1 => (Estado.Atencion, "Reporta advertencias"),
                    2 => (Estado.Problema, "Reporta fallas"),
                    _ => (Estado.Info, "Estado desconocido"),
                };
                Agregar("💽", "Almacenamiento", Wmi.Texto(d, "FriendlyName"),
                    $"{tipo} {bus} de {Wmi.Numero(d, "Size") / 1e9:0} GB".Replace("  ", " "), e, t);
            }
        });

        Intentar(() => LeerPantalla(Agregar));

        Intentar(() =>
        {
            foreach (var n in Wmi.Consultar("SELECT InterfaceDescription, PnPDeviceID, MediaConnectState FROM MSFT_NetAdapter WHERE HardwareInterface = TRUE", @"root\StandardCimv2"))
            {
                var desc = Wmi.Texto(n, "InterfaceDescription");
                var wifi = desc.Contains("Wi", StringComparison.OrdinalIgnoreCase) || desc.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
                           || desc.Contains("802.11");
                var conectado = Wmi.Numero(n, "MediaConnectState") == 1;
                var (e, t) = SegunPnp(Wmi.Texto(n, "PnPDeviceID"));
                Agregar(wifi ? "📶" : "🌐", wifi ? "WiFi" : "Red por cable", desc,
                    conectado ? "Conectado ahora" : (wifi ? "Sin conexión ahora" : "Sin cable conectado"), e, t);
            }
        });

        Intentar(() =>
        {
            var bt = Wmi.Consultar("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'")
                        .FirstOrDefault(b => Wmi.Texto(b, "Name") is var n &&
                                             (n.Contains("Adapter", StringComparison.OrdinalIgnoreCase) || n.Contains("Radio", StringComparison.OrdinalIgnoreCase)));
            if (bt == null) return;
            var (e, t) = SegunPnp(Wmi.Texto(bt, "DeviceID"));
            Agregar("🔵", "Bluetooth", Wmi.Texto(bt, "Name"), "", e, t);
        });

        Intentar(() =>
        {
            // Solo los dispositivos de audio físicos, no los virtuales que agregan los programas.
            foreach (var a in Wmi.Consultar("SELECT Name, PNPDeviceID FROM Win32_SoundDevice"))
            {
                var id = Wmi.Texto(a, "PNPDeviceID");
                if (!id.StartsWith("HDAUDIO", StringComparison.OrdinalIgnoreCase) && !id.StartsWith("INTELAUDIO", StringComparison.OrdinalIgnoreCase)
                    && !id.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) continue;
                var (e, t) = SegunPnp(id);
                Agregar("🔊", "Audio", Wmi.Texto(a, "Name"), "", e, t);
            }
        });

        Intentar(() =>
        {
            foreach (var c in Wmi.Consultar("SELECT Name, DeviceID FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'"))
            {
                var (e, t) = SegunPnp(Wmi.Texto(c, "DeviceID"));
                Agregar("📷", "Cámara", Wmi.Texto(c, "Name"), "", e, t);
            }
        });

        // Resumen para la tarjeta de la portada.
        var malos = lista.Where(c => c.Estado is Estado.Atencion or Estado.Problema).ToList();
        if (malos.Count == 0)
            s.Agregar("Componentes", $"Los {lista.Count} funcionan bien", Estado.Bien, "Windows no reporta problemas en ninguna pieza.");
        foreach (var c in malos)
            s.Agregar(c.Categoria, c.EstadoTexto, c.Estado, c.Nombre);

        s.Agregar($"Errores de hardware (últimos {DiasHistorial} días)", whea.Total == 0 ? "Ninguno" : $"{whea.Total}",
            whea.Fatales > 0 ? Estado.Problema : whea.Total > 0 ? Estado.Atencion : Estado.Bien,
            whea.Total == 0
                ? "Windows no registró fallas de procesador, memoria ni placa."
                : "Windows registró fallas de hardware. Si se repiten o el equipo se cuelga, llévalo a revisar.");

        var apagones = ContarApagadosInesperados();
        if (apagones >= 0)
            s.Agregar($"Apagados inesperados (últimos {DiasHistorial} días)", $"{apagones}",
                apagones >= 3 ? Estado.Atencion : Estado.Bien,
                apagones switch
                {
                    0 => "El notebook no se ha apagado de golpe.",
                    < 3 => "Pocas veces. Es normal si alguna vez lo apagaste manteniendo el botón o se quedó sin batería.",
                    _ => "Se apagó o reinició de golpe varias veces. Puede ser por calor, la batería, el cargador o un driver. Fíjate si pasa jugando (calor) o al azar.",
                });

        return (s, lista);
    }

    static ErroresHardware LeerErroresHardware()
    {
        int cpu = 0, mem = 0, pci = 0, fatal = 0, otros = 0;
        foreach (var id in LeerEventos("Microsoft-Windows-WHEA-Logger"))
        {
            switch (id)
            {
                case 1 or 18 or 20: fatal++; break;
                case 19: cpu++; break;
                case 47: mem++; break;
                case 17: pci++; break;
                default: otros++; break;
            }
        }
        return new ErroresHardware(cpu, mem, pci, fatal, otros);
    }

    static int ContarApagadosInesperados()
    {
        try
        {
            return LeerEventos("Microsoft-Windows-Kernel-Power", 41).Count();
        }
        catch
        {
            return -1;
        }
    }

    static IEnumerable<int> LeerEventos(string proveedor, int? id = null)
    {
        var ms = (long)TimeSpan.FromDays(DiasHistorial).TotalMilliseconds;
        var filtroId = id == null ? "" : $" and EventID={id}";
        var consulta = new EventLogQuery("System", PathType.LogName,
            $"*[System[Provider[@Name='{proveedor}']{filtroId} and TimeCreated[timediff(@SystemTime) <= {ms}]]]");
        var ids = new List<int>();
        try
        {
            using var lector = new EventLogReader(consulta);
            for (var ev = lector.ReadEvent(); ev != null; ev = lector.ReadEvent())
                using (ev) ids.Add(ev.Id);
        }
        catch (EventLogNotFoundException)
        {
        }
        return ids;
    }

    static Dictionary<string, long> LeerEstadosPnp() =>
        Wmi.Consultar("SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity")
           .GroupBy(d => Wmi.Texto(d, "DeviceID"), StringComparer.OrdinalIgnoreCase)
           .ToDictionary(g => g.Key, g => Wmi.Numero(g.First(), "ConfigManagerErrorCode"), StringComparer.OrdinalIgnoreCase);

    static void LeerRam(Action<string, string, string, string, Estado, string> agregar, int errores)
    {
        var modulos = Wmi.Consultar("SELECT Capacity, Speed, Manufacturer, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        if (modulos.Count == 0) return;

        var ranuras = Wmi.Primero("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray") is { } arr ? Wmi.Numero(arr, "MemoryDevices") : 0;
        var totalGb = modulos.Sum(m => Wmi.Numero(m, "Capacity")) / 1024.0 / 1024 / 1024;
        var tipo = Wmi.Numero(modulos[0], "SMBIOSMemoryType") switch
        {
            24 => "DDR3", 26 => "DDR4", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "",
        };
        var velocidad = Wmi.Numero(modulos[0], "Speed");

        var detalle = string.Join("\n", modulos.Select((m, i) =>
            $"Módulo {i + 1}: {Wmi.Numero(m, "Capacity") / 1024.0 / 1024 / 1024:0} GB · {Capitalizar(Wmi.Texto(m, "Manufacturer"))} · {Wmi.Texto(m, "PartNumber")}"));

        if (ranuras > 0)
        {
            detalle += $"\nRanuras usadas: {modulos.Count} de {ranuras}.";
            detalle += modulos.Count < ranuras
                ? " Hay una ranura libre: puedes agregar otro módulo igual para tener más RAM."
                : " Para tener más RAM hay que cambiar los módulos por unos más grandes.";
        }

        agregar("💾", "Memoria RAM", $"{totalGb:0} GB {tipo}{(velocidad > 0 ? $" a {velocidad} MHz" : "")}", detalle,
            errores == 0 ? Estado.Bien : Estado.Atencion,
            errores == 0 ? "Sin errores registrados" : $"{errores} error(es) de memoria en {DiasHistorial} días. Si se repiten, revisa la RAM.");
    }

    static void LeerPantalla(Action<string, string, string, string, Estado, string> agregar)
    {
        var video = Wmi.Consultar("SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController")
                       .FirstOrDefault(v => Wmi.Numero(v, "CurrentHorizontalResolution") > 0);
        var resolucion = video == null ? "" :
            $"{Wmi.Numero(video, "CurrentHorizontalResolution")}×{Wmi.Numero(video, "CurrentVerticalResolution")} a {Wmi.Numero(video, "CurrentRefreshRate")} Hz";

        var ids = Wmi.Consultar("SELECT InstanceName, ManufacturerName, UserFriendlyName FROM WmiMonitorID", @"root\wmi");
        var medidas = Wmi.Consultar("SELECT InstanceName, MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", @"root\wmi");

        if (ids.Count == 0)
        {
            if (resolucion.Length > 0) agregar("🖥", "Pantalla", resolucion, "", Estado.Bien, "Funciona bien");
            return;
        }

        foreach (var id in ids)
        {
            var fabricante = FabricantePanel(Decodificar(id["ManufacturerName"]));
            var modelo = Decodificar(id["UserFriendlyName"]);

            var medida = medidas.FirstOrDefault(m => Wmi.Texto(m, "InstanceName") == Wmi.Texto(id, "InstanceName"));
            var pulgadas = medida == null ? 0 :
                Math.Sqrt(Math.Pow(Wmi.Numero(medida, "MaxHorizontalImageSize"), 2) + Math.Pow(Wmi.Numero(medida, "MaxVerticalImageSize"), 2)) / 2.54;

            // El tamaño que informa la pantalla viene redondeado en cm; se ajusta al tamaño comercial más cercano.
            var comercial = TamanosComerciales.MinBy(t => Math.Abs(t - pulgadas));
            if (Math.Abs(comercial - pulgadas) <= 0.5) pulgadas = comercial;
            var nombre = (pulgadas > 5 ? $"{pulgadas:0.#}\" " : "") + resolucion;
            var detalle = $"Panel fabricado por {fabricante}" + (modelo.Length > 0 ? $" ({modelo})" : "");
            agregar("🖥", "Pantalla", nombre.Trim(), detalle, Estado.Bien, "Funciona bien");
        }
    }

    // WmiMonitorID guarda los textos como arreglos de códigos de caracteres.
    static string Decodificar(object? valor) =>
        valor is ushort[] codigos ? new string(codigos.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim() : "";

    static string FabricantePanel(string codigo) => codigo switch
    {
        "CSW" or "CSO" => "CSOT (China Star)",
        "BOE" => "BOE",
        "AUO" => "AU Optronics",
        "LGD" => "LG Display",
        "SDC" or "SAM" => "Samsung",
        "CMN" => "Innolux",
        "SHP" => "Sharp",
        "IVO" => "InfoVision",
        "" => "desconocido",
        _ => codigo,
    };

    static readonly double[] TamanosComerciales = { 11.6, 13.3, 13.5, 14, 15.6, 16, 17.3, 18 };

    static string Capitalizar(string s) =>
        s.Length <= 4 ? s.ToUpper() : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLower());
}
