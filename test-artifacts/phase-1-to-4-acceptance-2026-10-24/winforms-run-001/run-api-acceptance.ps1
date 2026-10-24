$ErrorActionPreference = 'Stop'
$root = 'D:\projects\AlKhayal\test-artifacts\phase-1-to-4-acceptance-2026-10-24\winforms-run-001'
$base = 'http://127.0.0.1:5137/'
$run = [guid]::NewGuid().ToString('N').Substring(0, 8)
$env:ALKHAYAL_DATABASE_PATH = Join-Path $root 'database\alkhayal-ui-acceptance.db'
$env:ALKHAYAL_FRONTEND_PATH = 'D:\projects\AlKhayal\frontend'
$password = [guid]::NewGuid().ToString('N') + 'A!'
$username = "acceptance-$run"
$psi = [Diagnostics.ProcessStartInfo]::new('D:\projects\AlKhayal\src\AlKhayal.Server.Host\bin\Debug\net48\AlKhayal.Server.Host.exe', "--provision-user $username")
$psi.WorkingDirectory = 'D:\projects\AlKhayal\src\AlKhayal.Server.Host\bin\Debug\net48'
$psi.UseShellExecute = $false; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.CreateNoWindow = $true
$proc = [Diagnostics.Process]::Start($psi)
$proc.StandardInput.WriteLine($password); $proc.StandardInput.Close(); $proc.WaitForExit()
if ($proc.ExitCode -ne 0) { throw 'Isolated acceptance administrator provisioning failed.' }
$handler = [Net.Http.HttpClientHandler]::new(); $handler.CookieContainer = [Net.CookieContainer]::new()
$client = [Net.Http.HttpClient]::new($handler); $client.BaseAddress = [uri]$base
$script:csrf = ''
function Request($method, $path, $body, [switch]$mutation) {
  $req = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method), $path)
  if ($null -ne $body) { $req.Content = [Net.Http.StringContent]::new(($body | ConvertTo-Json -Depth 20 -Compress), [Text.Encoding]::UTF8, 'application/json') }
  if ($mutation -and $script:csrf) { [void]$req.Headers.TryAddWithoutValidation('X-CSRF-Token', $script:csrf) }
  $res = $script:client.SendAsync($req).GetAwaiter().GetResult(); $txt = $res.Content.ReadAsStringAsync().GetAwaiter().GetResult()
  if (-not $res.IsSuccessStatusCode) { throw "$method $path failed ($([int]$res.StatusCode)): $txt" }
  if ($txt) { return ($txt | ConvertFrom-Json).data }
  return $null
}
try {
  $script:client = $client
  $login = Request POST 'api/auth/login' @{ username=$username; password=$password }
  $script:csrf = $login.csrfToken
  $health = Request GET 'api/system/health' $null
  $warehouses = Request GET 'api/inventory/warehouses' $null
  $warehouseId = [long]$warehouses[0].id
  $units = Request GET 'api/catalog/units' $null
  $unitId = [long]$units[0].id
  $supplier = Request POST 'api/suppliers' @{ code="AT-$run"; name='مورد اختبار القبول'; phone='01000000000'; contact='مختبر القبول'; address='مصر'; status='active' } -mutation
  $product = Request POST 'api/catalog/products' @{ operationKey="acceptance-product-$run"; name="قهوة اختبار $run"; description='بيانات خيالية للاختبار'; baseUnitId=$unitId; productType='stock'; salePrice='150.0000'; purchasePrice='100.0000'; isActive=$true } -mutation
  $product = Request GET "api/catalog/products/$($product.id)" $null
  $variantId = [long]$product.variants[0].id
  $productUnitId = [long]$product.units[0].id
  $draft = Request POST 'api/purchases/drafts' @{ supplierId=$supplier.id; warehouseId=$warehouseId; invoiceNumber="AT-$run-001"; invoiceDate='2026-10-24'; paymentMode='cash'; notes='اختبار قبول معزول'; lines=@(@{ variantId=$variantId; productUnitId=$productUnitId; quantity='5.000000'; purchaseUnitPrice='100.0000'; saleUnitPrice='150.0000' }) } -mutation
  $posted = Request POST "api/purchases/$($draft.id)/post" @{ operationKey="acceptance-post-$run-001" } -mutation
  $invoice = Request GET "api/purchases/$($draft.id)" $null
  if ($invoice.status -ne 'posted') { throw 'Purchase invoice did not post.' }
  $supplierAfterCash = Request GET "api/suppliers/$($supplier.id)/statement" $null
  $returnable = Request GET "api/purchases/$($draft.id)/returnable-lines?warehouseId=$warehouseId" $null
  $lineId = [long]$invoice.lines[0].id
  $creditReturn = Request POST "api/purchases/$($draft.id)/returns" @{ warehouseId=$warehouseId; returnMode='supplier_credit'; operationKey="acceptance-return-$run-001"; lines=@(@{ purchaseInvoiceLineId=$lineId; quantity='1.000000' }) } -mutation
  $cashDraft = Request POST 'api/purchases/drafts' @{ supplierId=$supplier.id; warehouseId=$warehouseId; invoiceNumber="AT-$run-002"; invoiceDate='2026-10-24'; paymentMode='cash'; notes='اختبار دفع فوري'; lines=@(@{ variantId=$variantId; productUnitId=$productUnitId; quantity='2.000000'; purchaseUnitPrice='25.1250'; saleUnitPrice='150.0000' }) } -mutation
  $null = Request POST "api/purchases/$($cashDraft.id)/post" @{ operationKey="acceptance-post-$run-002" } -mutation
  $cashInvoice = Request GET "api/purchases/$($cashDraft.id)" $null
  if ($cashInvoice.status -ne 'posted') { throw 'Second cash purchase invoice did not post.' }
  $cashLineId = [long]$cashInvoice.lines[0].id
  $cashReturn = Request POST "api/purchases/$($cashDraft.id)/returns" @{ warehouseId=$warehouseId; returnMode='cash_refund'; operationKey="acceptance-cash-refund-$run"; lines=@(@{ purchaseInvoiceLineId=$cashLineId; quantity='1.000000' }) } -mutation
  $supplierLedger = Request GET "api/suppliers/$($supplier.id)/ledger" $null
  $supplierStatement = Request GET "api/suppliers/$($supplier.id)/statement" $null
  $balances = Request GET "api/inventory/balances?warehouseId=$warehouseId" $null
  $movements = Request GET "api/inventory/movements?warehouseId=$warehouseId&page=1&pageSize=100" $null
  $integrity = Request GET 'api/inventory/integrity' $null
  $report = [ordered]@{ health=$health.status; supplierId=$supplier.id; productId=$product.id; variantId=$variantId; warehouseId=$warehouseId; firstInvoiceId=$draft.id; firstInvoiceTotal=$invoice.totalFormatted; firstInvoicePaymentMode=$invoice.paymentMode; supplierCreditReturn=@{mode=$creditReturn.returnMode;total=$creditReturn.totalFormatted}; secondInvoiceId=$cashDraft.id; secondInvoiceTotal=$cashInvoice.totalFormatted; cashRefund=@{mode=$cashReturn.returnMode;total=$cashReturn.totalFormatted}; supplierLedgerCount=@($supplierLedger).Count; supplierStatement=$supplierStatement; inventoryBalances=$balances; inventoryMovementCount=@($movements.items).Count; inventoryIntegrity=$integrity.consistent; createdVia='API only; GUI not exercised' }
  $report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $root 'api-acceptance-results.json') -Encoding UTF8
  Write-Output 'API acceptance scenarios completed. Results saved without credentials.'
}
finally { $client.Dispose(); $handler.Dispose(); $password=$null }
