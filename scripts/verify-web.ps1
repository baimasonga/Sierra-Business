param([int]$Port = 5197)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root ('.tools\web-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$dll = Join-Path $root 'src\SierraBusiness.Web\bin\Release\net10.0\SierraBusiness.Web.dll'
$names = @('ConnectionStrings__Business', 'Bootstrap__Email', 'Bootstrap__Password', 'ASPNETCORE_URLS')
$original = @{}
foreach ($name in $names) { $original[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$server = $null
try {
    $env:ConnectionStrings__Business = 'Data Source=' + (Join-Path $directory 'check.db')
    $env:Bootstrap__Email = 'verify@example.test'
    $env:Bootstrap__Password = 'Verify-' + [Guid]::NewGuid().ToString('N') + '!9a'
    & dotnet $dll --migrate *> (Join-Path $directory 'migrate.log')
    if ($LASTEXITCODE -ne 0) { throw 'Temporary database migration failed.' }
    & dotnet $dll --create-owner *> (Join-Path $directory 'owner.log')
    if ($LASTEXITCODE -ne 0) { throw 'Temporary owner creation failed.' }
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
    $server = Start-Process dotnet -ArgumentList ('"' + $dll + '"') -WorkingDirectory (Join-Path $root 'src\SierraBusiness.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $directory 'server.log') -RedirectStandardError (Join-Path $directory 'error.log')
    $base = $env:ASPNETCORE_URLS
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        try { $health = Invoke-WebRequest "$base/health" -UseBasicParsing; $ready = $health.StatusCode -eq 200; if ($ready) { break } } catch { Start-Sleep -Milliseconds 500 }
    }
    if (!$ready) { throw 'Temporary web host did not become healthy.' }
    $login = Invoke-WebRequest "$base/account/login" -SessionVariable session -UseBasicParsing
    $token = [regex]::Match($login.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
    $result = Invoke-WebRequest "$base/account/login" -WebSession $session -Method Post -Body @{ email = $env:Bootstrap__Email; password = $env:Bootstrap__Password; __RequestVerificationToken = $token } -UseBasicParsing
    if (!$result.Content.Contains('SIERRA BUSINESS WORKSPACE')) { throw 'Authenticated workspace did not render.' }
    $token = Invoke-RestMethod "$base/api/v1/antiforgery" -WebSession $session
    $headers = @{ RequestVerificationToken = $token }
    $product = @{ operationId = [guid]::NewGuid(); sku = 'CHECK'; name = 'Check product'; category = 'General'; unit = 'piece'; price = 100; tracking = 0; minimumPrice = 50 } | ConvertTo-Json
    $id = Invoke-RestMethod "$base/api/v1/products" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $product
    $date = Get-Date -Format yyyy-MM-dd
    $receipt = @{ operationId = [guid]::NewGuid(); kind = 0; party = 'Test supplier'; businessDate = $date; lines = @(@{ productId = $id; quantity = 10; unitPrice = 60; conversion = 1 }) } | ConvertTo-Json -Depth 5
    $null = Invoke-RestMethod "$base/api/v1/documents" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $receipt
    $sale = @{ operationId = [guid]::NewGuid(); kind = 1; party = 'Walk-in'; businessDate = $date; payment = 0; lines = @(@{ productId = $id; quantity = 2; unitPrice = 100; conversion = 1 }) } | ConvertTo-Json -Depth 5
    $saleId = Invoke-RestMethod "$base/api/v1/documents" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $sale
    $replayed = Invoke-RestMethod "$base/api/v1/documents" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $sale
    if ($saleId -ne $replayed) { throw 'HTTP replay duplicated sale.' }
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    if ($state.netSales -ne 200 -or $state.stock[0].quantity -ne 8) { throw 'HTTP accounting/stock mismatch.' }
    $reversal = @{ operationId = [guid]::NewGuid(); documentId = $saleId; reason = 'Verification reversal' } | ConvertTo-Json
    $null = Invoke-RestMethod "$base/api/v1/reversals" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $reversal
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    if ($state.netSales -ne 0 -or $state.stock[0].quantity -ne 10) { throw 'HTTP reversal mismatch.' }
    $interactiveScript = Invoke-WebRequest "$base/_framework/blazor.web.js" -UseBasicParsing
    if ($interactiveScript.StatusCode -ne 200) { throw "Interactive Blazor script was not served." }
    $printScript = Invoke-WebRequest "$base/print.js" -UseBasicParsing
    if (!$printScript.Content.Contains('window.sierraPrint')) { throw 'Print helper was not served.' }
    foreach ($endpoint in @('orders', 'inventory/locations', 'payroll', 'parties', 'returns', 'periods', 'users')) {
        $null = Invoke-RestMethod "$base/api/v1/$endpoint" -WebSession $session
    }
    $voucher = @{ operationId = [guid]::NewGuid(); type = 0; date = $date; narration = 'Opening capital verification'; lines = @(@{ accountCode = 1010; debit = 500; credit = 0 }, @{ accountCode = 3000; debit = 0; credit = 500 }); allocations = @() } | ConvertTo-Json -Depth 5
    $voucherId = Invoke-RestMethod "$base/api/v1/finance/vouchers" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $voucher
    $finance = Invoke-RestMethod "$base/api/v1/finance?from=$date&to=$date" -WebSession $session
    if (($finance.balances | Where-Object code -eq 1010).closing -ne 500) { throw 'HTTP voucher balance mismatch.' }
    $bankLine = $finance.entries | Where-Object { $_.documentId -eq $voucherId -and $_.accountCode -eq 1010 }
    $match = @{ operationId = [guid]::NewGuid(); journalLineId = $bankLine.id; clearedDate = $date; statementReference = 'CHECK-001' } | ConvertTo-Json
    $null = Invoke-RestMethod "$base/api/v1/finance/reconciliation" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $match
    $finance = Invoke-RestMethod "$base/api/v1/finance?from=$date&to=$date" -WebSession $session
    if (($finance.bankBalances | Where-Object accountCode -eq 1010).cleared -ne 500) { throw 'HTTP reconciliation mismatch.' }
    $history = Invoke-RestMethod "$base/api/v1/finance/reconciliation/history" -WebSession $session
    $cancelMatch = @{ operationId = [guid]::NewGuid(); matchId = $history[0].id; reason = 'Correct statement reference' } | ConvertTo-Json
    $null = Invoke-RestMethod "$base/api/v1/finance/reconciliation/cancel" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $cancelMatch
    $finance = Invoke-RestMethod "$base/api/v1/finance?from=$date&to=$date" -WebSession $session
    if (($finance.bankBalances | Where-Object accountCode -eq 1010).cleared -ne 0) { throw 'HTTP match cancellation mismatch.' }
    $cashFlow = Invoke-RestMethod "$base/api/v1/finance/cash-flow?from=$date&to=$date" -WebSession $session
    if ($cashFlow.closing -ne 500 -or ($cashFlow.opening + $cashFlow.received - $cashFlow.paid) -ne $cashFlow.closing) { throw 'HTTP cash flow mismatch.' }
    $location = @{ operationId = [guid]::NewGuid(); code = 'STORE2'; name = 'Second store' } | ConvertTo-Json
    $null = Invoke-RestMethod "$base/api/v1/inventory/locations" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $location
    $transfer = @{ operationId = [guid]::NewGuid(); productId = $id; from = 'MAIN'; to = 'STORE2'; quantity = 3; reason = 'HTTP transfer check' } | ConvertTo-Json
    $null = Invoke-RestMethod "$base/api/v1/inventory/transfers" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $transfer
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    if (($state.stock | Where-Object location -eq 'MAIN').quantity -ne 7 -or ($state.stock | Where-Object location -eq 'STORE2').quantity -ne 3) { throw 'HTTP warehouse transfer mismatch.' }
    $newUser = @{ email = 'readonly@example.test'; password = 'Check-' + [guid]::NewGuid().ToString('N') + '!9a'; permissions = @('business.read') } | ConvertTo-Json
    $userId = Invoke-RestMethod "$base/api/v1/users" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $newUser
    $users = Invoke-RestMethod "$base/api/v1/users" -WebSession $session
    if (!($users | Where-Object id -eq $userId)) { throw 'HTTP user creation mismatch.' }
    function Post-Check($endpoint, $payload) {
        Invoke-RestMethod "$base/api/v1/$endpoint" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body ($payload | ConvertTo-Json -Depth 8)
    }
    $orderId = Post-Check 'orders' @{ operationId = [guid]::NewGuid(); type = 1; party = 'School'; date = $date; dueDate = $date; reference = 'HTTP delivery'; lines = @(@{ productId = $id; quantity = 2; unitPrice = 100 }) }
    $null = Post-Check 'commerce/reservations' @{ operationId = [guid]::NewGuid(); orderId = $orderId; productId = $id; location = 'MAIN'; quantity = 2 }
    $deliveryPayload = @{ operationId = [guid]::NewGuid(); kind = 12; party = 'School'; businessDate = $date; orderId = $orderId; location = 'MAIN'; lines = @(@{ productId = $id; quantity = 2; unitPrice = 100; conversion = 1 }) }
    $deliveryId = Post-Check 'documents' $deliveryPayload
    $deliveryPayload.operationId = [guid]::NewGuid(); $deliveryPayload.kind = 1; $deliveryPayload.payment = 0; $deliveryPayload.deliveryId = $deliveryId
    $invoiceId = Post-Check 'documents' $deliveryPayload
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    if (($state.stock | Where-Object location -eq 'MAIN').quantity -ne 5) { throw 'Delivery/invoice issued stock more than once.' }
    $null = Post-Check 'returns' @{ operationId = [guid]::NewGuid(); originalDocumentId = $invoiceId; reason = 'One returned'; disposition = 3; lines = @(@{ productId = $id; quantity = 1 }) }
    $credits = Invoke-RestMethod "$base/api/v1/commerce/credits" -WebSession $session
    if ($credits[0].remaining -ne 100) { throw 'HTTP return credit mismatch.' }
    $null = Post-Check 'commerce/credits/use' @{ operationId = [guid]::NewGuid(); creditId = $credits[0].id; amount = 40; refundAccount = 1000; reason = 'Partial refund' }
    $credits = Invoke-RestMethod "$base/api/v1/commerce/credits" -WebSession $session
    if ($credits[0].remaining -ne 60) { throw 'HTTP partial refund mismatch.' }
    $pricePolicies = Invoke-RestMethod "$base/api/v1/commerce/prices" -WebSession $session
    if ($pricePolicies[0].minimumPrice -ne 50) { throw 'Owner price policy read failed.' }
    $statementId = Post-Check 'banking/statements' @{ operationId = [guid]::NewGuid(); accountCode = 1010; reference = 'HTTP-STATEMENT'; openingBalance = 0; closingBalance = 500; csv = "Date,Reference,Amount`n$date,CHECK-001,500" }
    $banking = Invoke-RestMethod "$base/api/v1/banking" -WebSession $session
    $row = $banking.rows | Where-Object statementId -eq $statementId
    if (!$row.candidates) { throw 'Statement match suggestion missing.' }
    $null = Post-Check 'finance/reconciliation' @{ operationId = [guid]::NewGuid(); journalLineId = $bankLine.id; clearedDate = $date; statementReference = 'CHECK-001'; statementRowId = $row.id }
    $shiftId = Post-Check 'banking/tills/open' @{ operationId = [guid]::NewGuid(); accountCode = 1000; openingCount = 50 }
    $chequeSale = Post-Check 'documents' @{ operationId = [guid]::NewGuid(); kind = 1; party = 'Cheque customer'; businessDate = $date; payment = 1; paymentReference = 'HTTP-CHEQUE'; bank = 'Test bank'; paymentDate = $date; lines = @(@{ productId = $id; quantity = 1; unitPrice = 100; conversion = 1 }) }
    $banking = Invoke-RestMethod "$base/api/v1/banking" -WebSession $session
    $cheque = $banking.cheques | Where-Object reference -eq 'HTTP-CHEQUE'
    if (!$cheque) { throw 'Cheque not automatically registered.' }
    $null = Post-Check 'banking/cheques/action' @{ operationId = [guid]::NewGuid(); chequeId = $cheque.id; expectedVersion = 0; action = 0; date = $date; bankAccount = 1010; reason = 'Deposit confirmed' }
    $null = Post-Check 'banking/cheques/action' @{ operationId = [guid]::NewGuid(); chequeId = $cheque.id; expectedVersion = 1; action = 1; date = $date; bankAccount = 1010; reason = 'Clearance confirmed' }
    $null = Post-Check 'banking/tills/close' @{ operationId = [guid]::NewGuid(); shiftId = $shiftId; expectedMovement = 0; counted = 48; note = 'Two leones short' }
    $tills = Invoke-RestMethod "$base/api/v1/banking/tills" -WebSession $session
    if ($tills[0].variance -ne -2) { throw 'Till variance mismatch.' }
    $null = Post-Check 'pos/barcodes' @{ operationId = [guid]::NewGuid(); productId = $id; code = '0000123456'; conversion = 2 }
    $barcodes = Invoke-RestMethod "$base/api/v1/pos/barcodes" -WebSession $session
    if ($barcodes[0].code -ne '0000123456' -or $barcodes[0].conversion -ne 2) { throw 'Barcode pack mapping mismatch.' }
    $heldDraft = @{ operationId = [guid]::NewGuid(); kind = 1; party = 'Held customer'; businessDate = $date; payment = 0; location = 'MAIN'; lines = @(@{ productId = $id; quantity = 1; unitPrice = 180; conversion = 2 }) }
    $heldId = Post-Check 'pos/held' @{ operationId = [guid]::NewGuid(); label = 'Bargaining customer'; draft = $heldDraft }
    $held = Invoke-RestMethod "$base/api/v1/pos/held" -WebSession $session
    if ($held[0].id -ne $heldId) { throw 'Held sale missing.' }
    $heldDraft.heldSaleId = $heldId
    $heldInvoice = Post-Check 'documents' $heldDraft
    $held = Invoke-RestMethod "$base/api/v1/pos/held" -WebSession $session
    if ($held.Count -ne 0) { throw 'Posted hold still active.' }
    $heldDraft.operationId = [guid]::NewGuid()
    $duplicateStatus = 0
    try { $null = Post-Check 'documents' $heldDraft } catch { $duplicateStatus = [int]$_.Exception.Response.StatusCode }
    if ($duplicateStatus -ne 422) { throw "Expected held-sale validation rejection (422), got $duplicateStatus." }
    $readOnlyLogin = Invoke-WebRequest "$base/account/login" -SessionVariable readOnlySession -UseBasicParsing
    $readOnlyToken = [regex]::Match($readOnlyLogin.Content, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
    $readOnlyUser = $newUser | ConvertFrom-Json
    $null = Invoke-WebRequest "$base/account/login" -WebSession $readOnlySession -Method Post -Body @{ email = $readOnlyUser.email; password = $readOnlyUser.password; __RequestVerificationToken = $readOnlyToken } -UseBasicParsing
    $denied = 0
    try { $null = Invoke-WebRequest "$base/api/v1/commerce/prices" -WebSession $readOnlySession -UseBasicParsing } catch { $denied = [int]$_.Exception.Response.StatusCode }
    if ($denied -ne 403) { throw 'Confidential minimum prices were not denied to a read-only user.' }
    $batchProduct = @{ operationId = [guid]::NewGuid(); sku = 'COUNT-BATCH'; name = 'Counted batch'; category = 'General'; unit = 'piece'; price = 100; tracking = 3; minimumPrice = 50 } | ConvertTo-Json
    $batchId = Invoke-RestMethod "$base/api/v1/products" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $batchProduct
    $expiry = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    $count = @{ operationId = [guid]::NewGuid(); productId = $batchId; location = 'MAIN'; expectedQuantity = 0; countedQuantity = 5; surplusUnitCost = 60; reason = 'Physical stock discovered'; batch = 'B-1'; expiry = $expiry } | ConvertTo-Json
    $countId = Invoke-RestMethod "$base/api/v1/inventory/counts" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $count
    $countReplay = Invoke-RestMethod "$base/api/v1/inventory/counts" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $count
    if ($countId -ne $countReplay) { throw 'Batch count replay duplicated adjustment.' }
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    $lot = $state.stock | Where-Object { $_.productId -eq $batchId -and $_.batch -eq 'B-1' -and $_.expiry -eq $expiry }
    if ($lot.quantity -ne 5 -or $lot.value -ne 300) { throw 'Batch count identity or value mismatch.' }
    $suggestRequest = @{ productId = $batchId; location = 'MAIN'; date = $date; quantity = 2; draftAllocations = @(@{ batch = 'B-1'; expiry = $expiry; quantity = 1 }) } | ConvertTo-Json -Depth 5
    $suggestions = @(Invoke-RestMethod "$base/api/v1/inventory/batch-suggestions" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $suggestRequest)
    if ($suggestions.Count -ne 1 -or $suggestions[0].quantity -ne 2 -or $suggestions[0].batch -ne 'B-1') { throw 'FEFO batch suggestion mismatch.' }
    $suggestedSale = @{ operationId = [guid]::NewGuid(); kind = 1; party = 'Batch buyer'; businessDate = $date; payment = 0; lines = @(@{ productId = $batchId; quantity = $suggestions[0].quantity; unitPrice = 100; batch = $suggestions[0].batch; expiry = $suggestions[0].expiry; conversion = 1 }) } | ConvertTo-Json -Depth 5
    $null = Invoke-RestMethod "$base/api/v1/documents" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $suggestedSale
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    if (($state.stock | Where-Object productId -eq $batchId).quantity -ne 3) { throw 'Suggested-batch sale stock mismatch.' }
    $serialProduct = @{ operationId = [guid]::NewGuid(); sku = 'COUNT-SERIAL'; name = 'Counted serial'; category = 'General'; unit = 'piece'; price = 100; tracking = 1; minimumPrice = 50 } | ConvertTo-Json
    $serialId = Invoke-RestMethod "$base/api/v1/products" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $serialProduct
    $serialCount = @{ operationId = [guid]::NewGuid(); productId = $serialId; location = 'MAIN'; expectedQuantity = 0; countedQuantity = 1; surplusUnitCost = 60; reason = 'Unrecorded serial'; serial = 'CHECK-SN' } | ConvertTo-Json
    $serialDocument = Invoke-RestMethod "$base/api/v1/inventory/counts" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $serialCount
    $serialReplay = Invoke-RestMethod "$base/api/v1/inventory/counts" -WebSession $session -Headers $headers -Method Post -ContentType application/json -Body $serialCount
    if ($serialDocument -ne $serialReplay) { throw 'Serial count replay failed.' }
    $state = Invoke-RestMethod "$base/api/v1/workspace" -WebSession $session
    $serialStock = $state.stock | Where-Object productId -eq $serialId
    if ($serialStock.quantity -ne 1 -or $serialStock.serial -ne 'CHECK-SN' -or $serialStock.value -ne 60) { throw 'Serial count stock mismatch.' }
    $status = 0
    $session.Headers.Remove('RequestVerificationToken')
    try { $null = Invoke-WebRequest "$base/api/v1/products" -WebSession $session -Method Post -ContentType application/json -Body $product -UseBasicParsing } catch { $status = [int]$_.Exception.Response.StatusCode }
    if ($status -ne 400) { throw "Missing antiforgery token returned $status rather than 400." }
    Write-Output 'PASS: HTTP health, sign-in, workspace, catalogue, receipt, sale, replay, reversal, module reads, voucher, bank match/cancellation, cash flow, warehouse transfer, user creation, reservations, delivery/invoice stock-once posting, paid return credit, partial refund, confidential-price denial, statement import/matching, cheque deposit/clearance, till closing, barcode packs, held-sale recall/post-once, batch count/replay, FEFO suggestion/sale, serial count/replay, print asset and antiforgery rejection.'
} finally {
    if ($server -and !$server.HasExited) { Stop-Process -Id $server.Id }
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $original[$name], 'Process') }
}









