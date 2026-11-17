# Plan de lanzamiento como app — ZASS!

**Decidido el 17-nov-2026.** Pablo: «primero como web, y luego la quiero lanzar en App Store (iOS) y
en Play Store (Android)». Todo lo de app va **después del lanzamiento web**. Antes de eso no se
empieza nada de este plan, ni siquiera el service worker.

Este archivo es el plan. Cuando llegue el momento, se ejecuta en el orden de abajo. Las decisiones
marcadas 🛑 son de Pablo y se preguntan antes de construir.

---

## 1. La decisión de tecnología: Capacitor

Pablo pidió una recomendación, y esta es la elegida: **Capacitor**, que empaqueta **el mismo
frontend** (React 19 + TypeScript, `frontend/`) como app de iOS y de Android. La API .NET no
cambia: atiende igual a la web y a las dos apps.

| Opción | Por qué no |
|---|---|
| Flutter (Dart), Kotlin, Swift | Habría que **reescribir todas las pantallas** en otro lenguaje y mantener dos frontends (o tres) |
| React Native / Expo | Mismo lenguaje, y Pablo lo conoce por Encuentra tu Mascota, pero **rehace las pantallas** (no usa HTML). Queda como **plan B**, solo si la app con Capacitor se sintiera lenta |
| Python | No hace falta: el backend ya está hecho en .NET |

---

## 2. Lo que es de Pablo, y conviene empezar antes

Nada de esto es código, y casi todo depende de la **SpA** (que además destraba Flow).

| Qué | Por qué | Costo |
|---|---|---|
| **Constituir la SpA** | Las cuentas de las tiendas, a nombre de una empresa | Gratis («Tu Empresa en un Día») |
| **Número D-U-N-S** de la SpA | Apple lo exige para una cuenta de **organización** | Gratis; tarda días |
| **Cuenta Apple Developer** (organización) | Publicar en App Store | USD 99 al año |
| **Cuenta Google Play Console** (organización) | Publicar en Play Store. Las cuentas **personales** creadas desde nov-2023 tienen que probar la app con **12 testers durante 14 días** antes de publicar; las de organización no | USD 25, pago único |
| Un **Mac**, o un servicio de compilación en la nube | Compilar y firmar la app de iOS se hace solo con Xcode, en macOS | Variable |

---

## 3. Las tres etapas

### Etapa 1 — Web (en curso)
Lo que ya está desplegado. Se lanza con Flow. **Este plan no la toca.**

### Etapa 2 — PWA completa
Medido el 17-nov: ya existen `frontend/public/manifest.webmanifest`, los íconos de 192 y 512, el
*maskable* y el `apple-touch-icon`. **Falta el service worker**, y sin él:
- no funciona sin conexión, ni siquiera la «cáscara» con un aviso;
- no hay notificaciones push web. En iPhone, las push web solo existen si la PWA se instaló desde Safari (iOS 16.4 o posterior).

**Tareas:** el service worker (cachear la cáscara y nunca `/api/*`), la página sin conexión, las
push web y la prueba en un teléfono de verdad, iPhone y Android.

### Etapa 3 — Capacitor y las tiendas
1. **Resolver la sesión primero** (ver el riesgo A). Es lo que más puede costar, y conviene saberlo antes de todo lo demás.
2. Agregar Capacitor a `frontend/`. Salen los proyectos `ios/` y `android/`.
3. **Lo nativo que pide Apple** (riesgo C):
   - **push nativas**: APNs en iOS y FCM en Android, con su registro en la API;
   - **cámara y archivos** para subir documentos de credenciales (va junto con la JQ-9);
   - **enlaces universales**: que `https://zass.cl/t/…` (la tarjeta NFC) y `https://zass.cl/prestadores/…` abran la app si está instalada. Necesitan `/.well-known/apple-app-site-association` y `/.well-known/assetlinks.json` en `zass.cl` (Cloudflare Pages), servidos con el tipo de contenido correcto.
4. Íconos, *splash* y capturas para las fichas de las tiendas.
5. Publicar: TestFlight en iOS y prueba interna en Android, después las tiendas.

---

## 4. Riesgos y decisiones, escritos antes de empezar

### A. La sesión dentro de la app (riesgo técnico, el mayor)
Hoy la sesión es una cookie `HttpOnly` con **`SameSite=Lax`** (`backend/src/Zass.Api/Configuracion/Seguridad.cs:130-131`),
y el CSRF va en la cabecera `X-CSRF-Token`. En la web funciona porque todo vive en `zass.cl` (el
proxy de Cloudflare reenvía `/api`). Dentro de Capacitor la app **no** está en `zass.cl`: su
origen es `capacitor://localhost` en iOS y `https://localhost` en Android. Una cookie `Lax` **no
viaja** en una petición de otro sitio, así que en la app **no habría sesión**.

Caminos posibles, **a medir antes de elegir** (no se elige en este plan):
- Las peticiones HTTP nativas de Capacitor (`CapacitorHttp`), que manejan las cookies desde el lado nativo.
- Cargar la app desde `https://zass.cl` (`server.url`). Es lo más simple, pero acerca la app a ser «una web en una caja» (riesgo C).
- Un mecanismo de sesión aparte para la app (por ejemplo, token en almacenamiento seguro). Es el cambio más grande en la API y en la seguridad: **N3**.

🛑 La elección final la aprueba Pablo, con lo que se haya medido.

### B. Cobrar la suscripción dentro de la app (decisión de negocio)
ZASS cobra una mensualidad al prestador (con Flow). **Apple y Google tienen reglas sobre cobrar
suscripciones dentro de la app**: pueden exigir su propio sistema de pagos, con su comisión. Hay
que leer las guías **vigentes cuando llegue el momento** (Apple App Review 3.1.x; Google Play
Payments), porque cambian, y con decisiones judiciales recientes.

🛑 Opciones para Pablo, cuando toque:
- Que **en la app no se pague**: el prestador paga en la web, y la app no muestra `/planes` ni enlaza a pagar. Hay que confirmar si las guías lo permiten.
- Usar la compra dentro de la app solo en las tiendas, con la comisión.

**El buscador no paga nunca**, así que para él esto no aplica.

### C. «Una web en una caja» (Apple, guía 4.2)
Apple rechaza las apps que solo envuelven un sitio. Por eso la etapa 3 suma lo nativo: push,
cámara y enlaces universales. Sin eso, el rechazo es probable.

### D. Borrar la cuenta desde la app (Apple lo exige) — **y falta también en la web**
Medido el 17-nov: **la API existe** (API-17, `EndpointsIdentidad.cs:186`, «elimina la cuenta y
anonimiza», RN-S1-07), pero **ninguna pantalla la ofrece**. Apple exige poder borrar la cuenta
desde la app. **Y no es solo de la app**: el derecho de supresión de la Ley 21.719 vale para la
web, que se lanza primero.

➡️ **Pendiente de la WEB, antes de su lanzamiento**: una pantalla «Eliminar mi cuenta» que llame a API-17.

### E. Privacidad en las tiendas
Las dos tiendas piden una **política de privacidad publicada** (URL) y un formulario de datos:
el *Data safety* de Google y la *Privacy Nutrition Label* de Apple. Tiene que coincidir con lo
real, incluida la **transferencia a EE.UU.** (Neon en `us-east-1` y Render en Oregón; ver
`ESTADO.md`, 17-nov). Es la misma revisión legal que necesita la web.

### F. La tarjeta NFC no necesita la app
La tarjeta guarda un enlace (`https://zass.cl/t/XXXX-XXXX`): cualquier teléfono lo abre en el
navegador. La app **solo** mejora eso con los enlaces universales: si está instalada, se abre ahí.
Nadie necesita instalar nada para usar una tarjeta.

---

## 5. Checklist para cuando llegue el momento

- [ ] Web lanzada con Flow (etapa 1)
- [ ] «Eliminar mi cuenta» en la web (riesgo D) — **va antes, con la web**
- [ ] SpA, D-U-N-S, Apple Developer y Google Play Console (sección 2)
- [ ] 🛑 Pablo decide el cobro dentro de la app (riesgo B)
- [ ] Service worker, sin conexión y push web (etapa 2)
- [ ] Medir los caminos de sesión en Capacitor, y 🛑 Pablo elige (riesgo A)
- [ ] Capacitor, push nativas, cámara y enlaces universales (etapa 3)
- [ ] Política de privacidad y formularios de datos de las tiendas (riesgo E)
- [ ] TestFlight y prueba interna, después las tiendas

Relacionado: `ESTADO.md` · `PLAN-JOAQUIN.md` (JQ-9, documentos) · `frontend/DESPLIEGUE-CLOUDFLARE.md`
