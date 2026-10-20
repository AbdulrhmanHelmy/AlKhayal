"use strict";

var fs = require("fs");
var path = require("path");
var root = path.resolve(__dirname, "..");
var registry = JSON.parse(fs.readFileSync(path.join(root, "frontend", "data", "screens.json"), "utf8"));
var browserSmoke = new Set([
  "dashboard", "pos", "sales-management", "purchase-management", "customers-list",
  "suppliers-list", "low-stock", "financial-movement", "profit-sales-report",
  "maintenance-tracking", "settings-program"
]);
var focusedFiles = { dashboard: "frontend/js/modules/dashboard.js", pos: "frontend/js/modules/pos.js" };
var interactions = {
  "entity-list": "Search, filter, sort, paginate, inspect and session actions",
  "entity-form": "Validate and save to the session store",
  settings: "Edit settings and save in session",
  report: "Date/branch filters, summary, chart, CSV/print preview",
  dashboard: "KPIs, charts, alerts and navigation",
  pos: "Product search, cart, totals, keyboard shortcuts and mock checkout",
  "invoice-editor": "Line items, quantities, dates and calculated totals",
  workflow: "Review and simulated confirmation",
  statement: "Party selection and running-balance review",
  payment: "Validate and simulate a party payment",
  export: "Generate a UTF-8 CSV download",
  printer: "Configuration and print preview only",
  barcode: "Interactive label design and print preview",
  permissions: "Toggle a session-only permission matrix",
  installments: "Build and review a calculated schedule"
};

function escapeCell(value) {
  return String(value || "—").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
}

var lines = [
  "# UI screen coverage",
  "",
  "Generated from `frontend/data/screens.json` by `node tools/generate-ui-coverage.js` on 2026-10-20.",
  "",
  "- **Browser smoke**: the route was opened and its primary UI behavior was exercised in the local browser.",
  "- **Registry verified**: automated tests confirmed unique routing, navigation references, renderer support, and required metadata.",
  "- Business data/actions are session-only demonstrations unless the API documentation explicitly says otherwise.",
  "",
  "| Module | Arabic screen | Route | Implementation | Layout/renderer | Mock source | Main interactions | Verification |",
  "|---|---|---|---|---|---|---|---|"
];

registry.screens.forEach(function (screen) {
  lines.push("| " + [
    screen.module,
    screen.title,
    "`" + screen.route + "`",
    "`" + (focusedFiles[screen.renderer] || "frontend/js/modules/renderers.js") + "`",
    screen.renderer,
    screen.data || (screen.renderer === "dashboard" ? "Aggregated mock store" : "UI/session state"),
    interactions[screen.renderer] || "Interactive session workflow or preview",
    browserSmoke.has(screen.route) ? "Browser smoke" : "Registry verified"
  ].map(escapeCell).join(" | ") + " |");
});

lines.push("", "Total distinct screens: **" + registry.screens.length + "**.", "");
fs.writeFileSync(path.join(root, "docs", "ui-screen-coverage.md"), lines.join("\n"), "utf8");
