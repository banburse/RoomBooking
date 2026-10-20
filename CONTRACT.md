# CONTRACT: RoomBooking API v1

Wiążący kontrakt między Dev-A (API), Dev-B (Domain + testy) i Dev-C (GUI). Zmiany tylko przez Orchestratora.
Źródło wymagań: `SPEC.md`. Tu są dokładne kształty JSON i decyzje, których SPEC nie rozstrzyga.

## Konwencje ogólne

- JSON w **camelCase**. Enumy jako **stringi**: `"Room"`, `"Desk"` (`JsonStringEnumConverter`).
- Czas: ISO 8601 z offsetem. Odpowiedzi zawsze w UTC, np. `"2026-10-21T08:00:00+00:00"`.
  Żądania muszą zawierać offset (`Z` lub `+02:00`); API normalizuje do UTC. Brak offsetu → 400.
- `Id` to GUID w formacie `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"`.
- Wszystkie błędy: `application/problem+json` (RFC 9457). Komunikaty (`title`, `detail`, `errors`) **po polsku**.
- Brak autoryzacji.

### Kształty błędów

**400 walidacja** (`ValidationProblemDetails`), klucze `errors` = nazwy pól JSON w camelCase:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Błąd walidacji.",
  "status": 400,
  "errors": {
    "bookedBy": ["Imię jest wymagane."],
    "endUtc": ["Rezerwacja musi trwać co najmniej 15 minut."]
  }
}
```
Błędy niezwiązane z konkretnym polem (np. zły parametr zapytania) idą pod kluczem nazwy parametru (`type`, `from`, `to`).

**404**:
```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5", "title": "Nie znaleziono.", "status": 404, "detail": "Zasób 3f25... nie istnieje." }
```

**409** (ogólny):
```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Konflikt.", "status": 409, "detail": "Zasób o nazwie 'Alfa' już istnieje." }
```

**409 kolizja rezerwacji** (`POST /bookings`), dodatkowe pole rozszerzenia na najwyższym poziomie:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Kolizja rezerwacji.",
  "status": 409,
  "detail": "Termin koliduje z istniejącą rezerwacją.",
  "conflictingBookingId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"
}
```

Dopuszczalne dodatkowe pola (`traceId`, `instance`). Klient polega tylko na `title`, `detail`, `status`, `errors`, `conflictingBookingId`.

## Modele odpowiedzi

**Resource**
```json
{ "id": "…", "name": "Alfa", "type": "Room", "capacity": 8, "isActive": true }
```

**Booking**
```json
{
  "id": "…",
  "resourceId": "…",
  "bookedBy": "Jan",
  "title": "Daily",
  "startUtc": "2026-10-21T08:00:00+00:00",
  "endUtc": "2026-10-21T09:00:00+00:00",
  "createdAt": "2026-10-20T18:12:03.123+00:00"
}
```
`title` może być `null`. Anulowane rezerwacje nie są zwracane przez API (brak pola `cancelledAt` w odpowiedzi).

## Endpointy

### 1. `GET /resources?type=Room|Desk`
- `type` opcjonalny, wielkość liter bez znaczenia. Nieznana wartość → **400** (`errors.type`).
- **200**: tablica aktywnych zasobów, posortowana po `type` (Room, potem Desk), potem po `name`.

### 2. `POST /resources`
Body:
```json
{ "name": "Delta", "type": "Room", "capacity": 6 }
```
- `name`: wymagane, po `Trim()` 1–80 znaków. `type`: wymagane, `Room`/`Desk`. `capacity`: 1–50.
- **201**, nagłówek `Location: /resources/{id}`, body = Resource (`isActive: true`).
- **400** walidacja. **409** gdy istnieje zasób o tej samej nazwie (porównanie bez wielkości liter, po `Trim()`).

### 3. `GET /resources/{id}/bookings?from=…&to=…`
- `from`, `to` wymagane. Format: data `yyyy-MM-dd` (oznacza **00:00 w strefie biura**, `Europe/Warsaw`)
  albo pełny ISO 8601 z offsetem (GUI wysyła ten wariant: północ poniedziałku/soboty w strefie przeglądarki, w UTC).
- Zwraca niezanulowane rezerwacje **nachodzące** na `[from, to)`: `startUtc < to && endUtc > from`, sortowane po `startUtc` rosnąco.
- **400**: brak/zły format, `to <= from`, zakres > 31 dni (`errors.from` lub `errors.to`).
- Limit 31 dni liczony w **czasie lokalnym biura** (dni kalendarzowe): zakres dozwolony, gdy lokalne `to` ≤ lokalne `from` + 31 dni.
  Przykład: `from=2026-10-01`, `to=2026-11-01` jest dozwolone, mimo że przez zmianę czasu to 31 d + 1 h czasu bezwzględnego.
- **404**: zasób nie istnieje (zasób nieaktywny nadal zwraca swoje rezerwacje).
- **200**: tablica Booking.

### 4. `POST /bookings`
Body:
```json
{
  "resourceId": "…",
  "bookedBy": "Jan",
  "title": "Daily",
  "startUtc": "2026-10-21T08:00:00Z",
  "endUtc": "2026-10-21T09:00:00Z"
}
```
Kolejność sprawdzeń (pierwszy błąd kończy):
1. Walidacja wejścia → **400**: `resourceId` niepusty, `bookedBy` wymagane po `Trim()` 1–60, `title` ≤ 100 (pusty/biały → `null`), `startUtc`/`endUtc` wymagane.
   Brak, zły format lub brak offsetu w czasie → błąd pod kluczem tego pola (`errors.startUtc` / `errors.endUtc`).
2. Zasób nie istnieje lub `isActive = false` → **404**.
3. Reguły czasowe `IBookingPolicy.Validate` → **400** (klucze `startUtc`/`endUtc`).
4. W jednej transakcji: kolizja (`IBookingPolicy.FindConflict`) → **409** z `conflictingBookingId`; inaczej zapis.
- **201**, `Location: /bookings/{id}`, body = Booking. (Brak `GET /bookings/{id}` w v1.)
- Dwa równoczesne żądania na ten sam slot: dokładnie jedno 201, drugie 409.

### 5. `DELETE /bookings/{id}`
- **204** bez body; ustawia `CancelledAt = now` (TimeProvider).
- **404**: nie istnieje lub już anulowana.
- **409**: rezerwacja już się zakończyła (`endUtc <= now`). Trwającą można anulować.

### 6. `GET /resources/available?start=…&end=…&type=…&minCapacity=…` (v1.1)
- `start`, `end` wymagane: pełny ISO 8601 z offsetem (bez wariantu samej daty). `type` opcjonalny jak w endpoincie 1. `minCapacity` opcjonalny, liczba 1–50.
- Kolejność sprawdzeń:
  1. Format parametrów → **400** z kluczami `start`, `end`, `type`, `minCapacity`.
  2. Reguły czasowe `IBookingPolicy.Validate` (z `ResourceId = Guid.Empty`, `BookedBy = ""`; pola tekstowe policy nie sprawdza) → **400**. Klucze błędów policy mapowane: `startUtc` → `start`, `endUtc` → `end`.
- **200**: tablica Resource (jak w endpoincie 1): aktywne zasoby pasujące do `type` i `capacity >= minCapacity`, bez żadnej niezanulowanej rezerwacji nachodzącej na `[start, end)`.
  Sortowanie: `capacity` rosnąco, potem `type` (Room, Desk), potem `name`. Pusta tablica, gdy nic nie ma.
- Trasa `/resources/available` nie może kolidować z `/resources/{id}/bookings` (id ma ograniczenie `:guid`).

### 7. `PUT /bookings/{id}` (v1.1)
Body (wszystkie trzy pola wysyłane zawsze; zasób i `bookedBy` niezmienne):
```json
{ "startUtc": "2026-10-21T08:30:00Z", "endUtc": "2026-10-21T09:30:00Z", "title": "Daily (przesunięte)" }
```
Kolejność sprawdzeń (pierwszy błąd kończy):
1. Walidacja wejścia → **400**: jak w `POST /bookings` dla `startUtc`, `endUtc`, `title`.
2. Rezerwacja nie istnieje lub anulowana → **404**. Jej zasób nieaktywny → **404**.
3. Rezerwacja już się rozpoczęła (`startUtc <= now`, stary termin) → **409** ogólny (`title: "Konflikt."`, `detail` o rozpoczętej rezerwacji, **bez** `conflictingBookingId`).
4. Reguły czasowe `IBookingPolicy.Validate` dla nowego terminu → **400** (klucze `startUtc`/`endUtc`).
5. W jednej transakcji: ponowne sprawdzenie 2–3, kolizja z **innymi** rezerwacjami zasobu (edytowana wyłączona z listy przekazanej do `FindConflict`) → **409** z `conflictingBookingId`; inaczej zapis.
- **200**, body = Booking po zmianie (`createdAt` bez zmian).
- Równoległe `PUT`/`POST` na ten sam wolny slot: dokładnie jedno się udaje (200/201), drugie 409.

### 8. `GET /bookings?bookedBy=…` (v1.1)
- `bookedBy` wymagane, po `Trim()` 1–60 znaków → inaczej **400** (`errors.bookedBy`).
- Porównanie bez wielkości liter, także dla polskich znaków (np. kolumna znormalizowana `ToUpperInvariant()` jak przy nazwie zasobu; migracja EF).
- **200**: niezanulowane rezerwacje tej osoby z `endUtc > now`, ze wszystkich zasobów (także nieaktywnych), sortowane po `startUtc`, maks. 100.
  Element tablicy = Booking z dwoma dodatkowymi polami:
```json
{
  "id": "…", "resourceId": "…", "bookedBy": "Łukasz", "title": null,
  "startUtc": "2026-10-21T08:00:00+00:00", "endUtc": "2026-10-21T09:00:00+00:00", "createdAt": "…",
  "resourceName": "Alfa", "resourceType": "Room"
}
```

## Reguły czasowe (Domain, `BookingPolicy`)
- `start < end`; czas trwania 15 min – 8 h.
- `start` i `end` wyrównane do 15 min (sekundy i milisekundy = 0) w strefie biura.
- Start i koniec tego samego dnia kalendarzowego w strefie biura; `end` = 20:00 dozwolone.
- Godziny pracy 08:00–20:00 czasu lokalnego biura, także w dni zmiany czasu (DST).
- `start >= now` (z `TimeProvider`).
- Komunikaty po polsku, klucze `startUtc` / `endUtc`.

## Kontrakt hostingu (dla testów integracyjnych)
- `public partial class Program;` w Api (jest w szkielecie) → `WebApplicationFactory<Program>`.
- `TimeProvider` rejestrowany jako singleton i używany wszędzie (policy, `CreatedAt`, `CancelledAt`). Testy podmieniają go:
  `ConfigureTestServices(s => s.AddSingleton<TimeProvider>(fakeTimeProvider))`.
- `IBookingPolicy` → `BookingPolicy` (singleton), `BookingPolicyOptions` z sekcji `Office` w `appsettings.json`.
- Connection string: `ConnectionStrings:RoomBooking`, domyślnie `Data Source=roombooking.db`. Czytany **leniwie** w
  `AddDbContext((sp, o) => o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("RoomBooking")))`,
  żeby testy mogły ustawić `builder.UseSetting("ConnectionStrings:RoomBooking", "Data Source=<plik tymczasowy>")`.
- Przy starcie: `Database.MigrateAsync()` + seed, jeśli tabela zasobów jest pusta. Seed: Room Alfa 8, Room Beta 4,
  Room Gamma 12, Desk D1–D4 (capacity 1).
- GUI: `UseDefaultFiles` + `UseStaticFiles` z `wwwroot`, fallback na `index.html` dla nieznanych ścieżek GET (nie dla API).
- Swagger UI pod `/swagger` tylko w `Development`.

## Uwagi implementacyjne
- SQLite nie porównuje/sortuje `DateTimeOffset` w SQL: użyj konwertera wartości (np. do `long` UTC ticks) albo równoważnego rozwiązania.
- `POST /bookings`: transakcja `BeginTransactionAsync()` w SQLite to `BEGIN IMMEDIATE` (blokada zapisu), w niej odczyt kolidujących rezerwacji i zapis.
