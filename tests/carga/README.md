# Pruebas de carga con k6 (opcional 7.2)

Responsable: Nadia. Las usa también Joel, para mostrar que el autoescalado (HPA) agrega una réplica bajo carga.

## Objetivos de nivel de servicio (definidos antes de ejecutar)

| Flujo | p95 | p99 | Errores |
|---|---|---|---|
| Seguimiento público (`GET /api/seguimiento/{token}`) | < 300 ms | < 800 ms | < 1 % |
| Backoffice (Monitoreo y Despacho, con sesión) | < 800 ms | < 1500 ms | < 1 % |
| Sincronización al volver al depósito (lote de 20 comprobantes) | < 2000 ms | — | < 1 % |

**Por qué estos valores.** El seguimiento es lectura con caché y el componente de más tráfico, así que tiene
que ser el más rápido. El Backoffice arma páginas con consultas. La sincronización valida cada comprobante
y consulta la base por cada uno. Si un umbral no se cumple, k6 termina con error: el resultado no se
interpreta a posteriori.

## Escenarios (perfil `carga`)

| Escenario | Qué simula | Forma |
|---|---|---|
| `seguimiento_publico` | Destinatarios abriendo su enlace | Rampa de 0 a 50 req/s en 2 min, meseta de 5 min, bajada de 30 s |
| `backoffice` | 10 despachadores navegando | Concurrencia constante durante 8 min |
| `sincronizacion_regreso` | **30 sincronizaciones a la vez** al volver al depósito (lo pide la letra), repartidas entre los repartidores de Norte y de Sur | Pico a los 4 min, en plena meseta de seguimiento |

| Perfil | Para qué | Duración |
|---|---|---|
| `humo` | Validar el script antes de la corrida real (misma forma, escala chica) | ~1,5 min |
| `carga` | La corrida que se informa contra los objetivos | ~8 min |
| `hpa` | Carga sostenida (120 req/s de seguimiento, 20 despachadores) para que el autoescalado tenga tiempo de agregar réplicas; la sincronización simultánea cae a los 7 min, con el HPA ya reaccionando | ~14 min |

Los objetivos son los mismos en los tres perfiles: con el HPA, la idea es mostrar que se siguen cumpliendo
mientras se agregan réplicas.

**Repartidores.** La semilla tiene un repartidor por operador (`alvaro@demo.test` y `sur.repartidor`). Cada uno
inicia sesión una vez y las 30 sincronizaciones simultáneas se reparten entre los dos: concurrencia real
sobre la misma ruta y entre operadores. Para más repartidores distintos hay que sumarlos a la semilla y
pasarlos en `REPARTIDORES`.

**Corte de seguridad.** Corre contra el ambiente real: si más del 10 % de los requests falla durante 30 s,
k6 se detiene solo (`abortOnFail`).

## No modifica datos

- Seguimiento y Backoffice: solo lectura.
- Sincronización: manda comprobantes completos (firma y posición) de envíos que no están en la ruta. El
  servidor los valida, consulta la base y los **rechaza sin escribir** (`SincronizarEntregas` solo guarda
  los aplicados). El chequeo `comprobantes rechazados sin escribir` lo confirma en cada lote.

## Parámetros

| Variable | Para qué | Ejemplo |
|---|---|---|
| `PERFIL` | `humo` o `carga` | `carga` |
| `BACKOFFICE` | URL del Backoffice | `https://backoffice.tallernet.cloud-ip.cc` |
| `API` | URL de la API (seguimiento y repartidor) | `https://api.tallernet.cloud-ip.cc` |
| `TOKENS` | **Opcional.** Tokens de seguimiento público separados por coma. Sin ellos, se toman números de seguimiento de Monitoreo (con el despachador) y se consulta el seguimiento por número: mismo caso de uso y caché | `abc…,def…` |
| `REPARTIDORES` | Usuarios repartidor; se loguean una vez y se reparten entre los usuarios virtuales. Por defecto, los dos de la semilla | `alvaro@demo.test,sur.repartidor` |
| `RESULTADO` | **Opcional.** Archivo JSON donde se guarda el resumen (umbrales y métricas) como evidencia | `k6-carga-09-11.json` |
| `USUARIO_BACKOFFICE`, `CLAVE` | Usuario del Backoffice y clave de la semilla | `norte.despachador` |

El seguimiento se mide por `api.` y no por `tracking.`, porque `tracking.` tiene un límite de 30 req/min
por IP (protección contra enumeración). Ese límite se demuestra aparte: con más de 30 consultas por minuto
desde una IP, `tracking.` responde 429.

## Cómo correrlo

Local, contra compose y sin instalar k6:

```powershell
docker run --rm -i --network ultimamilla_default -v ${PWD}/tests/carga:/carga grafana/k6 run `
  -e PERFIL=humo -e BACKOFFICE=http://backoffice:8080 /carga/carga.js
```

Contra la nube (ventana de AWS acordada con Joel, porque genera costo):

```powershell
k6 run -e PERFIL=carga -e BACKOFFICE=https://backoffice.tallernet.cloud-ip.cc `
  -e API=https://api.tallernet.cloud-ip.cc tests/carga/carga.js
```

Mientras corre: Grafana → Tablero técnico (latencia, p95, p99, errores y requests por instancia) y
`kubectl get hpa -w` (Joel) para registrar la réplica que agrega el autoescalado.

## Evidencia a guardar

- Salida de k6 con los umbrales (✓ o ✗), y el JSON de `-e RESULTADO=…`.
- Captura del tablero durante la corrida.
- Si hay HPA: `kubectl get hpa` y `kubectl get pods` antes, durante y después.
