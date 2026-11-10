# ADR 0008: Estilo arquitectónico

**Estado:** aceptada  
**Responsable:** Ana  
**Última revisión:** 09/11/2026  
**Requerimientos:** letra 6.1–6.3

## Contexto

La plataforma es un laboratorio de última milla con Backoffice, Portal, app móvil, worker y varios stores (PostgreSQL, Redis, RabbitMQ). Hay que fijar límites claros para no mezclar HTTP, dominio y mensajería.

## Decisión

Arquitectura en capas + mensajería asíncrona:

1. **Domain** puro (estados, transiciones, invariantes).
2. **Application** con casos de uso e interfaces de puertos.
3. **Infrastructure** con EF Core, Redis, RabbitMQ e Identity.
4. **Hosts** (Backoffice, Portal, Worker, Mobile) delgados: auth, HTTP y composición DI.
5. Comunicación entre bounded contexts vía **outbox → RabbitMQ**, no llamadas síncronas entre procesos.

## Alternativas descartadas

| Alternativa | Motivo |
|---|---|
| Modular monolito con módulos ricos acoplados | Encarece el lab y mezcla avisos con núcleo |
| Microservicios por agregado | Operación y costo AWS fuera de alcance |

## Consecuencias

Los casos de uso no conocen ASP.NET ni RabbitMQ. Los hosts solo orquestan. El worker consume eventos y no escribe el núcleo salvo lecturas SQL puntuales documentadas.
