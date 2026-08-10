<div align="center">

<img src="../../assets/logo-256.png" width="112" alt="Clockwork">

# Clockwork

**Pon en piloto automático las tareas repetitivas de tu PC**

Abre tus aplicaciones automáticamente al iniciar sesión · recordatorios programados · un toque para ejecutar toda una rutina

**[⬇ Descargar para Windows](https://github.com/rockbenben/Clockwork/releases/latest)** — portable, sin instalador

[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](../../LICENSE) [![365 Open Source Plan #020](https://img.shields.io/badge/365%20Open%20Source%20Plan-%23020-1f6feb)](https://github.com/rockbenben/365opensource)

</div>

<div align="center">

[English](../../README.md) · [简体中文](../../README.zh.md) · [繁體中文](README.zh-Hant.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Deutsch](README.de.md) · **Español** · [Français](README.fr.md) · [Italiano](README.it.md) · [Nederlands](README.nl.md) · [Português](README.pt.md) · [Русский](README.ru.md) · [Türkçe](README.tr.md) · [Tiếng Việt](README.vi.md) · [ไทย](README.th.md) · [Bahasa Indonesia](README.id.md) · [हिन्दी](README.hi.md) · [العربية](README.ar.md)

</div>

![La lista de inicio de Clockwork — una secuencia ordenada de pasos de inicio de sesión, cada uno con su tipo, retardo y condiciones](../../assets/screenshot.png)

## Qué hace

- 🚀 **Lista de inicio** — abre en orden tus aplicaciones de cada día al iniciar sesión, con retardo, condición de día y estilo de ventana por paso; cierra, enfoca o silencia cosas por el camino. Los pasos también pueden condicionarse a lo que hace el equipo: solo mientras una aplicación se ejecuta (o no), solo con corriente o solo con batería, solo si existe un archivo o carpeta.
- ⏰ **Tareas programadas** — un recordatorio a su hora, leído en voz alta si quieres, o un grupo de acciones ejecutado en silencio. Pulsar **Sí** puede ejecutar un programa, abrir un archivo o una URL, o disparar un grupo. O que lo dispare un evento en vez del reloj: al desbloquear, al bloquear, al reanudar de la suspensión, tras N minutos inactivo, al conectar o desconectar la corriente, o con la batería baja.
- 🧹 **Elementos de inicio del sistema** — todo lo que se inicia solo en tu PC, en una lista: desactiva lo que no necesites (desactivado, no eliminado) o traspásalo a tu propia lista de inicio.
- 🎛️ **Grupos de acciones** — agrupa una rutina (Concentración / Reunión / Cierre / Antes de dormir…) y actívala desde la bandeja, un **atajo global**, la lista de inicio o una tarea programada. Incluye plantillas.

> **Detenlo cuando quieras** — el botón de detención al final de la barra de pestañas (solo aparece mientras algo se ejecuta), bandeja → **Detener acciones en ejecución**, o el atajo de pánico global (por defecto `Ctrl+Alt+Q`). Las esperas largas se cortan, no se aguantan.

## Requisitos

| Aspecto | Detalle |
| --- | --- |
| **Sistema** | Windows 10 / 11, x64 |
| **Instalación** | Ninguna. Un solo `Clockwork.exe` portátil — ponlo en cualquier carpeta |
| **Administrador** | Solo para «Iniciar al arrancar sesión» y para los pasos que marques **ejecutar como administrador** |
| **Tu configuración** | `clockwork.settings.json` junto al exe (o `%APPDATA%\Clockwork\` si esa carpeta es de solo lectura) — nada sale del equipo |
| **Interfaz** | 18 idiomas, siguiendo el idioma de Windows en el primer arranque |

**Límites.** Sin instalador no hay actualización automática — descarga el zip nuevo y reemplaza el exe. Los lanzadores en sandbox bloquean enviar-teclas, acciones de ventana, activar-si-ya-se-ejecuta y volumen (recibirás un aviso claro; el simple «ejecutar programa» sigue funcionando). El remapeo de teclas y la expansión de texto quedan fuera del alcance — ese es el trabajo de AutoHotkey.

## Primeros pasos

1. Descarga la última versión desde [Releases](https://github.com/rockbenben/Clockwork/releases) —dos compilaciones, tres descargas— y coloca el único `Clockwork.exe` que te queda en cualquier carpeta.
   - **`Clockwork-<versión>-win-x64.zip`** — incluye el entorno de ejecución de .NET; funciona tal cual en cualquier Windows 10/11. Elige este si dudas, o si el PC está sin conexión o restringido.
   - **`Clockwork-<versión>-win-x64-needs-dotnet10.zip`** — necesita el [entorno de ejecución de escritorio de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) instalado. Instálalo una vez en un PC con internet y cada actualización posterior será una descarga mínima.
   - **`Clockwork.exe`** — la misma compilación del zip anterior, sin zip alrededor: haz clic y ejecútalo, o sobrescribe tu copia actual para actualizar. Si falta el entorno de ejecución, Windows te ofrece la descarga.
2. Haz doble clic para abrir la ventana de configuración. Los ejemplos que carga vienen todos **sin marcar** — no se ejecuta nada hasta que tú lo marques.
3. Para ejecutarlo en cada arranque: en la pestaña **Ajustes**, marca **Iniciar al arrancar sesión** (registra una tarea programada con permisos de administrador, así no hay una avalancha de avisos de UAC al arrancar).

Después se queda en la bandeja: doble clic en el icono para abrir la ventana, y el botón de cerrar solo vuelve a ocultarla. Para salir de verdad, usa **Salir** en el clic derecho de la bandeja.

> [!IMPORTANT]
> **El exe no está firmado**, así que SmartScreen muestra «Windows protegió su PC» en el primer arranque — haz clic en **Más información → Ejecutar de todas formas**. Algún antivirus también puede alertar: escribir claves Run del registro y tareas programadas es justo lo que hace un gestor de arranque… y también lo que hace el malware; desde fuera no se distinguen. Si prefieres no aceptarlo por confianza, [compílalo tú mismo](../../CONTRIBUTING.md) — mismo resultado, binario propio. Cada release incluye además un `SHA256SUMS.txt` y una atestación de compilación de GitHub: `gh attestation verify <archivo> -R rockbenben/Clockwork` demuestra que la descarga fue compilada por la CI de este repositorio, no en el portátil de alguien.

**Guía completa** — cada campo, cada caso límite: [English](../USAGE.md) · [中文](../USAGE.zh.md)

## Consejos

- **Haz doble clic en una fila para editarla**. Rutas, procesos y fechas no hay que escribirlos: **el botón … al final de la fila** abre el selector correspondiente (archivo, lista de procesos con búsqueda, fecha), y los atajos se graban pulsándolos con **Capturar**.
- **Arrastra una fila para reordenarla** — en las tres listas y en la lista de pasos del editor de grupos; los botones de subir/bajar siguen funcionando.
- **Pruébalo antes de guardar** — **▶ Ejecutar este paso** y **▶ Ejecutar grupo** del editor de grupos ejecutan lo que hay ahora en pantalla, y el botón se convierte en **■ Detener** mientras dura.
- **Duplicar** clona la tarea o el grupo seleccionado justo debajo — más rápido que rehacer uno casi idéntico. **Eliminar siempre pide confirmación**, en todas partes.
- Hacer doble clic en `Clockwork.exe` solo abre la ventana; **no** vuelve a ejecutar la lista de inicio. Para eso usa **Re-ejecutar lista de inicio** de la bandeja.

## Sobre el Plan 365 de código abierto

Proyecto **#020** del [Plan 365 de código abierto](https://github.com/rockbenben/365opensource) — una persona + IA, más de 300 proyectos de código abierto en un año.

[Envía tu idea →](https://365.aishort.top/) · [Discord](https://discord.gg/PZTQfJ4GjX) · [Telegram](https://t.me/aishort_top)
