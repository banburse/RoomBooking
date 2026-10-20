/*
 * RoomBooking GUI: vanilla JS, bez bibliotek.
 * Kontrakt API: CONTRACT.md. Wszystkie dane z API wstawiane wyłącznie przez textContent.
 */
(function () {
  'use strict';

  // ---------- Stałe ----------

  var DAY_START_MIN = 8 * 60;   // 08:00
  var DAY_END_MIN = 20 * 60;    // 20:00
  var SLOT_MIN = 30;
  var SLOT_COUNT = (DAY_END_MIN - DAY_START_MIN) / SLOT_MIN; // 24
  var DAYS = 5;                 // pn-pt
  var RESOURCE_KEY = 'roombooking.resourceId';
  var COLOR_CLASSES = 6;

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
    loadSeq: 0
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
    tzName: $('tz-name')
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

  // ---------- API ----------

  // Zwraca { ok, status, data, network } — nigdy nie rzuca.
  function request(method, url, body) {
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
    }
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
        btn.setAttribute('aria-label', 'Zarezerwuj: ' + fmtRange(slotStart, slotEnd));
        if (!hasResource || slotStart.getTime() < now.getTime()) {
          btn.disabled = true;
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
    return block;
  }

  // ---------- localStorage ----------

  function readStorage(key) {
    try { return window.localStorage.getItem(key); } catch (e) { return null; }
  }

  function writeStorage(key, value) {
    try { window.localStorage.setItem(key, value); } catch (e) { /* ignoruj */ }
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

  try {
    el.tzName.textContent = Intl.DateTimeFormat().resolvedOptions().timeZone || 'lokalna';
  } catch (e) {
    el.tzName.textContent = 'lokalna';
  }

  renderWeekLabel();
  renderGrid();
  loadResources();
})();
