# Plan pracy agentów: RoomBooking

## Role

| Agent | Zakres | Czego NIE rusza |
|---|---|---|
| **Orchestrator** (główna sesja) | Szkielet, kontrakt, merge, pilnowanie limitu iteracji | Nie pisze kodu funkcjonalnego |
| **Dev-A: API** | `RoomBooking.Api`: endpointy, EF Core, migracje, seed, walidacja, ProblemDetails, serwowanie `wwwroot` | `Domain/`, `wwwroot/` |
| **Dev-B: Domain + testy** | `RoomBooking.Domain` (kolizje, reguły czasu), testy jednostkowe, testy integracyjne API (w tym współbieżność) | Kod endpointów, `wwwroot/` |
| **Dev-C: Frontend** | `wwwroot/`: HTML/JS/CSS wg sekcji GUI w SPEC.md | `src/`, `tests/` |
| **Reviewer** | Read-only: diff, build, test, ręczny smoke test przez `curl` | Nie edytuje kodu, tylko raport |

Wszyscy devowie pracują równolegle, każdy na osobnym worktree/branchu. Pliki każdego należą do jednego agenta, więc konflikty merge nie powinny występować.

## Cykl

0. **Kontrakt (Orchestrator):**
   - szkielet solucji (3 projekty, `dotnet build` przechodzi),
   - interfejsy i typy w Domain: `IBookingPolicy`, `BookingRequest`, wynik walidacji,
   - plik `CONTRACT.md` z dokładnymi JSON-ami żądań i odpowiedzi dla 5 endpointów (w tym kształt `ProblemDetails` przy 409),
   - commit na `main`.
   Bez tego Dev-C nie ma do czego pisać, a Dev-A z Dev-B się rozjadą.
1. **Implementacja (równolegle):**
   - Dev-A: model, DbContext, migracja, seed, endpointy 1, 2, 3, 5, na końcu 4 (z użyciem polityki z Domain), `UseStaticFiles` + fallback na `index.html`.
   - Dev-B: implementacja `IBookingPolicy` z testami brzegowymi, potem testy integracyjne (`WebApplicationFactory`), w tym test równoległych rezerwacji.
   - Dev-C: GUI wg `CONTRACT.md`. Na początku może użyć mocków w JS, potem przełącza na prawdziwe API.
2. **Merge do `integration`** (Orchestrator), `dotnet build` + `dotnet test`, ręczne `dotnet run` i sprawdzenie, że `/` się ładuje.
3. **Review:** Reviewer sprawdza:
   - zgodność ze SPEC.md i CONTRACT.md (kody HTTP, nazwy pól),
   - kolizje: półotwarte przedziały, anulowane nie blokują,
   - strefy i DST, `TimeProvider`, brak `DateTime.Now`/`UtcNow` w logice,
   - transakcja i współbieżność przy `POST /bookings`,
   - N+1 i brak `AsNoTracking` w zapytaniach tylko-do-odczytu,
   - `CancellationToken`, walidacja wejścia,
   - frontend: XSS (`textContent` zamiast `innerHTML` dla danych użytkownika), obsługa błędów 409/400, brak `alert()`, strefa wyświetlania,
   - jakość testów (czy faktycznie łapią regresje).
4. **Poprawki:** uwagi blocker/major wracają do właściwego deva (wg zakresu).
5. **Re-review** tylko zmienionych fragmentów.
6. **Stop:** brak blocker/major albo max **2 rundy** poprawek. Reszta idzie do `TODO.md`.

## Format raportu Reviewera

```
[blocker|major|minor|nit] plik:linia: problem -> sugestia
```
Na końcu: werdykt `APPROVE` albo `CHANGES_REQUESTED`, wynik `dotnet test` i wynik smoke testu.

Przykładowy smoke test:
```
curl -s localhost:5000/resources
curl -s -X POST localhost:5000/bookings -H "Content-Type: application/json" -d '{...}'   # 201
curl -s -X POST localhost:5000/bookings -H "Content-Type: application/json" -d '{...}'   # to samo -> 409
```

## Subagenty (Claude Code, `.claude/agents/`)

`dev-api.md`
```markdown
---
name: dev-api
description: Implementuje warstwę API (Minimal API, EF Core, SQLite) wg SPEC.md i CONTRACT.md
tools: Read, Write, Edit, Bash, Glob, Grep
---
Pracujesz tylko w src/RoomBooking.Api. Trzymaj się SPEC.md i CONTRACT.md.
Nie zmieniaj interfejsów w Domain ani plików w wwwroot.
Kolizje sprawdzaj i zapisuj w jednej transakcji. Po zmianach uruchom dotnet build.
```

`dev-domain.md`
```markdown
---
name: dev-domain
description: Logika domenowa rezerwacji i testy (xUnit)
tools: Read, Write, Edit, Bash, Glob, Grep
---
Pracujesz w src/RoomBooking.Domain i tests/. Najpierw testy przypadków brzegowych ze SPEC.md, potem implementacja.
Używaj TimeProvider, nigdy DateTime.Now. Pamiętaj o DST.
```

`dev-frontend.md`
```markdown
---
name: dev-frontend
description: Frontend (HTML/JS/CSS bez builda) w wwwroot
tools: Read, Write, Edit, Bash, Glob, Grep
---
Pracujesz tylko w src/RoomBooking.Api/wwwroot. Bez frameworków i CDN.
Dane użytkownika wstawiaj przez textContent. Trzymaj się CONTRACT.md.
Błędy pokazuj w UI, nie przez alert().
```

`reviewer.md`
```markdown
---
name: reviewer
description: Read-only code review zgodny ze SPEC.md
tools: Read, Glob, Grep, Bash
---
Nie edytujesz plików. Uruchom dotnet build i dotnet test, przeczytaj diff, zrób smoke test curl-em.
Raportuj w formacie [severity] plik:linia: problem -> sugestia.
Zakończ werdyktem APPROVE lub CHANGES_REQUESTED.
```

## Prompt startowy dla Orchestratora

> Przeczytaj SPEC.md i AGENT_PLAN.md. Wykonaj krok 0 (szkielet, interfejsy w Domain, CONTRACT.md, commit). Potem uruchom dev-api, dev-domain i dev-frontend równolegle w osobnych worktree. Po ich zakończeniu scal do `integration`, uruchom reviewera i prowadź cykl poprawek zgodnie z planem (max 2 rundy). Na końcu podsumuj stan, otwarte uwagi i podaj komendę do uruchomienia aplikacji.

## Co obserwować w teście
- Czy agenci trzymają się swoich katalogów (brak konfliktów w merge)?
- Czy CONTRACT.md wystarcza, żeby front i API zadziałały razem za pierwszym razem?
- Czy Reviewer wyłapuje realne problemy (DST, współbieżność, XSS), a nie tylko nity?
- Ile rund poprawek i ile tokenów to kosztuje?
