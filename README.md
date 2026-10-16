# Salud del Notebook

App de escritorio (WPF, .NET 10) que revisa el estado de un notebook con Windows y lo muestra con un semáforo fácil de entender.

## Revisiones

- **Componentes**: inventario de piezas (placa, CPU, RAM, GPU, disco, pantalla, red, audio, cámara) y si Windows las ve funcionando; errores de hardware (WHEA).
- **Batería**: vida útil (capacidad actual vs. original) y ciclos de carga.
- **Disco**: estado SMART, desgaste del SSD, errores y espacio libre.
- **Temperatura**: sensor del sistema y tarjeta gráfica NVIDIA.
- **Rendimiento**: RAM, procesador, días sin reiniciar y programas de inicio.
- **Drivers**: dispositivos con error, driver de video y drivers antiguos.
- **Actualizaciones**: pendientes en Windows Update (importantes y opcionales), con instalación desde la app.
- **Fallos**: pantallazos azules (lee los minidumps para encontrar el driver culpable) y apagados inesperados.

## Herramientas

- **Modo juego**: mide temperaturas mientras juegas y da un veredicto.
- **Liberar espacio**: temporales, papelera, restos de Windows Update, caché de sombreadores, etc.
- **Programas**: cerrar programas abiertos sin uso y activar/desactivar los de inicio.
- **Historial**: un registro por día (batería, disco, temperaturas) con gráficos.
- **Ajustes**: revisión semanal automática con aviso, acceso directo que abre sin pedir permiso (tarea programada) e informe PDF (QuestPDF).

Pide permisos de administrador (para temperaturas, desgaste del disco, minidumps y tareas programadas).

## Ejecutar

```
dotnet run
```

## Instalar

```
dotnet publish -c Release -o "%LOCALAPPDATA%\Programs\SaludNotebook"
"%LOCALAPPDATA%\Programs\SaludNotebook\SaludNotebook.exe" --configurar
```

`--configurar` crea el acceso directo (escritorio y menú Inicio) y la revisión semanal. `--semanal` es lo que ejecuta la tarea programada.
