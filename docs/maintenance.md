# Maintenance workflow

The `الصيانة` sidebar module contains persisted device intake and customer-filtered ticket tracking. Intake stores the selected customer, device/problem details, expected delivery, estimated cost, and an optional advance. Ticket status changes and delivery are recorded in SQLite.

At intake, an optional advance is recorded as a ticket receipt for the uncharged service; it does not reduce existing customer debt or create a customer collection. At delivery, the operator enters final cost and selects نقدي or آجل. A single transaction stores the final charge, applies the saved advance once to that charge, records the remaining cash settlement when selected, updates the customer's balance, and marks the ticket delivered. An installment/credit delivery leaves the unpaid remainder due and visible in the customer statement.

`033_maintenance_tickets.sql` stores ticket data, append-only status history, and advance/cash receipt evidence. The API is under `/api/maintenance/tickets` and requires authentication, effective screen access, and CSRF validation on mutations. Operation keys make intake, status updates, and delivery safe to retry.

Cash paid for maintenance is recorded on the ticket and customer statement, but this workflow does not currently create a treasury/cashbox movement. It also does not change inventory or create a product sale.


Advances are applied in the customer statement on the delivery date, after the service charge; the original intake date and amount remain in the immutable ticket receipt. Existing tickets whose advances were already collected under the older implementation retain those entries unchanged: delivery validates and reuses them without a second deduction. A mismatched prior advance or receipt rejects delivery without writes. Existing `Customers.Balance >= 0` constraints remain unchanged; there is no artificial opening balance or negative debt projection. See [maintenance advance hotfix](maintenance-advance-hotfix.md) for the reproduced failure and validation evidence.
