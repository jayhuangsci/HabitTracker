# API Contract

## Scope

- Build MVP only.
- Do not add auth in v1.
- Do not add sync in v1.
- Do not add custom habit fields in v1.
- Keep one complete working flow first.

## Global Rules

- Use base path: `/api`
- Use JSON only
- Use `camelCase`
- Use date format: `YYYY-MM-DD`
- Use decimal for `quantity`
- Return `201` on create
- Return `200` on read and update
- Return `204` on delete
- Return `400` for invalid input
- Return `404` for missing resource
- Return `409` for business conflict

## Error Response

```json
{
  "code": "bad_request",
  "message": "quantity must be greater than or equal to 0",
  "field": "quantity"
}
```

## Resource: Habits

### GET `/api/habits`

- Return all habits

Response:

```json
[
  {
    "id": 1,
    "name": "Running",
    "unit": "km",
    "description": "Outdoor running",
    "createdAt": "2026-04-28T08:00:00Z"
  }
]
```

### POST `/api/habits`

Request:

```json
{
  "name": "Running",
  "unit": "km",
  "description": "Outdoor running"
}
```

Rules:

- `name` is required
- `unit` is required
- `name` must be unique

Response:

```json
{
  "id": 1,
  "name": "Running",
  "unit": "km",
  "description": "Outdoor running",
  "createdAt": "2026-04-28T08:00:00Z"
}
```

### PUT `/api/habits/{id}`

Request:

```json
{
  "name": "Running",
  "unit": "km",
  "description": "Easy run"
}
```

Rules:

- `id` must exist
- `name` must stay unique

Response:

```json
{
  "id": 1,
  "name": "Running",
  "unit": "km",
  "description": "Easy run",
  "createdAt": "2026-04-28T08:00:00Z"
}
```

### DELETE `/api/habits/{id}`

Rules:

- Delete the habit
- Delete related habit logs

Response:

- `204 No Content`

## Resource: Habit Logs

## Rule

- Allow only one log per `habitId + date` in v1

### GET `/api/habit-logs`

Query:

- `habitId` optional
- `startDate` optional
- `endDate` optional

Example:

`/api/habit-logs?habitId=1&startDate=2026-04-01&endDate=2026-04-30`

Response:

```json
[
  {
    "id": 1,
    "habitId": 1,
    "date": "2026-04-28",
    "quantity": 5,
    "comment": "Easy run",
    "createdAt": "2026-04-28T08:10:00Z"
  }
]
```

### POST `/api/habit-logs`

Request:

```json
{
  "habitId": 1,
  "date": "2026-04-28",
  "quantity": 5,
  "comment": "Easy run"
}
```

Rules:

- `habitId` is required
- `date` is required
- `quantity` is required
- `quantity >= 0`
- reject duplicate `habitId + date`

Response:

```json
{
  "id": 1,
  "habitId": 1,
  "date": "2026-04-28",
  "quantity": 5,
  "comment": "Easy run",
  "createdAt": "2026-04-28T08:10:00Z"
}
```

### PUT `/api/habit-logs/{id}`

Request:

```json
{
  "date": "2026-04-28",
  "quantity": 6,
  "comment": "Updated run"
}
```

Rules:

- `id` must exist
- `quantity >= 0`
- updated `habitId + date` must stay unique

Response:

```json
{
  "id": 1,
  "habitId": 1,
  "date": "2026-04-28",
  "quantity": 6,
  "comment": "Updated run",
  "createdAt": "2026-04-28T08:10:00Z"
}
```

### DELETE `/api/habit-logs/{id}`

Response:

- `204 No Content`

## Resource: Stats

- Build chart data on the server
- Do not build chart data in the client for v1

### GET `/api/stats/weekly`

Query:

- `habitId` required
- `year` required
- `week` required

Response:

```json
{
  "habitId": 1,
  "habitName": "Running",
  "unit": "km",
  "period": "weekly",
  "labels": ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"],
  "values": [5, 0, 3, 0, 4, 0, 0],
  "total": 12
}
```

### GET `/api/stats/monthly`

Query:

- `habitId` required
- `year` required
- `month` required

Response:

```json
{
  "habitId": 1,
  "habitName": "Running",
  "unit": "km",
  "period": "monthly",
  "labels": ["2026-04-01", "2026-04-02", "2026-04-03"],
  "values": [5, 0, 3],
  "total": 8
}
```

### GET `/api/stats/yearly`

Query:

- `habitId` required
- `year` required

Response:

```json
{
  "habitId": 1,
  "habitName": "Running",
  "unit": "km",
  "period": "yearly",
  "labels": ["Jan", "Feb", "Mar", "Apr"],
  "values": [12, 20, 16, 25],
  "total": 73
}
```

## Implementation Order

1. Implement `POST /api/habits`
2. Implement `GET /api/habits`
3. Implement `POST /api/habit-logs`
4. Implement `GET /api/habit-logs`
5. Implement `PUT /api/habit-logs/{id}`
6. Implement `DELETE /api/habit-logs/{id}`
7. Implement stats endpoints

## Do Not Add Yet

- `/api/auth/*`
- `/api/users/*`
- `/api/sync/*`
- pagination
- notification endpoints
- advanced analytics endpoints
