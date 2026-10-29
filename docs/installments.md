# Customer installments

The Installments section now stores installment contracts in SQLite. `InstallmentService` validates customer/product IDs, two-decimal amounts, interest, count (1–120), interval (1–12 months), and due date. The data adapter verifies the currently active catalog product and rounded catalog price before saving the contract snapshot.

Migration 032 creates immutable contracts and schedules and append-only payments. The financed balance is `(product price - down payment) * (1 + interest / 100)`, rounded to the nearest piastre, midpoint away from zero. Schedule rows split that amount into equal piastre installments, putting any remainder in the last row. The contract total includes the down payment plus financed schedule.

One SQLite transaction creates the contract, schedule, customer statement debit for the full contract amount, down-payment collection entry when nonzero, and customer balance projection update. Paying an installment atomically inserts its immutable payment, statement collection, and balance projection update. A given schedule row can be paid only once. Operation keys support safe retries; a key reused with a different payload is a conflict. Statement rows identify the installment sale, down payment, and each installment payment.

The frontend uses authenticated customer and active-product APIs. Sale requires `installment-system`; contract reads and settlement require `installment-payment`; mutations also require CSRF. Product price changes between selection and save are rejected and the operator must refresh.

This vertical slice records customer debt and installment servicing; it does not yet create a standard sales invoice, stock movement, or treasury deposit. Those operations require a further shared posting transaction and explicit payment-account selection. No historical customer balances or transactions are rewritten.

## Verification

`CustomerInstallmentTests` uses disposable SQLite data to verify migration-backed persistence, identical replay and changed-payload conflict, schedule dates and residuals, duplicate-payment prevention, atomic rollback, exact customer statement debit/credits, current balance, and SQLite foreign-key/integrity checks. API session, CSRF, and cashier-screen coverage is in the customer statement API suite.
