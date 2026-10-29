# Maintenance workflow

The `الصيانة` sidebar module contains persisted device intake and customer-filtered ticket tracking. Intake stores the selected customer, device/problem details, expected delivery, estimated cost, and an optional advance. Ticket status changes and delivery are recorded in SQLite.

At intake, an optional advance is recorded immediately as a customer collection and appears in the statement. At delivery, the operator enters final cost and selects نقدي or آجل. A single transaction stores the final charge, records the cash settlement when selected, updates the customer's balance, and marks the ticket delivered. An installment/credit delivery leaves the unpaid remainder due and visible in the customer statement.

`033_maintenance_tickets.sql` stores ticket data, append-only status history, and advance/cash receipt evidence. The API is under `/api/maintenance/tickets` and requires authentication, effective screen access, and CSRF validation on mutations. Operation keys make intake, status updates, and delivery safe to retry.

Cash paid for maintenance is recorded on the ticket and customer statement, but this workflow does not currently create a treasury/cashbox movement. It also does not change inventory or create a product sale.
