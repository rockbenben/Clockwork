<div align="center">

<img src="../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Pon en piloto automático las tareas repetitivas de tu PC**

Abre tus aplicaciones automáticamente al iniciar sesión · recordatorios programados · un toque para ejecutar toda una rutina

</div>

<div align="center">

[English](../README.md) · [简体中文](README.zh-CN.md) · [繁體中文](README.zh-TW.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · **Español** · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

> Plan de Código Abierto 365 #020 · Una herramienta de bandeja para Windows: lanzador de inicio · recordatorios · elementos de inicio del sistema · grupos de acciones

![Clockwork](../assets/social-card.png)

Una pequeña herramienta de bandeja para Windows que se encarga de las partes rutinarias de empezar tu día frente al ordenador:

- 🚀 **Lista de inicio** — abre automáticamente tus aplicaciones de cada día al iniciar sesión, en orden (permisos de administrador por paso, retardos, solo-en-ciertos-días-de-la-semana / solo-antes-de-las-N-en-punto, estilo de ventana, activar-si-ya-se-está-ejecutando, rutas de reserva), y realiza algunas tareas por el camino (cerrar o enfocar ventanas, enviar pulsaciones de teclas / texto, ajustar el volumen…).
- ⏰ **Recordatorios** — muestra un recordatorio a su hora; léelo en voz alta; repítelo por día de la semana / cada-N-días / mensualmente; o actívalo «al iniciar sesión». Al pulsar **Sí** se puede ejecutar un programa, abrir un archivo (p. ej. música) o una URL, o ejecutar un grupo de acciones.
- 🧹 **Elementos de inicio del sistema** — lista **todo lo que se inicia automáticamente en tu PC** y desactiva lo que no necesites (desactivado, no eliminado — vuelve a activarlo cuando quieras). Con un clic «asumes el control» de un elemento y lo pasas a tu propia lista de inicio.
- 🎛️ **Grupos de acciones** — agrupa una serie de acciones en un grupo reutilizable (Concentración / Reunión / Cierre / Antes de dormir…) y actívalo con un clic desde la bandeja, la lista de inicio o un recordatorio. Incluye plantillas integradas.

Sin instalación, totalmente portátil en una sola carpeta, todo configurable con el ratón; interfaz oscura, compatible con alta resolución (high-DPI).

## Requisitos

- Windows 10 / 11 (x64)
- Nada que instalar: un único archivo autónomo `Clockwork.exe` con el entorno de ejecución de .NET incluido.

## Primeros pasos

1. Descarga el último `Clockwork.exe` desde [Releases](https://github.com/rockbenben/Clockwork/releases) y colócalo en cualquier carpeta (portátil — ponlo donde quieras). Para compilarlo tú mismo, consulta **Para desarrolladores** más abajo.
2. Haz doble clic en **`Clockwork.exe`** para abrir la ventana de configuración.
   - En la **primera ejecución** carga una **configuración de ejemplo** (que muestra inicio / recordatorios / grupos de acciones) para que la adaptes a la tuya. Tu configuración vive en `clockwork.settings.json` junto al exe — solo local, nunca se sube al repositorio.
3. Para ejecutarlo en cada arranque: en la pestaña **Ajustes**, haz clic en **Iniciar al arrancar sesión** (registra una tarea programada con permisos de administrador, así no hay una avalancha de avisos de UAC al arrancar).

> Se queda tranquilo en la bandeja. Haz doble clic en el icono de la bandeja para abrir la ventana; el botón de cerrar de la ventana solo la oculta en la bandeja. Para salir de verdad, usa **Salir** en el clic derecho de la bandeja.

## Captura de pantalla

![Captura de pantalla](../assets/screenshot.png)

## Las cinco pestañas

### Lista de inicio
Una **lista ordenada de pasos** que se ejecutan de arriba abajo al iniciar sesión. Haz clic en **Añadir ▾** para elegir un tipo; añade, quita y reordena libremente; cada paso se puede habilitar/deshabilitar, asignarle un **retardo posterior al paso**, un **número de repeticiones** (repetirlo N veces) y condiciones (**solo en ciertos días de la semana / solo antes de las N en punto**). Tipos de paso:

- **Ejecutar programa** — destino (**Examinar…** para elegir un archivo) / argumentos / directorio de trabajo (déjalo en blanco = carpeta del destino) / administrador. El destino puede ser un `.exe`, un documento, un acceso directo o una URL; un `.ps1` se ejecuta mediante PowerShell. Avanzado: **estilo de ventana** (minimizada / maximizada / oculta), **activar si ya se está ejecutando** (traerla al frente en vez de relanzarla; nombre del proceso mediante **Elegir…**), **rutas de reserva** (una ruta completa por línea; se usa la primera que exista — útil cuando las rutas de instalación difieren entre equipos).
- **Enviar teclas** — p. ej. Win+D, Alt+K, Ctrl+Enter, F5 (**Capturar** para registrar un atajo pulsándolo).
- **Enviar texto** — escribe una cadena en la ventana enfocada (o en un **proceso de destino** elegido mediante **Elegir…**).
- **Volumen** — silenciar / quitar silencio / fijar el nivel.
- **Acción de ventana** — por nombre de proceso (**Elegir…**, con búsqueda): cerrar / minimizar / maximizar / traer-al-frente / traer-al-frente-y-enviar-teclas; las aplicaciones lentas pueden **esperar hasta N segundos a que aparezca la ventana**.
- **Comando del sistema** — mostrar el escritorio / bloquear / apagar el monitor / vaciar la papelera de reciclaje / borrar el portapapeles / abrir Configuración / Administrador de tareas / captura de pantalla / suspender / hibernar / cerrar sesión / reiniciar / apagar (los tres últimos piden confirmación primero).
- **Retardo** — simplemente espera N segundos antes del siguiente paso.
- **Grupo de acciones** — ejecuta un grupo de acciones definido; fija un número de repeticiones para repetir todo el grupo.

> **Retardo de inicio** (pestaña Ajustes, solo en el arranque): espera un número fijo de segundos tras iniciar sesión para que pase la «tormenta de inicio» (contención de disco/CPU de todos los programas que arrancan automáticamente) antes de que se ejecute la lista; una re-ejecución manual no se ve afectada. Súbelo (0–600 s) si las cosas arrancan demasiado pronto.

> **Detén cuando quieras** — bandeja → **Detener acciones en ejecución**, o el **atajo de pánico** global (se configura en la pestaña Ajustes; por defecto `Ctrl+Alt+Q`). Lo que se esté ejecutando se detiene tras la acción actual; las esperas largas (retardo de inicio, esperar a una ventana) se interrumpen de inmediato.

### Recordatorios
Fija una **hora** (o cambia a **al iniciar sesión**), una **periodicidad** (días de la semana / cada-N-días / mensual) y el **texto**; opcionalmente léelo en voz alta. Los recordatorios con una acción **Al-pulsar-Sí** (ejecutar programa / abrir archivo / URL / ejecutar grupo de acciones) muestran un diálogo **Sí / No** con un botón **Posponer** (por defecto 10 min, menú ▾ de 5–60 min); el resto se deslizan como una **tarjeta de recordatorio** en la esquina (se cierra sola tras los segundos configurados, **0 = permanece hasta que la descartes**). También puedes fijar un **grupo de acciones silencioso** — ejecuta un grupo a su hora sin ninguna ventana emergente.

Avanzado: **cierre automático**, **insistencia repetida** (vuelve a saltar cada N minutos hasta un plazo límite), **retardo posterior al disparo + variación aleatoria**, **margen de gracia** (recupera un disparo perdido por un breve apagado/suspensión), **recuperar si se perdió** (vuelve a dispararse una vez si la hibernación/apagado lo saltó) y una **fecha de anclaje** para cada-N-días (**Elegir fecha**). «Disparado hoy» y «pospuesto hasta» sobreviven a los reinicios (`clockwork.state.json`), así que una posposición se conserva tras un reinicio y nada se dispara dos veces.

¿Necesitas concentrarte o atender una reunión? La bandeja ofrece **Pausar recordatorios durante 1 / 2 / 4 horas** (No molestar): todo (incluidos los grupos silenciosos) se suprime y se reanuda automáticamente cuando se acaba el tiempo.

### Elementos de inicio del sistema
Lista **todo lo que se inicia automáticamente** (claves Run del registro, carpetas de Inicio, tareas programadas). Desmarca **Habilitar** para desactivar un elemento — **desactivado, no eliminado; vuelve a marcarlo para restaurarlo** (surte efecto de inmediato). Los elementos marcados como **requiere administrador** piden relanzar con permisos elevados. Los elementos de sistema / directiva / de una sola vez (Run de directiva de grupo, RunOnce, Winlogon, Active Setup) no se pueden alternar de forma normal y están **ocultos por defecto** — marca **Mostrar elementos de sistema / de solo lectura** para verlos (atenuados). **Asumir el control en la lista de inicio** entrega un elemento a Clockwork (solo claves Run del registro y elementos de la carpeta de Inicio). Un **filtro** en la parte superior busca por nombre / comando; pasa el cursor sobre un comando truncado para leerlo completo.

### Grupos de acciones
Agrupa acciones en un grupo reutilizable. **Añadir ▾** inicia uno a partir de una **plantilla integrada** (Concentración / Reunión / Cierre / Antes de dormir / Ausentarse / Captura de pantalla) — ajusta los nombres de los procesos y guarda. Un grupo **solo define acciones**; actívalo de tres maneras: desde la bandeja (**Ejecutar: <grupo>**), como un **paso de grupo de acciones** en la lista de inicio (en el arranque) o desde un recordatorio (**Al-pulsar-Sí / grupo silencioso**). Un grupo ejecuta solo una copia a la vez; un paso de **mensaje** puede actuar como una puerta de confirmación (responder **No** aborta el resto).

### Ajustes
**Retardo de inicio** (0–600 s, solo en el arranque), **iniciar minimizado en la bandeja**, **atajo de pánico** (haz clic en el cuadro y pulsa tu atajo; Esc cancela, Supr lo borra; por defecto `Ctrl+Alt+Q`) e **idioma de la interfaz** (chino simplificado, inglés, 日本語 y 15 más — 18 en total; cambiarlo reinicia la aplicación para aplicarlo).

## Consejos

- **Haz doble clic en una fila para editarla**. Al rellenar rutas / procesos / atajos / fechas no tienes que escribir a mano: **Examinar…**, **Elegir…** (selector de procesos con búsqueda), **Capturar** y **Elegir fecha**.
- Hacer doble clic en `Clockwork.exe` solo abre los ajustes — **no** ejecuta de inmediato la lista de inicio; para eso usa **Re-ejecutar lista de inicio** de la bandeja.
- **Láncalo con normalidad** (doble clic / bandeja / tarea programada). Algunos lanzadores de sandbox / privilegios reducidos bloquean las llamadas de bajo nivel, por lo que enviar-teclas / acciones de ventana / activar-si-ya-se-está-ejecutando / enviar-texto-a-proceso / volumen podrían no funcionar (recibirás un aviso claro; el simple «ejecutar programa» no se ve afectado).
- Tu configuración es `clockwork.settings.json` (solo local). Bórrala para restablecer al ejemplo. El estado de los recordatorios es `clockwork.state.json` (también local; se puede borrar sin problema).
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
- **CI / releases** (GitHub Actions): las compilaciones de push / PR construyen y ejecutan todas las pruebas en un runner de Windows; al subir una etiqueta `v*` (p. ej. `v2.0.0`) se compila, se sella la versión del archivo a partir de la etiqueta, se crea un Release de GitHub y se adjunta `Clockwork.exe`.

## Acerca del Plan de Código Abierto 365

Este es el proyecto #20 del [Plan de Código Abierto 365](https://github.com/rockbenben/365opensource) — una persona + IA, más de 300 proyectos de código abierto en un año. [Envía una solicitud →](https://my.feishu.cn/share/base/form/shrcnI6y7rrmlSjbzkYXh6sjmzb)

## Licencia

[MIT](../LICENSE) © rockbenben
