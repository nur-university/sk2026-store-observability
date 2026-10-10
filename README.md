# Nur.Store2026.Observability

Configuracion comun de observabilidad (OpenTelemetry + Serilog) para los servicios de Store.

## Que registra `AddObservability(serviceName, ...)`

| Senal | Instrumentacion | Destino |
|---|---|---|
| Trazas | ASP.NET Core, HttpClient, Npgsql, `Joseco.Outbox`, `Joseco.Communication.RabbitMQ` (las dos ultimas las agrega cada servicio) | OTLP gRPC -> collector -> Jaeger |
| Metricas | ASP.NET Core, HttpClient, runtime, proceso, meters `Joseco.Communication.RabbitMQ`, `Joseco.Outbox`, `Npgsql` y `Store.*` | OTLP gRPC -> collector -> Prometheus (ademas, `/metrics` si el WebApi llama a `UseOpenTelemetryPrometheusScrapingEndpoint`) |
| Logs | Serilog (`UseLogging`) con `TraceId`/`SpanId` y `ServiceName` | Consola y Seq |

Todas las senales comparten el recurso `service.name`, `service.namespace=store`, `service.version` y
`deployment.environment`; los dashboards filtran por esas etiquetas (`service_name`, `deployment_environment`).

## Destino OTLP

Se usa la variable estandar `OTEL_EXPORTER_OTLP_ENDPOINT` (variable de entorno o clave de configuracion).
Sin ella, `http://localhost:4317`, que es el puerto que publica el contenedor `otel-collector`.
En Docker: `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` (ya puesto en `Store/docker-compose.yml`).

## Metricas propias

- Los meters de negocio de un servicio se crean con un nombre que empiece por `Store.` (p. ej. `new Meter("Store.Orders")`)
  y se recogen sin tocar nada mas.
- Para otro meter o instrumentacion, pasar `metricsFactory: m => m.AddMeter("Mi.Meter")` a `AddObservability`.

## Instrumentos de mensajeria y outbox

Nombres tal como llegan a Prometheus (unidad `s` -> sufijo `_seconds`, contadores -> `_total`).

| Instrumento | Etiquetas | Que mide |
|---|---|---|
| `rabbitmq.messages.received` / `.failed` | `queue` | Mensajes procesados / intentos fallidos |
| `rabbitmq.messages.retried` | `queue`, `message_type` | Reintentos programados |
| `rabbitmq.messages.dead_lettered` | `queue`, `message_type`, `reason` (`handler_error`, `deserialization`) | Mensajes enviados a `{queue}.error` |
| `rabbitmq.message.processing.duration` | `queue`, `message_type`, `outcome` | Duracion del handler por intento |
| `rabbitmq.message.delivery.latency` | `queue`, `message_type` | Desde que se crea el mensaje hasta que arranca su handler |
| `rabbitmq.messages.published` / `.failed` / `.bytes` | `exchange` | Publicacion |
| `rabbitmq.message.publish.duration` | `exchange`, `outcome` | Duracion de publicar |
| `outbox.messages.processed` | `message_type`, `outcome` (`published`, `failed`) | Mensajes del outbox publicados |
| `outbox.message.publish.duration` | `message_type` | Duracion de publicar y marcar procesado |
| `outbox.message.delay` | `message_type` | Tiempo que el mensaje espero en el outbox |
| `outbox.unprocessed.batch_size` | - | Pendientes leidos en el ultimo ciclo (tope: tamano de pagina, 20) |
| `outbox.unprocessed.oldest_age` | - | Antiguedad del pendiente mas viejo (0 si esta vacio) |

Los buckets de los histogramas de duracion se fijan aqui (`AddView`), porque en .NET 8 el instrumento no los declara.

## Cambios respecto a la version anterior

- El exportador Jaeger (UDP) se elimino; `jeagerSettings` se conserva en la firma por compatibilidad y se ignora.
- Las metricas incluyen ahora HTTP (servidor y cliente) y Npgsql; antes solo runtime, proceso y RabbitMQ.
- Los WorkerService exportan metricas por OTLP (antes no tenian ningun camino de salida).
