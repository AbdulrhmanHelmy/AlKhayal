(function (global) {
  "use strict";
  var U = {};
  U.escape = function (value) { return String(value == null ? "" : value).replace(/[&<>"']/g, function (character) { return { "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;", "'": "&#39;" }[character]; }); };
  U.money = function (value) { return new Intl.NumberFormat("ar-EG", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Number(value || 0)) + " ج.م"; };
  U.number = function (value) { return new Intl.NumberFormat("ar-EG").format(Number(value || 0)); };
  U.date = function (value) { var date = value instanceof Date ? value : new Date(value); return isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("ar-EG", { year: "numeric", month: "2-digit", day: "2-digit" }).format(date); };
  U.dateTime = function (value) { var date = value instanceof Date ? value : new Date(value); return isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("ar-EG", { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" }).format(date); };
  U.normalize = function (value) { return String(value == null ? "" : value).toLowerCase().replace(/[أإآ]/g, "ا").replace(/ة/g, "ه").replace(/ى/g, "ي").replace(/[\u064B-\u065F\u0670]/g, "").trim(); };
  U.uid = function (prefix) { return (prefix || "id") + "-" + Date.now().toString(36) + "-" + Math.floor(Math.random() * 100000).toString(36); };
  U.debounce = function (callback, wait) { var timer; return function () { var args = arguments, context = this; clearTimeout(timer); timer = setTimeout(function () { callback.apply(context, args); }, wait); }; };
  U.clone = function (value) { return JSON.parse(JSON.stringify(value)); };
  U.downloadCsv = function (filename, columns, rows) { var quote = function (value) { return '"' + String(value == null ? "" : value).replace(/"/g, '""') + '"'; }; var csv = "\ufeff" + columns.map(function (column) { return quote(column.label); }).join(",") + "\r\n" + rows.map(function (row) { return columns.map(function (column) { return quote(row[column.key]); }).join(","); }).join("\r\n"); var url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" })); var anchor = document.createElement("a"); anchor.href = url; anchor.download = filename; document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(function () { URL.revokeObjectURL(url); }, 500); };
  global.AlKhayalUtils = U;
}(window));
