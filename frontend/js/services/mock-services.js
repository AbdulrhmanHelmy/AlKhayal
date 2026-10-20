(function (global) {
  "use strict";
  var U = global.AlKhayalUtils, C = global.AlKhayalCalculations, seed = global.AlKhayalMockSeed, state = U.clone(seed), listeners = [];
  function notify(entity) { listeners.forEach(function (listener) { listener(entity); }); }
  function collection(entity) { if (!Object.prototype.hasOwnProperty.call(state, entity) || !Array.isArray(state[entity])) state[entity] = []; return state[entity]; }
  var store = {
    all: function (entity) { return collection(entity).slice(); },
    find: function (entity, id) { return collection(entity).find(function (item) { return String(item.id) === String(id); }) || null; },
    add: function (entity, record) { var rows = collection(entity), next = rows.reduce(function (max, item) { return Math.max(max, Number(item.id) || 0); }, 0) + 1, saved = Object.assign({ id: next }, record); rows.unshift(saved); notify(entity); return saved; },
    update: function (entity, id, changes) { var row = this.find(entity, id); if (!row) return null; Object.assign(row, changes); notify(entity); return row; },
    remove: function (entity, id) { var rows = collection(entity), index = rows.findIndex(function (item) { return String(item.id) === String(id); }); if (index < 0) return false; rows.splice(index, 1); notify(entity); return true; },
    reset: function () { state = U.clone(seed); notify("*"); },
    subscribe: function (listener) { listeners.push(listener); return function () { listeners = listeners.filter(function (item) { return item !== listener; }); }; }
  };
  function todaySales() { return store.all("sales").filter(function (sale) { return sale.date.slice(0, 10) === "2026-10-20"; }); }
  var services = {
    store: store,
    dashboard: function () { var sales = todaySales(), revenue = sales.reduce(function (sum, sale) { return sum + sale.total; }, 0), cost = sales.reduce(function (sum, sale) { return sum + sale.cost; }, 0), expenses = store.all("expenses").filter(function (expense) { return expense.date.slice(0, 10) === "2026-10-20"; }).reduce(function (sum, expense) { return sum + expense.amount; }, 0); return { revenue: C.round(revenue), invoiceCount: sales.length, expenses: C.round(expenses), profit: C.round(revenue - cost - expenses), latestSales: sales.slice().reverse().slice(0, 8), lowStock: store.all("products").filter(function (product) { return product.stock <= product.minimum; }), week: store.all("salesWeek"), activities: store.all("activities") }; },
    query: function (entity, options) { options = options || {}; var rows = store.all(entity), keys = options.keys || (rows[0] ? Object.keys(rows[0]) : []); rows = C.filter(rows, options.query || "", keys, U.normalize); if (options.filterKey && options.filterValue) rows = rows.filter(function (row) { return String(row[options.filterKey]) === String(options.filterValue); }); if (options.sortKey) rows = C.sort(rows, options.sortKey, options.sortDirection); return rows; },
    invoiceTotals: C.invoice,
    installmentSchedule: C.installments,
    createMock: function (entity, record) { return store.add(entity, record); }, updateMock: function (entity, id, record) { return store.update(entity, id, record); }, deleteMock: function (entity, id) { return store.remove(entity, id); }
  };
  global.AlKhayalServices = services;
}(window));
