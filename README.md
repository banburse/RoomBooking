# RoomBooking

Rezerwacja sal i biurek: REST API (ASP.NET Core Minimal API, .NET 10, EF Core + SQLite) z prostym GUI w przeglądarce.

## Uruchomienie

```
dotnet run --project src/RoomBooking.Api
```

- GUI: http://localhost:5000/
- Swagger UI: http://localhost:5000/swagger (środowisko Development, domyślne przy `dotnet run`)

Baza `roombooking.db` (SQLite) tworzy się sama przy starcie (migracje EF) i dostaje dane startowe:
sale Alfa (8 os.), Beta (4 os.), Gamma (12 os.) oraz biurka D1–D4.

Konfiguracja w `src/RoomBooking.Api/appsettings.json`: connection string `ConnectionStrings:RoomBooking`
i reguły biura w sekcji `Office` (strefa `Europe/Warsaw`, godziny 08:00–20:00, 15 min – 8 h, siatka 15 min).

Testy: `dotnet test`.

## API

Szczegółowy kontrakt (kształty JSON, kody błędów): [CONTRACT.md](CONTRACT.md). Błędy zwracane są jako
`application/problem+json` (RFC 9457) z komunikatami po polsku. Czasy w żądaniach muszą mieć offset (`Z` lub `+02:00`),
odpowiedzi są zawsze w UTC.

```bash
# 1. Lista aktywnych zasobów (opcjonalny filtr: Room | Desk)
curl -s "http://localhost:5000/resources?type=Room"

# 2. Nowy zasób -> 201 + Location; 400 walidacja; 409 duplikat nazwy (bez wielkości liter)
curl -s -X POST http://localhost:5000/resources \
  -H "Content-Type: application/json" \
  -d '{"name":"Delta","type":"Room","capacity":6}'

# 3. Rezerwacje zasobu w zakresie [from, to), maks. 31 dni; data yyyy-MM-dd = północ w strefie biura
curl -s "http://localhost:5000/resources/<resourceId>/bookings?from=2026-10-19&to=2026-10-26"

# 4. Rezerwacja -> 201 + Location; 400 walidacja/reguły; 404 brak zasobu; 409 kolizja (pole conflictingBookingId)
curl -s -X POST http://localhost:5000/bookings \
  -H "Content-Type: application/json" \
  -d '{"resourceId":"<resourceId>","bookedBy":"Jan","title":"Daily","startUtc":"2026-10-21T08:00:00Z","endUtc":"2026-10-21T09:00:00Z"}'

# 5. Anulowanie (soft delete) -> 204; 404 nie istnieje / już anulowana; 409 rezerwacja już się zakończyła
curl -s -X DELETE http://localhost:5000/bookings/<bookingId>
```

## GUI

Pod http://localhost:5000/:

- Wybór sali lub biurka z listy rozwijanej.
- Widok tygodnia (pn–pt, 08:00–20:00 co 30 min), strzałki poprzedni/następny tydzień i przycisk „Dziś”.
  Godziny pokazywane w strefie przeglądarki.
- Klik w pusty slot otwiera okienko rezerwacji (imię, tytuł, długość). Imię jest zapamiętywane w przeglądarce.
- Klik w blok rezerwacji pokazuje szczegóły i przycisk „Anuluj rezerwację”.
- Kolizje (409) i błędy walidacji są pokazywane w okienku.
