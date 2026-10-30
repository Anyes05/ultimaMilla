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
| `sincronizacion_regreso` | **30 repartidores sincronizan a la vez** al volver al depósito (lo pide la letra) | Pico a los 4 min, en plena meseta de seguimiento |

El perfil `humo` hace lo mismo en escala chica (alrededor de 1 min): sirve para validar el script antes de la corrida real.

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
| `TOKENS` | Tokens de seguimiento público, separados por coma (los del enlace que recibe el destinatario) | `abc…,def…` |
| `REPARTIDORES` | Usuarios repartidor; se loguean una vez y se reparten entre los usuarios virtuales | `alvaro@demo.test` |
| `USUARIO_BACKOFFICE`, `CLAVE` | Usuario del Backoffice y clave de la semilla | `norte.despachador` |

El seguimiento se mide por `api.` y no por `tracking.`, porque `tracking.` tiene un límite de 30 req/min
por IP (protección contra enumeración). Ese límite se demuestra aparte: con más de 30 consultas por minuto
desde una IP, `tracking.` responde 429.

## Cómo correrlo

Local, contra compose y sin instalar k6:

```powershell
docker run --rm -i --network ultimamilla_default -v ${PWD}/tests/carga:/carga grafana/k6 run `
  -e PERFIL=humo -e BACKOFFICE=http://backoffice:8080 -e TOKENS=<t1,t2> /carga/carga.js
```

Contra la nube (ventana de AWS acordada con Joel, porque genera costo):

```powershell
k6 run -e PERFIL=carga -e BACKOFFICE=https://backoffice.tallernet.cloud-ip.cc `
  -e API=https://api.tallernet.cloud-ip.cc -e TOKENS=<t1,t2> tests/carga/carga.js
```

Mientras corre: Grafana → Tablero técnico (latencia, p95, p99, errores y requests por instancia) y
`kubectl get hpa -w` (Joel) para registrar la réplica que agrega el autoescalado.

## Evidencia a guardar

- Salida de k6 con los umbrales (✓ o ✗).
- Captura del tablero durante la corrida.
- Si hay HPA: `kubectl get hpa` y `kubectl get pods` antes, durante y después.
