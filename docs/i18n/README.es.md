<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Pon en piloto automático las tareas repetitivas de tu PC**

Abre tus aplicaciones automáticamente al iniciar sesión · recordatorios programados · un toque para ejecutar toda una rutina

**[⬇ Descargar para Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, sin instalador

[![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · **Español** · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Una herramienta de bandeja para Windows: lanzador de inicio · recordatorios · elementos de inicio del sistema · grupos de acciones

![Clockwork](../../assets/social-card.png)

Una pequeña herramienta de bandeja para Windows que se encarga de las partes rutinarias de empezar tu día frente al ordenador:

- 🚀 **Lista de inicio** — abre automáticamente tus aplicaciones de cada día al iniciar sesión, en orden (permisos de administrador por paso, retardos, solo-en-ciertos-días-de-la-semana / solo-antes-de-las-N-en-punto, estilo de ventana, activar-si-ya-se-está-ejecutando, rutas de reserva), y realiza algunas tareas por el camino (cerrar o enfocar ventanas, enviar pulsaciones de teclas / texto, ajustar el volumen…).
- ⏰ **Tareas programadas** — muestra un recordatorio a su hora; léelo en voz alta; repítelo por día de la semana / cada-N-días / mensualmente; o actívalo «al iniciar sesión». Al pulsar **Sí** se puede ejecutar un programa, abrir un archivo (p. ej. música) o una URL, o ejecutar un grupo de acciones. También admite ejecuciones por intervalos y la programación de una sola vez.
- 🧹 **Elementos de inicio del sistema** — lista **todo lo que se inicia automáticamente en tu PC** y desactiva lo que no necesites (desactivado, no eliminado — vuelve a activarlo cuando quieras). Con un clic «asumes el control» de un elemento y lo pasas a tu propia lista de inicio.
- 🎛️ **Grupos de acciones** — agrupa una serie de acciones en un grupo reutilizable (Concentración / Reunión / Cierre / Antes de dormir…) y actívalo con un clic desde la bandeja, un **atajo global**, la lista de inicio o un recordatorio. Incluye plantillas integradas.

Sin instalación, totalmente portátil en una sola carpeta, todo configurable con el ratón; interfaz oscura, compatible con alta resolución (high-DPI).

> 📖 **Guía completa:** [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Requisitos

- Windows 10 / 11 (x64)
- Nada que instalar: un único archivo autónomo `Clockwork.exe` con el entorno de ejecución de .NET incluido.

## Primeros pasos

1. Descarga el último `Clockwork-<versión>.zip` desde [Releases](https://github.com/rockbenben/Clockwork/releases) y descomprímelo — dentro hay un único `Clockwork.exe`; colócalo en cualquier carpeta (portátil — ponlo donde quieras). Para compilarlo tú mismo, consulta **Para desarrolladores** más abajo.
2. Haz doble clic en **`Clockwork.exe`** para abrir la ventana de configuración.
   - En la **primera ejecución** carga unos cuantos **ejemplos** en la lista de inicio y en los recordatorios para que los adaptes a los tuyos — todos vienen sin marcar, así que no se ejecuta nada hasta que tú lo marques. La pestaña **Grupos de acciones** también arranca con dos grupos listos para usar (Ausente un momento / Fin de jornada) — esos sí vienen *marcados*, porque un grupo nunca se dispara por sí solo; solo se ejecuta cuando tú lo activas. Tu configuración vive en `clockwork.settings.json` junto al exe — solo local, nunca se sube al repositorio.
3. Para ejecutarlo en cada arranque: en la pestaña **Ajustes**, haz clic en **Iniciar al arrancar sesión** (registra una tarea programada con permisos de administrador, así no hay una avalancha de avisos de UAC al arrancar).

> Se queda tranquilo en la bandeja. Haz doble clic en el icono de la bandeja para abrir la ventana; el botón de cerrar de la ventana solo la oculta en la bandeja. Para salir de verdad, usa **Salir** en el clic derecho de la bandeja.

> **La primera vez saldrá una advertencia: es normal.** El exe no está firmado, así que SmartScreen muestra «Windows protegió su PC» — haz clic en **Más información → Ejecutar de todas formas**. Algún antivirus también puede alertar: escribir claves Run del registro y tareas programadas es justo lo que hace un gestor de arranque… y también lo que hace el malware; desde fuera no se distinguen. Si prefieres no aceptarlo por confianza, compílalo tú mismo siguiendo **Para desarrolladores** más abajo: mismo resultado, binario propio.

## Captura de pantalla

![Captura de pantalla](../../assets/screenshot.png)

## Las cinco pestañas

Cinco pestañas; cada campo se explica una por una en la [guía completa](../USAGE.md).

- **Lista de inicio** — los pasos se ejecutan de arriba abajo al iniciar sesión. Tipos: ejecutar programa · enviar teclas · enviar texto · volumen · acción de ventana · comando del sistema · grupo de acciones · espera · mensaje. Cada paso admite una espera posterior, un número de repeticiones y condiciones (solo ciertos días / solo antes de las N); los programas además admin, estilo de ventana, activar-si-ya-se-ejecuta y rutas alternativas.
- **Tareas programadas** — una hora (o «al iniciar sesión») × una recurrencia (día de la semana / cada N días / mensual / una vez) × una acción: un recordatorio (diálogo Sí/No con posponer, o una tarjeta en la esquina, con lectura en voz alta opcional) o un grupo de acciones ejecutado en silencio. Además ejecuciones por intervalos, insistencia repetida, recuperación de disparos perdidos y No molestar desde la bandeja.
- **Elementos de inicio del sistema** — todo lo que arranca solo en tu PC (claves Run del registro, carpetas de Inicio, tareas programadas): desactivarlo (deshabilitado, no borrado), traspasarlo a tu propia lista de inicio o eliminarlo definitivamente.
- **Grupos de acciones** — un paquete reutilizable de acciones, disparado desde la bandeja, un **atajo global** (púlsalo otra vez para cancelar esa ejecución), un paso de la lista de inicio o una tarea programada. Un grupo puede repetirse por completo y referenciar otros grupos (las referencias circulares se rechazan al guardar); un paso de **mensaje** corta el resto con Sí / No.
- **Ajustes** — retardo de inicio (0–600 s, solo en el arranque), iniciar minimizado en la bandeja, iniciar al arrancar sesión, atajo de pánico, idioma de la interfaz (18), exportar / importar configuración.

> **Detenlo cuando quieras** — el **botón de detención** al final de la barra de pestañas (solo aparece mientras algo se ejecuta), bandeja → **Detener acciones en ejecución** o el **atajo de pánico** global (por defecto `Ctrl+Alt+Q`). Las esperas largas (retardo de inicio, esperar una ventana) se interrumpen de inmediato.

## Consejos

- **Haz doble clic en una fila para editarla**. Al rellenar rutas / procesos / atajos / fechas no tienes que escribir a mano: **Examinar…**, **Elegir…** (selector de procesos con búsqueda), **Capturar** y **Elegir fecha**.
- **Arrastra una fila para reordenarla** — en las tres listas (lista de inicio, tareas programadas, grupos de acciones) y en la lista de pasos del editor de grupos; los botones de subir/bajar siguen funcionando.
- **Pruébalo antes de guardar** — el editor de grupos tiene **▶ Ejecutar este paso** y **▶ Ejecutar grupo**, y ambos ejecutan lo que hay ahora en pantalla. Durante la ejecución el botón se convierte en **■ Detener**, y cerrar el editor también la detiene.
- **Duplicar** (pestañas Tareas programadas / Grupos de acciones) clona la fila seleccionada justo debajo de ella — más rápido que rehacer una casi idéntica; un grupo duplicado se llama «… (copia)».
- **Eliminar siempre pide confirmación**, en todas partes — filas de las listas, pasos dentro del editor de grupos y elementos de inicio del sistema.
- Hacer doble clic en `Clockwork.exe` solo abre los ajustes — **no** ejecuta de inmediato la lista de inicio; para eso usa **Re-ejecutar lista de inicio** de la bandeja.
- **Láncalo con normalidad** (doble clic / bandeja / tarea programada). Algunos lanzadores de sandbox / privilegios reducidos bloquean las llamadas de bajo nivel, por lo que enviar-teclas / acciones de ventana / activar-si-ya-se-está-ejecutando / enviar-texto-a-proceso / volumen podrían no funcionar (recibirás un aviso claro; el simple «ejecutar programa» no se ve afectado).
- Tu configuración es `clockwork.settings.json` (solo local). Bórrala para restablecer al ejemplo. El estado de las tareas es `clockwork.state.json` (también local; se puede borrar sin problema).
- Añadir un paso `.ahk` requiere tener AutoHotkey instalado. Los atajos globales / la expansión de texto quedan fuera del alcance — esa es la fortaleza de AutoHotkey.

## Para desarrolladores

C#/.NET WPF; código fuente en `app/` (necesita el SDK de .NET 10). Capas: `Core/` lógica pura · `Native/` interoperabilidad Win32 · `Engine/` ejecución · `ViewModels/` + `Views/` interfaz · `I18n/` + `Resources/` localización (neutral = fuente en chino, un satélite `Strings.<code>.resx` por idioma).

- Ejecutar las pruebas (xUnit):
  ```powershell
  dotnet test app.Tests/Clockwork.Tests.csproj
  ```
- Compilar el exe autónomo de un solo archivo (single-file / self-contained / compresión se configuran en el csproj):
  ```powershell
  dotnet publish app/Clockwork.csproj -c Release -r win-x64
  ```
  Salida: `app/bin/Release/net10.0-windows/win-x64/publish/Clockwork.exe`.
- **CI / releases** (GitHub Actions): las compilaciones de push / PR construyen y ejecutan todas las pruebas en un runner de Windows; al subir una etiqueta `v*` (p. ej. `v2.0.0`) se compila, se sella la versión del archivo a partir de la etiqueta, se crea un Release de GitHub y se adjunta `Clockwork-<tag>.zip` (que contiene `Clockwork.exe`).

## Sobre el Plan 365 de código abierto

Proyecto **#020** del [Plan 365 de código abierto](https://github.com/rockbenben/365opensource) — una persona + IA, más de 300 proyectos de código abierto en un año.

[Envía tu idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)