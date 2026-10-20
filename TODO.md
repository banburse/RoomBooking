# TODO

Otwarte uwagi po review (poziom minor/nit lub zaakceptowane ograniczenia). Żadna nie blokuje v1.

## Dev-A (API)

Nity z review PR #1:

- [ ] `src/RoomBooking.Api/Endpoints/BookingEndpoints.cs:65` — reguła kolizji jest powielona: filtr nakładania w SQL i `IBookingPolicy.FindConflict`. Zawęzić zapytanie tylko do zakresu (albo zostawić i opisać, że SQL to wstępne zawężenie), żeby reguła żyła w jednym miejscu.
- [ ] `src/RoomBooking.Api/Endpoints/ResourceEndpoints.cs:56` — sortowanie po nazwie w kolacji BINARY (wielkie litery i polskie znaki w złej kolejności). Sortować po znormalizowanej nazwie albo w pamięci z `StringComparer` dla `pl-PL`.
- [ ] `src/RoomBooking.Api/Endpoints/ResourceEndpoints.cs:98` — duplikat nazwy rozpoznawany po ogólnym kodzie SQLite 19 (`SQLITE_CONSTRAINT`). Sprawdzać rozszerzony kod 2067 (`SQLITE_CONSTRAINT_UNIQUE`), żeby inne naruszenia ograniczeń nie dawały 409.
- [ ] `src/RoomBooking.Api/Program.cs:28` — `ThrowOnBadRequest = true` sprawia, że błędy klienta (zły JSON) są logowane jako Error. Obniżyć poziom logowania w `ProblemExceptionHandler` dla `BadHttpRequestException`.

Minor z pełnego review PR #6:

- [ ] `src/RoomBooking.Api/Program.cs:63` — fallback SPA przechwytuje żądania, które routing powinien odrzucić: `POST /bookings` z `Content-Type: text/plain` → 404 zamiast 415, `PUT /bookings` → 404 zamiast 405. GUI nie jest dotknięte (zawsze wysyła JSON). Ograniczyć fallback do GET spoza ścieżek API i resztę zostawić routingowi.

## Znane ograniczenia (zaakceptowane)

- [ ] **GUI: strefa siatki vs. godziny pracy.** Siatka 08:00–20:00 jest rysowana w strefie przeglądarki, a reguły godzin pracy liczone w `Europe/Warsaw`. Użytkownik spoza tej strefy zobaczy sloty, które API odrzuci kodem 400. Dla użytkowników w Polsce bez znaczenia. Możliwa poprawka: rysować siatkę w strefie biura (z API lub konfiguracji) i tylko etykiety pokazywać lokalnie.
