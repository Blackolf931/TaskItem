# TaskApi

Тестовое задание: API задач на .NET 9 (Minimal API) с PostgreSQL (EF Core) и публикацией событий в RabbitMQ.

## Требования

- [Docker](https://www.docker.com/) с Docker Compose — для запуска через compose
- [.NET SDK 9](https://dotnet.microsoft.com/download/dotnet/9.0) — для локального запуска и тестов

## Запуск через Docker Compose

Из корня репозитория:

```bash
docker compose up --build
```

Поднимутся PostgreSQL, RabbitMQ и API. API стартует после того, как БД и брокер готовы, и при старте сам применяет миграции.

| Сервис | Адрес |
|---|---|
| API | http://localhost:8080/tasks |
| RabbitMQ UI | http://localhost:15672 (guest / guest) |
| PostgreSQL | localhost:5432, БД `tasks` (postgres / postgres) |

Остановить: `Ctrl+C` или `docker compose down`. Удалить заодно данные БД: `docker compose down -v`.

### Если стандартные порты заняты

Порты переопределяются переменными окружения `POSTGRES_PORT`, `RABBITMQ_PORT`, `RABBITMQ_UI_PORT`, `API_PORT`:

```bash
POSTGRES_PORT=55432 RABBITMQ_PORT=5673 RABBITMQ_UI_PORT=15673 API_PORT=18080 docker compose up --build
```

PowerShell:

```powershell
$env:POSTGRES_PORT="55432"; $env:RABBITMQ_PORT="5673"; $env:RABBITMQ_UI_PORT="15673"; $env:API_PORT="18080"
docker compose up --build
```

Те же значения можно положить в файл `.env` рядом с `docker-compose.yml`.

## Локальный запуск (API из IDE или `dotnet run`)

1. Поднять только PostgreSQL и RabbitMQ:

   ```bash
   docker compose up -d postgres rabbitmq
   ```

2. Запустить API:

   ```bash
   dotnet run --project TaskApi
   ```

   API будет доступно на http://localhost:5217. Готовые запросы есть в `TaskApi/TaskApi.http`.

Настройки подключения лежат в `TaskApi/appsettings.json` (`ConnectionStrings:Tasks` и секция `RabbitMq`) и по умолчанию совпадают с compose. Если порты PostgreSQL или RabbitMQ переопределены, передайте их приложению через переменные окружения, например:

```bash
ConnectionStrings__Tasks="Host=127.0.0.1;Port=55432;Database=tasks;Username=postgres;Password=postgres" \
RabbitMq__Port=5673 \
dotnet run --project TaskApi
```

## API

| Метод | Эндпоинт | Что делает | Ответы |
|---|---|---|---|
| POST | `/tasks` | Создаёт задачу | 200, 400 — некорректный запрос |
| GET | `/tasks` | Возвращает все задачи | 200 |
| PUT | `/tasks/{id}/complete` | Завершает задачу и отправляет событие в RabbitMQ | 200, 400, 404, 409 — уже завершена или изменена параллельно |
| DELETE | `/tasks/{id}` | Удаляет задачу | 200, 400, 404, 409 |

Ошибки возвращаются в формате ProblemDetails.

Пример:

```bash
curl -X POST http://localhost:8080/tasks \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy milk", "priority": "High"}'

curl -X PUT http://localhost:8080/tasks/{id}/complete
```

`priority` — `Low`, `Medium` или `High`, по умолчанию `Medium`. `title` — обязательный, не пустой, до 200 символов.

## Событие в RabbitMQ

При завершении задачи в обменник `task.events` (topic) с routing key `task.completed` публикуется сообщение:

```json
{
  "taskId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "completedAt": "2026-05-07T12:00:00Z",
  "priority": "High"
}
```

Чтобы увидеть сообщения, в RabbitMQ UI создайте очередь и привяжите её к обменнику `task.events` с routing key `task.completed` (обменник появляется после первой публикации). Если RabbitMQ недоступен, задача всё равно завершается, а событие пишется в лог.

## Тесты

```bash
dotnet test
```

Docker для тестов не нужен: в интеграционном тесте БД заменена на InMemory, а публикация в RabbitMQ — заглушкой.
