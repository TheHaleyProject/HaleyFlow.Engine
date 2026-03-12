# WFE.AdminApi

Admin API for Haley Flow Engine + Consumer monitoring.

## Key Endpoints

- `GET /api/admin/workflow/instance`
  - Query: `envCode?`, `defName`, `entityId`, `instanceGuid?`
- `GET /api/admin/workflow/timeline`
  - Query: `envCode?`, `defName`, `entityId`, `instanceGuid?`
- `GET /api/admin/workflow/refs`
  - Query: `envCode?`, `defName`, `flags?`, `skip?`, `take?`
- `GET /api/admin/workflow/entities`
  - Query: `defName?`, `runningOnly?`, `skip?`, `take?`
- `GET /api/admin/workflow/pending-acks`
  - Query: `skip?`, `take?`
- `GET /api/admin/workflow/consumer/workflows`
  - Query: `skip?`, `take?`
- `GET /api/admin/workflow/consumer/inbox`
  - Query: `status?`, `skip?`, `take?`
- `GET /api/admin/workflow/consumer/outbox`
  - Query: `status?`, `skip?`, `take?`
- `GET /api/admin/workflow/summary`

## Config

Set values in `appsettings.json`:

- `WorkFlowEngine.*` (engine bootstrap/runtime options)
- `WorkFlowConsumer.*` (consumer bootstrap/runtime options)
  - `WorkFlowConsumer.wrapper_assemblies` controls wrapper discovery for test use-cases (for example: `"WFE.Lib"`).
- `WorkflowAdmin.DefaultTake`
- `WorkflowAdmin.MaxTake`
