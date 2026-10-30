// Pruebas de carga (opcional 7.2, 2 puntos). Objetivos y escenarios explicados en README.md.
//
// Local (compose):  docker run --rm -i --network ultimamilla_default -v ${PWD}/tests/carga:/carga grafana/k6 run -e PERFIL=humo -e BACKOFFICE=http://backoffice:8080 -e TOKENS=<t1,t2> /carga/carga.js
// Remoto (EKS):     k6 run -e PERFIL=carga -e BACKOFFICE=https://backoffice.tallernet.cloud-ip.cc -e API=https://api.tallernet.cloud-ip.cc -e TOKENS=<t1,t2> tests/carga/carga.js
//
// No modifica datos: lee seguimiento y pantallas, y sincroniza comprobantes de envíos que no están en la ruta,
// que el servidor valida y rechaza sin escribir.

import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Trend } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

const BACKOFFICE = __ENV.BACKOFFICE || 'http://localhost:8080';
const API = __ENV.API || BACKOFFICE;
const PERFIL = __ENV.PERFIL || 'humo';
const CLAVE = __ENV.CLAVE || 'Demo123!';
const USUARIO_BACKOFFICE = __ENV.USUARIO_BACKOFFICE || 'norte.despachador';
const REPARTIDORES = (__ENV.REPARTIDORES || 'alvaro@demo.test').split(',').map((r) => r.trim()).filter(Boolean);
const TOKENS = (__ENV.TOKENS || '').split(',').map((t) => t.trim()).filter(Boolean);
const COMPROBANTES_POR_LOTE = Number(__ENV.LOTE || 20);

const duracionSync = new Trend('sincronizacion_duracion', true);

// Perfiles: "humo" valida el script en minutos; "carga" es la corrida que se informa (rampa + meseta + pico de sync).
const perfiles = {
  humo: {
    seguimiento: { tasa: 5, rampa: '20s', meseta: '40s' },
    backoffice: { usuarios: 2, duracion: '1m' },
    sync: { repartidores: 5, inicio: '20s' },
  },
  carga: {
    seguimiento: { tasa: 50, rampa: '2m', meseta: '5m' },
    backoffice: { usuarios: 10, duracion: '8m' },
    sync: { repartidores: 30, inicio: '4m' },
  },
};
const p = perfiles[PERFIL];

export const options = {
  scenarios: {
    // Destinatarios consultando su envío: el componente de más tráfico (letra 4).
    seguimiento_publico: {
      executor: 'ramping-arrival-rate',
      exec: 'seguimiento',
      startRate: 1,
      timeUnit: '1s',
      preAllocatedVUs: 20,
      maxVUs: 200,
      stages: [
        { target: p.seguimiento.tasa, duration: p.seguimiento.rampa },
        { target: p.seguimiento.tasa, duration: p.seguimiento.meseta },
        { target: 0, duration: '30s' },
      ],
    },
    // Despachadores trabajando en el Backoffice durante toda la prueba.
    backoffice: {
      executor: 'constant-vus',
      exec: 'backoffice',
      vus: p.backoffice.usuarios,
      duration: p.backoffice.duracion,
    },
    // Escenario exigido por la letra: varios repartidores sincronizan a la vez al volver al depósito.
    sincronizacion_regreso: {
      executor: 'per-vu-iterations',
      exec: 'sincronizacion',
      vus: p.sync.repartidores,
      iterations: 1,
      startTime: p.sync.inicio,
      maxDuration: '2m',
    },
  },
  // Objetivos de nivel de servicio, definidos antes de ejecutar (ver README.md).
  thresholds: {
    'http_req_failed{escenario:seguimiento}': ['rate<0.01'],
    'http_req_duration{escenario:seguimiento}': ['p(95)<300', 'p(99)<800'],
    'http_req_failed{escenario:backoffice}': ['rate<0.01'],
    'http_req_duration{escenario:backoffice}': ['p(95)<800', 'p(99)<1500'],
    'http_req_failed{escenario:sincronizacion}': ['rate<0.01'],
    sincronizacion_duracion: ['p(95)<2000'],
    checks: ['rate>0.99'],
  },
};

export function setup() {
  if (TOKENS.length === 0) {
    throw new Error('Falta TOKENS: tokens de seguimiento público separados por coma (ver README.md).');
  }
  // Un login por repartidor al inicio: el login tiene límite de intentos y la app real tampoco se loguea en cada sync.
  const sesiones = REPARTIDORES.map((usuario) => {
    const r = http.post(`${API}/api/repartidor/login`, JSON.stringify({ usuario, clave: CLAVE }), {
      headers: { 'Content-Type': 'application/json' },
    });
    if (r.status !== 200) throw new Error(`Login de ${usuario} falló: HTTP ${r.status}`);
    return r.json('token');
  });
  return { sesiones };
}

export function seguimiento() {
  const token = TOKENS[Math.floor(Math.random() * TOKENS.length)];
  const r = http.get(`${API}/api/seguimiento/${token}`, { tags: { escenario: 'seguimiento' } });
  check(r, { 'seguimiento 200': (x) => x.status === 200 });
}

export function backoffice() {
  // Cada usuario virtual tiene su propia cookie de sesión; se loguea una vez.
  if (!http.cookieJar().cookiesForURL(BACKOFFICE).acceso_backoffice) {
    http.post(`${BACKOFFICE}/session/login`, { usuario: USUARIO_BACKOFFICE, clave: CLAVE }, {
      redirects: 0,
      tags: { escenario: 'login' },
    });
  }
  group('monitoreo', () => {
    const r = http.get(`${BACKOFFICE}/`, { tags: { escenario: 'backoffice' } });
    check(r, { 'monitoreo 200': (x) => x.status === 200 && x.url.indexOf('/login') === -1 });
  });
  group('despacho', () => {
    const r = http.get(`${BACKOFFICE}/Despacho`, { tags: { escenario: 'backoffice' } });
    check(r, { 'despacho 200': (x) => x.status === 200 });
  });
  // Tiempo de lectura de una persona entre pantallas (1 a 3 s).
  sleep(1 + Math.random() * 2);
}

export function sincronizacion(datos) {
  const sesion = datos.sesiones[(__VU - 1) % datos.sesiones.length];
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${sesion}` };

  // Lo mismo que hace la app al recuperar conexión: baja la ruta y sube los comprobantes guardados offline.
  const ruta = http.get(`${API}/api/repartidor/rutas/hoy`, { headers, tags: { escenario: 'sincronizacion' } });
  check(ruta, { 'rutas/hoy 200': (x) => x.status === 200 });

  const ahora = new Date().toISOString();
  const eventos = Array.from({ length: COMPROBANTES_POR_LOTE }, () => ({
    eventoId: uuidv4(),
    envioId: uuidv4(), // fuera de la ruta: se valida completo y se rechaza sin escribir
    estado: 'Entregado',
    nombreReceptor: 'Prueba de carga',
    observaciones: '',
    fechaDispositivo: ahora,
    firma: '[[{"x":1,"y":1},{"x":2,"y":2}]]',
    latitud: -34.9,
    longitud: -56.16,
  }));
  const hoy = ahora.substring(0, 10);
  const r = http.post(`${API}/api/repartidor/sincronizacion`, JSON.stringify({ fecha: hoy, eventos }), {
    headers,
    tags: { escenario: 'sincronizacion' },
  });
  duracionSync.add(r.timings.duration);
  check(r, {
    'sincronizacion 200': (x) => x.status === 200,
    'comprobantes rechazados sin escribir': (x) => x.status === 200 && x.json('rechazados').length === COMPROBANTES_POR_LOTE,
  });
}
