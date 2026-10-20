# RoomBooking: specyfikacja

Aplikacja do rezerwacji sal i biurek: REST API + minimalne GUI w przeglądarce. Projekt testowy do pracy z agentami.

## Stack
- C# / .NET 10, ASP.NET Core Minimal API
- EF Core + SQLite (plik `roombooking.db`), migracje EF, seed danych przy starcie
- GUI: statyczny HTML + vanilla JS + CSS w `wwwroot` (bez npm, bez builda), serwowane przez to samo API
- xUnit + `WebApplicationFactory`
- Bez autoryzacji (v1). Użytkownik podaje imię przy rezerwacji.

## Uruchomienie (wymóg)
```
dotnet run --project src/RoomBooking.Api
```
GUI pod `http://localhost:5000/`, Swagger pod `/swagger` (Development). Jedna komenda, nic więcej do instalacji.

## Model danych

**Resource**
| Pole | Typ | Uwagi |
|---|---|---|
| Id | Guid | PK |
| Name | string(80) | wymagane, unikalne |
| Type | enum `Room` / `Desk` | |
| Capacity | int | 1-50 |
| IsActive | bool | domyślnie true |

**Booking**
| Pole | Typ | Uwagi |
|---|---|---|
| Id | Guid | PK |
| ResourceId | Guid | FK -> Resource |
| BookedBy | string(60) | wymagane |
| Title | string(100)? | opcjonalne |
| StartUtc | DateTimeOffset | |
| EndUtc | DateTimeOffset | |
| CreatedAt | DateTimeOffset | |
| CancelledAt | DateTimeOffset? | soft delete |

**Seed:** 3 sale (Alfa 8 os., Beta 4 os., Gamma 12 os.) i 4 biurka (D1-D4).

## Endpointy (5)

1. `GET /resources?type=Room|Desk` lista aktywnych zasobów.
2. `POST /resources` tworzy zasób. 201 + `Location`, 400 walidacja, 409 duplikat nazwy.
3. `GET /resources/{id}/bookings?from=2026-10-19&to=2026-10-26` aktywne (niezanulowane) rezerwacje zasobu w zakresie `[from, to)`, posortowane po starcie. 404 gdy zasób nie istnieje. Maks. zakres 31 dni.
4. `POST /bookings` body: `{ "resourceId", "bookedBy", "title", "startUtc", "endUtc" }`. 201 + `Location`. 400 walidacja/reguły, 404 brak zasobu, **409 kolizja** (w body `ProblemDetails` z polem `conflictingBookingId`).
5. `DELETE /bookings/{id}` anuluje rezerwację (soft delete). 204. 404 gdy nie istnieje lub już anulowana. 409 gdy rezerwacja już się zakończyła.

## Reguły biznesowe

Kalkulacje w `RoomBooking.Domain`, czyste i bez zależności od EF:

- **Kolizje:** przedziały półotwarte `[start, end)`. Rezerwacja 10:00-11:00 i 11:00-12:00 się **nie** koliduje. Anulowane rezerwacje nie blokują slotu.
- `start < end`; czas trwania min. **15 min**, maks. **8 h**.
- Początek i koniec wyrównane do **15 minut**.
- Rezerwacja w obrębie **jednego dnia** (w strefie biura).
- **Godziny pracy:** 08:00-20:00 w strefie `Europe/Warsaw` (konfigurowalna). Dane w bazie i API w UTC. Reguły godzin liczone w strefie biura, w tym dni zmiany czasu (DST).
- Nie można rezerwować w przeszłości (`start >= now`). Czas pobierany przez `TimeProvider`, żeby dało się testować.
- Zasób nieaktywny: 404 przy rezerwacji.
- **Współbieżność:** dwa równoczesne `POST /bookings` na ten sam slot, dokładnie jeden dostaje 201, drugi 409. Sprawdzenie kolizji i zapis w jednej transakcji. Wymagany test na to.

## GUI (`wwwroot/`)

Pliki: `index.html`, `app.js`, `style.css`.

- Wybór zasobu z listy rozwijanej (sale i biurka).
- **Widok tygodnia:** siatka pn-pt, wiersze co 30 min od 08:00 do 20:00. Strzałki poprzedni/następny tydzień i przycisk "Dziś".
- Rezerwacje jako kolorowe bloki z imieniem i tytułem.
- Klik w pusty slot otwiera okienko (`<dialog>`): imię, tytuł, długość (30 min / 1 h / 2 h / własna). Zapis wywołuje `POST /bookings`.
- Klik w blok pokazuje szczegóły i przycisk "Anuluj rezerwację".
- Błąd 409 i błędy walidacji pokazywane czytelnie w okienku (komunikat z `ProblemDetails`), bez `alert()`.
- Godziny wyświetlane w strefie przeglądarki użytkownika (API zwraca UTC). Imię zapamiętane w `localStorage`.
- Responsywne na tyle, by nie rozsypało się na wąskim ekranie (poziomy scroll siatki jest OK).
- Bez frameworków i bibliotek zewnętrznych (brak CDN).

## Wymagania niefunkcjonalne
- Błędy jako `ProblemDetails` (RFC 9457)
- Walidacja wejścia w jednym miejscu
- Struktura: `src/RoomBooking.Api`, `src/RoomBooking.Domain`, `tests/RoomBooking.Tests`
- `async/await` + `CancellationToken` w całym API
- Pokrycie testami logiki domenowej: min. 90%

## Przypadki brzegowe do przetestowania
- Back-to-back (koniec = start następnej) przechodzi
- Pełne zawarcie, częściowe nakładanie z lewej i z prawej, identyczny przedział
- Rezerwacja na zmianie czasu (ostatnia niedziela marca i października)
- 07:45-08:15 (przed otwarciem) i 19:45-20:15 (po zamknięciu) odrzucone
- Anulowana rezerwacja zwalnia slot
- Dwa równoległe żądania na ten sam slot
- Zakres `from`/`to` na granicy tygodnia

## Definition of Done
- `dotnet build` bez warningów, `dotnet test` zielone
- Każdy endpoint ma min. 1 test happy path i 1 test błędu
- GUI działa: da się zobaczyć tydzień, zarezerwować slot, zobaczyć konflikt i anulować
- README z komendą uruchomienia, przykładami `curl` i krótkim opisem GUI
- Review bez otwartych uwag blocker/major
