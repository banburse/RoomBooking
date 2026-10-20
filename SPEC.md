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

## v1.1: wyszukiwanie, edycja, moje rezerwacje

### Nowe endpointy (3)
6. `GET /resources/available?start=…&end=…&type=Room|Desk&minCapacity=N` aktywne zasoby wolne w `[start, end)`. Te same reguły czasowe co przy rezerwacji (godziny pracy, wyrównanie, przeszłość), więc znaleziony zasób da się od razu zarezerwować. Sortowane od najmniejszej pasującej pojemności.
7. `PUT /bookings/{id}` zmiana `startUtc`, `endUtc`, `title` rezerwacji, która jeszcze się nie rozpoczęła (zasób i imię bez zmian). 200 + rezerwacja. 400 walidacja/reguły, 404 brak lub anulowana, 409 kolizja (z `conflictingBookingId`) albo rezerwacja już się rozpoczęła.
8. `GET /bookings?bookedBy=Jan` nadchodzące i trwające (niezanulowane, `end > now`) rezerwacje danej osoby ze wszystkich zasobów, z nazwą zasobu. Imię porównywane bez wielkości liter (także polskie znaki), po `Trim()`.

### Reguły
- Edycja nie koliduje sama ze sobą: przesunięcie 10:00-11:00 na 10:30-11:30 przechodzi, jeśli slot 11:00-11:30 jest wolny.
- **Współbieżność:** `PUT` i `POST` (lub dwa `PUT`) na ten sam wolny slot: dokładnie jedno się udaje, drugie dostaje 409. Sprawdzenie i zapis w jednej transakcji. Wymagany test.
- Logika domenowa (`IBookingPolicy`) bez zmian: wyszukiwanie i edycja korzystają z `Validate` i `FindConflict`.

### GUI
- **Znajdź wolną salę:** panel z datą, godziną startu (krok 15 min), długością (30 min / 1 h / 2 h / własna), typem (dowolny / sala / biurko) i minimalną pojemnością. Wyniki jako lista (nazwa, typ, pojemność) z przyciskiem „Zarezerwuj”, który wybiera zasób, przechodzi do właściwego tygodnia i otwiera okienko rezerwacji z wypełnionym czasem. Pusta lista i błędy pokazane w panelu.
- **Edycja:** w szczegółach rezerwacji, która się jeszcze nie rozpoczęła, przycisk „Edytuj”: godzina startu, godzina końca (ten sam dzień), tytuł. Zapis przez `PUT`, błędy 400/409 w okienku.
- **Moje rezerwacje:** lista nadchodzących rezerwacji dla imienia z `localStorage` (z możliwością wpisania innego): zasób, data, godziny, tytuł. Klik w pozycję przechodzi do zasobu i tygodnia i otwiera szczegóły. Pusty stan z komunikatem.

### Przypadki brzegowe do przetestowania (v1.1)
- Wyszukiwanie: zasób z rezerwacją back-to-back jest wolny; z anulowaną rezerwacją jest wolny; nieaktywny nie jest zwracany; `minCapacity` i `type` filtrują
- Edycja: przesunięcie nachodzące na własny stary termin przechodzi; na cudzy daje 409; rozpoczęta daje 409; anulowana daje 404
- `bookedBy` bez wielkości liter z polskimi znakami („łukasz” znajduje „Łukasz”)
- Równoległe `PUT` i `POST` na ten sam slot

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
- (v1.1) GUI: da się znaleźć wolną salę i z wyniku zarezerwować, edytować rezerwację, zobaczyć swoje rezerwacje
- README z komendą uruchomienia, przykładami `curl` i krótkim opisem GUI
- Review bez otwartych uwag blocker/major
