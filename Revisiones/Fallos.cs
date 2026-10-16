using System.IO;

namespace SaludNotebook.Revisiones;

// Pantallazos azules y apagados inesperados, con su causa explicada.
static class Fallos
{
    const int DiasHistorial = 90;
    const int DiasRecientes = 30;
    const string CarpetaVolcados = @"C:\Windows\Minidump";

    public static (Seccion, List<Fallo>) Revisar()
    {
        var s = new Seccion { Nombre = "Fallos", Emoji = "💥" };
        var lista = new List<Fallo>();

        // 1. Pantallazos azules con archivo de volcado (minidump).
        var volcados = new List<(DateTime Fecha, uint Codigo)>();
        try
        {
            foreach (var archivo in Directory.GetFiles(CarpetaVolcados, "*.dmp"))
            {
                var fecha = File.GetLastWriteTime(archivo);
                if (fecha < DateTime.Now.AddDays(-DiasHistorial)) continue;
                var v = Volcado.Analizar(archivo);
                if (v == null) continue;
                volcados.Add((fecha, v.Codigo));
                lista.Add(DePantallazo(fecha, v));
            }
        }
        catch
        {
        }

        // 2. Apagados inesperados registrados por Windows (evento 41).
        foreach (var ev in Eventos.Leer("Microsoft-Windows-Kernel-Power", DiasHistorial, 41))
        {
            uint.TryParse(ev.Datos.GetValueOrDefault("BugcheckCode"), out var codigo);
            // Si ya está explicado por un volcado de la misma hora, no se repite.
            if (codigo != 0 && volcados.Any(v => Math.Abs((v.Fecha - ev.Fecha).TotalHours) < 2)) continue;
            lista.Add(DeApagado(ev, codigo));
        }

        lista.Sort((a, b) => b.Fecha.CompareTo(a.Fecha));

        // Resumen para la portada.
        var recientes = lista.Where(f => f.Fecha >= DateTime.Now.AddDays(-DiasRecientes)).ToList();
        var pantallazos = recientes.Count(f => f.Tipo.StartsWith("Pantallazo"));
        var otros = recientes.Count(f => f.Estado == Estado.Atencion && !f.Tipo.StartsWith("Pantallazo"));

        s.Agregar($"Pantallazos azules (últimos {DiasRecientes} días)", pantallazos == 0 ? "Ninguno" : $"{pantallazos}",
            pantallazos switch { 0 => Estado.Bien, < 3 => Estado.Atencion, _ => Estado.Problema },
            pantallazos == 0
                ? "Windows no se ha caído."
                : "Windows se cayó con pantallazo azul. Abajo ves la causa de cada uno y qué hacer.");
        s.Agregar($"Otros apagados inesperados (últimos {DiasRecientes} días)", $"{otros}",
            otros >= 3 ? Estado.Atencion : Estado.Bien,
            otros switch
            {
                0 => "El notebook no se ha apagado de golpe.",
                < 3 => "Pocas veces. Suele pasar si se quedó sin batería, se colgó y lo forzaste, o falló la suspensión.",
                _ => "Se apagó de golpe varias veces. Mira abajo en qué situación pasó cada uno.",
            });

        return (s, lista);
    }

    static Fallo DePantallazo(DateTime fecha, Volcado.Resultado v)
    {
        var (nombreError, queEs) = Codigos.GetValueOrDefault(v.Codigo, ("Error del sistema", "Windows encontró un error grave y tuvo que reiniciar."));
        var causa = v.Culpable == null ? "No se pudo identificar el driver" : Amigable(v.Culpable);
        return new Fallo
        {
            Fecha = fecha,
            Tipo = "Pantallazo azul",
            Codigo = $"0x{v.Codigo:X} {nombreError}",
            Causa = causa,
            Explicacion = $"{queEs} {Consejo(v.Culpable, v.Codigo)}",
            Estado = fecha >= DateTime.Now.AddDays(-DiasRecientes) ? Estado.Atencion : Estado.Info,
        };
    }

    static Fallo DeApagado(Eventos.Evento ev, uint codigo)
    {
        // SleepInProgress = suspensión clásica; CsEntryScenarioInstanceId = "espera moderna" (tapa cerrada en notebooks nuevos).
        bool Activo(string campo) => ev.Datos.GetValueOrDefault(campo) is { } v && v != "0" && !v.Equals("false", StringComparison.OrdinalIgnoreCase);
        var enSuspension = Activo("SleepInProgress") || Activo("CsEntryScenarioInstanceId");
        var conBoton = ev.Datos.GetValueOrDefault("PowerButtonTimestamp") is { } pb && pb != "0";
        var reciente = ev.Fecha >= DateTime.Now.AddDays(-DiasRecientes);

        if (codigo != 0)
        {
            var (nombreError, queEs) = Codigos.GetValueOrDefault(codigo, ("Error del sistema", "Windows encontró un error grave."));
            return new Fallo
            {
                Fecha = ev.Fecha, Tipo = "Pantallazo azul (sin archivo)", Codigo = $"0x{codigo:X} {nombreError}",
                Causa = "Windows no guardó el archivo del error",
                Explicacion = $"{queEs} Sin el archivo no se sabe qué driver fue; si se repite, actualiza los drivers de video y chipset.",
                Estado = reciente ? Estado.Atencion : Estado.Info,
            };
        }
        if (conBoton)
            return new Fallo
            {
                Fecha = ev.Fecha, Tipo = "Apagado con el botón", Causa = "Lo apagaste manteniendo el botón de encendido",
                Explicacion = "Si lo hiciste porque se había colgado, fíjate si se repite. Si no, no es un problema.",
                Estado = Estado.Info,
            };
        if (enSuspension)
            return new Fallo
            {
                Fecha = ev.Fecha, Tipo = "Falla en suspensión", Causa = "Se apagó mientras estaba suspendido",
                Explicacion = "Pasa cuando el notebook no logra despertar de la suspensión (tapa cerrada / reposo). Es común en notebooks y no daña nada. Mantener BIOS y drivers al día ayuda.",
                Estado = Estado.Info,
            };
        return new Fallo
        {
            Fecha = ev.Fecha, Tipo = "Apagado de golpe", Causa = "Se apagó sin dejar rastro",
            Explicacion = "Puede ser corte de luz o batería agotada, que se colgó y lo forzaste, o un sobrecalentamiento. Si pasa jugando, revisa la temperatura con \"Modo juego\".",
            Estado = reciente ? Estado.Atencion : Estado.Info,
        };
    }

    // Nombre comprensible para el archivo del driver.
    static string Amigable(string modulo)
    {
        var m = modulo.ToLowerInvariant();
        string? Nombre() => m switch
        {
            _ when m.StartsWith("nvlddmkm") => "Driver de video NVIDIA",
            _ when m.StartsWith("amdkmdag") || m.StartsWith("atikmdag") || m.StartsWith("amdkmpfd") => "Driver de video AMD",
            _ when m.StartsWith("igdkmd") => "Driver de video Intel",
            _ when m.StartsWith("dxgkrnl") || m.StartsWith("dxgmms") || m.StartsWith("watchdog") => "Sistema de gráficos de Windows (DirectX)",
            _ when m.StartsWith("rtw") || m.StartsWith("rtwlan") => "Driver WiFi Realtek",
            _ when m.StartsWith("netwtw") || m.StartsWith("netwbw") => "Driver WiFi Intel",
            _ when m.StartsWith("mtkwl") || m.StartsWith("mt76") => "Driver WiFi MediaTek",
            _ when m.StartsWith("rt640") || m.StartsWith("rtux") || m.StartsWith("rtcx") => "Driver de red por cable Realtek",
            _ when m.StartsWith("rtkvhd") => "Driver de audio Realtek",
            _ when m.StartsWith("nahimic") => "Nahimic (audio)",
            _ when m.StartsWith("vigembus") => "ViGEmBus (emulador de mandos)",
            _ when m.StartsWith("aehd") => "Android Emulator Hypervisor (Google Play Juegos / emuladores Android)",
            _ when m.StartsWith("vgk") => "Anti-trampas Vanguard (Valorant / LoL)",
            _ when m.StartsWith("easyanticheat") => "Anti-trampas EasyAntiCheat",
            _ when m.StartsWith("bedaisy") => "Anti-trampas BattlEye",
            _ when m.StartsWith("ntfs") => "Sistema de archivos del disco",
            _ when m.StartsWith("stornvme") || m.StartsWith("storport") => "Controlador del SSD",
            _ when m.StartsWith("usbxhci") || m.StartsWith("usbhub") || m.StartsWith("ucx") => "Puertos USB",
            _ when m.StartsWith("tcpip") || m.StartsWith("ndis") => "Red de Windows",
            _ when m.StartsWith("amdppm") || m.StartsWith("amdpsp") || m.StartsWith("amdgpio") || m.StartsWith("amdi2c") => "Driver de chipset AMD",
            _ when m.StartsWith("ntoskrnl") || m.StartsWith("hal.") => "Núcleo de Windows",
            _ => null,
        };
        return Nombre() is { } n ? $"{n} ({modulo})" : modulo;
    }

    static string Consejo(string? modulo, uint codigo)
    {
        var m = modulo?.ToLowerInvariant() ?? "";
        if (m.StartsWith("nvlddmkm"))
            return "Actualiza el driver con la app NVIDIA eligiendo \"instalación limpia\". Si se repite siempre con el mismo juego, puede ser ese juego.";
        if (m.StartsWith("amdkmdag") || m.StartsWith("atikmdag") || m.StartsWith("igdkmd") || m.StartsWith("dxg"))
            return "Actualiza el driver de video desde la página del fabricante.";
        if (m.StartsWith("rtw") || m.StartsWith("netw") || m.StartsWith("mtk") || m.StartsWith("mt76"))
            return "Actualiza el driver WiFi desde la app o página de soporte del fabricante, o desde Windows Update.";
        if (m.StartsWith("vgk") || m.StartsWith("easyanticheat") || m.StartsWith("bedaisy"))
            return "Es el anti-trampas de un juego. Actualiza o reinstala ese juego.";
        if (m.StartsWith("aehd") || m.StartsWith("vigembus"))
            return "Es de un emulador. Actualiza o reinstala el programa que lo instaló.";
        if (codigo is 0x124 or 0x1A or 0x50 or 0x7A or 0x101)
            return "Este tipo de error a veces es por la RAM o el hardware: pasa el \"Diagnóstico de memoria\" (botón de arriba).";
        if (m.StartsWith("ntoskrnl") || m.Length == 0)
            return "No apunta a un driver claro. Mantén Windows y los drivers al día; si se repite seguido, pasa el diagnóstico de memoria.";
        return "Actualiza el programa o driver que lo instaló.";
    }

    static readonly Dictionary<uint, (string, string)> Codigos = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "Un driver intentó usar memoria a la que no tenía acceso."),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Un driver intentó usar memoria a la que no tenía acceso."),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "Un driver cometió un error que Windows no pudo manejar."),
        [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Un driver cometió un error que Windows no pudo manejar."),
        [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "Un driver o componente de Windows falló."),
        [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "Se intentó leer memoria que no existía."),
        [0x1A] = ("MEMORY_MANAGEMENT", "Hubo un problema administrando la memoria RAM."),
        [0x19] = ("BAD_POOL_HEADER", "Un driver dañó la memoria del sistema."),
        [0xC2] = ("BAD_POOL_CALLER", "Un driver usó mal la memoria del sistema."),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "No se pudo leer algo del disco a la memoria."),
        [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "Un driver no respondió al suspender o despertar el equipo."),
        [0x116] = ("VIDEO_TDR_FAILURE", "La tarjeta gráfica dejó de responder y no se pudo recuperar."),
        [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "La tarjeta gráfica dejó de responder."),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "El hardware reportó un error grave (procesador, RAM o placa)."),
        [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", "Un núcleo del procesador dejó de responder."),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", "Un driver se quedó pegado demasiado tiempo."),
        [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "Windows detectó datos dañados en memoria."),
        [0xEF] = ("CRITICAL_PROCESS_DIED", "Un proceso esencial de Windows se cerró."),
        [0x154] = ("UNEXPECTED_STORE_EXCEPTION", "Hubo un problema leyendo del disco."),
    };
}
