# WFE.AdminApi

Admin API for Haley Flow Engine + Consumer monitoring.

## Key Endpoints

Engine (`/api/admin/wf/engine/...`):

- `GET /api/admin/wf/engine/instance`
  - Query: `envCode?`, `defName`, `entityId`, `instanceGuid?`
- `GET /api/admin/wf/engine/timeline`
  - Query: `envCode?`, `defName`, `entityId`, `instanceGuid?`
- `GET /api/admin/wf/engine/refs`
  - Query: `envCode?`, `defName`, `flags?`, `skip?`, `take?`
- `GET /api/admin/wf/engine/entities`
  - Query: `envCode`, `defName?`, `runningOnly?`, `skip?`, `take?`
- `GET /api/admin/wf/engine/instances`
  - Query: `envCode`, `defName?`, `status?`, `skip?`, `take?`
- `GET /api/admin/wf/engine/pending-acks`
  - Query: `envCode`, `skip?`, `take?`
- `GET /api/admin/wf/engine/summary`
  - Query: `envCode`
- `GET /api/admin/wf/engine/health`
- `POST /api/admin/wf/engine/instance/suspend`
  - Query: `instanceGuid`, `message?`
- `POST /api/admin/wf/engine/instance/resume`
  - Query: `instanceGuid`
- `POST /api/admin/wf/engine/instance/fail`
  - Query: `instanceGuid`, `message?`
- `POST /api/admin/wf/engine/instance/reopen`
  - Query: `instanceGuid`, `actor?`

Consumer (`/api/admin/wf/consumer/...`):

- `GET /api/admin/wf/consumer/workflows`
  - Query: `skip?`, `take?`
- `GET /api/admin/wf/consumer/inbox`
  - Query: `status?`, `skip?`, `take?`
- `GET /api/admin/wf/consumer/outbox`
  - Query: `status?`, `skip?`, `take?`

Test helpers (`/api/admin/wf/test/...`):

- `GET /api/admin/wf/test/usecases`
- `POST /api/admin/wf/test/entities`

## Config

Set values in `appsettings.json`:

- `WorkFlowEngine.*` (engine bootstrap/runtime options)
- `WorkFlowConsumer.*` (consumer bootstrap/runtime options)
  - `WorkFlowConsumer.wrapper_assemblies` controls wrapper discovery for test use-cases (for example: `"WFE.Lib"`).
- `WorkflowAdmin.DefaultTake`
- `WorkflowAdmin.MaxTake`
