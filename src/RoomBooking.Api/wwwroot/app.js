/*
 * RoomBooking GUI: vanilla JS, bez bibliotek.
 * Kontrakt API: CONTRACT.md. Wszystkie dane z API wstawiane wyłącznie przez textContent.
 * Tryb demo bez backendu: dodaj ?mock=1 do adresu strony.
 */
(function () {
  'use strict';

  // ---------- Stałe ----------

  var DAY_START_MIN = 8 * 60;   // 08:00
  var DAY_END_MIN = 20 * 60;    // 20:00
  var SLOT_MIN = 30;
  var SLOT_COUNT = (DAY_END_MIN - DAY_START_MIN) / SLOT_MIN; // 24
  var DAYS = 5;                 // pn-pt
  var NAME_KEY = 'roombooking.bookedBy';
  var RESOURCE_KEY = 'roombooking.resourceId';
  var COLOR_CLASSES = 6;

  var MOCK = /(?:^|[?&])mock=1(?:&|$)/.test(window.location.search);

  var timeFmt = new Intl.DateTimeFormat('pl-PL', { hour: '2-digit', minute: '2-digit' });
  var dayHeadFmt = new Intl.DateTimeFormat('pl-PL', { weekday: 'short' });
  var dayDateFmt = new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'numeric' });
  var longDateFmt = new Intl.DateTimeFormat('pl-PL', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  var rangeFmt = new Intl.DateTimeFormat('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' });
  var dateTimeFmt = new Intl.DateTimeFormat('pl-PL', { dateStyle: 'medium', timeStyle: 'short' });

  // ---------- Stan ----------

  var state = {
    resources: [],
    resourceId: '',
    weekStart: startOfWeek(new Date()),
    bookings: [],
    loadSeq: 0,
    pendingSlotStart: null,
    bookingDialogSeq: 0,
    selectedBooking: null
  };

  // ---------- Elementy ----------

  function $(id) { return document.getElementById(id); }

  var el = {
    resourceSelect: $('resource-select'),
    prevWeek: $('prev-week'),
    nextWeek: $('next-week'),
    todayBtn: $('today-btn'),
    weekRange: $('week-range'),
    status: $('status'),
    grid: $('week-grid'),
    tzName: $('tz-name'),
    mockBadge: $('mock-badge'),

    bookingDialog: $('booking-dialog'),
    bookingForm: $('booking-form'),
    bookingSummary: $('booking-summary'),
    bookedBy: $('booked-by'),
    bookingTitle: $('booking-title'),
    duration: $('duration'),
    customEndRow: $('custom-end-row'),
    customEnd: $('custom-end'),
    endPreview: $('booking-end-preview'),
    bookingError: $('booking-error'),
    bookingCancelBtn: $('booking-cancel-btn'),
    bookingSaveBtn: $('booking-save-btn'),

    detailsDialog: $('details-dialog'),
    detailsResource: $('details-resource'),
    detailsBookedBy: $('details-booked-by'),
    detailsTitle: $('details-title'),
    detailsWhen: $('details-when'),
    detailsCreated: $('details-created'),
    detailsError: $('details-error'),
    detailsCloseBtn: $('details-close-btn'),
    detailsDeleteBtn: $('details-delete-btn')
  };

  // ---------- Daty ----------

  function startOfWeek(date) {
    var d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    var dow = (d.getDay() + 6) % 7; // 0 = poniedziałek
    d.setDate(d.getDate() - dow);
    return d;
  }

  function addDays(date, n) {
    var d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    d.setDate(d.getDate() + n);
    return d;
  }

  // Lokalna data + minuty od północy (przez setHours, żeby poprawnie działało w dni zmiany czasu).
  function atMinutes(day, minutes) {
    return new Date(day.getFullYear(), day.getMonth(), day.getDate(), Math.floor(minutes / 60), minutes % 60, 0, 0);
  }

  function minutesOfDay(date) {
    return date.getHours() * 60 + date.getMinutes() + date.getSeconds() / 60;
  }

  function sameDay(a, b) {
    return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  }

  function fmtTime(d) { return timeFmt.format(d); }

  function fmtRange(start, end) {
    return longDateFmt.format(start) + ', ' + fmtTime(start) + '–' + fmtTime(end);
  }

  function durationLabel(minutes) {
    var h = Math.floor(minutes / 60);
    var m = minutes % 60;
    if (h && m) return h + ' h ' + m + ' min';
    if (h) return h + ' h';
    return m + ' min';
  }

  // ---------- API ----------

  // Zwraca { ok, status, data, network } — nigdy nie rzuca.
  function request(method, url, body) {
    if (MOCK) return mockRequest(method, url, body);

    var opts = { method: method, headers: { 'Accept': 'application/json, application/problem+json' } };
    if (body !== undefined) {
      opts.headers['Content-Type'] = 'application/json';
      opts.body = JSON.stringify(body);
    }
    return fetch(url, opts).then(function (res) {
      var ct = res.headers.get('Content-Type') || '';
      if (res.status === 204 || ct.indexOf('json') === -1) {
        return res.text().then(function () {
          return { ok: res.ok, status: res.status, data: null };
        });
      }
      return res.json().then(function (data) {
        return { ok: res.ok, status: res.status, data: data };
      }, function () {
        return { ok: res.ok, status: res.status, data: null };
      });
    }, function (err) {
      return { ok: false, status: 0, data: null, network: true, message: String(err && err.message || err) };
    });
  }

  var api = {
    getResources: function () { return request('GET', '/resources'); },
    getBookings: function (resourceId, from, to) {
      return request('GET', '/resources/' + encodeURIComponent(resourceId) + '/bookings' +
        '?from=' + encodeURIComponent(from.toISOString()) +
        '&to=' + encodeURIComponent(to.toISOString()));
    },
    createBooking: function (payload) { return request('POST', '/bookings', payload); },
    cancelBooking: function (id) { return request('DELETE', '/bookings/' + encodeURIComponent(id)); }
  };

  // ---------- Komunikaty błędów ----------

  var FALLBACK_TITLES = {
    0: 'Brak połączenia z serwerem.',
    400: 'Nieprawidłowe dane.',
    404: 'Nie znaleziono.',
    409: 'Konflikt.',
    500: 'Błąd serwera.'
  };

  // Buduje strukturę { title, lines[] } z ProblemDetails.
  function describeProblem(res) {
    var p = (res && res.data && typeof res.data === 'object') ? res.data : {};
    var title = typeof p.title === 'string' && p.title ? p.title
      : (FALLBACK_TITLES[res.status] || ('Błąd ' + res.status + '.'));
    var lines = [];
    if (typeof p.detail === 'string' && p.detail) lines.push(p.detail);
    if (p.errors && typeof p.errors === 'object') {
      Object.keys(p.errors).forEach(function (key) {
        var msgs = p.errors[key];
        if (!Array.isArray(msgs)) msgs = [msgs];
        msgs.forEach(function (m) { if (m) lines.push(String(m)); });
      });
    }
    if (res.network) lines.push('Sprawdź, czy API działa, i spróbuj ponownie.');
    else if (!lines.length && res.status >= 500) lines.push('Spróbuj ponownie za chwilę.');
    return { title: title, lines: lines };
  }

  function showErrorBox(box, info, extraLines) {
    clear(box);
    var strong = document.createElement('strong');
    strong.textContent = info.title;
    box.appendChild(strong);
    var lines = info.lines.concat(extraLines || []);
    if (lines.length) {
      var ul = document.createElement('ul');
      lines.forEach(function (line) {
        var li = document.createElement('li');
        li.textContent = line;
        ul.appendChild(li);
      });
      box.appendChild(ul);
    }
    box.hidden = false;
  }

  function hideErrorBox(box) {
    clear(box);
    box.hidden = true;
  }

  function clear(node) {
    while (node.firstChild) node.removeChild(node.firstChild);
  }

  // Pasek statusu nad siatką (ładowanie / błąd ładowania).
  function setStatus(kind, text, retry) {
    clear(el.status);
    if (!kind) { el.status.hidden = true; return; }
    el.status.className = 'status' + (kind === 'error' ? ' error' : '');
    var span = document.createElement('span');
    span.textContent = text;
    el.status.appendChild(span);
    if (retry) {
      var btn = document.createElement('button');
      btn.type = 'button';
      btn.textContent = 'Spróbuj ponownie';
      btn.addEventListener('click', retry);
      el.status.appendChild(btn);
    }
    el.status.hidden = false;
  }

  function problemToText(res) {
    var info = describeProblem(res);
    return [info.title].concat(info.lines).join(' ');
  }

  // ---------- Zasoby ----------

  function loadResources() {
    setStatus('info', 'Ładowanie listy zasobów…');
    el.resourceSelect.disabled = true;
    return api.getResources().then(function (res) {
      if (!res.ok || !Array.isArray(res.data)) {
        setStatus('error', 'Nie udało się pobrać listy zasobów. ' + problemToText(res), loadResources);
        fillResourceSelect([]);
        return;
      }
      state.resources = res.data;
      fillResourceSelect(res.data);
      setStatus(null);
      loadWeek();
    });
  }

  function fillResourceSelect(resources) {
    var sel = el.resourceSelect;
    clear(sel);

    if (!resources.length) {
      var empty = document.createElement('option');
      empty.value = '';
      empty.textContent = 'Brak zasobów';
      sel.appendChild(empty);
      sel.disabled = true;
      state.resourceId = '';
      return;
    }

    var groups = [
      { type: 'Room', label: 'Sale' },
      { type: 'Desk', label: 'Biurka' }
    ];
    groups.forEach(function (g) {
      var items = resources.filter(function (r) { return String(r.type).toLowerCase() === g.type.toLowerCase(); });
      if (!items.length) return;
      var og = document.createElement('optgroup');
      og.label = g.label;
      items.forEach(function (r) { og.appendChild(resourceOption(r)); });
      sel.appendChild(og);
    });
    // Zasoby o nieznanym typie (na wszelki wypadek).
    resources.filter(function (r) {
      var t = String(r.type).toLowerCase();
      return t !== 'room' && t !== 'desk';
    }).forEach(function (r) { sel.appendChild(resourceOption(r)); });

    var saved = readStorage(RESOURCE_KEY);
    var exists = resources.some(function (r) { return r.id === saved; });
    state.resourceId = exists ? saved : resources[0].id;
    sel.value = state.resourceId;
    sel.disabled = false;
  }

  function resourceOption(r) {
    var opt = document.createElement('option');
    opt.value = r.id;
    var isRoom = String(r.type).toLowerCase() === 'room';
    opt.textContent = isRoom && r.capacity ? r.name + ' (' + r.capacity + ' os.)' : String(r.name);
    return opt;
  }

  function currentResource() {
    for (var i = 0; i < state.resources.length; i++) {
      if (state.resources[i].id === state.resourceId) return state.resources[i];
    }
    return null;
  }

  // ---------- Tydzień ----------

  function weekBounds() {
    return { from: state.weekStart, to: addDays(state.weekStart, DAYS) }; // poniedziałek 00:00 .. sobota 00:00
  }

  function loadWeek() {
    renderWeekLabel();
    var seq = ++state.loadSeq;
    if (!state.resourceId) {
      state.bookings = [];
      renderGrid();
      return Promise.resolve();
    }
    var b = weekBounds();
    setStatus('info', 'Ładowanie rezerwacji…');
    return api.getBookings(state.resourceId, b.from, b.to).then(function (res) {
      if (seq !== state.loadSeq) return; // nowsze żądanie w toku
      if (!res.ok || !Array.isArray(res.data)) {
        state.bookings = [];
        renderGrid();
        setStatus('error', 'Nie udało się pobrać rezerwacji. ' + problemToText(res), loadWeek);
        return;
      }
      state.bookings = res.data.map(normalizeBooking).filter(Boolean);
      setStatus(null);
      renderGrid();
    });
  }

  function normalizeBooking(b) {
    if (!b || typeof b !== 'object') return null;
    var start = new Date(b.startUtc);
    var end = new Date(b.endUtc);
    if (isNaN(start.getTime()) || isNaN(end.getTime())) return null;
    return {
      id: b.id,
      resourceId: b.resourceId,
      bookedBy: b.bookedBy == null ? '' : String(b.bookedBy),
      title: b.title == null ? '' : String(b.title),
      start: start,
      end: end,
      createdAt: b.createdAt ? new Date(b.createdAt) : null
    };
  }

  function renderWeekLabel() {
    var fri = addDays(state.weekStart, DAYS - 1);
    el.weekRange.textContent = rangeFmt.format(state.weekStart) + ' – ' + rangeFmt.format(fri);
  }

  function colorClass(name) {
    var h = 0;
    for (var i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0;
    return 'c' + (h % COLOR_CLASSES);
  }

  function renderGrid() {
    var grid = el.grid;
    var now = new Date();
    var hasResource = !!state.resourceId;
    clear(grid);

    // Nagłówki
    var corner = document.createElement('div');
    corner.className = 'grid-head';
    grid.appendChild(corner);
    var days = [];
    for (var d = 0; d < DAYS; d++) {
      var day = addDays(state.weekStart, d);
      days.push(day);
      var head = document.createElement('div');
      head.className = 'grid-head' + (sameDay(day, now) ? ' today' : '');
      head.textContent = dayHeadFmt.format(day);
      var small = document.createElement('small');
      small.textContent = dayDateFmt.format(day);
      head.appendChild(small);
      grid.appendChild(head);
    }

    // Kolumna godzin
    var timeCol = document.createElement('div');
    timeCol.className = 'time-col';
    timeCol.setAttribute('aria-hidden', 'true');
    for (var s = 0; s < SLOT_COUNT; s++) {
      var lbl = document.createElement('div');
      lbl.className = 'time-label';
      lbl.textContent = fmtTime(atMinutes(days[0], DAY_START_MIN + s * SLOT_MIN));
      timeCol.appendChild(lbl);
    }
    grid.appendChild(timeCol);

    // Kolumny dni
    days.forEach(function (dayDate) {
      var col = document.createElement('div');
      col.className = 'day-col' + (sameDay(dayDate, now) ? ' today' : '');
      var dayOpen = atMinutes(dayDate, DAY_START_MIN);
      var dayClose = atMinutes(dayDate, DAY_END_MIN);
      var span = dayClose.getTime() - dayOpen.getTime();

      for (var i = 0; i < SLOT_COUNT; i++) {
        var slotStart = atMinutes(dayDate, DAY_START_MIN + i * SLOT_MIN);
        var slotEnd = atMinutes(dayDate, DAY_START_MIN + (i + 1) * SLOT_MIN);
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'slot' + (i % 2 === 1 ? ' full-hour' : '');
        // Gdy początek slotu minął, ale jego druga połowa (od :15/:45) jeszcze nie, rezerwacja startuje od niej.
        var bookStart = slotStart.getTime() < now.getTime() ? new Date(slotStart.getTime() + 15 * 60000) : slotStart;
        btn.setAttribute('aria-label', 'Zarezerwuj: ' + fmtRange(bookStart, slotEnd));
        if (!hasResource || bookStart.getTime() < now.getTime()) {
          btn.disabled = true;
        } else {
          btn.addEventListener('click', openBookingDialog.bind(null, bookStart));
        }
        col.appendChild(btn);
      }

      state.bookings.forEach(function (b) {
        var s = Math.max(b.start.getTime(), dayOpen.getTime());
        var e = Math.min(b.end.getTime(), dayClose.getTime());
        if (e <= s) return; // brak części w tym dniu w godzinach widoku
        col.appendChild(bookingBlock(b, (s - dayOpen.getTime()) / span, (e - s) / span, now));
      });

      grid.appendChild(col);
    });
  }

  function bookingBlock(b, topFrac, heightFrac, now) {
    var block = document.createElement('button');
    block.type = 'button';
    block.className = 'booking ' + colorClass(b.bookedBy) + (b.end.getTime() <= now.getTime() ? ' past' : '');
    block.style.top = (topFrac * 100) + '%';
    block.style.height = (heightFrac * 100) + '%';

    var time = document.createElement('span');
    time.className = 'b-time';
    time.textContent = fmtTime(b.start) + '–' + fmtTime(b.end);
    block.appendChild(time);

    var who = document.createElement('span');
    who.className = 'b-who';
    who.textContent = b.bookedBy;
    block.appendChild(who);

    if (b.title) {
      var title = document.createElement('span');
      title.className = 'b-title';
      title.textContent = b.title;
      block.appendChild(title);
    }

    block.title = fmtTime(b.start) + '–' + fmtTime(b.end) + ' ' + b.bookedBy + (b.title ? ': ' + b.title : '');
    block.setAttribute('aria-label', 'Rezerwacja ' + fmtRange(b.start, b.end) + ', ' + b.bookedBy +
      (b.title ? ', ' + b.title : ''));
    block.addEventListener('click', openDetailsDialog.bind(null, b));
    return block;
  }

  // ---------- Dialog rezerwacji ----------

  function openBookingDialog(slotStart) {
    state.pendingSlotStart = slotStart;
    state.bookingDialogSeq++; // unieważnia odpowiedzi z poprzednich otwarć okienka
    hideErrorBox(el.bookingError);
    var res = currentResource();
    el.bookingSummary.textContent = (res ? res.name + ', ' : '') + longDateFmt.format(slotStart) + ', od ' + fmtTime(slotStart);
    el.bookedBy.value = readStorage(NAME_KEY) || '';
    el.bookingTitle.value = '';
    el.duration.value = '30';
    var defEnd = Math.min(minutesOfDay(slotStart) + 60, DAY_END_MIN);
    el.customEnd.value = pad2(Math.floor(defEnd / 60)) + ':' + pad2(defEnd % 60);
    el.customEnd.min = hhmm(minutesOfDay(slotStart) + 15);
    // Wyłącz gotowe długości, które wychodzą poza koniec dnia pracy (np. 2 h od 19:30).
    var startMin = minutesOfDay(slotStart);
    Array.prototype.forEach.call(el.duration.options, function (opt) {
      opt.disabled = opt.value !== 'custom' && startMin + parseInt(opt.value, 10) > DAY_END_MIN;
    });
    if (el.duration.options[el.duration.selectedIndex].disabled) el.duration.value = 'custom'; // np. start 19:45
    updateDurationUi();
    el.bookingSaveBtn.disabled = false;
    el.bookingDialog.showModal();
    (el.bookedBy.value ? el.bookingTitle : el.bookedBy).focus();
  }

  function pad2(n) { return (n < 10 ? '0' : '') + n; }
  function hhmm(minutes) { return pad2(Math.floor(minutes / 60)) + ':' + pad2(minutes % 60); }

  // Zwraca Date końca albo null, gdy wybrana własna godzina jest niepoprawna.
  function computeEnd() {
    var start = state.pendingSlotStart;
    if (!start) return null;
    if (el.duration.value === 'custom') {
      var m = /^(\d{1,2}):(\d{2})/.exec(el.customEnd.value);
      if (!m) return null;
      return atMinutes(start, parseInt(m[1], 10) * 60 + parseInt(m[2], 10));
    }
    return new Date(start.getTime() + parseInt(el.duration.value, 10) * 60000);
  }

  // Koniec po 20:00 (lub następnego dnia) jest poza godzinami pracy.
  function endsAfterClose(start, end) {
    return end.getTime() > atMinutes(start, DAY_END_MIN).getTime();
  }

  function updateDurationUi() {
    el.customEndRow.hidden = el.duration.value !== 'custom';
    var start = state.pendingSlotStart;
    var end = computeEnd();
    if (!start || !end || end <= start) {
      el.endPreview.textContent = el.duration.value === 'custom' ? 'Podaj godzinę końca późniejszą niż początek.' : '';
      return;
    }
    var text = 'Termin: ' + fmtTime(start) + '–' + fmtTime(end) +
      ' (' + durationLabel(Math.round((end - start) / 60000)) + ')';
    if (endsAfterClose(start, end)) text += '. Uwaga: koniec po ' + hhmm(DAY_END_MIN) + ', poza godzinami pracy.';
    el.endPreview.textContent = text;
  }

  function submitBooking(ev) {
    ev.preventDefault();
    hideErrorBox(el.bookingError);

    var start = state.pendingSlotStart;
    var end = computeEnd();
    var name = el.bookedBy.value.trim();
    var title = el.bookingTitle.value.trim();

    var clientErrors = [];
    if (!name) clientErrors.push('Imię jest wymagane.');
    if (!end || end <= start) clientErrors.push('Godzina końca musi być późniejsza niż początek.');
    else if (endsAfterClose(start, end)) clientErrors.push('Rezerwacja musi zakończyć się najpóźniej o ' + hhmm(DAY_END_MIN) + '.');
    if (clientErrors.length) {
      showErrorBox(el.bookingError, { title: 'Popraw formularz.', lines: clientErrors });
      (name ? el.customEnd : el.bookedBy).focus();
      return;
    }

    writeStorage(NAME_KEY, name);
    el.bookingSaveBtn.disabled = true;

    // Odpowiedź dotyczy tylko tego otwarcia okienka; po zamknięciu/ponownym otwarciu ją pomijamy.
    var seq = state.bookingDialogSeq;
    function stillCurrent() { return seq === state.bookingDialogSeq && el.bookingDialog.open; }

    api.createBooking({
      resourceId: state.resourceId,
      bookedBy: name,
      title: title || null,
      startUtc: start.toISOString(),
      endUtc: end.toISOString()
    }).then(function (res) {
      if (!stillCurrent()) {
        loadWeek(); // stan mógł się zmienić; samo okienko zostawiamy w spokoju
        return;
      }
      el.bookingSaveBtn.disabled = false;
      if (res.ok) {
        el.bookingDialog.close();
        loadWeek();
        return;
      }
      var info = describeProblem(res);
      if (res.status === 409) {
        // Odśwież tydzień, żeby było widać kolidującą rezerwację; dopisz jej szczegóły.
        loadWeek().then(function () {
          if (!stillCurrent()) return;
          var extra = [];
          var cid = res.data && res.data.conflictingBookingId;
          var c = cid ? findBooking(cid) : null;
          if (c) {
            extra.push('Zajęte: ' + fmtTime(c.start) + '–' + fmtTime(c.end) + ', ' + c.bookedBy + (c.title ? ' (' + c.title + ')' : '') + '.');
          }
          showErrorBox(el.bookingError, info, extra);
        });
        showErrorBox(el.bookingError, info);
        return;
      }
      if (res.status === 404) loadWeek();
      showErrorBox(el.bookingError, info);
    });
  }

  function findBooking(id) {
    for (var i = 0; i < state.bookings.length; i++) {
      if (state.bookings[i].id === id) return state.bookings[i];
    }
    return null;
  }

  // ---------- Dialog szczegółów ----------

  function openDetailsDialog(b) {
    state.selectedBooking = b;
    hideErrorBox(el.detailsError);
    var res = currentResource();
    el.detailsResource.textContent = res ? res.name : '';
    el.detailsBookedBy.textContent = b.bookedBy;
    el.detailsTitle.textContent = b.title || '(brak)';
    el.detailsWhen.textContent = fmtRange(b.start, b.end);
    el.detailsCreated.textContent = b.createdAt && !isNaN(b.createdAt.getTime()) ? dateTimeFmt.format(b.createdAt) : '–';
    var ended = b.end.getTime() <= Date.now();
    el.detailsDeleteBtn.hidden = ended;
    el.detailsDeleteBtn.disabled = false;
    el.detailsDialog.showModal();
    el.detailsCloseBtn.focus();
  }

  function cancelSelectedBooking() {
    var b = state.selectedBooking;
    if (!b) return;
    hideErrorBox(el.detailsError);
    el.detailsDeleteBtn.disabled = true;
    api.cancelBooking(b.id).then(function (res) {
      if (res.ok) {
        el.detailsDialog.close();
        loadWeek();
        return;
      }
      showErrorBox(el.detailsError, describeProblem(res));
      if (res.status === 404 || res.status === 409) {
        el.detailsDeleteBtn.hidden = true;
        loadWeek();
      } else {
        el.detailsDeleteBtn.disabled = false;
      }
    });
  }

  // ---------- localStorage ----------

  function readStorage(key) {
    try { return window.localStorage.getItem(key); } catch (e) { return null; }
  }

  function writeStorage(key, value) {
    try { window.localStorage.setItem(key, value); } catch (e) { /* ignoruj */ }
  }

  // ---------- Mock API (?mock=1) ----------

  var mockDb = null;

  function mockInit() {
    function guid() {
      return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0;
        return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
      });
    }
    var resources = [
      ['Alfa', 'Room', 8], ['Beta', 'Room', 4], ['Gamma', 'Room', 12],
      ['D1', 'Desk', 1], ['D2', 'Desk', 1], ['D3', 'Desk', 1], ['D4', 'Desk', 1]
    ].map(function (r) { return { id: guid(), name: r[0], type: r[1], capacity: r[2], isActive: true }; });

    var mon = startOfWeek(new Date());
    var alfa = resources[0].id;
    function mk(resId, dayOffset, startMin, endMin, who, title) {
      var day = addDays(mon, dayOffset);
      return {
        id: guid(), resourceId: resId, bookedBy: who, title: title,
        startUtc: atMinutes(day, startMin).toISOString(), endUtc: atMinutes(day, endMin).toISOString(),
        createdAt: new Date().toISOString(), cancelled: false
      };
    }
    var bookings = [
      mk(alfa, 0, 9 * 60, 10 * 60, 'Jan', 'Daily'),
      mk(alfa, 1, 10 * 60 + 15, 11 * 60 + 45, 'Anna', 'Przegląd sprintu'),
      mk(alfa, 2, 13 * 60, 15 * 60, 'Piotr', null),
      mk(alfa, 3, 8 * 60, 8 * 60 + 30, '<b>Test</b>', '<img src=x>'),
      mk(alfa, 4, 16 * 60 + 45, 19 * 60, 'Ewa', 'Warsztaty z bardzo długim tytułem, który się nie zmieści'),
      mk(resources[3].id, 2, 8 * 60, 16 * 60, 'Marek', 'Praca przy biurku')
    ];
    mockDb = { resources: resources, bookings: bookings, guid: guid };
  }

  function mockProblem(status, title, detail, errors, extra) {
    var p = { type: 'about:blank', title: title, status: status };
    if (detail) p.detail = detail;
    if (errors) p.errors = errors;
    if (extra) Object.keys(extra).forEach(function (k) { p[k] = extra[k]; });
    return { ok: false, status: status, data: p };
  }

  function mockPublic(b) {
    return { id: b.id, resourceId: b.resourceId, bookedBy: b.bookedBy, title: b.title,
      startUtc: b.startUtc, endUtc: b.endUtc, createdAt: b.createdAt };
  }

  function mockRequest(method, url, body) {
    if (!mockDb) mockInit();
    var result;
    try {
      result = mockRoute(method, url, body ? JSON.parse(JSON.stringify(body)) : body);
    } catch (e) {
      result = mockProblem(500, 'Błąd mocka.', String(e));
    }
    return new Promise(function (resolve) { setTimeout(function () { resolve(result); }, 150); });
  }

  function mockRoute(method, url, body) {
    var parts = url.split('?');
    var path = parts[0];
    var query = {};
    (parts[1] || '').split('&').forEach(function (kv) {
      if (!kv) return;
      var i = kv.indexOf('=');
      query[decodeURIComponent(kv.slice(0, i))] = decodeURIComponent(kv.slice(i + 1));
    });
    var m;

    if (method === 'GET' && path === '/resources') {
      return { ok: true, status: 200, data: mockDb.resources.filter(function (r) { return r.isActive; }) };
    }

    if (method === 'GET' && (m = /^\/resources\/([^/]+)\/bookings$/.exec(path))) {
      var rid = decodeURIComponent(m[1]);
      var from = new Date(query.from), to = new Date(query.to);
      if (isNaN(from) || isNaN(to)) return mockProblem(400, 'Błąd walidacji.', null, { from: ['Nieprawidłowy zakres dat.'] });
      if (!mockDb.resources.some(function (r) { return r.id === rid; })) {
        return mockProblem(404, 'Nie znaleziono.', 'Zasób ' + rid + ' nie istnieje.');
      }
      var list = mockDb.bookings.filter(function (b) {
        return !b.cancelled && b.resourceId === rid && new Date(b.startUtc) < to && new Date(b.endUtc) > from;
      }).sort(function (a, b) { return new Date(a.startUtc) - new Date(b.startUtc); }).map(mockPublic);
      return { ok: true, status: 200, data: list };
    }

    if (method === 'POST' && path === '/bookings') {
      var errors = {};
      var name = (body.bookedBy || '').trim();
      if (!name) errors.bookedBy = ['Imię jest wymagane.'];
      else if (name.length > 60) errors.bookedBy = ['Imię może mieć maksymalnie 60 znaków.'];
      if (body.title && body.title.length > 100) errors.title = ['Tytuł może mieć maksymalnie 100 znaków.'];
      if (Object.keys(errors).length) return mockProblem(400, 'Błąd walidacji.', null, errors);

      var res = mockDb.resources.filter(function (r) { return r.id === body.resourceId && r.isActive; })[0];
      if (!res) return mockProblem(404, 'Nie znaleziono.', 'Zasób ' + body.resourceId + ' nie istnieje.');

      var s = new Date(body.startUtc), e = new Date(body.endUtc);
      var dur = (e - s) / 60000;
      var terr = {};
      if (!(s < e)) terr.endUtc = ['Koniec musi być późniejszy niż początek.'];
      else if (dur < 15) terr.endUtc = ['Rezerwacja musi trwać co najmniej 15 minut.'];
      else if (dur > 480) terr.endUtc = ['Rezerwacja może trwać maksymalnie 8 godzin.'];
      if (s.getMinutes() % 15 || s.getSeconds()) terr.startUtc = ['Początek musi być wyrównany do 15 minut.'];
      if (e.getMinutes() % 15 || e.getSeconds()) terr.endUtc = (terr.endUtc || []).concat(['Koniec musi być wyrównany do 15 minut.']);
      if (minutesOfDay(s) < DAY_START_MIN || !sameDay(s, e) || minutesOfDay(e) > DAY_END_MIN) {
        terr.startUtc = (terr.startUtc || []).concat(['Rezerwacja musi mieścić się w godzinach pracy 08:00–20:00 jednego dnia.']);
      }
      if (s < new Date()) terr.startUtc = (terr.startUtc || []).concat(['Nie można rezerwować w przeszłości.']);
      if (Object.keys(terr).length) return mockProblem(400, 'Błąd walidacji.', null, terr);

      var conflict = mockDb.bookings.filter(function (b) {
        return !b.cancelled && b.resourceId === res.id && new Date(b.startUtc) < e && new Date(b.endUtc) > s;
      })[0];
      if (conflict) {
        return mockProblem(409, 'Kolizja rezerwacji.', 'Termin koliduje z istniejącą rezerwacją.', null,
          { conflictingBookingId: conflict.id });
      }
      var nb = {
        id: mockDb.guid(), resourceId: res.id, bookedBy: name, title: (body.title || '').trim() || null,
        startUtc: s.toISOString(), endUtc: e.toISOString(), createdAt: new Date().toISOString(), cancelled: false
      };
      mockDb.bookings.push(nb);
      return { ok: true, status: 201, data: mockPublic(nb) };
    }

    if (method === 'DELETE' && (m = /^\/bookings\/([^/]+)$/.exec(path))) {
      var id = decodeURIComponent(m[1]);
      var bk = mockDb.bookings.filter(function (b) { return b.id === id && !b.cancelled; })[0];
      if (!bk) return mockProblem(404, 'Nie znaleziono.', 'Rezerwacja ' + id + ' nie istnieje lub została już anulowana.');
      if (new Date(bk.endUtc) <= new Date()) return mockProblem(409, 'Konflikt.', 'Rezerwacja już się zakończyła.');
      bk.cancelled = true;
      return { ok: true, status: 204, data: null };
    }

    return mockProblem(404, 'Nie znaleziono.', 'Brak endpointu ' + method + ' ' + path + ' w mocku.');
  }

  // ---------- Zdarzenia i start ----------

  el.resourceSelect.addEventListener('change', function () {
    state.resourceId = el.resourceSelect.value;
    writeStorage(RESOURCE_KEY, state.resourceId);
    state.bookings = [];
    loadWeek();
  });

  el.prevWeek.addEventListener('click', function () {
    state.weekStart = addDays(state.weekStart, -7);
    loadWeek();
  });
  el.nextWeek.addEventListener('click', function () {
    state.weekStart = addDays(state.weekStart, 7);
    loadWeek();
  });
  el.todayBtn.addEventListener('click', function () {
    state.weekStart = startOfWeek(new Date());
    loadWeek();
  });

  el.duration.addEventListener('change', updateDurationUi);
  el.customEnd.addEventListener('input', updateDurationUi);
  el.bookingForm.addEventListener('submit', submitBooking);
  el.bookingCancelBtn.addEventListener('click', function () { el.bookingDialog.close(); });

  el.detailsCloseBtn.addEventListener('click', function () { el.detailsDialog.close(); });
  el.detailsDeleteBtn.addEventListener('click', cancelSelectedBooking);

  // Klik w tło dialogu zamyka go.
  [el.bookingDialog, el.detailsDialog].forEach(function (dlg) {
    dlg.addEventListener('click', function (ev) { if (ev.target === dlg) dlg.close(); });
  });

  try {
    el.tzName.textContent = Intl.DateTimeFormat().resolvedOptions().timeZone || 'lokalna';
  } catch (e) {
    el.tzName.textContent = 'lokalna';
  }
  el.mockBadge.hidden = !MOCK;

  // Co minutę przerysuj siatkę, żeby wyszarzanie przeszłych slotów było aktualne.
  setInterval(function () {
    if (!el.bookingDialog.open && !el.detailsDialog.open) renderGrid();
  }, 60000);

  renderWeekLabel();
  renderGrid();
  loadResources();
})();
